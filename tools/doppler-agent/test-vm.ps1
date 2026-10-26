<#
.SYNOPSIS
Creates, prepares and removes the Doppler proof VM: one Generation 2 Windows 11 VM with Secure Boot, a virtual TPM and BitLocker on one Hyper-V host
(lane L4b goal 3, tools/doppler-agent/README.md "The VM proof").

.DESCRIPTION
Security context: defensive security engineering under the charter in CLAUDE.md (CM.BlackHole, Doppler). The VM is a throw-away test machine in
a workgroup: not domain-joined, not in Intune, removed after the acceptance test. Creating it is a gated step; it needs the owner's words naming
the host first.

Run it on the admin machine in Windows PowerShell 5.1 as an account that is a Hyper-V administrator on -HostName. It reaches the host over WinRM
(Invoke-Command, Kerberos) and the guest over PowerShell Direct from the host, so the guest needs no network for setup. Never run git elevated.

Actions (every one without -Apply prints WOULD lines and changes nothing; every one is safe to run again):
  Check         read only: the host's Hyper-V, memory, switches, the folder and its free space, the ISO, the VM if it exists, the local guardian.
  Create        the VM (fixed memory, the disk, Secure Boot with the Microsoft Windows template, a vTPM, the guest service interface), the Windows
                ISO and an answer ISO (unattended setup, one local administrator), boot from DVD, start, press a key for the DVD prompt.
  Prepare       waits until setup has finished (PowerShell Direct answers), turns BitLocker on with the TPM protector, reports Secure Boot and the
                TPM, then detaches both ISOs and deletes the answer ISO.
  InstallAgent  copies the package source folder (package.ps1 -Apply, "source") and intune-like.ps1 into the guest and runs exactly the Intune
                command line as SYSTEM there (intune-like.ps1 -Apply).
  Remove        turns the VM off, deletes it, its disk, the answer ISO and its folder (only a folder this script created, it holds a marker file),
                and the local credential file. The host's local guardian stays: other VMs with a vTPM may use it.

Secrets: the guest's local administrator password is generated here, never printed, and kept only as a DPAPI-protected credential file under
the operator's profile ($env:LOCALAPPDATA\cm\doppler-test-vm\<VmName>.xml); it is in the answer ISO on the host until Prepare deletes that.
agent.json in the package holds the knock secret: the copy on the host is deleted after InstallAgent. Nothing here goes into the repository.

.PARAMETER Action
Check (default), Create, Prepare, InstallAgent or Remove.

.PARAMETER HostName
The Hyper-V host, full name (Kerberos). The owner names it; this script does not choose (the operations master ranks the hosts by load).

.PARAMETER VmName
The VM's name and computer name, at most 15 characters. Default CM-DOPPLER-T01.

.PARAMETER VmRoot
Create: a folder on the host's data volume; the VM lives in <VmRoot>\<VmName>.

.PARAMETER SwitchName
Create: the virtual switch, one from which the guest reaches the Doppler test host.

.PARAMETER IsoPath
Create: the Windows 11 Enterprise evaluation ISO, as a path on the host.

.PARAMETER ImageIndex
The image in install.wim to install. Default 1 (the evaluation ISO has one edition).

.PARAMETER PackageFolder
InstallAgent: the package's source folder on this machine (package.ps1 -Apply prints it).

.EXAMPLE
.\test-vm.ps1 -ExpectedHost <this-computer> -HostName host.example.invalid -VmRoot E:\cm-test -SwitchName 'External' -IsoPath E:\iso\win11-eval.iso

