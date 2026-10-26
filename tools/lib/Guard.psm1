<#
.SYNOPSIS
The shared guard of every script an admin runs by hand (docs/process/owner-scripts.md). Written for Windows PowerShell 5.1; also loads in PowerShell 7.

.DESCRIPTION
A script that changes something, or that is handed to the owner, starts with two lines and nothing else before them but its param block:

    Import-Module (Join-Path $PSScriptRoot '..\lib\Guard.psm1') -Force -ErrorAction Stop      # ops\lib\ next to the script's folder
    $null = Assert-Script -ScriptPath $PSCommandPath -Kind Changes -Applying:$Apply -ExpectedHost 'HOST01' -Elevation Required

Assert-Script checks, and throws one error listing everything that is wrong (nothing has run yet) unless all of these hold:
  host        the computer name is one of -ExpectedHost (or the script says why it can run anywhere: -AnyHostBecause '<reason>')
  domain      the computer is joined to the machine domain of the policy below (-MachineDomain overrides; -AnyMachineDomainBecause for a workgroup server)
  user        the signed-in account matches one of -User (default: the policy's AllowedUsers)
  elevation   Required, Forbidden or Either; asked for by name, never assumed
  edition     Windows PowerShell 5.1 (Desktop) unless -Edition Any
  repo        the script comes from a git checkout; when the script changes something for real (-Applying) that checkout must be clean, on 'main' and
              equal to origin/main as last fetched, or be pinned: $env:CM_EXPECT_COMMIT = '<sha>' (that exact commit, any branch). A command block may also name the checkout it was written for: $env:CM_EXPECT_REPO = '<absolute folder>' (wrong worktree = refused, also on a dry run).
              A fix that is only on a pull request branch runs with
              $env:CM_ALLOW_BRANCH = '<branch>' on the command line (that checked-out branch at its origin tip, as last fetched, and clean), or pinned to its sha. A dry run or read only run only prints the state.
              A machine that has no checkout (the script was copied there) says -RepoNotRequiredBecause '<reason>': the state is then only printed.
  signature   off today; switched on in the policy when the scripts are signed (AllSigned, see the doc): then the script and this module must carry a valid
              signature of one of the TrustedSignerThumbprints.
It reads the repo state from the .git folder itself, so it works in an elevated shell where git refuses the folder.

It prints one banner line with the facts it found, so a pasted screen shows where and as whom the script ran.

Assert-Target is the second guard, for the system a script is about (a server, a database): it throws unless the name is on the allowed list.
Resolve-Principal / Get-LocalGroup resolve local groups by well-known SID: the systems run German Windows ("Administratoren"), so a script never
writes a group name.

The policy hashtable below is part of this signed file on purpose (a data file next to it would not be covered by the signature). An empty MachineDomain or AllowedUsers
switches that check off unless the script names its own value; the CM repository ("roles, not values") ships this file with those two left empty and passes the values as parameters.
#>
Set-StrictMode -Version 2

# ---- policy: the facts about the environment that are true for every script (docs/ENVIRONMENT.md) -------------------------------------------------------
$script:GuardPolicy = @{
    MachineDomain            = $null                           # roles, not values: a script passes its own with -MachineDomain
    AllowedUsers             = @()                             # -like patterns on DOMAIN\name; a script passes its own with -User
    Branch                   = 'main'
    RequireSignature         = $false                          # flip to $true when the scripts are signed (docs/process/owner-scripts.md "Signing")
    TrustedSignerThumbprints = @()                             # SHA-1 thumbprints of the signing certificate(s) once it exists
}

function Get-GuardPolicy { [CmdletBinding()] param() return $script:GuardPolicy }

# ---- facts about this session (the only part that touches the machine; tests pass their own) -------------------------------------------------------------
function Get-RepoState {
    <# Reads branch, commit and origin/main from the .git folder without git. Returns $null when the path is not inside a checkout. #>
    [CmdletBinding()] param([Parameter(Mandatory)][string]$StartPath)
    $dir = Get-Item -LiteralPath $StartPath -ErrorAction Stop
    if (-not $dir.PSIsContainer) { $dir = $dir.Directory }
    $root = $null
    for ($d = $dir; $null -ne $d; $d = $d.Parent) {
        if (Test-Path -LiteralPath (Join-Path $d.FullName '.git')) { $root = $d.FullName; break }
    }
    if (-not $root) { return $null }
    $gitEntry = Join-Path $root '.git'
    $gitDir = $gitEntry
    if (Test-Path -LiteralPath $gitEntry -PathType Leaf) {                       # a worktree: ".git" is a file "gitdir: <path>"
        $line = (Get-Content -LiteralPath $gitEntry -TotalCount 1)
        if ($line -notmatch '^gitdir:\s*(.+)$') { return $null }
        $gitDir = $Matches[1].Trim()
        if (-not [System.IO.Path]::IsPathRooted($gitDir)) { $gitDir = [System.IO.Path]::GetFullPath((Join-Path $root $gitDir)) }
    }
    $common = $gitDir
    $commonFile = Join-Path $gitDir 'commondir'
    if (Test-Path -LiteralPath $commonFile) {
        $c = (Get-Content -LiteralPath $commonFile -TotalCount 1).Trim()
        $common = if ([System.IO.Path]::IsPathRooted($c)) { $c } else { [System.IO.Path]::GetFullPath((Join-Path $gitDir $c)) }
    }
    function Resolve-Ref([string]$Ref) {
        foreach ($base in @($gitDir, $common)) {
            $f = Join-Path $base $Ref
            if (Test-Path -LiteralPath $f -PathType Leaf) { return (Get-Content -LiteralPath $f -TotalCount 1).Trim() }
        }
        $packed = Join-Path $common 'packed-refs'
        if (Test-Path -LiteralPath $packed) {
            foreach ($l in Get-Content -LiteralPath $packed) {
                if ($l -match '^([0-9a-f]{40})\s+(.+)$' -and $Matches[2] -eq $Ref) { return $Matches[1] }
            }
        }
        return $null
    }
    $head = (Get-Content -LiteralPath (Join-Path $gitDir 'HEAD') -TotalCount 1).Trim()
    $branch = $null; $sha = $null
    if ($head -match '^ref:\s*refs/heads/(.+)$') { $branch = $Matches[1]; $sha = Resolve-Ref "refs/heads/$branch" }
    elseif ($head -match '^[0-9a-f]{40}$') { $sha = $head }
    [pscustomobject]@{
        Root = $root; Branch = $branch; Sha = $sha
        OriginMainSha = (Resolve-Ref 'refs/remotes/origin/main')
        OriginBranchSha = $(if ($branch) { Resolve-Ref "refs/remotes/origin/$branch" } else { $null })
        Dirty = $null                                                              # filled by Get-GuardFacts when git works
    }
}

function Get-RepoDirty {
    <# $true / $false / $null (unknown: no git on the PATH, or git refuses the folder). Tracked files only; untracked files do not change what a script does. #>
    [CmdletBinding()] param([Parameter(Mandatory)][string]$Root)
    $git = Get-Command git.exe, git -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $git) { return $null }
    try {
        $out = & $git.Source -c "safe.directory=$Root" -C $Root status --porcelain --untracked-files=no 2>&1
        if ($LASTEXITCODE -ne 0) { return $null }
        return (@($out | Where-Object { "$_".Trim() }).Count -gt 0)
    } catch { return $null }
}

