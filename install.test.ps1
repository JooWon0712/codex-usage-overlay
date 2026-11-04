$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install.ps1')

$testBase = Join-Path $PSScriptRoot '.build'
$null = New-Item -ItemType Directory -Path $testBase -Force
Assert-CuoNoReparsePoint $testBase $PSScriptRoot
$testRoot = Assert-CuoChildPath (Join-Path $testBase ('install-tests-' + [Guid]::NewGuid().ToString('N'))) $testBase
$null = New-Item -ItemType Directory -Path $testRoot
$checks = New-Object 'System.Collections.Generic.List[string]'

function Assert-InstallTest {
    param([bool]$Condition, [string]$Name)
    if (-not $Condition) { throw "Failed: $Name" }
    $checks.Add($Name)
}
function Write-TestText { param([string]$Path,[string]$Text) [IO.File]::WriteAllText($Path,$Text,(New-Object Text.UTF8Encoding($false))) }

try {
    $stage = Join-Path $testRoot 'stage'
    $target = Join-Path $testRoot 'target'
    $backup = Join-Path $testRoot 'backup'
    $null = New-Item -ItemType Directory -Path $stage,$target
    Write-TestText (Join-Path $stage 'UsageOverlay.exe') 'new-executable'
    Write-TestText (Join-Path $stage 'worker.mjs') 'new-worker'
    Write-TestText (Join-Path $target 'UsageOverlay.exe') 'old-executable'
    Write-TestText (Join-Path $target 'worker.mjs') 'old-worker'
    Write-TestText (Join-Path $target 'settings.json') '{"Position":"title"}'
    Write-TestText (Join-Path $target 'unrelated.txt') 'keep-me'
    Invoke-CuoFileTransaction $stage $target $backup @('UsageOverlay.exe','worker.mjs')
    Assert-InstallTest (([IO.File]::ReadAllText((Join-Path $target 'UsageOverlay.exe'))) -eq 'new-executable') 'allowlisted executable replaced'
    Assert-InstallTest (([IO.File]::ReadAllText((Join-Path $target 'worker.mjs'))) -eq 'new-worker') 'allowlisted worker replaced'
    Assert-InstallTest (([IO.File]::ReadAllText((Join-Path $target 'settings.json'))) -eq '{"Position":"title"}') 'existing settings preserved'
    Assert-InstallTest (([IO.File]::ReadAllText((Join-Path $target 'unrelated.txt'))) -eq 'keep-me') 'unrelated files preserved'

    $rollbackStage = Join-Path $testRoot 'rollback-stage'
    $rollbackTarget = Join-Path $testRoot 'rollback-target'
    $rollbackBackup = Join-Path $testRoot 'rollback-backup'
    $null = New-Item -ItemType Directory -Path $rollbackStage,$rollbackTarget
    Write-TestText (Join-Path $rollbackStage 'UsageOverlay.exe') 'replacement-executable'
    Write-TestText (Join-Path $rollbackStage 'worker.mjs') 'replacement-worker'
    Write-TestText (Join-Path $rollbackTarget 'UsageOverlay.exe') 'original-executable'
    Write-TestText (Join-Path $rollbackTarget 'worker.mjs') 'original-worker'
    $lock = [IO.File]::Open((Join-Path $rollbackTarget 'worker.mjs'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $failed = $false
    $rollbackConfirmed = $false
    try { Invoke-CuoFileTransaction $rollbackStage $rollbackTarget $rollbackBackup @('UsageOverlay.exe','worker.mjs') }
    catch { $failed = $true; $rollbackConfirmed = $_.Exception.Data['RollbackSucceeded'] -eq $true }
    finally { $lock.Dispose() }
    Assert-InstallTest $failed 'locked replacement aborts update'
    Assert-InstallTest $rollbackConfirmed 'failed update confirms successful file rollback'
    Assert-InstallTest (([IO.File]::ReadAllText((Join-Path $rollbackTarget 'UsageOverlay.exe'))) -eq 'original-executable') 'earlier replacement rolled back after later failure'
    Assert-InstallTest (([IO.File]::ReadAllText((Join-Path $rollbackTarget 'worker.mjs'))) -eq 'original-worker') 'locked original remains intact'

    $newStage = Join-Path $testRoot 'new-file-stage'
    $newTarget = Join-Path $testRoot 'new-file-target'
    $newBackup = Join-Path $testRoot 'new-file-backup'
    $null = New-Item -ItemType Directory -Path $newStage,$newTarget
    Write-TestText (Join-Path $newStage 'added.mjs') 'new-file'
    Write-TestText (Join-Path $newStage 'worker.mjs') 'replacement-worker'
    Write-TestText (Join-Path $newTarget 'worker.mjs') 'original-worker'
    $lock = [IO.File]::Open((Join-Path $newTarget 'worker.mjs'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $failed = $false
    try { Invoke-CuoFileTransaction $newStage $newTarget $newBackup @('added.mjs','worker.mjs') }
    catch { $failed = $true }
    finally { $lock.Dispose() }
    Assert-InstallTest $failed 'new-file transaction aborts when later file is locked'
    Assert-InstallTest (-not [IO.File]::Exists((Join-Path $newTarget 'added.mjs'))) 'rollback removes newly introduced file'

    $unknown = Join-Path $testRoot 'unknown-owner'
    $null = New-Item -ItemType Directory -Path $unknown
    Write-TestText (Join-Path $unknown 'unrelated.txt') 'keep-me'
    $rejected = $false
    try { Assert-CuoInstallOwnership $unknown } catch { $rejected = $true }
    Assert-InstallTest $rejected 'nonempty unowned install folder rejected'
    Write-TestText (Join-Path $unknown 'install-manifest.json') '{"applicationId":"another.app","schemaVersion":1}'
    $rejected = $false
    try { Assert-CuoInstallOwnership $unknown } catch { $rejected = $true }
    Assert-InstallTest $rejected 'different application manifest rejected'
    Write-TestText (Join-Path $unknown 'install-manifest.json') '{"applicationId":"Personal.CodexUsageOverlay","schemaVersion":1}'
    Assert-CuoInstallOwnership $unknown
    Assert-InstallTest $true 'matching application manifest accepted'

    $source = Join-Path $testRoot 'settings-source'
    $data = Join-Path $testRoot 'settings-data'
    $null = New-Item -ItemType Directory -Path $source
    Write-TestText (Join-Path $source 'settings.json') '{"Position":"title"}'
    Initialize-CuoSettings $source $data
    Assert-InstallTest (([IO.File]::ReadAllText((Join-Path $data 'settings.json'))) -eq '{"Position":"title"}') 'first-install settings migrated'
    Write-TestText (Join-Path $source 'settings.json') '{"Position":"sidebar"}'
    Initialize-CuoSettings $source $data
    Assert-InstallTest (([IO.File]::ReadAllText((Join-Path $data 'settings.json'))) -eq '{"Position":"title"}') 'existing stable settings never overwritten'

    $rejected = $false
    try { Assert-CuoChildPath (Join-Path $testRoot '..\outside.txt') $testRoot } catch { $rejected = $true }
    Assert-InstallTest $rejected 'parent traversal rejected'
    $rejected = $false
    try { Assert-CuoLeafNames @('..\outside.txt') } catch { $rejected = $true }
    Assert-InstallTest $rejected 'package path traversal rejected'
    $rejected = $false
    try { Assert-CuoLeafNames @('worker.mjs','WORKER.MJS') } catch { $rejected = $true }
    Assert-InstallTest $rejected 'case-insensitive duplicate package name rejected'

    $work = Join-Path $testRoot ('install-backup-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $work
    Write-TestText (Join-Path $work 'UsageOverlay.exe') 'temporary-rollback-copy'
    Remove-CuoInstallWorkDirectory -Path $work -BuildRoot $testRoot
    Assert-InstallTest (-not (Test-Path -LiteralPath $work)) 'completed install work directory removed'
    Assert-InstallTest ([IO.File]::Exists((Join-Path $target 'UsageOverlay.exe'))) 'work cleanup preserves installed target'

    $work = Join-Path $testRoot ('install-stage-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $work
    Write-TestText (Join-Path $work 'UsageOverlay.exe') 'temporary-build'
    Write-TestText (Join-Path $work 'unrelated.txt') 'keep-me'
    $rejected = $false
    try { Remove-CuoInstallWorkDirectory -Path $work -BuildRoot $testRoot } catch { $rejected = $true }
    Assert-InstallTest $rejected 'unexpected work contents block cleanup'
    Assert-InstallTest ([IO.File]::Exists((Join-Path $work 'UsageOverlay.exe')) -and [IO.File]::Exists((Join-Path $work 'unrelated.txt'))) 'work contents checked before deleting any file'
    $rejected = $false
    try { Remove-CuoInstallWorkDirectory -Path $target -BuildRoot $testRoot } catch { $rejected = $true }
    Assert-InstallTest $rejected 'non-work directory cleanup rejected'
    $rejected = $false
    try { Remove-CuoInstallWorkDirectory -Path $work -BuildRoot (Join-Path $testRoot 'other-root') } catch { $rejected = $true }
    Assert-InstallTest $rejected 'work cleanup outside expected root rejected'

    # Dot-source mode cannot install even if the conflicting-option guard regresses.
    $rejected = $false
    try { . (Join-Path $PSScriptRoot 'install.ps1') -Launch -NoLaunch }
    catch { $rejected = $_.Exception.Message -eq 'Use either -Launch or -NoLaunch, not both.' }
    Assert-InstallTest $rejected 'conflicting launch options rejected before installation'

    [pscustomobject]@{ passed=$checks.Count; failed=0; checks=@($checks) } | ConvertTo-Json -Depth 3
}
finally {
    $resolvedTestRoot = Assert-CuoChildPath $testRoot $testBase
    Assert-CuoNoReparsePoint $resolvedTestRoot $testBase
    if (Test-Path -LiteralPath $resolvedTestRoot) { Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force }
}
