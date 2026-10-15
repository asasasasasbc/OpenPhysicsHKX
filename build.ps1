param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw '.NET Framework 4.x x64 compiler required.' }
$out = Join-Path $root 'bin\Release'
[IO.Directory]::CreateDirectory($out) | Out-Null
function Compile([string[]]$arguments) {
    & $csc @arguments
    if ($LASTEXITCODE -ne 0) { throw "Compiler failed ($LASTEXITCODE)." }
}
$core = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\OpenPhysicsHKX.Core') -Filter '*.cs' -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName })
$dll = Join-Path $out 'OpenPhysicsHKX.Core.dll'
Compile (@('/nologo','/target:library','/platform:x64','/optimize+','/reference:System.Numerics.dll',"/out:$dll") + $core)
$cli = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\OpenPhysicsHKX.Cli') -Filter '*.cs' -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName })
Compile (@('/nologo','/target:exe','/platform:x64','/optimize+','/reference:System.Web.Extensions.dll',"/reference:$dll",('/out:' + (Join-Path $out 'OpenPhysicsHKX.Cli.exe'))) + $cli)
if ($Test) {
    $exe = Join-Path $out 'OpenPhysicsHKX.Tests.exe'
    Compile @('/nologo','/target:exe','/platform:x64','/optimize+',"/reference:$dll",("/out:$exe"),(Join-Path $root 'tests\OpenPhysicsHKX.Tests\Program.cs'))
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Tests failed ($LASTEXITCODE)." }
}
Write-Host "Built: $dll"
