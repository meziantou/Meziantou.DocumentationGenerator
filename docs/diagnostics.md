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
| `DG2005` | error (safety) | The package contains unsafe entries (rooted paths, `..`, links, duplicates, excessive sizes) |
| `DG2006` | error | Package not available in the cache in offline mode |
| `DG2009` | error | The package identity does not match the configuration |
| `DG7001` | error | A build stage failed unexpectedly |
| `DG7002` | error | The build was cancelled |
| `DG7003` | error | Invalid command-line value |
