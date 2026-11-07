$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskDirectory = $PSScriptRoot
& $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ /debug- /warn:4 /win32manifest:"$taskDirectory\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Configuration.dll /reference:System.Xml.dll /reference:System.Core.dll /out:"$taskDirectory\LockIn.exe" "$taskDirectory\LockIn.cs" "$taskDirectory\Checks.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output (Join-Path $taskDirectory 'LockIn.exe')
