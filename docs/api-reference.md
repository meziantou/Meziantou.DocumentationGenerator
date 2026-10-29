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

## Target frameworks

The frameworks shown are the asset folders of the package (`net8.0`, `netstandard2.0`...). They are not a compatibility
matrix: an API available in `netstandard2.0` is shown as available in `netstandard2.0`, not in `net8.0`, even though a
`net8.0` project can use it.

Each type has a single page. Members that are not available in every framework show their frameworks. When a declaration
differs between frameworks (signature, nullability, constraints, attributes, base types), each declaration is displayed
with its frameworks. With JavaScript, a filter hides the members of other frameworks; the URL never changes.

## Visibility

Documented: public types; public, protected and protected internal members of visible types; explicit interface
implementations of visible interfaces. Not documented: internal and private protected symbols, protected members of
sealed types or structs, accessors (documented with their property or event), record `EqualityContract`, and
compiler-generated types. Use [`api.include` and `api.exclude`](configuration.md#api) to filter further.

## URLs and anchors

| Page | URL |
| --- | --- |
| API reference | `/{product}/api/` |
| Namespace | `/{product}/api/namespaces/{Namespace}.html` |
| Type | `/{product}/api/{Namespace}.{Type}.html`, generic arity as `-N` (`Tree-1.Node.html`) |
| Member | type page + `#` + the member part of the documentation ID (`#Send(System.String)`, `#%23ctor`) |
| Overloads | type page + `#{Name}-overloads` |

URLs only depend on product identifiers, namespaces and type names. They do not change when titles change, when the
order of the inputs changes, or when target frameworks are added. Two types mapping to the same file (for instance in
two packages of a product, or names differing only by case) are disambiguated with the assembly name, then a hash
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

- methods, properties and events link to the first line of their body (labeled "Source"); the declaration may be a few
  lines above;
- types link to the file of their first method (labeled "Source file");
- fields and members without code have no link;
- generated files that are not mapped by Source Link, and hosts other than GitHub (`DG3006`), have no link.

## External references

References to symbols outside the configured packages are displayed as code. Configure [`xrefs`](configuration.md#xrefs)
to link them, for instance to Microsoft Learn for `System` types.
