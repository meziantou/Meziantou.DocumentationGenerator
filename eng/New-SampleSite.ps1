#Requires -Version 7
<#
.SYNOPSIS
Installs the tool from a local package into a clean temporary tool manifest, and generates the sample site with it. The
source checkout is only used for the sample content, never to run the tool.
#>
param(
    [Parameter(Mandatory)] [string] $PackageDirectory,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [string] $BasePath = '/',
    [string] $SiteUrl
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("docgen-sample-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $packages = (Resolve-Path $PackageDirectory).Path
    $toolDirectory = Join-Path $work 'tool'
    New-Item -ItemType Directory -Path $toolDirectory | Out-Null
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packages" />
  </packageSources>
</configuration>
"@ | Set-Content (Join-Path $toolDirectory 'nuget.config')

    $env:NUGET_PACKAGES = Join-Path $work 'nuget-packages'
    $env:DOTNET_CLI_HOME = Join-Path $work 'cli-home'
    Push-Location $toolDirectory
    try {
        dotnet new tool-manifest
        dotnet tool install --local Meziantou.DocumentationGenerator --version $Version
    }
    finally {
        Pop-Location
    }

    # The sample library is built from source with the public NuGet feed, independently of the tool installation
    Remove-Item Env:NUGET_PACKAGES
    $sample = Join-Path $work 'sample'
    Copy-Item -Recurse (Join-Path $root 'samples/basic') $sample
    Copy-Item (Join-Path $root 'global.json') $work
    Copy-Item (Join-Path $root 'samples/Directory.Build.props') $work
    Copy-Item (Join-Path $root 'samples/Directory.Packages.props') $work
    dotnet pack (Join-Path $sample 'src/Sample.Library') --output (Join-Path $sample 'artifacts/packages')

    $arguments = @('build', '--config', (Join-Path $sample 'docgen.json'), '--output', [System.IO.Path]::GetFullPath($OutputDirectory), '--base-path', $BasePath, '--warnaserror')
    if ($SiteUrl) { $arguments += @('--site-url', $SiteUrl) }
    $env:NUGET_PACKAGES = Join-Path $work 'nuget-packages'
    Push-Location $toolDirectory
    try {
        dotnet tool run Meziantou.DocumentationGenerator -- @arguments
    }
    finally {
        Pop-Location
    }
}
finally {
    Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    Remove-Item Env:DOTNET_CLI_HOME -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
