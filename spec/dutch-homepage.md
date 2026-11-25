# Dutch homepage at `/nl` - feature specification

**Status:** Proposed  
**Version:** 1.0  
**Date:** 2026-11-25  
**Depends on:** the SEO work described in `docs/seo.md`  
**Canonical URL:** `https://ip.privacylink.nl/nl`  
**Language of this document:** English (implementation identifiers and code stay English; the product copy described here is Dutch)

**Follow-up decision:** The original no-redirect rules below apply to the
server. With JavaScript and session storage available, `/` may send visitors
with a Dutch browser preference to `/nl`. An explicit language choice takes
precedence for the current tab session. The server does not vary responses by
language, `/nl` never redirects to `/`, and no cookies are added.

## 1. Goal

Add a native Dutch homepage at `/nl` so the service can compete for Dutch
queries such as:

- "wat is mijn ip adres"
- "mijn ip adres"
- "ip adres checken"
- "wat is mijn ipv4 adres"
- "wat is mijn ipv6 adres"
- "publiek ip adres"
- "ip adres verbergen"
- "ip adres opvragen api"

The English homepage at `/` stays unchanged in purpose and remains the
`x-default` version. The Dutch page is not a machine translation of the English
page and is not a thin keyword page: it must be genuinely useful Dutch copy
around the same working IP tool.

### 1.1 Success criteria

- `/nl` is a fully Dutch page: visible text, metadata, ARIA labels, status
  messages and copy feedback.
- The page works without JavaScript for the initial IP, exactly like `/`.
- The page uses the same IP-detection and privacy behavior as `/`; no new
  tracking, storage or third-party calls.
- Search engines see one canonical Dutch URL and a correct reciprocal
  `hreflang` pair with the English page.
- The Dutch page is at least as useful as the English page, not a shorter
  doorway page.
- All existing tests keep passing, with new tests for `/nl`, redirects and the
  sitemap.

## 2. Scope

### 2.1 In scope

- A new template `src/PrivacyLink.Ip/Web/index.nl.html`.
- A new route `GET`/`HEAD /nl`.
- Dutch metadata, Open Graph data and JSON-LD.
- Dutch visible copy and FAQ.
- Dutch UI strings in `site.js` (copy labels, status notes, announcements).
- A crawlable language switch between `/` and `/nl`.
- Redirects for aliases and family hostnames.
- A sitemap entry for `/nl`.
- Tests and documentation.

### 2.2 Out of scope

- Translating `/api` (the API documentation stays English for now).
- Translating `docs/privacy.md`, `docs/deployment.md` or internal documentation.
- Translating API error codes (`invalid_host`, `ip_unavailable`, etc.).
- Automatic language selection through `Accept-Language`, cookies or
  geolocation.
- Separate Dutch pages per topic such as `/nl/ipv4-of-ipv6`.
- New product features such as geolocation, ASN, provider lookup or DNS tools.
- Changing IP detection, trusted-proxy handling, CORS or cache behavior.

## 3. URL and language model

### 3.1 URLs

| URL | Role | Indexable |
| --- | --- | --- |
| `https://ip.privacylink.nl/` | English homepage, `x-default` | Yes |
| `https://ip.privacylink.nl/nl` | Dutch homepage | Yes |
| `https://ip.privacylink.nl/api` | English API documentation | Yes |
| `https://ip.privacylink.nl/nl/` | Alias | No, `301` to `/nl` |
| `https://ip.privacylink.nl/index.nl.html` | Raw template path | No, `301` to `/nl` |
| `https://v4.ip.privacylink.nl/nl` | Family hostname | No, `301` to main `/nl` |
| `https://v6.ip.privacylink.nl/nl` | Family hostname | No, `301` to main `/nl` |

`/nl` is the canonical Dutch URL. Do not introduce a second Dutch URL such as
`/wat-is-mijn-ip-adres` unless a later decision explicitly replaces `/nl` with a
single `301`.

### 3.2 `hreflang`

Both homepages must carry the same reciprocal set:

