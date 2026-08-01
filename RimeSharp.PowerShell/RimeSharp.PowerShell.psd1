@{
    RootModule           = 'RimeSharp.PowerShell.psm1'
    ModuleVersion        = '0.2.0'
    GUID                 = 'a3f1b8c7-2d4e-5f6a-8b9c-0d1e2f3a4b5c'
    Author               = 'RimeInn Contributors'
    CompanyName          = 'RimeInn'
    Copyright            = '(c) RimeInn Contributors. Apache 2.0.'
    Description          = 'PowerShell cmdlets for RIME Input Method Engine'
    PowerShellVersion    = '7.4'
    CompatiblePSEditions = @('Core')
    RequiredAssemblies   = @()
    RequiredModules      = @()
    FunctionsToExport    = @()
    CmdletsToExport      = @(
        'Start-Rime'
        'Stop-Rime'
        'New-RimeSession'
        'Remove-RimeSession'
        'Deploy-Rime'
        'Get-RimeConfig'
        'Send-RimeKey'
        'Send-RimeKeyEvent'
        'Receive-RimeCommit'
        'Get-RimeInput'
        'Get-RimeContext'
        'Get-RimeStatus'
        'Get-RimeCandidate'
        'Select-RimeCandidate'
        'Set-RimePage'
        'Get-RimeSchema'
        'Set-RimeSchema'
        'Get-RimeSwitcherSchema'
        'Get-RimeOption'
        'Set-RimeOption'
        'Get-RimeStateLabel'
        'Remove-RimeCandidate'
        'Invoke-RimeHighlight'
        'Get-RimeNotificationSource'
        'Receive-RimeNotification'
    )
    VariablesToExport    = @()
    AliasesToExport      = @()
    PrivateData          = @{
        PSData = @{
            Tags       = @('RIME', 'InputMethod', 'IME')
            LicenseUri = 'https://github.com/rimeinn/RimeSharp/blob/master/LICENSE.txt'
            ProjectUri = 'https://github.com/rimeinn/RimeSharp'
        }
    }
}