.EXAMPLE
.\test-vm.ps1 -ExpectedHost <this-computer> -Action Create -HostName host.example.invalid -VmRoot E:\cm-test -SwitchName 'External' -IsoPath E:\iso\win11-eval.iso -Apply
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Check', 'Create', 'Prepare', 'InstallAgent', 'Remove')][string]$Action = 'Check',
    [Parameter(Mandatory)][string]$HostName,
    [ValidatePattern('^[A-Za-z0-9-]{1,15}$')][string]$VmName = 'CM-DOPPLER-T01',
    [string]$VmRoot,
    [string]$SwitchName,
    [string]$IsoPath,
    [int]$ImageIndex = 1,
    [ValidateRange(4, 64)][int]$MemoryGB = 8,
    [ValidateRange(2, 16)][int]$ProcessorCount = 4,
    [ValidateRange(64, 512)][int]$DiskGB = 80,
    [string]$PackageFolder,
    [switch]$Apply,
    # The computer this script is meant for; it stops at once on any other (docs/process/owner-scripts.md). The operating organisation's host name is never in the repository.
    [Parameter(Mandatory)][string]$ExpectedHost
)
$ErrorActionPreference = 'Stop'
# The guard first (docs/process/owner-scripts.md): wrong machine, session or edition stops the script here, before anything else runs.
Import-Module (Join-Path $PSScriptRoot '..\lib\Guard.psm1') -Force -ErrorAction Stop
$null = Assert-Script -ScriptPath $PSCommandPath -Kind Changes -Applying:$Apply -ExpectedHost $ExpectedHost -Elevation Either

function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Line([string]$Text, [string]$Color = 'Gray') { Write-Host "   $Text" -ForegroundColor $Color }
function Write-Lines($Lines) { foreach ($l in @($Lines)) { $c = 'Gray'; if ("$l" -like 'WOULD*') { $c = 'Yellow' } elseif ("$l" -like 'FAIL*') { $c = 'Red' } elseif ("$l" -like 'OK*' -or "$l" -like 'CREATED*' -or "$l" -like 'DONE*') { $c = 'Green' }; Write-Line "$l" $c } }

$credFolder = Join-Path $env:LOCALAPPDATA 'cm\doppler-test-vm'
$credFile = Join-Path $credFolder "$VmName.xml"
$guestUser = 'cmtest'
$marker = 'cm-doppler-test-vm.txt'

function Get-GuestCredential {
    if (-not (Test-Path -LiteralPath $credFile)) { throw "No credential file for $VmName ($credFile). Create made it; without it the guest cannot be reached." }
    Import-Clixml -LiteralPath $credFile
}

# Letters and digits plus a few symbols that need no escaping in XML or on a command line.
function New-GuestPassword {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_.+='.ToCharArray()
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] }) + 'Aa1-'
}

function New-AnswerXml([string]$ComputerName, [string]$Password, [int]$Index) {
    $c = 'processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS"'
    @"
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
  <settings pass="windowsPE">
    <component name="Microsoft-Windows-International-Core-WinPE" $c>
      <SetupUILanguage><UILanguage>en-US</UILanguage></SetupUILanguage>
      <InputLocale>de-DE</InputLocale><SystemLocale>en-US</SystemLocale><UILanguage>en-US</UILanguage><UserLocale>de-DE</UserLocale>
    </component>
    <component name="Microsoft-Windows-Setup" $c>
      <DiskConfiguration>
        <Disk wcm:action="add">
          <DiskID>0</DiskID><WillWipeDisk>true</WillWipeDisk>
          <CreatePartitions>
            <CreatePartition wcm:action="add"><Order>1</Order><Type>EFI</Type><Size>260</Size></CreatePartition>
            <CreatePartition wcm:action="add"><Order>2</Order><Type>MSR</Type><Size>16</Size></CreatePartition>
            <CreatePartition wcm:action="add"><Order>3</Order><Type>Primary</Type><Extend>true</Extend></CreatePartition>
          </CreatePartitions>
          <ModifyPartitions>
            <ModifyPartition wcm:action="add"><Order>1</Order><PartitionID>1</PartitionID><Format>FAT32</Format><Label>System</Label></ModifyPartition>
            <ModifyPartition wcm:action="add"><Order>2</Order><PartitionID>2</PartitionID></ModifyPartition>
            <ModifyPartition wcm:action="add"><Order>3</Order><PartitionID>3</PartitionID><Format>NTFS</Format><Label>Windows</Label><Letter>C</Letter></ModifyPartition>
          </ModifyPartitions>
        </Disk>
      </DiskConfiguration>
      <ImageInstall><OSImage>
        <InstallTo><DiskID>0</DiskID><PartitionID>3</PartitionID></InstallTo>
        <InstallFrom><MetaData wcm:action="add"><Key>/IMAGE/INDEX</Key><Value>$Index</Value></MetaData></InstallFrom>
      </OSImage></ImageInstall>
      <UserData><AcceptEula>true</AcceptEula></UserData>
    </component>
  </settings>
  <settings pass="specialize">
    <component name="Microsoft-Windows-Shell-Setup" $c><ComputerName>$ComputerName</ComputerName></component>
  </settings>
  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-Shell-Setup" $c>
      <OOBE>
        <HideEULAPage>true</HideEULAPage><HideOnlineAccountScreens>true</HideOnlineAccountScreens><HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
        <HideLocalAccountScreen>true</HideLocalAccountScreen><HideOEMRegistrationScreen>true</HideOEMRegistrationScreen><ProtectYourPC>3</ProtectYourPC>
      </OOBE>
      <UserAccounts><LocalAccounts><LocalAccount wcm:action="add">
        <Name>$guestUser</Name><Group>Administrators</Group><Password><Value>$Password</Value><PlainText>true</PlainText></Password>
      </LocalAccount></LocalAccounts></UserAccounts>
    </component>
  </settings>
</unattend>
"@
}

