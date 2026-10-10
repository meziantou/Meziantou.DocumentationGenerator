# Contributing

## Commands

```bash
dotnet build Meziantou.DocumentationGenerator.slnx
```

```bash
dotnet test --solution Meziantou.DocumentationGenerator.slnx
```

Set `DiffEngine_Disabled=true` when running tests. Browser tests install Chromium with Playwright on first use; in CI it is
installed with `pwsh tests/Meziantou.DocumentationGenerator.Tests/bin/Release/net11.0/playwright.ps1 install --with-deps chromium`.

The JSON schema (`schema/docgen.schema.json`) is generated from the configuration model: when a test reports that it is
outdated, it has been regenerated; review and commit it.

## Architecture

Read [docs/architecture.md](docs/architecture.md) first. Each stage of the build lives in its own folder of
`src/Meziantou.DocumentationGenerator` and only consumes the models of the previous stages:

| Folder | Responsibility |
| --- | --- |
| `Configuration` | `docgen.json` model, validation and schema |
| `Packages` | Acquisition, cache and safe archive reading |
| `Discovery` | Asset selection, XML documentation and PDB candidates, README |
| `Api` | PublicApiGenerator integration, visibility policy, aggregation across frameworks |
| `Links` | Routes, anchors, URLs, cross-reference resolution and link validation |
| `XmlDocs` | XML documentation parsing, rendering, `inheritdoc` and `include` |
| `SourceLinks` | PDB validation and GitHub source links |
| `Content` | Markdown rendering, highlighting, sanitization, assets |
| `Navigation` | `toc.yml` and generated navigation |
| `Search` | Search index |
| `Rendering`, `Components` | Razor rendering, staging, validation and publication |
| `Build`, `Cli` | Stages, coordinator and command line |

Rules:

- Do not add a second metadata reader: API extraction belongs to PublicApiGenerator. Only portable PDBs are read
  directly.
- Do not build URLs outside `LinkService`, and do not render code without `HighlightingService`.
- Never execute package code; read archives through `PackageArchive`.
- Keep the output deterministic: no timestamps, no machine paths, ordinal ordering.
- Report problems with a diagnostic from `DiagnosticDescriptors` and document new codes in `docs/diagnostics.md`.
- Add tests to the existing test file of the area you change.
