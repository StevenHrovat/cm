<#
.SYNOPSIS
Removes the Doppler agent cleanly. Runs from the unpacked Intune Win32 package as SYSTEM. Dry run until -Apply.

.DESCRIPTION
Security context: defensive security engineering under the charter in CLAUDE.md (CM.BlackHole, Doppler, lane L4b goal 3).

The Intune uninstall command line is exactly:
  %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1 -Apply

What it does, in order (each step only when there is something left to do, so a second run changes nothing):
  1. Stops and deletes the service CmDopplerAgent.
  2. Runs "cm-doppler-agent.exe forget": deletes the agent's TPM keys (attestation key and device key, LocalSystem's keys in the
     Platform Crypto Provider) and the diagnose keys.
  3. Restores NetBIOS over TCP/IP on each adapter that is still present to the value install.ps1 kept (netbios-before.json).
  4. Deletes the program folder, the data folder (agent.json with the knock secret, state.json, logs) and the detection marker.

The machine stays in Doppler's register until an administrator revokes it there; with its keys gone it can no longer use the
enrolment. To enrol it again it needs a new plan (DopplerServer.AddPlan: a plan is used up by the enrolment it allows).

Exit codes: 0 done (or nothing left), 1 failed (the message says which step).

.PARAMETER Apply
Actually remove. Without it the script only prints what it would do.
#>
#Requires -Version 5.1
[CmdletBinding()]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'

if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) {
    $native = Join-Path $env:SystemRoot 'Sysnative\WindowsPowerShell\v1.0\powershell.exe'
    $relay = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($Apply) { $relay += '-Apply' }
    & $native @relay
    exit $LASTEXITCODE
}

$ServiceName = 'CmDopplerAgent'
$ExeName = 'cm-doppler-agent.exe'
$ProgramFolder = Join-Path $env:ProgramFiles 'CM\DopplerAgent'
$DataFolder = Join-Path $env:ProgramData 'CM\DopplerAgent'
$RegistryKey = 'HKLM:\SOFTWARE\CM\DopplerAgent'
$TargetExe = Join-Path $ProgramFolder $ExeName
$NetBiosBefore = Join-Path $DataFolder 'netbios-before.json'

function Write-Line([string]$Text, [string]$Color = 'Gray') { Write-Host "   $Text" -ForegroundColor $Color }
function Write-Ok([string]$Text) { Write-Line "OK       $Text" 'Green' }
function Write-Would([string]$Text) { Write-Line "WOULD    $Text" 'Yellow' }
function Write-Did([string]$Text) { Write-Line "DID      $Text" 'Green' }

try {
    Write-Host "== Doppler agent uninstall ($(if ($Apply) { 'apply' } else { 'dry run' }))" -ForegroundColor Cyan

    # 1. The service.
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $service) { Write-Ok "no service $ServiceName" }
    elseif ($Apply) {
        if ($service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
        & sc.exe delete $ServiceName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "sc.exe delete $ServiceName returned $LASTEXITCODE." }
        Write-Did "stopped and deleted $ServiceName"
    }
    else { Write-Would "stop and delete $ServiceName" }

    # 2. The TPM keys. "forget" is idempotent: it reports which keys existed.
    if (-not (Test-Path -LiteralPath $TargetExe)) { Write-Ok 'no agent left to delete TPM keys with' }
    elseif ($Apply) {
        $out = & $TargetExe forget
        if ($LASTEXITCODE -ne 0) { throw "cm-doppler-agent forget returned $LASTEXITCODE." }
        Write-Did "TPM keys: $out"
    }
    else { Write-Would 'delete the agent''s TPM keys (cm-doppler-agent forget)' }

    # 3. NetBIOS back to what it was.
    if (-not (Test-Path -LiteralPath $NetBiosBefore)) { Write-Ok 'no kept NetBIOS settings to restore' }
    else {
        $before = @(Get-Content -LiteralPath $NetBiosBefore -Raw | ConvertFrom-Json)
        foreach ($kept in $before) {
            $adapter = Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter "SettingID = '$($kept.SettingID)'" -ErrorAction SilentlyContinue
            if (-not $adapter) { Write-Ok "adapter $($kept.SettingID) is gone"; continue }
            if ([int]$adapter.TcpipNetbiosOptions -eq [int]$kept.TcpipNetbiosOptions) { Write-Ok "NetBIOS on $($kept.SettingID) is $($kept.TcpipNetbiosOptions) again"; continue }
            if ($Apply) {
                $result = Invoke-CimMethod -InputObject $adapter -MethodName SetTcpipNetbios -Arguments @{ TcpipNetbiosOptions = [uint32]$kept.TcpipNetbiosOptions }
                if ($result.ReturnValue -notin 0, 1) { throw "SetTcpipNetbios on $($kept.SettingID) returned $($result.ReturnValue)." }
                Write-Did "NetBIOS on $($kept.SettingID) back to $($kept.TcpipNetbiosOptions)"
            }
            else { Write-Would "set NetBIOS on $($kept.SettingID) back to $($kept.TcpipNetbiosOptions)" }
        }
    }

    # 4. Folders and the detection marker.
    foreach ($folder in @($ProgramFolder, $DataFolder)) {
        if (-not (Test-Path -LiteralPath $folder)) { Write-Ok "no $folder"; continue }
        if ($Apply) { Remove-Item -LiteralPath $folder -Recurse -Force; Write-Did "deleted $folder" }
        else { Write-Would "delete $folder" }
    }
    if (-not (Test-Path -Path $RegistryKey)) { Write-Ok 'no detection marker' }
    elseif ($Apply) { Remove-Item -Path $RegistryKey -Recurse -Force; Write-Did "deleted $RegistryKey" }
    else { Write-Would "delete $RegistryKey" }
    exit 0
}
catch {
    Write-Line "FAILED   $($_.Exception.Message)" 'Red'
    exit 1
}