# ---- runs on the host -----------------------------------------------------------------------------------------------

$checkBlock = {
    param($VmName, $VmRoot, $SwitchName, $IsoPath, $MemoryGB, $DiskGB)
    $out = New-Object System.Collections.Generic.List[string]
    $os = Get-CimInstance Win32_OperatingSystem
    $out.Add("host: $($os.Caption) $($os.Version), $([math]::Round($os.FreePhysicalMemory / 1MB)) of $([math]::Round($os.TotalVisibleMemorySize / 1MB)) GB memory free")
    if (-not (Get-Module -ListAvailable -Name Hyper-V)) { $out.Add('FAIL     the Hyper-V PowerShell module is not on the host'); return $out }
    $out.Add('OK       Hyper-V module present')
    if ($os.FreePhysicalMemory / 1MB -lt $MemoryGB + 4) { $out.Add("FAIL     less than $($MemoryGB + 4) GB free memory") }
    $switches = @(Get-VMSwitch | ForEach-Object { "$($_.Name) ($($_.SwitchType))" })
    $out.Add('switches: ' + ($switches -join ', '))
    if ($SwitchName) { if (Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue) { $out.Add("OK       switch '$SwitchName'") } else { $out.Add("FAIL     no switch '$SwitchName'") } }
    if ($VmRoot) {
        $qualifier = Split-Path -Path $VmRoot -Qualifier
        $vol = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$qualifier'"
        if ($vol) {
            $free = [math]::Round($vol.FreeSpace / 1GB)
            $color = if ($vol.FreeSpace / 1GB -ge $DiskGB + 20) { 'OK      ' } else { 'FAIL    ' }
            $out.Add("$color volume $qualifier $free GB free of $([math]::Round($vol.Size / 1GB)) GB (needs $DiskGB + 20)")
        } else { $out.Add("FAIL     no volume $qualifier") }
        $out.Add("folder $VmRoot exists: $(Test-Path -LiteralPath $VmRoot)")
    }
    if ($IsoPath) { if (Test-Path -LiteralPath $IsoPath) { $out.Add("OK       ISO $IsoPath ($([math]::Round((Get-Item -LiteralPath $IsoPath).Length / 1GB, 1)) GB)") } else { $out.Add("FAIL     no ISO at $IsoPath") } }
    if (Get-Module -ListAvailable -Name HgsClient) {
        $g = Get-HgsGuardian -Name UntrustedGuardian -ErrorAction SilentlyContinue
        $out.Add("local guardian UntrustedGuardian: $(if ($g) { 'present' } else { 'absent (Create adds it once, for the vTPM key protector)' })")
    } else { $out.Add('FAIL     the HgsClient module is not on the host (needed for a vTPM key protector)') }
    $vm = Get-VM -Name $VmName -ErrorAction SilentlyContinue
    if ($vm) {
        $fw = Get-VMFirmware -VM $vm
        $sec = Get-VMSecurity -VM $vm
        $out.Add("VM $VmName exists: $($vm.State), generation $($vm.Generation), Secure Boot $($fw.SecureBoot), vTPM $($sec.TpmEnabled), path $($vm.Path)")
    } else { $out.Add("VM $VmName does not exist") }
    $out
}

