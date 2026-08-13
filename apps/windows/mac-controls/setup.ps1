[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall')]
    [string]$Action = 'Install',
    [switch]$Apply,
    [ValidateSet('Standard', 'Swapped')]
    [string]$ModifierLayout,
    [string]$KeyboardManagerDirectory = (Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\Keyboard Manager')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$projectPath = Join-Path $scriptRoot 'MacControls.csproj'
$installRoot = Join-Path $env:LOCALAPPDATA 'PlebTools\MacControls'
$stateRoot = Join-Path $env:LOCALAPPDATA 'PlebTools\MacControlsState'
$receiptPath = Join-Path $stateRoot 'install-receipt.json'
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'PlebToolsMacControls'

$effectiveModifierLayout = if ($PSBoundParameters.ContainsKey('ModifierLayout')) {
    $ModifierLayout
}
elseif (Test-Path -LiteralPath $receiptPath) {
    $existingLayoutReceipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ([string]$existingLayoutReceipt.modifierLayout -eq 'LeftAltCommand') { 'Swapped' } else { 'Standard' }
}
else {
    'Standard'
}

$ownedKeyMappings = @(
    @{ Source = '91'; Target = '162'; Description = 'Left Win -> Left Ctrl' },
    @{ Source = '89'; Target = '90'; Description = 'Y -> Z' },
    @{ Source = '90'; Target = '89'; Description = 'Z -> Y' },
    @{ Source = '220'; Target = '226'; Description = 'OEM 5 -> OEM 102' }
)
$allOwnedKeySources = @($ownedKeyMappings.Source) + @('164')
$ownedShortcutMappings = @(
    @{ Source = '164;8'; Target = '163;8'; Description = 'Left Alt + Backspace -> Right Ctrl + Backspace' },
    @{ Source = '164;37'; Target = '163;37'; Description = 'Left Alt + Left -> Right Ctrl + Left' },
    @{ Source = '164;39'; Target = '163;39'; Description = 'Left Alt + Right -> Right Ctrl + Right' },
    @{ Source = '91;37'; Target = '36'; Description = 'Left Win + Left -> Home' },
    @{ Source = '91;39'; Target = '35'; Description = 'Left Win + Right -> End' }
)
$allOwnedShortcutSources = @('91;8', '91;37', '91;38', '91;39', '91;40', '164;8', '164;37', '164;38', '164;39', '164;40')
$ownedTextMappings = @(
    @{ Source = '18;74'; Text = "'"; Description = "Alt + J -> '" }
)

function Get-ActivePowerToysProfilePath {
    $settingsPath = Join-Path $KeyboardManagerDirectory 'settings.json'
    if (-not (Test-Path -LiteralPath $settingsPath)) {
        return $null
    }

    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $activeName = [string]$settings.properties.activeConfiguration.value
    if ([string]::IsNullOrWhiteSpace($activeName)) {
        $activeName = 'default'
    }

    return Join-Path $KeyboardManagerDirectory "$activeName.json"
}

function Get-PowerToysChangeSet {
    param([Parameter(Mandatory)][string]$ProfilePath)

    $profile = Get-Content -LiteralPath $ProfilePath -Raw | ConvertFrom-Json
    $keyMappings = @($profile.remapKeys.inProcess)
    $shortcutMappings = @($profile.remapShortcuts.global)
    $textMappings = @($profile.remapShortcutsToText.global)

    $unexpectedKeyMappings = @($keyMappings | Where-Object {
        $source = [string]$_.originalKeys
        $target = [string]$_.newRemapKeys
        $expected = @($ownedKeyMappings | Where-Object {
            $_.Source -eq $source -and $_.Target -eq $target
        })
        $source -in $allOwnedKeySources -and $expected.Count -eq 0
    })
    $unexpectedShortcuts = @($shortcutMappings | Where-Object {
        $source = [string]$_.originalKeys
        $target = [string]$_.newRemapKeys
        $expected = @($ownedShortcutMappings | Where-Object {
            $_.Source -eq $source -and $_.Target -eq $target
        })
        $source -in $allOwnedShortcutSources -and $expected.Count -eq 0
    })
    $unexpectedTextMappings = @($textMappings | Where-Object {
        $source = [string]$_.originalKeys
        $text = [string]$_.unicodeText
        $expected = @($ownedTextMappings | Where-Object {
            $_.Source -eq $source -and $_.Text -eq $text
        })
        $source -in @($ownedTextMappings.Source) -and $expected.Count -eq 0
    })

    if ($unexpectedKeyMappings.Count -gt 0 -or $unexpectedShortcuts.Count -gt 0 -or $unexpectedTextMappings.Count -gt 0) {
        throw 'The active PowerToys profile contains an overlapping mapping that Mac Controls does not recognize. No changes were made; resolve that mapping manually first.'
    }

    $removedKeys = @($keyMappings | Where-Object {
        $source = [string]$_.originalKeys
        $target = [string]$_.newRemapKeys
        @($ownedKeyMappings | Where-Object {
            $_.Source -eq $source -and $_.Target -eq $target
        }).Count -gt 0
    })
    $removedShortcuts = @($shortcutMappings | Where-Object {
        $source = [string]$_.originalKeys
        $target = [string]$_.newRemapKeys
        @($ownedShortcutMappings | Where-Object {
            $_.Source -eq $source -and $_.Target -eq $target
        }).Count -gt 0
    })
    $removedTextMappings = @($textMappings | Where-Object {
        $source = [string]$_.originalKeys
        $text = [string]$_.unicodeText
        @($ownedTextMappings | Where-Object {
            $_.Source -eq $source -and $_.Text -eq $text
        }).Count -gt 0
    })

    [pscustomobject]@{
        Profile = $profile
        RemovedKeys = $removedKeys
        RemovedShortcuts = $removedShortcuts
        RemovedTextMappings = $removedTextMappings
        RemainingKeys = @($keyMappings | Where-Object { $_ -notin $removedKeys })
        RemainingShortcuts = @($shortcutMappings | Where-Object { $_ -notin $removedShortcuts })
        RemainingTextMappings = @($textMappings | Where-Object { $_ -notin $removedTextMappings })
    }
}

function Write-JsonAtomic {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Value
    )

    $temporaryPath = "$Path.mac-controls.tmp"
    $replacementBackupPath = "$Path.mac-controls.replace-backup"
    $json = $Value | ConvertTo-Json -Depth 100 -Compress
    [IO.File]::WriteAllText($temporaryPath, $json, [Text.UTF8Encoding]::new($false))
    try {
        if (Test-Path -LiteralPath $Path) {
            [IO.File]::Replace($temporaryPath, $Path, $replacementBackupPath)
        }
        else {
            [IO.File]::Move($temporaryPath, $Path)
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
        if (Test-Path -LiteralPath $replacementBackupPath) {
            Remove-Item -LiteralPath $replacementBackupPath -Force
        }
    }
}

function Get-FileHashValue {
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ExistingRunValue {
    $runKey = Get-ItemProperty -Path $runKeyPath -ErrorAction SilentlyContinue
    if ($null -eq $runKey -or $runKey.PSObject.Properties.Name -notcontains $runValueName) {
        return $null
    }

    return [string]$runKey.$runValueName
}

function Merge-UniqueMappings {
    param(
        [object[]]$Existing = @(),
        [object[]]$Added = @()
    )

    return @(
        @($Existing) + @($Added) |
            Group-Object -Property originalKeys, newRemapKeys |
            ForEach-Object { $_.Group[0] }
    )
}

function Stop-MacControls {
    Get-Process -Name 'MacControls' -ErrorAction SilentlyContinue | Stop-Process -Force
}

function Get-PowerToysProcesses {
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -like 'PowerToys*'
    })
}

function Stop-PowerToys {
    $processes = @(Get-PowerToysProcesses)
    if ($processes.Count -eq 0) {
        return
    }

    $processes | Stop-Process -Force
    $processes | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

function Start-PowerToys {
    try {
        $startupLink = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\PowerToys (Preview).lnk'
        if (-not (Test-Path -LiteralPath $startupLink)) {
            Write-Warning 'PowerToys startup shortcut was not found. Start PowerToys manually so it reloads the updated profile.'
            return
        }

        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($startupLink)
        if ([string]::IsNullOrWhiteSpace($shortcut.TargetPath)) {
            Write-Warning 'PowerToys startup shortcut has no target. Start PowerToys manually.'
            return
        }

        $arguments = @{}
        if (-not [string]::IsNullOrWhiteSpace($shortcut.Arguments)) {
            $arguments.ArgumentList = $shortcut.Arguments
        }
        Start-Process -FilePath $shortcut.TargetPath -WindowStyle Hidden @arguments
    }
    catch {
        Write-Warning "PowerToys could not be restarted automatically: $($_.Exception.Message) Start it manually to reload the updated profile."
    }
}

function Assert-SafeInstallRoot {
    $expectedParent = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'PlebTools'))
    $resolvedInstallRoot = [IO.Path]::GetFullPath($installRoot)
    if (-not $resolvedInstallRoot.StartsWith($expectedParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify unexpected install path: $resolvedInstallRoot"
    }
}

function Preview-Install {
    param([string]$ProfilePath, $ChangeSet)

    Write-Output 'Mac Controls install preview'
    Write-Output "  Application: $installRoot"
    Write-Output "  Startup: HKCU Run value $runValueName"
    Write-Output ("  Modifier layout: " + $(if ($effectiveModifierLayout -eq 'Swapped') { 'physical Left Alt = Command, physical Left Win = Option' } else { 'physical Left Win = Command, physical Left Alt = Option' }))
    if ([string]::IsNullOrWhiteSpace($ProfilePath)) {
        Write-Output '  PowerToys: no Keyboard Manager profile found'
    }
    else {
        Write-Output "  PowerToys profile: $ProfilePath"
        Write-Output "  Overlapping key mappings to transfer: $($ChangeSet.RemovedKeys.Count)"
        Write-Output "  Overlapping shortcuts to transfer: $($ChangeSet.RemovedShortcuts.Count)"
        Write-Output "  Overlapping text shortcuts to transfer: $($ChangeSet.RemovedTextMappings.Count)"
        Write-Output "  Unrelated key mappings preserved: $($ChangeSet.RemainingKeys.Count)"
        Write-Output "  Unrelated shortcuts preserved: $($ChangeSet.RemainingShortcuts.Count)"
        Write-Output "  Unrelated text shortcuts preserved: $($ChangeSet.RemainingTextMappings.Count)"
    }
    Write-Output 'No machine settings were changed. Run install.ps1 -Apply to install.'
}

function Install-MacControls {
    $profilePath = Get-ActivePowerToysProfilePath
    $changeSet = $null
    if ($null -ne $profilePath) {
        if (-not (Test-Path -LiteralPath $profilePath)) {
            throw "The active PowerToys profile does not exist: $profilePath"
        }
        $changeSet = Get-PowerToysChangeSet -ProfilePath $profilePath
    }

    if (-not $Apply) {
        Preview-Install -ProfilePath $profilePath -ChangeSet $changeSet
        return
    }

    Assert-SafeInstallRoot
    $publishRoot = Join-Path ([IO.Path]::GetTempPath()) "pleb-tools-mac-controls-$PID"
    $previousInstallRoot = Join-Path (Split-Path -Parent $installRoot) "MacControls.previous-$PID"
    $powerToysWasRunning = @(Get-PowerToysProcesses).Count -gt 0
    $macControlsWasRunning = @(Get-Process -Name 'MacControls' -ErrorAction SilentlyContinue).Count -gt 0
    $powerToysStopped = $false
    $profileChanged = $false
    $profileBackupPath = $null
    $previousInstallMoved = $false
    $applicationStopped = $false
    $newInstallCreated = $false
    $existingRunValue = Get-ExistingRunValue
    $existingReceiptText = $null
    $readyEvent = $null
    if (Test-Path -LiteralPath $receiptPath) {
        $existingReceiptText = Get-Content -LiteralPath $receiptPath -Raw
    }
    $previousApplicationArguments = if ($null -ne $existingReceiptText -and
        [string](($existingReceiptText | ConvertFrom-Json).modifierLayout) -eq 'LeftAltCommand') {
        @('--swap-left-win-alt')
    }
    else {
        @()
    }

    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    try {
        dotnet publish $projectPath --configuration Release --runtime win-x64 --self-contained false --output $publishRoot --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE."
        }

        Stop-MacControls
        $applicationStopped = $true
        if (Test-Path -LiteralPath $installRoot) {
            if (Test-Path -LiteralPath $previousInstallRoot) {
                Remove-Item -LiteralPath $previousInstallRoot -Recurse -Force
            }
            Move-Item -LiteralPath $installRoot -Destination $previousInstallRoot
            $previousInstallMoved = $true
        }
        New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
        $newInstallCreated = $true
        Copy-Item -Path (Join-Path $publishRoot '*') -Destination $installRoot -Recurse -Force

        New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
        $existingReceipt = if ($null -ne $existingReceiptText) {
            $existingReceiptText | ConvertFrom-Json
        }
        else {
            $null
        }

        if ($null -ne $existingReceipt -and
            -not [string]::IsNullOrWhiteSpace([string]$existingReceipt.profilePath) -and
            -not [string]::IsNullOrWhiteSpace([string]$profilePath) -and
            [string]$existingReceipt.profilePath -ne [string]$profilePath) {
            throw 'The existing install receipt belongs to a different PowerToys profile. Uninstall Mac Controls before installing into the new active profile.'
        }

        $receipt = [ordered]@{
            schemaVersion = 3
            installedAt = if ($null -ne $existingReceipt) { [string]$existingReceipt.installedAt } else { [DateTimeOffset]::Now.ToString('o') }
            updatedAt = [DateTimeOffset]::Now.ToString('o')
            profilePath = if ($null -ne $existingReceipt -and -not [string]::IsNullOrWhiteSpace([string]$existingReceipt.profilePath)) {
                [string]$existingReceipt.profilePath
            }
            else {
                $profilePath
            }
            backupPath = if ($null -ne $existingReceipt) { [string]$existingReceipt.backupPath } else { $null }
            originalProfileSha256 = if ($null -ne $existingReceipt) { [string]$existingReceipt.originalProfileSha256 } else { $null }
            installedProfileSha256 = $null
            modifierLayout = if ($effectiveModifierLayout -eq 'Swapped') { 'LeftAltCommand' } else { 'LeftWinCommand' }
            removedKeys = if ($null -ne $existingReceipt) { @($existingReceipt.removedKeys) } else { @() }
            removedShortcuts = if ($null -ne $existingReceipt) { @($existingReceipt.removedShortcuts) } else { @() }
            removedTextMappings = if ($null -ne $existingReceipt -and $existingReceipt.PSObject.Properties.Name -contains 'removedTextMappings') {
                @($existingReceipt.removedTextMappings)
            }
            else {
                @()
            }
        }

        if ($null -ne $profilePath) {
            $profileBackupPath = Join-Path $stateRoot ("PowerToys-profile-before-install-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
            Copy-Item -LiteralPath $profilePath -Destination $profileBackupPath
            if ([string]::IsNullOrWhiteSpace([string]$receipt.originalProfileSha256)) {
                $receipt.originalProfileSha256 = Get-FileHashValue -Path $profilePath
                $receipt.backupPath = $profileBackupPath
            }

            $receipt.removedKeys = @(Merge-UniqueMappings -Existing @($receipt.removedKeys) -Added @($changeSet.RemovedKeys))
            $receipt.removedShortcuts = @(Merge-UniqueMappings -Existing @($receipt.removedShortcuts) -Added @($changeSet.RemovedShortcuts))
            $receipt.removedTextMappings = @(Merge-UniqueMappings -Existing @($receipt.removedTextMappings) -Added @($changeSet.RemovedTextMappings))

            if ($powerToysWasRunning) {
                Stop-PowerToys
                $powerToysStopped = $true
            }
            $changeSet.Profile.remapKeys.inProcess = @($changeSet.RemainingKeys)
            $changeSet.Profile.remapShortcuts.global = @($changeSet.RemainingShortcuts)
            $changeSet.Profile.remapShortcutsToText.global = @($changeSet.RemainingTextMappings)
            Write-JsonAtomic -Path $profilePath -Value $changeSet.Profile
            $profileChanged = $true
            $receipt.installedProfileSha256 = Get-FileHashValue -Path $profilePath
        }

        [IO.File]::WriteAllText(
            $receiptPath,
            ($receipt | ConvertTo-Json -Depth 100),
            [Text.UTF8Encoding]::new($false))

        $executablePath = Join-Path $installRoot 'MacControls.exe'
        [string[]]$applicationArguments = if ($effectiveModifierLayout -eq 'Swapped') { @('--swap-left-win-alt') } else { @() }
        $runValue = '"{0}"{1}' -f $executablePath, $(if ($effectiveModifierLayout -eq 'Swapped') { ' --swap-left-win-alt' } else { '' })
        New-ItemProperty -Path $runKeyPath -Name $runValueName -PropertyType String -Value $runValue -Force | Out-Null
        $readyEventName = "PlebTools.MacControls.Setup.$PID.$([Guid]::NewGuid().ToString('N'))"
        $readyEvent = [Threading.EventWaitHandle]::new(
            $false,
            [Threading.EventResetMode]::AutoReset,
            $readyEventName)
        $applicationArguments += @('--ready-event', $readyEventName)
        $startedApplication = Start-Process -FilePath $executablePath -ArgumentList $applicationArguments -WindowStyle Hidden -PassThru
        if (-not $readyEvent.WaitOne([TimeSpan]::FromSeconds(5))) {
            throw 'Mac Controls did not confirm that its tray and keyboard hook started successfully.'
        }
        $startedApplication.Refresh()
        if ($startedApplication.HasExited) {
            throw "Mac Controls exited during startup with code $($startedApplication.ExitCode)."
        }

        if ($previousInstallMoved -and (Test-Path -LiteralPath $previousInstallRoot)) {
            Remove-Item -LiteralPath $previousInstallRoot -Recurse -Force
            $previousInstallMoved = $false
        }
        if ($null -ne $profilePath) {
            Start-PowerToys
            $powerToysStopped = $false
        }
        Write-Output "Mac Controls installed and started from $installRoot"
    }
    catch {
        if ($applicationStopped) {
            Stop-MacControls
            if ($null -ne $existingRunValue) {
                New-ItemProperty -Path $runKeyPath -Name $runValueName -PropertyType String -Value $existingRunValue -Force | Out-Null
            }
            else {
                Remove-ItemProperty -Path $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue
            }

            if ($profileChanged -and @(Get-PowerToysProcesses).Count -gt 0) {
                Stop-PowerToys
                $powerToysStopped = $true
            }
            if ($profileChanged -and $null -ne $profileBackupPath -and (Test-Path -LiteralPath $profileBackupPath)) {
                Copy-Item -LiteralPath $profileBackupPath -Destination $profilePath -Force
            }

            if ($null -ne $existingReceiptText) {
                [IO.File]::WriteAllText($receiptPath, $existingReceiptText, [Text.UTF8Encoding]::new($false))
            }
            elseif (Test-Path -LiteralPath $receiptPath) {
                Remove-Item -LiteralPath $receiptPath -Force
            }

            if ($newInstallCreated -and (Test-Path -LiteralPath $installRoot)) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force
            }
            if ($previousInstallMoved -and (Test-Path -LiteralPath $previousInstallRoot)) {
                Move-Item -LiteralPath $previousInstallRoot -Destination $installRoot
                $previousInstallMoved = $false
            }
            if ($macControlsWasRunning) {
                Start-Process -FilePath (Join-Path $installRoot 'MacControls.exe') -ArgumentList $previousApplicationArguments -WindowStyle Hidden
            }
        }

        throw
    }
    finally {
        if ($null -ne $readyEvent) {
            $readyEvent.Dispose()
        }
        if ($powerToysStopped -and $powerToysWasRunning) {
            Start-PowerToys
        }
        if (Test-Path -LiteralPath $publishRoot) {
            Remove-Item -LiteralPath $publishRoot -Recurse -Force
        }
    }
}

