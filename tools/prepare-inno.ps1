# Developer/CI compiler bootstrap. Uses Inno's portable mode, not a registered installation.
param([string]$Destination = (Join-Path $PSScriptRoot '..\.build\tools\inno-setup-6.7.4'))
$ErrorActionPreference = 'Stop'
$taskVersion = '6.7.4'
$taskSource = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe'
# Official immutable release asset digest:
# https://github.com/jrsoftware/issrc/releases/expanded_assets/is-6_7_4
$taskSourceHash = 'a549929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskRoots = @([IO.Path]::GetFullPath((Join-Path $taskProject '.build')))
if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { $taskRoots += [IO.Path]::GetFullPath($env:RUNNER_TEMP) }
$taskDestination = [IO.Path]::GetFullPath($Destination).TrimEnd([IO.Path]::DirectorySeparatorChar)
$taskRoot = $taskRoots | Where-Object {
    $taskDestination.StartsWith($_.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
} | Select-Object -First 1
if (-not $taskRoot) { throw 'Compiler destination must be a child of this project .build or RUNNER_TEMP.' }

function Assert-InnoBootstrapPath([string]$Path) {
    $taskFull = [IO.Path]::GetFullPath($Path)
    if (-not $taskFull.StartsWith($taskRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Compiler bootstrap path escaped its expected build directory.'
    }
    $taskCurrent = $taskFull
    while (-not [string]::IsNullOrEmpty($taskCurrent)) {
        if ((Test-Path -LiteralPath $taskCurrent) -and
            ((Get-Item -LiteralPath $taskCurrent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Compiler bootstrap does not follow reparse points.'
        }
        $taskCurrent = [IO.Path]::GetDirectoryName($taskCurrent)
    }
    return $taskFull
}

function Assert-InnoCompiler([string]$Path) {
    $null = Assert-InnoBootstrapPath $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Pinned Inno Setup compiler was not produced.' }
    # Official 6.7.4 compiler files report FileVersion 0.0.0.0. The supported
    # preprocessor Ver constant identifies the engine that actually compiles.
    $taskProbe = Assert-InnoBootstrapPath (Join-Path ([IO.Path]::GetDirectoryName($Path)) ('inno-version-' + [Guid]::NewGuid().ToString('N') + '.iss'))
    $taskProbeSource = @'
#if Ver != EncodeVer(6,7,4)
  #error The compiler engine must be Inno Setup 6.7.4.
#endif
[Setup]
AppName=Compiler version verification
AppVersion=1.0
DefaultDirName={tmp}\inno-version-probe
Output=no
OutputDir=.
'@
    try {
        [IO.File]::WriteAllText($taskProbe, $taskProbeSource, [Text.UTF8Encoding]::new($false))
        $taskProbeOutput = & $Path /Q $taskProbe 2>&1
        if ($LASTEXITCODE -ne 0) { throw ('Inno Setup 6.7.4 engine verification failed. ' + ($taskProbeOutput -join ' ')) }
    }
    finally {
        if (Test-Path -LiteralPath $taskProbe) { Remove-Item -LiteralPath $taskProbe -Force }
    }
}

function Get-InnoCoreHashes([string]$Directory) {
    $taskHashes = [ordered]@{}
    foreach ($taskName in @('ISCC.exe', 'ISCmplr.dll', 'ISPP.dll')) {
        $taskCore = Assert-InnoBootstrapPath (Join-Path $Directory $taskName)
        if (-not (Test-Path -LiteralPath $taskCore -PathType Leaf)) { throw "Pinned compiler core is missing: $taskName" }
        $taskHashes[$taskName] = (Get-FileHash -LiteralPath $taskCore -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $taskHashes
}

$taskDestination = Assert-InnoBootstrapPath $taskDestination
$taskCompiler = Join-Path $taskDestination 'ISCC.exe'
$taskMarker = Join-Path $taskDestination 'compiler-bootstrap.json'
if (Test-Path -LiteralPath $taskDestination) {
    # Reuse only this bootstrap's completed cache. Do not overwrite an unrelated directory.
    if (-not (Test-Path -LiteralPath $taskMarker -PathType Leaf)) { throw 'Compiler destination already exists without a completed bootstrap record.' }
    $taskRecord = Get-Content -LiteralPath $taskMarker -Raw | ConvertFrom-Json
    $taskCoreHashes = Get-InnoCoreHashes $taskDestination
    if ($taskRecord.version -cne $taskVersion -or $taskRecord.source -cne $taskSource -or
        $taskRecord.sourceSha256 -cne $taskSourceHash) {
        throw 'Cached compiler does not match the pinned bootstrap record.'
    }
    foreach ($taskName in $taskCoreHashes.Keys) {
        if ($null -eq $taskRecord.coreHashes -or $taskRecord.coreHashes.$taskName -cne $taskCoreHashes[$taskName]) {
            throw "Cached compiler core does not match its bootstrap record: $taskName"
        }
    }
    Assert-InnoCompiler $taskCompiler
    Write-Host "Reusing portable Inno Setup $taskVersion compiler."
    return $taskCompiler
}

$taskParent = [IO.Path]::GetDirectoryName($taskDestination)
[void][IO.Directory]::CreateDirectory($taskParent)
$taskId = [Guid]::NewGuid().ToString('N')
$taskStage = Assert-InnoBootstrapPath (Join-Path $taskParent ('inno-stage-' + $taskId))
$taskDownload = Assert-InnoBootstrapPath (Join-Path $taskParent ('innosetup-6.7.4-' + $taskId + '.exe'))
try {
    Invoke-WebRequest -Uri $taskSource -OutFile $taskDownload -MaximumRedirection 5
    if ((Get-FileHash -LiteralPath $taskDownload -Algorithm SHA256).Hash.ToLowerInvariant() -cne $taskSourceHash) {
        throw 'Downloaded Inno Setup installer does not match its pinned SHA-256.'
    }
    $taskSignature = Get-AuthenticodeSignature -LiteralPath $taskDownload
    if ($taskSignature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
        $taskSignature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -cne 'Pyrsys B.V.') {
        throw 'Downloaded Inno Setup installer has an unexpected Authenticode signature.'
    }
    # Official isportable.iss disables uninstall registration and uses portable defaults:
    # https://github.com/jrsoftware/issrc/blob/is-6_7_4/isportable.iss
    $taskArguments = @('/PORTABLE=1', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/CURRENTUSER', '/NOICONS', '/NORESTART', '/SP-', ('/DIR="' + $taskStage + '"'))
    $taskProvisioner = Start-Process -FilePath $taskDownload -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
    if (-not $taskProvisioner.WaitForExit(60000)) {
        $taskProvisioner.Kill()
        throw "Owned compiler bootstrap timed out. Inspect temporary output at $taskStage."
    }
    if ($taskProvisioner.ExitCode -ne 0) { throw "Compiler bootstrap failed with exit code $($taskProvisioner.ExitCode). Temporary output: $taskStage" }
    $taskStagedCompiler = Join-Path $taskStage 'ISCC.exe'
    Assert-InnoCompiler $taskStagedCompiler
    $taskRecord = [ordered]@{ version = $taskVersion; source = $taskSource; sourceSha256 = $taskSourceHash;
        coreHashes = Get-InnoCoreHashes $taskStage }
    [IO.File]::WriteAllText((Join-Path $taskStage 'compiler-bootstrap.json'), ($taskRecord | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    # Both absolute paths were checked against the dedicated build root above.
    [IO.Directory]::Move($taskStage, $taskDestination)
    Write-Host "Prepared portable Inno Setup $taskVersion compiler without installation registration."
    return $taskCompiler
}
finally {
    if (Test-Path -LiteralPath $taskDownload) { Remove-Item -LiteralPath $taskDownload -Force }
}
