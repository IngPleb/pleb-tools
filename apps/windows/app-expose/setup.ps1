[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall')]
    [string]$Action = 'Install',
    [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'AppExpose.csproj'
$installRoot = Join-Path $env:LOCALAPPDATA 'PlebTools\AppExpose'
$stateRoot = Join-Path $env:LOCALAPPDATA 'PlebTools\AppExposeState'
$receiptPath = Join-Path $stateRoot 'install-receipt.json'
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'PlebToolsAppExpose'

function Assert-SafeInstallRoot {
    $expectedParent = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'PlebTools'))
    $resolvedInstallRoot = [IO.Path]::GetFullPath($installRoot)
    if (-not $resolvedInstallRoot.StartsWith($expectedParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify unexpected install path: $resolvedInstallRoot"
    }
}

function Get-ExistingRunValue {
    $runKey = Get-ItemProperty -Path $runKeyPath -ErrorAction SilentlyContinue
    if ($null -eq $runKey -or $runKey.PSObject.Properties.Name -notcontains $runValueName) {
        return $null
    }

    return [string]$runKey.$runValueName
}

function Stop-AppExpose {
    Get-Process -Name 'AppExpose' -ErrorAction SilentlyContinue | Stop-Process -Force
}

function Preview-Install {
    Write-Output 'App Expose install preview'
    Write-Output "  Application: $installRoot"
    Write-Output "  Startup: HKCU Run value $runValueName"
    Write-Output '  Start and verify the notification-area app'
    Write-Output 'No machine settings were changed. Run install.ps1 -Apply to install.'
}

function Install-AppExpose {
    if (-not $Apply) {
        Preview-Install
        return
    }

    Assert-SafeInstallRoot
    $publishRoot = Join-Path ([IO.Path]::GetTempPath()) "pleb-tools-app-expose-$PID"
    $previousInstallRoot = Join-Path (Split-Path -Parent $installRoot) "AppExpose.previous-$PID"
    $applicationWasRunning = @(Get-Process -Name 'AppExpose' -ErrorAction SilentlyContinue).Count -gt 0
    $existingRunValue = Get-ExistingRunValue
    $existingReceiptText = if (Test-Path -LiteralPath $receiptPath) {
        Get-Content -LiteralPath $receiptPath -Raw
    }
    else {
        $null
    }
    $previousInstallMoved = $false
    $applicationStopped = $false
    $newInstallCreated = $false
    $readyEvent = $null

    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    try {
        dotnet publish $projectPath --configuration Release --runtime win-x64 --self-contained false --output $publishRoot --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE."
        }

        Stop-AppExpose
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
        $receipt = [ordered]@{
            schemaVersion = 1
            installedAt = if ($null -ne $existingReceipt) { [string]$existingReceipt.installedAt } else { [DateTimeOffset]::Now.ToString('o') }
            updatedAt = [DateTimeOffset]::Now.ToString('o')
            installRoot = $installRoot
        }
        [IO.File]::WriteAllText(
            $receiptPath,
            ($receipt | ConvertTo-Json),
            [Text.UTF8Encoding]::new($false))

        $executablePath = Join-Path $installRoot 'AppExpose.exe'
        New-Item -ItemType Directory -Path $runKeyPath -Force | Out-Null
        New-ItemProperty -Path $runKeyPath -Name $runValueName -PropertyType String -Value ('"{0}"' -f $executablePath) -Force | Out-Null

        $readyEventName = "PlebTools.AppExpose.Setup.$PID.$([Guid]::NewGuid().ToString('N'))"
        $readyEvent = [Threading.EventWaitHandle]::new(
            $false,
            [Threading.EventResetMode]::AutoReset,
            $readyEventName)
        $startedApplication = Start-Process -FilePath $executablePath -ArgumentList @('--ready-event', $readyEventName) -WindowStyle Hidden -PassThru
        if (-not $readyEvent.WaitOne([TimeSpan]::FromSeconds(5))) {
            throw 'App Expose did not confirm that its tray and shortcut hook started successfully.'
        }
        $startedApplication.Refresh()
        if ($startedApplication.HasExited) {
            throw "App Expose exited during startup with code $($startedApplication.ExitCode)."
        }

        if ($previousInstallMoved -and (Test-Path -LiteralPath $previousInstallRoot)) {
            Remove-Item -LiteralPath $previousInstallRoot -Recurse -Force
            $previousInstallMoved = $false
        }
        Write-Output "App Expose installed and started from $installRoot"
    }
    catch {
        if ($applicationStopped) {
            Stop-AppExpose
            if ($null -ne $existingRunValue) {
                New-Item -ItemType Directory -Path $runKeyPath -Force | Out-Null
                New-ItemProperty -Path $runKeyPath -Name $runValueName -PropertyType String -Value $existingRunValue -Force | Out-Null
            }
            else {
                Remove-ItemProperty -Path $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue
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
            if ($applicationWasRunning) {
                Start-Process -FilePath (Join-Path $installRoot 'AppExpose.exe') -WindowStyle Hidden
            }
        }

        throw
    }
    finally {
        if ($null -ne $readyEvent) {
            $readyEvent.Dispose()
        }
        if (Test-Path -LiteralPath $publishRoot) {
            Remove-Item -LiteralPath $publishRoot -Recurse -Force
        }
    }
}

function Uninstall-AppExpose {
    if (-not $Apply) {
        Write-Output 'App Expose uninstall preview'
        Write-Output '  Stop AppExpose.exe'
        Write-Output "  Remove startup value: $runValueName"
        Write-Output "  Remove application: $installRoot"
        Write-Output "  Preserve audit receipt: $receiptPath"
        Write-Output 'No machine settings were changed. Run uninstall.ps1 -Apply to continue.'
        return
    }

    Assert-SafeInstallRoot
    Stop-AppExpose
    Remove-ItemProperty -Path $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $installRoot) {
        Remove-Item -LiteralPath $installRoot -Recurse -Force
    }
    Write-Output 'App Expose was removed. Its audit receipt remains in:'
    Write-Output "  $stateRoot"
}

if ($Action -eq 'Install') {
    Install-AppExpose
}
else {
    Uninstall-AppExpose
}
