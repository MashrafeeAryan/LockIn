$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$taskReport = Join-Path $PSScriptRoot 'checks.txt'
$taskExecutable = Join-Path $PSScriptRoot 'LockIn.exe'
$taskRun = Start-Process -FilePath $taskExecutable -ArgumentList @('--self-test', ('"' + $taskReport + '"')) -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath $taskReport
if ($taskRun.ExitCode -ne 0) { throw 'LockIn component checks failed.' }
