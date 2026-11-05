[CmdletBinding()]
param([switch]$PortableRuntimePaths)

$ErrorActionPreference = 'Stop'
$script:CuoPortableName = 'codex-usage-overlay'
$script:CuoPortableArchive = 'codex-usage-overlay-windows-x64.zip'
$script:CuoPortableFiles = @('UsageOverlay.exe', 'runtime-paths.json', 'worker.mjs', 'probe.mjs', 'usage-state.mjs', 'README.md', '사용안내.txt')

function Assert-CuoPortableChildPath {
    param([string]$Path, [string]$Root)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package path must remain inside the expected directory.' }
    return $fullPath
}

function Assert-CuoPortableNoReparsePoint {
    param([string]$Path, [string]$Root)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $current = [IO.Path]::GetFullPath($Path)
    if (-not $current.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase)) { $null = Assert-CuoPortableChildPath $current $fullRoot }
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse points are not supported in package paths.' }
        }
        if ($current.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase)) { break }
        $current = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrEmpty($current)) { throw 'Could not validate package path.' }
    }
}

function Assert-CuoPortablePayload {
    param([string]$Directory, [string]$Root)
    $path = Assert-CuoPortableChildPath $Directory $Root
    Assert-CuoPortableNoReparsePoint $path $Root
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw 'Portable payload is missing.' }
    $items = @(Get-ChildItem -LiteralPath $path -Force)
    if ($items.Count -ne $script:CuoPortableFiles.Count) { throw 'Portable payload must contain the complete application file set only.' }
    foreach ($item in $items) {
        if ($item.PSIsContainer -or $item.Name -notin $script:CuoPortableFiles -or $item.Length -eq 0) { throw 'Unexpected or empty portable payload file.' }
        Assert-CuoPortableNoReparsePoint $item.FullName $Root
    }
    foreach ($name in $script:CuoPortableFiles) { if (-not [IO.File]::Exists((Join-Path $path $name))) { throw "Portable package file missing: $name" } }
    $runtime = Get-Content -LiteralPath (Join-Path $path 'runtime-paths.json') -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($runtime.Node) -or [string]::IsNullOrWhiteSpace($runtime.Codex)) { throw 'Portable runtime configuration is incomplete.' }
}

function Assert-CuoPortableArchive {
    param([string]$Archive, [string]$Root, [string]$Payload)
    $path = Assert-CuoPortableChildPath $Archive $Root
    Assert-CuoPortableNoReparsePoint $path $Root
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        if ($zip.Entries.Count -ne $script:CuoPortableFiles.Count) { throw 'Unexpected portable archive entry count.' }
        $seen = @{}
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -notin $script:CuoPortableFiles -or $entry.Length -eq 0 -or $seen.ContainsKey($entry.FullName)) { throw 'Unexpected portable archive entry.' }
            $seen[$entry.FullName] = $true
            if ($Payload) {
                $stream = $entry.Open()
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
                finally { $sha.Dispose(); $stream.Dispose() }
                if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $Payload $entry.FullName) -Algorithm SHA256).Hash) { throw 'Portable archive does not match its payload.' }
            }
        }
    } finally { $zip.Dispose() }
}

function Remove-CuoPortableStage {
    param([string]$Stage, [string]$ProjectRoot)
    $buildRoot = Assert-CuoPortableChildPath (Join-Path $ProjectRoot '.build') $ProjectRoot
    $fullStage = Assert-CuoPortableChildPath $Stage $buildRoot
    if ([IO.Path]::GetDirectoryName($fullStage) -ine $buildRoot -or [IO.Path]::GetFileName($fullStage) -notmatch '^portable-stage-[0-9a-f]{32}$') { throw 'Unexpected portable staging directory.' }
    Assert-CuoPortableNoReparsePoint $fullStage $ProjectRoot
    if (-not (Test-Path -LiteralPath $fullStage)) { return }
    # Inspect children before visiting them; never follow a junction during cleanup.
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $directories = New-Object 'System.Collections.Generic.List[string]'
    $files = New-Object 'System.Collections.Generic.List[string]'
    $pending.Push($fullStage)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop(); $directories.Add($directory)
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force)) {
            $null = Assert-CuoPortableChildPath $item.FullName $fullStage
            Assert-CuoPortableNoReparsePoint $item.FullName $ProjectRoot
            if ($item.PSIsContainer) { $pending.Push($item.FullName) } else { $files.Add($item.FullName) }
        }
    }
    foreach ($file in $files) { Remove-Item -LiteralPath $file -Force }
    for ($index = $directories.Count - 1; $index -ge 0; $index--) { [IO.Directory]::Delete($directories[$index], $false) }
}

