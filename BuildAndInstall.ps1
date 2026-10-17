param([string]$Profile = 'Default', [string]$DataRoot = (Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim'))
$ErrorActionPreference = 'Stop'

# BuildPackage builds the DLL and verifies the complete Thunderstore archive.
& (Join-Path $PSScriptRoot 'BuildPackage.ps1')
# Register the same package locally; this never publishes to Thunderstore.
& (Join-Path $PSScriptRoot 'RegisterLocalMod.ps1') -Profile $Profile -DataRoot $DataRoot