function Add-MappingsBack {
    param(
        [Parameter(Mandatory)]$Profile,
        [Parameter(Mandatory)]$Receipt
    )

    $keys = [Collections.ArrayList]@($Profile.remapKeys.inProcess)
    foreach ($mapping in @($Receipt.removedKeys)) {
        $source = [string]$mapping.originalKeys
        if (@($keys | Where-Object { [string]$_.originalKeys -eq $source }).Count -eq 0) {
            [void]$keys.Add($mapping)
        }
        else {
            Write-Warning "Skipped restoring PowerToys key source $source because it is already assigned."
        }
    }

    $shortcuts = [Collections.ArrayList]@($Profile.remapShortcuts.global)
    foreach ($mapping in @($Receipt.removedShortcuts)) {
        $source = [string]$mapping.originalKeys
        if (@($shortcuts | Where-Object { [string]$_.originalKeys -eq $source }).Count -eq 0) {
            [void]$shortcuts.Add($mapping)
        }
        else {
            Write-Warning "Skipped restoring PowerToys shortcut source $source because it is already assigned."
        }
    }

    $Profile.remapKeys.inProcess = @($keys)
    $Profile.remapShortcuts.global = @($shortcuts)

    $textMappings = [Collections.ArrayList]@($Profile.remapShortcutsToText.global)
    $removedTextMappings = if ($Receipt.PSObject.Properties.Name -contains 'removedTextMappings') {
        @($Receipt.removedTextMappings)
    }
    else {
        @()
    }
    foreach ($mapping in $removedTextMappings) {
        $source = [string]$mapping.originalKeys
        if (@($textMappings | Where-Object { [string]$_.originalKeys -eq $source }).Count -eq 0) {
            [void]$textMappings.Add($mapping)
        }
        else {
            Write-Warning "Skipped restoring PowerToys text shortcut source $source because it is already assigned."
        }
    }

    $Profile.remapShortcutsToText.global = @($textMappings)
}

