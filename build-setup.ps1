$ErrorActionPreference = 'Stop'
$taskDirectory = $PSScriptRoot
& (Join-Path $taskDirectory 'build.ps1')
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskCompilerArgs = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4'
    "/win32manifest:$taskDirectory\app.manifest"
    "/win32icon:$taskDirectory\assets\lockin.ico"
    '/reference:System.Windows.Forms.dll', '/reference:Microsoft.CSharp.dll'
    "/resource:$taskDirectory\LockIn.exe,LockIn.exe"
    "/resource:$taskDirectory\LockIn.exe.config,LockIn.exe.config"
    "/out:$taskDirectory\LockIn-Setup.exe"
    "$taskDirectory\installer\Setup.cs"
)
& $taskCompiler @taskCompilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
Write-Output (Join-Path $taskDirectory 'LockIn-Setup.exe')