function Publish-CuoPortablePackage {
    param([string]$Stage, [string]$DistributionRoot, [string]$ProjectRoot)
    $stagePath = Assert-CuoPortableChildPath $Stage (Join-Path $ProjectRoot '.build')
    if ([IO.Path]::GetFileName($stagePath) -notmatch '^portable-stage-[0-9a-f]{32}$') { throw 'Unexpected portable staging directory.' }
    $dist = Assert-CuoPortableChildPath $DistributionRoot $ProjectRoot
    Assert-CuoPortableNoReparsePoint $stagePath $ProjectRoot
    Assert-CuoPortableNoReparsePoint $dist $ProjectRoot
    $payload = Join-Path $stagePath 'payload'
    $newZip = Join-Path $stagePath $script:CuoPortableArchive
    Assert-CuoPortablePayload $payload $ProjectRoot
    Assert-CuoPortableArchive $newZip $ProjectRoot $payload
    $target = Assert-CuoPortableChildPath (Join-Path $dist $script:CuoPortableName) $dist
    $targetZip = Assert-CuoPortableChildPath (Join-Path $dist $script:CuoPortableArchive) $dist
    $oldTarget = Join-Path $stagePath 'previous-package'
    $oldZip = Join-Path $stagePath 'previous.zip'
    if ((Test-Path -LiteralPath $oldTarget) -or (Test-Path -LiteralPath $oldZip)) { throw 'Use a fresh package stage.' }
    $hadTarget = Test-Path -LiteralPath $target
    $hadZip = Test-Path -LiteralPath $targetZip
    if ($hadTarget) {
        Assert-CuoPortablePayload $target $ProjectRoot
        # An executable or module held by another process must abort publication.
        foreach ($name in $script:CuoPortableFiles) {
            $handle = [IO.File]::Open((Join-Path $target $name), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $handle.Dispose()
        }
    }
    if ($hadZip) { Assert-CuoPortableArchive $targetZip $ProjectRoot }
    $null = New-Item -ItemType Directory -Path $dist -Force
    $movedTarget = $false; $movedZip = $false; $publishedTarget = $false; $publishedZip = $false
    try {
        if ($hadTarget) { [IO.Directory]::Move($target, $oldTarget); $movedTarget = $true }
        if ($hadZip) { [IO.File]::Move($targetZip, $oldZip); $movedZip = $true }
        [IO.Directory]::Move($payload, $target); $publishedTarget = $true
        [IO.File]::Move($newZip, $targetZip); $publishedZip = $true
        Assert-CuoPortablePayload $target $ProjectRoot
        Assert-CuoPortableArchive $targetZip $ProjectRoot $target
    }
    catch {
        $failure = $_
        try {
            if ($publishedZip) { [IO.File]::Move($targetZip, $newZip) }
            if ($publishedTarget) { [IO.Directory]::Move($target, $payload) }
            if ($movedZip) { [IO.File]::Move($oldZip, $targetZip) }
            if ($movedTarget) { [IO.Directory]::Move($oldTarget, $target) }
        }
        catch { throw "Package publication failed and needs recovery at $stagePath. $($_.Exception.Message)" }
        throw $failure
    }
}

function Invoke-CuoPortablePackage {
    param([switch]$PortableRuntimePaths)
    $project = [IO.Path]::GetFullPath($PSScriptRoot)
    $buildRoot = Assert-CuoPortableChildPath (Join-Path $project '.build') $project
    $dist = Assert-CuoPortableChildPath (Join-Path $project 'dist') $project
    Assert-CuoPortableNoReparsePoint $buildRoot $project
    Assert-CuoPortableNoReparsePoint $dist $project
    $null = New-Item -ItemType Directory -Path $buildRoot -Force
    $stage = Assert-CuoPortableChildPath (Join-Path $buildRoot ('portable-stage-' + [Guid]::NewGuid().ToString('N'))) $buildRoot
    $payload = Join-Path $stage 'payload'
    $null = New-Item -ItemType Directory -Path $payload
    & (Join-Path $project 'build.ps1') -OutputDirectory $payload -PortableRuntimePaths:$PortableRuntimePaths
    foreach ($name in $script:CuoPortableFiles) {
        if ($name -notin @('UsageOverlay.exe','runtime-paths.json')) { [IO.File]::Copy((Join-Path $project $name), (Join-Path $payload $name), $false) }
    }
    Assert-CuoPortablePayload $payload $project
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, (Join-Path $stage $script:CuoPortableArchive), [IO.Compression.CompressionLevel]::Optimal, $false)
    $update = New-Object Threading.Mutex($false, 'Local\PersonalCodexUsageOverlayUpdateLock')
    $instance = New-Object Threading.Mutex($false, 'Local\PersonalCodexUsageOverlayPrototype')
    $updateOwned = $false; $instanceOwned = $false
    try {
        try { $updateOwned = $update.WaitOne(0) } catch [Threading.AbandonedMutexException] { $updateOwned = $true }
        if (-not $updateOwned) { throw 'Application startup/update is in progress; portable output was not replaced.' }
        try { $instanceOwned = $instance.WaitOne(0) } catch [Threading.AbandonedMutexException] { $instanceOwned = $true }
        if (-not $instanceOwned) { throw 'Close Codex Usage Overlay before replacing the portable package. It was not stopped automatically.' }
        Publish-CuoPortablePackage -Stage $stage -DistributionRoot $dist -ProjectRoot $project
        try { Remove-CuoPortableStage -Stage $stage -ProjectRoot $project }
        catch { Write-Warning "Portable package was published, but temporary files could not be removed: $stage. $($_.Exception.Message)" }
    }
    finally {
        if ($instanceOwned) { $instance.ReleaseMutex() }; $instance.Dispose()
        if ($updateOwned) { $update.ReleaseMutex() }; $update.Dispose()
    }
    Write-Output ('Portable executable: ' + (Join-Path $dist ($script:CuoPortableName + '\UsageOverlay.exe')))
    Write-Output ('Portable archive: ' + (Join-Path $dist $script:CuoPortableArchive))
    Write-Output 'No installation, application launch, automatic startup or scheduled task was performed.'
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-CuoPortablePackage -PortableRuntimePaths:$PortableRuntimePaths }
