param([string]$OutputDirectory = (Join-Path $PSScriptRoot '.build'))
$ErrorActionPreference = 'Stop'
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($taskOutput)
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Windows C# compiler not found.' }
$taskNode = (Get-Command node.exe -ErrorAction Stop).Source
$taskCodex = (Get-Command codex.exe -ErrorAction Stop).Source
@{ Node = $taskNode; Codex = $taskCodex } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'runtime-paths.json') -Encoding utf8
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
if ($taskSources.Count -eq 0) { throw 'Application sources not found.' }
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/win32manifest:$PSScriptRoot\overlay.manifest" "/out:$taskOutput\UsageOverlay.exe" @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Prototype build failed.' }
Write-Output "Built: $taskOutput\UsageOverlay.exe"
