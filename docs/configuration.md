# Configuration reference

The generator reads a JSON file, `docgen.json` by default. Comments and trailing commas are allowed. Add
`"$schema"` to get completion and validation in editors:

```json
{
  "$schema": "https://raw.githubusercontent.com/meziantou/Meziantou.DocumentationGenerator/main/schema/docgen.schema.json",
  "version": 1
}
```

The schema is also included in the tool package (`schema/docgen.schema.json`) and is generated from the
configuration model, so it cannot drift from what the tool accepts.

## Rules

- Every relative path is resolved against the directory containing the configuration file, never the current
  working directory.
- Unknown properties are errors (`DG1002`).
- Errors report the file, line, column and JSON path of the invalid value.
- Loading the configuration never downloads packages and never writes to the output directory.
- Credentials are never stored in the configuration. Configure feeds and credentials in `NuGet.Config` or with
  a credential provider.

## Top-level properties

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `version` | integer | required | Configuration format version. Must be `1`. |
| `site` | object | | Site identity and deployment settings. |
| `output` | path | `_site` | Output directory. It must be a dedicated directory: it cannot contain the configuration file, the content directory, the cache or packages. |
| `cacheDirectory` | path | `DOCGEN_CACHE`, or a per-user directory | Package cache. |
| `packages` | array | `[]` | Packages whose APIs are documented. |
| `references` | array | `[]` | Packages used for inherited documentation and cross-references only; their APIs are not published. Same properties as `packages`. |
| `content` | object | | Authored Markdown pages. |
| `api` | object | all visible APIs | API selection rules. |
| `xrefs` | array | `System` and `Microsoft` to Microsoft Learn | Links to documentation of symbols outside the configured packages. |
| `diagnostics` | object | | Diagnostic severity settings. |
| `sourceLinks` | object | enabled | Source link settings. |

At least one package or a content directory is required.

## `site`

| Property | Default | Description |
| --- | --- | --- |
| `title` | `Documentation` | Site title, used in the header and page titles. |
| `description` | | Default page description. |
| `language` | `en` | Language of the content (`<html lang>`). |
| `basePath` | `/` | URL path where the site is deployed. `docs`, `/docs` and `/docs/` are equivalent. |
| `url` | | Absolute origin (`https://example.com`) used for canonical URLs, the sitemap and social previews. It must not contain a path: canonical URLs are `url` + `basePath` + page path. Without `url`, no canonical URL and no sitemap are generated. |
| `logo` | | Logo image file. |
| `favicon` | | Favicon file. |
| `socialImage` | | Image used by social previews. Requires `url`. |
| `robots` | `none` | `none`: no `robots.txt` and no robots meta tag. `allow`: `robots.txt` allowing crawlers and referencing the sitemap. `disallow`: `robots.txt` disallowing crawlers and a `noindex` meta tag on every page. `robots.txt` is only generated when `basePath` is `/` because crawlers only read it at the origin root (`DG1010`). |
| `footer` | | Text displayed in the footer. |

## `packages[]`

