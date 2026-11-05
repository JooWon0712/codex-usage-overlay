[CmdletBinding()]
param(
    [string]$SetupPath = (Join-Path $PSScriptRoot 'dist\codex-usage-overlay-setup-windows-x64.exe'),
    [string]$CompilerPath = $env:INNO_SETUP_COMPILER,
    [switch]$Integration
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'setup.ps1') -CompilerPath $CompilerPath
$checks = New-Object 'System.Collections.Generic.List[string]'
$script:SetupDiagnosticRuns = New-Object 'System.Collections.Generic.List[object]'
function Assert-SetupTest([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "Failed: $Name" }
    $checks.Add($Name)
}
function Get-SetupTestHashes([string]$Directory) {
    $hashes = @{}
    foreach ($name in $script:CuoPortableFiles) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash }
    return $hashes
}
function Assert-SetupTestHashes([string]$Directory, [hashtable]$Expected, [string[]]$Except = @()) {
    foreach ($name in $script:CuoPortableFiles) {
        if ($name -notin $Except) { Assert-SetupTest ((Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash -eq $Expected[$name]) ("installed payload bytes: $name") }
    }
}
function Invoke-SetupTestProcess([string]$File, [string[]]$Arguments) {
    $observation = [pscustomobject]@{ fileName = [IO.Path]::GetFileName($File); arguments = @($Arguments); startedUtc = [DateTime]::UtcNow.ToString('o'); completedUtc = $null; exitCode = $null; timedOut = $false }
    $script:SetupDiagnosticRuns.Add($observation)
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(90000)) { $observation.timedOut = $true; throw "Test process did not finish in 90 seconds: $File" }
        $process.Refresh(); $observation.exitCode = $process.ExitCode
        if ($process.ExitCode -ne 0) {
            Write-Host ('Setup test command returned exit code ' + $process.ExitCode + ': ' + $observation.fileName)
            Write-Host ('Exact Setup test arguments: ' + ($Arguments | ConvertTo-Json -Compress))
            foreach ($argument in $Arguments) {
                if ($argument -match '^/LOG="(?<path>[^"]+)"$') {
                    $log = [IO.Path]::GetFullPath($Matches.path)
                    if (-not $log.StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Setup test log escaped its disposable test directory.' }
                    if ([IO.File]::Exists($log)) {
                        Write-Host ('Last Setup test log lines: ' + $log)
                        Get-Content -LiteralPath $log -Tail 50 | ForEach-Object { Write-Host $_ }
                    } else { Write-Host ('Setup test log was not created: ' + $log) }
                }
            }
        }
        return $process.ExitCode
    } finally { $observation.completedUtc = [DateTime]::UtcNow.ToString('o'); $process.Dispose() }
}

