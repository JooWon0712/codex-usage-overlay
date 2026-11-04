[CmdletBinding()]
param([switch]$Launch, [switch]$NoLaunch)

$ErrorActionPreference = 'Stop'
if ($Launch -and $NoLaunch) { throw 'Use either -Launch or -NoLaunch, not both.' }
$script:CuoApplicationId = 'Personal.CodexUsageOverlay'
$script:CuoPackageFiles = @('UsageOverlay.exe', 'runtime-paths.json', 'worker.mjs', 'probe.mjs', 'usage-state.mjs', 'README.md', '사용안내.txt')

function Assert-CuoChildPath {
    param([Parameter(Mandatory=$true)][string]$Path, [Parameter(Mandatory=$true)][string]$Root)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path must remain inside the expected directory: $fullRoot"
    }
    return $fullPath
}

function Assert-CuoNoReparsePoint {
    param([Parameter(Mandatory=$true)][string]$Path, [Parameter(Mandatory=$true)][string]$Root)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase)) { $null = Assert-CuoChildPath $fullPath $fullRoot }
    $current = $fullPath
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse points are not supported in install paths: $current" }
        }
        if ($current.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase)) { break }
        $current = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrEmpty($current)) { throw 'Could not validate the install path.' }
    }
}

function Assert-CuoLeafNames {
    param([Parameter(Mandatory=$true)][string[]]$Names)
    $seen = @{}
    foreach ($name in $Names) {
        if ([string]::IsNullOrWhiteSpace($name) -or $name -eq '.' -or $name -eq '..' -or $name.IndexOfAny([char[]]@('\','/')) -ge 0 -or [IO.Path]::IsPathRooted($name)) {
            throw 'Package names must be simple file names.'
        }
        if ($seen.ContainsKey($name)) { throw 'Duplicate package file name.' }
        $seen[$name] = $true
    }
}

function Assert-CuoInstallOwnership {
    param([Parameter(Mandatory=$true)][string]$Destination)
    if (-not (Test-Path -LiteralPath $Destination)) { return }
    if (-not (Test-Path -LiteralPath $Destination -PathType Container)) { throw 'The fixed install path is not a directory.' }
    $markerPath = Join-Path $Destination 'install-manifest.json'
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        if (@(Get-ChildItem -LiteralPath $Destination -Force).Count -gt 0) { throw 'The fixed install folder is not owned by this application; no files were changed.' }
        return
    }
    try { $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json }
    catch { throw 'The existing install manifest is invalid; no files were changed.' }
    if ($marker.applicationId -cne $script:CuoApplicationId -or $marker.schemaVersion -ne 1) { throw 'The fixed install folder belongs to another application; no files were changed.' }
}

function Install-CuoAtomicFile {
    param([Parameter(Mandatory=$true)][string]$Source, [Parameter(Mandatory=$true)][string]$Destination, [Parameter(Mandatory=$true)][string]$DestinationRoot)
    $destinationPath = Assert-CuoChildPath $Destination $DestinationRoot
    Assert-CuoNoReparsePoint $destinationPath $DestinationRoot
    $temporary = Assert-CuoChildPath (Join-Path $DestinationRoot ('.update-' + [Guid]::NewGuid().ToString('N') + '.tmp')) $DestinationRoot
    $atomicBackup = Assert-CuoChildPath (Join-Path $DestinationRoot ('.replace-backup-' + [Guid]::NewGuid().ToString('N') + '.tmp')) $DestinationRoot
    try {
        [IO.File]::Copy($Source, $temporary, $false)
        if ((Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash) { throw 'Staged copy verification failed.' }
        if ([IO.File]::Exists($destinationPath)) { [IO.File]::Replace($temporary, $destinationPath, $atomicBackup) }
        else { [IO.File]::Move($temporary, $destinationPath) }
    }
    finally {
        if ([IO.File]::Exists($temporary)) { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
        if ([IO.File]::Exists($atomicBackup)) { Remove-Item -LiteralPath $atomicBackup -Force -ErrorAction SilentlyContinue }
    }
}

function Invoke-CuoFileTransaction {
    param(
        [Parameter(Mandatory=$true)][string]$Stage,
        [Parameter(Mandatory=$true)][string]$Destination,
        [Parameter(Mandatory=$true)][string]$Backup,
        [Parameter(Mandatory=$true)][string[]]$Files
    )
    Assert-CuoLeafNames $Files
    $null = New-Item -ItemType Directory -Path $Destination -Force
    $null = New-Item -ItemType Directory -Path $Backup -Force
    Assert-CuoNoReparsePoint $Destination $Destination
    Assert-CuoNoReparsePoint $Backup $Backup
    $original = @{}
    foreach ($name in $Files) {
        $sourcePath = Assert-CuoChildPath (Join-Path $Stage $name) $Stage
        $targetPath = Assert-CuoChildPath (Join-Path $Destination $name) $Destination
        Assert-CuoNoReparsePoint $sourcePath $Stage
        Assert-CuoNoReparsePoint $targetPath $Destination
        if (-not [IO.File]::Exists($sourcePath)) { throw "Package file missing: $name" }
        $original[$name] = [IO.File]::Exists($targetPath)
        if ($original[$name]) { [IO.File]::Copy($targetPath, (Assert-CuoChildPath (Join-Path $Backup $name) $Backup), $false) }
    }
    $written = New-Object 'System.Collections.Generic.List[string]'
    try {
        foreach ($name in $Files) {
            Install-CuoAtomicFile -Source (Join-Path $Stage $name) -Destination (Join-Path $Destination $name) -DestinationRoot $Destination
            $written.Add($name)
        }
        foreach ($name in $Files) {
            if ((Get-FileHash -LiteralPath (Join-Path $Stage $name) -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $Destination $name) -Algorithm SHA256).Hash) { throw 'Installed file verification failed.' }
        }
    }
    catch {
        $failure = $_
        $rollbackFailures = New-Object 'System.Collections.Generic.List[string]'
        for ($index = $written.Count - 1; $index -ge 0; $index--) {
            $name = $written[$index]
            try {
                $targetPath = Assert-CuoChildPath (Join-Path $Destination $name) $Destination
                if ($original[$name]) { Install-CuoAtomicFile -Source (Join-Path $Backup $name) -Destination $targetPath -DestinationRoot $Destination }
                elseif ([IO.File]::Exists($targetPath)) { Remove-Item -LiteralPath $targetPath -Force }
            }
            catch { $rollbackFailures.Add($name) }
        }
        if ($rollbackFailures.Count -gt 0) {
            $rollbackError = [InvalidOperationException]::new("Update failed and rollback needs attention for: $($rollbackFailures -join ', '). Backup: $Backup", $failure.Exception)
            $rollbackError.Data['RollbackSucceeded'] = $false
            throw $rollbackError
        }
        $rollbackError = [InvalidOperationException]::new('Update failed; replaced files were rolled back. ' + $failure.Exception.Message, $failure.Exception)
        $rollbackError.Data['RollbackSucceeded'] = $true
        throw $rollbackError
    }
}

function Get-CuoProcessAtPath {
    param([Parameter(Mandatory=$true)][string]$Executable)
    $expected = [IO.Path]::GetFullPath($Executable)
    foreach ($process in @(Get-Process -Name 'UsageOverlay' -ErrorAction SilentlyContinue)) {
        try { if ($process.Path -and ([IO.Path]::GetFullPath($process.Path)).Equals($expected, [StringComparison]::OrdinalIgnoreCase)) { $process } }
        catch { }
    }
}

function Invoke-CuoShutdown {
    param([Parameter(Mandatory=$true)][string]$Executable)
    $shutdown = Start-Process -FilePath $Executable -ArgumentList @('--shutdown','--wait','15000') -PassThru -Wait -WindowStyle Hidden
    if ($shutdown.ExitCode -ne 0) { throw "The existing program did not confirm a clean shutdown (code $($shutdown.ExitCode)); no installed files were replaced." }
}

function Invoke-CuoStageValidation {
    param([Parameter(Mandatory=$true)][string]$Stage)
    foreach ($name in $script:CuoPackageFiles) {
        $path = Assert-CuoChildPath (Join-Path $Stage $name) $Stage
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw "Package file missing or empty: $name" }
    }
    $runtime = Get-Content -LiteralPath (Join-Path $Stage 'runtime-paths.json') -Raw | ConvertFrom-Json
    foreach ($runtimePath in @($runtime.Node, $runtime.Codex)) {
        if ([string]::IsNullOrWhiteSpace($runtimePath) -or -not (Test-Path -LiteralPath $runtimePath -PathType Leaf)) { throw 'The Node/Codex runtime path is unavailable. Build and install were stopped before shutting down the existing program.' }
    }
    $testResult = Join-Path $Stage 'geometry-test.json'
    $selfTest = Start-Process -FilePath (Join-Path $Stage 'UsageOverlay.exe') -ArgumentList @('--self-test', ('"' + $testResult + '"')) -PassThru -Wait -WindowStyle Hidden
    if ($selfTest.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $testResult -PathType Leaf)) { throw 'The staged executable self-test failed; the existing program was not stopped.' }
    $result = Get-Content -LiteralPath $testResult -Raw | ConvertFrom-Json
    if ($result.passed -ne $true) { throw 'The staged executable self-test reported a failure; the existing program was not stopped.' }
}

