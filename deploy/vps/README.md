# Contabo VPS setup (softable.nl)

This runbook prepares the existing Softable VPS for the automated deployment of
PrivacyLink IP. The service is an independent Compose project next to the main
PrivacyLink stacks; it must never share their volumes, ports or configuration.

Assumptions, verified on the server:

- Debian 13 with Docker Engine and the Compose plugin.
- Caddy runs on the host and is the only public HTTP(S) entry point.
- DNS is already live: `ip.privacylink.nl` (A + AAAA),
  `v4.ip.privacylink.nl` (A only), `v6.ip.privacylink.nl` (AAAA only).
- Loopback port `8082` is free and not used by any other stack.
- The deployment account is `codex`, may log in with its own key and may run
  only the deploy helper through sudo.

## 1. Install the Compose file and deploy helper

```sh
sudo install -d -o root -g root -m 0755 /opt/privacylink-ip
sudo install -o root -g root -m 0644 compose.yaml /opt/privacylink-ip/compose.yaml
sudo install -o root -g root -m 0750 privacylink-ip-deploy /usr/local/sbin/privacylink-ip-deploy
```

The deploy helper accepts only a digest of
`ghcr.io/janouwehand/privacylink-ip`. It pulls that digest, recreates only the
`privacylink-ip` container, waits for readiness on `/healthz` and removes unused
images built from this repository. Images still used by a container are kept.

Grant the deployment account sudo rights for this one command only:

```sh
echo 'codex ALL=(root) NOPASSWD: /usr/local/sbin/privacylink-ip-deploy' \
  | sudo tee /etc/sudoers.d/privacylink-ip-deploy >/dev/null
sudo chmod 0440 /etc/sudoers.d/privacylink-ip-deploy
sudo visudo -cf /etc/sudoers.d/privacylink-ip-deploy
```

## 2. Install the Caddy site

```sh
sudo install -o root -g root -m 0644 privacylink-ip.caddy \
  /etc/caddy/sites-enabled/privacylink-ip.caddy
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

The site proxies the three hostnames to `127.0.0.1:8082` and deliberately has no
access log.

## 3. Network and trusted proxy

The stack defines its own network `privacylink-ip-backend` with the fixed subnet
`10.77.0.0/24`. Because the port is published on the host loopback, the
application sees Caddy as the gateway `10.77.0.1`. That subnet is the only trusted
forwarded-header peer; do not widen it and do not trust `0.0.0.0/0`.

## 4. Make the container image pullable

The VPS pulls the image by digest without registry credentials, so the GHCR
package `privacylink-ip` must be public. Run the `Deploy` workflow once with
`publish_only` enabled to create the package, then set the package visibility to
public in the repository's package settings (or with
`gh api --method PATCH /user/packages/container/privacylink-ip -f visibility=public`).
GitHub does not allow reverting a public package to private.

## 5. Configure GitHub

Create the `production` environment in the repository settings and restrict it to
the `main` branch. Add the deployment secrets to that environment (not to
repository-wide secrets):

| Secret | Value |
| --- | --- |
| `VPS_HOST` | `softable.nl` |
| `VPS_USER` | `codex` |
| `VPS_SSH_PRIVATE_KEY` | private key of the deployment account |
| `VPS_SSH_KNOWN_HOSTS` | pinned `ssh-keyscan` line for the VPS |

Verify the host-key fingerprint out of band before storing it.

## 6. Deploy and verify

Start the `Deploy` workflow manually (or push a `v*` tag). It builds one image,
deploys that digest and verifies:

- `https://ip.privacylink.nl/healthz` from the runner.
- `https://v4.ip.privacylink.nl/api/ip` over IPv4 and
  `https://v6.ip.privacylink.nl/api/ip` over IPv6 from the VPS.
- That a spoofed `X-Forwarded-For` header does not change the reported address.

## 7. Rollback

Deployments are immutable digests. To roll back, re-run the deployment for the
previous digest:

```sh
sudo /usr/local/sbin/privacylink-ip-deploy ghcr.io/janouwehand/privacylink-ip@sha256:<previous>
```

The helper removes unused images after a healthy deployment, so an older digest
may need to be pulled again from GHCR. The `Clean up container package versions`
workflow keeps the newest `sha-*` versions (default 10) and every release-tagged
version in the registry; raise `keep_sha_versions` before relying on an older
digest for a delayed rollback.
