# ADR 0001: Trusted forwarded headers and fail-closed client-IP resolution

- **Status:** Accepted
- **Date:** 2026-11-22
- **Decision area:** Client-IP identification behind Caddy

## Context

The ASP.NET Core container is not directly exposed to the internet. Caddy
terminates TLS and forwards requests over an internal Docker network, so the
direct peer seen by the app is Caddy, not the visitor.

Forwarded headers (`X-Forwarded-For`, `X-Forwarded-Proto`) are attacker
controlled unless they come from a trusted proxy. A naive implementation that
reads the first `X-Forwarded-For` value allows any client to spoof its displayed
IP. Since the displayed address is the product, this is a correctness and
privacy-critical risk.

ASP.NET Core 8+ no longer trusts forwarded headers from unknown proxies by
default. If the proxy is not configured, the app keeps seeing the internal
container/Caddy address. Presenting that address as the visitor's public IP
would leak internal network details and mislead users.

## Options considered

1. **Trust all proxies / read the first header value.**
   Rejected: trivially spoofable, violates the MVP requirements.
2. **Use a CDN or managed edge that injects a trusted header.**
   Rejected for the MVP: adds a third party, complicates the privacy claim and
   the DNS family split.
3. **Explicit trusted proxy IPs/CIDRs plus a fail-closed public-address check.**
   Chosen.

## Decision

- Run `UseForwardedHeaders` early with only `XForwardedFor` and
  `XForwardedProto`.
- Trust only explicitly configured Caddy IPs (`ForwardedHeaders:KnownProxies`)
  or a strictly bounded proxy subnet (`ForwardedHeaders:KnownNetworks`). Never
  trust all networks.
- Set `ForwardLimit` to `1` unless a documented proxy chain exists.
- After the middleware, resolve the address from
  `HttpContext.Connection.RemoteIpAddress`, normalize IPv4-mapped IPv6
  addresses, and never include ports or brackets.
- Reject loopback, private, link-local, CGNAT, unique-local, multicast and
  unspecified addresses. Return a neutral `503 ip_unavailable` instead of a
  `200` with a non-public address.
- Log only a non-identifying reason, never the IP or full headers.
- Validate the `Host` header against an exact allowlist and enforce the
  v4/v6 family on the corresponding hostnames.

## Consequences

- Misconfiguration is visible as `503`, not as a plausible but wrong public IP.
- Local development on plain `localhost` also fails closed; developers must
  configure a trusted loopback proxy and send an `X-Forwarded-For` value (see
  README).
- Adding a CDN or another proxy hop requires re-reviewing the full trust chain,
  updating `KnownProxies`/`KnownNetworks`, and retesting spoofing and family
  behavior.
- Integration tests cover trusted proxy resolution, unknown-proxy spoofing,
  multi-hop headers, malformed headers, missing configuration and private
  addresses.

## References

- ASP.NET Core proxy and forwarded-header guidance (see `spec/spec.md` §15).
- ASP.NET Core 8 breaking change: forwarded headers from unknown proxies are
  ignored.
