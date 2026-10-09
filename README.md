# Meziantou.DocumentationGenerator

A .NET tool that generates a fully static documentation site from NuGet packages and Markdown pages.

## Building

The repository requires the .NET 11 SDK pinned in `global.json` (a prerelease SDK while .NET 11 is in preview).

```bash
dotnet build Meziantou.DocumentationGenerator.slnx
dotnet test --solution Meziantou.DocumentationGenerator.slnx
dotnet pack src/Meziantou.DocumentationGenerator/Meziantou.DocumentationGenerator.csproj --output artifacts/package
```
