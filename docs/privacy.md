# Privacy

PrivacyLink IP shows the IP addresses that our servers observe when a browser
connects. We do not run ads, set tracking cookies, or store a history of your IP
address. Network providers and intermediate proxies may still see connection
metadata.

This page describes the design intent of the software. Operators must verify
that their actual infrastructure and log configuration match this description
before publishing the service.

## What is processed

When you request a page or API endpoint, your connection exposes network
metadata, including:

- the source IP address observed by the reverse proxy and application;
- the address family (IPv4 or IPv6);
- the hostname you requested;
- standard HTTP request metadata needed to return a response.

The application uses the observed IP address only to render the response for
that request. It does not need an account, cookie, tracking identifier or
browser fingerprint.

## Why it is processed

The IP address is the product: the service's purpose is to show the visitor
which public address the server sees. The address is also technically necessary
to send the response back over the network.

## What is not stored

The application:

- has no database and no IP history;
- does not set cookies;
- does not use analytics, advertising or third-party browser scripts/fonts;
- does not log visitor IP addresses in application logs;
- does not send your IP to a third-party API.

Operational logs may record non-identifying events such as "IP family mismatch"
or "client address not public", without the address itself.

## Infrastructure and abuse protection

The VPS provider, network operator and reverse proxy necessarily process
connection metadata to deliver traffic. If the edge proxy, firewall, Docker
logging driver or hosting provider retains access logs, those logs may contain
visitor IPs. Operators must:

- avoid per-request access logs containing visitor IPs for these hostnames;
- or document the retention period and legal basis of any such infrastructure
  logs;
- keep the privacy claim consistent with the real configuration.

The service does not include a CAPTCHA, account system or persistent
rate-limiting database in the MVP.

## DNS and network effects

The service measures the address observed by this server, not necessarily the
physical device address. VPNs, corporate proxies, NAT, DNS64/NAT64 and tunnels
can change which address is visible. A browser may use a temporary IPv6 privacy
address that changes between requests; each response is a snapshot of that
request.

The second address family is fetched from a different hostname so that DNS can
select the relevant protocol. That hostname is part of the same service and uses
the same privacy rules.

## Your choices

- Use a VPN or proxy if you do not want to reveal your current public address to
  this service.
- Disable JavaScript: the initial IP is still present and selectable in the
  server-rendered HTML.
- Contact the operator with privacy questions.

## Contact and source

- Contact: `<PRIVACY_CONTACT>` (set by the operator; do not invent an address
  in this repository).
- Source code: `<SOURCE_REPOSITORY_URL>`.

## Changes

If IP metadata such as geolocation or ASN is added later, this document must be
updated before release, including the data source, local processing, accuracy
limits and retention.
