<#
.SYNOPSIS
    Runs native-backed RimeSharp.PowerShell 0.2 smoke scenarios.

.DESCRIPTION
    Build the module first and provide working librime data directories.
    Deterministic failure injection uses the shim described in
    NativeScenarioMatrix.md and is not simulated by this script.
#>

[CmdletBinding()]
param(
    [string]$ModulePath = (Join-Path $PSScriptRoot '..\RimeSharp.PowerShell.psd1'),
    [Parameter(Mandatory)]
    [string]$SharedDataDir,
    [Parameter(Mandatory)]
    [string]$UserDataDir,
    [string]$SchemaId,
    [string]$CommitSequence,
    [string]$ConfigId,
    [string]$StringConfigPath,
    [switch]$RunDefaultConfigShapeScenarios,
    [switch]$RunWorkspaceDeployment,
    [switch]$RunConfigFileDeployment,
    [string]$DeploymentConfigFile = 'default.yaml',
    [string]$DeploymentVersionKey = 'config_version'
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param(
        $Expected,
        $Actual,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-ErrorId {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Action,
        [Parameter(Mandatory)]
        [string]$ExpectedId
    )

    try {
        & $Action
    }
    catch {
        if (-not $_.FullyQualifiedErrorId.StartsWith($ExpectedId)) {
            throw "Expected error '$ExpectedId', got '$($_.FullyQualifiedErrorId)'."
        }
        return
    }

    throw "Expected error '$ExpectedId', but the operation succeeded."
}

function Invoke-ZeroSessionStartup {
    param(
        [hashtable]$Traits,
        [switch]$FullMaintenance
    )

    if ($FullMaintenance) {
        $output = @(Start-Rime @Traits -FullMaintenance)
    }
    else {
        $output = @(Start-Rime @Traits)
    }
    Assert-Equal 0 $output.Count 'Start-Rime must not emit a session.'
    Stop-Rime -Confirm:$false
}

Import-Module $ModulePath -Force
$traits = @{
    AppName = 'RimeSharp.PowerShell.NativeIntegration'
    SharedDataDir = $SharedDataDir
    UserDataDir = $UserDataDir
}

Stop-Rime -Confirm:$false -ErrorAction SilentlyContinue

Write-Host 'SCENARIO default maintenance startup with zero sessions'
Invoke-ZeroSessionStartup -Traits $traits

Write-Host 'SCENARIO full maintenance startup with zero sessions'
Invoke-ZeroSessionStartup -Traits $traits -FullMaintenance

if ($RunWorkspaceDeployment) {
    Write-Host 'SCENARIO workspace deployment followed by startup'
    $deployment = Deploy-Rime @traits -Workspace -Confirm:$false
    Assert-True $deployment.Succeeded 'Workspace deployment must succeed.'
    Assert-True $deployment.CleanupSucceeded 'Deployment cleanup must succeed.'
    Invoke-ZeroSessionStartup -Traits $traits
}

if ($RunConfigFileDeployment) {
    Write-Host 'SCENARIO config-file deployment followed by startup'
    $deployment = Deploy-Rime @traits `
        -ConfigFile $DeploymentConfigFile `
        -VersionKey $DeploymentVersionKey `
        -Confirm:$false
    Assert-True $deployment.Succeeded 'Config-file deployment must succeed.'
    Assert-True $deployment.CleanupSucceeded (
        'Config-file deployment cleanup must succeed.')
    Invoke-ZeroSessionStartup -Traits $traits
}

Write-Host 'SCENARIO multiple explicit sessions and isolation'
Start-Rime @traits
$first = New-RimeSession
$second = New-RimeSession
try {
    Assert-True ($first.Id -ne 0) 'The first session ID must be nonzero.'
    Assert-True ($second.Id -ne 0) 'The second session ID must be nonzero.'
    Assert-True ($first.Id -ne $second.Id) 'Live session IDs must be distinct.'

    if ($SchemaId) {
        Set-RimeSchema -Session $first -SchemaId $SchemaId
        Set-RimeSchema -Session $second -SchemaId $SchemaId
    }

    Set-RimeOption -Session $first -Name ascii_mode -Value $true
    Set-RimeOption -Session $second -Name ascii_mode -Value $false
    $firstOption = Get-RimeOption -Session $first -Name ascii_mode
    $secondOption = Get-RimeOption -Session $second -Name ascii_mode
    Assert-Equal $true $firstOption 'The first session option must be isolated.'
    Assert-Equal $false $secondOption 'The second session option must be isolated.'

    $firstResult = Send-RimeKeyEvent -Session $first -KeyCode 110
    Assert-True ($null -ne $firstResult.Status) 'Status must be non-null.'
    Assert-True ($null -ne $firstResult.Context) 'Context must be non-null.'
    Assert-True ($null -ne $firstResult.Notifications) 'Notifications must be non-null.'
    $secondInput = Get-RimeInput -Session $second
    Assert-Equal '' $secondInput 'Untouched second-session input must remain empty.'

    $secondResult = Send-RimeKeyEvent -Session $second -KeyCode 104
    Assert-True ($null -ne $secondResult.Input) 'Raw input must be non-null.'
    $unhandledResult = Send-RimeKeyEvent -Session $first -KeyCode 0
    Assert-Equal $false $unhandledResult.Handled (
        'Key code zero must produce a normal unhandled result.')
    $firstInput = Get-RimeInput -Session $first
    Assert-Equal $firstResult.Input $firstInput (
        'Processing the second session must not change first-session input.')
    $crossRouted = @(
        $firstResult.Notifications |
            Where-Object SessionId -NE $first.Id
    )
    Assert-Equal 0 $crossRouted.Count (
        'A key result must not capture another-session notification.')

    if ($CommitSequence) {
        $sequenceResult = Send-RimeKey -Session $first -Sequence $CommitSequence
        if ($null -ne $sequenceResult.Commit) {
            $unreadCommit = @(Receive-RimeCommit -Session $first)
            Assert-Equal 0 $unreadCommit.Count (
                'A key result must consume the commit it contains.')
        }
    }

    Assert-ErrorId {
        Deploy-Rime @traits -Workspace -Confirm:$false
    } 'RimeDeploymentWhileActive'

    $whatIfOutput = @(Deploy-Rime @traits -Workspace -WhatIf)
    Assert-Equal 0 $whatIfOutput.Count (
        'WhatIf deployment must not emit a result.')
    $null = Get-RimeStatus -Session $second

    Assert-ErrorId {
        Get-RimeConfig -ConfigId default -Shape $null
    } 'RimeConfigShapeInvalid'

    if ($ConfigId -and $StringConfigPath) {
        $configParameters = @{
            ConfigId = $ConfigId
            Path = $StringConfigPath
            Shape = [string]
        }
        $value = Get-RimeConfig @configParameters
        Assert-True ($value -is [string]) (
            'The scalar config value must be a string.')

        Assert-ErrorId {
            $missingParameters = @{
                ConfigId = $ConfigId
                Path = '__rimesharp_missing_required_scalar__'
                Shape = [string]
            }
            Get-RimeConfig @missingParameters
        } 'RimeConfigMaterializationFailed'

        if ($RunDefaultConfigShapeScenarios) {
            Write-Host 'SCENARIO explicit config shape materialization'
            $fixedShape = [ordered]@{
                config_version = [string]
                menu = [ordered]@{
                    page_size = [int]
                }
            }
            $fixedMap = Get-RimeConfig `
                -ConfigId $ConfigId `
                -Shape $fixedShape
            Assert-Equal $value $fixedMap.config_version (
                'Fixed-map scalar projection must match the tiny projection.')
            Assert-Equal 5 $fixedMap.menu.page_size (
                'Nested fixed-map integer projection must be materialized.')

            $schemaShape = [RimeConfigShape]::List(
                [ordered]@{ schema = [string] })
            $schemas = Get-RimeConfig `
                -ConfigId $ConfigId `
                -Path schema_list `
                -Shape $schemaShape
            Assert-True ($schemas.Count -ge 2) (
                'The default schema list must contain at least two entries.')
            Assert-Equal 'luna_pinyin' $schemas[0].schema (
                'The first default schema must be luna_pinyin.')

            $switchKeys = Get-RimeConfig `
                -ConfigId $ConfigId `
                -Path ascii_composer/switch_key `
                -Shape ([RimeConfigShape]::MapOf([string]))
            Assert-True $switchKeys.ContainsKey('Shift_L') (
                'MapOf must preserve runtime config keys.')
            Assert-Equal 'inline_ascii' $switchKeys['Shift_L'] (
                'MapOf must materialize values with the declared shape.')

            $emptyListOutput = @(
                Get-RimeConfig `
                    -ConfigId $ConfigId `
                    -Path __rimesharp_authoritative_empty_list__ `
                    -Shape ([RimeConfigShape]::List([string]))
            )
            Assert-Equal 1 $emptyListOutput.Count (
                'An empty root list must remain one pipeline object.')
            Assert-Equal 0 $emptyListOutput[0].Count (
                'An authoritative empty list must materialize as object[0].')

            $emptyMap = Get-RimeConfig `
                -ConfigId $ConfigId `
                -Path __rimesharp_authoritative_empty_map__ `
                -Shape ([RimeConfigShape]::MapOf([string]))
            Assert-Equal 0 $emptyMap.Count (
                'An authoritative empty MapOf must materialize as an empty map.')
        }
    }

    Remove-RimeSession -Session $first
    Remove-RimeSession -Session $first
    $null = Get-RimeStatus -Session $second
}
finally {
    Remove-RimeSession -Session $second -ErrorAction Continue
    Stop-Rime -Confirm:$false -ErrorAction Continue
}

Write-Host 'SCENARIO stale session rejection after lifecycle restart'
Start-Rime @traits
$current = New-RimeSession
try {
    Assert-ErrorId {
        Get-RimeInput -Session $second
    } 'RimeSessionInvalid'
}
finally {
    Remove-RimeSession -Session $current -ErrorAction Continue
    Stop-Rime -Confirm:$false -ErrorAction Continue
}

Write-Host 'Native integration smoke scenarios passed.'
