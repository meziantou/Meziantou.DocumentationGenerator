# Meziantou.DocumentationGenerator

Meziantou.DocumentationGenerator is a .NET tool that generates a fully static documentation site from NuGet packages and Markdown
pages whose folder structure defines the navigation.

- API reference generated from the public API of the packages (read as metadata, package code is never executed),
  with one page per type, member anchors, C# declarations, XML documentation, inherited documentation and links to the
  source code on GitHub.
- Source packages (`contentFiles/cs/**/*.cs`) are documented too: their sources are compiled in memory and their
  `internal` types are shown as the consuming project sees them.
- APIs of every target framework of a package on a single page, with availability per framework and declaration
  variants when frameworks differ.
- Authored Markdown pages with front matter, tables, alerts, syntax highlighting and `cref:` links to APIs.
- Plain HTML files that work on any static host, at the root of a domain or under a path such as `/docs/`. No .NET
  runtime, server or CDN is needed to browse the site; JavaScript only adds search, theme selection and conveniences.
- Light, dark and system themes, responsive and accessible layout, client-side search.

## Requirements

- The .NET 11 SDK (or runtime) to run the tool. While .NET 11 is in preview, install a preview SDK.
- No requirement to browse the generated site.

## Quick start

Install the tool in a local tool manifest:

```bash
dotnet new tool-manifest
```

```bash
dotnet tool install Meziantou.DocumentationGenerator
```

Create a configuration file and a `docs` directory:

```bash
dotnet Meziantou.DocumentationGenerator init --package Contoso.Json --package-version 2.1.0 --title "Contoso"
```

Generate the site in `_site`:

```bash
dotnet Meziantou.DocumentationGenerator build
```

Preview the site on `http://localhost:8080`, rebuilding it and reloading open pages when you edit the content:

```bash
dotnet Meziantou.DocumentationGenerator build --watch --serve
```

To install the tool globally instead, use `dotnet tool install --global Meziantou.DocumentationGenerator` and run
`Meziantou.DocumentationGenerator`.

## Minimal configuration

```json
{
  "version": 1,
  "site": { "title": "Contoso" },
  "packages": [
    { "id": "Contoso.Json", "version": "2.1.0" }
  ],
  "content": { "root": "docs" }
}
```

Each package becomes a product with an API reference. The `docs` directory contains Markdown pages; `docs/index.md` is
the home page and the folder structure of `docs` defines the navigation.

## Commands

| Command | Description |
| --- | --- |
| `Meziantou.DocumentationGenerator init [directory]` | Creates `docgen.json` and `docs/index.md`. Existing Markdown files are never overwritten; `--force` overwrites `docgen.json`. Options: `--package`, `--package-version`, `--title`. |
| `Meziantou.DocumentationGenerator build` | Generates the site. Options: `--config` (default `docgen.json`), `--output`, `--base-path`, `--site-url`, `--offline`, `--no-source-links`, `--warnaserror`, `--verbosity quiet\|minimal\|normal\|detailed`, `--watch` (rebuild when the configuration, content or local packages change), `--serve` (preview the site on a local web server; with `--watch`, open pages reload after each build), `--port` (default `8080`, `0` picks a free port). |
| `Meziantou.DocumentationGenerator --version`, `--help` | Displays the version or the help. |

Exit codes: `0` success, `1` build failed (nothing is published), `2` invalid command line, `130` cancelled. With
`--watch` or `--serve`, the command runs until Ctrl+C and then exits with `0`; without `--watch`, a failed build exits
with `1` and the server is not started.

## Documentation

- [Configuration reference](docs/configuration.md): every option, path rules and examples (grouped products,
  multiple target frameworks, private feeds, `/docs/` deployment).
- [Authoring content](docs/content.md): Markdown, front matter, `cref:` links, assets, navigation, XML documentation tags,
  `inheritdoc` and `include`.
- [API reference](docs/api-reference.md): `ref/` versus `lib/`, target frameworks, visibility, URLs and anchors, source
  links, missing XML or PDB.
- [Deployment](docs/deployment.md): static hosts, GitHub Pages, base path, 404 page, canonical URLs, sitemap and robots.
- [Diagnostics](docs/diagnostics.md) and [troubleshooting](docs/troubleshooting.md).
- [Architecture and contracts](docs/architecture.md), [performance budgets](docs/performance.md) and
  [contributing](CONTRIBUTING.md).
- [Release process](docs/releasing.md).
- A complete [sample](samples/basic) built from source.

## Limitations

Versioned sites and version selectors, API diffs, full-text search, localization, custom themes and plugins, and
complete DocFX compatibility are not supported. See
[architecture.md](docs/architecture.md#deferred-features).

## Building from source

The repository requires the .NET 11 SDK pinned in `global.json`.

```bash
dotnet build Meziantou.DocumentationGenerator.slnx
```

```bash
dotnet test --solution Meziantou.DocumentationGenerator.slnx
```

```bash
dotnet pack src/Meziantou.DocumentationGenerator/Meziantou.DocumentationGenerator.csproj --output artifacts/package
```
