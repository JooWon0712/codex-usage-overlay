[CmdletBinding()]
param(
    [string]$PayloadPath = (Join-Path $PSScriptRoot 'dist\codex-usage-overlay'),
    [string]$CompilerPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'package.ps1')
$script:CuoSetupFile = 'codex-usage-overlay-setup-windows-x64.exe'
$script:CuoSetupAppId = '{18D4E149-28A8-4F05-B98E-10B66B725C54}_is1'

function Resolve-CuoSetupCompiler {
    param([string]$Path)
    if (-not $Path) {
        $candidates = @()
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $candidates += $command.Source }
        if (${env:ProgramFiles(x86)}) { $candidates += (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe') }
        if ($env:ProgramFiles) { $candidates += (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe') }
        $candidates += (Join-Path $PSScriptRoot '.build\tools\inno-setup-6.7.4\ISCC.exe')
        $Path = @($candidates | Where-Object { [IO.File]::Exists($_) } | Select-Object -First 1)[0]
    }
    if (-not $Path -or -not [IO.File]::Exists($Path)) {
        throw 'Inno Setup 6.7.4 ISCC.exe was not found. Supply -CompilerPath, or explicitly provision the pinned workspace compiler with tools/prepare-inno.ps1. No tools were installed.'
    }
    $resolved = [IO.Path]::GetFullPath($Path)
    # Official 6.7.4 compiler binaries report FileVersion 0.0.0.0. The .iss
    # verifies the actual ISPP Ver value before any installer can be emitted.
    return $resolved
}

function Get-CuoSetupVersion {
    param([string]$Payload)
    $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src\Program.cs'))
    $assembly = [regex]::Match($source, 'AssemblyVersion\("(\d+\.\d+\.\d+\.0)"\)')
    $file = [regex]::Match($source, 'AssemblyFileVersion\("(\d+\.\d+\.\d+\.0)"\)')
    if (-not $assembly.Success -or -not $file.Success -or $assembly.Groups[1].Value -ne $file.Groups[1].Value) {
        throw 'Source assembly/file versions must match X.Y.Z.0.'
    }
    $expected = [Version]$assembly.Groups[1].Value
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Payload 'UsageOverlay.exe'))
    $actualVersion = New-Object Version($actual.FileMajorPart, $actual.FileMinorPart, $actual.FileBuildPart, $actual.FilePrivatePart)
    if ($actualVersion -ne $expected) { throw "The existing portable payload is version $actualVersion; source is $expected. Validate a matching payload before building Setup." }
    return $expected.ToString()
}

function Remove-CuoSetupStage {
    param([string]$Stage)
    $build = Join-Path $PSScriptRoot '.build'
    $resolved = Assert-CuoPortableChildPath $Stage $build
    if ([IO.Path]::GetDirectoryName($resolved) -ine [IO.Path]::GetFullPath($build) -or [IO.Path]::GetFileName($resolved) -notmatch '^setup-stage-[0-9a-f]{32}$') { throw 'Unexpected Setup staging directory.' }
    Assert-CuoPortableNoReparsePoint $resolved $PSScriptRoot
    if (-not (Test-Path -LiteralPath $resolved)) { return }
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $dirs = New-Object 'System.Collections.Generic.List[string]'
    $files = New-Object 'System.Collections.Generic.List[string]'
    $pending.Push($resolved)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop(); $dirs.Add($directory)
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force)) {
            $null = Assert-CuoPortableChildPath $item.FullName $resolved
            Assert-CuoPortableNoReparsePoint $item.FullName $PSScriptRoot
            if ($item.PSIsContainer) { $pending.Push($item.FullName) } else { $files.Add($item.FullName) }
        }
    }
    foreach ($file in $files) { Remove-Item -LiteralPath $file -Force }
    for ($index = $dirs.Count - 1; $index -ge 0; $index--) { [IO.Directory]::Delete($dirs[$index], $false) }
}

function Invoke-CuoSetupBuild {
    param([string]$Payload, [string]$Compiler, [string]$Output)
    $project = [IO.Path]::GetFullPath($PSScriptRoot)
    $payloadDirectory = Assert-CuoPortableChildPath $Payload $project
    Assert-CuoPortablePayload $payloadDirectory $project
    $outputRoot = Assert-CuoPortableChildPath $Output $project
    Assert-CuoPortableNoReparsePoint $outputRoot $project
    $compilerExe = Resolve-CuoSetupCompiler $Compiler
    $version = Get-CuoSetupVersion $payloadDirectory
    $buildRoot = Join-Path $project '.build'
    Assert-CuoPortableNoReparsePoint $buildRoot $project
    $null = [IO.Directory]::CreateDirectory($buildRoot)
    $stage = Join-Path $buildRoot ('setup-stage-' + [Guid]::NewGuid().ToString('N'))
    $null = [IO.Directory]::CreateDirectory($stage)
    $arguments = @('/Qp', ('/O' + $stage), ('/DPayloadDirectory=' + $payloadDirectory), ('/DRepositoryRoot=' + $project), ('/DAppVersionFull=' + $version))
    $hashes = @{}
    for ($index = 0; $index -lt $script:CuoPortableFiles.Count; $index++) {
        $name = $script:CuoPortableFiles[$index]
        $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $payloadDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        $arguments += ('/DPayloadHash' + $index + '=' + $hashes[$name])
    }
    $arguments += (Join-Path $project 'installer\codex-usage-overlay.iss')
    try {
        & $compilerExe @arguments
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE. Existing Setup output was preserved." }
        foreach ($name in $script:CuoPortableFiles) {
            if ((Get-FileHash -LiteralPath (Join-Path $payloadDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashes[$name]) { throw 'Portable payload changed while Setup was compiling. Output was not published.' }
        }
        $compiled = Join-Path $stage $script:CuoSetupFile
        if (-not [IO.File]::Exists($compiled) -or (Get-Item -LiteralPath $compiled).Length -eq 0) { throw 'Setup output is missing or empty.' }
        $null = [IO.Directory]::CreateDirectory($outputRoot)
        $destination = Join-Path $outputRoot $script:CuoSetupFile
        Assert-CuoPortableNoReparsePoint $destination $project
        if ([IO.File]::Exists($destination)) {
            $backup = Join-Path $stage 'previous-setup.exe'
            [IO.File]::Replace($compiled, $destination, $backup)
        } else { [IO.File]::Move($compiled, $destination) }
        Write-Output ('Setup executable: ' + $destination)
        Write-Output ('Setup SHA-256: ' + (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant())
        Write-Output 'The existing portable payload was packaged. No installation, application launch or automatic startup registration was performed.'
    }
    finally { Remove-CuoSetupStage $stage }
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-CuoSetupBuild -Payload $PayloadPath -Compiler $CompilerPath -Output $OutputDirectory }
