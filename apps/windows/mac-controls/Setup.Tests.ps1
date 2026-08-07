$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("mac-controls-setup-tests-{0}" -f [Guid]::NewGuid().ToString('N'))
$keyboardManager = Join-Path $testRoot 'Keyboard Manager'
New-Item -ItemType Directory -Path $keyboardManager -Force | Out-Null
try {
    [IO.File]::WriteAllText(
        (Join-Path $keyboardManager 'settings.json'),
        '{"properties":{"activeConfiguration":{"value":"default"}}}',
        [Text.UTF8Encoding]::new($false))
    $fixture = @'
{"remapKeys":{"inProcess":[{"originalKeys":"89","newRemapKeys":"90"},{"originalKeys":"90","newRemapKeys":"89"},{"originalKeys":"220","newRemapKeys":"226"},{"originalKeys":"91","newRemapKeys":"162"}]},"remapKeysToText":{"inProcess":[]},"remapShortcuts":{"global":[{"originalKeys":"164;8","exactMatch":false,"operationType":0,"newRemapKeys":"163;8"},{"originalKeys":"164;37","exactMatch":false,"operationType":0,"newRemapKeys":"163;37"},{"originalKeys":"164;39","exactMatch":false,"operationType":0,"newRemapKeys":"163;39"},{"originalKeys":"91;37","exactMatch":false,"newRemapKeys":"36"},{"originalKeys":"91;39","exactMatch":false,"newRemapKeys":"35"}],"appSpecific":[]},"remapShortcutsToText":{"global":[{"originalKeys":"18;74","exactMatch":false,"unicodeText":"'"}],"appSpecific":[]}}
'@
    $profilePath = Join-Path $keyboardManager 'default.json'
    [IO.File]::WriteAllText($profilePath, $fixture, [Text.UTF8Encoding]::new($false))

    . (Join-Path $PSScriptRoot 'setup.ps1') -Action Install -KeyboardManagerDirectory $keyboardManager | Out-Null

    $changeSet = Get-PowerToysChangeSet -ProfilePath $profilePath
    Assert-Equal 4 $changeSet.RemovedKeys.Count 'Owned key count differs.'
    Assert-Equal 5 $changeSet.RemovedShortcuts.Count 'Overlapping shortcut count differs.'
    Assert-Equal 1 $changeSet.RemovedTextMappings.Count 'Owned text shortcut count differs.'
    Assert-Equal 0 $changeSet.RemainingKeys.Count 'Owned keys were not fully transferred.'

    $changeSet.Profile.remapKeys.inProcess = @($changeSet.RemainingKeys)
    $changeSet.Profile.remapShortcuts.global = @($changeSet.RemainingShortcuts)
    $changeSet.Profile.remapShortcutsToText.global = @($changeSet.RemainingTextMappings)
    Write-JsonAtomic -Path $profilePath -Value $changeSet.Profile

    $migrated = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
    Assert-Equal 0 @($migrated.remapKeys.inProcess).Count 'Migration left owned keys.'
    Assert-Equal 0 @($migrated.remapShortcuts.global).Count 'Migration left overlapping shortcuts.'
    Assert-Equal 0 @($migrated.remapShortcutsToText.global).Count 'Migration left the owned Alt+J mapping.'

    $secondChangeSet = Get-PowerToysChangeSet -ProfilePath $profilePath
    Assert-Equal 0 $secondChangeSet.RemovedKeys.Count 'Second migration was not idempotent for keys.'
    Assert-Equal 0 $secondChangeSet.RemovedShortcuts.Count 'Second migration was not idempotent for shortcuts.'
    Assert-Equal 0 $secondChangeSet.RemovedTextMappings.Count 'Second migration was not idempotent for text shortcuts.'

    $mergedKeys = @(Merge-UniqueMappings -Existing $changeSet.RemovedKeys -Added $changeSet.RemovedKeys)
    $mergedShortcuts = @(Merge-UniqueMappings -Existing $changeSet.RemovedShortcuts -Added $changeSet.RemovedShortcuts)
    $mergedTextMappings = @(Merge-UniqueMappings -Existing $changeSet.RemovedTextMappings -Added $changeSet.RemovedTextMappings)
    Assert-Equal 4 $mergedKeys.Count 'Receipt merge duplicated a key mapping.'
    Assert-Equal 5 $mergedShortcuts.Count 'Receipt merge duplicated shortcut mappings.'
    Assert-Equal 1 $mergedTextMappings.Count 'Receipt merge duplicated a text mapping.'

    $receipt = [pscustomobject]@{
        removedKeys = $mergedKeys
        removedShortcuts = $mergedShortcuts
        removedTextMappings = $mergedTextMappings
    }
    Add-MappingsBack -Profile $migrated -Receipt $receipt
    Write-JsonAtomic -Path $profilePath -Value $migrated
    $restored = Get-PowerToysChangeSet -ProfilePath $profilePath
    Assert-Equal 4 $restored.RemovedKeys.Count 'Rollback did not restore the key mappings.'
    Assert-Equal 5 $restored.RemovedShortcuts.Count 'Rollback did not restore the shortcuts.'
    Assert-Equal 1 $restored.RemovedTextMappings.Count 'Rollback did not restore the text shortcut.'

    Write-Output 'PASS PowerToys migration preserves unrelated mappings'
    Write-Output 'PASS repeated install receipt merge is idempotent'
    Write-Output 'PASS rollback restores transferred mappings'
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected test path: $resolvedTestRoot"
    }
    if (Test-Path -LiteralPath $resolvedTestRoot) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
