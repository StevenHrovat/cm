<#
.SYNOPSIS
Builds the Operational CMOS issuing CA (AD CS, under the operational root) of the new certificate authority as a VM on the cluster, in stages. Dry run until -Apply.

.DESCRIPTION
Security context: defensive security engineering under the charter in CLAUDE.md. This script builds the operating organisation's own new
issuing CA (CMOS's own estate). It never reads or writes the old CA, never writes Active Directory and never
publishes the new root anywhere. The plan it implements: CM.BlackHole/docs/ca-plan.md.

Every stage except Check is a DRY RUN until you add -Apply: it reads, prints OK / MISSING / NOTE lines and WOULD lines,
and changes nothing. Check (the default) is read only and is also the proof after each stage. Every stage is idempotent:
a second run only does what is still missing.

Roles, not values: this script names no company, host, network, path or account of the operating organisation. Every place is a
parameter, filled in by the command block of the how-to, which lives outside this repository.

GATED (the owner's own words first): stage Vm creates a VM on the cluster; stage Root is the root ceremony on the ceremony
host (never the cluster) and only prints its commands. Stages Role and Root need -KeyAlgorithm and -HashAlgorithm, which
have no default: they come from the inventory. The other stages work inside the new VM, which is CMOS's own estate once it exists.

Stages, in order:

  Check    Default. Read only. Does the VM exist, on which node, with vTPM, Secure Boot, no automatic checkpoints, the
           VLAN; inside the guest (if -GuestCredential is given and this is the owning node): the AD CS role, the CA
           type and name, the CA certificate, CRL periods, audit filter, CDP/AIA, the firewall rules.
  Vm       Creates the Generation 2 VM from a sysprepped Windows Server Core VHDX on the cluster shared volume, with vTPM,
           Secure Boot, no automatic checkpoints, the network adapter in -VlanId, and makes it a clustered role.
  Role     In the guest (PowerShell Direct, run on the node that owns the VM): installs the AD CS role, writes CAPolicy.inf
           (path length 0, EKU limited to the CMOS roles, CRL periods) and creates the subordinate request. Copies the
           request out to -WorkFolder for the root ceremony.
  Root     Prints the root ceremony's commands for the admin at the ceremony host (create or restore the NIC-less root,
           sign the request, check the dump, store the image). Runs nothing, with or without -Apply.
  Install  In the guest: installs the issued certificate and the root certificate, sets CRL periods, audit, CDP and AIA,
           restricts inbound traffic to WinRM over HTTPS from -RequesterAddress, starts the service, publishes the first CRL.

.PARAMETER Stage
Check (default), Vm, Role, Root, Install.

.PARAMETER Apply
Actually change something. Without it every stage prints WOULD lines.

.PARAMETER ClusterName
The failover cluster that runs the VM.

.PARAMETER NodeName
The cluster node to create the VM on (stage Vm). Stages Role and Install must run ON the node that owns the VM.

.PARAMETER VmName
The VM's name in Hyper-V and the cluster, and the guest's computer name.

.PARAMETER VmPath
The folder on the cluster shared volume for the VM's files, for example a folder below C:\ClusterStorage\<volume>.

.PARAMETER TemplateVhdx
A sysprepped Windows Server Core VHDX (Generation 2). It is copied, never changed.

.PARAMETER SwitchName
The Hyper-V virtual switch of the CMOS segment.

.PARAMETER VlanId
The access VLAN of the CMOS segment. 0 means untagged.

.PARAMETER MemoryGB
Startup memory. Default 4.

.PARAMETER ProcessorCount
Virtual processors. Default 2.

.PARAMETER GuestCredential
The guest's local administrator, for PowerShell Direct. Asked for when a guest stage needs it and it is missing; never stored.

.PARAMETER CaCommonName
The issuing CA's common name. Default "CM Issuing CA 1" (a name of the framework, not of the operating organisation).

.PARAMETER PublicationUrl
The HTTP base URL Milky Way serves the CRLs and CA certificates from (no trailing slash). Role and Install need it.

.PARAMETER RequesterAddress
The address (or range) the CA's one requester (the operations master, for the Operational CMOS issuing CA) connects from. The only source allowed inbound (WinRM over HTTPS). Install needs it.

.PARAMETER KeyAlgorithm
The CA key: RSA4096 or ECDSA_P384. No default: it is decided from the phase 0 inventory (ca-plan.md section 5). Stages Role and
Root refuse to run without it.

.PARAMETER HashAlgorithm
The signature hash: SHA384 or SHA512 (SHA256 only if the inventory shows a consumer that needs it). No default, as KeyAlgorithm.

.PARAMETER WorkFolder
A folder on the host for the request going out and the certificates coming back. Default .\issuing-ca-work.

.EXAMPLE
.\issuing-ca.ps1 -ExpectedHost <this-computer> -ClusterName <cluster> -VmName <vm>                                              # Check, read only

.EXAMPLE
.\issuing-ca.ps1 -ExpectedHost <this-computer> -Stage Vm -ClusterName <cluster> -NodeName <node> -VmName <vm> -VmPath <csv folder> -TemplateVhdx <vhdx> -SwitchName <switch> -VlanId <id>
# dry run; add -Apply only after the owner's words (gated)

.EXAMPLE
.\issuing-ca.ps1 -ExpectedHost <this-computer> -Stage Install -ClusterName <cluster> -VmName <vm> -PublicationUrl <url> -RequesterAddress <address> -Apply
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Check', 'Vm', 'Role', 'Root', 'Install')][string]$Stage = 'Check',
    [switch]$Apply,
    [string]$ClusterName = '',
    [string]$NodeName = '',
    [string]$VmName = '',
    [string]$VmPath = '',
    [string]$TemplateVhdx = '',
    [string]$SwitchName = '',
    [int]$VlanId = 0,
    [int]$MemoryGB = 4,
    [int]$ProcessorCount = 2,
    [System.Management.Automation.PSCredential]$GuestCredential = $null,
    [string]$CaCommonName = 'CM Issuing CA 1',
    [string]$PublicationUrl = '',
    [string]$RequesterAddress = '',
    [ValidateSet('', 'RSA4096', 'ECDSA_P384')][string]$KeyAlgorithm = '',
    [ValidateSet('', 'SHA256', 'SHA384', 'SHA512')][string]$HashAlgorithm = '',
    [string]$WorkFolder = '.\issuing-ca-work',
    # The computer this script is meant for; it stops at once on any other (docs/process/owner-scripts.md). The operating organisation's host name is never in the repository.
    [Parameter(Mandatory)][string]$ExpectedHost
)

