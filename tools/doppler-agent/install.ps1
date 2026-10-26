<#
.SYNOPSIS
Installs the Doppler agent as a Windows service. Runs from the unpacked Intune Win32 package as SYSTEM. Dry run until -Apply.

.DESCRIPTION
Security context: defensive security engineering under the charter in CLAUDE.md. This installs the operating organisation's own device agent
(CM.BlackHole, Doppler, lane L4b goal 3). It connects out to Milky Way's gate only; it opens no inbound port except that it holds TCP
and UDP 138 so that NetBIOS cannot.

The Intune install command line is exactly:
  %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Apply
Intune runs it as SYSTEM from the unpacked package folder. When Intune starts it as a 32-bit process on 64-bit Windows, the script
starts itself again in 64-bit PowerShell (through Sysnative), so the service, the registry and the TPM keys are the 64-bit ones.

Without -Apply the script reads and prints OK / MISSING / WOULD lines and changes nothing. With -Apply it does only what is still
missing (idempotent): a second run on an installed machine changes nothing and exits 0.

What it does, in order:
  1. Program folder %ProgramFiles%\CM\DopplerAgent: copies cm-doppler-agent.exe (when its SHA-256 differs; the service is stopped first).
  2. Data folder %ProgramData%\CM\DopplerAgent: creates it with an ACL of SYSTEM and Administrators only (no inheritance), copies
     agent.json from the package (it holds the knock secret; the script never prints its content).
  3. NetBIOS over TCP/IP off on every IP-enabled adapter through CIM (applies at once). The old value of each adapter is kept in
     netbios-before.json in the data folder, written once on the first install, so the uninstaller can restore it.
  4. The service CmDopplerAgent (LocalSystem, automatic start, restart on failure), started.
  5. HKLM\SOFTWARE\CM\DopplerAgent: Version and PackageSha256 (the Intune detection rule reads PackageSha256).

Exit codes: 0 done (or nothing to do), 1 failed (the message says which step). Log: install.log in the data folder (steps only).

Roles, not values: this script names no host, address or secret. The package carries agent.json, written outside the repository.

.PARAMETER Apply
Actually install. Without it the script only prints what it would do.
#>
#Requires -Version 5.1
[CmdletBinding()]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'

# Intune's management extension can start install commands as a 32-bit process; the agent and its keys are 64-bit.
if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) {
    $native = Join-Path $env:SystemRoot 'Sysnative\WindowsPowerShell\v1.0\powershell.exe'
    $relay = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($Apply) { $relay += '-Apply' }
    & $native @relay
    exit $LASTEXITCODE
}

$ServiceName = 'CmDopplerAgent'
$DisplayName = 'CM Doppler agent'
$ExeName = 'cm-doppler-agent.exe'
$ProgramFolder = Join-Path $env:ProgramFiles 'CM\DopplerAgent'
$DataFolder = Join-Path $env:ProgramData 'CM\DopplerAgent'
$RegistryKey = 'HKLM:\SOFTWARE\CM\DopplerAgent'
$PackageFolder = $PSScriptRoot
$SourceExe = Join-Path $PackageFolder $ExeName
$SourceConfig = Join-Path $PackageFolder 'agent.json'
$TargetExe = Join-Path $ProgramFolder $ExeName
$TargetConfig = Join-Path $DataFolder 'agent.json'
$NetBiosBefore = Join-Path $DataFolder 'netbios-before.json'
$NetBiosDisabled = 2

function Write-Line([string]$Text, [string]$Color = 'Gray') {
    Write-Host "   $Text" -ForegroundColor $Color
    if ($script:LogReady) { Add-Content -Path (Join-Path $DataFolder 'install.log') -Value ("{0:u} {1}" -f (Get-Date).ToUniversalTime(), $Text) }
}
function Write-Ok([string]$Text) { Write-Line "OK       $Text" 'Green' }
function Write-Missing([string]$Text) { Write-Line "MISSING  $Text" 'Yellow' }
function Write-Would([string]$Text) { Write-Line "WOULD    $Text" 'Yellow' }
function Write-Did([string]$Text) { Write-Line "DID      $Text" 'Green' }

function Get-FileHash256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or $identity.IsSystem
}

function Set-DataFolderAcl {
    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $inherit = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
        $account = (New-Object System.Security.Principal.SecurityIdentifier $sid)
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule $account, 'FullControl', $inherit, 'None', 'Allow'))
    }
    $acl.SetOwner((New-Object System.Security.Principal.SecurityIdentifier 'S-1-5-32-544'))
    Set-Acl -LiteralPath $DataFolder -AclObject $acl
}

function Test-DataFolderAcl {
    if (-not (Test-Path -LiteralPath $DataFolder)) { return $false }
    $acl = Get-Acl -LiteralPath $DataFolder
    if (-not $acl.AreAccessRulesProtected) { return $false }
    $sids = @($acl.Access | ForEach-Object { $_.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value } | Sort-Object -Unique)
    return (($sids -join ',') -eq 'S-1-5-18,S-1-5-32-544')
}

