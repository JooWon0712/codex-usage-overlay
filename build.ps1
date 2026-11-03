$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Windows C# compiler not found.' }
$taskNode = (Get-Command node.exe -ErrorAction Stop).Source
$taskCodex = (Get-Command codex.exe -ErrorAction Stop).Source
@{ Node = $taskNode; Codex = $taskCodex } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime-paths.json') -Encoding utf8
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/win32manifest:$PSScriptRoot\overlay.manifest" "/out:$PSScriptRoot\UsageOverlay.exe" "$PSScriptRoot\Overlay.cs"
if ($LASTEXITCODE -ne 0) { throw 'Prototype build failed.' }
Write-Output "Built: $PSScriptRoot\UsageOverlay.exe"
