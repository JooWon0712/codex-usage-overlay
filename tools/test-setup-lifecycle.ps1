[CmdletBinding()]
param(
    [Parameter(Mandatory=$true,ParameterSetName='Integration')][string]$SetupPath,
    [Parameter(Mandatory=$true,ParameterSetName='Integration')][string]$InstalledPath,
    [Parameter(Mandatory=$true,ParameterSetName='Integration')][string]$PortablePath,
    [Parameter(Mandatory=$true,ParameterSetName='Integration')][string]$TestRoot,
    [Parameter(Mandatory=$true,ParameterSetName='TimestampTest')][switch]$TimestampSelfTest,
    [Parameter(Mandatory=$true,ParameterSetName='ArgumentTest')][switch]$ArgumentSelfTest
)
$ErrorActionPreference = 'Stop'

function Convert-LifecycleUtcTicks([object]$Value) {
    # ConvertFrom-Json returns DateTime in recent PowerShell and strings in 5.1.
    # Stringifying a date can discard fractions and reinterpret its offset.
    if ($Value -is [DateTimeOffset]) { return $Value.UtcDateTime.Ticks }
    if ($Value -is [DateTime]) { return $Value.ToUniversalTime().Ticks }
    if ($Value -isnot [string]) { throw 'Unsupported fixture timestamp type.' }
    return [DateTimeOffset]::Parse($Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None).UtcDateTime.Ticks
}

function New-LifecycleLoggedArguments([string[]]$Prefix, [string]$LogPath) {
    if ([string]::IsNullOrWhiteSpace($LogPath) -or $LogPath.Contains('"')) { throw 'Invalid fixture log path.' }
    # Keep concatenation separate from comma-separated array literals. Otherwise
    # PowerShell can turn the log option, path and quote into three arguments.
    return @($Prefix) + @('/LOG="' + $LogPath + '"')
}

