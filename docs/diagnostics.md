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
| `DG3003` | info | No usable PDB or Source Link information for an assembly; no source links are generated |
| `DG3004` | warning | A README link targets a file that is not in the package and the package has no GitHub repository commit; the link is removed instead of being guessed |
| `DG3005` | warning | A PDB does not match its assembly (PDB identifier and timestamp); it is not used |
| `DG3006` | info | Source Link points to a host other than GitHub; no source link is generated |
| `DG3007` | info | Several implementation assemblies give different locations for a reference assembly symbol; no source link is generated |
| `DG4001` | warning | Unresolved reference: no page and no `xrefs` mapping; the name is displayed without a link |
| `DG4002` | warning | Ambiguous reference: several symbols match at the same priority; qualify it with `{packageId}!` |
| `DG4003` | error | Broken link in the generated site (missing page or anchor) |
| `DG4004` | warning | Several types map to the same page; URLs are disambiguated with the assembly name or a hash |
| `DG4005` | error | Several files map to the same output path (compared case-insensitively) |
| `DG4006` | error | Duplicate anchor in a page |
| `DG4007` | error (safety) | Unsafe output path |
| `DG4008` | warning | A URL with a scheme other than http, https or mailto was removed |
| `DG4009` | info | A documentation reference targets a symbol outside the configured packages and no `xrefs` mapping matches; it is displayed as code |
| `DG5001` | warning | A link targets a Markdown file that is not a page of the content directory |
| `DG5002` | warning | Invalid front matter or Markdown file |
| `DG5003` | error | Invalid table of contents (YAML error, malformed node, unsafe link) |
| `DG5004` | error | Cycle in nested tables of contents |
| `DG5005` | warning | A page is not referenced by the table of contents; it is generated and searchable |
| `DG5006` | error | A table of contents entry targets a missing page, product or symbol |
| `DG5007` | info | Unsupported table of contents key, ignored |
| `DG5008` | warning | A link or image targets a file that does not exist |
| `DG5009` | error (safety) | A link or include targets a path outside the allowed directory |
| `DG5010` | info | Unknown code language; the code is displayed without highlighting |
| `DG6001` | warning | Malformed XML documentation file; API pages are generated without documentation |
| `DG6002` | info | XML documentation entries that match no public symbol (usually internal members) |
| `DG6003` | warning | `<inheritdoc>` cannot be resolved (missing or ambiguous target, or dependency not configured in `references`); a note is displayed |
| `DG6004` | warning | Cycle in inherited documentation |
| `DG6005` | warning | `<include>` cannot be resolved (missing file or no matching element) |
| `DG6006` | info | Unsupported XML documentation tag; its text is displayed |
| `DG6007` | warning | A `param`, `typeparam`, `paramref` or `typeparamref` names a parameter that does not exist |
| `DG6008` | warning | A documentation section or entry is specified more than once; the first one is used |
| `DG6009` | warning | Unsupported selector in `<include path>` or `<inheritdoc path>` |
| `DG6010` | info | Several interfaces provide documentation for an implicitly inherited member; the first declared interface is used |
| `DG7001` | error | A build stage failed unexpectedly |
| `DG7002` | error | The build was cancelled |
| `DG7003` | error | Invalid command-line value |
| `DG7004` | error | The site cannot be published (for example a file not generated by the tool would be overwritten) |
