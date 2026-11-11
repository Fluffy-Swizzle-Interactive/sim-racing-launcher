# Prints the release notes for one version from CHANGELOG.md: everything under
# its "## vX.Y.Z" heading, up to the next "## " heading. Fails if the version
# has no section, so a release can't go out without notes.
#
# Usage: changelog-section.ps1 -Version v1.1.0 [-Path CHANGELOG.md]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Path = "CHANGELOG.md"
)

$ErrorActionPreference = "Stop"
$heading = "## $Version"
$lines = Get-Content -Path $Path -Encoding utf8

$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq $heading) { $start = $i + 1; break }
}
if ($start -lt 0) {
    Write-Error "CHANGELOG.md has no '$heading' section. Add one before tagging $Version."
    exit 1
}

$end = $lines.Count
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i].StartsWith("## ")) { $end = $i; break }
}

$section = ($lines[$start..($end - 1)] -join "`n").Trim()
if (-not $section) {
    Write-Error "The '$heading' section in CHANGELOG.md is empty."
    exit 1
}
$section