function Uninstall-MacControls {
    if (-not $Apply) {
        Write-Output 'Mac Controls uninstall preview'
        Write-Output "  Stop MacControls.exe"
        Write-Output "  Remove startup value: $runValueName"
        Write-Output "  Restore transferred PowerToys entries without overwriting newer mappings"
        Write-Output "  Remove application: $installRoot"
        Write-Output 'No machine settings were changed. Run uninstall.ps1 -Apply to continue.'
        return
    }

    Assert-SafeInstallRoot
    $powerToysWasRunning = @(Get-PowerToysProcesses).Count -gt 0
    $powerToysStopped = $false
    Stop-MacControls
    try {
        if (Test-Path -LiteralPath $receiptPath) {
            $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
            if (-not [string]::IsNullOrWhiteSpace([string]$receipt.profilePath) -and (Test-Path -LiteralPath $receipt.profilePath)) {
                $profile = Get-Content -LiteralPath $receipt.profilePath -Raw | ConvertFrom-Json
                New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
                $backupPath = Join-Path $stateRoot ("PowerToys-profile-before-uninstall-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
                Copy-Item -LiteralPath $receipt.profilePath -Destination $backupPath
                if ($powerToysWasRunning) {
                    Stop-PowerToys
                    $powerToysStopped = $true
                }
                Add-MappingsBack -Profile $profile -Receipt $receipt
                Write-JsonAtomic -Path $receipt.profilePath -Value $profile
            }
        }
        else {
            Write-Warning 'Install receipt not found. PowerToys mappings were not changed.'
        }

        Remove-ItemProperty -Path $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $installRoot) {
            Remove-Item -LiteralPath $installRoot -Recurse -Force
        }
    }
    finally {
        if ($powerToysStopped -and $powerToysWasRunning) {
            Start-PowerToys
        }
    }
    Write-Output 'Mac Controls was removed. Audit receipts and PowerToys backups remain in:'
    Write-Output "  $stateRoot"
}

if ($Action -eq 'Install') {
    Install-MacControls
}
else {
    Uninstall-MacControls
}
