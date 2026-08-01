<#
.SYNOPSIS
    Runs a small interactive console against one explicit RIME session.

.DESCRIPTION
    This example demonstrates the version 0.2 lifecycle contract. Start-Rime
    owns the process lifecycle, while New-RimeSession and Remove-RimeSession
    independently manage input contexts.
#>

[CmdletBinding()]
param(
    [string]$ModulePath = (Join-Path $PSScriptRoot '..\RimeSharp.PowerShell.psd1'),
    [string]$SharedDataDir = 'shared',
    [string]$UserDataDir = 'user'
)

$ErrorActionPreference = 'Stop'
Import-Module $ModulePath -Force

$traits = @{
    AppName       = 'RimeSharp.PowerShell.ApiConsole'
    SharedDataDir = $SharedDataDir
    UserDataDir   = $UserDataDir
}

function Show-ApiConsoleBanner {
    Write-Host 'Enter a librime key sequence, or one of these commands:'
    Write-Host '  :h, :help                      Show all commands'
    Write-Host '  :q, :quit                      Exit'
    Write-Host 'Enter :help to show the complete command list.'
}

function Show-ApiConsoleHelp {
    Write-Host 'Commands:'
    Write-Host '  :print                         Show commit, status, and context'
    Write-Host '  :input                         Show raw input'
    Write-Host '  :schemas                       List schemas'
    Write-Host '  :schema <id>                   Select a schema'
    Write-Host '  :switcher <available|selected> List switcher schemas'
    Write-Host '  :candidates                    List all candidates'
    Write-Host '  :select <index>                Select from the current page'
    Write-Host '  :highlight <index>             Highlight on the current page'
    Write-Host '  :delete <index>                Delete by global index'
    Write-Host '  :delete-page <index>           Delete from the current page'
    Write-Host '  :prev                          Move to the previous page'
    Write-Host '  :next                          Move to the next page'
    Write-Host '  :option <name>                 Show a boolean option'
    Write-Host '  :set-option <name> <on|off>    Set a boolean option'
    Write-Host '  :toggle <name>                 Toggle a boolean option'
    Write-Host '  :key <code> [mask]             Process one raw key event'
    Write-Host '  :notifications                 Drain queued notifications'
    Write-Host '  :reload                        Restart lifecycle and session'
    Write-Host '  :h, :help                      Show this command list'
    Write-Host '  :q, :quit                      Exit'
}

function ConvertTo-ZeroBasedIndex {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    $parsed = 0
    if (-not [int]::TryParse($Value, [ref]$parsed) -or $parsed -lt 1) {
        throw "Candidate index '$Value' must be a positive integer."
    }

    return $parsed - 1
}

function Show-RimeStatusSnapshot {
    param(
        [Parameter(Mandatory)]
        $Status
    )

    Write-Host ('schema: {0} / {1}' -f $Status.SchemaId, $Status.SchemaName)
    $flags = [System.Collections.Generic.List[string]]::new()
    if ($Status.IsDisabled) { $flags.Add('disabled') }
    if ($Status.IsComposing) { $flags.Add('composing') }
    if ($Status.IsAsciiMode) { $flags.Add('ascii') }
    if ($Status.IsFullShape) { $flags.Add('full_shape') }
    if ($Status.IsSimplified) { $flags.Add('simplified') }
    if ($Status.IsTraditional) { $flags.Add('traditional') }
    if ($Status.IsAsciiPunct) { $flags.Add('ascii_punct') }
    Write-Host ('status: {0}' -f ($flags -join ' '))
}

function Show-RimeCompositionSnapshot {
    param(
        [Parameter(Mandatory)]
        $Composition
    )

    if ($null -eq $Composition.Preedit) { return }

    $preedit = $Composition.Preedit
    $line = [System.Text.StringBuilder]::new()
    for ($i = 0; $i -le $preedit.Length; $i++) {
        if ($Composition.SelectionStart -lt $Composition.SelectionEnd) {
            if ($i -eq $Composition.SelectionStart) {
                [void]$line.Append('[')
            }
            elseif ($i -eq $Composition.SelectionEnd) {
                [void]$line.Append(']')
            }
        }

        if ($i -eq $Composition.CursorPosition) {
            [void]$line.Append('|')
        }
        if ($i -lt $preedit.Length) {
            [void]$line.Append($preedit[$i])
        }
    }

    Write-Host $line.ToString()
}