$ErrorActionPreference = 'Stop'
# The guard first (docs/process/owner-scripts.md): wrong machine, session or edition stops the script here, before anything else runs.
Import-Module (Join-Path $PSScriptRoot '..\lib\Guard.psm1') -Force -ErrorAction Stop
$null = Assert-Script -ScriptPath $PSCommandPath -Kind Changes -Applying:$Apply -ExpectedHost $ExpectedHost -Elevation Either

# The CMOS role OIDs (cm.os/src/Trust/TrustRole.cs, TrustOids). A test in cm.os.Tests checks that this list matches the code.
$CmArc = '2.25.43701335367132911904664927645766928773'
$RoleOids = @("$CmArc.1.1", "$CmArc.1.2", "$CmArc.1.3", "$CmArc.1.4", "$CmArc.1.5")
$ServerAuth = '1.3.6.1.5.5.7.3.1'
$ClientAuth = '1.3.6.1.5.5.7.3.2'

# Values of the plan (ca-plan.md section 4).
$CrlPeriodDays = 7
$CrlOverlapDays = 2
$AuditFilter = 127
$GuestWork = 'C:\cm-ca'

function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Line([string]$Text, [string]$Color = 'Gray') { Write-Host "   $Text" -ForegroundColor $Color }
function Write-Ok([string]$Text) { Write-Line "OK       $Text" 'Green' }
function Write-Missing([string]$Text) { Write-Line "MISSING  $Text" 'Yellow' }
function Write-Note([string]$Text) { Write-Line "NOTE     $Text" 'DarkYellow' }
function Write-Would([string]$Text) { Write-Line "WOULD    $Text" 'Yellow' }
function Write-Did([string]$Text) { Write-Line "DID      $Text" 'Green' }

function Require([string]$Name, $Value) {
    if (-not $Value) { throw "Stage $Stage needs -$Name." }
}

