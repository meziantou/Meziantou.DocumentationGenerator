#Requires -Version 7
<#
.SYNOPSIS
Writes the release notes and the checksums of the release artifacts.
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $PackagePath,
    [Parameter(Mandatory)] [string] $OutputPath,
    [string] $Commit = $env:GITHUB_SHA,
    [string] $Repository = $env:GITHUB_REPOSITORY
)

$ErrorActionPreference = 'Stop'
$package = Get-Item $PackagePath
$hash = (Get-FileHash -Algorithm SHA256 $package.FullName).Hash.ToLowerInvariant()
$repositoryUrl = if ($Repository) { "https://github.com/$Repository" } else { 'https://github.com/meziantou/Meziantou.DocumentationGenerator' }

@"
# Meziantou.DocumentationGenerator $Version

Install or update the tool:

``````bash
dotnet tool install --global Meziantou.DocumentationGenerator --version $Version
``````

## Requirements and compatibility

- Requires the .NET 11 runtime. While .NET 11 is in preview, the tool targets the preview runtime pinned in ``global.json``.
- Configuration format version 1. Unknown configuration properties are errors.
- Generated URLs follow the rules documented in [docs/api-reference.md]($repositoryUrl/blob/$Commit/docs/api-reference.md#urls-and-anchors).

## Supported behavior

See the [README]($repositoryUrl/blob/$Commit/README.md) and the [documentation]($repositoryUrl/tree/$Commit/docs).

## Limitations

Versioned sites, API diffs, full-text search, localization, custom themes and plugins, watch mode and complete DocFX
compatibility are not supported. Source links are best effort and limited to GitHub.

## Provenance

| Artifact | SHA-256 |
| --- | --- |
| $($package.Name) | ``$hash`` |

Built from commit ``$Commit``.
"@ | Set-Content -Path $OutputPath -Encoding utf8NoBOM

"$hash  $($package.Name)" | Set-Content -Path (Join-Path (Split-Path $OutputPath -Parent) 'SHA256SUMS.txt') -Encoding utf8NoBOM
