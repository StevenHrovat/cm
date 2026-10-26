<#
.SYNOPSIS
Sets up a test environment on a Windows machine: WSL2 with a hardened Ubuntu, GitHub Actions runners in it, and the task that keeps it running. Dry run until -Apply.

.DESCRIPTION
The one script an agent (or a person) runs to get from "a Windows laptop" to "runners that execute my repositories' CI". Read tools\testenv\README.md first.
Every stage is a DRY RUN until you add -Apply: it reads the live state, prints WOULD lines for what it would change, and changes nothing. Every change is a guarded
write (read first, write only when the state is the one it may overwrite, read back afterwards). Run it twice and the second run says "already" everywhere.
Nothing is written to a file that holds a secret: the registration token lives in memory and in the environment of one child process.

Stages (-Stage), in the order they are needed:
  Check      Default. Reads only: WSL, the distro, its user, the runners GitHub lists, the keepalive task.
  Wsl        Installs WSL2 and the distro (wsl.exe --install --no-launch). Needs an ELEVATED shell. A reboot may be needed; run the script again afterwards.
  Distro     Writes the memory/core cap (.wslconfig, only when none exists), then hardens and provisions the distro with tools\runner\setup-runner-wsl.sh
             (systemd on, no Windows drives, no Windows interop, local user, build tools) and restarts the distro.
  Runners    Registers -RunnersPerRepo runners (label -Labels) for every -Repository in the distro, as systemd services. gh must be signed in with admin on the repository.
  Keepalive  The scheduled task that holds the distro up after logon (WSL stops an idle distro, and with it every runner).
  All        Wsl, Distro, Runners, Keepalive in this order (stops at the first stage that says a reboot is due).

.PARAMETER ExpectedHost
The computer the command block says it runs on. The guard refuses any other. Mandatory.

.PARAMETER Repository
owner/name of each repository the runners serve (Runners stage). Runner names are wsl-<name>-<k>.

.EXAMPLE
& 'C:\path\to\checkout\tools\testenv\setup-test-env.ps1' -ExpectedHost 'MYLAPTOP' -Repository 'owner/a','owner/b'                    # Check: read only
& 'C:\path\to\checkout\tools\testenv\setup-test-env.ps1' -ExpectedHost 'MYLAPTOP' -Repository 'owner/a','owner/b' -Stage All -Apply  # do it
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExpectedHost,
    [ValidateSet('Check', 'Wsl', 'Distro', 'Runners', 'Keepalive', 'All')][string]$Stage = 'Check',
    [switch]$Apply,
    [string[]]$Repository = @(),
    [ValidateRange(1, 8)][int]$RunnersPerRepo = 4,
    [string]$Labels = 'wsl',
    [string]$DistroName = 'Ubuntu-24.04',
    [ValidateRange(4, 128)][int]$MemoryGB = 20,
    [ValidateRange(1, 64)][int]$Processors = 16,
    [ValidateRange(0, 64)][int]$SwapGB = 8,
    [string]$TaskName = 'CI WSL keepalive'
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\lib\Guard.psm1') -Force -ErrorAction Stop
$null = Assert-Script -ScriptPath $PSCommandPath -Kind Changes -Applying:$Apply -ExpectedHost $ExpectedHost -Elevation Either
$ProgressPreference = 'SilentlyContinue'
$wsl = Join-Path $env:SystemRoot 'System32\wsl.exe'
$linuxScript = Join-Path $PSScriptRoot '..\runner\setup-runner-wsl.sh'
$wslConfig = Join-Path $env:USERPROFILE '.wslconfig'
$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Invoke-Wsl([string[]]$Arguments, [string]$StdIn = $null) {
    # wsl.exe prints UTF-16 for its own messages: strip the NULs. Windows PowerShell 5.1 turns native stderr into an error under 'Stop', so relax it here.
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $out = if ($null -ne $StdIn) { $StdIn | & $wsl @Arguments 2>&1 } else { & $wsl @Arguments 2>&1 } } finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{ Code = $LASTEXITCODE; Output = (($out | ForEach-Object { "$_" -replace "`0", '' }) -join "`n") }
}
function Test-DistroInstalled { $r = Invoke-Wsl @('-l', '-q'); return ($r.Code -eq 0 -and (($r.Output -split "`n") | ForEach-Object { $_.Trim() }) -contains $DistroName) }
function Get-LinuxScriptText { return ((Get-Content $linuxScript -Raw) -replace "`r`n", "`n") }
function Test-LinuxState {
    # True when the distro is hardened and provisioned (the user exists, systemd runs as PID 1, no Windows drive mounted).
    if (-not (Test-DistroInstalled)) { return $false }
    $r = Invoke-Wsl @('-d', $DistroName, '-u', 'root', '--', 'bash', '-c', 'id ci-runner >/dev/null 2>&1 && [ "$(ps -p 1 -o comm=)" = systemd ] && [ ! -d /mnt/c/Windows ] && echo READY')
    return ($r.Output -match 'READY')
}