```html
<link rel="alternate" hreflang="en" href="https://{{MAIN_HOST}}/">
<link rel="alternate" hreflang="nl" href="https://{{MAIN_HOST}}/nl">
<link rel="alternate" hreflang="x-default" href="https://{{MAIN_HOST}}/">
```

Rules:

- Use absolute URLs.
- Every `hreflang` target must return `200` and must be self-canonical.
- `x-default` points to the English page because English is the fallback for
  unmatched languages.
- Do not add `hreflang` to `/api`; it has no translated counterpart.
- Do not use `hreflang` to hide duplicate content. The Dutch body copy must be
  genuinely Dutch.

### 3.3 Canonical

- `/` keeps `<link rel="canonical" href="https://{{MAIN_HOST}}/">`.
- `/nl` uses `<link rel="canonical" href="https://{{MAIN_HOST}}/nl">`.
- Redirect aliases instead of canonicalizing them.

### 3.4 No automatic redirect

Do not redirect `/` to `/nl` based on `Accept-Language` or a cookie. A stable
URL per language is required for crawling and for users who switch language
manually. The language switch is a normal link, not a redirect.

## 4. Page requirements

### 4.1 Template

Create `src/PrivacyLink.Ip/Web/index.nl.html` with:

```html
<!DOCTYPE html>
<html lang="nl">
```

The template uses the same placeholders as `index.html`:

- `{{MAIN_HOST}}`
- `{{V4_HOST}}`
- `{{V6_HOST}}`
- `{{INITIAL_IPV4}}`
- `{{INITIAL_IPV6}}`
- `{{SERVER_STATE}}`
- `{{ASSET_CSS_URL}}`
- `{{ASSET_JS_URL}}`

The existing `SiteRenderer.RenderHtml(template, model, mainHost, v4Host, v6Host,
assets)` must render both templates without a new renderer variant. The Dutch
template uses the same CSS and JavaScript files; do not fork the stylesheet or
the detection logic.

The page structure and DOM ids stay identical to `index.html` so the existing
JavaScript and tests keep working. Only text, metadata and language attributes
change.

### 4.2 Metadata

Use this copy exactly unless a native Dutch reviewer changes it before
implementation:

```html
<title>Wat is mijn IP-adres? Bekijk IPv4 en IPv6</title>
<meta name="description" content="Wat is mijn IP-adres? Bekijk direct je publieke IPv4- en IPv6-adres. Kopieer het met één klik. Geen advertenties, geen tracking, niets opgeslagen.">
<meta name="robots" content="index, follow">
<link rel="canonical" href="https://{{MAIN_HOST}}/nl">
```

Open Graph:

```html
<meta property="og:type" content="website">
<meta property="og:site_name" content="PrivacyLink IP">
<meta property="og:title" content="Wat is mijn IP-adres? Bekijk IPv4 en IPv6">
<meta property="og:description" content="Bekijk direct je publieke IPv4- en IPv6-adres. Kopieer het met één klik. Geen advertenties, geen tracking, niets opgeslagen.">
<meta property="og:url" content="https://{{MAIN_HOST}}/nl">
<meta property="og:locale" content="nl_NL">
<meta property="og:locale:alternate" content="en_US">
```

Twitter:

```html
<meta name="twitter:card" content="summary">
<meta name="twitter:title" content="Wat is mijn IP-adres? Bekijk IPv4 en IPv6">
<meta name="twitter:description" content="Bekijk direct je publieke IPv4- en IPv6-adres. Geen advertenties, geen tracking, niets opgeslagen.">
```

The English page must add the reciprocal Open Graph locale:

```html
<meta property="og:locale:alternate" content="nl_NL">
```

Do not put the visitor's IP address in the title, description or Open Graph
metadata. The value changes per request and is not a stable page description.

### 4.3 JSON-LD

The Dutch page carries the same structured-data shape as the English page, with
Dutch names and descriptions and `inLanguage: "nl"`:

- `WebSite`
- `WebApplication`
- `WebPage`

Use `/nl` in the `@id` and `url` values, for example
`https://{{MAIN_HOST}}/nl#website`. Do not reuse the English `@id` values on the
Dutch page.