function Get-GuardFacts {
    [CmdletBinding()] param([Parameter(Mandatory)][string]$ScriptPath)
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $elevated = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)   # built-in role = SID based
    $computer = $env:COMPUTERNAME
    if (-not $computer) { $computer = [Environment]::MachineName }
    $domain = $null
    try { $domain = (Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop).Domain } catch { $domain = $env:USERDNSDOMAIN }
    $repo = Get-RepoState -StartPath $ScriptPath
    if ($repo) { $repo.Dirty = Get-RepoDirty -Root $repo.Root }
    $edition = if ($PSVersionTable.PSObject.Properties['PSEdition']) { [string]$PSVersionTable.PSEdition } else { 'Desktop' }
    $sigs = @{}
    foreach ($p in @($ScriptPath, $PSCommandPath)) {
        $s = Get-AuthenticodeSignature -FilePath $p -ErrorAction SilentlyContinue
        $sigs[$p] = if ($s) { [pscustomobject]@{ Status = [string]$s.Status; Thumbprint = $(if ($s.SignerCertificate) { $s.SignerCertificate.Thumbprint } else { '' }) } } else { $null }
    }
    [pscustomobject]@{
        ComputerName = $computer; MachineDomain = $domain; UserName = $identity.Name; Elevated = $elevated
        Edition = $edition; PSVersion = $PSVersionTable.PSVersion.ToString()
        Repo = $repo; ScriptPath = $ScriptPath; ModulePath = $PSCommandPath; Signatures = $sigs
        ExpectCommit = $env:CM_EXPECT_COMMIT; AllowBranch = $env:CM_ALLOW_BRANCH; ExpectRepo = $env:CM_EXPECT_REPO
    }
}

