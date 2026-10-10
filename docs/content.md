# Authoring content

## Markdown pages

Every `.md` file of `content.root` is a page, except files matching `content.exclude`. The output path mirrors the
source path: `guides/getting-started.md` becomes `guides/getting-started.html`, and `guides/index.md` becomes
`guides/index.html`, linked as `guides/`. The root `index.md` is the home page of the site; without it, the home page
lists the products.

### Front matter

```markdown
---
title: Getting started
description: Install the package and send a first message.
uid: getting-started
slug: start
---
```

| Key | Description |
| --- | --- |
| `title` | Page title. Default: the first level-1 heading, then the file name. A leading level-1 heading is not repeated in the page. |
| `description` | Meta description and social preview text. Default: the first paragraph. |
| `uid` | Identifier that `toc.yml` entries can reference with `uid:`. |
| `slug` | Output path relative to the content root, without extension (`start` produces `start.html`). Use it to keep URLs stable when a file moves. |

Other keys are ignored. Front matter is parsed as YAML with bounded nesting; anchors and aliases are rejected.

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

### Links

| Link | Result |
| --- | --- |
| `[guide](guides/intro.md#install)` | Link to the generated page, fragment and query string kept. |
| `[folder](guides/)` | Link to `guides/index.md`. |
| `![logo](images/logo.png)` | The file is copied to the same relative path in the output. |
| `[API](cref:T:Contoso.Client)` | Link to the page of a type. |
| `[](cref:Contoso.Client.Send)` | Link with the display name of the symbol (`Client.Send`) as text. |
| `https://...`, `mailto:` | Kept as is. |
| `javascript:`, `data:`, other schemes | Removed (`DG4008`). |

Relative links are resolved against the Markdown file. Targets outside the content root are not published (`DG5009`),
missing files are reported (`DG5001`, `DG5008`) and their link is removed.

### `cref:` syntax

- `cref:T:Contoso.Json.JsonReader`: a compiler documentation ID (`T:` type, `M:` method, `P:` property, `F:` field,
  `E:` event, `N:` namespace).
- `cref:Contoso.Json.JsonReader`: without prefix, types are searched first, then methods, properties, fields, events and
  namespaces.
- `cref:M:Contoso.Json.JsonReader.Read`: without parameters, links to the overloads of the method.
- `cref:Contoso.Collections.Tree{T}` or `cref:Contoso.Collections.Tree<T>.Node`: generic types.
- `cref:Contoso.Json!T:Utils.Guard`: restricts the lookup to a package when several packages define the symbol.

References are resolved first in the current assembly, then in the current product, then in every package. Several
matches at the same level are ambiguous (`DG4002`); unresolved references are displayed as code (`DG4001`). Symbols
outside the configured packages are linked only through [`xrefs`](configuration.md#xrefs).

### Raw HTML

Raw HTML is allowed and sanitized with an allow-list: scripts, styles, frames, objects, forms, event handlers, `style`
attributes and unsafe URL schemes are removed. Relative `href` and `src` attributes are resolved like Markdown links.

## Assets

Files referenced by Markdown links and images are copied automatically. Use `content.assets` for files that are not
referenced, such as `CNAME`. Package README assets are copied from the package to `{product}/_readme/`. A README link to a
file that is not in the package targets the GitHub repository at the commit recorded in the package metadata; without
repository commit, the link is removed (`DG3004`), never guessed.

## Table of contents

`toc.yml` follows a subset of the [DocFX table of contents](https://dotnet.github.io/docfx/docs/table-of-contents.html):

```yaml
- name: Home
  href: index.md
- name: Guides
  expanded: true
  items:
    - href: guides/getting-started.md
    - name: Advanced
      href: advanced/        # includes advanced/toc.yml, links to its first article
- name: Reference
  href: api:contoso         # generated API navigation of the "contoso" product
- uid: T:Contoso.Json.JsonReader
- name: GitHub
  href: https://github.com/contoso/json
```

- Root shapes: a sequence, or a mapping with `items`.
- Keys: `name`, `href`, `uid`, `items`, `expanded` (and the DocFX aliases `topicHref` and `topicUid`). Other keys are
  ignored (`DG5007`).
- `href` targets a Markdown page (optionally with a fragment), an `http(s)` URL, a nested `toc.yml`, a directory
  containing a `toc.yml`, or `api:{productId}`.
- `uid` targets a page `uid` from front matter or an API symbol.
- Entries without `href` or `uid` are non-clickable groups.
- Display names: `name`, then the page title, then the symbol name.
- Paths are relative to the `toc.yml` that declares them.
- Order is preserved. Generated API navigation is sorted by namespace, then type, ordinally.
- A page listed several times uses its first occurrence in depth-first order for the sidebar and breadcrumbs. Pages not
  listed are still generated and searchable (`DG5005`).
- Missing targets (`DG5006`), cycles (`DG5004`), malformed entries and unsafe links (`DG5003`) are errors.

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