Suggested Dutch values:

- `name`: `PrivacyLink IP`
- `alternateName`: `PrivacyLink IP-adreschecker`
- `description` (`WebSite`): `Een snelle, advertentievrije checker voor je publieke IPv4- en IPv6-adres.`
- `description` (`WebApplication`): `Toont het publieke IPv4- en IPv6-adres dat deze server voor je verbinding ziet. Geen advertenties, geen tracking, niets opgeslagen.`
- `browserRequirements`: `Een moderne browser. Het eerste adres wordt door de server weergegeven; JavaScript voegt de tweede adresfamilie en de kopieerknoppen toe.`
- `featureList`: `Publiek IPv4-adres tonen`, `Publiek IPv6-adres tonen`, `Een adres met één klik kopiëren`, `API met platte tekst en JSON`
- `inLanguage`: `nl`

Do not add `FAQPage` structured data. The visible FAQ is enough; Google's FAQ
rich results are limited to authoritative government and health sites.

### 4.4 Visible copy

Use the following Dutch copy as the baseline. A native reviewer may improve
style, but the meaning and the factual claims must match the English page.

#### Hero

```text
Publiek IP-adres
H1: Wat is mijn IP-adres?
Sub: Bekijk de publieke IPv4- en IPv6-adressen die deze server voor je verbinding ziet. Kopieer er één — geen advertenties, geen tracking, niets opgeslagen.
```

#### IP rows

```text
01 · Jouw publieke IPv4
02 · Jouw publieke IPv6
```

Buttons:

```text
Kopiëren
Opnieuw proberen
```

Initial connection line:

```text
Verbinding: het andere adres wordt gecontroleerd…
```

#### Noscript

```text
JavaScript staat uit. Het adres hierboven is door de server weergegeven en blijft leesbaar. Automatische detectie van het andere adres, de verbindingsregel en de kopieerknoppen hebben JavaScript nodig — selecteer een adres met je muis of toetsenbord om het te kopiëren.
```

#### Content sections

Use these headings and cover the same facts as the English page:

| Section | Dutch heading |
| --- | --- |
| About | Over deze IP-checker |
| What is an IP address? | Wat is een IP-adres? |
| IPv4 vs IPv6 | IPv4 en IPv6: wat is het verschil? |
| Public vs private | Publiek en privé-IP-adres |
| Why it changes | Waarom verandert mijn IP-adres? |
| How to hide it | Hoe kan ik mijn IP-adres verbergen? |
| API | API |

The body copy must explain at least:

- an IP address is the number a network uses to route traffic; it is not a name
  or an exact location
- the difference between a public address and a private address such as
  `192.168.1.20`
- IPv4 uses dotted decimal groups, IPv6 uses hexadecimal groups
- a connection may have IPv4 only, IPv6 only or both
- a failed IPv6 check does not prove the provider has no IPv6
- a VPN, proxy, CGNAT or a switch between Wi-Fi and mobile data can change the
  observed address
- IPv6 privacy extensions can rotate addresses
- a VPN hides the address from websites but does not make someone anonymous
- the service does not store visitor IPs, set cookies or run analytics

Do not add claims that the English page does not make.

#### FAQ

Use these seven questions, in this order:

1. `Wat is mijn IP-adres?`
2. `Waarom laten IPv4 en IPv6 verschillende resultaten zien?`
3. `Is mijn IP-adres een persoonsgegeven?`
4. `Kan iemand mijn huis vinden met mijn IP-adres?`
5. `Slaat deze site mijn IP-adres op?`
6. `Hoe controleer ik mijn IP-adres met curl?`
7. `Waarom staat er bij IPv6 "niet bereikbaar"?`

Each answer should be two to four sentences and should match the English FAQ in
substance. Keep the answers in the HTML even when the `<details>` element is
closed; Google indexes the content, and users can open it.

#### API block

Keep the same commands, with the same hostname placeholders:

