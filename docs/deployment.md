# Deployment

The output directory contains plain files: HTML pages, CSS, JavaScript, the search index, copied assets, `404.html`, and
optionally `sitemap.xml` and `robots.txt`. Any static host works; no server-side code, rewrite rule or SPA fallback is
needed.

## Base path

Internal links, images, stylesheets, scripts and search URLs are relative to the HTML file containing them.
The same generated files can be deployed at the root of a domain or under any subpath without rebuilding or
setting `site.basePath`.

For example, `api/contoso.json/Contoso.Json.JsonReader.html` links to `../../_assets/site.css` and to its
sibling type as `Contoso.Json.JsonWriter.html`. Both work at `https://docs.example.com/` and at
`https://meziantou.github.io/Meziantou.Framework/`.

Set `site.basePath` (or `--base-path`) when generating absolute canonical/social URLs and a sitemap with `site.url`,
or to choose the local preview server's path. These absolute metadata URLs describe one deployment and must be
regenerated if that deployment changes. External links remain unchanged.

Directory URLs (`api/contoso.json/`) rely on the host serving `index.html` for directories, which every static host
does. Opening the files with `file://` is not supported.

## 404 page

`404.html` is generated at the root of the output directory. Its links and assets are relative too, so configure the host
to **redirect** missing-page requests to its actual URL (for example `/Meziantou.Framework/404.html`), not rewrite
the response while retaining the missing URL. The local preview server does this automatically.

Hosts such as GitHub Pages that automatically serve `404.html` at the missing URL can show the error text, but
its navigation and assets cannot reliably resolve for nested missing URLs without a host-level redirect.

## Canonical URLs, sitemap and robots

- Set `site.url` (or `--site-url`) to the origin, such as `https://docs.example.com`, to generate canonical URLs, Open
  Graph URLs and `sitemap.xml`. The origin must not contain a path: canonical URLs are `url` + `basePath` + page path.
- Without `site.url`, no canonical URL and no sitemap are generated; no placeholder host is ever used.
- `site.robots` is `none` by default: no `robots.txt` is generated, so a deployment is never opted into indexing silently.
  With `allow` or `disallow`, `robots.txt` is generated only when `basePath` is `/`, because crawlers only read it at
  the origin root (`DG1010`). `disallow` also adds a `noindex` meta tag to every page.

## GitHub Pages

A project site is served at `https://{owner}.github.io/{repository}/`. Relative links work there without a base path;
the example sets it to generate correct canonical URLs and a sitemap:

```yaml
name: Documentation

on:
  push:
    branches: [main]

permissions:
  contents: read
  pages: write
  id-token: write

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 11.0.x
          dotnet-quality: preview
      - run: dotnet tool restore
      - run: dotnet Meziantou.DocumentationGenerator build --base-path "/${{ github.event.repository.name }}/" --site-url "https://${{ github.repository_owner }}.github.io"
      - uses: actions/upload-pages-artifact@v4
        with:
          path: _site

  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - id: deployment
        uses: actions/deploy-pages@v4
```

See the [404 page limitation](#404-page) above for GitHub Pages' automatic error responses. Add `.nojekyll` to the content assets if a path starts with `_` and
Jekyll processing is enabled for the branch (artifact deployments are not processed by Jekyll).

## Output ownership

The generator owns the files listed in `.docgen-manifest.json`. A build replaces them, deletes generated files that are no
longer produced, and never deletes or overwrites other files (it fails with `DG7004` instead). A failed or cancelled build
leaves the previous site untouched. Do not configure an output directory that contains the configuration, the content or
the packages (`DG1009`).