function Show-RimeMenuSnapshot {
    param(
        [Parameter(Mandatory)]
        $Context
    )

    $menu = $Context.Menu
    if ($menu.NumCandidates -eq 0) { return }

    $pageMarker = if ($menu.IsLastPage) { '$' } else { ' ' }
    Write-Host ('page: {0}{1} (of size {2})' -f
        ($menu.PageNo + 1), $pageMarker, $menu.PageSize)
    for ($i = 0; $i -lt $menu.Candidates.Count; $i++) {
        $candidate = $menu.Candidates[$i]
        $label = $i + 1
        if ($i -lt $Context.SelectLabels.Count -and
            -not [string]::IsNullOrEmpty($Context.SelectLabels[$i])) {
            $label = $Context.SelectLabels[$i]
        }

        $comment = if ([string]::IsNullOrEmpty($candidate.Comment)) {
            ''
        }
        else {
            " $($candidate.Comment)"
        }
        if ($i -eq $menu.HighlightedCandidateIndex) {
            Write-Host ('{0}. [{1}]{2}' -f $label, $candidate.Text, $comment)
        }
        else {
            Write-Host ('{0}.  {1}{2}' -f $label, $candidate.Text, $comment)
        }
    }
}

function Show-RimeContextSnapshot {
    param(
        [Parameter(Mandatory)]
        $Context
    )

    if ($Context.Composition.Length -gt 0 -or
        $Context.Menu.NumCandidates -gt 0) {
        Show-RimeCompositionSnapshot -Composition $Context.Composition
    }
    else {
        Write-Host '(not composing)'
    }

    Show-RimeMenuSnapshot -Context $Context
}

function Show-RimeNotification {
    param(
        [Parameter(Mandatory)]
        $Notification
    )

    Write-Host ('message: [{0}] [{1}] {2}' -f
        $Notification.SessionId,
        $Notification.MessageType,
        $Notification.MessageValue)
}

function Show-RimeOperationResult {
    param(
        [Parameter(Mandatory)]
        $Result
    )

    if ($null -ne $Result.PSObject.Properties['Handled']) {
        Write-Host ('handled: {0}' -f $Result.Handled)
    }
    if ($null -ne $Result.Commit) {
        Write-Host ('commit: {0}' -f $Result.Commit)
    }
    Write-Host ('input: {0}' -f $Result.Input)
    Show-RimeStatusSnapshot -Status $Result.Status
    Show-RimeContextSnapshot -Context $Result.Context
    foreach ($notification in $Result.Notifications) {
        Show-RimeNotification -Notification $notification
    }
}

function Show-RimeCurrentState {
    param(
        [Parameter(Mandatory)]
        $Session
    )

    $commit = Receive-RimeCommit -Session $Session
    if ($null -ne $commit) {
        Write-Host ('commit: {0}' -f $commit)
    }
    Write-Host ('input: {0}' -f (Get-RimeInput -Session $Session))
    Show-RimeStatusSnapshot -Status (Get-RimeStatus -Session $Session)
    Show-RimeContextSnapshot -Context (Get-RimeContext -Session $Session)
}

$source = Get-RimeNotificationSource
$eventParameters = @{
    InputObject = $source
    EventName   = 'NotificationReceived'
    Action      = {
        $notification = $EventArgs.Notification
        Write-Host (
            '[{0}] {1}: {2}' -f
            $EventArgs.Origin,
            $notification.MessageType,
            $notification.MessageValue)
    }
}
$subscription = Register-ObjectEvent @eventParameters