```text
API
Hetzelfde antwoord zonder browser: platte tekst of JSON, geen cookies, geen sleutel.
curl -sS https://{{MAIN_HOST}}/api/ip
Deze hostname antwoordt met de adresfamilie van je verbinding. Voor alleen IPv4 gebruik je https://{{V4_HOST}}/api/ip; voor alleen IPv6 gebruik je https://{{V6_HOST}}/api/ip.
Volledige API-documentatie →
```

The linked API documentation page remains English.

#### Footer

```text
FAQ
API
GitHub
PrivacyLink IP · geen advertenties, geen tracking
```

#### Language switch

Add a crawlable switch to the header or footer of both homepages:

English page:

```html
<a href="/nl" hreflang="nl" lang="nl">Nederlands</a>
```

Dutch page:

```html
<a href="/" hreflang="en" lang="en">English</a>
```

Use `aria-current="page"` on the current language if the switch is rendered as a
list. Do not use JavaScript for the switch. Do not add the switch to `/api`,
because the API documentation has no Dutch counterpart.

### 4.5 JavaScript strings

`site.js` currently contains English user-facing strings. The Dutch page must not
show English status text, copy feedback or announcements.

Implement a single string table in `site.js`, keyed by the document language:

```js
var STRINGS = {
  en: { /* current English strings */ },
  nl: { /* Dutch translations */ }
};

var strings = STRINGS[document.documentElement.lang] || STRINGS.en;
```

Use `document.documentElement.lang` rather than a separate data attribute. The
English page keeps `lang="en"`; the Dutch page uses `lang="nl"`.

Translate all of these:

| Key / location | English | Dutch |
| --- | --- | --- |
| Copy button | Copy | Kopiëren |
| Copied | Copied | Gekopieerd |
| Copy failed | Copy failed | Kopiëren mislukt |
| Copy IPv4 ARIA | Copy IPv4 address | IPv4-adres kopiëren |
| Copy IPv6 ARIA | Copy IPv6 address | IPv6-adres kopiëren |
| Retry IPv6 ARIA | Retry IPv6 detection | IPv6-detectie opnieuw proberen |
| Loading note | Checking for an IPv4/IPv6 address… | Controleren op een IPv4-/IPv6-adres… |
| Timeout note | No answer within 3 seconds. A firewall, proxy or VPN may be blocking this check. | Geen antwoord binnen 3 seconden. Een firewall, proxy of VPN kan deze controle blokkeren. |
| Unavailable note | Not reachable from this browser. That does not prove your connection lacks IPv4/IPv6 — a VPN, firewall or proxy can cause it. | Niet bereikbaar vanuit deze browser. Dat bewijst niet dat je verbinding geen IPv4/IPv6 heeft — een VPN, firewall of proxy kan de oorzaak zijn. |
| Error note | The IPv4/IPv6 check could not be completed. Try again in a moment. | De IPv4-/IPv6-controle kon niet worden voltooid. Probeer het zo meteen opnieuw. |
| Server-state error | This page could not read the address the server sent. Reload the page to try again. | Deze pagina kon het adres dat de server stuurde niet lezen. Herlaad de pagina om het opnieuw te proberen. |
| Announcer: available | IPv4/IPv6 address found. | IPv4-/IPv6-adres gevonden. |
| Announcer: timeout | IPv4/IPv6 check timed out. | IPv4-/IPv6-controle verlopen. |
| Announcer: unavailable | IPv4/IPv6 not reachable from this browser. | IPv4/IPv6 niet bereikbaar vanuit deze browser. |
| Announcer: error | IPv4/IPv6 check failed. | IPv4-/IPv6-controle mislukt. |
| Announcer: copied | IPv4/IPv6 address copied. | IPv4-/IPv6-adres gekopieerd. |
| Announcer: copy failed | Copy failed. The address is selected, so you can copy it with your keyboard. | Kopiëren mislukt. Het adres is geselecteerd, zodat je het met je toetsenbord kunt kopiëren. |
| Announcer: example copied | Example copied. | Voorbeeld gekopieerd. |
| Connection: both | Connection: IPv4 + IPv6 available | Verbinding: IPv4 + IPv6 beschikbaar |
| Connection: loading | Connection: checking IPv4/IPv6… | Verbinding: IPv4/IPv6 controleren… |
| Connection: available | Connection: IPv4/IPv6 available | Verbinding: IPv4/IPv6 beschikbaar |
| Connection: timeout | Connection: IPv4/IPv6 timed out | Verbinding: IPv4-/IPv6-controle verlopen |
| Connection: unavailable | Connection: IPv4/IPv6 not reachable | Verbinding: IPv4/IPv6 niet bereikbaar |
| Connection: error | Connection: IPv4/IPv6 check failed | Verbinding: IPv4-/IPv6-controle mislukt |

