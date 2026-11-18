$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'package.ps1')
$project = Join-Path $PSScriptRoot ('.build\package-tests-' + [Guid]::NewGuid().ToString('N'))
$null = Assert-CuoPortableChildPath $project (Join-Path $PSScriptRoot '.build')
$null = New-Item -ItemType Directory -Path $project
$checks = New-Object 'System.Collections.Generic.List[string]'
function Assert-PackageTest([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "Failed: $Name" }; $checks.Add($Name)
}
function New-TestPackage([string]$Marker, [string[]]$Files = $script:CuoPortableFiles) {
    $stage = Join-Path $project ('.build\portable-stage-' + [Guid]::NewGuid().ToString('N'))
    $payload = Join-Path $stage 'payload'
    $null = New-Item -ItemType Directory -Path $payload -Force
    foreach ($name in $Files) {
        $text = if ($name -eq 'runtime-paths.json') { '{"Node":"C:\\runtime\\node.exe","Codex":"C:\\runtime\\codex.exe"}' } else { $Marker + ':' + $name }
        $path = Join-Path $payload $name
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, (Join-Path $stage $script:CuoPortableArchive))
    return $stage
}
try {
    $dist = Join-Path $project 'dist'
    $null = New-Item -ItemType Directory -Path $dist
    $legacyStage = New-TestPackage 'legacy' $script:CuoPreClaudePortableFiles
    [IO.Directory]::Move((Join-Path $legacyStage 'payload'), (Join-Path $dist $script:CuoPortableName))
    [IO.File]::Move((Join-Path $legacyStage $script:CuoPortableArchive), (Join-Path $dist $script:CuoPortableArchive))
    $stage = New-TestPackage 'first'
    Publish-CuoPortablePackage $stage $dist $project
    $exe = Join-Path $dist 'codex-usage-overlay\UsageOverlay.exe'
    Assert-PackageTest ([IO.File]::ReadAllText($exe) -eq 'first:UsageOverlay.exe') 'complete portable payload published'
    $claudeModule = Join-Path $dist 'codex-usage-overlay\claude-statusline.mjs'
    Assert-PackageTest ([IO.File]::ReadAllText($claudeModule) -eq 'first:claude-statusline.mjs') 'legacy payload replaced with Claude module included'
    $claudeProbe = Join-Path $dist 'codex-usage-overlay\claude-usage-probe.mjs'
    Assert-PackageTest ([IO.File]::ReadAllText($claudeProbe) -eq 'first:claude-usage-probe.mjs') 'automatic Claude probe published'
    $bridgeManifest = Join-Path $dist 'codex-usage-overlay/claude-code-overlay/.claude-plugin/plugin.json'
    Assert-PackageTest ([IO.File]::Exists($bridgeManifest)) 'Desktop bridge manifest published'
    Assert-PackageTest ([IO.File]::Exists((Join-Path $dist $script:CuoPortableArchive))) 'portable archive published'
    Assert-CuoPortableArchive (Join-Path $dist $script:CuoPortableArchive) $project (Join-Path $dist $script:CuoPortableName)
    Assert-PackageTest $true 'archive hashes match every payload file'
    Remove-CuoPortableStage $stage $project
    Assert-PackageTest (-not (Test-Path -LiteralPath $stage)) 'successful transient stage removed'

    foreach ($legacy in @(
        @{ Name = 'statusline'; Files = $script:CuoStatuslinePortableFiles }
        @{ Name = 'desktop-bridge'; Files = $script:CuoBridgePortableFiles }
    )) {
        $legacyDist = Join-Path $project ('dist-' + $legacy.Name)
        $null = New-Item -ItemType Directory -Path $legacyDist
        $legacyStage = New-TestPackage $legacy.Name $legacy.Files
        [IO.Directory]::Move((Join-Path $legacyStage 'payload'), (Join-Path $legacyDist $script:CuoPortableName))
        [IO.File]::Move((Join-Path $legacyStage $script:CuoPortableArchive), (Join-Path $legacyDist $script:CuoPortableArchive))
        $stage = New-TestPackage 'upgraded'
        Publish-CuoPortablePackage $stage $legacyDist $project
        $upgradedProbe = Join-Path $legacyDist 'codex-usage-overlay\claude-usage-probe.mjs'
        Assert-PackageTest ([IO.File]::ReadAllText($upgradedProbe) -eq 'upgraded:claude-usage-probe.mjs') "upgrades $($legacy.Name) package with automatic Claude probe"
        Remove-CuoPortableStage $legacyStage $project
        Remove-CuoPortableStage $stage $project
    }

    $stage = New-TestPackage 'second'
    Publish-CuoPortablePackage $stage $dist $project
    Assert-PackageTest ([IO.File]::ReadAllText($exe) -eq 'second:UsageOverlay.exe') 'latest output replaces previous output'
    Remove-CuoPortableStage $stage $project
    Assert-PackageTest (-not (Test-Path -LiteralPath $stage)) 'previous payload is discarded with transient stage'

    $stage = New-TestPackage 'legacy-stage' $script:CuoPreClaudePortableFiles
    $rejected = $false
    try { Publish-CuoPortablePackage $stage $dist $project } catch { $rejected = $true }
    Assert-PackageTest $rejected 'previous layout cannot be published as a current package'
    Assert-PackageTest ([IO.File]::ReadAllText($claudeModule) -eq 'second:claude-statusline.mjs') 'current Claude module preserved after previous layout was rejected'
    Remove-CuoPortableStage $stage $project

    $stage = New-TestPackage 'missing-bridge-manifest'
    Remove-Item -LiteralPath (Join-Path $stage 'payload/claude-code-overlay/.claude-plugin/plugin.json')
    $rejected = $false
    try { Publish-CuoPortablePackage $stage $dist $project } catch { $rejected = $true }
    Assert-PackageTest $rejected 'missing Desktop bridge manifest rejects publication'
    Assert-PackageTest ([IO.File]::Exists($bridgeManifest)) 'missing bridge manifest preserves current package'
    Remove-CuoPortableStage $stage $project

    $stage = New-TestPackage 'bridge-as-current' $script:CuoBridgePortableFiles
    $rejected = $false
    try { Publish-CuoPortablePackage $stage $dist $project } catch { $rejected = $true }
    Assert-PackageTest $rejected 'current package requires the automatic Claude probe'
    Assert-PackageTest ([IO.File]::ReadAllText($claudeProbe) -eq 'second:claude-usage-probe.mjs') 'missing automatic Claude probe preserves current package'
    Remove-CuoPortableStage $stage $project

    $stage = New-TestPackage 'missing'
    Remove-Item -LiteralPath (Join-Path $stage 'payload\worker.mjs')
    $rejected = $false
    try { Publish-CuoPortablePackage $stage $dist $project } catch { $rejected = $true }
    Assert-PackageTest $rejected 'missing worker rejects publication'
    Assert-PackageTest ([IO.File]::ReadAllText($exe) -eq 'second:UsageOverlay.exe') 'missing module preserves current package'
    Remove-CuoPortableStage $stage $project

    $stage = New-TestPackage 'locked'
    $lock = [IO.File]::Open($exe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $rejected = $false
    try { Publish-CuoPortablePackage $stage $dist $project } catch { $rejected = $true }
    finally { $lock.Dispose() }
    Assert-PackageTest $rejected 'locked existing executable aborts publication'
    Assert-PackageTest ([IO.File]::ReadAllText($exe) -eq 'second:UsageOverlay.exe') 'locked executable remains unchanged'
    Remove-CuoPortableStage $stage $project

    $stage = New-TestPackage 'unknown'
    [IO.File]::WriteAllText((Join-Path $dist 'codex-usage-overlay\unrelated.txt'), 'preserve')
    $rejected = $false
    try { Publish-CuoPortablePackage $stage $dist $project } catch { $rejected = $true }
    Assert-PackageTest $rejected 'unexpected destination file rejects replacement'
    Assert-PackageTest ([IO.File]::ReadAllText((Join-Path $dist 'codex-usage-overlay\unrelated.txt')) -eq 'preserve') 'unrelated destination file preserved'
    Remove-CuoPortableStage $stage $project
    $rejected = $false
    try { Remove-CuoPortableStage $project $project } catch { $rejected = $true }
    Assert-PackageTest $rejected 'cleanup rejects project root'
    $rejected = $false
    try { Assert-CuoPortableChildPath (Join-Path $project '..\outside') $project } catch { $rejected = $true }
    Assert-PackageTest $rejected 'parent traversal rejected'
    [pscustomobject]@{passed=$checks.Count;failed=0;checks=@($checks)} | ConvertTo-Json -Depth 3
}
finally {
    # Test fixtures live only inside a freshly created, checked workspace directory.
    $resolved = Assert-CuoPortableChildPath $project (Join-Path $PSScriptRoot '.build')
    Assert-CuoPortableNoReparsePoint $resolved $PSScriptRoot
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