# The AD CS provider and key length for -KeyAlgorithm (ca-plan.md section 5).
function Get-KeySpec {
    if (-not $KeyAlgorithm -or -not $HashAlgorithm) {
        throw "Stage $Stage needs -KeyAlgorithm and -HashAlgorithm: they are decided from the inventory (ca-plan.md section 5), there is no default."
    }
    if ($KeyAlgorithm -eq 'RSA4096') { return [pscustomobject]@{ Provider = 'RSA#Microsoft Software Key Storage Provider'; Length = 4096; Hash = $HashAlgorithm } }
    return [pscustomobject]@{ Provider = 'ECDSA_P384#Microsoft Software Key Storage Provider'; Length = 384; Hash = $HashAlgorithm }
}

function Get-OwnedVm {
    $vm = $null
    if ($ClusterName) {
        $group = Get-ClusterGroup -Cluster $ClusterName -Name $VmName -ErrorAction SilentlyContinue
        if ($group) { $vm = Get-VM -ComputerName $group.OwnerNode.Name -Name $VmName -ErrorAction SilentlyContinue }
    }
    if (-not $vm -and $NodeName) { $vm = Get-VM -ComputerName $NodeName -Name $VmName -ErrorAction SilentlyContinue }
    return $vm
}

function Test-OnOwningNode($vm) {
    return ($vm -and ($vm.ComputerName -ieq $env:COMPUTERNAME))
}

function Get-GuestSession($vm) {
    if (-not (Test-OnOwningNode $vm)) {
        throw "PowerShell Direct works only on the node that owns the VM. Run this stage on $($vm.ComputerName)."
    }
    if (-not $script:GuestCredential) { $script:GuestCredential = Get-Credential -Message "Local administrator of $VmName" }
    return New-PSSession -VMName $VmName -Credential $script:GuestCredential
}

# The CAPolicy.inf of the subordinate request (ca-plan.md section 4). The EKU section limits every chain below the issuing CA.
function Get-CaPolicyInf {
    $lines = @('[Version]', 'Signature="$Windows NT$"', '', '[EnhancedKeyUsageExtension]')
    foreach ($oid in ($RoleOids + @($ServerAuth, $ClientAuth))) { $lines += "OID=$oid" }
    $lines += @('Critical=No', '', '[BasicConstraintsExtension]', 'PathLength=0', 'Critical=Yes', '',
        '[Certsrv_Server]', 'LoadDefaultTemplates=0', 'AlternateSignatureAlgorithm=0',
        'CRLPeriod=Days', "CRLPeriodUnits=$CrlPeriodDays", 'CRLOverlapPeriod=Days', "CRLOverlapUnits=$CrlOverlapDays",
        'CRLDeltaPeriodUnits=0')
    return ($lines -join "`r`n")
}