Rules:

- Every user-facing string in `site.js` comes from `STRINGS`; no Dutch text is
  hardcoded outside the table.
- The English output must remain byte-for-byte equivalent in meaning and
  behavior.
- `aria-label` values updated by JavaScript must use the string table too.
- The fallback language is English when `lang` is missing or unsupported.
- Do not use a third-party i18n library.

### 4.6 Accessibility

- The document language is `nl` on `/nl` and `en` on `/`.
- The language switch links use `hreflang` and `lang`.
- `aria-current="page"` is used for the current language.
- All `aria-label`, `aria-live` messages and visible labels are Dutch on `/nl`.
- The page remains keyboard operable and works at 320px width.
- The `<details>`/`<summary>` FAQ keeps native keyboard behavior.
- Do not rely on color alone for status; the existing dot plus text pattern
  stays.

## 5. Routing and rendering

### 5.1 Shared handler

Refactor the current homepage handler so `/` and `/nl` share the same IP
resolution and family checks:

```text
HandleHomepageAsync(context, templateFile, canonicalPath)
```

Behavior:

1. Classify the Host header.
2. Reject unknown hosts with the existing `400 invalid_host`.
3. If the host is `v4.*` or `v6.*`, redirect to
   `https://{Hosts:Main}{canonicalPath}` with `301`.
4. Resolve the client IP with the existing `ClientIpResolver`.
5. Preserve the existing `503 ip_unavailable` and `503 ip_family_mismatch`
   behavior.
6. Read the requested template from `Web/`.
7. Render it with the existing `SiteRenderer`.
8. Return `text/html; charset=utf-8` with `Cache-Control: no-store`.

Suggested routes:

```text
MapMethods("/", ["GET", "HEAD"], HandleHomepage)
MapMethods("/index.html", ["GET", "HEAD"], HandleHomepage)
MapMethods("/nl", ["GET", "HEAD"], HandleDutchHomepage)
```

The raw `index.nl.html` path must never serve an unrendered template. Add a
redirect middleware entry (`/index.nl.html` -> `/nl`) before static files and
restrict static file serving to `/assets` so HTML templates are never publicly
addressable. Do not map `/index.nl.html` to the Dutch handler; the redirect is
the only supported behavior.

### 5.2 Redirects

Add these permanent redirects:

| Request | Response |
| --- | --- |
| `/nl/` | `301` to `/nl` |
| `/index.nl.html` | `301` to `/nl` |
| `/nl` on `v4.*` | `301` to `https://{main}/nl` |
| `/nl` on `v6.*` | `301` to `https://{main}/nl` |

Preserve the existing redirects for `/index.html`, `/api.html` and `/api/`.

### 5.3 Static files

The current `UseStaticFiles` serves every file under `Web/`, including HTML
templates. Adding `index.nl.html` would expose it at `/index.nl.html` unless the
middleware is constrained.

Required approach:

- Serve only `Web/assets` under the `/assets` request path.
- Keep the content-hashed cache behavior for `site.css` and `site.js`.
- Keep `favicon.svg` available under `/assets/favicon.svg`.
- Add the `/index.nl.html` redirect as defense in depth.

Do not serve `Web/*.html` as static files.

### 5.4 Caching

- `/nl` is IP-bearing and must use `Cache-Control: no-store`, no `ETag`, no
  `Last-Modified`, exactly like `/`.
- `/api` remains `public, no-cache` with its weak `ETag`.
- `/robots.txt` and `/sitemap.xml` remain cacheable for one hour.

## 6. SEO requirements

### 6.1 Indexing

