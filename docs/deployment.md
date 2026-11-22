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
  - `<TRUSTED_PROXY_SUBNET>` (dedicated Docker network of this stack; its
    gateway is the trusted proxy peer)
  - `<HOST_PORT>` (free loopback port published for Caddy, e.g. `8082`)
  - `<APP_DIR>` (deployment directory on the VPS, e.g. `/opt/privacylink-ip`)
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

Caddy runs on the VPS host and reaches the app through a loopback-only published
port. The app must not be reachable from the internet and must not share a
network with the main PrivacyLink stacks.

1. Give this stack a dedicated Docker network with a fixed subnet, for example
   `<TRUSTED_PROXY_SUBNET>` = `10.77.0.0/24`. The container sees Caddy as that
   network's gateway address (`10.77.0.1`).
2. Configure the app with `ForwardedHeaders__KnownNetworks__0` set to that
   subnet. Trusting the dedicated stack network is intentional: the gateway of
   that subnet is the only peer that can reach the published loopback port.
3. Keep `ForwardedHeaders__ForwardLimit` at `1` unless a documented multi-hop
   chain exists.
4. Never use `0.0.0.0/0`, `::/0` or a shared/unbounded Docker network as a
   trusted proxy.

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
	reverse_proxy 127.0.0.1:<HOST_PORT>
}
```

The production site block is versioned in `deploy/vps/privacylink-ip.caddy`.

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

`.github/workflows/deploy.yml` builds and deploys the service. It:

1. Builds, tests and format-checks the solution.
2. Pushes one immutable image to GHCR tagged `sha-<12 chars>` (plus the Git tag
   for `v*` pushes) and hands the image digest to the deploy job.
3. Deploys that exact digest over SSH through the root-owned
   `/usr/local/sbin/privacylink-ip-deploy` helper.
4. Verifies `/healthz`, both address families from the VPS and that a spoofed
   `X-Forwarded-For` header does not change the result.

The deploy job runs in the protected GitHub environment `production`, so it can
require a manual approval. The VPS setup is documented in
[`deploy/vps/README.md`](../deploy/vps/README.md).

Required GitHub configuration (placeholders, configure manually in the
`production` environment):

| Kind | Name | Meaning |
| --- | --- | --- |
| Secret | `VPS_HOST` | VPS hostname (`<YOUR_VPS_IPV4>` or a DNS name) |
| Secret | `VPS_USER` | least-privilege deploy user |
| Secret | `VPS_SSH_PRIVATE_KEY` | private key for that user |
| Secret | `VPS_SSH_KNOWN_HOSTS` | pinned host key line |

The deploy user may only run the deploy helper through sudo and must not be in
the `docker` group. The helper accepts nothing but a digest of
`ghcr.io/<YOUR_GITHUB_REPOSITORY>` and must be installed from a reviewed commit.

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

Images are immutable and deployments use digests. To roll back:

1. Find the previous digest in the GHCR package versions or a previous
   deployment log.
2. On the VPS, run
   `sudo /usr/local/sbin/privacylink-ip-deploy <image>@sha256:<previous-digest>`.
3. The helper recreates only the app container and waits for `/healthz`.
4. Verify the public smoke tests in section 7.

Never move or overwrite an existing version tag. The helper prunes unused images
after a healthy deployment, so a rollback may need to pull the old digest again.

## 9. Operational checklist

- [ ] DNS A/AAAA split verified; no unwanted AAAA on v4, no A on v6.
- [ ] `HTTPS`/`SVCB` and CNAME records checked.
- [ ] TLS certificates valid for all three hostnames.
- [ ] App port not reachable from the internet (loopback publish only).
- [ ] Trusted proxy set to the dedicated stack subnet, `ForwardLimit=1`.
- [ ] Spoofed `X-Forwarded-For` does not change the response.
- [ ] Private/container addresses are never returned as visitor IPs.
- [ ] No IP-bearing response is cached (`Cache-Control: no-store`).
- [ ] Caddy/edge/Docker logs do not persist visitor IPs.
- [ ] Healthcheck and public smoke tests pass over IPv4 and IPv6.
- [ ] Rollback procedure tested.