# ---- the decision (pure: facts in, findings out; this is what the tests exercise) -------------------------------------------------------------------------
function Test-Guard {
    <# Returns @{ Problems = string[]; Notes = string[] }. Problems stop the script. #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Facts,
        [Parameter(Mandatory)][ValidateSet('ReadOnly', 'Changes')][string]$Kind,
        [bool]$Applying,
        [string[]]$ExpectedHost,
        [string]$AnyHostBecause,
        [Parameter(Mandatory)][ValidateSet('Required', 'Forbidden', 'Either')][string]$Elevation,
        [string[]]$User,
        [string]$AnyUserBecause,
        [string]$MachineDomain,
        [string]$AnyMachineDomainBecause,
        [ValidateSet('Desktop', 'Any')][string]$Edition = 'Desktop',
        [string]$RepoNotRequiredBecause,
        $Policy = $script:GuardPolicy
    )
    $problems = New-Object System.Collections.Generic.List[string]
    $notes = New-Object System.Collections.Generic.List[string]

    # host
    if ($ExpectedHost -and $AnyHostBecause) { $problems.Add('script bug: both -ExpectedHost and -AnyHostBecause given.') }
    elseif ($ExpectedHost) {
        $hostNames = @($ExpectedHost | ForEach-Object { $_.Trim() })
        if ($hostNames | Where-Object { -not $_ -or $_ -match '[\*\?\[\]]' }) { $problems.Add('script bug: -ExpectedHost must be exact computer names, no wildcards.') }
        elseif (-not ($hostNames | Where-Object { $_ -ieq $Facts.ComputerName })) {
            $problems.Add(("host: this computer is '{0}', the script is for {1}. Nothing was changed. Run it on the right machine." -f $Facts.ComputerName, (($hostNames | ForEach-Object { "'$_'" }) -join ' or ')))
        }
    }
    elseif ($AnyHostBecause) { $notes.Add("host check off: $AnyHostBecause") }
    else { $problems.Add('script bug: -ExpectedHost or -AnyHostBecause is required.') }

    # machine domain
    if ($AnyMachineDomainBecause) { $notes.Add("domain check off: $AnyMachineDomainBecause") }
    else {
        $want = if ($MachineDomain) { $MachineDomain } else { $Policy.MachineDomain }
        if (-not $want) { $notes.Add('domain check skipped: neither the policy nor the script names a machine domain.') }
        elseif ($Facts.MachineDomain -ine $want) { $problems.Add(("domain: this computer is in '{0}', expected '{1}'." -f $Facts.MachineDomain, $want)) }
    }

    # user
    if ($AnyUserBecause) { $notes.Add("user check off: $AnyUserBecause") }
    else {
        $patterns = if ($User) { $User } else { $Policy.AllowedUsers }
        if (-not $patterns) { $notes.Add('user check skipped: neither the policy nor the script names an allowed account.') }
        elseif (-not ($patterns | Where-Object { $Facts.UserName -like $_ })) {
            $problems.Add(("user: signed in as '{0}', allowed: {1}. Sign in with the account the how-to names." -f $Facts.UserName, ($patterns -join ', ')))
        }
    }

    # elevation
    if ($Elevation -eq 'Required' -and -not $Facts.Elevated) { $problems.Add('elevation: this session is not elevated. Start Windows PowerShell with "Run as administrator" (Als Administrator ausfuehren).') }
    if ($Elevation -eq 'Forbidden' -and $Facts.Elevated) { $problems.Add('elevation: this session IS elevated, the script wants a normal one (least privilege). Start a normal Windows PowerShell.') }

    # edition
    if ($Edition -eq 'Desktop' -and $Facts.Edition -ne 'Desktop') { $problems.Add("edition: this is PowerShell $($Facts.PSVersion) ($($Facts.Edition)); the script is written for Windows PowerShell 5.1 (powershell.exe).") }

    # repo
    $repo = $Facts.Repo
    # The wrong worktree is the classic slip when one person hops between threads: a command block names the checkout it was written for
    # ($env:CM_EXPECT_REPO = '<folder>') and the script must come from exactly that folder, also on a dry run.
    if ($Facts.ExpectRepo) {
        $norm = { param([string]$x) if (-not $x) { return '' } return ([System.IO.Path]::GetFullPath($x)).TrimEnd('\', '/') }
        if (-not $repo) { $problems.Add(("repo: CM_EXPECT_REPO names '{0}' but the script is not inside a git checkout." -f $Facts.ExpectRepo)) }
        elseif ((& $norm $repo.Root) -ine (& $norm $Facts.ExpectRepo)) { $problems.Add(("repo: this script comes from the checkout '{0}', the command block was written for '{1}'. You are in the wrong worktree; use the absolute path the hand-off gives." -f $repo.Root, $Facts.ExpectRepo)) }
    }
    $enforce = ($Kind -eq 'Changes' -and $Applying)
    $repoProblems = New-Object System.Collections.Generic.List[string]
    if (-not $repo) { $repoProblems.Add('repo: the script is not inside a git checkout, so nobody can say which version it is. Run it from a clone of the repository.') }
    else {
        $sha = $Facts.ExpectCommit
        if ($sha) {
            if ($sha.Length -lt 7 -or -not $repo.Sha -or -not $repo.Sha.StartsWith($sha, [StringComparison]::OrdinalIgnoreCase)) {
                $repoProblems.Add(("repo: CM_EXPECT_COMMIT is '{0}' but the checkout {1} is at '{2}'." -f $sha, $repo.Root, $(if ($repo.Sha) { $repo.Sha.Substring(0, 7) } else { 'unknown' })))
            }
        }
        else {
            $allowed = $Policy.Branch
            if ($Facts.AllowBranch -and $repo.Branch -eq $Facts.AllowBranch) {
                # A named branch is allowed only as it was pushed: the local tip must equal origin/<branch> as last fetched, so unpushed commits cannot run.
                if (-not $repo.OriginBranchSha) { $repoProblems.Add("repo: CM_ALLOW_BRANCH names '$($Facts.AllowBranch)' but origin/$($Facts.AllowBranch) is not known here (git fetch origin $($Facts.AllowBranch)).") }
                elseif ($repo.Sha -ne $repo.OriginBranchSha) { $repoProblems.Add(('repo: local {0} ({1}) differs from origin/{0} as last fetched ({2}). Pull, or push what is not pushed.' -f $repo.Branch, $repo.Sha.Substring(0, 7), $repo.OriginBranchSha.Substring(0, 7))) }
                $notes.Add("BRANCH OVERRIDE: running '$($Facts.AllowBranch)' because CM_ALLOW_BRANCH says so.")
            }
            elseif ($repo.Branch -ne $allowed) { $repoProblems.Add(("repo: the checkout {0} is on '{1}', not '{2}'. Run it from the merged '{2}' (git checkout {2}; git pull --ff-only), or pin the commit with `$env:CM_EXPECT_COMMIT." -f $repo.Root, $(if ($repo.Branch) { $repo.Branch } else { 'a detached commit' }), $allowed)) }
            elseif ($repo.OriginMainSha -and $repo.Sha -ne $repo.OriginMainSha) { $repoProblems.Add(('repo: local main ({0}) differs from origin/main as last fetched ({1}). Pull, or push what is not merged.' -f $repo.Sha.Substring(0, 7), $repo.OriginMainSha.Substring(0, 7))) }
        }
        if ($repo.Dirty -eq $true) { $repoProblems.Add("repo: the checkout $($repo.Root) has uncommitted changes to tracked files; the script on disk is not a committed version.") }
        elseif ($null -eq $repo.Dirty) { $notes.Add('repo: could not check for uncommitted changes (git not usable here).') }
    }
    if ($RepoNotRequiredBecause) { $notes.Add("repo check off: $RepoNotRequiredBecause"); foreach ($p in $repoProblems) { $notes.Add("(not enforced) $p") } }
    elseif ($enforce) { foreach ($p in $repoProblems) { $problems.Add($p) } }
    else { foreach ($p in $repoProblems) { $notes.Add("(not enforced: nothing is changed) $p") } }

    # signature (off until the scripts are signed)
    if ($Policy.RequireSignature) {
        foreach ($p in @($Facts.ScriptPath, $Facts.ModulePath)) {
            $s = $Facts.Signatures[$p]
            if (-not $s -or $s.Status -ne 'Valid') { $problems.Add(("signature: '{0}' has no valid signature ({1})." -f (Split-Path -Leaf $p), $(if ($s) { $s.Status } else { 'unreadable' }))) }
            elseif ($Policy.TrustedSignerThumbprints.Count -gt 0 -and $Policy.TrustedSignerThumbprints -notcontains $s.Thumbprint) { $problems.Add(("signature: '{0}' is signed by an unknown certificate ({1})." -f (Split-Path -Leaf $p), $s.Thumbprint)) }
        }
    }
    [pscustomobject]@{ Problems = $problems.ToArray(); Notes = $notes.ToArray() }
}

function Assert-Script {
    <# See the module help. Returns the facts object so a script can print them; throws when a check fails. #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ScriptPath,
        [Parameter(Mandatory)][ValidateSet('ReadOnly', 'Changes')][string]$Kind,
        [switch]$Applying,
        [string[]]$ExpectedHost,
        [string]$AnyHostBecause,
        [Parameter(Mandatory)][ValidateSet('Required', 'Forbidden', 'Either')][string]$Elevation,
        [string[]]$User,
        [string]$AnyUserBecause,
        [string]$MachineDomain,
        [string]$AnyMachineDomainBecause,
        [ValidateSet('Desktop', 'Any')][string]$Edition = 'Desktop',
        [string]$RepoNotRequiredBecause
    )
    $facts = Get-GuardFacts -ScriptPath $ScriptPath
    # A Changes script that does not say -Applying has no dry run (the older scripts); it counts as applying all the time.
    $applying = if ($Kind -ne 'Changes') { $false } elseif ($PSBoundParameters.ContainsKey('Applying')) { [bool]$Applying } else { $true }
    $splat = @{ Facts = $facts; Kind = $Kind; Applying = $applying; Elevation = $Elevation; Edition = $Edition }
    foreach ($k in 'ExpectedHost', 'AnyHostBecause', 'User', 'AnyUserBecause', 'MachineDomain', 'AnyMachineDomainBecause', 'RepoNotRequiredBecause') {
        if ($PSBoundParameters.ContainsKey($k)) { $splat[$k] = $PSBoundParameters[$k] }
    }
    $r = Test-Guard @splat
    $name = Split-Path -Leaf $ScriptPath
    if ($r.Problems.Count -gt 0) {
        $text = "GUARD refused to run $name. Nothing was started or changed.`n  - " + ($r.Problems -join "`n  - ")
        throw $text
    }
    $repo = $facts.Repo
    $where = if ($repo) { "{0} {1}@{2}" -f $repo.Root, $(if ($repo.Branch) { $repo.Branch } else { 'detached' }), $(if ($repo.Sha) { $repo.Sha.Substring(0, 7) } else { '?' }) } else { 'no checkout' }
    $mode = if ($Kind -eq 'ReadOnly') { 'read only' } elseif ($applying) { 'APPLY' } else { 'dry run' }
    Write-Host ("Guard OK [{0}]: {1} | {2} | {3} | PowerShell {4} | {5} | {6}" -f $ScriptPath, $facts.ComputerName, $facts.UserName, $(if ($facts.Elevated) { 'elevated' } else { 'not elevated' }), $facts.PSVersion, $where, $mode) -ForegroundColor DarkGray
    foreach ($n in $r.Notes) { Write-Host "Guard note: $n" -ForegroundColor Yellow }
    return $facts
}