$session = $null
try {
    Start-Rime @traits
    $session = New-RimeSession

    Show-ApiConsoleBanner

    while ($true) {
        $line = Read-Host 'rime'
        try {
            if ($line -eq ':q' -or $line -eq ':quit') {
                return
            }
            if ($line -eq ':h' -or $line -eq ':help') {
                Show-ApiConsoleHelp
                continue
            }
            if ($line -eq ':print') {
                Show-RimeCurrentState -Session $session
                continue
            }
            if ($line -eq ':input') {
                Write-Host (Get-RimeInput -Session $session)
                continue
            }
            if ($line -eq ':notifications') {
                foreach ($notification in @(Receive-RimeNotification)) {
                    Show-RimeNotification -Notification $notification
                }
                continue
            }
            if ($line -eq ':schemas') {
                Get-RimeSchema -Session $session |
                    Format-Table SchemaId, Name, IsCurrent -AutoSize
                continue
            }
            if ($line -match '^:schema\s+(.+?)\s*$') {
                Set-RimeSchema -Session $session -SchemaId $Matches[1]
                Show-RimeCurrentState -Session $session
                continue
            }
            if ($line -match '^:switcher\s+(available|selected)\s*$') {
                if ($Matches[1] -eq 'available') {
                    Get-RimeSwitcherSchema -Available |
                        Format-Table SchemaId, Name -AutoSize
                }
                else {
                    Get-RimeSwitcherSchema -Selected |
                        Format-Table SchemaId, Name -AutoSize
                }
                continue
            }
            if ($line -eq ':candidates') {
                $candidates = @(Get-RimeCandidate -Session $session)
                if ($candidates.Count -eq 0) {
                    Write-Host 'no candidates.'
                }
                else {
                    foreach ($candidate in $candidates) {
                        $comment = if ([string]::IsNullOrEmpty(
                            $candidate.Comment)) {
                            ''
                        }
                        else {
                            " ($($candidate.Comment))"
                        }
                        Write-Host ('{0}. {1}{2}' -f
                            ($candidate.Index + 1),
                            $candidate.Text,
                            $comment)
                    }
                }
                continue
            }
            if ($line -match '^:select\s+(\S+)\s*$') {
                $index = ConvertTo-ZeroBasedIndex -Value $Matches[1]
                Select-RimeCandidate `
                    -Session $session `
                    -Index $index `
                    -OnCurrentPage
                Show-RimeCurrentState -Session $session
                continue
            }
            if ($line -match '^:highlight\s+(\S+)\s*$') {
                $index = ConvertTo-ZeroBasedIndex -Value $Matches[1]
                Invoke-RimeHighlight `
                    -Session $session `
                    -Index $index `
                    -OnCurrentPage
                Show-RimeCurrentState -Session $session
                continue
            }
            if ($line -match '^:delete-page\s+(\S+)\s*$') {
                $index = ConvertTo-ZeroBasedIndex -Value $Matches[1]
                Remove-RimeCandidate `
                    -Session $session `
                    -Index $index `
                    -OnCurrentPage `
                    -Confirm:$false
                Show-RimeCurrentState -Session $session
                continue
            }
            if ($line -match '^:delete\s+(\S+)\s*$') {
                $index = ConvertTo-ZeroBasedIndex -Value $Matches[1]
                Remove-RimeCandidate `
                    -Session $session `
                    -Index $index `
                    -Confirm:$false
                Show-RimeCurrentState -Session $session
                continue
            }
            if ($line -eq ':prev' -or $line -eq ':next') {
                $direction = if ($line -eq ':prev') { 'Previous' } else { 'Next' }
                Set-RimePage -Session $session -Direction $direction
                Show-RimeCurrentState -Session $session
                continue
            }
            if ($line -match '^:option\s+(\S+)\s*$') {
                $optionName = $Matches[1]
                Write-Host ('{0} = {1}' -f
                    $optionName,
                    (Get-RimeOption -Session $session -Name $optionName))
                continue
            }
            if ($line -match '^:set-option\s+(\S+)\s+(on|off)\s*$') {
                $optionName = $Matches[1]
                $optionValue = $Matches[2] -eq 'on'
                Set-RimeOption `
                    -Session $session `
                    -Name $optionName `
                    -Value $optionValue
                Write-Host ('{0} set {1}.' -f $optionName, $Matches[2])
                continue
            }
            if ($line -match '^:toggle\s+(\S+)\s*$') {
                $optionName = $Matches[1]
                $optionValue = -not (
                    Get-RimeOption -Session $session -Name $optionName)
                Set-RimeOption `
                    -Session $session `
                    -Name $optionName `
                    -Value $optionValue
                Write-Host ('{0} = {1}' -f $optionName, $optionValue)
                continue
            }
            if ($line -match '^:key\s+(-?\d+)(?:\s+(-?\d+))?\s*$') {
                $mask = if ($Matches[2]) { [int]$Matches[2] } else { 0 }
                $result = Send-RimeKeyEvent `
                    -Session $session `
                    -KeyCode ([int]$Matches[1]) `
                    -Mask $mask
                Show-RimeOperationResult -Result $result
                continue
            }
            if ($line -eq ':reload') {
                Remove-RimeSession -Session $session
                $session = $null
                Stop-Rime -Confirm:$false
                Start-Rime @traits
                $session = New-RimeSession
                Show-RimeCurrentState -Session $session
                continue
            }
            if ($line.StartsWith(':', [StringComparison]::Ordinal)) {
                Write-Warning "Unknown command '$line'. Enter :help for commands."
                continue
            }

            if ($line.Length -eq 0) {
                $line = "`r"
            }
            $result = Send-RimeKey -Session $session -Sequence $line
            Show-RimeOperationResult -Result $result
        }
        catch {
            if ($null -eq $session) { throw }
            Write-Warning $_.Exception.Message
        }
    }
}
finally {
    if ($null -ne $session) {
        Remove-RimeSession -Session $session -ErrorAction Continue
    }

    Stop-Rime -Confirm:$false -ErrorAction Continue
    Unregister-Event -SubscriptionId $subscription.Id -ErrorAction SilentlyContinue
    Remove-Job -Id $subscription.Id -Force -ErrorAction SilentlyContinue
}