# ---------------------------------------------------------------------------------------------
function Invoke-Check {
    Write-Step "Check: issuing CA VM '$VmName' (read only)"
    Require 'VmName' $VmName
    $vm = Get-OwnedVm
    if (-not $vm) { Write-Missing "no VM '$VmName' (stage Vm)"; return }
    Write-Ok "VM exists on $($vm.ComputerName), state $($vm.State), generation $($vm.Generation)"
    $security = Get-VMSecurity -VM $vm
    if ($security.TpmEnabled) { Write-Ok 'virtual TPM on' } else { Write-Missing 'virtual TPM off (stage Vm)' }
    $firmware = Get-VMFirmware -VM $vm
    if ("$($firmware.SecureBoot)" -eq 'On') { Write-Ok 'Secure Boot on' } else { Write-Missing 'Secure Boot off (stage Vm)' }
    if (-not $vm.AutomaticCheckpointsEnabled) { Write-Ok 'automatic checkpoints off' } else { Write-Missing 'automatic checkpoints on: a CA must never be rolled back to a checkpoint (stage Vm)' }
    $adapter = @(Get-VMNetworkAdapter -VM $vm)
    if ($adapter.Count -eq 1) {
        $vlan = Get-VMNetworkAdapterVlan -VMNetworkAdapter $adapter[0]
        Write-Ok "one network adapter on '$($adapter[0].SwitchName)', VLAN mode $($vlan.OperationMode) $($vlan.AccessVlanId)"
    } else { Write-Missing "expected one network adapter, found $($adapter.Count)" }
    if ($ClusterName) {
        $group = Get-ClusterGroup -Cluster $ClusterName -Name $VmName -ErrorAction SilentlyContinue
        if ($group) { Write-Ok "clustered role, owner $($group.OwnerNode.Name)" } else { Write-Missing 'not a clustered role (stage Vm)' }
    }

    if (-not (Test-OnOwningNode $vm)) { Write-Note "guest checks need PowerShell Direct: run Check on $($vm.ComputerName) with -GuestCredential"; return }
    if (-not $GuestCredential) { Write-Note 'guest checks skipped: pass -GuestCredential'; return }
    if ("$($vm.State)" -ne 'Running') { Write-Note 'guest checks skipped: the VM is not running'; return }

    Write-Step 'Check: inside the guest'
    $session = Get-GuestSession $vm
    try {
        $facts = Invoke-Command -Session $session -ScriptBlock {
            $feature = Get-WindowsFeature -Name ADCS-Cert-Authority
            $config = $null; $type = $null; $name = $null; $crl = $null; $overlap = $null; $audit = $null; $cdp = $null; $aia = $null; $cert = $null
            $svc = Get-Service -Name certsvc -ErrorAction SilentlyContinue
            $key = 'HKLM:\SYSTEM\CurrentControlSet\Services\CertSvc\Configuration'
            if (Test-Path $key) {
                $config = (Get-ItemProperty $key).Active
                if ($config) {
                    $p = Get-ItemProperty (Join-Path $key $config)
                    $type = $p.CAType; $crl = $p.CRLPeriodUnits; $overlap = $p.CRLOverlapUnits; $audit = $p.AuditFilter
                    $cdp = $p.CRLPublicationURLs; $aia = $p.CACertPublicationURLs; $name = $config
                    $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -like "CN=$config*" } | Select-Object -First 1
                }
            }
            [pscustomobject]@{
                Installed = $feature.Installed; Service = if ($svc) { "$($svc.Status)" } else { '' }; Name = $name; Type = $type
                CrlDays = $crl; OverlapDays = $overlap; Audit = $audit; Cdp = ($cdp -join ' | '); Aia = ($aia -join ' | ')
                CertNotAfter = if ($cert) { $cert.NotAfter.ToString('yyyy-MM-dd') } else { '' }
                Request = Test-Path 'C:\cm-ca\issuing.req'
            }
        }
    } finally { Remove-PSSession $session }
    if ($facts.Installed) { Write-Ok 'AD CS role installed' } else { Write-Missing 'AD CS role not installed (stage Role)'; return }
    if ($facts.Name) { Write-Ok "CA '$($facts.Name)', type $($facts.Type) (4 = standalone subordinate)" } else { Write-Missing 'CA not configured (stage Role)' }
    if ($null -ne $facts.Type -and $facts.Type -ne 4) { Write-Missing "CA type is $($facts.Type), the plan says standalone subordinate (4)" }
    if ($facts.CertNotAfter) { Write-Ok "CA certificate installed, valid to $($facts.CertNotAfter)" }
    elseif ($facts.Request) { Write-Note 'request created, waiting for the root ceremony (stage Root, then Install)' }
    else { Write-Missing 'no CA certificate and no request (stage Role)' }
    if ($facts.Service -eq 'Running') { Write-Ok 'certsvc running' } else { Write-Missing "certsvc is '$($facts.Service)' (stage Install)" }
    if ($facts.CrlDays -eq $CrlPeriodDays -and $facts.OverlapDays -eq $CrlOverlapDays) { Write-Ok "CRL $CrlPeriodDays days, overlap $CrlOverlapDays" } else { Write-Missing "CRL period $($facts.CrlDays)/$($facts.OverlapDays), plan $CrlPeriodDays/$CrlOverlapDays (stage Install)" }
    if ($facts.Audit -eq $AuditFilter) { Write-Ok 'audit filter 127' } else { Write-Missing "audit filter '$($facts.Audit)' (stage Install)" }
    if ($PublicationUrl -and $facts.Cdp -like "*$PublicationUrl*") { Write-Ok 'CDP points at the publication URL' } elseif ($facts.Name) { Write-Note "CDP: $($facts.Cdp)" }
}

