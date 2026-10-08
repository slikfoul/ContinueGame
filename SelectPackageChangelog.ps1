param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$ProjectDirectory = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Invalid package version.' }
$publicPackage = ([Version]$Version).Major -ge 1
$fileName = if ($publicPackage) { 'CHANGELOG.release.md' } else { 'CHANGELOG.md' }
$changelogPath = Join-Path $ProjectDirectory $fileName
if (-not (Test-Path -LiteralPath $changelogPath)) { throw "Required changelog is missing: $fileName" }
$changelogText = Get-Content -LiteralPath $changelogPath -Raw
$changelogVersions = @([regex]::Matches($changelogText, '(?m)^# ([0-9]+\.[0-9]+\.[0-9]+)\s*$') | ForEach-Object { $_.Groups[1].Value })
if (-not $changelogVersions.Count -or $changelogVersions[0] -cne $Version) { throw 'Changelog must start with the current manifest version.' }
$lastChangelogVersion = $null
foreach ($changelogVersion in $changelogVersions) {
    $parsedChangelogVersion = [Version]$changelogVersion
    if ($publicPackage -and $parsedChangelogVersion.Major -lt 1) { throw 'Public package changelog must not contain development versions.' }
    if ($null -ne $lastChangelogVersion -and $parsedChangelogVersion -ge $lastChangelogVersion) { throw 'Changelog versions must be unique and ordered newest first.' }
    $lastChangelogVersion = $parsedChangelogVersion
}
Write-Output $changelogPath
