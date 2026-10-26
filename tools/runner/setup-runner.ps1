<#
.SYNOPSIS
Sets up one or more GitHub Actions self-hosted runners on a Windows machine, as services under a local user, and takes them away again. Dry run until -Apply.

.DESCRIPTION
Written to be run on a personal or lab machine that is not joined to a domain (nothing in it is specific to one organisation, repository or host: every
name, folder and label is a parameter). Run it in an ELEVATED Windows PowerShell 5.1, from a clean checkout of this repository on main.
The guard (tools/lib/Guard.psm1) stops the script at once when the computer is not -ExpectedHost, when the session is not elevated, when it is not Windows
PowerShell 5.1, or when the checkout is not clean and on main (an apply only). -AllowedUser narrows the account further.

Every stage is a DRY RUN until you add -Apply: it reads the live state, prints WOULD lines for what it would change, and changes nothing. Every change is
a guarded write: the live state is read first, the write runs only when that state is the one it may overwrite, and the state is read back afterwards.
Run it twice and the second run says "already" everywhere. Nothing is written to a file that holds a secret: the registration token, the removal token and
the password of the runner account live in memory and on the screen once.

Stages:

  Check   Default. Reads only. The runner user, the runner folders, the services, the tools (git, gh, dotnet) on the machine PATH and what GitHub lists.
  Runner  Creates the local user (generated password, member of the built-in Users group, 'Log on as a service'), downloads the runner release and checks
          its SHA-256 against the release data, unpacks it, writes the job environment file (.env), registers the runner as a service under that user.
          -RunnerCount N makes runners 1 to N, all with the same labels, so a job takes whichever is free. Runner k > 1 is named <RunnerName>-k and uses
          <RunnerRoot>-k and <RunnerWork>-k. -RunnerFrom K limits the stage to the runners from K on; the others are only looked at, never restarted.
  Undo    Stops the runner services RunnerFrom to RunnerCount, removes the runners from GitHub (a removal token from gh), and deletes the local user when no
          other runner service uses it. The folders stay (delete them by hand when you are sure).

How many runners: **the CPU limits the count**, not RAM or disk. A runner runs one job at a time and a build or test job keeps every core busy, so more
runners than cores' worth of parallel jobs only make every job slower. Rule of thumb: one runner per four to six logical cores for a build-and-test
workflow, and fewer when the machine is also somebody's laptop. The count never exceeds eight in this script.

.PARAMETER ExpectedHost
The computer the command block says it runs on. The guard refuses any other. Mandatory, so the hand-off line "host . context . account" and the script assert the same thing.

.PARAMETER Stage
Check (default), Runner or Undo.

.PARAMETER Apply
Change something for real. Without it every stage is read only.

.PARAMETER Repository
The repository the runners serve, as owner/name. Mandatory. gh must be signed in with the right to create a registration token for it (admin on the repository).

.PARAMETER AllowedUser
Optional: a -like pattern the signed-in account must match, for example 'MYLAPTOP\myname'. Empty = any account that is an administrator in an elevated session.

.PARAMETER RunnerUser
The local user the runner services run as. Default ci-runner.

.PARAMETER RunnerName
The name of runner 1 on GitHub; runner k is <RunnerName>-k. Default runner.

.PARAMETER RunnerLabels
The labels of every runner, comma separated. Default cm-runner. A workflow selects them with runs-on: [self-hosted, cm-runner].

.PARAMETER RunnerRoot
The install folder of runner 1; runner k is <RunnerRoot>-k. Default <system drive>\actions-runner.

.PARAMETER RunnerWork
The work folder of runner 1; runner k is <RunnerWork>-k. Default <system drive>\ci-work. Put it on the largest drive.

.PARAMETER RunnerCount
How many runners, 1 to 8 (see "How many runners" above). Default 1.

.PARAMETER RunnerFrom
Start at this runner number (default 1). Undo of only the second runner: -Stage Undo -RunnerFrom 2 -RunnerCount 2.

.PARAMETER RunnerVersion
A specific runner release, for example 2.320.0. Default: the latest release.