# ---------------------------------------------------------------------------------------------
function Invoke-Vm {
    Write-Step "Vm: create '$VmName' on $NodeName (GATED: the owner's words first)"
    foreach ($n in @('ClusterName', 'NodeName', 'VmName', 'VmPath', 'TemplateVhdx', 'SwitchName')) { Require $n (Get-Variable $n -ValueOnly) }
    $vm = Get-OwnedVm
    $vhdx = Join-Path $VmPath "$VmName\$VmName.vhdx"
    if (-not $vm) {
        if (-not (Test-Path $TemplateVhdx)) { throw "template VHDX not found: $TemplateVhdx" }
        if (-not $Apply) {
            Write-Would "copy $TemplateVhdx to $vhdx"
            Write-Would "New-VM -Generation 2 -Name $VmName -ComputerName $NodeName -Path $VmPath -MemoryStartupBytes $($MemoryGB)GB -SwitchName $SwitchName"
        } else {
            New-Item -ItemType Directory -Force -Path (Split-Path $vhdx) | Out-Null
            Copy-Item -Path $TemplateVhdx -Destination $vhdx
            $vm = New-VM -Generation 2 -Name $VmName -ComputerName $NodeName -Path $VmPath -MemoryStartupBytes ($MemoryGB * 1GB) -VHDPath $vhdx -SwitchName $SwitchName
            Write-Did "VM created on $NodeName"
        }
    } else { Write-Ok "VM exists on $($vm.ComputerName)" }

    if (-not $vm) {
        Write-Would "Set-VMProcessor -Count $ProcessorCount; Set-VM -AutomaticCheckpointsEnabled `$false -CheckpointType Production"
        Write-Would 'Set-VMFirmware -EnableSecureBoot On; Set-VMKeyProtector -NewLocalKeyProtector; Enable-VMTPM'
        if ($VlanId -gt 0) { Write-Would "Set-VMNetworkAdapterVlan -Access -VlanId $VlanId" }
        Write-Would "Add-ClusterVirtualMachineRole -Cluster $ClusterName -VMName $VmName; Start-VM"
        return
    }

    if ($vm.ProcessorCount -ne $ProcessorCount) {
        if ($Apply) { Set-VMProcessor -VM $vm -Count $ProcessorCount; Write-Did "processors $ProcessorCount" } else { Write-Would "Set-VMProcessor -Count $ProcessorCount" }
    }
    if ($vm.AutomaticCheckpointsEnabled) {
        if ($Apply) { Set-VM -VM $vm -AutomaticCheckpointsEnabled $false; Write-Did 'automatic checkpoints off' } else { Write-Would 'Set-VM -AutomaticCheckpointsEnabled $false' }
    }
    if ("$((Get-VMFirmware -VM $vm).SecureBoot)" -ne 'On') {
        if ($Apply) { Set-VMFirmware -VM $vm -EnableSecureBoot On; Write-Did 'Secure Boot on' } else { Write-Would 'Set-VMFirmware -EnableSecureBoot On' }
    }
    if (-not (Get-VMSecurity -VM $vm).TpmEnabled) {
        if ($Apply) { Set-VMKeyProtector -VM $vm -NewLocalKeyProtector; Enable-VMTPM -VM $vm; Write-Did 'virtual TPM on' } else { Write-Would 'Set-VMKeyProtector -NewLocalKeyProtector; Enable-VMTPM' }
        Write-Note 'a local key protector moves with failover only when the UntrustedGuardian certificates are on every node (owner step in the how-to)'
    }
    if ($VlanId -gt 0) {
        $vlan = Get-VMNetworkAdapterVlan -VM $vm
        if ($vlan.AccessVlanId -ne $VlanId) {
            if ($Apply) { Set-VMNetworkAdapterVlan -VM $vm -Access -VlanId $VlanId; Write-Did "VLAN $VlanId" } else { Write-Would "Set-VMNetworkAdapterVlan -Access -VlanId $VlanId" }
        }
    }
    if (-not (Get-ClusterGroup -Cluster $ClusterName -Name $VmName -ErrorAction SilentlyContinue)) {
        if ($Apply) { Add-ClusterVirtualMachineRole -Cluster $ClusterName -VMName $VmName | Out-Null; Write-Did 'clustered role' } else { Write-Would "Add-ClusterVirtualMachineRole -Cluster $ClusterName -VMName $VmName" }
    }
    if ("$($vm.State)" -ne 'Running') {
        if ($Apply) { Start-VM -VM $vm; Write-Did 'started' } else { Write-Would 'Start-VM' }
    }
    Write-Note "first boot finishes setup from the template; set the guest's computer name to $VmName and its address in the CMOS segment, then stage Role"
}

