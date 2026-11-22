# ADR 0002: Dedicated API documentation page at `/api`

- **Status:** Accepted
- **Date:** 2026-11-22
- **Decision area:** Public API documentation and discoverability

## Context

The homepage carried a short API block with two `curl` examples. That was not
enough to explain the product's most important API nuance:

- `ip.privacylink.nl` returns the address family of **that specific
  connection** (IPv4 or IPv6).
- `v4.ip.privacylink.nl` and `v6.ip.privacylink.nl` select a family through
  DNS, not through the URL path.
- A path such as `/api/ipv4` cannot force a family.
- `v4.*` / `v6.*` only work when that family is actually reachable.

The example also failed on Windows PowerShell 5.1, where `curl` is an alias for
`Invoke-WebRequest` and does not accept Unix flags such as `-s`.

The API itself is public, keyless and cookie-free. The missing piece was
documentation, not another endpoint.

## Options considered

1. **Keep extending the homepage block.**
   Rejected: it would compete with the primary IP-display task and still be too
   long for the homepage hierarchy.
2. **Link to Markdown on GitHub.**
   Rejected as the primary experience: it leaves the site, is not styled for
   copy/paste, and does not help non-technical visitors understand the family
   behavior.
3. **A dedicated, server-rendered `/api` page with copyable, environment-specific
   examples.**
   Chosen.
4. **Add a live "Try it" request button immediately.**
   Deferred: it adds client-side state, error handling and tests without
   improving the core explanation. Static response examples are enough for the
   first iteration.

## Decision

- Serve a new server-rendered page at `GET`/`HEAD` `/api` (and `/api/`).
- Keep the existing homepage header/footer and visual language. The homepage
  gets a short teaser and a link to the full documentation.
- Explain the hostname/family matrix **before** the command examples:
  - main host: family of this connection;
  - `v4.*`: IPv4 only via DNS;
  - `v6.*`: IPv6 only via DNS;
  - a URL path cannot select a family;
  - VPN, NAT, proxy and IPv6 privacy extensions can change the observed
    address.
- Provide five tabs: `cURL`, `PowerShell`, `JavaScript`, `Python`, `wget`.
  Every tab shows plain text, JSON, forced IPv4 and forced IPv6 examples.
- Make the Windows problem explicit: PowerShell 5.1 users must use `curl.exe`
  or `Invoke-RestMethod`; the page shows both.
- Keep CORS unchanged for now. The JavaScript tab documents that browser
  requests are allowed only from the canonical origin (`https://ip.privacylink.nl`)
  and that Node/server-side requests have no CORS restriction. Opening the API
  to arbitrary browser origins remains a separate decision.
- Do not use `localStorage`, `sessionStorage` or cookies for tab preferences.
  Deep links such as `/api#powershell` select a tab without storing anything.
- Inject the three hostnames from configuration at render time
  (`{{MAIN_HOST}}`, `{{V4_HOST}}`, `{{V6_HOST}}`) instead of hardcoding them in
  the template.
- Never serve the raw `Web/api.html` template with unreplaced placeholders.
  `/api.html` is rewritten to `/api` before static-file handling.
- Cache the page with `public, no-cache` and a weak `ETag`: it contains no
  visitor IP or other personal data, revalidation makes documentation changes
  visible immediately, and unchanged responses cost only a `304`.

## Consequences

- The homepage stays focused on the two IP addresses; the API explanation has
  room to be correct and complete.
- The documented examples are copyable and work on Linux, macOS and Windows.
- The page must stay in sync with the API contract and configuration-driven
  hostnames.
- Tab behavior must remain accessible (ARIA tabs, keyboard support) and must
  degrade gracefully without JavaScript: all panels stay readable.
- A live "Try it" control, OpenAPI output and a language switch remain possible
  follow-ups.
- CORS broadening needs a new decision; the current page must not imply that
  arbitrary browser origins can read the API.

## References

- `spec/spec.md` §5 (HTTP API contract) and §5.5 (cross-origin access).
- `README.md` (API section and local verification commands).