function Assert-Target {
    <# The system the script is about (a SQL server, a cluster, a mailbox host) must be on the allowed list; short names and FQDNs both match. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string[]]$Allowed, [string]$What = 'target')
    $short = ($Name.Trim() -split '\.')[0]
    if (-not ($Allowed | Where-Object { $_ -ieq $short })) {
        throw ("GUARD refused the {0} '{1}'; allowed: {2}. Nothing was changed." -f $What, $Name, ($Allowed -join ', '))
    }
}

# ---- programs: an install or upgrade must leave the tool working (Git was removed from a laptop by an installer rollback, 2026-10-25) ------------------
function Get-ToolVersion {
    <# The first output line of '<tool> <VersionArgs>' (default --version), or $null when the program is not found or does not run. Looks at the machine and user PATH too, not only the one this session started with. #>
    [CmdletBinding()] param([Parameter(Mandatory)][string]$Name, [string[]]$VersionArgs = @('--version'))
    $cmd = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $cmd) {
        $fresh = (@([Environment]::GetEnvironmentVariable('Path', 'Machine'), [Environment]::GetEnvironmentVariable('Path', 'User')) | Where-Object { $_ }) -join ';'
        $env:Path = $fresh
        $cmd = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if (-not $cmd) { return $null }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'      # Windows PowerShell 5.1 turns a program's stderr text into a terminating error under 'Stop'; the exit code is the truth
    try { $out = & $cmd.Source @VersionArgs 2>&1; $code = $LASTEXITCODE } catch { return $null } finally { $ErrorActionPreference = $previous }
    if ($code -ne 0) { return $null }
    $line = @($out | ForEach-Object { "$_" } | Where-Object { $_.Trim() } | Select-Object -First 1)
    if ($line.Count -eq 0) { return $null }
    return $line[0].Trim()
}

