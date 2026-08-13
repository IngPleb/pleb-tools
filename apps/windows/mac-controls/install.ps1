[CmdletBinding()]
param(
    [switch]$Apply,
    [ValidateSet('Standard', 'Swapped')]
    [string]$ModifierLayout
)

$setupArguments = @{
    Action = 'Install'
    Apply = $Apply
}
if ($PSBoundParameters.ContainsKey('ModifierLayout')) {
    $setupArguments.ModifierLayout = $ModifierLayout
}

& (Join-Path $PSScriptRoot 'setup.ps1') @setupArguments
if (-not $?) { exit 1 }
exit 0
