# Architecture

## Overview

PrivacyLink IP is one small ASP.NET Core process behind the existing Caddy edge
proxy. It has no database, no external API calls and no shared code or
deployment coupling with the main PrivacyLink application.

```text
Browser / curl
      |
      | IPv4 or IPv6, port 80/443
      v
Caddy (public edge, TLS, HTTP/2/3)
      |
      | internal Docker network, trusted X-Forwarded-For / X-Forwarded-Proto
      v
privacylink-ip:8080 (ASP.NET Core, non-root, no public port)
      |
      +-- server-rendered HTML (initial IP)
      +-- /api           documentation page
      +-- /api/ip        text/plain
      +-- /api/ip.json   JSON
      +-- /healthz
```

The three public hostnames share one Caddy instance and one app container. DNS,
not an application path, selects the address family:

| Hostname | DNS | Role |
| --- | --- | --- |
| `ip.privacylink.nl` | A + AAAA | Homepage and dual-stack API |
| `v4.ip.privacylink.nl` | A only | IPv4-only detection |
| `v6.ip.privacylink.nl` | AAAA only | IPv6-only detection |

## Request pipeline

The pipeline order is security-relevant:

1. **Forwarded headers.** `UseForwardedHeaders` runs early and processes
   `X-Forwarded-For`/`X-Forwarded-Proto` only from explicitly configured proxy
   IPs or CIDRs. No blind first-header fallback exists.
2. **Exception handler.** Unhandled exceptions become a neutral `500` response
   without stack traces, request IPs or internal details.
3. **Security headers.** `X-Content-Type-Options: nosniff`, `Referrer-Policy:
   no-referrer`, `Permissions-Policy`, CSP and HSTS (HTTPS only).
4. **HTTPS redirect.** Outside Development, HTTP requests for allowed hostnames
   receive a `308` to HTTPS. Unknown hosts are never used as redirect targets.
5. **Canonical redirects.** `/index.html` redirects permanently to `/`,
   `/api.html` and `/api/` redirect permanently to `/api`, and the homepage and
   API documentation on the `v4.*`/`v6.*` hostnames redirect to the main
   hostname. This keeps one indexable URL per page. Assets under `/assets/` are
   referenced with a content-hash query (`?v=...`). Only the current hash is
   cached as `public, max-age=31536000, immutable`; unversioned and stale URLs
   use `public, no-cache` and revalidate via `ETag`. Other static files also use
   `public, no-cache`.
6. **Endpoints.** `/`, `/api`, `/api/ip`, `/api/ip.json`, `/healthz`,
   `/robots.txt`, `/sitemap.xml`.

## IP resolution and fail-closed behavior

`ClientIpResolver` reads `HttpContext.Connection.RemoteIpAddress` after the
forwarded-header middleware. It normalizes IPv4-mapped IPv6 addresses
(`::ffff:192.0.2.1` → `192.0.2.1`) and formats addresses with standard .NET
formatting (compact IPv6, no port, no brackets).

The resolver rejects loopback, private (RFC 1918), link-local, CGNAT, unique
local, multicast and unspecified addresses. This is intentional: if the
forwarded-header configuration is missing, ASP.NET Core ignores headers from
unconfigured proxies and leaves the direct peer address visible — normally the
internal Caddy/container IP. Returning that address would be a correctness and
privacy bug. Instead the service logs a non-identifying reason and returns a
neutral `503`.

This means local development on plain `localhost` fails closed too. See the
README for a safe local simulation using a trusted loopback proxy.

## Host allowlist and family gating

`HostAllowlist` compares the normalized `Host` header exactly against the
configured main, v4 and v6 hostnames. No suffix match is used, so
`v4.ip.privacylink.nl.evil.example` is rejected. Unknown hosts receive `400`.

On `v4.*` only an IPv4 observation is valid, and on `v6.*` only IPv6. Any
mismatch returns `503 ip_family_mismatch` and logs a non-identifying event. The
API never returns a `200` with the wrong family.

## Server-side rendering

The homepage is a static template read from `Web/index.html` at request time.
`SiteRenderer.RenderHtml(template, IpState)` performs exactly three replacements:

| Placeholder | Value |
| --- | --- |
| `{{MAIN_HOST}}` | HTML-encoded main hostname, used in canonical and Open Graph URLs |
| `{{V4_HOST}}` | HTML-encoded IPv4-only hostname |
| `{{V6_HOST}}` | HTML-encoded IPv6-only hostname |
| `{{INITIAL_IPV4}}` | HTML-encoded IPv4, or empty when the initial family is IPv6 |
| `{{INITIAL_IPV6}}` | HTML-encoded IPv6, or empty when the initial family is IPv4 |
| `{{SERVER_STATE}}` | JSON `{"initialIp":"...","initialVersion":4|6,"otherVersion":6|4}` |
| `{{ASSET_CSS_URL}}` | Content-hashed URL of `/assets/site.css` |
| `{{ASSET_JS_URL}}` | Content-hashed URL of `/assets/site.js` |

The asset URLs come from `AssetManifest`, which hashes each file once at
startup. Templates must always use the placeholders instead of hardcoding
`/assets/...`, otherwise a deploy can leave clients on stale CSS or JS.

IP values are encoded with `HtmlEncoder.Default`. The JSON block is serialized
with `System.Text.Json`; the default encoder escapes HTML-sensitive characters,
so the model cannot break out of a `<script type="application/json">` block.
The renderer is a standalone class so unit tests use inline templates and do not
depend on frontend files.

The API documentation page is also server-rendered from `Web/api.html`.
`SiteRenderer.RenderDocs(template, mainHost, v4Host, v6Host, assets)` replaces
the `{{MAIN_HOST}}`, `{{V4_HOST}}`, `{{V6_HOST}}`, `{{ASSET_CSS_URL}}` and
`{{ASSET_JS_URL}}` placeholders, again with HTML-encoded values. The page
contains no visitor-specific data and does not run IP resolution or family
gating. It is served on the main hostname; requests for the documentation on
`v4.*` or `v6.*` redirect to the canonical main URL. The global Host allowlist
still rejects unknown hosts.

## Caching and cross-origin behavior

- Every IP-bearing response (homepage, `/api/ip`, `/api/ip.json`) has
  `Cache-Control: no-store`, no `ETag` and no `Last-Modified`.
- The `/api` documentation page uses `Cache-Control: public, no-cache` and a
  weak `ETag`. Browsers revalidate on every navigation, so a deploy is visible
  immediately; a matching `If-None-Match` returns `304 Not Modified`.
- Assets under `/assets/` carry a SHA-256 content hash in the query string.
  The current URL is cached for one year as `immutable`; unversioned or stale
  URLs use `public, no-cache` so they revalidate instead of serving old bytes.
  A query parameter is used instead of a filename segment to avoid a build-time
  rename step; the only cache in front of the origin is the browser, since
  Caddy is configured as a pure reverse proxy.
- Changing `site.css` or `site.js` changes its URL on the next start. Because
  both HTML pages are revalidated, clients pick up the new URLs directly.
- `v4.*` and `v6.*` API responses include
  `Access-Control-Allow-Origin: https://ip.privacylink.nl` exactly. No wildcard
  and no credentials header. CORS is not authentication; CLI access remains open.

## Search engine behavior

- `/` and `/api` are the only indexable pages. Both carry a self-referencing
  canonical URL, `index, follow` robots metadata and JSON-LD.
- `/index.html`, `/api.html`, `/api/` and the homepage/API documentation on
  `v4.*`/`v6.*` redirect permanently to the canonical URL.
- `/robots.txt` allows crawling on the main hostname and disallows the API-only
  hostnames. It points to `/sitemap.xml`, which lists only main-host URLs.
- `/api/ip`, `/api/ip.json`, `/healthz` and all error responses send
  `X-Robots-Tag: noindex, nofollow`. Raw address data must not appear in search
  results.
- IP-bearing responses keep `Cache-Control: no-store`. `no-store` is a caching
  directive, not an indexing directive; Google can still index `/` while it
  revalidates the address on every crawl.
- The visible copy targets the primary intent without changing the tool's
  behavior. See `docs/seo.md` for the keyword, content and Search Console plan.

## Security headers

- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: no-referrer`
- `Permissions-Policy` disables camera, geolocation, microphone and similar
  capabilities.
- CSP allows only same-origin scripts/styles and `connect-src` to the canonical
  origin plus the two family hostnames. No inline scripts and no third parties.
- HSTS is per hostname with `max-age=31536000`, without `includeSubDomains` or
  `preload`.

## Failure handling

| Condition | Behavior |
| --- | --- |
| Missing/private/untrusted client address | `503 ip_unavailable`, warning without IP |
| Family mismatch | `503 ip_family_mismatch`, warning without IP |
| Unknown Host | `400 invalid_host` |
| Missing homepage template | `404 homepage_not_available` |
| Unexpected exception | `500 internal_error` |

Operational logs intentionally exclude visitor IPs. Aggregate status/latency
metrics are acceptable; individual request logs with full client IPs are not.
