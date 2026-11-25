# Search engine optimization

This document records the SEO decisions for the PrivacyLink IP homepage and
API documentation page. It separates what the application now does
automatically from the work that still happens outside the repository, such as
Google Search Console verification and link building.

## Goal

The primary search intent is: "show me my own public IP address". The page
should be eligible to rank for that intent and for closely related long-tail
queries, while keeping the IP tool itself first and fast.

The page does not try to win the head term "what is my IP" overnight. That term
is dominated by established, high-authority IP lookup sites. A new subdomain
wins by being technically correct, fast, privacy-friendly, useful, and easy to
cite for specific queries.

## What was wrong before

- The homepage title and H1 did not contain the primary query. The H1 was
  "Your public IP, as the world sees it".
- There was no canonical URL. `/index.html` returned `200` with the same page as
  `/`, and `/api.html` returned `200` with the same page as `/api`. Both v4/v6
  hostnames also served the homepage and documentation.
- There was no `robots.txt` and no `sitemap.xml`.
- The page had almost no indexable explanatory content beyond a short About
  block. It had no FAQ, no "what is an IP address" section and no content for
  IPv4/IPv6 or privacy queries.
- The API documentation page had the H1 "API" and a short description, with no
  canonical URL and no structured data.
- Raw data endpoints (`/api/ip`, `/api/ip.json`, `/healthz`) and error responses
  did not explicitly opt out of indexing.
- The root homepage `https://privacylink.nl/` sends `X-Robots-Tag: noindex,
  nofollow, noarchive`. That header does not automatically apply to
  subdomains, but it means the parent site's homepage cannot pass link equity.

## What is implemented

### One canonical URL per page

| URL | Behavior | Indexable |
| --- | --- | --- |
| `https://ip.privacylink.nl/` | English homepage (`x-default`) | Yes |
| `https://ip.privacylink.nl/nl` | Dutch homepage | Yes |
| `https://ip.privacylink.nl/api` | API documentation | Yes |
| `https://ip.privacylink.nl/index.html` | `301` to `/` | No, redirect |
| `https://ip.privacylink.nl/nl/` and `/index.nl.html` | `301` to `/nl` | No, redirect |
| `https://ip.privacylink.nl/api.html` | `301` to `/api` | No, redirect |
| `https://ip.privacylink.nl/api/` | `301` to `/api` | No, redirect |
| `https://v4.ip.privacylink.nl/` | `301` to the main homepage | No, redirect |
| `https://v6.ip.privacylink.nl/` | `301` to the main homepage | No, redirect |
| `/nl` on either family hostname | `301` to the main Dutch homepage | No, redirect |
| `https://v4.ip.privacylink.nl/api` | `301` to the main API page | No, redirect |
| `https://v6.ip.privacylink.nl/api` | `301` to the main API page | No, redirect |
| `https://ip.privacylink.nl/api/ip` | Plain-text data | No, `X-Robots-Tag` |
| `https://ip.privacylink.nl/api/ip.json` | JSON data | No, `X-Robots-Tag` |
| `https://ip.privacylink.nl/healthz` | Health check | No, `X-Robots-Tag` |

Each rendered page carries a self-referencing `<link rel="canonical">`.
The family hostnames are excluded from crawling by their own `robots.txt`
response (`Disallow: /`). They remain fully usable for the browser's
second-family request and for API clients.

`301` (and `308`) are both strong permanent-redirect signals for Google. The
application uses `301` because it is the most widely understood permanent
redirect.

### Metadata and content

The homepage now has:

- `<title>What Is My IP Address? Check IPv4 &amp; IPv6</title>`
- an exact-match H1: "What is my IP address?"
- a description that explains the value and the privacy position
- Open Graph and Twitter card metadata for link previews
- JSON-LD for `WebSite`, `WebApplication` and `WebPage`
- visible sections for:
  - what an IP address is
  - IPv4 versus IPv6
  - public versus private addresses
  - why an address changes
  - how to hide an address
  - the API
- a FAQ with concise answers to the questions people actually ask after they
  see their address

The API documentation page now has:

- `<title>IP Address API — Plain Text &amp; JSON | PrivacyLink IP</title>`
- an H1 that names the product and the intent: "IP address API"
- a canonical URL, Open Graph metadata and Twitter card metadata
- `TechArticle` and `BreadcrumbList` JSON-LD
- a visible breadcrumb (Home / API)

The content is deliberately below the tool. The primary action (see and copy the
address) still fits on the first screen and still works before JavaScript runs.

### Structured data

