# Deployment and operations

This document describes the expected self-hosted setup. All infrastructure
values are placeholders; none are real VPS addresses, Docker subnets or
credentials.

## Prerequisites

- Debian VPS with public IPv4 and IPv6 connectivity on TCP 80/443.
- Existing Caddy instance and Docker network shared with the main PrivacyLink
  deployment (do not modify the main application's routes).
- Outbound HTTPS from the VPS/CI to pull images and packages.
- DNS control for `privacylink.nl`.
- Placeholders to replace:
  - `<YOUR_VPS_IPV4>`, `<YOUR_VPS_IPV6>`
  - `<TRUSTED_PROXY_SUBNET>` (the Docker network containing Caddy)
  - `<EDGE_NETWORK>` (external Docker network name)
  - `<APP_DIR>` (deployment directory on the VPS)
  - `<YOUR_GITHUB_REPOSITORY>`, `<YOUR_GITHUB_ENVIRONMENT>`

## 1. DNS

Create DNS-only records (no CDN proxy in the MVP):

| Name | Type | Value |
| --- | --- | --- |
| `ip` | `A` | `<YOUR_VPS_IPV4>` |
| `ip` | `AAAA` | `<YOUR_VPS_IPV6>` |
| `v4.ip` | `A` | `<YOUR_VPS_IPV4>` |
| `v6.ip` | `AAAA` | `<YOUR_VPS_IPV6>` |

`v4.ip` must have **no AAAA** record and `v6.ip` must have **no A** record.
Check CNAME flattening and `HTTPS`/`SVCB` records too; they must not offer an
alternative route that bypasses the intended family.

Verification:

```bash
dig A ip.privacylink.nl +short
dig AAAA ip.privacylink.nl +short
dig A v4.ip.privacylink.nl +short
dig AAAA v4.ip.privacylink.nl +short   # expect empty
dig A v6.ip.privacylink.nl +short      # expect empty
dig AAAA v6.ip.privacylink.nl +short
```

## 2. Docker network and trusted proxy

Caddy and the app must share an internal Docker network. The app must not
publish port 8080.

1. Determine the subnet of the shared network, for example
   `<TRUSTED_PROXY_SUBNET>` = `172.20.0.0/16`.
2. Configure the app with `ForwardedHeaders__KnownNetworks__0` set to that
   subnet, or set `ForwardedHeaders__KnownProxies__0` to the Caddy container IP.
3. Keep `ForwardedHeaders__ForwardLimit` at `1` unless a documented multi-hop
   chain exists.
4. Never use `0.0.0.0/0`, `::/0` or an unbounded Docker network as a trusted
   proxy.

If the trusted proxy is not configured, the app fails closed with `503` rather
than expose the internal address. That is expected until the network is fixed.

## 3. Configuration

Copy `deploy/compose.example.yml` to the VPS and adapt it. Set at minimum:

```yaml
environment:
  Hosts__Main: ip.privacylink.nl
  Hosts__V4: v4.ip.privacylink.nl
  Hosts__V6: v6.ip.privacylink.nl
  Cors__AllowedOrigin: https://ip.privacylink.nl
  ForwardedHeaders__KnownNetworks__0: "<TRUSTED_PROXY_SUBNET>"
```

`Hosts__MainAliases__*` is for local development only. Leave it empty in
production.

## 4. Caddy

Merge the contents of `deploy/Caddyfile.example` into the existing Caddy
configuration without changing the main PrivacyLink route:

```caddyfile
ip.privacylink.nl, v4.ip.privacylink.nl, v6.ip.privacylink.nl {
	encode zstd gzip
	reverse_proxy privacylink-ip:8080
}
```

Notes:

- Do not add per-family `bind` directives; DNS selects the family.
- Caddy must listen publicly on both IPv4 and IPv6.
- Do not add a `log` directive for these hostnames. If the edge logs all
  requests, exclude these hosts or use privacy-safe aggregated logging with no
  visitor IPs.
- Reload with `caddy reload --config <CADDYFILE>` (or the existing deployment
  convention) and confirm certificate issuance for all three names.

## 5. Build and run

```bash
docker build -f deploy/Dockerfile -t privacylink-ip:local .
docker compose -f deploy/compose.example.yml up -d
```

The container exposes only the internal port `8080`, runs as a non-root user and
uses `GET /healthz` for its healthcheck. `curl` is installed in the runtime image
for that healthcheck.

## 6. Deployment workflow

`.github/workflows/deploy.yml` is a placeholder. It:

1. Builds and pushes immutable GHCR tags: `sha-<12 chars>` and a version tag.
2. Optionally deploys over SSH when `vars.DEPLOY_ENABLED == 'true'`.
3. Runs a public smoke test against `vars.SMOKE_URL`.

Required GitHub configuration (placeholders, configure manually):

| Kind | Name | Meaning |
| --- | --- | --- |
| Variable | `DEPLOY_ENABLED` | `true` enables the deploy job |
| Variable | `APP_DIR` | `<APP_DIR>` on the VPS |
| Variable | `PRIVACYLINK_IP_IMAGE` | image name used by the VPS compose file |
| Variable | `SMOKE_URL` | e.g. `https://ip.privacylink.nl/api/ip` |
| Secret | `VPS_HOST` | `<YOUR_VPS_IPV4>` or hostname |
| Secret | `VPS_USER` | least-privilege deploy user |
| Secret | `VPS_SSH_KEY` | private key for that user |
| Secret | `VPS_KNOWN_HOSTS` | pinned host key line |

This workflow is independent of the main PrivacyLink deployment and must remain
so. Use a dedicated deploy user/key with access only to this compose project and
the minimum Docker privileges.

## 7. Healthcheck and smoke tests

```bash
# Inside the Docker network
docker compose exec privacylink-ip curl -fsS http://127.0.0.1:8080/healthz

# Public checks from a dual-stack host
curl -4 -sS https://ip.privacylink.nl/api/ip
curl -6 -sS https://ip.privacylink.nl/api/ip
curl -4 -sS https://v4.ip.privacylink.nl/api/ip
curl -6 -sS https://v6.ip.privacylink.nl/api/ip

# Spoof attempts must not change the result for an internet client
curl -sS https://ip.privacylink.nl/api/ip -H 'X-Forwarded-For: 1.2.3.4'
```

Expected: the returned address matches the observed client connection; a family
mismatch never returns `200`; spoofed headers from an unknown proxy are ignored.

## 8. Rollback

Images are immutable. To roll back:

1. Stop routing new deploys to the new tag.
2. On the VPS, set `PRIVACYLINK_IP_IMAGE=<image>:<previous-tag>` in the compose
   environment or `.env` file.
3. Run `docker compose pull && docker compose up -d`.
4. Verify `/healthz` and the public smoke tests.
5. Re-run `deploy.yml` with the previous version tag if the workflow is used for
   rollback.

Never move or overwrite an existing version tag.

## 9. Operational checklist

- [ ] DNS A/AAAA split verified; no unwanted AAAA on v4, no A on v6.
- [ ] `HTTPS`/`SVCB` and CNAME records checked.
- [ ] TLS certificates valid for all three hostnames.
- [ ] App port 8080 not reachable from the internet.
- [ ] Trusted proxy set to the exact Caddy network/IP, `ForwardLimit=1`.
- [ ] Spoofed `X-Forwarded-For` does not change the response.
- [ ] Private/container addresses are never returned as visitor IPs.
- [ ] No IP-bearing response is cached (`Cache-Control: no-store`).
- [ ] Caddy/edge/Docker logs do not persist visitor IPs.
- [ ] Healthcheck and public smoke tests pass over IPv4 and IPv6.
- [ ] Rollback procedure tested.