- `/nl` is indexable: `200`, `text/html`, `index, follow`, no
  `X-Robots-Tag: noindex`.
- `/nl` must not send `X-Robots-Tag`.
- `/nl` must not be blocked in `robots.txt`.
- `/api/ip`, `/api/ip.json`, `/healthz` and errors keep
  `X-Robots-Tag: noindex, nofollow`.

### 6.2 Sitemap

Add the Dutch URL to `/sitemap.xml`:

```xml
<url><loc>https://{main}/nl</loc></url>
```

The sitemap must continue to contain only main-host URLs. Do not add v4/v6
hostnames or redirect aliases.

### 6.3 Internal links

- The English homepage gets a visible language switch to `/nl`.
- The Dutch homepage gets a visible language switch to `/`.
- The Dutch homepage links to the English `/api` documentation with descriptive
  Dutch anchor text: `Volledige API-documentatie`.
- Do not add a language switch to `/api`.
- Do not add hidden keyword links.

### 6.4 Content quality

- The Dutch page must not be a word-for-word translation that reads awkwardly.
  Use natural Dutch and the terminology in section 7.
- Every H2 must answer a real follow-up question; do not add headings only for
  keywords.
- The tool stays above the explanatory content.
- The page should keep the same fast, server-rendered first IP and the same
  single extra request for the other family.

### 6.5 Search Console

After deployment:

1. Submit `https://ip.privacylink.nl/sitemap.xml` again.
2. Use URL Inspection for `https://ip.privacylink.nl/nl`.
3. Click **Request indexing**.
4. Check that Google selects `https://ip.privacylink.nl/nl` as canonical and
   does not treat `/nl/` or `/index.nl.html` as duplicates.
5. Monitor Dutch queries in **Performance → Search results**. Do not expect the
   head term to rank within weeks.

## 7. Dutch terminology and tone

- Use informal `je`/`jouw`, consistent with the English "your".
- Use `het IP-adres` (neuter), `het IPv4-adres`, `het IPv6-adres`.
- Use `publiek IP-adres` / `publieke IP-adressen`; avoid mixing `openbaar` and
  `publiek` randomly.
- Use `internetaanbieder` for a consumer ISP; `provider` is acceptable in
  technical contexts.
- Use `thuisnetwerk`, `router`, `openbaar IP-adres`, `privé-IP-adres`.
- Keep technical terms in their common form: `NAT`, `CGNAT`, `VPN`, `DNS64`,
  `dual-stack`, `IPv4`, `IPv6`, `API`, `JSON`, `curl`.
- Use `kopiëren` with the trema, and `één` where it means "one".
- Do not use machine-translated sentences such as "uw IP-adres is zichtbaar
  voor de wereld". Prefer direct, concrete Dutch.
- Do not use fear-based copy about IP addresses. Explain what an address does
  and does not reveal.

## 8. Tests

### 8.1 Integration tests

Add a `DutchHomepageTests` class with at least:

- `DutchHomepage_ReturnsRenderedHtmlWithNoStore`
  - `200`, `text/html; charset=utf-8`, `Cache-Control: no-store`
  - contains `<html lang="nl">`
  - contains the Dutch H1
  - contains `rel="canonical"` with `/nl`
  - contains `hreflang="en"` and `hreflang="nl"`
  - contains `og:locale` `nl_NL`
  - contains valid JSON-LD with `"inLanguage": "nl"`
  - contains no `{{`
  - has no `X-Robots-Tag`
- `DutchHomepage_RendersIpv4AndIpv6Placeholders`
- `DutchHomepage_EncodesInjectedValues` (reuse renderer behavior)
- `DutchHomepage_OnFamilyHostRedirectsToMain`
- `DutchHomepage_WithTrailingSlashRedirects`
- `IndexNlHtml_RedirectsToDutchHomepage`
- `EnglishHomepage_ContainsDutchHreflang`
- `SitemapXml_ContainsDutchHomepage`
- `RobotsTxt_AllowsDutchHomepageOnMainHost`
- `ApiDocs_RemainEnglishAndUnaffected`
- `HomepageTemplates_UseTheSamePlaceholders` (read both templates and compare
  the set of `{{PLACEHOLDER}}` tokens)