.PARAMETER ExtraEnv
Further NAME=value lines for the job environment file (.env) of every runner. Never a secret.

.EXAMPLE
& 'C:\path\to\checkout\tools\runner\setup-runner.ps1' -ExpectedHost 'MYLAPTOP' -Repository 'owner/repo'                       # Check: read only

.EXAMPLE
& 'C:\path\to\checkout\tools\runner\setup-runner.ps1' -ExpectedHost 'MYLAPTOP' -Repository 'owner/repo' -Stage Runner -RunnerCount 2          # dry run

.EXAMPLE
& 'C:\path\to\checkout\tools\runner\setup-runner.ps1' -ExpectedHost 'MYLAPTOP' -Repository 'owner/repo' -Stage Runner -RunnerCount 2 -Apply
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExpectedHost,
    [ValidateSet('Check', 'Runner', 'Undo')][string]$Stage = 'Check',
    [switch]$Apply,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$')][string]$Repository,
    [string]$AllowedUser = '',
    [ValidatePattern('^[A-Za-z0-9_-]{1,20}$')][string]$RunnerUser = 'ci-runner',
    [ValidatePattern('^[A-Za-z0-9._-]+$')][string]$RunnerName = 'runner',
    [string]$RunnerLabels = 'cm-runner',
    [string]$RunnerRoot = (Join-Path $env:SystemDrive 'actions-runner'),
    [string]$RunnerWork = (Join-Path $env:SystemDrive 'ci-work'),
    [ValidateRange(1, 8)][int]$RunnerCount = 1,
    [ValidateRange(1, 8)][int]$RunnerFrom = 1,
    [string]$RunnerVersion = '',
    [string[]]$ExtraEnv = @()
)
$ErrorActionPreference = 'Stop'
# The guard first (docs/process/owner-scripts.md): wrong machine, session or checkout stops the script here, before anything else runs.
Import-Module (Join-Path $PSScriptRoot '..\lib\Guard.psm1') -Force -ErrorAction Stop
$null = Assert-Script -ScriptPath $PSCommandPath -Kind Changes -Applying:$Apply -ExpectedHost $ExpectedHost -Elevation Required
if ($AllowedUser) {
    Assert-State -What 'the signed-in account' -Read { [Security.Principal.WindowsIdentity]::GetCurrent().Name } -Expect { param($n) $n -like $AllowedUser }
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'

$problems = New-Object System.Collections.Generic.List[string]
function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "   OK       $Text" -ForegroundColor Green }
function Write-Missing([string]$Text) { Write-Host "   MISSING  $Text" -ForegroundColor Yellow; $script:problems.Add($Text) }
function Write-Note([string]$Text) { Write-Host "   NOTE     $Text" }

$sidUsers = 'S-1-5-32-545'                        # the built-in Users group by well-known SID, never by name: German Windows calls it "Benutzer"
$localAccount = "$env:COMPUTERNAME\$RunnerUser"
$repositoryKey = $Repository.Replace('/', '-')

function Get-NativeTry([string]$File, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # Windows PowerShell 5.1 turns a native program's stderr into a terminating error under 'Stop'
    try { $output = & $File @Arguments 2>&1 } finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{ Code = $LASTEXITCODE; Output = $output }
}

function Get-Native([string]$File, [string[]]$Arguments) {
    $r = Get-NativeTry $File $Arguments
    if ($r.Code -ne 0) { throw "$File $($Arguments -join ' ') failed with exit code $($r.Code)`n$($r.Output | Out-String)" }
    return $r.Output
}

function Get-LocalUserOrNull([string]$Name) {
    try { return Get-LocalUser -Name $Name -ErrorAction Stop } catch { return $null }
}

function Get-AccountSid([string]$Who) {
    return (New-Object Security.Principal.NTAccount($Who)).Translate([Security.Principal.SecurityIdentifier])
}

function Get-RandomPassword {
    $alphabet = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789-_'
    $bytes = New-Object byte[] 24
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $text = -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
    return $text + 'aZ7!'   # one of each class, whatever the random part came out as
}

