if (!(Test-Path -Path $PROFILE)) {
    New-Item -ItemType File -Path $PROFILE -Force
}

@'
Set-Alias -Name pbcopy -Value Set-Clipboard
Set-Alias -Name pbpaste -Value Get-Clipboard
'@ >> $PROFILE