$createBlock = {
    param($VmName, $VmRoot, $SwitchName, $IsoPath, $MemoryGB, $ProcessorCount, $DiskGB, $AnswerXml, $Marker, $Apply)
    $out = New-Object System.Collections.Generic.List[string]
    $dir = Join-Path $VmRoot $VmName
    $answerIso = Join-Path $dir 'cm-answer.iso'
    $vm = Get-VM -Name $VmName -ErrorAction SilentlyContinue
    if ($vm) { $out.Add("ALREADY  VM $VmName ($($vm.State))") }
    elseif (-not $Apply) { $out.Add("WOULD    create $dir, a $DiskGB GB disk and the generation 2 VM $VmName ($ProcessorCount vCPU, $MemoryGB GB fixed) on '$SwitchName'") }
    else {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dir $Marker) -Value "Created by tools/doppler-agent/test-vm.ps1 for the Doppler VM proof. test-vm.ps1 -Action Remove deletes this folder."
        $vm = New-VM -Name $VmName -Generation 2 -MemoryStartupBytes ([int64]$MemoryGB * 1GB) -NewVHDPath (Join-Path $dir "$VmName.vhdx") -NewVHDSizeBytes ([int64]$DiskGB * 1GB) -SwitchName $SwitchName -Path $VmRoot
        $out.Add("CREATED  VM $VmName in $dir")
    }
    if (-not $Apply) {
        $out.Add('WOULD    set fixed memory, the vCPUs, Secure Boot (template MicrosoftWindows), a vTPM (local guardian UntrustedGuardian, created if absent), the guest service interface')
        $out.Add("WOULD    write $answerIso (unattended setup, local administrator, password only in the operator's DPAPI file) and attach it and $IsoPath, boot from DVD, start, press a key")
        return $out
    }
    if ($vm.State -ne 'Off') { $out.Add("ALREADY  running ($($vm.State)); settings and DVDs left as they are"); return $out }

    Set-VMProcessor -VM $vm -Count $ProcessorCount
    Set-VMMemory -VM $vm -DynamicMemoryEnabled $false -StartupBytes ([int64]$MemoryGB * 1GB)
    Set-VMFirmware -VM $vm -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows
    if (-not (Get-VMSecurity -VM $vm).TpmEnabled) {
        $g = Get-HgsGuardian -Name UntrustedGuardian -ErrorAction SilentlyContinue
        if (-not $g) { $g = New-HgsGuardian -Name UntrustedGuardian -GenerateCertificates; $out.Add('CREATED  local guardian UntrustedGuardian') }
        $kp = New-HgsKeyProtector -Owner $g -AllowUntrustedRoot
        Set-VMKeyProtector -VM $vm -KeyProtector $kp.RawData
        Enable-VMTPM -VM $vm
        $out.Add('DONE     vTPM on')
    } else { $out.Add('ALREADY  vTPM on') }
    Enable-VMIntegrationService -VM $vm -Name 'Guest Service Interface'

    if (-not (Test-Path -LiteralPath $answerIso)) {
        $tmp = Join-Path $env:TEMP ('cm-answer-' + [guid]::NewGuid().ToString('n'))
        New-Item -ItemType Directory -Path $tmp | Out-Null
        try {
            [IO.File]::WriteAllText((Join-Path $tmp 'autounattend.xml'), $AnswerXml, (New-Object Text.UTF8Encoding $false))
            if (-not ('CmIsoWriter' -as [type])) {
                Add-Type -TypeDefinition @'
using System; using System.IO; using System.Runtime.InteropServices.ComTypes;
public static class CmIsoWriter {
    public static void Save(object stream, string path, int blockSize, int blocks) {
        IStream s = (IStream)stream;
        using (FileStream f = File.Create(path)) {
            byte[] buffer = new byte[blockSize];
            for (int i = 0; i < blocks; i++) { s.Read(buffer, blockSize, IntPtr.Zero); f.Write(buffer, 0, blockSize); }
        }
    }
}
'@
            }
            $fsi = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
            $fsi.FileSystemsToCreate = 3
            $fsi.VolumeName = 'CMANSWER'
            $fsi.Root.AddTree($tmp, $false)
            $image = $fsi.CreateResultImage()
            [CmIsoWriter]::Save($image.ImageStream, $answerIso, $image.BlockSize, $image.TotalBlocks)
        } finally {
            Remove-Item -LiteralPath $tmp -Recurse -Force
        }
        $out.Add("CREATED  $answerIso")
    }
    $drives = @(Get-VMDvdDrive -VM $vm)
    if (-not ($drives | Where-Object { $_.Path -eq $IsoPath })) { Add-VMDvdDrive -VM $vm -Path $IsoPath }
    if (-not ($drives | Where-Object { $_.Path -eq $answerIso })) { Add-VMDvdDrive -VM $vm -Path $answerIso }
    Set-VMFirmware -VM $vm -FirstBootDevice (Get-VMDvdDrive -VM $vm | Where-Object { $_.Path -eq $IsoPath })
    Start-VM -VM $vm
    # A generation 2 VM shows "Press any key to boot from CD or DVD" for a few seconds; Enter through the VM's keyboard answers it.
    $keyboard = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_ComputerSystem -Filter "ElementName='$VmName'" | Get-CimAssociatedInstance -ResultClassName Msvm_Keyboard
    for ($i = 0; $i -lt 12; $i++) { Start-Sleep -Seconds 1; if ($keyboard) { Invoke-CimMethod -InputObject $keyboard -MethodName TypeKey -Arguments @{ keyCode = [uint32]13 } | Out-Null } }
    $out.Add('DONE     started; unattended setup takes 15 to 30 minutes, then run -Action Prepare')
    $out
}

