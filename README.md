# PrivacyLink IP

A fast, ad-free, privacy-first public-IP display service. It shows the IPv4
and/or IPv6 address that the server observes for the visitor's connection.

- **Server-rendered first IP:** the initial address is present in the HTML
  response, before JavaScript runs.
- **Second family on demand:** the browser fetches only the missing family from
  the dedicated `v4.*` or `v6.*` hostname.
- **No tracking:** no cookies, no analytics, no third-party browser requests, no
  database, and no permanent visitor-IP storage.
- **Small and open:** ASP.NET Core Minimal API with a small vanilla frontend.

This repository contains the backend, tests, deployment examples and
documentation. The homepage stylesheet/script and HTML template live in
`src/PrivacyLink.Ip/Web/` and are maintained separately; when they are absent
the API still builds and runs, and `/` returns a neutral `404`.

## Stack

| Component | Choice |
| --- | --- |
| Backend | ASP.NET Core / .NET 10 LTS (`net10.0`) |
| App model | Minimal API + tiny server-side template renderer |
| Frontend | Semantic HTML, vanilla CSS/JavaScript (separate task) |
| Reverse proxy | Existing Caddy instance (see `deploy/`) |
| Database | None |
| Third-party APIs | None in MVP |

`global.json` pins SDK `10.0.100` with `"rollForward": "latestMajor"`. This
allows a newer installed SDK (for example `11.0.100`) to build the `net10.0`
target while keeping CI reproducible.

## Quick start

```bash
dotnet restore
dotnet run --project src/PrivacyLink.Ip
```

The app listens on `http://localhost:5000` by default.

### Local development and fail-closed behavior

The backend deliberately refuses to present loopback, private, link-local,
CGNAT or unspecified addresses as a visitor's public IP. On plain `localhost`
the connection address is `127.0.0.1`, so API calls return `503` until a trusted
proxy is configured.

To simulate a public client locally, trust the local loopback proxy and send a
documentation address:

```bash
ASPNETCORE_ENVIRONMENT=Development \
ForwardedHeaders__KnownProxies__0=127.0.0.1 \
dotnet run --project src/PrivacyLink.Ip
```

```bash
curl -sS http://localhost:5000/api/ip \
  -H 'Host: ip.privacylink.nl' \
  -H 'X-Forwarded-For: 203.0.113.42'
```

`203.0.113.42` is an RFC 5737 documentation address, not a real client.

## API

All endpoints support `GET` and `HEAD`; other methods return `405`.
Responses that contain or derive from the visitor IP use `Cache-Control:
no-store` and do not emit `ETag`/`Last-Modified`.

### `GET /api/ip`

`text/plain; charset=utf-8`, exactly one IP string plus a single trailing `\n`.

```http
HTTP/1.1 200 OK
Content-Type: text/plain; charset=utf-8
Cache-Control: no-store

203.0.113.42
```

- `ip.privacylink.nl` (main): the address family of this request.
- `v4.ip.privacylink.nl`: IPv4 only; an IPv6 observation returns `503`.
- `v6.ip.privacylink.nl`: IPv6 only; an IPv4 observation returns `503`.

### `GET /api/ip.json`

`application/json`, exact and additive-only schema:

```json
{"ip":"203.0.113.42","version":4}
```

`version` is `4` or `6`.

### `GET /healthz`

`200 OK` when the process can serve requests. It has no external dependencies
and returns no identifying data.

### Errors

| Situation | Status | Body |
| --- | --- | --- |
| Family mismatch on `v4.*`/`v6.*` | `503` | `ip_family_mismatch` (plain) or `{"error":"ip_family_mismatch"}` (JSON) |
| Untrusted/private/malformed client address | `503` | `ip_unavailable` |
| Unknown Host header | `400` | `invalid_host` |
| Internal error | `500` | `internal_error` |

Error responses never echo the client IP, request headers or stack traces.

## curl examples

