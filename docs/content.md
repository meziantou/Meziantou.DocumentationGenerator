# Authoring content

## Markdown pages

Every `.md` file of `content.root` is a page, except files matching `content.exclude`. Files such as `AGENTS.md`
are not skipped automatically; exclude them explicitly (`"exclude": ["**/AGENTS.md"]`).

The output path is computed from titles, not from the source path. Each folder contributes a segment: the `slug` of its
`_folder.yml`, otherwise its slugified title. The page contributes the last segment: its front matter `slug`, otherwise
its slugified title. With the titles "Imaging", "Loading and saving" and "Loading images",
`01-Imaging/05-LoadingAndSaving/02-LoadingImages.md` becomes `imaging/loading-and-saving/loading-images.html`.
Slugifying removes accents, lowercases, replaces runs of characters other than ASCII letters and digits with `-`, and
trims `-`. Two pages, or two folders, with the same output path are an error (`DG4005`), and only the root `index.md`
can map to `index.html`.

The page title is the front matter `title`, then a leading level-1 heading, then the file name without its number
prefix (`02-LoadingImages.md` → `LoadingImages`).

The root `index.md` is the home page of the site (`index.html`). Other `index.md` files are ordinary pages. Without a
root `index.md`, the home page lists the top-level folders (title and description of their `_folder.yml`, linking to
their first page) and the top-level pages (title and description). This generated home page is not in the navigation
unless `site.homeTitle` is set. A custom `index.md` can still show these lists with
[`<SectionList />` and `<PackageList />`](#generated-lists).

### Front matter

```markdown
---
title: Getting started
description: Install the package and send a first message.
slug: start
---
```

| Key | Description |
| --- | --- |
| `title` | Page title. Default: the first level-1 heading, then the file name. A leading level-1 heading is not repeated in the page. |
| `description` | Meta description and social preview text. Default: the first paragraph. |
| `slug` | Last segment of the output path, instead of the slugified title (`start` produces `{folders}/start.html`). A single segment: no `/` and no `.html`. Use it to keep URLs stable when the title changes. An invalid slug is an error (`DG5012`). |

Other keys are ignored. Front matter is parsed as YAML with bounded nesting; anchors and aliases are rejected.

With [`content.editUrl`](configuration.md#content), each page shows an "edit" link next to its title that opens its
Markdown file in the editor, such as GitHub.

### Supported syntax

CommonMark, pipe tables, task lists, footnotes, auto links, strikethrough and other emphasis extras, heading
identifiers (GitHub rules: `## Install` gets the `install` identifier, duplicates get `-1`, `-2`), and GitHub alerts:

```markdown
> [!NOTE]
> Useful information.
```

Supported alert kinds are `NOTE`, `TIP`, `IMPORTANT`, `WARNING` and `CAUTION`.

DocFX-specific syntax (`[!INCLUDE]`, `:::` zones, `@uid` links, tabs) is not supported and is displayed as text.

### Code

Fenced code blocks are highlighted when the site is generated, with highlight.js-compatible grammars:

````markdown
```csharp
var client = new Client();
```
````

Common aliases are supported (`cs`, `c#`, `sh`, `ps1`, `yml`, `js`, `ts`...). `text`, `console` and `output` are
displayed as plain text. Unknown languages are displayed as plain text with the info diagnostic `DG5010`.

### Components

These elements are replaced when the site is generated. Names and attributes are case-insensitive.

| Element | Output |
|---|---|
| `<InstallNuGetPackage Id="Contoso.Client" />` | `dotnet package add Contoso.Client` |
| `<InstallNuGetPackage Id="Contoso.Client, Contoso.Extensions" />` | One `dotnet package add` command per package |
| `<InstallDotnetTool Id="Contoso.Tool" />` | `dotnet tool install --global Contoso.Tool` |
| `<NuGetReference Id="Contoso.Client" />` | A link to `https://www.nuget.org/packages/Contoso.Client`, with the NuGet icon |
| `<DependencyGraph Packages="*" />` | A graph of the dependencies between the documented packages (see [Dependency graphs](#dependency-graphs)) |
| `<SectionList />` | The top-level folders and pages, like the generated home page (see [Generated lists](#generated-lists)) |
| `<PackageList />` | The documented packages (see [Generated lists](#generated-lists)) |

Attributes:

- `Id` (required): the package identifier. For `InstallNuGetPackage`, it also accepts a
  comma-separated collection of package identifiers.
  Whitespace around each identifier is ignored; empty or invalid identifiers are errors. Commands keep the input order.
- `Version` (optional): adds `--version <version>` to the command, or links to that version on nuget.org.
  When using a collection in `Id`, the version applies to every package.
- `Local="true"` (`InstallDotnetTool` only): installs the tool in the local tool manifest (`dotnet tool install Contoso.Tool`).

Put `InstallNuGetPackage` and `InstallDotnetTool` on their own line, separated from other text by a blank line, to get a
code block with a copy button. A collection renders as one block with a command on each line. Inside a paragraph, they
render as inline code, with collection commands separated by `; `. `NuGetReference` can be used anywhere in
text. An invalid element (missing or invalid `Id` or `Version`, unknown attribute) is removed
and reported as `DG5013`.

#### Dependency graphs

`<DependencyGraph />` draws how the packages of the site depend on each other, from the NuGet dependencies declared in
their `.nuspec`:

```markdown
<DependencyGraph Packages="*" />

<DependencyGraph Packages="Contoso.*, Fabrikam.Core" External="true" Direction="TopDown" />
```

- `Packages` (required): comma-separated package identifiers of the packages configured in `packages`. `*` matches any
  characters, and the comparison is case-insensitive. The graph also contains the documented packages that they depend
  on, directly or not. A pattern that matches no package is reported as `DG5014`.
- `External="true"` (optional): also draws the dependencies that the site does not document, such as
  `Microsoft.Extensions.Logging`, with a dashed border. They link to nuget.org.
- `Direction` (optional): `LeftRight` (default) or `TopDown`. Arrows go from a package to its dependencies.

The graph is generated as an SVG image when the site is built, so it is displayed without JavaScript. Each package
links to its page, or to nuget.org when it has no public API. A dependency declared by every target framework is drawn
once; the tooltip of an arrow lists the target frameworks when only some of them declare it. Packages without any
dependency in the graph are displayed below it, and dependencies that form a cycle are drawn with a dashed red line.

In the browser, selecting a package highlights what it depends on, directly or not, in green, and what depends on it in
orange; selecting it again opens its page, and <kbd>Esc</kbd> clears the selection. The toolbar filters packages by
name and zooms; <kbd>Ctrl</kbd> + wheel also zooms and dragging the background scrolls a large graph.

The element must be on its own line, separated from other text by a blank line.

#### Generated lists

`<SectionList />` and `<PackageList />` insert the lists that the generator builds, so a custom home page can mix
authored text with them and stay up to date:

```markdown
# Contoso

Contoso is a set of libraries to send messages.

## Guides

<SectionList />

## Packages

<PackageList Packages="Contoso.*" Layout="Table" />
```

- `<SectionList />` lists the top-level folders (title and description of their `_folder.yml`, linking to their first
  page) and the top-level pages (title and description), in name order. With the default layout, it is the list of the
  generated home page.
- `<PackageList />` lists the packages that have a public API, ordered by identifier, with their version and
  description. Each package links to its page.
  - `Packages` (optional, default `*`): comma-separated package identifiers. `*` matches any characters, and the
    comparison is case-insensitive. A pattern that matches no package, or a site without package, is reported as
    `DG5015`.
- `Layout` (optional, both elements): `Cards` (default, a grid of cards), `List` (one line per entry) or `Table`
  (like the API reference page). Other values are reported as `DG5013`.

The elements must be on their own line, separated from other text by a blank line.

### Links

| Link | Result |
| --- | --- |
| `[guide](guides/intro.md#install)` | Link to the generated page, fragment and query string kept. |
| `[folder](03-Guides/)` | Link to the first page of the folder in name order. The content root links to the home page. |
| `![logo](images/logo.png)` | The file is copied next to the pages of its folder (see [Assets](#assets)). |
| `[API](cref:T:Contoso.Client)` | Link to the page of a type. |
| `[](cref:Contoso.Client.Send)` | Link with the display name of the symbol (`Client.Send`) as text. |
| `https://...`, `mailto:` | Kept as is. |
| `javascript:`, `data:`, other schemes | Removed (`DG4008`). |

Relative links are resolved against the Markdown file. Targets outside the content root are not published (`DG5009`).
A link to a Markdown page that does not exist or is excluded by `content.exclude`, or to a folder without pages
(`DG5001`), and a link or image to a missing file (`DG5008`) are errors: the link is removed and the build fails. The
generated HTML is validated afterwards: every internal `href`, `src` and fragment must exist (`DG4003`). External
`http(s)` links are not checked.

### `cref:` syntax

- `cref:T:Contoso.Json.JsonReader`: a compiler documentation ID (`T:` type, `M:` method, `P:` property, `F:` field,
  `E:` event, `N:` namespace).
- `cref:Contoso.Json.JsonReader`: without prefix, types are searched first, then methods, properties, fields, events and
  namespaces.
- `cref:M:Contoso.Json.JsonReader.Read`: without parameters, links to the overloads of the method.
- `cref:Contoso.Collections.Tree{T}` or `cref:Contoso.Collections.Tree<T>.Node`: generic types.
- `cref:Contoso.Json!T:Utils.Guard`: restricts the lookup to a package when several packages define the symbol.
  Namespaces have a single page for every package, so they never need a package; `cref:Contoso.Json!N:Contoso` is
  accepted and only requires that the package contributes to the namespace.

References are resolved first in the current assembly, then in the current package, then in every package. Several
matches at the same level are ambiguous (`DG4002`); unresolved references are an error (`DG4001`). Symbols
outside the configured packages are linked only through [`xrefs`](configuration.md#xrefs) (by default, `System` and
`Microsoft` namespaces to Microsoft Learn).

To check authored documentation without its packages, set `diagnostics.allowInvalidCref` to `true` or pass
`--allow-invalid-cref`. Unresolved and ambiguous references then produce warnings and display their text without a
link. This also applies to XML `cref` attributes. Explicit diagnostic severity overrides take precedence, and
warnings-as-errors still promotes these warnings to errors.

### Raw HTML

Raw HTML is allowed and sanitized with an allow-list: scripts, styles, frames, objects, forms, event handlers, `style`
attributes and unsafe URL schemes are removed. Relative `href` and `src` attributes are resolved like Markdown links.

## Assets

Files referenced by Markdown links and images are copied automatically. A file is published under the URL of the
nearest folder that contains pages, followed by the rest of its source path, so it sits next to the pages that use it:
`01-Imaging/07-Drawing/images/DrawBadge.png` is published as `imaging/drawing/images/DrawBadge.png` when the folders
are titled "Imaging" and "Drawing". Two files published at the same path are an error (`DG4005`).

Use `content.assets` for files that are not referenced, such as `CNAME`. They are copied to the same path relative to
the content root.

## Navigation

The navigation follows the folder structure of `content.root`:

```text
docs/
  index.md                  # home page, first in the navigation
  01-Introduction.md
  02-GettingStarted.md
  03-Guides/
    _folder.yml
    01-Install.md
    02-Configure.md
    images/logo.png         # no page: not in the navigation
```

```yaml
# 03-Guides/_folder.yml
title: Guides
description: Step-by-step guides.
slug: guides
```

- Files and folders of a folder are interleaved and sorted ordinally by name; number prefixes set the order.
- A folder is a group that links to its first page, in the sidebar and in breadcrumbs. Only folders containing at least
  one page appear.
- `_folder.yml` keys: `title` (display name), `description` (shown on the generated home page), `slug` (URL segment).
  Other keys are ignored. It is parsed as YAML with bounded nesting; anchors and aliases are rejected.
- Without `_folder.yml` or without `title`, the title is the folder name without its number prefix (`03-Guides` →
  `Guides`) and `DG5011` is reported.
- The last section is the generated API reference: an `API reference` entry, linking to `api/index.html`, with the
  namespaces of every package and their types, sorted ordinally.
- The home page is the first entry: the root `index.md`, under its title, or the generated home page when
  [`site.homeTitle`](configuration.md#site) is set. `site.homeTitle` also renames the entry of a root `index.md`.
- Previous and next links only go through authored pages, and the home page when it is in the navigation.

```json
"site": { "title": "Contoso", "homeTitle": "Introduction" }
```

## XML documentation

Supported sections: `summary`, `remarks`, `example`, `param`, `typeparam`, `returns`, `value`, `exception`,
`seealso`. Supported inline elements: `see` (`cref`, `href`, `langword`), `paramref`, `typeparamref`, `c`, `code`
(highlighted, `language` attribute, C# by default), `para`, `list` (`bullet`, `number`, `table`), and the compatibility
tags `b`, `strong`, `i`, `em`, `br`, `a`, `ul`, `ol`, `p`. Unknown tags display their text (`DG6006`). Documentation is
never emitted as raw HTML.

When the documentation of a symbol differs between target frameworks, each version is displayed with its frameworks.

### `inheritdoc`

- `<inheritdoc/>` inherits from the overridden member, the base type, or the implemented interface member (explicit
  implementations first, then the first declared interface; `DG6010` when several interfaces match).
- `<inheritdoc cref="..."/>` inherits from the given symbol.
- `<inheritdoc path="/remarks"/>` restricts the inherited sections. Paths are element steps with optional
  `[@name='value']` predicates; other XPath constructs are rejected (`DG6009`).
- Local sections win; inherited parameter documentation is renamed by position.
- Documentation of dependencies is available only when the dependency is listed in `references`. Missing inherited
  documentation is reported (`DG6003`) and displayed as a note, never silently empty.

### `include`

`<include file="docs.xml" path="doc/members/member[@name='Run']/*"/>` is resolved relative to the XML documentation file,
inside the same package only (`DG5009` otherwise), with the same path subset and bounded depth and size.