function Invoke-WslStage {
    Write-Step "Wsl: WSL2 and the distro $DistroName"
    $null = Invoke-GuardedWrite -What "wsl.exe --install -d $DistroName --no-launch (needs an elevated shell; a reboot may follow)" `
        -Read { Test-DistroInstalled } -Expect { param($s) $true } -Done { param($s) $s -eq $true } -DependentSteps @('every later stage') -Apply:$Apply `
        -Write {
            if (-not $elevated) { throw 'The Wsl stage needs an elevated shell (Run as administrator). Nothing was changed.' }
            $r = Invoke-Wsl @('--install', '-d', $DistroName, '--no-launch'); Write-Host $r.Output
        }
    if ($Apply -and -not (Test-DistroInstalled)) { Write-Host 'REBOOT or run this stage again: the distro is not installed yet (the first --install enables the Windows features and asks for a restart).' -ForegroundColor Yellow; return $false }
    return $true
}

function Invoke-DistroStage {
    Write-Step "Distro: cap ($MemoryGB GB, $Processors cores), hardening, provisioning"
    $configText = "[wsl2]`r`nmemory=${MemoryGB}GB`r`nprocessors=$Processors`r`nswap=${SwapGB}GB`r`n[experimental]`r`nautoMemoryReclaim=gradual"
    $null = Invoke-GuardedWrite -What "Write $wslConfig (only when none exists: an existing one is never overwritten)" `
        -Read { if (Test-Path $wslConfig) { (Get-Content $wslConfig -Raw).Trim() } else { $null } } `
        -Expect { param($s) $true } -Done { param($s) $null -ne $s } -DependentSteps @() -Apply:$Apply `
        -Write { [IO.File]::WriteAllText($wslConfig, $configText + "`r`n", (New-Object Text.ASCIIEncoding)) }
    $null = Invoke-GuardedWrite -What "Provision $DistroName with setup-runner-wsl.sh (systemd, no Windows drives, no interop, user ci-runner, packages) and restart it" `
        -Read { Test-LinuxState } -Expect { param($s) $true } -Done { param($s) $s -eq $true } -DependentSteps @('Runners') -Apply:$Apply `
        -Write {
            $r = Invoke-Wsl @('-d', $DistroName, '-u', 'root', '--', 'bash', '-s', '--', 'provision') (Get-LinuxScriptText); Write-Host $r.Output
            $null = Invoke-Wsl @('--shutdown')   # applies /etc/wsl.conf; the next wsl call starts the distro again
            Start-Sleep -Seconds 3
        }
}

function Invoke-RunnersStage {
    Write-Step "Runners: $RunnersPerRepo per repository, label $Labels"
    if ($Repository.Count -eq 0) { Write-Host '   NOTE     No -Repository given; nothing to register.'; return }
    foreach ($repo in $Repository) {
        $name = $repo.Split('/')[1]
        $null = Invoke-GuardedWrite -What "Register $RunnersPerRepo runners for $repo (wsl-$name-1..$RunnersPerRepo) as systemd services in $DistroName" `
            -Read {
                $r = Invoke-Wsl @('-d', $DistroName, '-u', 'root', '--', 'bash', '-c', "systemctl list-units 'actions.runner.*' --no-legend 2>/dev/null | grep -c 'wsl-$name-[0-9]'")
                [int](($r.Output -split "`n" | Select-Object -Last 1).Trim())
            } `
            -Expect { param($s) $true } -Done { param($s) $s -ge $RunnersPerRepo } -DependentSteps @() -Apply:$Apply `
            -Write {
                $token = (& gh api -X POST "repos/$repo/actions/runners/registration-token" --jq .token)
                if ($LASTEXITCODE -ne 0 -or -not $token) { throw "gh could not create a registration token for $repo (gh signed in? admin on the repository?)." }
                $env:WSLENV = 'REG_TOKEN:REPO:COUNT:LABELS'; $env:REG_TOKEN = $token; $env:REPO = $repo; $env:COUNT = "$RunnersPerRepo"; $env:LABELS = $Labels
                try { $r = Invoke-Wsl @('-d', $DistroName, '-u', 'root', '--', 'bash', '-s', '--', 'runner') (Get-LinuxScriptText); Write-Host $r.Output }
                finally { Remove-Item Env:REG_TOKEN, Env:REPO, Env:COUNT, Env:LABELS, Env:WSLENV -ErrorAction SilentlyContinue; $token = $null }
            }
    }
}