function Assert-ToolWorks {
    <# Call it after every install or upgrade of a program. Throws when the tool does not run; with -Before (its version from Get-ToolVersion before the step) the message says it was working and is gone, which is what a rolled-back upgrade looks like. Returns the version line. #>
    [CmdletBinding()] param([Parameter(Mandatory)][string]$Name, [string[]]$VersionArgs = @('--version'), [string]$Before)
    $now = Get-ToolVersion -Name $Name -VersionArgs $VersionArgs
    if (-not $now) {
        if ($Before) { throw ("GUARD: '{0}' worked before this step ({1}) and does not run now. The installer probably failed and rolled back, and the rollback removed the old version. Reinstall '{0}' first, then read the installer's log before you try again." -f $Name, $Before) }
        throw ("GUARD: '{0}' does not run after the install ('{0} {1}' found nothing or failed). Install it by hand and run this stage again." -f $Name, ($VersionArgs -join ' '))
    }
    Write-Host ("Tool OK: {0} {1}{2}" -f $Name, $now, $(if ($Before -and $Before -ne $now) { " (was $Before)" } else { '' })) -ForegroundColor DarkGray
    return $now
}

# ---- guarded writes: a write is allowed only when a read has found the state it is about to overwrite ----------------------------------------------------
$script:SkippedWrites = New-Object System.Collections.Generic.List[string]

