[CmdletBinding()]
param([switch]$Apply)

& (Join-Path $PSScriptRoot 'setup.ps1') -Action Uninstall -Apply:$Apply
if (-not $?) { exit 1 }
exit 0