Structured data is emitted as JSON-LD in the HTML. Inline JSON-LD is a data
block, not an executable script, so the strict CSP (`script-src 'self'`) does not
need a nonce or hash for it.

There is no `FAQPage` markup. Google has limited FAQ rich results to
well-known government and health sites, so the markup would add maintenance
cost without a realistic display benefit. The visible FAQ is still useful for
users, snippets and AI answers.

### `no-store` does not block indexing

All responses that contain a visitor IP use `Cache-Control: no-store`, and the
IP-bearing HTML is no exception. That is a privacy decision and it stays.

Google's technical requirements are an accessible page, an HTTP `200` response
and indexable content. `no-store` controls caches; it is not an indexing
directive. Googlebot fetches a fresh copy and can index the page. Use
`noindex`/`X-Robots-Tag` when a page must stay out of the index, not `no-store`.
See <https://developers.google.com/search/docs/essentials/technical> and
<https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag>.

One consequence is intentional: the address in Google's rendered copy is
Google's own observed address, not a visitor's. The title, description and
explanatory content are stable, so this does not create duplicate-content or
cloaking problems.

### Performance

The page remains small, server-rendered and free of third-party requests:

- the first IP is in the HTML before JavaScript runs
- CSS and JavaScript are content-hashed and cached immutably
- there are no fonts, analytics scripts or images that block rendering
- the second-family request is the only extra network call for address
  detection

Good Core Web Vitals help rankings indirectly, through user experience, but
they are not a substitute for relevance and authority.

## Google Search Console

These steps happen outside the repository and need access to the DNS zone and
the Google account that will own the property.

1. Open <https://search.google.com/search-console>.
2. Add a **Domain property** for `privacylink.nl`. A domain property covers
   `ip.privacylink.nl` and both family hostnames.
3. Copy the DNS `TXT` record Google provides and add it at the DNS provider for
   `privacylink.nl`. Wait for DNS propagation.
4. Click **Verify**. If the domain property is not possible, use a URL-prefix
   property for `https://ip.privacylink.nl/` and verify with a DNS `TXT` record
   or the HTML file method. The meta-tag method would require a template
   placeholder and a deploy, so DNS is simpler.
5. Open **Sitemaps** and submit `https://ip.privacylink.nl/sitemap.xml`.
6. Open **URL Inspection**, enter `https://ip.privacylink.nl/`, and use
   **Request indexing**. Repeat for `https://ip.privacylink.nl/nl` and
   `https://ip.privacylink.nl/api`.
7. After a few days, check **Page indexing** for all three URLs. A new site can
   take days to weeks before the first impressions appear.
8. Use **Performance → Search results** to see the actual queries that bring
   impressions. Optimize for those queries instead of guessing.

A rough external check is a search for `site:ip.privacylink.nl`. It is not a
complete index report, but it confirms whether at least one URL is known.

### What not to do in Search Console

- Do not add a `noindex` rule to `/` or `/api`.
- Do not disallow `/` in `robots.txt`. A disallowed URL can still be indexed
  from links, but Google cannot see a `noindex` directive on it.
- Do not submit the v4/v6 hostnames as separate sites. They are API transport
  hostnames and should not compete with the canonical page.
- Do not expect the Sitemap report to show "Success" immediately. "Couldn't
  fetch" during the first hours is common; check the HTTP status and content
  type first.

## Keyword strategy

### English

The page is currently English and `lang="en"`. The realistic clusters are:

| Cluster | Example queries | Where the page answers it |
| --- | --- | --- |
| Primary | what is my ip, what is my ip address, my ip address | H1, title, hero, FAQ |
| Address families | what is my ipv6, ipv4 and ipv6 address, dual stack ip | IPv4/IPv6 section, rows, FAQ |
| Privacy | ip address checker no ads, ip lookup without tracking, private ip checker | About, privacy FAQ, hero |
| Public vs private | public ip vs private ip, why is my ip different from my router | Public vs private section |
| Developer | ip address api, curl my ip, public ip json api | API block and `/api` page |
| No JavaScript | check ip without javascript, server rendered ip address | Noscript note, About |

The page should not repeat "what is my IP" in every heading. Use natural
language and answer the follow-up questions. Keyword stuffing is a spam risk and
does not help.

### Dutch

The Dutch homepage at `https://ip.privacylink.nl/nl` targets queries such as
"wat is mijn ip adres", "mijn ip adres" and "ip adres checken" with Dutch copy
and the same server-rendered IP tool. Both homepages are in the sitemap, carry
reciprocal `en`, `nl` and `x-default` alternates, and link to each other without
JavaScript. The API documentation remains English and has no alternates.

