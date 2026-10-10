# Deployment

The output directory contains plain files: HTML pages, CSS, JavaScript, the search index, copied assets, `404.html`, and
optionally `sitemap.xml` and `robots.txt`. Any static host works; no server-side code, rewrite rule or SPA fallback is
needed.

## Base path

Set `site.basePath` (or `--base-path`) to the URL path where the site is served:

| Deployment | `basePath` | Home page | Type page | Search index |
| --- | --- | --- | --- | --- |
| `https://docs.example.com/` | `/` | `/` | `/json/api/Contoso.Json.JsonReader.html` | `/_assets/search-index.json` |
| `https://example.com/docs/` | `/docs/` | `/docs/` | `/docs/json/api/Contoso.Json.JsonReader.html` | `/docs/_assets/search-index.json` |

Links are root-relative and include the base path, so the files must be published at that path. Directory URLs
(`/docs/json/`) rely on the host serving `index.html` for directories, which every static host does. Opening the files
with `file://` is not supported.

## 404 page

`404.html` is generated at the root of the output directory. Its links work whatever URL triggered it. Configure the host
to serve it for missing files:

- GitHub Pages, Netlify, Cloudflare Pages: automatic for `404.html` at the root of the published directory.
- Azure Static Web Apps: `"responseOverrides": { "404": { "rewrite": "/docs/404.html" } }` in `staticwebapp.config.json`.
- nginx: `error_page 404 /docs/404.html;`.
- IIS: `<httpErrors><error statusCode="404" path="/docs/404.html" responseMode="ExecuteURL" /></httpErrors>`.

## Canonical URLs, sitemap and robots

- Set `site.url` (or `--site-url`) to the origin, such as `https://docs.example.com`, to generate canonical URLs, Open
  Graph URLs and `sitemap.xml`. The origin must not contain a path: canonical URLs are `url` + `basePath` + page path.
- Without `site.url`, no canonical URL and no sitemap are generated; no placeholder host is ever used.
- `site.robots` is `none` by default: no `robots.txt` is generated, so a deployment is never opted into indexing silently.
  With `allow` or `disallow`, `robots.txt` is generated only when `basePath` is `/`, because crawlers only read it at
  the origin root (`DG1010`). `disallow` also adds a `noindex` meta tag to every page.

## GitHub Pages

A project site is served at `https://{owner}.github.io/{repository}/`, so use the repository name as base path:

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
      - run: dotnet meziantou-docgen build --base-path "/${{ github.event.repository.name }}/" --site-url "https://${{ github.repository_owner }}.github.io"
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

GitHub Pages serves `404.html` automatically. Add `.nojekyll` to the content assets if a path starts with `_` and
Jekyll processing is enabled for the branch (artifact deployments are not processed by Jekyll).

## Output ownership

The generator owns the files listed in `.docgen-manifest.json`. A build replaces them, deletes generated files that are no
longer produced, and never deletes or overwrites other files (it fails with `DG7004` instead). A failed or cancelled build
leaves the previous site untouched. Do not configure an output directory that contains the configuration, the content or
the packages (`DG1009`).