try {
    if (-not (Test-Path -LiteralPath $SourceExe)) { throw "The package has no $ExeName next to install.ps1." }
    if (-not (Test-Path -LiteralPath $SourceConfig)) { throw 'The package has no agent.json next to install.ps1.' }
    if ($Apply -and -not (Test-Elevated)) { throw 'install.ps1 -Apply needs SYSTEM or an elevated administrator.' }
    $packageHash = Get-FileHash256 $SourceExe
    $version = (Get-Item -LiteralPath $SourceExe).VersionInfo.ProductVersion
    Write-Host "== Doppler agent install ($(if ($Apply) { 'apply' } else { 'dry run' })), package $packageHash" -ForegroundColor Cyan

    # 2 first, so the log has its folder: the data folder and its ACL.
    if (Test-DataFolderAcl) { Write-Ok "data folder $DataFolder, SYSTEM and Administrators only"; $script:LogReady = $true }
    elseif ($Apply) {
        New-Item -ItemType Directory -Force -Path $DataFolder | Out-Null
        Set-DataFolderAcl
        $script:LogReady = $true
        Write-Did "data folder $DataFolder, ACL SYSTEM and Administrators only"
    }
    else { Write-Would "create $DataFolder with an ACL of SYSTEM and Administrators only" }

    # 1. The program folder and the agent.
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ((Get-FileHash256 $TargetExe) -eq $packageHash) { Write-Ok "$TargetExe is this package's agent" }
    elseif ($Apply) {
        if ($service -and $service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force; Write-Did "stopped $ServiceName for the update" }
        New-Item -ItemType Directory -Force -Path $ProgramFolder | Out-Null
        Copy-Item -LiteralPath $SourceExe -Destination $TargetExe -Force
        Write-Did "copied $ExeName to $ProgramFolder"
    }
    else { Write-Would "copy $ExeName to $ProgramFolder" }

    if ((Get-FileHash256 $TargetConfig) -eq (Get-FileHash256 $SourceConfig)) { Write-Ok 'agent.json is this package''s' }
    elseif ($Apply) { Copy-Item -LiteralPath $SourceConfig -Destination $TargetConfig -Force; Write-Did 'copied agent.json to the data folder' }
    else { Write-Would 'copy agent.json to the data folder' }

    # 3. NetBIOS over TCP/IP off, old values kept once.
    $adapters = @(Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter 'IPEnabled = TRUE')
    if (-not (Test-Path -LiteralPath $NetBiosBefore)) {
        if ($Apply) {
            $before = @($adapters | ForEach-Object { [pscustomobject]@{ SettingID = $_.SettingID; TcpipNetbiosOptions = [int]$_.TcpipNetbiosOptions } })
            ConvertTo-Json -InputObject $before | Set-Content -LiteralPath $NetBiosBefore -Encoding UTF8
            Write-Did "kept the NetBIOS setting of $($before.Count) adapter(s) for the uninstaller"
        }
        else { Write-Would "keep the NetBIOS setting of $($adapters.Count) adapter(s) for the uninstaller" }
    }
    else { Write-Ok 'the NetBIOS settings before the first install are kept' }
    foreach ($adapter in $adapters) {
        if ([int]$adapter.TcpipNetbiosOptions -eq $NetBiosDisabled) { Write-Ok "NetBIOS off on $($adapter.SettingID)"; continue }
        if ($Apply) {
            $result = Invoke-CimMethod -InputObject $adapter -MethodName SetTcpipNetbios -Arguments @{ TcpipNetbiosOptions = [uint32]$NetBiosDisabled }
            if ($result.ReturnValue -notin 0, 1) { throw "SetTcpipNetbios on $($adapter.SettingID) returned $($result.ReturnValue)." }
            Write-Did "NetBIOS off on $($adapter.SettingID)"
        }
        else { Write-Would "switch NetBIOS off on $($adapter.SettingID)" }
    }

    # 4. The service.
    $binary = '"' + $TargetExe + '"'
    if ($service) {
        $current = (Get-CimInstance -ClassName Win32_Service -Filter "Name = '$ServiceName'").PathName
        if ($current -eq $binary) { Write-Ok "service $ServiceName runs $TargetExe" }
        elseif ($Apply) { & sc.exe config $ServiceName binPath= $binary start= auto obj= LocalSystem | Out-Null; Write-Did "service $ServiceName points at $TargetExe" }
        else { Write-Would "point service $ServiceName at $TargetExe" }
    }
    elseif ($Apply) {
        New-Service -Name $ServiceName -BinaryPathName $binary -DisplayName $DisplayName -StartupType Automatic -Description 'Enrols this machine with Doppler (TPM-attested) and sends its heartbeat to Milky Way.' | Out-Null
        Write-Did "service $ServiceName created (LocalSystem, automatic)"
    }
    else { Write-Would "create service $ServiceName (LocalSystem, automatic)" }
    if ($Apply) {
        # Restart after 1, 5 and 15 minutes; the failure count resets after a day. sc.exe failure is idempotent.
        & sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/300000/restart/900000 | Out-Null
        if ((Get-Service -Name $ServiceName).Status -ne 'Running') { Start-Service -Name $ServiceName; Write-Did "started $ServiceName" }
        else { Write-Ok "$ServiceName is running" }
    }
    else { Write-Would "set restart on failure and start $ServiceName" }

    # 5. The detection marker.
    $marker = Get-ItemProperty -Path $RegistryKey -ErrorAction SilentlyContinue
    if ($marker -and $marker.PackageSha256 -eq $packageHash) { Write-Ok "detection marker PackageSha256 $packageHash" }
    elseif ($Apply) {
        New-Item -Path $RegistryKey -Force | Out-Null
        Set-ItemProperty -Path $RegistryKey -Name PackageSha256 -Value $packageHash
        Set-ItemProperty -Path $RegistryKey -Name Version -Value ([string]$version)
        Write-Did "detection marker PackageSha256 $packageHash"
    }
    else { Write-Would "write the detection marker $RegistryKey PackageSha256" }
    exit 0
}
catch {
    Write-Line "FAILED   $($_.Exception.Message)" 'Red'
    exit 1
}