$prepareBlock = {
    param($VmName, $Credential, $Apply)
    $out = New-Object System.Collections.Generic.List[string]
    $vm = Get-VM -Name $VmName -ErrorAction SilentlyContinue
    if (-not $vm) { $out.Add("FAIL     no VM $VmName"); return $out }
    if (-not $Apply) {
        $out.Add('WOULD    wait (up to 60 min) until PowerShell Direct answers, turn BitLocker on (TPM protector, XTS-AES 256, used space), report Secure Boot and the TPM')
        $out.Add('WOULD    detach both ISOs and delete the answer ISO')
        return $out
    }
    $ready = $false
    for ($i = 0; $i -lt 120 -and -not $ready; $i++) {
        try { Invoke-Command -VMName $VmName -Credential $Credential -ScriptBlock { $env:COMPUTERNAME } -ErrorAction Stop | Out-Null; $ready = $true }
        catch { Start-Sleep -Seconds 30 }
    }
    if (-not $ready) { $out.Add('FAIL     the guest did not answer over PowerShell Direct within 60 minutes'); return $out }
    $guest = Invoke-Command -VMName $VmName -Credential $Credential -ScriptBlock {
        $r = New-Object System.Collections.Generic.List[string]
        $r.Add("guest: $((Get-CimInstance Win32_OperatingSystem).Caption) $((Get-CimInstance Win32_OperatingSystem).Version)")
        $r.Add("Secure Boot: $(Confirm-SecureBootUEFI)")
        $tpm = Get-Tpm
        $r.Add("TPM present $($tpm.TpmPresent), ready $($tpm.TpmReady)")
        $bl = Get-BitLockerVolume -MountPoint 'C:'
        if ($bl.ProtectionStatus -ne 'On' -and $bl.VolumeStatus -eq 'FullyDecrypted') {
            Enable-BitLocker -MountPoint 'C:' -EncryptionMethod XtsAes256 -UsedSpaceOnly -TpmProtector -SkipHardwareTest | Out-Null
            $r.Add('DONE     BitLocker turned on')
        }
        for ($i = 0; $i -lt 60 -and (Get-BitLockerVolume -MountPoint 'C:').VolumeStatus -ne 'FullyEncrypted'; $i++) { Start-Sleep -Seconds 10 }
        if ((Get-BitLockerVolume -MountPoint 'C:').ProtectionStatus -ne 'On') { Resume-BitLocker -MountPoint 'C:' | Out-Null }
        $bl = Get-BitLockerVolume -MountPoint 'C:'
        $r.Add("BitLocker C: $($bl.VolumeStatus), protection $($bl.ProtectionStatus), $($bl.EncryptionMethod)")
        $r
    }
    $guest | ForEach-Object { $out.Add($_) }
    foreach ($d in @(Get-VMDvdDrive -VM $vm)) {
        $path = $d.Path
        Remove-VMDvdDrive -VMDvdDrive $d
        if ($path -and (Split-Path -Leaf $path) -eq 'cm-answer.iso') { Remove-Item -LiteralPath $path -Force; $out.Add("DONE     deleted $path") }
    }
    $out.Add('DONE     ISOs detached')
    $out
}