function Save-SetupIntegrationDiagnostics($Failure) {
    try {
        $diagnosticRoot = Join-Path $PSScriptRoot '.build\setup-integration-diagnostics'
        $destination = Assert-CuoPortableChildPath (Join-Path $diagnosticRoot ([IO.Path]::GetFileName($testRoot))) $diagnosticRoot
        Assert-CuoPortableNoReparsePoint $destination $PSScriptRoot
        $null = [IO.Directory]::CreateDirectory($destination)
        # Top-level fixture logs/readiness records contain no account session.
        foreach ($file in @(Get-ChildItem -LiteralPath $testRoot -File | Where-Object { $_.Extension -in @('.log', '.json') })) {
            if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            [IO.File]::Copy($file.FullName, (Join-Path $destination $file.Name), $true)
        }
        $summary = [pscustomobject]@{ failure = $Failure.Exception.Message; utc = [DateTime]::UtcNow.ToString('o'); passedChecks = $checks.ToArray(); commands = $script:SetupDiagnosticRuns.ToArray() }
        [IO.File]::WriteAllText((Join-Path $destination 'setup-checks.json'), ($summary | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        Write-Host ('Setup failure diagnostics saved: ' + $destination)
    } catch { Write-Host ('Could not save Setup diagnostics: ' + $_.Exception.Message) }
}
function Assert-SetupTestShortcut([string]$Path, [string]$Target) {
    $shell = New-Object -ComObject WScript.Shell
    try { $link = $shell.CreateShortcut($Path); Assert-SetupTest ($link.TargetPath -ieq $Target) ('shortcut target: ' + [IO.Path]::GetFileName($Path)) }
    finally { if ($link) { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }; $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
}
function Get-SetupTestAutostartSnapshot {
    $snapshot = [ordered]@{}
    foreach ($path in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce')) {
        $values = [ordered]@{}
        if (Test-Path -LiteralPath $path) {
            $key = Get-Item -LiteralPath $path
            foreach ($name in @($key.GetValueNames() | Sort-Object)) { $values[$name] = @($key.GetValueKind($name).ToString(), $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)) }
        }
        $snapshot[$path] = $values
    }
    $startup = [Environment]::GetFolderPath('Startup')
    $snapshot['StartupFiles'] = @(Get-ChildItem -LiteralPath $startup -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { $_.Name + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash })
    return ($snapshot | ConvertTo-Json -Depth 8 -Compress)
}

function New-SyntheticSetupFixture {
    param([string]$Root, [string]$Payload, [string]$Compiler)
    # This is an isolated synthetic older version, not a historical release.
    # Only the copied assembly/file versions differ from the current source.
    $fixture = Join-Path $Root 'synthetic-older-project'
    foreach ($directory in @('src', 'assets', 'installer', 'dist\codex-usage-overlay')) { $null = [IO.Directory]::CreateDirectory((Join-Path $fixture $directory)) }
    foreach ($sourceFile in @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File)) { [IO.File]::Copy($sourceFile.FullName, (Join-Path $fixture ('src\' + $sourceFile.Name)), $false) }
    foreach ($name in @('assets\app.ico', 'overlay.manifest', 'build.ps1', 'package.ps1', 'setup.ps1', 'installer\codex-usage-overlay.iss', 'installer\requirements.txt')) { [IO.File]::Copy((Join-Path $PSScriptRoot $name), (Join-Path $fixture $name), $false) }
    $program = Join-Path $fixture 'src\Program.cs'
    $original = [IO.File]::ReadAllText($program)
    $versionPattern = '(Assembly(?:File)?Version\(")\d+\.\d+\.\d+\.\d+("\))'
    if ([regex]::Matches($original, $versionPattern).Count -ne 2) { throw 'Expected exactly two version fields in synthetic fixture source.' }
    $synthetic = [regex]::Replace($original, $versionPattern, [Text.RegularExpressions.MatchEvaluator]{ param($match) $match.Groups[1].Value + '0.0.1.0' + $match.Groups[2].Value })
    [IO.File]::WriteAllText($program, $synthetic, [Text.UTF8Encoding]::new($true))
    $fixturePayload = Join-Path $fixture 'dist\codex-usage-overlay'
    & (Join-Path $fixture 'build.ps1') -OutputDirectory $fixturePayload -PortableRuntimePaths | Out-Host
    foreach ($name in $script:CuoPortableFiles) {
        if ($name -notin @('UsageOverlay.exe', 'runtime-paths.json')) { [IO.File]::Copy((Join-Path $Payload $name), (Join-Path $fixturePayload $name), $false) }
    }
    & (Join-Path $fixture 'setup.ps1') -PayloadPath $fixturePayload -CompilerPath $Compiler -OutputDirectory (Join-Path $fixture 'dist') | Out-Host
    return [pscustomobject]@{ Setup = (Join-Path $fixture ('dist\' + $script:CuoSetupFile)); Payload = $fixturePayload }
}

$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'installer\codex-usage-overlay.iss'))
Assert-SetupTest ($source -match '(?m)^AppId=\{\{18D4E149-28A8-4F05-B98E-10B66B725C54\}') 'stable installer application identity'
Assert-SetupTest ($source -match '(?m)^PrivilegesRequired=lowest\r?$') 'current-user installation'
Assert-SetupTest ($source -match '(?m)^DefaultDirName=\{localappdata\}\\Programs\\CodexUsageOverlay\r?$' -and $source -match '(?m)^UsePreviousAppDir=yes\r?$') 'stable default and previous custom directory'
Assert-SetupTest ($source -match 'InstallLocation.*PreviousDirectory' -and $source -match "CustomMessage\('LocationChanged'\)") 'registered installation directory changes are refused before update'
Assert-SetupTest ($source -match '(?m)^CloseApplications=no\r?$' -and $source -match '(?m)^RestartApplications=no\r?$') 'Restart Manager never force-closes or restarts apps'
Assert-SetupTest ($source -notmatch '(?mi)^\[(Run|UninstallRun|Registry|InstallDelete|UninstallDelete)\]') 'no app launch, startup registration or broad deletion sections'
$fileLines = @($source -split '\r?\n' | Where-Object { $_ -match '^Source:' })
Assert-SetupTest ($fileLines.Count -eq $script:CuoPortableFiles.Count -and ($fileLines -join "`n") -notmatch '\*') 'only the seven exact portable payload files are included'
$runtimeLine = @($fileLines | Where-Object { $_ -match 'runtime-paths\.json' })
Assert-SetupTest ($runtimeLine.Count -eq 1 -and $runtimeLine[0] -match 'onlyifdoesntexist uninsneveruninstall') 'runtime configuration survives upgrade and uninstall'
Assert-SetupTest ($source -notmatch 'Source:.*settings\.json') 'user settings are outside installer file ownership'
Assert-SetupTest ($source -match 'Local\\PersonalCodexUsageOverlayUpdateLock' -and $source -match 'CreateMutexW@kernel32.dll' -and $source -match 'ReleaseMutex@kernel32.dll') 'native update gate is shared with application startup'
Assert-SetupTest ($source -match '--prepare-install --target' -and $source -match '--no-close' -and $source -match 'ALLOWAPPCLOSE') 'verified exit with explicit unattended close permission'
Assert-SetupTest ($source -match 'usAppMutexCheck' -and $source -match 'DeinitializeUninstall' -and $source -match 'Abort;') 'uninstaller prepares before removal and releases its gate'
Assert-SetupTest ([IO.File]::Exists($SetupPath) -and (Get-Item -LiteralPath $SetupPath).Length -gt 0) 'Setup executable exists'
$setupExe = [IO.Path]::GetFullPath($SetupPath)
$stream = [IO.File]::OpenRead($setupExe)
try { Assert-SetupTest ($stream.ReadByte() -eq 77 -and $stream.ReadByte() -eq 90) 'Setup is a Windows executable' }
finally { $stream.Dispose() }
$expectedVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src\Program.cs')), 'AssemblyVersion\("([\d.]+)"\)').Groups[1].Value
$setupVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($setupExe)
Assert-SetupTest ([Version]$setupVersion.FileVersion -eq [Version]$expectedVersion) 'Setup version matches application source'
& (Join-Path $PSScriptRoot 'tools\test-setup-lifecycle.ps1') -TimestampSelfTest | Out-Host
Assert-SetupTest ($LASTEXITCODE -eq 0) 'fixture timestamp identity preserves offset and fractional ticks across PowerShell versions'
& (Join-Path $PSScriptRoot 'tools\test-setup-lifecycle.ps1') -ArgumentSelfTest | Out-Host
Assert-SetupTest ($LASTEXITCODE -eq 0) 'fixture log path and explicit close permission remain separate complete arguments'

if ($Integration) {
    # This switch is intentionally unavailable on personal PCs or self-hosted runners.
    if ($env:OS -ne 'Windows_NT' -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $env:RUNNER_TEMP) {
        throw 'Integration installation tests run only on disposable GitHub-hosted Windows runners. No installer was executed.'
    }
    $temporaryRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP)
    $testRoot = Assert-CuoPortableChildPath (Join-Path $temporaryRoot ('cuo-setup-test-' + [Guid]::NewGuid().ToString('N'))) $temporaryRoot
    Assert-CuoPortableNoReparsePoint $testRoot $temporaryRoot
    $null = [IO.Directory]::CreateDirectory($testRoot)
    $defaultInstall = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\CodexUsageOverlay'
    $dataRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexUsageOverlay'
    $settings = Join-Path $dataRoot 'settings.json'
    $startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Usage Overlay.lnk'
    $desktop = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Codex Usage Overlay.lnk'
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $script:CuoSetupAppId
    foreach ($existing in @($defaultInstall, $dataRoot, $startMenu, $desktop, $uninstallKey)) {
        if (Test-Path -LiteralPath $existing) { throw "Disposable runner must have no existing Overlay installation, settings or shortcuts: $existing" }
    }
    $autoBefore = Get-SetupTestAutostartSnapshot
    $payload = Join-Path $PSScriptRoot 'dist\codex-usage-overlay'
    Assert-CuoPortablePayload $payload $PSScriptRoot
    $payloadHashes = Get-SetupTestHashes $payload
    $integrationCompiler = Resolve-CuoSetupCompiler $CompilerPath
    $portable = Join-Path $testRoot 'portable'
    $null = [IO.Directory]::CreateDirectory($portable)
    foreach ($name in $script:CuoPortableFiles) { [IO.File]::Copy((Join-Path $payload $name), (Join-Path $portable $name), $false) }
    $installed = $null
    try {
        $olderFixture = New-SyntheticSetupFixture -Root $testRoot -Payload $payload -Compiler $integrationCompiler
        $olderHashes = Get-SetupTestHashes $olderFixture.Payload
        Assert-SetupTest ([Version]([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $olderFixture.Payload 'UsageOverlay.exe'))).FileVersion -eq [Version]'0.0.1.0') 'isolated older-version fixture has synthetic EXE version 0.0.1.0'
        $baseArguments = @('/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-')
        $code = Invoke-SetupTestProcess $olderFixture.Setup ($baseArguments + @('/TASKS=', ('/LOG="' + (Join-Path $testRoot 'first-synthetic-older.log') + '"')))
        Assert-SetupTest ($code -eq 0) 'first install of synthetic older version succeeds without starting the app'
        $installed = $defaultInstall
        Assert-SetupTest ([IO.File]::Exists((Join-Path $installed 'UsageOverlay.exe'))) 'default current-user path is used'
        Assert-SetupTest (Test-Path -LiteralPath $uninstallKey) 'Installed Apps uninstall registration exists'
        Assert-SetupTest ((Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion -eq '0.0.1.0') 'synthetic older AppVersion is registered before upgrade'
        Assert-SetupTest (-not [IO.File]::Exists($desktop)) 'desktop shortcut is optional and off by default'
        Assert-SetupTestShortcut $startMenu (Join-Path $installed 'UsageOverlay.exe')
        Assert-SetupTestHashes $installed $olderHashes
        Assert-SetupTest (@(Get-Process -Name UsageOverlay -ErrorAction SilentlyContinue).Count -eq 0) 'installation does not launch the application'

        $null = [IO.Directory]::CreateDirectory($dataRoot)
        $settingsText = '{"setupTest":"' + [Guid]::NewGuid().ToString('N') + '"}'
        [IO.File]::WriteAllText($settings, $settingsText, [Text.UTF8Encoding]::new($false))
        $runtimeText = '{"Node":"user-custom-node.exe","Codex":"user-custom-codex.exe"}'
        [IO.File]::WriteAllText((Join-Path $installed 'runtime-paths.json'), $runtimeText, [Text.UTF8Encoding]::new($false))
        $code = Invoke-SetupTestProcess $setupExe ($baseArguments + @('/TASKS=', ('/LOG="' + (Join-Path $testRoot 'upgrade.log') + '"')))
        Assert-SetupTest ($code -eq 0) 'synthetic older-version installation upgrades to current Setup'
        Assert-SetupTest ((Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion -eq $expectedVersion) 'upgrade registers the current AppVersion'
        Assert-SetupTest ([Version]([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $installed 'UsageOverlay.exe'))).FileVersion -eq [Version]$expectedVersion) 'upgrade replaces older EXE with the current version'
        Assert-SetupTest ([IO.File]::ReadAllText($settings) -eq $settingsText) 'upgrade preserves user settings'
        Assert-SetupTest ([IO.File]::ReadAllText((Join-Path $installed 'runtime-paths.json')) -eq $runtimeText) 'upgrade preserves custom runtime paths'
        Assert-SetupTestHashes $installed $payloadHashes @('runtime-paths.json')

        $blockedDirectory = Join-Path $testRoot 'refused-moved-install'
        $beforeLocationAttempt = Get-SetupTestHashes $installed
        $code = Invoke-SetupTestProcess $setupExe ($baseArguments + @('/TASKS=', ('/DIR="' + $blockedDirectory + '"'), ('/LOG="' + (Join-Path $testRoot 'refused-directory-change.log') + '"')))
        Assert-SetupTest ($code -ne 0) 'update refuses a different installation directory'
        Assert-SetupTest (-not [IO.File]::Exists((Join-Path $blockedDirectory 'UsageOverlay.exe'))) 'refused location change does not create another executable'
        Assert-SetupTest ((Get-ItemProperty -LiteralPath $uninstallKey).InstallLocation.TrimEnd('\') -ieq $installed.TrimEnd('\')) 'refused location change preserves registered installation path'
        Assert-SetupTestHashes $installed $beforeLocationAttempt

        # Fixture mode owns no account session or UI. Its ready file identifies the
        # exact fake parent and job children; normal application execution is absent.
        & (Join-Path $PSScriptRoot 'tools\test-setup-lifecycle.ps1') -SetupPath $setupExe -InstalledPath $installed -PortablePath $portable -TestRoot $testRoot
        Assert-SetupTest ($LASTEXITCODE -eq 0) 'running update and removal prove parent and owned-job exit'
        Assert-SetupTest (-not [IO.File]::Exists((Join-Path $installed 'UsageOverlay.exe'))) 'uninstall removes application executable'
        Assert-SetupTest (-not (Test-Path -LiteralPath $uninstallKey)) 'uninstall removes Installed Apps entry'
        Assert-SetupTest (-not [IO.File]::Exists($startMenu)) 'uninstall removes Start menu shortcut'
        Assert-SetupTest ([IO.File]::ReadAllText($settings) -eq $settingsText) 'uninstall preserves user settings'
        Assert-SetupTest ([IO.File]::ReadAllText((Join-Path $installed 'runtime-paths.json')) -eq $runtimeText) 'uninstall preserves custom runtime configuration'
        $installed = $null

        $custom = Join-Path $testRoot 'custom-installed'
        $code = Invoke-SetupTestProcess $setupExe ($baseArguments + @('/TASKS=desktopicon', ('/DIR="' + $custom + '"'), ('/LOG="' + (Join-Path $testRoot 'custom-first.log') + '"')))
        Assert-SetupTest ($code -eq 0 -and [IO.File]::Exists((Join-Path $custom 'UsageOverlay.exe'))) 'custom install directory is supported'
        $installed = $custom
        Assert-SetupTestShortcut $desktop (Join-Path $custom 'UsageOverlay.exe')
        $code = Invoke-SetupTestProcess $setupExe ($baseArguments + @('/TASKS=desktopicon', ('/LOG="' + (Join-Path $testRoot 'custom-upgrade.log') + '"')))
        Assert-SetupTest ($code -eq 0) 'upgrade reuses previous custom directory'
        Assert-SetupTestShortcut $startMenu (Join-Path $custom 'UsageOverlay.exe')
        Assert-SetupTest (-not [IO.File]::Exists((Join-Path $defaultInstall 'UsageOverlay.exe'))) 'custom upgrade does not create another default install'
        $code = Invoke-SetupTestProcess (Join-Path $custom 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $testRoot 'custom-uninstall.log') + '"'))
        Assert-SetupTest ($code -eq 0) 'custom directory uninstall succeeds'
        $installed = $null
        Assert-SetupTest (-not [IO.File]::Exists($desktop)) 'uninstall removes selected desktop shortcut'
        Assert-SetupTestHashes $portable $payloadHashes
        Assert-SetupTestHashes $payload $payloadHashes
        Assert-SetupTest ((Get-SetupTestAutostartSnapshot) -eq $autoBefore) 'Run keys and Startup files are unchanged'
    }
    catch { Save-SetupIntegrationDiagnostics $_; throw }
    finally {
        # Failures retain logs and installed fixtures for the disposable runner's
        # diagnostic artifact. Successful runs remove only known temporary files.
        if (-not $installed) {
            if ([IO.File]::Exists($settings) -and [IO.File]::ReadAllText($settings) -eq $settingsText) { Remove-Item -LiteralPath $settings -Force }
            if ([IO.Directory]::Exists($dataRoot) -and @(Get-ChildItem -LiteralPath $dataRoot -Force).Count -eq 0) { [IO.Directory]::Delete($dataRoot, $false) }
            $runtime = Join-Path $defaultInstall 'runtime-paths.json'
            if ([IO.File]::Exists($runtime) -and [IO.File]::ReadAllText($runtime) -eq $runtimeText) { Remove-Item -LiteralPath $runtime -Force }
            if ([IO.Directory]::Exists($defaultInstall) -and @(Get-ChildItem -LiteralPath $defaultInstall -Force).Count -eq 0) { [IO.Directory]::Delete($defaultInstall, $false) }
        }
    }
}
[pscustomobject]@{ passed = $checks.Count; failed = 0; integration = [bool]$Integration; checks = @($checks) } | ConvertTo-Json -Depth 3
