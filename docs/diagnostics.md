# Diagnostics

Every diagnostic has a stable code, a severity and, when available, the configuration path, file, line, column,
package, target framework, assembly or symbol it applies to. Diagnostics are sorted deterministically: by
severity, then by location, then by code.

## Severity and exit codes

- `error`: the build fails and the output directory is not modified.
- `warning`: the build succeeds unless `--warnaserror` or `diagnostics.warningsAsErrors` is set.
- `info`: displayed with `--verbosity detailed`.

Override a severity with `diagnostics.severity` in the configuration. Diagnostics marked as *safety* report unsafe
input and cannot be lowered.

| Exit code | Meaning |
| --- | --- |
| `0` | Success. |
| `1` | Build failed: invalid configuration, invalid input, or an error diagnostic. The previous site is preserved. |
| `2` | Invalid command line. |
| `130` | Cancelled (Ctrl+C). The previous site is preserved. |

## Codes

| Code | Default severity | Description |
| --- | --- | --- |
| `DG1001` | error | Unsupported configuration version |
| `DG1002` | error | Invalid configuration syntax or unknown property |
| `DG1003` | error | The configuration declares no package and no content |
| `DG1004` | error | Invalid package identifier or version (version ranges are rejected) |
| `DG1005` | error | Invalid target framework filter |
| `DG1006` | error | Invalid site URL or base path |
| `DG1007` | error | Configured input file or directory not found |
| `DG1008` | error | Duplicate identity |
| `DG1009` | error (safety) | The output directory overlaps inputs |
| `DG1010` | warning | robots.txt cannot be generated under a base path |
| `DG1011` | error (safety) | Credentials must not be stored in the configuration |
| `DG1012` | error | Invalid product definition |
| `DG1013` | error | Invalid configuration value |
| `DG1014` | error | Invalid API filter pattern |
| `DG1015` | error | Invalid diagnostic severity override |
| `DG2001` | error | Package not found in the configured sources (the exact version is required) |
| `DG2002` | error | Package source or authentication failure |
| `DG2003` | error | Invalid or corrupt package |
| `DG2004` | error | A target framework or assembly filter matches no asset of the package |
| `DG2005` | error (safety) | The package contains unsafe entries (rooted paths, `..`, links, duplicates, excessive sizes) |
| `DG2006` | error | Package not available in the cache in offline mode |
| `DG2007` | warning | The package contains no compile-time managed assembly (dependency-only, native or tool package) |
| `DG2008` | warning | Invalid managed assembly or unrecognized target framework folder, ignored |
| `DG2009` | error | The package identity does not match the configuration |
| `DG2010` | info | A package dependency is not configured in `references`, so documentation inherited from it is unavailable |
| `DG2011` | info | Explains which asset is documented for each target framework |
| `DG2012` | warning | Unsupported or unreadable metadata (the assembly or attribute is skipped or displayed partially) |
| `DG2013` | error | The builds of an assembly cannot be aggregated across target frameworks (duplicate inputs) |
| `DG3001` | warning | The package has no README; a generated page is used |
| `DG3002` | warning | No XML documentation for an assembly; pages show signatures only |
| `DG7001` | error | A build stage failed unexpectedly |
| `DG7002` | error | The build was cancelled |
| `DG7003` | error | Invalid command-line value |
