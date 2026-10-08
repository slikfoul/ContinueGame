param([string]$Profile = 'Default')
$ErrorActionPreference = 'Stop'

# BuildPackage builds the DLL and verifies the complete Thunderstore archive.
& (Join-Path $PSScriptRoot 'BuildPackage.ps1')
# Register the same package locally; this never publishes to Thunderstore.
& (Join-Path $PSScriptRoot 'RegisterLocalMod.ps1') -Profile $Profile