function Format-State {
    <# One line for a state in a message. Compact JSON, not Out-String: in a headless host Out-String printed nothing for small objects (L1, 2026-10-25). #>
    [CmdletBinding()] param($State)
    if ($null -eq $State) { return '(nothing)' }
    if ($State -is [string]) { return $State.Trim() }
    try { return (ConvertTo-Json -InputObject $State -Compress -Depth 4 -WarningAction SilentlyContinue) } catch { return "$State" }
}

function Invoke-GuardedWrite {
    <#
    Read, assert, write, read back. The read ALWAYS runs (also in a dry run), so a dry run already shows every mismatch.
      -Read   { }           LIVE read of the target system (never a variable filled earlier): returns the current state. A read failure counts as "state not found".
      -Expect { param($s) } $true when the state is the one this write may overwrite
      -Done   { param($s) } optional: $true when the state already is the wanted one (outcome Already, nothing written; keeps the script idempotent)
      -Verify { param($s) } $true when the state READ BACK after the write is the wanted one (defaults to -Done; one of the two is required). A write that did not take is always a hard stop.
      -DependentSteps       the later steps that need this write to have happened; mandatory so the author decides. Empty = nothing depends on it.
      -Attempts             default 1. Each attempt starts with a fresh -Read and a fresh -Expect; a retry never reuses an earlier read.
      -Write  { param($s) } the change; runs only with -Apply and only after Expect held
    State not found: with dependent steps the script stops here (hard, a throw, nothing written); with none it warns, records the skip and goes on (soft).
    Outcomes: Written, WouldWrite, Already, Skipped. Finish the script with: exit (Write-RunSummary)
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$What,
        [Parameter(Mandatory)][scriptblock]$Read,
        [Parameter(Mandatory)][scriptblock]$Expect,
        [scriptblock]$Done,
        [scriptblock]$Verify,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$DependentSteps,
        [ValidateRange(1, 10)][int]$Attempts = 1,
        [ValidateRange(0, 300)][int]$RetryDelaySeconds = 2,
        [Parameter(Mandatory)][scriptblock]$Write,
        [switch]$Apply
    )
    if (-not $Verify) { $Verify = $Done }
    if (-not $Verify) { throw "GUARD: script bug in '$What': -Verify (or -Done) is required, every write is read back." }
    $shortState = {
        param($state, $err)
        $t = if ($err) { "read failed: $err" } else { Format-State $state }
        if ($t.Length -gt 200) { $t = $t.Substring(0, 200) + '...' }
        $t
    }
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $readError = $null
        $state = $null
        try { $state = & $Read } catch { $readError = $_.Exception.Message }      # fresh on every attempt
        $shown = & $shortState $state $readError
        if (-not $readError -and $Done -and (& $Done $state)) {
            Write-Host "OK    $What (already in the wanted state)" -ForegroundColor Green
            return [pscustomobject]@{ What = $What; Outcome = 'Already'; State = $state }
        }
        if ($readError -or -not (& $Expect $state)) {
            $text = "expected state not found for '$What'; found: $shown. Nothing was written."
            if ($attempt -lt $Attempts) { Write-Warning "$text Attempt $attempt of $Attempts; reading again."; Start-Sleep -Seconds $RetryDelaySeconds; continue }
            if ($DependentSteps.Count -gt 0) { throw ("GUARD: {0} Later steps depend on it: {1}." -f $text, ($DependentSteps -join ', ')) }
            Write-Warning "$text Skipped; nothing later depends on it."
            $script:SkippedWrites.Add("$What -- found: $shown")
            return [pscustomobject]@{ What = $What; Outcome = 'Skipped'; State = $state }
        }
        if (-not $Apply) {
            Write-Host "WOULD $What (state found: $shown)" -ForegroundColor Cyan
            return [pscustomobject]@{ What = $What; Outcome = 'WouldWrite'; State = $state }
        }
        & $Write $state
        $after = $null
        $afterError = $null
        try { $after = & $Read } catch { $afterError = $_.Exception.Message }          # read back, live
        if (-not $afterError -and (& $Verify $after)) {
            Write-Host "DONE  $What (was: $shown; now: $(& $shortState $after $null))" -ForegroundColor Green
            return [pscustomobject]@{ What = $What; Outcome = 'Written'; State = $after }
        }
        $bad = "the write for '$What' did not take: read back $(& $shortState $after $afterError)."
        if ($attempt -lt $Attempts) { Write-Warning "$bad Attempt $attempt of $Attempts; reading again."; Start-Sleep -Seconds $RetryDelaySeconds; continue }
        throw ("GUARD: {0} The state of the target is unknown now; check it by hand before anything else runs." -f $bad)
    }
}