$removeBlock = {
    param($VmName, $Marker, $Apply)
    $out = New-Object System.Collections.Generic.List[string]
    $vm = Get-VM -Name $VmName -ErrorAction SilentlyContinue
    if (-not $vm) { $out.Add("ALREADY  no VM $VmName"); return $out }
    $disks = @(Get-VMHardDiskDrive -VM $vm | ForEach-Object { $_.Path })
    $dvds = @(Get-VMDvdDrive -VM $vm | Where-Object { $_.Path -and (Split-Path -Leaf $_.Path) -eq 'cm-answer.iso' } | ForEach-Object { $_.Path })
    $folders = @($disks | ForEach-Object { Split-Path -Parent $_ } | Select-Object -Unique | Where-Object { Test-Path -LiteralPath (Join-Path $_ $Marker) })
    if (-not $Apply) {
        $out.Add("WOULD    turn off and delete VM $VmName ($($vm.State))")
        $disks + $dvds | ForEach-Object { $out.Add("WOULD    delete $_") }
        $folders | ForEach-Object { $out.Add("WOULD    delete folder $_ (it holds $Marker)") }
        return $out
    }
    if ($vm.State -ne 'Off') { Stop-VM -VM $vm -TurnOff -Force }
    Remove-VM -VM $vm -Force
    $out.Add("DONE     VM $VmName deleted")
    foreach ($f in $disks + $dvds) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force; $out.Add("DONE     deleted $f") } }
    foreach ($f in $folders) { Remove-Item -LiteralPath $f -Recurse -Force; $out.Add("DONE     deleted folder $f") }
    $out
}

# ---- the actions ----------------------------------------------------------------------------------------------------