function Invoke-KeepaliveStage {
    Write-Step "Keepalive: scheduled task '$TaskName' (at logon, holds $DistroName up)"
    $null = Invoke-GuardedWrite -What "Register the scheduled task '$TaskName' (conhost --headless wsl.exe ... sleep infinity, current user, not elevated) and start it" `
        -Read { [bool](Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) } -Expect { param($s) $true } -Done { param($s) $s -eq $true } -DependentSteps @() -Apply:$Apply `
        -Write {
            $who = "$env:USERDOMAIN\$env:USERNAME"
            $action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\conhost.exe') -Argument "--headless $wsl -d $DistroName -u root --exec /bin/sleep infinity"
            $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
            Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger (New-ScheduledTaskTrigger -AtLogOn -User $who) -Settings $settings `
                -Principal (New-ScheduledTaskPrincipal -UserId $who -LogonType Interactive -RunLevel Limited) -Description "Keeps $DistroName (CI runners) running after logon. Remove: Unregister-ScheduledTask '$TaskName'" | Out-Null
            Start-ScheduledTask -TaskName $TaskName
        }
}

function Invoke-Check {
    Write-Step 'Check (read only)'
    Write-Host ("   elevated shell: {0}; wsl.exe: {1}; gh: {2}" -f $elevated, (Test-Path $wsl), [bool](Get-Command gh -ErrorAction SilentlyContinue))
    Write-Host ("   distro {0} installed: {1}; hardened and provisioned: {2}" -f $DistroName, (Test-DistroInstalled), (Test-LinuxState))
    Write-Host ("   {0}: {1}" -f $wslConfig, $(if (Test-Path $wslConfig) { 'present' } else { 'missing' }))
    Write-Host ("   task '{0}': {1}" -f $TaskName, $(if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) { (Get-ScheduledTask -TaskName $TaskName).State } else { 'missing' }))
    foreach ($repo in $Repository) {
        $r = & gh api "repos/$repo/actions/runners" 2>$null
        if ($LASTEXITCODE -eq 0) { $list = @(($r | ConvertFrom-Json).runners); Write-Host ("   {0}: {1} runner(s) online of {2} registered" -f $repo, @($list | Where-Object { $_.status -eq 'online' }).Count, $list.Count) }
        else { Write-Host "   $repo : cannot list runners (gh signed in? admin on the repository?)" }
    }
}

Write-Host "setup-test-env.ps1  stage $Stage  $(if ($Apply) { 'APPLY' } else { 'dry run (add -Apply to change something)' })" -ForegroundColor White
switch ($Stage) {
    'Check'     { Invoke-Check }
    'Wsl'       { $null = Invoke-WslStage }
    'Distro'    { Invoke-DistroStage }
    'Runners'   { Invoke-RunnersStage }
    'Keepalive' { Invoke-KeepaliveStage }
    'All'       { if (Invoke-WslStage) { Invoke-DistroStage; Invoke-RunnersStage; Invoke-KeepaliveStage } }
}
if (-not $Apply -and $Stage -ne 'Check') { Write-Host ''; Write-Host 'Dry run: nothing was changed. Add -Apply to do it.' -ForegroundColor DarkYellow }
exit (Write-RunSummary)