| Property | Default | Description |
| --- | --- | --- |
| `id` | required | Package identifier. |
| `version` | latest stable | Exact version (`1.2.3`, `1.2.3-beta.1`), [version range](https://learn.microsoft.com/nuget/concepts/package-versioning#version-ranges) (`[1.0,2.0)`) or floating version (`1.*`, `*`, `*-*`). See [package versions](#package-versions). |
| `source` | NuGet.Config sources | Source URL or the name of a source defined in `NuGet.Config`. |
| `path` | | Local `.nupkg` file, or folder containing `.nupkg` files (the highest version matching `version` is used). No feed is queried. Cannot be combined with `source`. |
| `targetFrameworks` | all | Target framework folders to document (`net8.0`, `netstandard2.0`). |
| `assemblies` | all | Assembly file names to document (`Contoso.dll`). |
| `defineConstants` | none | Preprocessor symbols defined when compiling a [source package](api-reference.md#source-packages), in addition to those of the target framework. Use it for the symbols the package's MSBuild targets define in the consuming project (`"ROSLYN_4_12_OR_GREATER"`). |

### Package versions

| `version` | Package used |
| --- | --- |
| omitted, `*` | Latest stable version |
| `*-*` | Latest version, including prereleases |
| `1.*`, `1.2.*-*` | Latest version matching the floating version (NuGet rules) |
| `[1.0,2.0)`, `[1.0,)` | Highest version in the range. Prereleases only match when a bound is a prerelease (`[2.0.0-beta,3.0)`) |
| `1.2.3` | Exactly `1.2.3`. Unlike in a `PackageReference`, a version without range syntax is not a minimum version |

A version other than an exact version is resolved at each build from the versions listed by the package sources.
All the sources of the package must respond (`DG2002`), because a failing source could hide a higher version. The
resolved version is reported as `DG2015` (`--verbosity detailed`) and recorded in the output manifest. With
`--offline`, it resolves to the highest matching version in the package cache. With `path`, the version of a local
`.nupkg` file must match `version`, and a [local folder](#local-packages-and-offline-builds) resolves to its highest
matching package.

Use exact versions for reproducible builds: the site changes when a new matching version is published.

Dependencies of the documented packages are never downloaded implicitly. To inherit documentation from a
dependency (`<inheritdoc />` on an override of a dependency type), add it to `references` (`DG2010` lists the
dependencies that are not configured).

Each package is a section of the API reference, at `/api/{package}/` where `{package}` is the lowercase package ID.
The package description is the lead paragraph of its page.

## `content`

| Property | Default | Description |
| --- | --- | --- |
| `root` | required | Directory containing Markdown pages. Every `.md` file is a page; the folder structure defines the navigation (see [content.md](content.md#navigation)). |
| `assets` | `[]` | Files or directories, relative to `root`, copied as-is (for example `CNAME`). Files referenced by Markdown links and images are copied automatically. |
| `exclude` | `[]` | Glob patterns of Markdown files to ignore. Files such as `AGENTS.md` are not skipped automatically: `"exclude": ["**/AGENTS.md"]`. |
| `editUrl` | none | Absolute URL of the editor of a page. `{path}` is replaced by the path of the Markdown file relative to `root`, such as `https://github.com/contoso/docs/edit/main/docs/{path}`. Each page then shows an "edit" link next to its title. |

## `api`

```json
"api": {
  "include": [ "Contoso.*" ],
  "exclude": [ "N:Contoso.Internal", "T:Contoso.Legacy*", "M:Contoso.Client.Dispose" ]
}
```

A pattern is an optional documentation ID prefix (`N:`, `T:`, `M:`, `P:`, `F:`, `E:`) followed by a qualified
name in which `*` matches any characters. A pattern matches a symbol when it matches the symbol or one of its
containers, so excluding a namespace excludes its types and their members. `include` patterns select namespaces
and types. `exclude` patterns win over `include` patterns.

## `xrefs[]`

```json
"xrefs": [
  { "namespace": "System", "url": "https://learn.microsoft.com/dotnet/api/{uid}" }
]
```

When `xrefs` is omitted, `System` and `Microsoft` namespaces link to Microsoft Learn
(`https://learn.microsoft.com/dotnet/api/{uid}`). A configured list replaces this default: include these two entries to
keep them next to your own, and add a query string to pin a version
(`https://learn.microsoft.com/dotnet/api/{uid}?view=net-10.0`). `"xrefs": []` disables external links.

References to symbols that are not part of the configured packages are linked only when a mapping matches
their namespace. `{uid}` is replaced by the lowercase documentation ID without its prefix, with generic arity
markers replaced by `-N` (`T:System.Collections.Generic.List`1` → `system.collections.generic.list-1`), and `#`, `{`,
`}` and `,` replaced by `-` (`M:System.String.#ctor(System.Char[])` → `system.string.-ctor`). Methods link to the page of
their overloads, without parameters or generic arity. Enum values link to the page of their enum with a fragment
(`F:System.StringComparison.Ordinal` → `system.stringcomparison#system-stringcomparison-ordinal`), as on Microsoft
Learn; enums are recognized from `references` packages and from the .NET runtime running the tool. The longest
matching namespace wins.

## `diagnostics`

```json
"diagnostics": {
  "warningsAsErrors": true,
  "severity": { "DG3003": "none", "DG5010": "warning" }
}
```

Severity values are `error`, `warning`, `info` and `none`. Diagnostics that report unsafe input (for instance
`DG1009`) cannot be lowered. See [diagnostics.md](diagnostics.md) for the list of codes.

## `sourceLinks`

| Property | Default | Description |
| --- | --- | --- |
| `enabled` | `true` | Generate links to source files using PDBs and Source Link. |
| `symbolServers` | `[]` | Symbol servers (SSQP protocol, such as `https://symbols.nuget.org/download/symbols`) queried for PDBs that are neither embedded nor next to the assembly in the package. Only these servers are contacted, without credentials, and downloaded PDBs are cached. |

## Examples

### Single package

```json
{
  "version": 1,
  "site": { "title": "Contoso.Json" },
  "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ]
}
```

### Multiple packages

```json
{
  "version": 1,
  "site": { "title": "Contoso" },
  "packages": [
    { "id": "Contoso.Data", "version": "3.0.0" },
    { "id": "Contoso.Data.Sqlite", "version": "3.0.0" }
  ],
  "content": { "root": "docs" }
}
```

The API reference has one section per package: `/api/contoso.data/` and `/api/contoso.data.sqlite/`.

### Multiple target frameworks

```json
{
  "version": 1,
  "packages": [
    {
      "id": "Contoso.Json",
      "version": "2.1.0",
      "targetFrameworks": [ "net8.0", "netstandard2.0" ]
    }
  ]
}
```

All target frameworks of a package are documented by default; `targetFrameworks` restricts them. Each API page shows
the frameworks of each member.

### Private feed

Feeds and credentials are configured in `NuGet.Config`, next to `docgen.json` or in any parent directory, as for
`dotnet restore`:

```xml
<configuration>
  <packageSources>
    <add key="contoso" value="https://pkgs.example.com/contoso/v3/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <contoso>
      <add key="Username" value="build" />
      <add key="ClearTextPassword" value="%CONTOSO_FEED_TOKEN%" />
    </contoso>
  </packageSourceCredentials>
</configuration>
```

```json
{
  "version": 1,
  "packages": [ { "id": "Contoso.Internal", "version": "1.4.0", "source": "contoso" } ]
}
```

Credential providers (such as the Azure Artifacts credential provider) are used non-interactively. Package source mapping
is honored.

### Deployment under `/docs/`

```json
{
  "version": 1,
  "site": {
    "title": "Contoso",
    "url": "https://www.example.com",
    "basePath": "/docs/"
  },
  "packages": [ { "id": "Contoso.Json", "version": "2.1.0" } ]
}
```

Pages are linked as `/docs/...` and canonical URLs are `https://www.example.com/docs/...`. See
[deployment.md](deployment.md).

### Local packages and offline builds

```json
{
  "version": 1,
  "packages": [
    { "id": "Contoso.Json", "version": "2.1.0", "path": "artifacts/Contoso.Json.2.1.0.nupkg" },
    { "id": "Contoso.Http", "version": "*-*", "path": "artifacts/packages" }
  ]
}
```

`path` is a `.nupkg` file or a folder. In a folder, like in a NuGet local feed, the `.nupkg` files at the root of the
folder whose name starts with the package identifier (`Contoso.Http.*.nupkg`, excluding `.symbols.nupkg`) are read, and
the highest version matching `version` is used, following the [package versions](#package-versions) rules. Use
`*-*` to accept the prerelease versions produced by CI builds. A `.nupkg` file only needs its version to be in the
range.

Local packages are used in place, are available in offline mode, and their identity is verified. Remote packages are cached; `--offline` uses only the
cache and fails when a package is missing (`DG2006`). In offline mode, a version range resolves to the highest
matching version in the cache.
