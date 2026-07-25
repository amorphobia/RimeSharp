#requires -Version 5.1

<#
.SYNOPSIS
    Runs an interactive API console for the RimeSharp.PowerShell module.

.DESCRIPTION
    Demonstrates the PowerShell cmdlets with a command loop modeled after
    librime's rime_api_console and the RimeSharp.Test console application.

    SharedDataDir and UserDataDir are resolved by Start-Rime from the current
    PowerShell location. On Windows, set LIBRIME_LIB_DIR or add the directory
    containing rime.dll to PATH before starting this script.

.PARAMETER SharedDataDir
    Directory containing shared RIME data.

.PARAMETER UserDataDir
    Directory containing user-specific RIME data.

.PARAMETER AppName
    Application name reported to librime.

.EXAMPLE
    $env:LIBRIME_LIB_DIR = 'C:\path\to\librime\bin'
    pwsh .\RimeSharp.PowerShell\Examples\ApiConsole.ps1 `
        -SharedDataDir .\shared `
        -UserDataDir .\user
#>

[CmdletBinding()]
param(
    [string]$SharedDataDir = 'shared',

    [string]$UserDataDir = 'user',

    [string]$AppName = 'rime.console'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$moduleDirectory = Split-Path -Parent $PSScriptRoot
$moduleManifest = Join-Path $moduleDirectory 'RimeSharp.PowerShell.psd1'

Import-Module -Name $moduleManifest -ErrorAction Stop

function Write-ConsoleError {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $Host.UI.WriteErrorLine($Message)
}

function Show-RimeStatusSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Status
    )

    Write-Host ("schema: {0} / {1}" -f $Status.SchemaId, $Status.SchemaName)

    $flags = [System.Collections.Generic.List[string]]::new()
    if ($Status.IsDisabled) {
        $flags.Add('disabled')
    }
    if ($Status.IsComposing) {
        $flags.Add('composing')
    }
    if ($Status.IsAsciiMode) {
        $flags.Add('ascii')
    }
    if ($Status.IsFullShape) {
        $flags.Add('full_shape')
    }
    if ($Status.IsSimplified) {
        $flags.Add('simplified')
    }
    if ($Status.IsTraditional) {
        $flags.Add('traditional')
    }
    if ($Status.IsAsciiPunct) {
        $flags.Add('ascii_punct')
    }

    Write-Host ("status: {0}" -f ($flags -join ' '))
}

function Show-RimeCompositionSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Composition
    )

    if ($null -eq $Composition.Preedit) {
        return
    }

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

    Write-Host ($line.ToString())
}

function Show-RimeMenuSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context
    )

    $menu = $Context.Menu
    if ($menu.NumCandidates -eq 0) {
        return
    }

    $pageMarker = if ($menu.IsLastPage) { '$' } else { ' ' }
    Write-Host ("page: {0}{1} (of size {2})" -f
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
            Write-Host ("{0}. [{1}]{2}" -f $label, $candidate.Text, $comment)
        }
        else {
            Write-Host ("{0}.  {1}{2}" -f $label, $candidate.Text, $comment)
        }
    }
}

function Show-RimeContextSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Context
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

function Show-RimeResponse {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Response
    )

    if (-not [string]::IsNullOrEmpty($Response.Commit)) {
        Write-Host ("commit: {0}" -f $Response.Commit)
    }

    Show-RimeStatusSnapshot -Status $Response.Status
    Show-RimeContextSnapshot -Context $Response.Context
}

function Show-RimeState {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Session
    )

    $commit = Get-RimeCommit -Session $Session
    $status = Get-RimeStatus -Session $Session
    $context = Get-RimeContext -Session $Session

    if (-not [string]::IsNullOrEmpty($commit)) {
        Write-Host ("commit: {0}" -f $commit)
    }

    Show-RimeStatusSnapshot -Status $status
    Show-RimeContextSnapshot -Context $context
}