Write-Step "$Action on $HostName, VM $VmName$(if (-not $Apply -and $Action -ne 'Check') { ' (dry run, add -Apply)' })"
switch ($Action) {
    'Check' {
        Write-Lines (Invoke-Command -ComputerName $HostName -ScriptBlock $checkBlock -ArgumentList $VmName, $VmRoot, $SwitchName, $IsoPath, $MemoryGB, $DiskGB)
        Write-Line "local credential file: $(if (Test-Path -LiteralPath $credFile) { 'present' } else { 'absent' })"
    }
    'Create' {
        foreach ($p in 'VmRoot', 'SwitchName', 'IsoPath') { if (-not (Get-Variable -Name $p -ValueOnly)) { throw "Create needs -$p." } }
        $answer = ''
        if ($Apply -and -not (Test-Path -LiteralPath $credFile)) {
            $password = New-GuestPassword
            New-Item -ItemType Directory -Path $credFolder -Force | Out-Null
            New-Object System.Management.Automation.PSCredential ($guestUser, (ConvertTo-SecureString -String $password -AsPlainText -Force)) | Export-Clixml -LiteralPath $credFile
            Write-Line "DONE     credential for $guestUser kept in $credFile (DPAPI, this account on this machine only)" 'Green'
        }
        if ($Apply) {
            $password = (Get-GuestCredential).GetNetworkCredential().Password
            $answer = New-AnswerXml -ComputerName $VmName -Password $password -Index $ImageIndex
            $password = $null
        }
        Write-Lines (Invoke-Command -ComputerName $HostName -ScriptBlock $createBlock -ArgumentList $VmName, $VmRoot, $SwitchName, $IsoPath, $MemoryGB, $ProcessorCount, $DiskGB, $answer, $marker, ([bool]$Apply))
        $answer = $null
    }
    'Prepare' {
        $cred = if ($Apply) { Get-GuestCredential } else { $null }
        Write-Lines (Invoke-Command -ComputerName $HostName -ScriptBlock $prepareBlock -ArgumentList $VmName, $cred, ([bool]$Apply))
    }
    'InstallAgent' {
        if (-not $PackageFolder -or -not (Test-Path -LiteralPath (Join-Path $PackageFolder 'install.ps1'))) { throw 'InstallAgent needs -PackageFolder: the source folder package.ps1 built (with install.ps1).' }
        $intuneLike = Join-Path $PSScriptRoot 'intune-like.ps1'
        if (-not $Apply) {
            Write-Line "WOULD    copy $PackageFolder and intune-like.ps1 to the host and into the guest (C:\cm-package, C:\cm-tools), delete the host copy" 'Yellow'
            Write-Line 'WOULD    run in the guest: intune-like.ps1 -PackageFolder C:\cm-package -Apply (the Intune command line as SYSTEM)' 'Yellow'
            break
        }
        $cred = Get-GuestCredential
        $hostSession = New-PSSession -ComputerName $HostName
        try {
            $staging = Invoke-Command -Session $hostSession -ScriptBlock { $d = Join-Path $env:TEMP ('cm-package-' + [guid]::NewGuid().ToString('n')); New-Item -ItemType Directory -Path $d | Out-Null; $d }
            Copy-Item -Path (Join-Path $PackageFolder '*') -Destination $staging -ToSession $hostSession -Recurse
            Copy-Item -LiteralPath $intuneLike -Destination $staging -ToSession $hostSession
            $lines = Invoke-Command -Session $hostSession -ArgumentList $VmName, $cred, $staging -ScriptBlock {
                param($VmName, $Credential, $Staging)
                try {
                    $guest = New-PSSession -VMName $VmName -Credential $Credential
                    try {
                        Invoke-Command -Session $guest -ScriptBlock { foreach ($d in 'C:\cm-package', 'C:\cm-tools') { if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory -Path $d | Out-Null } }
                        Get-ChildItem -LiteralPath $Staging | Where-Object { $_.Name -ne 'intune-like.ps1' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination 'C:\cm-package' -ToSession $guest -Recurse }
                        Copy-Item -LiteralPath (Join-Path $Staging 'intune-like.ps1') -Destination 'C:\cm-tools' -ToSession $guest
                        Invoke-Command -Session $guest -ScriptBlock { & 'C:\cm-tools\intune-like.ps1' -PackageFolder 'C:\cm-package' -Apply *>&1 | ForEach-Object { "$_" }; "exit $LASTEXITCODE" }
                    } finally { Remove-PSSession $guest }
                } finally { Remove-Item -LiteralPath $Staging -Recurse -Force }
            }
            Write-Lines $lines
        } finally { Remove-PSSession $hostSession }
    }
    'Remove' {
        Write-Lines (Invoke-Command -ComputerName $HostName -ScriptBlock $removeBlock -ArgumentList $VmName, $marker, ([bool]$Apply))
        if ($Apply -and (Test-Path -LiteralPath $credFile)) { Remove-Item -LiteralPath $credFile -Force; Write-Line "DONE     deleted $credFile" 'Green' }
        elseif (Test-Path -LiteralPath $credFile) { Write-Line "WOULD    delete $credFile" 'Yellow' }
    }
}
