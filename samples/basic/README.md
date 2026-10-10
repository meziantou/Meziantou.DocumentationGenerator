# Basic sample

A product built from a local package and Markdown pages. Build it from the repository root:

```bash
dotnet pack samples/basic/src/Sample.Library --output samples/basic/artifacts/packages
dotnet run --project src/Meziantou.DocumentationGenerator -- build --config samples/basic/docgen.json
```

The site is generated in `samples/basic/artifacts/site`. Serve it with any static file server, for example:

```bash
dotnet tool run dotnet-serve --directory samples/basic/artifacts/site
```
