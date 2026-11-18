[CmdletBinding()]
param([switch]$PortableRuntimePaths)

$ErrorActionPreference = 'Stop'
$script:CuoPortableName = 'codex-usage-overlay'
$script:CuoPortableArchive = 'codex-usage-overlay-windows-x64.zip'
$script:CuoPreClaudePortableFiles = @('UsageOverlay.exe', 'runtime-paths.json', 'worker.mjs', 'probe.mjs', 'usage-state.mjs', 'README.md', '사용안내.txt')
$script:CuoStatuslinePortableFiles = $script:CuoPreClaudePortableFiles + 'claude-statusline.mjs'
$script:CuoBridgeFiles = @(
    'claude-code-overlay/.claude-plugin/marketplace.json',
    'claude-code-overlay/.claude-plugin/plugin.json',
    'claude-code-overlay/hooks/hooks.json',
    'claude-code-overlay/hooks/register.js'
)
$script:CuoBridgePortableFiles = $script:CuoStatuslinePortableFiles + $script:CuoBridgeFiles
$script:CuoPortableFiles = $script:CuoBridgePortableFiles + 'claude-usage-probe.mjs'

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
    param([string]$Directory, [string]$Root, [switch]$AllowPreviousLayout)
    $path = Assert-CuoPortableChildPath $Directory $Root
    Assert-CuoPortableNoReparsePoint $path $Root
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw 'Portable payload is missing.' }
    $files = $script:CuoPortableFiles
    if ($AllowPreviousLayout -and -not (Test-Path -LiteralPath (Join-Path $path 'claude-usage-probe.mjs'))) {
        if (Test-Path -LiteralPath (Join-Path $path 'claude-code-overlay')) { $files = $script:CuoBridgePortableFiles }
        elseif (Test-Path -LiteralPath (Join-Path $path 'claude-statusline.mjs')) { $files = $script:CuoStatuslinePortableFiles }
        else { $files = $script:CuoPreClaudePortableFiles }
    }
    $expected = @{}
    foreach ($name in $files) {
        $file = Join-Path $path $name
        $expected[$file] = $false
        $parent = [IO.Path]::GetDirectoryName($file)
        while ($parent -ine $path) {
            $expected[$parent] = $true
            $parent = [IO.Path]::GetDirectoryName($parent)
        }
    }
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($path)
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath ($pending.Pop()) -Force) {
            Assert-CuoPortableNoReparsePoint $item.FullName $Root
            if (-not $expected.ContainsKey($item.FullName) -or $expected[$item.FullName] -ne $item.PSIsContainer) { throw 'Unexpected portable payload entry.' }
            $expected.Remove($item.FullName)
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            elseif ($item.Length -eq 0) { throw 'Empty portable payload file.' }
        }
    }
    if ($expected.Count -gt 0) { throw 'Portable payload is incomplete.' }
    $runtime = Get-Content -LiteralPath (Join-Path $path 'runtime-paths.json') -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($runtime.Node) -or [string]::IsNullOrWhiteSpace($runtime.Codex)) { throw 'Portable runtime configuration is incomplete.' }
}

function Assert-CuoPortableArchive {
    param([string]$Archive, [string]$Root, [string]$Payload, [switch]$AllowPreviousLayout)
    $path = Assert-CuoPortableChildPath $Archive $Root
    Assert-CuoPortableNoReparsePoint $path $Root
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $files = $script:CuoPortableFiles
        if ($AllowPreviousLayout) {
            if ($zip.Entries.Count -eq $script:CuoPreClaudePortableFiles.Count) { $files = $script:CuoPreClaudePortableFiles }
            elseif ($zip.Entries.Count -eq $script:CuoStatuslinePortableFiles.Count) { $files = $script:CuoStatuslinePortableFiles }
            elseif ($zip.Entries.Count -eq $script:CuoBridgePortableFiles.Count) { $files = $script:CuoBridgePortableFiles }
        }
        if ($zip.Entries.Count -ne $files.Count) { throw 'Unexpected portable archive entry count.' }
        $seen = @{}
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name -notin $files -or $entry.Length -eq 0 -or $seen.ContainsKey($name)) { throw 'Unexpected portable archive entry.' }
            $seen[$name] = $true
            if ($Payload) {
                $stream = $entry.Open()
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
                finally { $sha.Dispose(); $stream.Dispose() }
                if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $Payload $name) -Algorithm SHA256).Hash) { throw 'Portable archive does not match its payload.' }
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
        Assert-CuoPortablePayload $target $ProjectRoot -AllowPreviousLayout
        # An executable or module held by another process must abort publication.
        foreach ($name in $script:CuoPortableFiles) {
            $existingFile = Join-Path $target $name
            if ($name -notin $script:CuoPreClaudePortableFiles -and -not [IO.File]::Exists($existingFile)) { continue }
            $handle = [IO.File]::Open($existingFile, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $handle.Dispose()
        }
    }
    if ($hadZip) { Assert-CuoPortableArchive $targetZip $ProjectRoot -AllowPreviousLayout }
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

function New-CuoPortableStage {
    param([string]$Project, [switch]$PortableRuntimePaths)
    $buildRoot = Assert-CuoPortableChildPath (Join-Path $project '.build') $project
    Assert-CuoPortableNoReparsePoint $buildRoot $project
    $null = New-Item -ItemType Directory -Path $buildRoot -Force
    $stage = Assert-CuoPortableChildPath (Join-Path $buildRoot ('portable-stage-' + [Guid]::NewGuid().ToString('N'))) $buildRoot
    $payload = Join-Path $stage 'payload'
    $null = New-Item -ItemType Directory -Path $payload
    & (Join-Path $project 'build.ps1') -OutputDirectory $payload -PortableRuntimePaths:$PortableRuntimePaths | Out-Host
    foreach ($name in $script:CuoPortableFiles) {
        if ($name -in @('UsageOverlay.exe','runtime-paths.json')) { continue }
        $source = Join-Path $project $name
        $destination = Join-Path $payload $name
        Assert-CuoPortableNoReparsePoint $source $project
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        [IO.File]::Copy($source, $destination, $false)
    }
    Assert-CuoPortablePayload $payload $project
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, (Join-Path $stage $script:CuoPortableArchive), [IO.Compression.CompressionLevel]::Optimal, $false)
    return $stage
}

function Invoke-CuoPortablePackage {
    param([switch]$PortableRuntimePaths)
    $project = [IO.Path]::GetFullPath($PSScriptRoot)
    $dist = Assert-CuoPortableChildPath (Join-Path $project 'dist') $project
    Assert-CuoPortableNoReparsePoint $dist $project
    $stage = New-CuoPortableStage -Project $project -PortableRuntimePaths:$PortableRuntimePaths
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