function Test-CanModify([string]$Path, [string]$Who) {
    # True when the account has a Modify-or-better allow right on the folder (an ACE for the account itself; group rights are not followed).
    if (-not (Test-Path $Path)) { return $false }
    $sid = (Get-AccountSid $Who).Value
    foreach ($rule in (Get-Acl $Path).Access) {
        if ($rule.AccessControlType -ne 'Allow') { continue }
        $ruleSid = try { $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { $null }
        if ($ruleSid -eq $sid -and (($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Modify) -eq [Security.AccessControl.FileSystemRights]::Modify)) { return $true }
    }
    return $false
}

function Initialize-LsaType {
    if ('Cm.Lsa' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
namespace Cm {
  public static class Lsa {
    [StructLayout(LayoutKind.Sequential)] struct LSA_OBJECT_ATTRIBUTES { public int Length; public IntPtr RootDirectory; public IntPtr ObjectName; public int Attributes; public IntPtr SecurityDescriptor; public IntPtr SecurityQualityOfService; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct LSA_UNICODE_STRING { public ushort Length; public ushort MaximumLength; public string Buffer; }
    [DllImport("advapi32.dll", PreserveSig = true)] static extern uint LsaOpenPolicy(IntPtr systemName, ref LSA_OBJECT_ATTRIBUTES attributes, int access, out IntPtr policy);
    [DllImport("advapi32.dll", PreserveSig = true)] static extern uint LsaAddAccountRights(IntPtr policy, byte[] sid, LSA_UNICODE_STRING[] rights, int count);
    [DllImport("advapi32.dll", PreserveSig = true)] static extern uint LsaEnumerateAccountRights(IntPtr policy, byte[] sid, out IntPtr rights, out int count);
    [DllImport("advapi32.dll")] static extern int LsaClose(IntPtr policy);
    [DllImport("advapi32.dll")] static extern int LsaFreeMemory(IntPtr buffer);
    static IntPtr Open() {
      var attributes = new LSA_OBJECT_ATTRIBUTES(); IntPtr policy;
      uint status = LsaOpenPolicy(IntPtr.Zero, ref attributes, 0x00000800 | 0x00000010 | 0x00000001, out policy); // LOOKUP_NAMES | CREATE_ACCOUNT | VIEW_LOCAL_INFORMATION
      if (status != 0) throw new InvalidOperationException("LsaOpenPolicy failed: 0x" + status.ToString("x"));
      return policy;
    }
    public static bool Has(string sidText, string right) {
      var sid = new byte[68]; new SecurityIdentifier(sidText).GetBinaryForm(sid, 0);
      var policy = Open();
      try {
        IntPtr buffer; int count; uint status = LsaEnumerateAccountRights(policy, sid, out buffer, out count);
        if (status == 0xC0000034) return false; // STATUS_OBJECT_NAME_NOT_FOUND: the account has no rights yet
        if (status != 0) throw new InvalidOperationException("LsaEnumerateAccountRights failed: 0x" + status.ToString("x"));
        try {
          for (int i = 0; i < count; i++) {
            var item = (LSA_UNICODE_STRING)Marshal.PtrToStructure(IntPtr.Add(buffer, i * Marshal.SizeOf(typeof(LSA_UNICODE_STRING))), typeof(LSA_UNICODE_STRING));
            if (string.Equals(item.Buffer, right, StringComparison.OrdinalIgnoreCase)) return true;
          }
          return false;
        } finally { LsaFreeMemory(buffer); }
      } finally { LsaClose(policy); }
    }
    public static void Add(string sidText, string right) {
      var sid = new byte[68]; new SecurityIdentifier(sidText).GetBinaryForm(sid, 0);
      var policy = Open();
      try {
        var rights = new LSA_UNICODE_STRING[1];
        rights[0].Buffer = right; rights[0].Length = (ushort)(right.Length * 2); rights[0].MaximumLength = (ushort)(right.Length * 2 + 2);
        uint status = LsaAddAccountRights(policy, sid, rights, 1);
        if (status != 0) throw new InvalidOperationException("LsaAddAccountRights failed: 0x" + status.ToString("x"));
      } finally { LsaClose(policy); }
    }
  }
}
'@
}

# ===================================================================== the runners
# -RunnerCount N means runners 1..N under the one local user, all with the same labels, so a job takes whichever is free. Runner 1 keeps the plain names;
# runner k > 1 is <RunnerName>-k, <RunnerRoot>-k and <RunnerWork>-k. -RunnerFrom limits a stage to the runners from that number on.
function Get-RunnerDefinitions {
    $definitions = @()
    foreach ($k in $RunnerFrom..$RunnerCount) {
        if ($k -eq 1) { $definitions += [pscustomobject]@{ Index = 1; Name = $RunnerName; Root = $RunnerRoot; Work = $RunnerWork } }
        else { $definitions += [pscustomobject]@{ Index = $k; Name = "$RunnerName-$k"; Root = "$RunnerRoot-$k"; Work = "$RunnerWork-$k" } }
    }
    return $definitions
}

function Get-RunnerService($Definition) {
    return Get-CimInstance Win32_Service -Filter "Name LIKE 'actions.runner.%'" -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq "actions.runner.$repositoryKey.$($Definition.Name)" } | Select-Object -First 1
}

function Get-AllRunnerServices {
    return @(Get-CimInstance Win32_Service -Filter "Name LIKE 'actions.runner.%'" -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "actions.runner.$repositoryKey.*" })
}

# A job is running when the runner's worker process (Runner.Worker.exe) lives in this runner's folder. A restart would cancel it.
function Test-RunnerBusy($Definition) {
    return [bool](Get-Process -Name 'Runner.Worker' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -like "$($Definition.Root)\*" })
}

