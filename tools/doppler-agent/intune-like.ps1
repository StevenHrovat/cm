<#
.SYNOPSIS
Runs the package's install or uninstall command line the way Intune does: as SYSTEM, from the package folder, in 32-bit PowerShell.

.DESCRIPTION
Security context: defensive security engineering under the charter in CLAUDE.md (CM.BlackHole, Doppler, lane L4b goal 3).

For the proof on a clean test VM, without Intune: the source folder package.ps1 built (the same files that went into the
.intunewin) is copied to the VM, and this script starts exactly the Intune command line in it as SYSTEM through a one-shot
scheduled task, from the 32-bit PowerShell Intune's management extension uses, waits for it and prints its exit code and output.
It also runs the agent's "status" as SYSTEM afterwards (the TPM keys are SYSTEM's).

Run it in an elevated PowerShell on the test VM only. Without -Apply it prints what it would run.

.PARAMETER PackageFolder
The package's source folder on the VM (cm-doppler-agent.exe, install.ps1, uninstall.ps1, agent.json).

.PARAMETER Action
Install (default), Uninstall or Status.

.PARAMETER Apply
Actually run it.
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageFolder,
    [ValidateSet('Install', 'Uninstall', 'Status')][string]$Action = 'Install',
    [switch]$Apply,
    # The computer this script is meant for; it stops at once on any other (docs/process/owner-scripts.md). The operating organisation's host name is never in the repository.
    [Parameter(Mandatory)][string]$ExpectedHost
)

$ErrorActionPreference = 'Stop'
# The guard first (docs/process/owner-scripts.md): wrong machine, session or edition stops the script here, before anything else runs.
Import-Module (Join-Path $PSScriptRoot '..\lib\Guard.psm1') -Force -ErrorAction Stop
$null = Assert-Script -ScriptPath $PSCommandPath -Kind Changes -Applying:$Apply -ExpectedHost $ExpectedHost -Elevation Required
$folder = (Resolve-Path -LiteralPath $PackageFolder).Path
$powershell32 = Join-Path $env:SystemRoot 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $powershell32)) { $powershell32 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe' }
$outFile = Join-Path $env:ProgramData 'cm-intune-like.out.txt'
switch ($Action) {
    'Install' { $command = '-NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Apply' }
    'Uninstall' { $command = '-NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1 -Apply' }
    'Status' { $command = "-NoProfile -Command `"& '$env:ProgramFiles\CM\DopplerAgent\cm-doppler-agent.exe' status`"" }
}
# The task writes its output to a file; cmd /c keeps the exit code.
$cmdLine = "/c cd /d `"$folder`" && `"$powershell32`" $command > `"$outFile`" 2>&1"

Write-Host "== $Action as SYSTEM in $folder" -ForegroundColor Cyan
Write-Host "   $powershell32 $command"
if (-not $Apply) { Write-Host '   WOULD    run it through a one-shot scheduled task as SYSTEM. Add -Apply.' -ForegroundColor Yellow; exit 0 }

$name = 'cm-intune-like-' + [guid]::NewGuid().ToString('n').Substring(0, 8)
$taskAction = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\cmd.exe') -Argument $cmdLine -WorkingDirectory $folder
$principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName $name -Action $taskAction -Principal $principal | Out-Null
try {
    Start-ScheduledTask -TaskName $name
    do {
        Start-Sleep -Seconds 2
        $task = Get-ScheduledTask -TaskName $name
    } while ($task.State -eq 'Running' -or $task.State -eq 'Queued')
    $code = (Get-ScheduledTaskInfo -TaskName $name).LastTaskResult
    if (Test-Path -LiteralPath $outFile) { Get-Content -LiteralPath $outFile | ForEach-Object { Write-Host "   | $_" } }
    Write-Host "   exit code $code" -ForegroundColor $(if ($code -eq 0) { 'Green' } else { 'Red' })
    exit $code
}
finally {
    Unregister-ScheduledTask -TaskName $name -Confirm:$false
    Remove-Item -LiteralPath $outFile -ErrorAction SilentlyContinue
}
