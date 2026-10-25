#Requires -Version 7
<#
.SYNOPSIS
Verifies the tool package before it is published: identity, version, command name, framework, packaged dependencies,
license, README and schema.
#>
param(
    [Parameter(Mandatory)] [string] $PackagePath,
    [Parameter(Mandatory)] [string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$errors = [System.Collections.Generic.List[string]]::new()
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $PackagePath))
try {
    $entries = $archive.Entries | ForEach-Object { $_.FullName }
    function Read-Entry([string] $name) {
        $entry = $archive.GetEntry($name)
        if ($null -eq $entry) { return $null }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }

    [xml] $nuspec = Read-Entry 'Meziantou.DocumentationGenerator.nuspec'
    if ($null -eq $nuspec) { throw 'The package has no Meziantou.DocumentationGenerator.nuspec' }
    $metadata = $nuspec.package.metadata
    if ($metadata.id -ne 'Meziantou.DocumentationGenerator') { $errors.Add("Unexpected package id '$($metadata.id)'") }
    if ($metadata.version -ne $ExpectedVersion) { $errors.Add("Unexpected version '$($metadata.version)', expected '$ExpectedVersion'") }
    if ($metadata.packageTypes.packageType.name -ne 'DotnetTool') { $errors.Add('The package is not a .NET tool') }
    if ($metadata.license.'#text' -ne 'MIT') { $errors.Add('The license expression must be MIT') }
    if ($metadata.readme -ne 'README.md' -or 'README.md' -notin $entries) { $errors.Add('The README is not packaged') }
    if ([string]::IsNullOrEmpty($metadata.repository.url)) { $errors.Add('The repository URL is missing') }
    if ($metadata.dependencies.group | Where-Object { $_.dependency }) { $errors.Add('A tool package must not declare dependencies') }
    if ('schema/docgen.schema.json' -notin $entries) { $errors.Add('The configuration schema is not packaged') }

    $toolDirectory = 'tools/net11.0/any/'
    [xml] $settings = Read-Entry "${toolDirectory}DotnetToolSettings.xml"
    if ($null -eq $settings -or $settings.DotNetCliTool.Commands.Command.Name -ne 'Meziantou.DocumentationGenerator') { $errors.Add('The command name must be Meziantou.DocumentationGenerator') }

    $runtimeConfig = Read-Entry "${toolDirectory}Meziantou.DocumentationGenerator.runtimeconfig.json" | ConvertFrom-Json
    $framework = @($runtimeConfig.runtimeOptions.framework) + @($runtimeConfig.runtimeOptions.frameworks) | Where-Object { $_ }
    if (-not ($framework | Where-Object { $_.name -eq 'Microsoft.NETCore.App' -and $_.version -like '11.*' })) { $errors.Add('The tool must target the .NET 11 runtime') }
    if ($framework | Where-Object { $_.name -eq 'Microsoft.AspNetCore.App' }) { $errors.Add('The tool must not require the ASP.NET Core shared framework') }

    foreach ($assembly in 'Meziantou.DocumentationGenerator.dll', 'Microsoft.AspNetCore.Components.Web.dll', 'Microsoft.AspNetCore.Components.dll', 'Meziantou.Framework.PublicApiGenerator.dll', 'Meziantou.Framework.Markdown.dll', 'Meziantou.Framework.SyntaxHighlighting.dll', 'NuGet.Protocol.dll', 'System.CommandLine.dll') {
        if ("$toolDirectory$assembly" -notin $entries) { $errors.Add("'$assembly' is not packaged") }
    }
}
finally {
    $archive.Dispose()
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Host "The package $PackagePath is valid"