function Assert-State {
    <# Stops the script unless a LIVE read finds the expected state. Host, account, session and checkout are checked by the guard; use this for the rest: the tenant, the domain, the server a connection points at. #>
    [CmdletBinding()] param([Parameter(Mandatory)][string]$What, [Parameter(Mandatory)][scriptblock]$Read, [Parameter(Mandatory)][scriptblock]$Expect)
    $state = $null
    try { $state = & $Read } catch { throw "GUARD: could not read $What ($($_.Exception.Message)). Nothing was changed." }
    if (-not (& $Expect $state)) { throw ("GUARD: {0} is '{1}', not what this script expects. Nothing was changed." -f $What, (Format-State $state)) }
}

function Assert-Tenant {
    <# The tenant a connected session points at must be the expected one (a wrong sign-in is the most expensive slip there is). -Service Graph reads Get-MgContext, Exchange reads Get-ConnectionInformation; -Current replaces the read (tests). #>
    [CmdletBinding()] param([Parameter(Mandatory)][string]$ExpectedTenantId, [ValidateSet('Graph', 'Exchange')][string]$Service = 'Graph', [scriptblock]$Current)
    if (-not $Current) { $Current = if ($Service -eq 'Graph') { { (Get-MgContext).TenantId } } else { { (Get-ConnectionInformation | Select-Object -First 1).TenantId } } }
    Assert-State -What "the connected $Service tenant id" -Read $Current -Expect { param($t) "$t" -ieq $ExpectedTenantId }
}