if ($ArgumentSelfTest) {
    $prefixes = @(
        @{ name = 'silent-update'; value = @('/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') },
        @{ name = 'permitted-update'; value = @('/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/ALLOWAPPCLOSE=1') },
        @{ name = 'permitted-uninstall'; value = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/ALLOWAPPCLOSE=1') }
    )
    $logPath = 'C:\disposable test path\fixture.log'
    foreach ($case in $prefixes) {
        $arguments = @(New-LifecycleLoggedArguments $case.value $logPath)
        if ($arguments.Count -ne $case.value.Count + 1 -or $arguments[-1] -cne '/LOG="C:\disposable test path\fixture.log"') { throw "Fixture log option split into multiple arguments: $($case.name)" }
        for ($index = 0; $index -lt $case.value.Count; $index++) { if ($arguments[$index] -cne $case.value[$index]) { throw 'Fixture command prefix changed.' } }
    }
    [pscustomobject]@{ passed = $prefixes.Count; failed = 0; checks = @($prefixes | ForEach-Object name) } | ConvertTo-Json -Depth 3
    exit 0
}

if ($TimestampSelfTest) {
    # Pure conversion checks: no fixture processes, files or installation.
    $utc = [DateTime]::SpecifyKind(([DateTime]::new(2026, 11, 5, 8, 20, 0)).AddTicks(1234567), [DateTimeKind]::Utc)
    $offset = [DateTimeOffset]::new(2026, 11, 5, 17, 20, 0, [TimeSpan]::FromHours(9)).AddTicks(1234567)
    $json = '{"parentStartUtc":"2026-11-05T17:20:00.1234567+09:00","childStartUtc":"2026-11-05T08:20:00.1234567Z"}' | ConvertFrom-Json
    $cases = [ordered]@{
        utcString = '2026-11-05T08:20:00.1234567Z'
        offsetString = '2026-11-05T17:20:00.1234567+09:00'
        utcDateTime = $utc
        localDateTime = $utc.ToLocalTime()
        offsetDateTime = $offset
        jsonOffset = $json.parentStartUtc
        jsonUtc = $json.childStartUtc
    }
    foreach ($name in $cases.Keys) {
        if ((Convert-LifecycleUtcTicks $cases[$name]) -ne $utc.Ticks) { throw "Timestamp precision/offset conversion failed: $name" }
    }
    [pscustomobject]@{ passed = $cases.Count; failed = 0; expectedUtcTicks = $utc.Ticks; checks = @($cases.Keys) } | ConvertTo-Json -Depth 3
    exit 0
}

if ($env:OS -ne 'Windows_NT' -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $env:RUNNER_TEMP) {
    throw 'Lifecycle fixtures require a disposable GitHub-hosted Windows runner. No application was started.'
}
$root = [IO.Path]::GetFullPath($TestRoot)
$runner = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
if (-not $root.StartsWith($runner, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($root) -notmatch '^cuo-setup-test-[0-9a-f]{32}$') { throw 'Unexpected disposable test directory.' }
$portableExe = [IO.Path]::GetFullPath((Join-Path $PortablePath 'UsageOverlay.exe'))
$installedExe = [IO.Path]::GetFullPath((Join-Path $InstalledPath 'UsageOverlay.exe'))
if (-not $portableExe.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Portable fixture must be inside disposable test directory.' }
if (@(Get-Process -Name UsageOverlay -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Fixture requires no other running Overlay process.' }
$script:LifecycleDiagnosticChecks = New-Object 'System.Collections.Generic.List[object]'
$script:LifecycleDiagnosticRuns = New-Object 'System.Collections.Generic.List[object]'

function Assert-Lifecycle([bool]$Condition, [string]$Name) {
    $script:LifecycleDiagnosticChecks.Add([pscustomobject]@{ name = $Name; passed = $Condition; utc = [DateTime]::UtcNow.ToString('o') })
    if (-not $Condition) { throw "Failed: $Name" }
    Write-Output ('Passed: ' + $Name)
}
function Invoke-LifecycleSetup([string]$File, [string[]]$Arguments) {
    $observation = [pscustomobject]@{ fileName = [IO.Path]::GetFileName($File); arguments = @($Arguments); startedUtc = [DateTime]::UtcNow.ToString('o'); completedUtc = $null; exitCode = $null; timedOut = $false }
    $script:LifecycleDiagnosticRuns.Add($observation)
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(90000)) { $observation.timedOut = $true; throw "Setup fixture process did not exit: $File" }
        $process.Refresh(); $observation.exitCode = $process.ExitCode
        if ($process.ExitCode -ne 0) {
            Write-Host ('Fixture command returned exit code ' + $process.ExitCode + ': ' + $observation.fileName)
            Write-Host ('Exact fixture arguments: ' + ($Arguments | ConvertTo-Json -Compress))
            foreach ($argument in $Arguments) {
                if ($argument -match '^/LOG="(?<path>[^"]+)"$') {
                    $log = [IO.Path]::GetFullPath($Matches.path)
                    if (-not $log.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture log escaped its disposable test directory.' }
                    if ([IO.File]::Exists($log)) {
                        Write-Host ('Last fixture log lines: ' + $log)
                        Get-Content -LiteralPath $log -Tail 50 | ForEach-Object { Write-Host $_ }
                    } else { Write-Host ('Fixture log was not created: ' + $log) }
                }
            }
        }
        return $process.ExitCode
    } finally { $observation.completedUtc = [DateTime]::UtcNow.ToString('o'); $process.Dispose() }
}

function Save-LifecycleDiagnostics($Failure) {
    try {
        $project = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
        $destination = [IO.Path]::GetFullPath((Join-Path $project ('.build\setup-integration-diagnostics\' + [IO.Path]::GetFileName($root))))
        if (-not $destination.StartsWith($project + '\.build\setup-integration-diagnostics\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected diagnostics directory.' }
        $current = $destination
        while ($true) {
            if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse points are not supported in diagnostics paths.' }
            if ($current.Equals($project, [StringComparison]::OrdinalIgnoreCase)) { break }
            $current = [IO.Path]::GetDirectoryName($current)
        }
        $null = [IO.Directory]::CreateDirectory($destination)
        foreach ($file in @(Get-ChildItem -LiteralPath $root -File | Where-Object { $_.Extension -in @('.log', '.json') })) {
            if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            [IO.File]::Copy($file.FullName, (Join-Path $destination $file.Name), $true)
        }
        $summary = [pscustomobject]@{ failure = $Failure.Exception.Message; utc = [DateTime]::UtcNow.ToString('o'); checks = $script:LifecycleDiagnosticChecks.ToArray(); commands = $script:LifecycleDiagnosticRuns.ToArray() }
        [IO.File]::WriteAllText((Join-Path $destination 'lifecycle-checks.json'), ($summary | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        Write-Host ('Fixture failure diagnostics saved: ' + $destination)
    } catch { Write-Host ('Could not save fixture diagnostics: ' + $_.Exception.Message) }
}
function Start-LifecycleFixture([string]$Executable, [string]$Name, [switch]$Legacy) {
    $ready = Join-Path $root ($Name + '.json')
    $arguments = @('--lifetime-fixture', '--ready', ('"' + $ready + '"'), '--child-delay', '500', '--exit-delay', '5000')
    if ($Legacy) { $arguments += '--legacy' }
    $parent = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not [IO.File]::Exists($ready)) {
        if ($parent.HasExited -or $timer.ElapsedMilliseconds -gt 15000) { throw 'Fixture did not become ready.' }
        Start-Sleep -Milliseconds 50
    }
    $record = Get-Content -LiteralPath $ready -Raw | ConvertFrom-Json
    if ($record.parentPid -ne $parent.Id -or $parent.StartTime.ToUniversalTime().Ticks -ne (Convert-LifecycleUtcTicks $record.parentStartUtc) -or $parent.Path -ine $Executable) { throw 'Fixture parent identity did not match its captured process.' }
    $child = Get-Process -Id $record.childPid -ErrorAction Stop
    if ($child.StartTime.ToUniversalTime().Ticks -ne (Convert-LifecycleUtcTicks $record.childStartUtc) -or $child.Path -ine $Executable) { throw 'Fixture child identity did not match.' }
    $null = $parent.Handle; $null = $child.Handle
    return [pscustomobject]@{ Parent = $parent; Child = $child; Record = $record; Executable = $Executable }
}
function Test-LifecycleAlive($Fixture) {
    return (-not $Fixture.Parent.HasExited -and -not $Fixture.Child.HasExited -and
        $Fixture.Parent.StartTime.ToUniversalTime().Ticks -eq (Convert-LifecycleUtcTicks $Fixture.Record.parentStartUtc) -and
        $Fixture.Child.StartTime.ToUniversalTime().Ticks -eq (Convert-LifecycleUtcTicks $Fixture.Record.childStartUtc))
}
function Wait-LifecycleExited($Fixture) {
    Assert-Lifecycle ($Fixture.Parent.WaitForExit(1000) -and $Fixture.Child.WaitForExit(1000)) 'captured parent and job-child handles prove actual exit'
    $Fixture.Parent.Refresh(); $Fixture.Child.Refresh()
    Assert-Lifecycle ($Fixture.Parent.ExitCode -eq 0 -and $Fixture.Child.ExitCode -eq 0) 'fixture parent and owned child exit cleanly'
}
function Close-LifecycleFixture($Fixture) {
    if (-not $Fixture -or $Fixture.Parent.HasExited) { return }
    # Cleanup is restricted to the exact fixture pair created above. Never signal
    # the shared event if another process could have taken the application role.
    if (-not (Test-LifecycleAlive $Fixture)) { throw 'Cannot safely identify fixture during cleanup.' }
    $ids = @($Fixture.Parent.Id, $Fixture.Child.Id)
    foreach ($process in @(Get-Process -Name UsageOverlay -ErrorAction SilentlyContinue)) {
        if ($process.Id -notin $ids) { throw 'Another Overlay process appeared; fixture cleanup refused.' }
    }
    $signal = [Threading.EventWaitHandle]::OpenExisting('Local\PersonalCodexUsageOverlayShutdown')
    try { $null = $signal.Set() } finally { $signal.Dispose() }
    if (-not $Fixture.Parent.WaitForExit(10000) -or -not $Fixture.Child.WaitForExit(1000)) { throw 'Owned fixture did not cleanly stop.' }
}
function Get-LifecycleFileSnapshot {
    $snapshot = [ordered]@{}
    foreach ($file in @(Get-ChildItem -LiteralPath $InstalledPath -File | Sort-Object Name)) { $snapshot[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    return ($snapshot | ConvertTo-Json -Compress)
}

$fixture = $null
try {
    $baseArguments = @('/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/TASKS=')
    $fixture = Start-LifecycleFixture $portableExe 'portable-running'
    $duplicateReady = Join-Path $root 'duplicate-should-not-be-created.json'
    $duplicateCode = Invoke-LifecycleSetup $installedExe @('--lifetime-fixture', '--ready', ('"' + $duplicateReady + '"'), '--child-delay', '500', '--exit-delay', '0')
    Assert-Lifecycle ($duplicateCode -eq 4) 'installed copy cannot create a second instance while portable fixture owns the singleton'
    Assert-Lifecycle (-not [IO.File]::Exists($duplicateReady)) 'duplicate start creates no fixture readiness or child tree'
    Assert-Lifecycle (Test-LifecycleAlive $fixture) 'duplicate start preserves the original portable parent and child'
    $expectedIds = @($fixture.Parent.Id, $fixture.Child.Id)
    $currentIds = @(Get-Process -Name UsageOverlay -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    Assert-Lifecycle ($currentIds.Count -eq 2 -and @($currentIds | Where-Object { $_ -notin $expectedIds }).Count -eq 0) 'portable and installed copies share exactly one parent and its owned child'
    $before = Get-LifecycleFileSnapshot
    $code = Invoke-LifecycleSetup $SetupPath (New-LifecycleLoggedArguments $baseArguments (Join-Path $root 'running-update-refused.log'))
    Assert-Lifecycle ($code -ne 0) 'unattended update requires explicit close permission'
    Assert-Lifecycle (Test-LifecycleAlive $fixture) 'refused update leaves both fixture processes alive'
    Assert-Lifecycle ((Get-LifecycleFileSnapshot) -eq $before) 'refused update leaves every installed file unchanged'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $code = Invoke-LifecycleSetup $SetupPath (New-LifecycleLoggedArguments ($baseArguments + '/ALLOWAPPCLOSE=1') (Join-Path $root 'running-update.log'))
    Assert-Lifecycle ($code -eq 0) 'permitted update completes'
    Wait-LifecycleExited $fixture
    Assert-Lifecycle ($timer.ElapsedMilliseconds -ge 5300) 'update waits through child drain and parent post-mutex exit delay'
    $fixture.Parent.Dispose(); $fixture.Child.Dispose(); $fixture = $null

    $fixture = Start-LifecycleFixture $portableExe 'legacy-running' -Legacy
    $before = Get-LifecycleFileSnapshot
    $code = Invoke-LifecycleSetup $SetupPath (New-LifecycleLoggedArguments ($baseArguments + '/ALLOWAPPCLOSE=1') (Join-Path $root 'legacy-refused.log'))
    Assert-Lifecycle ($code -ne 0) 'unidentifiable legacy fixture is refused even with close permission'
    Assert-Lifecycle (Test-LifecycleAlive $fixture) 'legacy refusal does not signal or kill fixture'
    Assert-Lifecycle ((Get-LifecycleFileSnapshot) -eq $before) 'legacy refusal preserves installed bytes'
    Close-LifecycleFixture $fixture
    $fixture.Parent.Dispose(); $fixture.Child.Dispose(); $fixture = $null

    $fixture = Start-LifecycleFixture $installedExe 'installed-running'
    $uninstaller = Join-Path $InstalledPath 'unins000.exe'
    $before = Get-LifecycleFileSnapshot
    $code = Invoke-LifecycleSetup $uninstaller (New-LifecycleLoggedArguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') (Join-Path $root 'running-uninstall-refused.log'))
    Assert-Lifecycle ($code -ne 0) 'unattended uninstall requires explicit close permission'
    Assert-Lifecycle (Test-LifecycleAlive $fixture) 'refused uninstall leaves fixture alive'
    Assert-Lifecycle ((Get-LifecycleFileSnapshot) -eq $before) 'refused uninstall leaves installed bytes unchanged'
    $code = Invoke-LifecycleSetup $uninstaller (New-LifecycleLoggedArguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/ALLOWAPPCLOSE=1') (Join-Path $root 'running-uninstall.log'))
    Assert-Lifecycle ($code -eq 0) 'permitted uninstall completes'
    Wait-LifecycleExited $fixture
    Assert-Lifecycle (-not [IO.File]::Exists($installedExe)) 'running uninstall removes executable only after actual exit'
    $fixture.Parent.Dispose(); $fixture.Child.Dispose(); $fixture = $null
}
catch { Save-LifecycleDiagnostics $_; throw }
finally {
    if ($fixture) {
        Close-LifecycleFixture $fixture
        $fixture.Parent.Dispose(); $fixture.Child.Dispose()
    }
}
exit 0