# ---------------------------------------------------------------------------------------------
function Invoke-Role {
    Write-Step "Role: AD CS standalone subordinate in '$VmName'"
    Require 'VmName' $VmName; Require 'PublicationUrl' $PublicationUrl
    $vm = Get-OwnedVm
    if (-not $vm) { throw "no VM '$VmName' (stage Vm first)" }
    $inf = Get-CaPolicyInf
    $key = Get-KeySpec
    if (-not $Apply) {
        Write-Would 'Install-WindowsFeature ADCS-Cert-Authority -IncludeManagementTools (in the guest)'
        Write-Would 'write C:\Windows\CAPolicy.inf:'
        $inf.Split("`n") | ForEach-Object { Write-Line "           $($_.TrimEnd())" 'DarkGray' }
        Write-Would "Install-AdcsCertificationAuthority -CAType StandaloneSubordinateCA -CACommonName '$CaCommonName' -KeyLength $($key.Length) -HashAlgorithmName $($key.Hash) -CryptoProviderName '$($key.Provider)' -OutputCertRequestFile $GuestWork\issuing.req"
        Write-Would "copy $GuestWork\issuing.req out to $WorkFolder"
        return
    }
    $session = Get-GuestSession $vm
    try {
        $state = Invoke-Command -Session $session -ArgumentList $inf, $CaCommonName, $GuestWork, $key -ScriptBlock {
            param($inf, $name, $work, $key)
            New-Item -ItemType Directory -Force -Path $work | Out-Null
            if (-not (Get-WindowsFeature ADCS-Cert-Authority).Installed) { Install-WindowsFeature ADCS-Cert-Authority -IncludeManagementTools | Out-Null; $installed = 'installed' } else { $installed = 'present' }   # lint-ok: install the dry run returned above this block; only -Apply gets here
            Set-Content -Path 'C:\Windows\CAPolicy.inf' -Value $inf -Encoding ASCII
            $active = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\CertSvc\Configuration' -ErrorAction SilentlyContinue).Active
            if (-not $active) {
                Install-AdcsCertificationAuthority -CAType StandaloneSubordinateCA -CACommonName $name -KeyLength $key.Length -HashAlgorithmName $key.Hash `
                    -CryptoProviderName $key.Provider -OutputCertRequestFile (Join-Path $work 'issuing.req') -Force | Out-Null
                $configured = 'configured'
            } else { $configured = "already configured as '$active'" }
            [pscustomobject]@{ Role = $installed; Ca = $configured; Request = Test-Path (Join-Path $work 'issuing.req') }
        }
        Write-Did "AD CS role $($state.Role); CA $($state.Ca)"
        if ($state.Request) {
            New-Item -ItemType Directory -Force -Path $WorkFolder | Out-Null
            Copy-Item -FromSession $session -Path "$GuestWork\issuing.req" -Destination (Join-Path $WorkFolder 'issuing.req') -Force
            Write-Did "request copied to $(Join-Path $WorkFolder 'issuing.req'); next: the root ceremony (stage Root)"
        }
    } finally { Remove-PSSession $session }
}

# ---------------------------------------------------------------------------------------------
function Invoke-Root {
    Write-Step 'Root: the ceremony (GATED; printed only, this script never runs it)'
    Write-Note 'On the ceremony host (not a cluster node, not domain-joined), by one admin, the second admin informed. Plan: CM.BlackHole/docs/ca-plan.md sections 4 and 10.'
    $key = Get-KeySpec
    $renewal = if ($KeyAlgorithm -eq 'RSA4096') { 'RenewalKeyLength=4096, ' } else { '' }
    $url = if ($PublicationUrl) { $PublicationUrl } else { '<PublicationUrl>' }
    $steps = @(
        "1. First ceremony: create a Generation 2 VM for the root WITHOUT a network adapter (New-VM without -SwitchName, then Remove-VMNetworkAdapter), NO virtual TPM, Windows Server Core, never domain-joined. Later ceremonies: restore the newest image from the backup server, check its hash against the last ceremony's record, start it, type the BitLocker password at the console, then step 6 (or only certutil -crl for the yearly CRL) and step 10.",
        "2. In it: allow BitLocker without a TPM (policy 'Require additional authentication at startup', allow without a compatible TPM); Enable-BitLocker C: -PasswordProtector, with the password from the cold-start vault; no recovery key file on any disk of the host.",
        "3. CAPolicy.inf with [Certsrv_Server] $($renewal)RenewalValidityPeriod=Years, RenewalValidityPeriodUnits=20, CRLPeriod=Months, CRLPeriodUnits=12, CRLOverlapPeriod=Weeks, CRLOverlapUnits=4, LoadDefaultTemplates=0.",
        "4. Install-WindowsFeature ADCS-Cert-Authority; Install-AdcsCertificationAuthority -CAType StandaloneRootCA -CACommonName 'CM Root CA' -CryptoProviderName '$($key.Provider)' -KeyLength $($key.Length) -HashAlgorithmName $($key.Hash) -ValidityPeriod Years -ValidityPeriodUnits 20.",
        "5. certutil -setreg CA\ValidityPeriodUnits 10; certutil -setreg CA\ValidityPeriod Years; CDP and AIA to $url; restart certsvc; certutil -crl.",
        "6. Copy issuing.req in (Copy-Item -ToSession); certreq -submit issuing.req; certutil -resubmit <RequestId>; certreq -retrieve <RequestId> issuing.crt.",
        "7. certutil -dump issuing.crt: check Basic Constraints 'Path Length Constraint=0' and that Enhanced Key Usage lists exactly the five CMOS role OIDs, Server Authentication and Client Authentication. If not: do not use it, revoke it, fix the request.",
        "8. Copy issuing.crt, the root certificate (.crt) and the root CRL out to $WorkFolder.",
        "9. First ceremony only: certutil -backup <folder> with a password from the vault; put the backup into the cold-start vault.",
        "10. Shut the root VM down; record the VHDX hash; hand the image to the backup job (backup server, replicated to the DR site) and a copy to the cold-start vault; remove the VM and its files from the ceremony host.",
        "11. Then: stage Install."
    )
    foreach ($s in $steps) { Write-Line $s }
}

# ---------------------------------------------------------------------------------------------
function Invoke-Install {
    Write-Step "Install: issued certificate, CRL, audit, publication, firewall in '$VmName'"
    Require 'VmName' $VmName; Require 'PublicationUrl' $PublicationUrl; Require 'RequesterAddress' $RequesterAddress
    $issued = Join-Path $WorkFolder 'issuing.crt'
    $root = Join-Path $WorkFolder 'root.crt'
    foreach ($f in @($issued, $root)) { if (-not (Test-Path $f)) { Write-Missing "$f (from the root ceremony)" } }
    $crlUrls = "1:C:\Windows\system32\CertSrv\CertEnroll\%3%8%9.crl\n2:$PublicationUrl/%3%8%9.crl"
    $aiaUrls = "1:C:\Windows\system32\CertSrv\CertEnroll\%1_%3%4.crt\n2:$PublicationUrl/%1_%3%4.crt"
    if (-not $Apply) {
        Write-Would "copy root.crt and issuing.crt into $GuestWork; certutil -addstore Root root.crt; certutil -installcert issuing.crt"
        Write-Would "certutil -setreg CA\CRLPeriodUnits $CrlPeriodDays; CA\CRLOverlapUnits $CrlOverlapDays; CA\CRLDeltaPeriodUnits 0; CA\AuditFilter $AuditFilter"
        Write-Would "certutil -setreg CA\CRLPublicationURLs `"$crlUrls`""
        Write-Would "certutil -setreg CA\CACertPublicationURLs `"$aiaUrls`""
        Write-Would 'auditpol /set /subcategory:"Certification Services" /success:enable /failure:enable'
        Write-Would "firewall: disable the Certification Authority rule group (RPC/DCOM enrolment); allow TCP 5986 inbound from $RequesterAddress only"
        Write-Would 'Start-Service certsvc; certutil -crl; copy the CA certificate and CRL out for Milky Way'
        Write-Note 'then back up the issuing CA key: certutil -backupkey <folder> with a password, into the cold-start vault'
        return
    }
    foreach ($f in @($issued, $root)) { if (-not (Test-Path $f)) { throw "missing $f" } }
    $vm = Get-OwnedVm
    if (-not $vm) { throw "no VM '$VmName'" }
    $session = Get-GuestSession $vm
    try {
        Copy-Item -ToSession $session -Path $issued -Destination "$GuestWork\issuing.crt" -Force
        Copy-Item -ToSession $session -Path $root -Destination "$GuestWork\root.crt" -Force
        $out = Invoke-Command -Session $session -ArgumentList $GuestWork, $CrlPeriodDays, $CrlOverlapDays, $AuditFilter, $crlUrls, $aiaUrls, $RequesterAddress -ScriptBlock {
            param($work, $crlDays, $overlapDays, $audit, $crlUrls, $aiaUrls, $requester)
            $done = @()
            & certutil.exe -addstore Root (Join-Path $work 'root.crt') | Out-Null; $done += 'root in the CA host''s own Root store'
            # A subordinate CA's service cannot run before its certificate is installed, so "running" means installed.
            if ("$((Get-Service certsvc).Status)" -ne 'Running') { & certutil.exe -installcert (Join-Path $work 'issuing.crt') | Out-Null; $done += 'CA certificate installed' }
            else { $done += 'CA certificate already installed' }
            & certutil.exe -setreg CA\CRLPeriod Days | Out-Null
            & certutil.exe -setreg CA\CRLPeriodUnits $crlDays | Out-Null
            & certutil.exe -setreg CA\CRLOverlapPeriod Days | Out-Null
            & certutil.exe -setreg CA\CRLOverlapUnits $overlapDays | Out-Null
            & certutil.exe -setreg CA\CRLDeltaPeriodUnits 0 | Out-Null
            & certutil.exe -setreg CA\AuditFilter $audit | Out-Null
            & certutil.exe -setreg CA\CRLPublicationURLs $crlUrls | Out-Null
            & certutil.exe -setreg CA\CACertPublicationURLs $aiaUrls | Out-Null
            & auditpol.exe /set /subcategory:"Certification Services" /success:enable /failure:enable | Out-Null
            $done += 'CRL periods, audit, CDP and AIA set'
            Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayGroup -like '*Certification Authority*' -and $_.Enabled -eq 'True' } | Disable-NetFirewallRule
            if (-not (Get-NetFirewallRule -Name 'CM-WinRM-HTTPS-Requester' -ErrorAction SilentlyContinue)) {
                New-NetFirewallRule -Name 'CM-WinRM-HTTPS-Requester' -DisplayName 'CM: WinRM over HTTPS from the requester only' -Direction Inbound -Protocol TCP -LocalPort 5986 -RemoteAddress $requester -Action Allow | Out-Null
            } else { Set-NetFirewallRule -Name 'CM-WinRM-HTTPS-Requester' -RemoteAddress $requester }
            $done += 'firewall: CA enrolment rules off, 5986 from the requester only'
            Restart-Service certsvc
            & certutil.exe -crl | Out-Null
            $done += 'certsvc restarted, CRL published'
            $done
        }
        $out | ForEach-Object { Write-Did $_ }
        New-Item -ItemType Directory -Force -Path $WorkFolder | Out-Null
        Copy-Item -FromSession $session -Path 'C:\Windows\system32\CertSrv\CertEnroll\*' -Destination $WorkFolder -Force
        Write-Did "CA certificate and CRL copied to $WorkFolder for Milky Way's publication URL"
        Write-Note 'now back up the issuing CA key: certutil -backupkey <folder> with a password, into the cold-start vault; then Check'
    } finally { Remove-PSSession $session }
}

switch ($Stage) {
    'Check' { Invoke-Check }
    'Vm' { Invoke-Vm }
    'Role' { Invoke-Role }
    'Root' { Invoke-Root }
    'Install' { Invoke-Install }
}
if (-not $Apply -and $Stage -ne 'Check' -and $Stage -ne 'Root') { Write-Host ''; Write-Host 'Dry run: nothing changed. Add -Apply to act.' -ForegroundColor Yellow }
