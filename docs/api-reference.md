# API reference

## How APIs are read

Assemblies are read as metadata with
[Meziantou.Framework.PublicApiGenerator](https://www.nuget.org/packages/Meziantou.Framework.PublicApiGenerator). They are
never loaded or executed: module initializers, static constructors, attributes, MSBuild targets and analyzers of the
packages never run.

## Assets: `ref/` versus `lib/`

For each target framework folder of a package:

- when `ref/{tfm}/` exists, its assemblies are documented (they are what the compiler uses);
- otherwise, `lib/{tfm}/` assemblies are documented;
- `lib/` assemblies shadowed by `ref/`, and `runtimes/{rid}/lib/` assemblies, are only used to find PDBs and source
  locations; they never add APIs;
- `_._` placeholders, satellite assemblies (`*.resources.dll`), native files and unmanaged files are ignored.

The detailed `DG2011` diagnostics (`--verbosity detailed`) explain which file is documented for each framework.

## Source packages

A package with no compile-time assembly (only `_._` placeholders, or no `ref/` and `lib/` folders) whose
`contentFiles/cs/{tfm}/` or `contentFiles/any/{tfm}/` folders contain `.cs` files is a source package: NuGet adds
those files to the consuming project. For each framework folder, the files are compiled in memory with Roslyn into a
metadata-only assembly and its XML documentation, then documented like any other assembly. The code is only parsed
and compiled: it is never executed, and no analyzer or source generator runs.

- The preprocessor symbols of the framework are defined (`NET8_0`, `NET6_0_OR_GREATER`, `NETSTANDARD2_0`, `NET472`,
  `TRACE`, `RELEASE`...), so `#if` branches match what the consuming project compiles. MSBuild targets of the package
  never run: add the symbols they define with [`defineConstants`](configuration.md#packages).
- `internal` and `protected internal` types, members and accessors are documented, as well as top-level types without
  accessibility modifier: the consuming project can use them because the files are part of it. Declarations show
  their accessibility (`internal sealed class Options`, `public string Name { get; internal set; }`), and type pages
  say the code is compiled into the consuming project. `private` and `private protected` symbols are not documented.
- The sources are compiled against the compile-time assemblies of the configured `packages` and `references`, for the
  nearest framework (for `contentFiles/{lang}/any/`, the framework of the running .NET runtime). Packages the sources
  use without declaring them as dependencies must be configured too. When none of them provides a core library (`System.Runtime.dll`, `netstandard.dll` or
  `mscorlib.dll`), the assemblies of the .NET runtime the tool runs on are used. To document against the exact
  framework, add a targeting pack such as `Microsoft.NETCore.App.Ref` to `references`.
- Errors in method bodies don't matter. Errors in declarations, usually a dependency that is not configured, skip the
  framework (`DG2014`).
- The assembly is named after the package; use `{PackageId}.dll` in `assemblies` filters.
- Example, for [Meziantou.Framework.Roslyn](https://www.nuget.org/packages/Meziantou.Framework.Roslyn), whose sources
  use Roslyn and whose targets define `ROSLYN_*` symbols from the Roslyn version of the project:

  ```json
  "packages": [
    { "id": "Meziantou.Framework.Roslyn", "version": "1.2.2", "defineConstants": [ "ROSLYN_4_12_OR_GREATER", "ROSLYN_5_6_OR_GREATER" ] }
  ],
  "references": [
    { "id": "Microsoft.CodeAnalysis.Common", "version": "5.9.0" },
    { "id": "Microsoft.CodeAnalysis.CSharp", "version": "5.9.0" }
  ]
  ```

- Not supported: preprocessed files (`.cs.pp`), the `buildAction` of the nuspec `contentFiles` element (every `.cs` file is
  compiled), `<include>` in documentation comments, and source links.

## Target frameworks

The frameworks shown are the asset folders of the package (`net8.0`, `netstandard2.0`...). They are not a compatibility
matrix: an API available in `netstandard2.0` is shown as available in `netstandard2.0`, not in `net8.0`, even though a
`net8.0` project can use it.

Each type has a single page. Members that are not available in every framework show their frameworks. When a declaration
differs between frameworks (signature, nullability, constraints, attributes, base types), each declaration is displayed
with its frameworks. With JavaScript, a filter hides the members of other frameworks; the URL never changes.

## Visibility

Documented: public types; public, protected and protected internal members of visible types; explicit interface
implementations of visible interfaces, and the internal symbols of [source packages](#source-packages). Not
documented: other internal and private protected symbols, protected members of
sealed types or structs, accessors (documented with their property or event), record `EqualityContract`, and
compiler-generated types. Use [`api.include` and `api.exclude`](configuration.md#api) to filter further.

## URLs and anchors

| Page | URL |
| --- | --- |
| Package API reference | `/api/{package}/`, where `{package}` is the lowercase package ID |
| Namespace | `/api/{package}/namespaces/{Namespace}.html` |
| Type | `/api/{package}/{Namespace}.{Type}.html`, generic arity as `-N` (`Tree-1.Node.html`) |
| Member | type page + `#` + the member part of the documentation ID (`#Send(System.String)`, `#%23ctor`) |
| Overloads | type page + `#{Name}-overloads` |

URLs only depend on package identifiers, namespaces and type names. They do not change when titles change, when the
order of the inputs changes, or when target frameworks are added. Two types mapping to the same file (for instance in
two assemblies of a package, or names differing only by case) are disambiguated with the assembly name, then a hash
(`DG4004`); such URLs are only stable while the set of colliding types is unchanged.

## Missing inputs

| Missing | Behavior |
| --- | --- |
| XML documentation | Pages show declarations; members show "No documentation is available" (`DG3002`). |
| PDB or Source Link | No source link (`DG3003`). |
| Dependency documentation for `inheritdoc` | A note explains that the inherited documentation is not available (`DG6003`). |

## Source links

Source links are best effort. A PDB embedded in the assembly, next to it in the package, or downloaded from a configured
symbol server is used only when its identifier matches the assembly. Symbols of reference assemblies are matched to the
implementation by documentation ID. Links target GitHub at the commit recorded by Source Link:

- methods, properties and events link to the first line of their body (a GitHub icon and "code", next to the member
  name); the declaration may be a few lines above;
- types link to the file of their first method (a GitHub icon and "file", next to the page title);
- fields and members without code have no link;
- generated files that are not mapped by Source Link, and hosts other than GitHub (`DG3006`), have no link.

## External references

References to symbols outside the configured packages link to Microsoft Learn when they are in a `System` or
`Microsoft` namespace, and are displayed as code otherwise. Configure [`xrefs`](configuration.md#xrefs) to link other
namespaces or to change these links.