function Receive-AndShowRimeNotification {
    $notifications = @(Receive-RimeNotification)
    foreach ($notification in $notifications) {
        Write-Host ("message: [{0}] [{1}] [{2}]" -f
            $notification.SessionId,
            $notification.MessageType,
            $notification.MessageValue)

        if (-not [string]::IsNullOrEmpty($notification.OptionName) -and
            $null -ne $notification.OptionState -and
            $null -ne $notification.Session) {
            try {
                $label = Get-RimeStateLabel `
                    -Name $notification.OptionName `
                    -State ([bool]$notification.OptionState) `
                    -Session $notification.Session

                if (-not [string]::IsNullOrEmpty($label)) {
                    Write-Host ("updated option: {0} = {1} // {2}" -f
                        $notification.OptionName,
                        $notification.OptionState,
                        $label)
                }
            }
            catch {
                Write-ConsoleError (
                    "Unable to resolve the option state label: {0}" -f
                    $_.Exception.Message)
            }
        }
    }
}

function ConvertTo-ZeroBasedIndex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $parsed = 0
    if (-not [int]::TryParse($Value, [ref]$parsed) -or $parsed -lt 1) {
        Write-ConsoleError (
            "Invalid {0}: '{1}'. Enter a positive integer." -f
            $Description,
            $Value)
        return $null
    }

    return $parsed - 1
}

function Show-ApiConsoleHelp {
    Write-Host @'
Commands:
  help
      Show this command list.
  print
      Print the current commit, status, composition, and candidate page.
  print schema list
      List schemas and mark the current schema.
  print available schemas
      List schemas available through the RIME switcher.
  print selected schemas
      List schemas selected in the RIME switcher.
  select schema SCHEMA_ID
      Select a schema.
  print candidate list
      Enumerate all candidates using one-based display indexes.
  select candidate INDEX
      Select a candidate on the current page.
  delete INDEX
      Delete a candidate by its global display index.
  delete on current page INDEX
      Delete a candidate by its current-page display index.
  highlight candidate INDEX
      Highlight a candidate on the current page.
  prev
      Move to the previous candidate page.
  next
      Move to the next candidate page.
  get option OPTION
      Print the boolean value of an option.
  set option OPTION
      Enable an option.
  set option !OPTION
      Disable an option.
  key event KEY_CODE [MASK]
      Send a raw key event and optional modifier mask.
  synchronize
      Report that user-data synchronization is not exposed by this module.
  reload
      Recreate the RIME engine and session.
  exit
      Stop RIME and leave the console.

Any other line is passed to Send-RimeKey as a key sequence. An empty line sends
a carriage return, matching librime's rime_api_console behavior.
'@
}

function Start-ApiConsoleSession {
    Write-Host 'initializing...'
    $startedSession = Start-Rime `
        -AppName $AppName `
        -SharedDataDir $SharedDataDir `
        -UserDataDir $UserDataDir
    Write-Host 'ready.'
    return $startedSession
}

function Invoke-ApiConsoleCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Line,

        [Parameter(Mandatory = $true)]
        [object]$Session
    )

    switch -Regex ($Line) {
        '^help$' {
            Show-ApiConsoleHelp
            return $true
        }

        '^print$' {
            Show-RimeState -Session $Session
            return $true
        }

        '^print schema list$' {
            $schemas = @(Get-RimeSchema -Session $Session)
            Write-Host 'schema list:'

            for ($i = 0; $i -lt $schemas.Count; $i++) {
                $currentMarker = if ($schemas[$i].IsCurrent) { '*' } else { ' ' }
                Write-Host ("{0}{1}. {2} [{3}]" -f
                    $currentMarker,
                    ($i + 1),
                    $schemas[$i].SchemaId,
                    $schemas[$i].Name)
            }

            $current = $schemas | Where-Object IsCurrent | Select-Object -First 1
            if ($null -ne $current) {
                Write-Host ("current schema: [{0}]" -f $current.SchemaId)
            }
            return $true
        }

        '^print available schemas$' {
            $schemas = @(Get-RimeSwitcherSchema -Available)
            Write-Host 'available schemas:'
            for ($i = 0; $i -lt $schemas.Count; $i++) {
                Write-Host ("{0}. {1} [{2}]" -f
                    ($i + 1),
                    $schemas[$i].SchemaId,
                    $schemas[$i].Name)
            }
            return $true
        }

        '^print selected schemas$' {
            $schemas = @(Get-RimeSwitcherSchema -Selected)
            Write-Host 'selected schemas:'
            for ($i = 0; $i -lt $schemas.Count; $i++) {
                Write-Host ("{0}. {1} [{2}]" -f
                    ($i + 1),
                    $schemas[$i].SchemaId,
                    $schemas[$i].Name)
            }
            return $true
        }

        '^select schema (.+)$' {
            $schemaId = $Matches[1]
            Set-RimeSchema -SchemaId $schemaId -Session $Session
            Write-Host ("selected schema: [{0}]" -f $schemaId)
            Show-RimeState -Session $Session
            return $true
        }

        '^print candidate list$' {
            $candidates = @(Get-RimeCandidate -Session $Session)
            if ($candidates.Count -eq 0) {
                Write-Host 'no candidates.'
                return $true
            }

            foreach ($candidate in $candidates) {
                $comment = if ([string]::IsNullOrEmpty($candidate.Comment)) {
                    ''
                }
                else {
                    " ($($candidate.Comment))"
                }

                Write-Host ("{0}. {1}{2}" -f
                    ($candidate.Index + 1),
                    $candidate.Text,
                    $comment)
            }
            return $true
        }

        '^select candidate (.+)$' {
            $index = ConvertTo-ZeroBasedIndex `
                -Value $Matches[1] `
                -Description 'candidate index'
            if ($null -ne $index) {
                Select-RimeCandidate `
                    -Index $index `
                    -OnCurrentPage `
                    -Session $Session
                Show-RimeState -Session $Session
            }
            return $true
        }

        '^delete on current page (.+)$' {
            $index = ConvertTo-ZeroBasedIndex `
                -Value $Matches[1] `
                -Description 'candidate index'
            if ($null -ne $index) {
                Remove-RimeCandidate `
                    -Index $index `
                    -OnCurrentPage `
                    -Session $Session `
                    -Confirm:$false
                Show-RimeState -Session $Session
            }
            return $true
        }

        '^delete (.+)$' {
            $index = ConvertTo-ZeroBasedIndex `
                -Value $Matches[1] `
                -Description 'candidate index'
            if ($null -ne $index) {
                Remove-RimeCandidate `
                    -Index $index `
                    -Session $Session `
                    -Confirm:$false
                Show-RimeState -Session $Session
            }
            return $true
        }

        '^highlight candidate (.+)$' {
            $index = ConvertTo-ZeroBasedIndex `
                -Value $Matches[1] `
                -Description 'candidate index'
            if ($null -ne $index) {
                Invoke-RimeHighlight `
                    -Index $index `
                    -OnCurrentPage `
                    -Session $Session
                Show-RimeState -Session $Session
            }
            return $true
        }

        '^set option (.+)$' {
            $option = $Matches[1]
            $isOn = $true

            if ($option.StartsWith('!')) {
                $isOn = $false
                $option = $option.Substring(1)
            }

            if ([string]::IsNullOrWhiteSpace($option)) {
                Write-ConsoleError 'An option name is required.'
                return $true
            }

            Set-RimeOption `
                -Name $option `
                -Value $isOn `
                -Session $Session
            Write-Host ("{0} set {1}." -f
                $option,
                $(if ($isOn) { 'on' } else { 'off' }))
            return $true
        }

        '^get option (.+)$' {
            $option = $Matches[1]
            $value = Get-RimeOption -Name $option -Session $Session
            Write-Host ("{0} = {1}" -f $option, $value)
            return $true
        }

        '^prev$' {
            Set-RimePage -Direction Previous -Session $Session
            Show-RimeState -Session $Session
            return $true
        }

        '^next$' {
            Set-RimePage -Direction Next -Session $Session
            Show-RimeState -Session $Session
            return $true
        }

        '^key event (-?\d+)(?:\s+(-?\d+))?$' {
            $keyCode = [int]$Matches[1]
            $mask = if ($Matches.ContainsKey(2)) {
                [int]$Matches[2]
            }
            else {
                0
            }

            $handled = Send-RimeKeyEvent `
                -KeyCode $keyCode `
                -Mask $mask `
                -Session $Session
            Write-Host ("handled: {0}" -f $handled)
            if ($handled) {
                Show-RimeState -Session $Session
            }
            return $true
        }

        '^synchronize$' {
            Write-ConsoleError (
                'User-data synchronization is not exposed by ' +
                'RimeSharp.PowerShell.')
            return $true
        }

        '^reload$' {
            $script:reloadRequested = $true
            return $true
        }

        '^exit$' {
            $script:exitRequested = $true
            return $true
        }
    }

    return $false
}

$script:exitRequested = $false
$script:reloadRequested = $false
$session = $null

Register-RimeNotification

try {
    $session = Start-ApiConsoleSession
    Receive-AndShowRimeNotification
    Show-ApiConsoleHelp

    while (-not $script:exitRequested) {
        Write-Host 'rime> ' -NoNewline
        $line = [Console]::ReadLine()

        if ($null -eq $line) {
            break
        }

        if ($line.Length -eq 0) {
            $line = "`r"
        }

        try {
            $handled = Invoke-ApiConsoleCommand `
                -Line $line `
                -Session $session

            Receive-AndShowRimeNotification

            if ($script:reloadRequested) {
                $script:reloadRequested = $false
                Stop-Rime -Session $session
                $session = $null
                try {
                    $session = Start-ApiConsoleSession
                }
                catch {
                    $script:exitRequested = $true
                    throw
                }
                Receive-AndShowRimeNotification
                continue
            }

            if ($handled) {
                continue
            }

            $response = Send-RimeKey `
                -Sequence $line `
                -Session $session
            Show-RimeResponse -Response $response
            Receive-AndShowRimeNotification
        }
        catch {
            Write-ConsoleError $_.Exception.Message
        }
    }
}
finally {
    if ($null -ne $session) {
        try {
            Stop-Rime -Session $session
        }
        catch {
            Write-ConsoleError (
                "Failed to stop RIME cleanly: {0}" -f $_.Exception.Message)
        }
    }
}