### 8.2 Static / JavaScript checks

The existing test suite does not execute browser JavaScript. Add at least one
of:

- a small unit test that reads `site.js` and asserts that every string key used
  by `site.js` exists in both `STRINGS.en` and `STRINGS.nl` (if the string table
  is exported or can be parsed), or
- a manual browser checklist in the pull request.

Manual browser checklist:

- `/nl` with JavaScript on: initial IP visible, other family detected, copy
  labels Dutch, announcements Dutch, retry label Dutch.
- `/nl` with JavaScript off: initial IP visible and selectable, Dutch noscript
  note visible.
- Language switch from `/` to `/nl` and back.
- 320px viewport: no horizontal scroll, long IPv6 addresses wrap.
- Keyboard-only: skip link, copy buttons, retry button, FAQ summaries.
- Screen reader: document language `nl`, Dutch labels and live announcements.
- `/api` still English and unchanged.

### 8.3 Acceptance gate

The feature is complete only when:

- [ ] `/nl` returns `200` and is fully Dutch.
- [ ] `/` still returns `200` and contains the Dutch `hreflang` link.
- [ ] `/nl/` and `/index.nl.html` redirect permanently to `/nl`.
- [ ] v4/v6 `/nl` redirect to the main hostname.
- [ ] `/nl` has a self-referencing canonical and reciprocal `hreflang`.
- [ ] `/sitemap.xml` contains `/` and `/nl`.
- [ ] `/robots.txt` does not block `/nl`.
- [ ] `/nl` has no `X-Robots-Tag` and uses `Cache-Control: no-store`.
- [ ] No raw template is reachable under `Web/`.
- [ ] All visible and dynamic UI text is Dutch.
- [ ] No third-party requests, cookies or storage were added.
- [ ] `dotnet build PrivacyLink.Ip.slnx -c Release` succeeds.
- [ ] `dotnet test PrivacyLink.Ip.slnx -c Release` passes.
- [ ] All text files use CRLF.

## 9. Rollout

1. Implement on a feature branch.
2. Run the build and tests.
3. Deploy to production.
4. Verify `/nl` and `/` with `curl` and a browser.
5. Validate the JSON-LD with Google's Rich Results Test.
6. Submit the sitemap and request indexing for `/nl`.
7. Monitor Search Console for Dutch queries and canonical selection.
8. Keep the English page available; do not redirect it.

Rollback is the same as any frontend deploy: redeploy the previous image. The
Dutch page is additive; removing the route and template restores the previous
state.

## 10. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Mixed Dutch/English UI | Central string table; manual checklist; search the template for English strings. |
| Duplicate content | Genuine Dutch copy; canonical `/nl`; reciprocal `hreflang`; no parameterized variants. |
| Raw template exposure | Serve only `/assets` statically; redirect `/index.nl.html`. |
| hreflang mistakes | Absolute URLs; every target `200` and self-canonical; test both pages. |
| Two templates drift | Keep identical DOM ids, use shared CSS/JS, add parity tests for placeholders and metadata. |
| Thin doorway page | Full content and FAQ, at least as substantial as the English page. |
| Root domain noindex limits link equity | Build external links to `/nl`; link from GitHub and relevant directories. |
| Wrong translation tone | Native Dutch review before release. |

## 11. Open decisions

These are decided for this version; record a new decision if they change:

- URL: `/nl` (not a keyword slug).
- Tone: informal `je`.
- API documentation: stays English for now.
- Privacy documentation: stays in the repository and is not translated as part
  of this feature.
- No automatic language redirect.
- No Dutch `FAQPage` structured data.

## 12. Definition of done

A Dutch visitor opens `https://ip.privacylink.nl/nl` and immediately sees their
public IPv4 and/or IPv6 address, with Dutch labels, Dutch copy feedback, a Dutch
FAQ and a working language switch to English. Google can crawl the page, sees
one canonical Dutch URL with correct `hreflang`, and can index the page without
redirects, duplicate URLs or mixed-language UI.