function Get-MachinePathEntries {
    $path = [string][Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager\Environment').GetValue('Path', '', 'DoNotExpandEnvironmentNames')
    return @($path -split ';' | Where-Object { $_ } | ForEach-Object { [Environment]::ExpandEnvironmentVariables($_).TrimEnd([char]92) })
}

function Test-ToolOnMachinePath([string]$Exe) {
    return [bool](Get-MachinePathEntries | Where-Object { Test-Path (Join-Path $_ "$Exe.exe") })
}

function Get-LatestRunner {
    $headers = @{ 'User-Agent' = 'cm-setup-runner' }
    $release = if ($RunnerVersion) { Invoke-RestMethod "https://api.github.com/repos/actions/runner/releases/tags/v$RunnerVersion" -Headers $headers }
               else { Invoke-RestMethod 'https://api.github.com/repos/actions/runner/releases/latest' -Headers $headers }
    $version = $release.tag_name.TrimStart('v')
    $assetName = "actions-runner-win-x64-$version.zip"
    $asset = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    if (-not $asset) { throw "Release $version of actions/runner has no asset $assetName." }
    $sha = $null
    if ($asset.PSObject.Properties.Name -contains 'digest' -and $asset.digest -like 'sha256:*') { $sha = $asset.digest.Substring(7).ToLowerInvariant() }
    elseif ($release.body -match '<!-- BEGIN SHA win-x64 -->([0-9a-fA-F]{64})<!-- END SHA win-x64 -->') { $sha = $Matches[1].ToLowerInvariant() }
    if (-not $sha) { throw "No SHA-256 for $assetName in the release data; refusing to install an unverified download." }
    return [pscustomobject]@{ Version = $version; Url = $asset.browser_download_url; Sha256 = $sha; Name = $assetName }
}

# The registration token comes from gh (kept in memory only); when gh cannot make one the person pastes it. Used only with -Apply.
function Get-RegistrationToken {
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        $r = Get-NativeTry 'gh' @('api', '-X', 'POST', "repos/$Repository/actions/runners/registration-token", '--jq', '.token')
        if ($r.Code -eq 0 -and $r.Output) { return ([string]($r.Output | Select-Object -First 1)).Trim() }
        Write-Note "gh could not create a registration token ($(($r.Output | Out-String).Trim()))."
    }
    $secure = Read-Host "Paste the registration token (gh api -X POST repos/$Repository/actions/runners/registration-token --jq .token)" -AsSecureString
    return [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

# The password of the runner user for a further runner service. A new user: the one just generated. An existing user: asked for (from the password manager)
# and checked against the machine. Used only with -Apply.
function Get-RunnerPassword {
    if ($script:runnerPassword) { return $script:runnerPassword }
    $secure = Read-Host "Password of the local user $RunnerUser (from your password manager)" -AsSecureString
    $typed = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    Add-Type -AssemblyName System.DirectoryServices.AccountManagement
    $context = New-Object System.DirectoryServices.AccountManagement.PrincipalContext([System.DirectoryServices.AccountManagement.ContextType]::Machine)
    if (-not $typed -or -not $context.ValidateCredentials($RunnerUser, $typed)) { throw "The machine does not accept that password for $RunnerUser. Nothing was changed; run the stage again." }
    $script:runnerPassword = $typed
    return $typed
}

function Test-Runner($Definition) {
    $user = Get-LocalUserOrNull $RunnerUser
    if ($user) { Write-Ok "Local user $RunnerUser exists (enabled: $($user.Enabled))" } else { Write-Missing "Local user $RunnerUser does not exist (the Runner stage creates it)." }
    if (Test-Path (Join-Path $Definition.Root 'config.cmd')) { Write-Ok "Runner files in $($Definition.Root)" } else { Write-Missing "No runner in $($Definition.Root) (the Runner stage downloads and unpacks it)." }
    $service = Get-RunnerService $Definition
    if ($service) {
        if ($service.State -eq 'Running') { Write-Ok "Runner service $($service.Name) runs as $($service.StartName)" } else { Write-Missing "Runner service $($service.Name) is $($service.State)." }
        if ($user -and $service.StartName -notlike "*\$RunnerUser") { Write-Missing "Runner service runs as $($service.StartName), not $RunnerUser." }
    } else { Write-Missing "No runner service for $($Definition.Name) (the Runner stage registers it)." }
    if (Test-Path $Definition.Work) { Write-Ok "Work folder $($Definition.Work)" } else { Write-Missing "Work folder $($Definition.Work) does not exist." }
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        $r = Get-NativeTry 'gh' @('api', "repos/$Repository/actions/runners")   # no --jq: Windows PowerShell 5.1 drops the double quotes of a native argument
        if ($r.Code -eq 0) {
            $runners = @(($r.Output | Out-String | ConvertFrom-Json).runners)
            $line = ($runners | ForEach-Object { "$($_.name) $($_.status)" }) -join '; '
            $mine = $runners | Where-Object { $_.name -eq $Definition.Name } | Select-Object -First 1
            if ($mine -and $mine.status -eq 'online') { Write-Ok "GitHub sees the runner $($Definition.Name) online (all: $line)" }
            elseif ($line) { Write-Missing "GitHub lists: $line (expected '$($Definition.Name) online')." }
            else { Write-Missing "GitHub lists no runner for $Repository." }
        } else { Write-Note 'Could not ask GitHub for the runner list (gh not signed in, or no admin access to the repository).' }
    }
}

function Test-Tools {
    # The workflows call gh, git and dotnet as the runner user, whose PATH is the machine PATH: a per-user install is invisible to it.
    foreach ($tool in 'gh', 'git', 'dotnet') {
        if (Test-ToolOnMachinePath $tool) { Write-Ok "$tool is on the machine PATH" }
        else { Write-Missing "$tool.exe is on no folder of the machine PATH, so $RunnerUser cannot call it." }
    }
}

function Invoke-OneRunner($Definition, $Runner) {
    Write-Step "Runner $($Definition.Index) ($($Definition.Name)): $Repository as a service under $RunnerUser, install $($Definition.Root), work $($Definition.Work), labels $RunnerLabels"
    $service = Get-RunnerService $Definition
    $user = Get-LocalUserOrNull $RunnerUser

    $null = Invoke-GuardedWrite -What "Create the local user $RunnerUser (generated password, never expires, member of Users only)" `
        -Read { Get-LocalUserOrNull $RunnerUser } `
        -Expect { param($s) $true } `
        -Done { param($s) [bool]$s } `
        -DependentSteps @('every later step of this runner') `
        -Apply:$Apply `
        -Write {
            $script:runnerPassword = Get-RandomPassword
            New-LocalUser -Name $RunnerUser -Password (ConvertTo-SecureString $script:runnerPassword -AsPlainText -Force) -PasswordNeverExpires -UserMayNotChangePassword -AccountNeverExpires -Description 'CI: GitHub Actions runner service' | Out-Null
            # Shown at once, before any later step can fail: the password exists nowhere else.
            Write-Host "   PASSWORD of ${RunnerUser}: $($script:runnerPassword)" -ForegroundColor Magenta
            Write-Host '   Store it in your password manager NOW. It is shown once and written nowhere.' -ForegroundColor Magenta
        }

    $usersGroupSid = $sidUsers
    $null = Invoke-GuardedWrite -What "Make $RunnerUser a member of the built-in Users group ($usersGroupSid)" `
        -Read { @(Get-LocalGroupMember -SID $usersGroupSid -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*\$RunnerUser" }).Count } `
        -Expect { param($s) $true } `
        -Done { param($s) $s -ge 1 } `
        -DependentSteps @() `
        -Apply:$Apply `
        -Write { Add-LocalGroupMember -SID $usersGroupSid -Member $RunnerUser }

    Initialize-LsaType
    $null = Invoke-GuardedWrite -What "Grant 'Log on as a service' to $RunnerUser" `
        -Read { if (Get-LocalUserOrNull $RunnerUser) { [Cm.Lsa]::Has((Get-AccountSid $localAccount).Value, 'SeServiceLogonRight') } else { $false } } `
        -Expect { param($s) $true } `
        -Done { param($s) $s -eq $true } `
        -DependentSteps @('the runner service starts') `
        -Apply:$Apply `
        -Write { [Cm.Lsa]::Add((Get-AccountSid $localAccount).Value, 'SeServiceLogonRight') }

    # gh and git: machine-wide, because the runner user's PATH is the machine PATH. A per-user install is invisible to it.
    foreach ($tool in @(@{ Exe = 'gh'; Id = 'GitHub.cli' }, @{ Exe = 'git'; Id = 'Git.Git' })) {
        $toolExe = $tool.Exe
        $toolId = $tool.Id
        $before = Get-ToolVersion -Name $toolExe
        if ($Apply) {
            $null = Invoke-GuardedWrite -What "winget install --id $toolId --scope machine" `
                -Read { Test-ToolOnMachinePath $toolExe } `
                -Expect { param($s) $true } `
                -Done { param($s) $s -eq $true } `
                -DependentSteps @('the workflows call the tool as the runner user') `
                -Apply:$Apply `
                -Write { & winget install --id $toolId --scope machine --silent --accept-package-agreements --accept-source-agreements --force }
            # An installer that fails can roll back and remove an older install too: prove the tool runs now.
            $null = Assert-ToolWorks -Name $toolExe -Before $before
        } elseif (-not (Test-ToolOnMachinePath $toolExe)) { Write-Host "WOULD winget install --id $toolId --scope machine (the workflows call $toolExe as $RunnerUser)" -ForegroundColor Cyan }
        else { Write-Ok "$toolExe is on the machine PATH (already)" }
    }

    foreach ($folder in @($Definition.Work, $Definition.Root)) {
        $null = Invoke-GuardedWrite -What "Create $folder and give $RunnerUser the Modify right on it" `
            -Read { [pscustomobject]@{ Exists = (Test-Path $folder); Modify = (Test-CanModify $folder $localAccount) } } `
            -Expect { param($s) $true } `
            -Done { param($s) $s.Exists -and $s.Modify } `
            -DependentSteps @('the runner writes its logs and jobs there') `
            -Apply:$Apply `
            -Write {
                New-Item -ItemType Directory -Force -Path $folder | Out-Null
                Get-Native 'icacls.exe' @($folder, '/grant', "${RunnerUser}:(OI)(CI)M") | Out-Null
            }
    }

    $rootForUnpack = $Definition.Root
    $null = Invoke-GuardedWrite -What "Download $(if ($Runner) { $Runner.Name } else { 'the runner' }), check its SHA-256 against the release data, unpack it to $rootForUnpack" `
        -Read { Test-Path (Join-Path $rootForUnpack 'config.cmd') } `
        -Expect { param($s) $true } `
        -Done { param($s) $s -eq $true } `
        -DependentSteps @('registering the runner') `
        -Apply:$Apply `
        -Write {
            $zip = Join-Path $env:TEMP $Runner.Name
            Invoke-WebRequest -Uri $Runner.Url -OutFile $zip -UseBasicParsing
            $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne $Runner.Sha256) { Remove-Item $zip -Force; throw "SHA-256 of the download is $actual, expected $($Runner.Sha256). Nothing was unpacked." }
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $rootForUnpack)
            Remove-Item $zip -Force
        }

    # The runner reads .env into the environment of every job. Not the machine environment: other accounts on this machine stay untouched.
    $envFile = Join-Path $Definition.Root '.env'
    $envText = (@('DOTNET_CLI_TELEMETRY_OPTOUT=1', 'DOTNET_NOLOGO=1') + $ExtraEnv) -join "`r`n"
    $null = Invoke-GuardedWrite -What "Write the job environment to $envFile" `
        -Read { if (Test-Path $envFile) { (Get-Content $envFile -Raw).Trim() } else { '' } } `
        -Expect { param($s) $true } `
        -Done { param($s) $s -eq $envText } `
        -DependentSteps @() `
        -Apply:$Apply `
        -Write { [System.IO.File]::WriteAllText($envFile, $envText + "`r`n", (New-Object System.Text.UTF8Encoding($false))) }

    $serviceName = "actions.runner.$repositoryKey.$($Definition.Name)"
    if ($service) { Write-Ok "Runner service $serviceName exists (already). To register again: Undo, then Runner." }
    elseif (-not $Apply) {
        Write-Host "WOULD ask for the password of $RunnerUser (only when the user already existed), get a registration token (gh api -X POST repos/$Repository/actions/runners/registration-token) and run: config.cmd --unattended --url https://github.com/$Repository --name $($Definition.Name) --labels $RunnerLabels --work $($Definition.Work) --runasservice --windowslogonaccount $localAccount --replace" -ForegroundColor Cyan
    } else {
        $password = Get-RunnerPassword
        $token = Get-RegistrationToken
        $null = Invoke-GuardedWrite -What "Register the runner $($Definition.Name) and install it as the service $serviceName" `
            -Read { Get-RunnerService $Definition } `
            -Expect { param($s) -not $s } `
            -Done { param($s) [bool]$s -and $s.StartName -like "*\$RunnerUser" } `
            -DependentSteps @() `
            -Apply:$Apply `
            -Write {
                Push-Location $Definition.Root
                try {
                    Get-Native (Join-Path $Definition.Root 'config.cmd') @('--unattended', '--url', "https://github.com/$Repository", '--token', $token, '--name', $Definition.Name, '--labels', $RunnerLabels, '--work', $Definition.Work, '--runasservice', '--windowslogonaccount', $localAccount, '--windowslogonpassword', $password, '--replace') | Out-Null
                } finally { Pop-Location }
            }
        $token = $null
        $password = $null
    }

    Write-Step "End check (runner $($Definition.Index))"
    if (-not $Apply) { Write-Note 'Dry run: the end check shows the state now, not the result of the plan.' }
    Test-Runner $Definition
}

