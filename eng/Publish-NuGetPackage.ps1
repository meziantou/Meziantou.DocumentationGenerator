#Requires -Version 7
<#
.SYNOPSIS
Publishes the validated package to nuget.org. Reruns are safe: when the version is already published with the same
content, nothing is pushed; when it is published with different content, the script fails instead of reporting success.
#>
param(
    [Parameter(Mandatory)] [string] $PackagePath,
    [string] $ApiKey,
    [string] $Source = 'https://api.nuget.org/v3/index.json',
    [string] $FlatContainer = 'https://api.nuget.org/v3-flatcontainer',
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-ContentHashes([string] $path) {
    # nuget.org adds a repository signature, so the signature file is excluded from the comparison
    $archive = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        $archive.Entries |
            Where-Object { $_.FullName -ne '.signature.p7s' -and -not $_.FullName.EndsWith('/') } |
            Sort-Object FullName |
            ForEach-Object {
                $stream = $_.Open()
                try { "$($_.FullName):$([Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream)))" } finally { $stream.Dispose() }
            }
    }
    finally {
        $archive.Dispose()
    }
}

$package = (Resolve-Path $PackagePath).Path
$archive = [System.IO.Compression.ZipFile]::OpenRead($package)
try {
    $reader = [System.IO.StreamReader]::new(($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' -and $_.FullName -notlike '*/*' } | Select-Object -First 1).Open())
    [xml] $nuspec = $reader.ReadToEnd()
    $reader.Dispose()
}
finally {
    $archive.Dispose()
}

$id = $nuspec.package.metadata.id
$version = $nuspec.package.metadata.version
$lowerId = $id.ToLowerInvariant()
$lowerVersion = $version.ToLowerInvariant()

$published = $false
try {
    $index = Invoke-RestMethod "$FlatContainer/$lowerId/index.json"
    $published = $index.versions -contains $lowerVersion
}
catch {
    if ($_.Exception.Response.StatusCode -ne 404) { throw }
}

if ($published) {
    $existing = Join-Path ([System.IO.Path]::GetTempPath()) "$lowerId.$lowerVersion.published.nupkg"
    Invoke-WebRequest "$FlatContainer/$lowerId/$lowerVersion/$lowerId.$lowerVersion.nupkg" -OutFile $existing
    $same = (Compare-Object (Get-ContentHashes $existing) (Get-ContentHashes $package)).Count -eq 0
    Remove-Item $existing
    if (-not $same) {
        throw "$id $version is already published with different content. Versions cannot be replaced: publish a new version."
    }

    Write-Host "$id $version is already published with the same content; nothing to do."
    exit 0
}

if ($DryRun) {
    Write-Host "Dry run: $id $version would be pushed to $Source."
    exit 0
}

if ([string]::IsNullOrEmpty($ApiKey)) { throw 'An API key is required to publish' }
dotnet nuget push $package --api-key $ApiKey --source $Source
Write-Host "$id $version was published."