function Initialize-CuoSettings {
    param([Parameter(Mandatory=$true)][string]$SourceDirectory, [Parameter(Mandatory=$true)][string]$DataDirectory)
    $targetSettings = Assert-CuoChildPath (Join-Path $DataDirectory 'settings.json') $DataDirectory
    Assert-CuoNoReparsePoint $targetSettings $DataDirectory
    if ([IO.File]::Exists($targetSettings)) { return }
    $oldSettings = Join-Path $SourceDirectory 'settings.json'
    if (-not [IO.File]::Exists($oldSettings)) { return }
    $text = [IO.File]::ReadAllText($oldSettings)
    $null = $text | ConvertFrom-Json
    $null = New-Item -ItemType Directory -Path $DataDirectory -Force
    Assert-CuoNoReparsePoint $targetSettings $DataDirectory
    $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($text)
    $stream = New-Object IO.FileStream($targetSettings, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}

function Remove-CuoInstallWorkDirectory {
    param([Parameter(Mandatory=$true)][string]$Path, [Parameter(Mandatory=$true)][string]$BuildRoot)
    $fullPath = Assert-CuoChildPath $Path $BuildRoot
    if ([IO.Path]::GetFileName($fullPath) -notmatch '^install-(stage|backup)-[0-9a-f]{32}$') { throw 'Unexpected install work directory.' }
    Assert-CuoNoReparsePoint $fullPath $BuildRoot
    if (-not (Test-Path -LiteralPath $fullPath)) { return }
    $allowed = $script:CuoPackageFiles + @('install-manifest.json', 'geometry-test.json')
    $items = @(Get-ChildItem -LiteralPath $fullPath -Force)
    foreach ($item in $items) {
        if ($item.PSIsContainer -or $item.Name -notin $allowed) { throw 'Unexpected contents in install work directory; preserved for inspection.' }
        Assert-CuoNoReparsePoint $item.FullName $BuildRoot
    }
    foreach ($item in $items) { Remove-Item -LiteralPath $item.FullName -Force }
    # Delete only an empty directory. Never recurse into an unexpected child.
    [IO.Directory]::Delete($fullPath, $false)
}

function Invoke-CuoInstall {
    param([switch]$LaunchAfterInstall)
    $local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($local)) { throw 'The per-user application-data directory is unavailable.' }
    $destination = Assert-CuoChildPath (Join-Path $local 'Programs\CodexUsageOverlay') $local
    $dataDirectory = Assert-CuoChildPath (Join-Path $local 'CodexUsageOverlay') $local
    Assert-CuoNoReparsePoint $destination $local
    Assert-CuoNoReparsePoint $dataDirectory $local
    Assert-CuoInstallOwnership $destination

    $installerMutex = New-Object Threading.Mutex($false, 'Local\PersonalCodexUsageOverlayInstaller')
    $installerAcquired = $false
    $updateMutex = $null
    $updateAcquired = $false
    $failure = $null
    $installedExe = Join-Path $destination 'UsageOverlay.exe'
    try {
        try { $installerAcquired = $installerMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $installerAcquired = $true }
        if (-not $installerAcquired) { throw 'Another installation/update is already running.' }
        if (-not ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'UsageOverlay.exe'))).Equals([IO.Path]::GetFullPath($installedExe), [StringComparison]::OrdinalIgnoreCase)) {
            if (@(Get-CuoProcessAtPath (Join-Path $PSScriptRoot 'UsageOverlay.exe')).Count -gt 0) { throw 'The older workspace prototype is still running. Close or exit that copy, then run install.ps1 again. It was not force-terminated.' }
        }
        $buildRoot = Join-Path $PSScriptRoot '.build'
        $null = New-Item -ItemType Directory -Path $buildRoot -Force
        Assert-CuoNoReparsePoint $buildRoot $PSScriptRoot
        $stage = Assert-CuoChildPath (Join-Path $buildRoot ('install-stage-' + [Guid]::NewGuid().ToString('N'))) $buildRoot
        $backup = Assert-CuoChildPath (Join-Path $buildRoot ('install-backup-' + [Guid]::NewGuid().ToString('N'))) $buildRoot
        $null = New-Item -ItemType Directory -Path $stage
        & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $stage
        foreach ($name in $script:CuoPackageFiles) {
            if ($name -eq 'UsageOverlay.exe' -or $name -eq 'runtime-paths.json') { continue }
            [IO.File]::Copy((Join-Path $PSScriptRoot $name), (Join-Path $stage $name), $false)
        }
        Invoke-CuoStageValidation $stage
        $hashes = [ordered]@{}
        foreach ($name in $script:CuoPackageFiles) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $stage $name) -Algorithm SHA256).Hash }
        $packageVersion = ([Version][Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stage 'UsageOverlay.exe')).FileVersion).ToString(3)
        [ordered]@{ applicationId=$script:CuoApplicationId; schemaVersion=1; version=$packageVersion; updatedAtUtc=[DateTime]::UtcNow.ToString('o'); files=$script:CuoPackageFiles; sha256=$hashes } |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage 'install-manifest.json') -Encoding UTF8
        Initialize-CuoSettings -SourceDirectory $PSScriptRoot -DataDirectory $dataDirectory
        $updateMutex = New-Object Threading.Mutex($false, 'Local\PersonalCodexUsageOverlayUpdateLock')
        try { $updateAcquired = $updateMutex.WaitOne(5000) } catch [Threading.AbandonedMutexException] { $updateAcquired = $true }
        if (-not $updateAcquired) { throw 'The application is starting or another update is in progress; no files were replaced.' }
        Assert-CuoNoReparsePoint $destination $local
        Assert-CuoInstallOwnership $destination
        $runningInstances = @(Get-CuoProcessAtPath $installedExe)
        $shutdownExe = if (Test-Path -LiteralPath $installedExe -PathType Leaf) { $installedExe } else { Join-Path $stage 'UsageOverlay.exe' }
        Invoke-CuoShutdown $shutdownExe
        foreach ($instance in $runningInstances) {
            try { if (-not $instance.WaitForExit(5000)) { throw 'The old process is still exiting; no installed files were replaced.' } }
            finally { $instance.Dispose() }
        }
        Invoke-CuoFileTransaction -Stage $stage -Destination $destination -Backup $backup -Files ($script:CuoPackageFiles + @('install-manifest.json'))
        # File hashes are verified before temporary rollback copies are discarded.
        # Previous application versions are recovered from Git, not retained EXEs.
        foreach ($workDirectory in @($backup, $stage)) {
            try { Remove-CuoInstallWorkDirectory -Path $workDirectory -BuildRoot $buildRoot }
            catch { Write-Warning "Installation succeeded, but temporary files could not be removed: $workDirectory. $($_.Exception.Message)" }
        }
    }
    catch { $failure = $_ }
    finally {
        if ($updateAcquired) { $updateMutex.ReleaseMutex() }
        if ($updateMutex) { $updateMutex.Dispose() }
        if ($installerAcquired) { $installerMutex.ReleaseMutex() }
        $installerMutex.Dispose()
    }
    if ($failure) {
        # A rolled-back installation may still be a legacy tray version. Do not launch it.
        throw $failure
    }
    if ($LaunchAfterInstall) { $null = Start-Process -FilePath $installedExe -WindowStyle Normal }
    Write-Output "Installed/updated: $installedExe"
    Write-Output "Settings preserved at: $dataDirectory"
    if (-not $LaunchAfterInstall) { Write-Output 'Program not launched. Use -Launch to open it after installation.' }
    Write-Output 'No startup registration or scheduled task was created.'
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-CuoInstall -LaunchAfterInstall:$Launch }