function Invoke-Runner {
    $runner = $null
    try { $runner = Get-LatestRunner } catch {
        if ($Apply) { throw }
        Write-Note "Could not look up the runner release on GitHub ($($_.Exception.Message)); the dry run goes on without it."
    }
    if ($runner) {
        Write-Host "   Runner version $($runner.Version); $($runner.Url)"
        Write-Host "   SHA-256 from GitHub's release data: $($runner.Sha256)"
    }
    foreach ($definition in (Get-RunnerDefinitions)) { Invoke-OneRunner $definition $runner }
    Write-Step 'End check (tools)'
    Test-Tools
    if ($RunnerCount -gt 1) { Write-Note "$RunnerCount runners share the labels ${RunnerLabels}: a job takes whichever is free. Each has its own install folder and work folder. The CPU limits the count (see the help)." }
}

function Invoke-Undo {
    # Runners RunnerFrom..RunnerCount. The user goes only when no other runner service uses it.
    $removed = @()
    foreach ($definition in (Get-RunnerDefinitions)) {
        $service = Get-RunnerService $definition
        if (-not $service) { Write-Ok "There is no runner service for $($definition.Name)."; continue }
        if (Test-RunnerBusy $definition) { throw "A job runs on $($definition.Name). Undo when it is idle." }
        $null = Invoke-GuardedWrite -What "Stop the runner service $($service.Name) and remove the runner from GitHub (config.cmd remove with a removal token)" `
            -Read { Get-RunnerService $definition } `
            -Expect { param($s) [bool]$s } `
            -Done { param($s) -not $s } `
            -DependentSteps @() `
            -Apply:$Apply `
            -Write {
                Stop-Service $service.Name -Force -ErrorAction SilentlyContinue
                $r = Get-NativeTry 'gh' @('api', '-X', 'POST', "repos/$Repository/actions/runners/remove-token", '--jq', '.token')
                if ($r.Code -ne 0) { throw 'gh could not create a removal token. Remove the runner on the repository settings page, then delete the service with: sc.exe delete ' + $service.Name }
                Push-Location $definition.Root
                try { Get-Native (Join-Path $definition.Root 'config.cmd') @('remove', '--token', ([string]($r.Output | Select-Object -First 1)).Trim()) | Out-Null } finally { Pop-Location }
            }
        $removed += $service.Name
        Write-Note "$($definition.Root) and $($definition.Work) stay (delete them by hand when you are sure)."
    }
    $remaining = @(Get-AllRunnerServices | Where-Object { $removed -notcontains $_.Name -and $_.StartName -like "*\$RunnerUser" })
    if ($remaining.Count -gt 0) { Write-Note "The local user $RunnerUser stays: $($remaining.Count) other runner service(s) use it." }
    else {
        $null = Invoke-GuardedWrite -What "Delete the local user $RunnerUser" `
            -Read { Get-LocalUserOrNull $RunnerUser } `
            -Expect { param($s) $true } `
            -Done { param($s) -not $s } `
            -DependentSteps @() `
            -Apply:$Apply `
            -Write { Remove-LocalUser -Name $RunnerUser }
    }
}

# ===================================================================== main
Write-Host "setup-runner.ps1  stage $Stage  repository $Repository  $(if ($Apply) { 'APPLY' } else { 'dry run (add -Apply to change something)' })" -ForegroundColor White
switch ($Stage) {
    'Check' {
        Write-Step "Check: runners $RunnerFrom to $RunnerCount for $Repository"
        foreach ($definition in (Get-RunnerDefinitions)) { Test-Runner $definition }
        Test-Tools
    }
    'Runner' { Invoke-Runner }
    'Undo' { Invoke-Undo }
}

Write-Host ''
if ($problems.Count -gt 0) {
    Write-Host "Open points ($($problems.Count)):" -ForegroundColor Yellow
    $problems | ForEach-Object { Write-Host "  - $_" }
} else { Write-Host 'No open points.' -ForegroundColor Green }
if (-not $Apply -and $Stage -ne 'Check') { Write-Host 'Dry run: nothing was changed. Add -Apply to do it.' -ForegroundColor DarkYellow }
exit (Write-RunSummary)
