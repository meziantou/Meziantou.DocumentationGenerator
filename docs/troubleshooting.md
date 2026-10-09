# Troubleshooting

## Reading diagnostics

Diagnostics have a stable code (see [diagnostics.md](diagnostics.md)), a severity, and the most precise location
available: configuration file, line, column and JSON path; Markdown or TOC file and line; package, target framework,
assembly or symbol. Use `--verbosity detailed` to see informational diagnostics and stage timings.

Diagnostics never contain credentials: URLs with user information and secret query parameters are redacted.

## Common problems

| Symptom | Cause and fix |
| --- | --- |
| `DG2001` package not found | The exact version is not in the configured sources. Versions are never floated and other sources are never tried implicitly. Check `NuGet.Config`, source mapping and `source`. |
| `DG2002` authentication failed | Configure credentials in `NuGet.Config` (`packageSourceCredentials`) or install the credential provider of your feed. Prompts are disabled. |
| `DG2006` in offline mode | Run one build without `--offline` to populate the cache. |
| `DG2003` corrupt package | A cached package failed its integrity check. It is removed and downloaded again when online. You can also delete the cache directory. |
| `DG4003` broken link | A link targets a page or anchor that is not generated. The message names the page and the link. |
| `DG7004` publication failed | The output directory contains a file that the generator does not own at a path it must write. Move the file or use another output directory. |
| Pages show no documentation | The package has no XML documentation file for that framework (`DG3002`). Enable `GenerateDocumentationFile` in the library. |
| No source links | The package has no PDB, the PDB does not match, Source Link is missing, or the host is not GitHub (`DG3003`, `DG3005`, `DG3006`). Embed the PDB (`DebugType=embedded`) or configure `sourceLinks.symbolServers`. |
| Broken styles under a sub-path | `site.basePath` does not match the deployment path. |

## Cache

Downloaded packages are cached in `cacheDirectory` (default: the `DOCGEN_CACHE` environment variable, or
`Meziantou.DocumentationGenerator/cache` in the per-user local application data directory). Each package is stored with its SHA-512 and
verified before use. Deleting the cache is always safe.

## Cancellation

Press Ctrl+C to cancel a build. The build stops at the next checkpoint, the temporary staging directory is deleted, and
the output directory is not modified (exit code `130`).

## Compatibility

The configuration format is versioned (`"version": 1`). Unknown properties are errors, so that a misspelled option is
not silently ignored. Generated URLs follow the rules of [api-reference.md](api-reference.md#urls-and-anchors) and only
change when product identifiers, namespaces, type names or authored paths change.