function Get-SkippedWrites { [CmdletBinding()] param() return $script:SkippedWrites.ToArray() }

function Write-RunSummary {
    <# Prints the soft-skipped writes and returns the exit code: 0 when none, 3 when some. A skipped write must never look like a clean run. #>
    [CmdletBinding()] param()
    if ($script:SkippedWrites.Count -eq 0) { return 0 }
    Write-Host ("{0} write(s) were SKIPPED because the expected state was not found:" -f $script:SkippedWrites.Count) -ForegroundColor Yellow
    foreach ($w in $script:SkippedWrites) { Write-Host "  - $w" -ForegroundColor Yellow }
    return 3
}

# ---- principals by SID (German Windows: "Administratoren", "Benutzer"; a name in a script breaks there) -----------------------------------------------------
$script:WellKnownSids = @{
    Administrators        = 'S-1-5-32-544'
    Users                 = 'S-1-5-32-545'
    Guests                = 'S-1-5-32-546'
    BackupOperators       = 'S-1-5-32-551'
    RemoteDesktopUsers    = 'S-1-5-32-555'
    PerformanceMonitorUsers = 'S-1-5-32-558'
    PerformanceLogUsers   = 'S-1-5-32-559'
    RemoteManagementUsers = 'S-1-5-32-580'
    System                = 'S-1-5-18'
    LocalService          = 'S-1-5-19'
    NetworkService        = 'S-1-5-20'
    Everyone              = 'S-1-1-0'
    AuthenticatedUsers    = 'S-1-5-11'
}

function Get-Sid {
    <# The well-known SID for a role name above, as text. #>
    [CmdletBinding()] param([Parameter(Mandatory)][ValidateSet('Administrators', 'Users', 'Guests', 'BackupOperators', 'RemoteDesktopUsers', 'PerformanceMonitorUsers', 'PerformanceLogUsers', 'RemoteManagementUsers', 'System', 'LocalService', 'NetworkService', 'Everyone', 'AuthenticatedUsers')][string]$Role)   # lint-ok: principal-name role keys of Get-Sid, mapped to SIDs below
    return $script:WellKnownSids[$Role]
}

function Resolve-Principal {
    <# The name of a well-known principal on THIS machine in the machine's own language ("Administratoren"), from its SID. Use it only to print or for tools that need a name. #>
    [CmdletBinding()] param([Parameter(Mandatory)][string]$Role)
    $sid = New-Object Security.Principal.SecurityIdentifier((Get-Sid -Role $Role))
    return $sid.Translate([Security.Principal.NTAccount]).Value
}

Export-ModuleMember -Function Format-State, Invoke-GuardedWrite, Assert-State, Assert-Tenant, Get-SkippedWrites, Write-RunSummary, Assert-ToolWorks, Get-ToolVersion, Assert-Script, Assert-Target, Test-Guard, Get-GuardFacts, Get-RepoState, Get-GuardPolicy, Get-Sid, Resolve-Principal
