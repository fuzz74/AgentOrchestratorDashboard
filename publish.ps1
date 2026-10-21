#requires -Version 7.2
<#
.SYNOPSIS
    Publishes a Release build of OrchDash to the publish folder next to this script.
.DESCRIPTION
    Empties the publish folder first, so no file from an earlier publish is left behind, and then
    runs dotnet publish on src\OrchDash in Release. The build is framework-dependent: it needs the
    .NET 10 runtime on the PC, and it adds no runtime section to packages.lock.json. Start the app
    with publish\OrchDash.exe <repo>. The publish folder is in .gitignore.
.EXAMPLE
    .\publish.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\OrchDash\OrchDash.csproj'
$output = Join-Path $PSScriptRoot 'publish'

if (Test-Path $output) {
    Remove-Item -Recurse -Force $output
}

dotnet publish $project --configuration Release --output $output
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Host "Published to $output. Start it with: $(Join-Path $output 'OrchDash.exe') <repo>"
