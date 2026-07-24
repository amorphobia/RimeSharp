@{
    RootModule           = 'RimeSharp.PowerShell.psm1'
    ModuleVersion        = '0.1.0'
    GUID                 = 'a3f1b8c7-2d4e-5f6a-8b9c-0d1e2f3a4b5c'
    Author               = 'RimeInn Contributors'
    CompanyName          = 'RimeInn'
    Copyright            = '(c) RimeInn Contributors. Apache 2.0.'
    Description          = 'PowerShell cmdlets for RIME Input Method Engine'
    PowerShellVersion    = '5.1'
    RequiredAssemblies   = @()
    RequiredModules      = @()
    FunctionsToExport    = @()
    CmdletsToExport      = @(
        'Start-Rime'
        'Stop-Rime'
        'Send-RimeKey'
        'Send-RimeKeyEvent'
        'Get-RimeCommit'
        'Get-RimeContext'
        'Get-RimeStatus'
        'Select-RimeCandidate'
        'Set-RimePage'
        'Get-RimeSchema'
        'Set-RimeSchema'
        'Get-RimeOption'
        'Set-RimeOption'
        'Remove-RimeCandidate'
        'Invoke-RimeHighlight'
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