The server always renders English at `/` and Dutch at `/nl`, independent of
`Accept-Language`. With JavaScript and session storage available, the homepage
at `/` redirects visitors whose preferred browser language is Dutch to `/nl`.
An explicit language choice overrides browser detection for the current tab
session. Only the chosen language is stored; no cookies are used.

Before releasing changes to the Dutch homepage, verify in a browser:

- With JavaScript on, the initial IP is visible, the other family is checked,
  and copy feedback, retry labels and live announcements are in Dutch.
- With JavaScript off, the initial IP remains visible and selectable, and the
  Dutch no-script message is visible.
- Both language links work; `/api` remains English.
- At 320px, there is no horizontal scrolling and long IPv6 addresses wrap.
- With a keyboard, the skip link, copy and retry buttons, and FAQ summaries
  work. With a screen reader, the document language and live feedback are Dutch.

### Brand

"PrivacyLink" collides with a US fence company and with similarly named Dutch
companies. Always use the full product name "PrivacyLink IP" in titles and
anchors. Brand queries such as "PrivacyLink IP" are small but easy to win once
the page is indexed.

## Content plan

Implemented now:

- homepage with the primary query in the title and H1
- explanatory sections and FAQ
- API documentation page
- Dutch homepage at `/nl`, with `hreflang` and translated UI strings

Recommended next, in order:

1. One or two deep pages only if they answer a distinct intent, for example
   "IPv4 vs IPv6" or "public vs private IP address". Do not create thin
   near-duplicates of the homepage.
2. A short "about the project" page or an expanded privacy page, linked from the
   homepage. This supports trust and gives other sites something to cite.
3. Keep the API documentation current. Developer queries are lower volume but
   highly qualified and easier to win than consumer head terms.

## Authority and links

Technical SEO makes the page eligible; links and mentions make it competitive.

- Link to `https://ip.privacylink.nl/` from the GitHub repository README and
  release notes, not only to the API documentation. The README now does this.
- Set the GitHub repository **Website** field to `https://ip.privacylink.nl/`.
  The repository page is a relevant, high-authority citation.
- Ask relevant privacy, open-source and developer directories for a listing.
  A real tool with a clear privacy position is a legitimate listing.
- Answer real questions on Stack Overflow, Reddit or forums only where the tool
  genuinely helps. Drive-by link drops are spam.
- If `privacylink.nl` later becomes indexable, link to the IP tool from a
  relevant page. Today the root site sends `noindex` and cannot pass that
  signal.

Do not buy links, use link exchanges at scale, or generate doorway pages for
every city or query variant. Those tactics risk a manual action and are not
needed for this tool.

## Measurement

Track these in Search Console, not in the browser:

- impressions and average position for the query clusters above
- click-through rate for the homepage title and description
- which URLs are indexed, and whether Google chose the intended canonical
- Core Web Vitals for mobile and desktop

A reasonable expectation for a new subdomain is:

- indexing: days to a few weeks
- first long-tail impressions: two to eight weeks
- meaningful traffic for competitive head terms: months, and only with links
  and ongoing content

Search Console data is aggregated and does not expose individual visitor IPs.
The application still does not add analytics scripts or cookies.

## Maintenance checklist

- Keep the canonical hostname in `Hosts:Main`; the sitemap and canonical tags
  follow it automatically.
- Keep `/` and `/api` indexable. Check for accidental `X-Robots-Tag` headers
  after proxy or hosting changes.
- Keep the v4/v6 redirects and `robots.txt` behavior intact. They are part of
  the API contract and the canonicalization strategy.
- Update the FAQ when a recurring support question appears.
- Re-run the tests after template or header changes:
  `dotnet test PrivacyLink.Ip.slnx -c Release`.
- Validate structured data with Google's Rich Results Test after editing the
  JSON-LD.
- Add every new canonical page to the sitemap endpoint in `Program.cs`, then
  re-submit the sitemap and request indexing for that page.

## References

- Google Search technical requirements:
  <https://developers.google.com/search/docs/essentials/technical>
- Robots meta tag and `X-Robots-Tag`:
  <https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag>
- Canonicalization:
  <https://developers.google.com/search/docs/crawling-indexing/canonicalization>
- HTTP status codes for crawlers:
  <https://developers.google.com/crawling/docs/troubleshooting/http-status-codes>
- Sitemaps:
  <https://developers.google.com/search/docs/crawling-indexing/sitemaps/overview>
- Search Console:
  <https://search.google.com/search-console>