```bash
# Dual-stack: the family of the connection curl chooses
curl -sS https://ip.privacylink.nl/api/ip

# Force an IPv4 path via DNS
curl -sS https://v4.ip.privacylink.nl/api/ip

# Force an IPv6 path via DNS
curl -sS https://v6.ip.privacylink.nl/api/ip

# JSON
curl -sS https://v4.ip.privacylink.nl/api/ip.json

# Explicit network protocol for the main hostname
curl -4 -sS https://ip.privacylink.nl/api/ip
curl -6 -sS https://ip.privacylink.nl/api/ip
```

`curl -6` fails on clients without usable IPv6 connectivity.

## Configuration

All environment-specific values are configuration, not hardcoded. Defaults in
`src/PrivacyLink.Ip/appsettings.json` target local development and the
canonical production hostnames.

| Key | Environment variable | Default | Purpose |
| --- | --- | --- | --- |
| `Hosts:Main` | `Hosts__Main` | `ip.privacylink.nl` | Exact main hostname |
| `Hosts:V4` | `Hosts__V4` | `v4.ip.privacylink.nl` | Exact IPv4-only hostname |
| `Hosts:V6` | `Hosts__V6` | `v6.ip.privacylink.nl` | Exact IPv6-only hostname |
| `Hosts:MainAliases:N` | `Hosts__MainAliases__0` | `localhost` | Extra exact main-host aliases (development only) |
| `ForwardedHeaders:KnownProxies:N` | `ForwardedHeaders__KnownProxies__0` | empty | Trusted Caddy proxy IPs |
| `ForwardedHeaders:KnownNetworks:N` | `ForwardedHeaders__KnownNetworks__0` | empty | Trusted proxy CIDRs |
| `ForwardedHeaders:ForwardLimit` | `ForwardedHeaders__ForwardLimit` | `1` | Maximum proxy hops |
| `Cors:AllowedOrigin` | `Cors__AllowedOrigin` | `https://ip.privacylink.nl` | Exact browser origin for v4/v6 APIs |
| `Https:Redirect` | `Https__Redirect` | `true` | HTTP→HTTPS redirect outside Development |

Never configure `KnownProxies`/`KnownNetworks` to trust all addresses. A missing
or wrong trusted-proxy configuration fails closed with `503` instead of showing
the internal container address.

## Docker and reverse proxy

```bash
docker build -f deploy/Dockerfile -t privacylink-ip:local .
```

The image runs as a non-root user, listens on internal port `8080`, and exposes a
`/healthz` healthcheck. It must not be published directly to the internet.

- `deploy/compose.example.yml`: internal Docker network, no public port mapping,
  healthcheck and resource limits.
- `deploy/Caddyfile.example`: integration template for the three hostnames.
- `docs/deployment.md`: DNS, network, deployment and rollback runbook.

## Tests

```bash
dotnet test PrivacyLink.Ip.slnx -c Release
```

The suite includes unit tests for IP normalization, exact host allowlisting and
HTML encoding, plus `WebApplicationFactory` integration tests for trusted and
spoofed forwarded headers, family mismatch, cache headers, CORS, Host-header
injection and `/healthz`. Tests use their own inline HTML template and do not
depend on the separate frontend files.

## Documentation

- [Architecture](docs/architecture.md)
- [Deployment and operations](docs/deployment.md)
- [Privacy](docs/privacy.md)
- [ADR 0001: forwarded headers](docs/adr/0001-forwarded-headers.md)

## Placeholders and production specifics

Production values are intentionally not committed. Replace placeholders such as
`<YOUR_VPS_IPV4>`, `<YOUR_VPS_IPV6>`, `<TRUSTED_PROXY_SUBNET>`,
`<EDGE_NETWORK>`, `<APP_DIR>`, DNS credentials and GitHub secrets before
deploying. DNS provider, VPS provider and the existing Caddy deployment are
outside this repository.

## License

MIT. See [LICENSE](LICENSE).
