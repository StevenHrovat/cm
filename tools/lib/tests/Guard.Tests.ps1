<#
Self-test of ops/lib/Guard.psm1. Plain assertions, no Pester, runs in Windows PowerShell 5.1 and PowerShell 7, on Linux too (no machine facts are read:
Test-Guard takes the facts as a parameter and the repo reader works on fake .git folders made here). Exit code 0 = all passed. Run by
tools/verify (step scripts); by hand: powershell.exe -NoProfile -File tools\lib\tests\Guard.Tests.ps1
#>
param([string]$Module = (Join-Path $PSScriptRoot '..\Guard.psm1'))
$ErrorActionPreference = 'Stop'
Import-Module $Module -Force
$failed = 0; $count = 0
function Check([string]$Name, [bool]$Condition) {
    $script:count++
    if ($Condition) { Write-Host "ok   $Name" } else { $script:failed++; Write-Host "FAIL $Name" -ForegroundColor Red }
}
function New-Facts([hashtable]$Over = @{}) {
    $repo = [pscustomobject]@{ Root = 'C:\work\checkout'; Branch = 'main'; Sha = ('a' * 40); OriginMainSha = ('a' * 40); Dirty = $false }
    $f = [ordered]@{
        ComputerName = 'ADMINHOST01'; MachineDomain = 'ad.example.test'; UserName = 'AD\adm_alice'; Elevated = $true; Edition = 'Desktop'; PSVersion = '5.1.26100.1'
        Repo = $repo; ScriptPath = 'C:\x\s.ps1'; ModulePath = 'C:\x\m.psm1'; Signatures = @{}; ExpectCommit = $null; AllowBranch = $null; ExpectRepo = $null
    }
    foreach ($k in $Over.Keys) { $f[$k] = $Over[$k] }
    [pscustomobject]$f
}
# The tests bring their own policy, so this file is the same in every repository that carries the module (the operations master, CM).
$testPolicy = @{ MachineDomain = 'ad.example.test'; AllowedUsers = @('AD\adm_*', 'AD\inst_*'); Branch = 'main'; RequireSignature = $false; TrustedSignerThumbprints = @() }
function Run($Facts, [hashtable]$Args2) {
    $a = @{ Facts = $Facts; Kind = 'Changes'; Applying = $true; ExpectedHost = 'ADMINHOST01'; Elevation = 'Required'; Policy = $testPolicy }
    foreach ($k in $Args2.Keys) { $a[$k] = $Args2[$k] }
    if ($a.ContainsKey('AnyHostBecause')) { $a.Remove('ExpectedHost') }
    Test-Guard @a
}
function Has($r, [string]$Text) { @($r.Problems | Where-Object { $_ -like "*$Text*" }).Count -gt 0 }

# ---- host (the owner's rule) ----
Check 'all good passes' ((Run (New-Facts) @{}).Problems.Count -eq 0)
Check 'wrong host fails' (Has (Run (New-Facts @{ ComputerName = 'OTHERHOST02' }) @{}) "this computer is 'OTHERHOST02'")
Check 'host compare ignores case' ((Run (New-Facts @{ ComputerName = 'adminhost01' }) @{}).Problems.Count -eq 0)
Check 'a prefix of the host is not a match' (Has (Run (New-Facts @{ ComputerName = 'ADMINHOST0' }) @{}) 'this computer')
Check 'two allowed hosts, second matches' ((Run (New-Facts @{ ComputerName = 'B' }) @{ ExpectedHost = @('A', 'B') }).Problems.Count -eq 0)
Check 'wildcard host is a script bug' (Has (Run (New-Facts) @{ ExpectedHost = 'ADMIN*' }) 'no wildcards')
Check 'no host argument at all fails' (Has (Test-Guard -Facts (New-Facts) -Kind Changes -Applying $true -Elevation Required -Policy $testPolicy) '-ExpectedHost or -AnyHostBecause is required')
$any = Run (New-Facts @{ ComputerName = 'LAPTOP' }) @{ AnyHostBecause = 'gh runs anywhere' }
Check 'AnyHostBecause passes and leaves a note' ($any.Problems.Count -eq 0 -and ($any.Notes -join ' ') -like '*gh runs anywhere*')
Check 'both host forms is a script bug' (Has (Test-Guard -Facts (New-Facts) -Kind Changes -Applying $true -ExpectedHost 'ADMINHOST01' -AnyHostBecause 'x' -Elevation Required -Policy $testPolicy) 'both')

# ---- domain, user, elevation, edition ----
Check 'wrong machine domain fails' (Has (Run (New-Facts @{ MachineDomain = 'WORKGROUP' }) @{}) "expected 'ad.example.test'")
Check 'null machine domain fails' (Has (Run (New-Facts @{ MachineDomain = $null }) @{}) 'domain:')
Check 'workgroup server with a reason passes' ((Run (New-Facts @{ MachineDomain = 'WORKGROUP' }) @{ AnyMachineDomainBecause = 'BACKUPHOST is a workgroup server' }).Problems.Count -eq 0)
Check 'machine domain override' ((Run (New-Facts @{ MachineDomain = 'other.test' }) @{ MachineDomain = 'other.test' }).Problems.Count -eq 0)
Check 'adm_ account passes' ((Run (New-Facts @{ UserName = 'AD\adm_bob' }) @{}).Problems.Count -eq 0)
Check 'a normal account fails the default' (Has (Run (New-Facts @{ UserName = 'AD\alice' }) @{}) 'user:')
Check 'same name in another domain fails' (Has (Run (New-Facts @{ UserName = 'EVIL\adm_alice' }) @{}) 'user:')
Check 'explicit -User widens' ((Run (New-Facts @{ UserName = 'AD\alice' }) @{ User = @('AD\alice') }).Problems.Count -eq 0)
Check 'elevation required, not elevated' (Has (Run (New-Facts @{ Elevated = $false }) @{}) 'not elevated')
Check 'elevation forbidden, elevated' (Has (Run (New-Facts) @{ Elevation = 'Forbidden' }) 'IS elevated')
Check 'elevation Either passes both' ((Run (New-Facts @{ Elevated = $false }) @{ Elevation = 'Either' }).Problems.Count -eq 0)
Check 'pwsh 7 fails Desktop edition' (Has (Run (New-Facts @{ Edition = 'Core'; PSVersion = '7.5.0' }) @{}) 'edition:')
Check 'edition Any passes pwsh 7' ((Run (New-Facts @{ Edition = 'Core' }) @{ Edition = 'Any' }).Problems.Count -eq 0)
Check 'all problems are listed at once' ((Run (New-Facts @{ ComputerName = 'X'; Elevated = $false; UserName = 'Y\z' }) @{}).Problems.Count -eq 3)

# ---- repo ----
$feature = [pscustomobject]@{ Root = 'C:\claude\wt\t'; Branch = 'claude/x'; Sha = ('b' * 40); OriginMainSha = ('a' * 40); OriginBranchSha = ('b' * 40); Dirty = $false }
Check 'feature branch + applying fails' (Has (Run (New-Facts @{ Repo = $feature }) @{}) "is on 'claude/x'")
$dry = Run (New-Facts @{ Repo = $feature }) @{ Applying = $false }
Check 'feature branch + dry run passes with a note' ($dry.Problems.Count -eq 0 -and ($dry.Notes -join ' ') -like '*not enforced*')
Check 'read only kind never enforces the repo' ((Run (New-Facts @{ Repo = $feature }) @{ Kind = 'ReadOnly' }).Problems.Count -eq 0)
Check 'pinned commit passes on a feature branch' ((Run (New-Facts @{ Repo = $feature; ExpectCommit = 'bbbbbbb' }) @{}).Problems.Count -eq 0)
Check 'pinned commit that differs fails' (Has (Run (New-Facts @{ Repo = $feature; ExpectCommit = 'ccccccc' }) @{}) 'CM_EXPECT_COMMIT')
Check 'a pin shorter than 7 characters fails' (Has (Run (New-Facts @{ Repo = $feature; ExpectCommit = 'bbb' }) @{}) 'CM_EXPECT_COMMIT')
$ov = Run (New-Facts @{ Repo = $feature; AllowBranch = 'claude/x' }) @{}
Check 'CM_ALLOW_BRANCH passes and says so' ($ov.Problems.Count -eq 0 -and ($ov.Notes -join ' ') -like '*BRANCH OVERRIDE*')
$ahead = [pscustomobject]@{ Root = 'C:\r'; Branch = 'claude/x'; Sha = ('c' * 40); OriginMainSha = ('a' * 40); OriginBranchSha = ('b' * 40); Dirty = $false }
Check 'CM_ALLOW_BRANCH with unpushed commits fails' (Has (Run (New-Facts @{ Repo = $ahead; AllowBranch = 'claude/x' }) @{}) 'differs from origin/claude/x')
$unfetched = [pscustomobject]@{ Root = 'C:\r'; Branch = 'claude/x'; Sha = ('c' * 40); OriginMainSha = ('a' * 40); OriginBranchSha = $null; Dirty = $false }
Check 'CM_ALLOW_BRANCH without a fetched origin branch fails' (Has (Run (New-Facts @{ Repo = $unfetched; AllowBranch = 'claude/x' }) @{}) 'is not known here')
Check 'CM_ALLOW_BRANCH for another branch does not' (Has (Run (New-Facts @{ Repo = $feature; AllowBranch = 'other' }) @{}) "is on 'claude/x'")
$behind = [pscustomobject]@{ Root = 'C:\r'; Branch = 'main'; Sha = ('c' * 40); OriginMainSha = ('a' * 40); Dirty = $false }
Check 'local main differing from origin/main fails' (Has (Run (New-Facts @{ Repo = $behind }) @{}) 'differs from origin/main')
$dirty = [pscustomobject]@{ Root = 'C:\r'; Branch = 'main'; Sha = ('a' * 40); OriginMainSha = ('a' * 40); Dirty = $true }
Check 'dirty checkout fails' (Has (Run (New-Facts @{ Repo = $dirty }) @{}) 'uncommitted')
$unknown = [pscustomobject]@{ Root = 'C:\r'; Branch = 'main'; Sha = ('a' * 40); OriginMainSha = ('a' * 40); Dirty = $null }
Check 'unknown dirty state is a note, not a failure' ((Run (New-Facts @{ Repo = $unknown }) @{}).Problems.Count -eq 0)
Check 'no checkout fails when applying' (Has (Run (New-Facts @{ Repo = $null }) @{}) 'not inside a git checkout')
Check 'detached head without a pin fails' (Has (Run (New-Facts @{ Repo = [pscustomobject]@{ Root = 'C:\r'; Branch = $null; Sha = ('a' * 40); OriginMainSha = ('a' * 40); Dirty = $false } }) @{}) 'detached')

$off = Run (New-Facts @{ Repo = $null }) @{ RepoNotRequiredBecause = 'BACKUPHOST has no git' }
Check 'RepoNotRequiredBecause turns the repo check into a note' ($off.Problems.Count -eq 0 -and ($off.Notes -join ' ') -like '*repo check off*')

# ---- the wrong worktree ----
$other2 = [pscustomobject]@{ Root = 'C:\claude\wt\other'; Branch = 'main'; Sha = ('a' * 40); OriginMainSha = ('a' * 40); OriginBranchSha = ('a' * 40); Dirty = $false }
Check 'CM_EXPECT_REPO naming another worktree fails, also on a dry run' (Has (Run (New-Facts @{ Repo = $other2; ExpectRepo = 'C:\work\checkout' }) @{ Applying = $false }) 'wrong worktree')
$same = [pscustomobject]@{ Root = (Join-Path ([System.IO.Path]::GetTempPath()) 'wt-x'); Branch = 'main'; Sha = ('a' * 40); OriginMainSha = ('a' * 40); OriginBranchSha = ('a' * 40); Dirty = $false }
Check 'CM_EXPECT_REPO naming the same folder passes (trailing slash ignored)' ((Run (New-Facts @{ Repo = $same; ExpectRepo = ($same.Root + [System.IO.Path]::DirectorySeparatorChar) }) @{}).Problems.Count -eq 0)

# ---- signature policy ----
$policy = @{ MachineDomain = 'ad.example.test'; AllowedUsers = @('AD\adm_*'); Branch = 'main'; RequireSignature = $true; TrustedSignerThumbprints = @('AAAA') }
$signed = @{ 'C:\x\s.ps1' = [pscustomobject]@{ Status = 'Valid'; Thumbprint = 'AAAA' }; 'C:\x\m.psm1' = [pscustomobject]@{ Status = 'Valid'; Thumbprint = 'AAAA' } }
Check 'signed by the trusted certificate passes' ((Test-Guard -Facts (New-Facts @{ Signatures = $signed }) -Kind Changes -Applying $true -ExpectedHost 'ADMINHOST01' -Elevation Required -Policy $policy).Problems.Count -eq 0)
Check 'unsigned fails when signatures are required' ((Test-Guard -Facts (New-Facts) -Kind Changes -Applying $true -ExpectedHost 'ADMINHOST01' -Elevation Required -Policy $policy).Problems.Count -eq 2)
$other = @{ 'C:\x\s.ps1' = [pscustomobject]@{ Status = 'Valid'; Thumbprint = 'BBBB' }; 'C:\x\m.psm1' = [pscustomobject]@{ Status = 'Valid'; Thumbprint = 'AAAA' } }
Check 'a valid signature of an unknown certificate fails' (Has (Test-Guard -Facts (New-Facts @{ Signatures = $other }) -Kind Changes -Applying $true -ExpectedHost 'ADMINHOST01' -Elevation Required -Policy $policy) 'unknown certificate')
$empty = @{ MachineDomain = $null; AllowedUsers = @(); Branch = 'main'; RequireSignature = $false; TrustedSignerThumbprints = @() }
$e = Test-Guard -Facts (New-Facts @{ MachineDomain = 'whatever'; UserName = 'X\y' }) -Kind Changes -Applying $true -ExpectedHost 'ADMINHOST01' -Elevation Required -Policy $empty
Check 'an empty policy skips the domain and user checks with a note' ($e.Problems.Count -eq 0 -and ($e.Notes -join ' ') -like '*domain check skipped*' -and ($e.Notes -join ' ') -like '*user check skipped*')
Check 'an empty policy still enforces what the script names' ((Test-Guard -Facts (New-Facts @{ UserName = 'X\y' }) -Kind Changes -Applying $true -ExpectedHost 'ADMINHOST01' -Elevation Required -Policy $empty -User 'AD\adm_*').Problems.Count -eq 1)
Check 'the shipped policy does not require signatures yet' (-not (Get-GuardPolicy).RequireSignature)

# ---- repo reader on fake .git folders ----
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('guard-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $sha1 = '1' * 40; $sha2 = '2' * 40
    # a normal clone with a loose ref and a packed origin/main
    $a = Join-Path $tmp 'clone'
    New-Item -ItemType Directory -Force -Path (Join-Path $a '.git\refs\heads'), (Join-Path $a 'ops\admin') | Out-Null
    Set-Content (Join-Path $a '.git\HEAD') 'ref: refs/heads/main'
    Set-Content (Join-Path $a '.git\refs\heads\main') $sha1
    Set-Content (Join-Path $a '.git\packed-refs') "# pack-refs`n$sha2 refs/remotes/origin/main`n"
    $s = Get-RepoState -StartPath (Join-Path $a 'ops\admin')
    Check 'reader: branch, loose ref, packed origin/main' ($s.Branch -eq 'main' -and $s.Sha -eq $sha1 -and $s.OriginMainSha -eq $sha2 -and $s.OriginBranchSha -eq $sha2 -and $s.Root -eq $a)
    # packed branch + detached head
    Set-Content (Join-Path $a '.git\HEAD') $sha2
    $s = Get-RepoState -StartPath $a
    Check 'reader: detached head' ($null -eq $s.Branch -and $s.Sha -eq $sha2)
    # a worktree: .git is a file, HEAD in its own gitdir, refs in the common dir
    $wt = Join-Path $tmp 'wt'; New-Item -ItemType Directory -Force -Path $wt | Out-Null
    $wtGit = Join-Path $a '.git\worktrees\wt'; New-Item -ItemType Directory -Force -Path $wtGit | Out-Null
    Set-Content (Join-Path $wtGit 'HEAD') 'ref: refs/heads/feature'
    Set-Content (Join-Path $wtGit 'commondir') '..\..'
    Set-Content (Join-Path $a '.git\refs\heads\feature') $sha1
    Set-Content (Join-Path $wt '.git') ("gitdir: " + $wtGit)
    $s = Get-RepoState -StartPath $wt
    Check 'reader: worktree through gitdir and commondir' ($s.Branch -eq 'feature' -and $s.Sha -eq $sha1 -and $s.OriginMainSha -eq $sha2)
    $none = Join-Path $tmp 'nothing'; New-Item -ItemType Directory -Force -Path $none | Out-Null
    # a folder above the temp folder may be inside a checkout on a dev box; only assert when none is found
    $s = Get-RepoState -StartPath $none
    Check 'reader: a folder outside a checkout gives null (or the enclosing checkout of the temp folder)' ($null -eq $s -or $s.Root -ne $none)
}
finally { Remove-Item -Recurse -Force -LiteralPath $tmp -ErrorAction SilentlyContinue }

# ---- target guard and SIDs ----
Assert-Target -Name 'sqltest02.ad.example.test' -Allowed 'SQLTEST02'
Check 'target guard accepts an FQDN of the allowed server' $true
$threw = $false; try { Assert-Target -Name 'SQLPROD01' -Allowed 'SQLTEST02' } catch { $threw = $_.Exception.Message -like '*refused the target*' }
Check 'target guard refuses another server' $threw
Check 'well-known SIDs' ((Get-Sid -Role Administrators) -eq 'S-1-5-32-544' -and (Get-Sid -Role Users) -eq 'S-1-5-32-545' -and (Get-Sid -Role RemoteManagementUsers) -eq 'S-1-5-32-580' -and (Get-Sid -Role System) -eq 'S-1-5-18')

# ---- programs: install or upgrade must leave the tool working ----
$gitNow = Get-ToolVersion -Name git
Check 'a program that runs has a version line' ($null -ne $gitNow -and $gitNow -like '*git*')
Check 'a program that is not there has no version' ($null -eq (Get-ToolVersion -Name 'no-such-program-xyz'))
Check 'Assert-ToolWorks returns the version of a working program' ((Assert-ToolWorks -Name git 6>$null) -eq $gitNow)
$msg = ''
try { Assert-ToolWorks -Name 'no-such-program-xyz' -Before 'git version 2.51.0' } catch { $msg = $_.Exception.Message }
Check 'a vanished program is reported as removed by a rollback' ($msg -like '*worked before this step (git version 2.51.0)*rolled back*')
$msg = ''
try { Assert-ToolWorks -Name 'no-such-program-xyz' } catch { $msg = $_.Exception.Message }
Check 'a program that never ran is reported' ($msg -like "*'no-such-program-xyz' does not run after the install*")

# ---- guarded writes: live read, assert the expected state, write, read back ----
$script:wrote = 0
$script:reads = 0
$script:world = 'old'
$gw = { param([switch]$Apply, $Dependents = @(), $Done, $Verify, [int]$Attempts = 1, [scriptblock]$Write)
    if (-not $Write) { $Write = { param($s) $script:wrote++; $script:world = 'new' } }
    $a = @{ What = 'set X'; Read = { $script:reads++; $script:world }; Expect = { param($s) $s -eq 'old' }; DependentSteps = $Dependents; Write = $Write; Attempts = $Attempts; RetryDelaySeconds = 0; Apply = $Apply }
    $a.Verify = if ($Verify) { $Verify } else { { param($s) $s -eq 'new' } }
    if ($Done) { $a.Done = $Done }
    Invoke-GuardedWrite @a 6>$null 3>$null }
$r = & $gw -Apply
Check 'expected state found + Apply writes and reads back' ($r.Outcome -eq 'Written' -and $script:wrote -eq 1 -and $script:reads -eq 2 -and $r.State -eq 'new')
$script:world = 'old'; $script:reads = 0
$r = & $gw
Check 'dry run reads, writes nothing' ($r.Outcome -eq 'WouldWrite' -and $script:wrote -eq 1 -and $script:reads -eq 1)
$script:world = 'other'
$r = & $gw -Apply
Check 'state not found, nothing depends on it: skipped softly, nothing written' ($r.Outcome -eq 'Skipped' -and $script:wrote -eq 1 -and (Get-SkippedWrites).Count -eq 1)
$r = & $gw
Check 'a dry run also shows the mismatch' ($r.Outcome -eq 'Skipped')
$msg = ''
try { & $gw -Apply -Dependents @('step B', 'step C') } catch { $msg = $_.Exception.Message }
Check 'state not found, later steps depend on it: hard stop naming them' ($msg -like '*expected state not found*found: other*Nothing was written*step B, step C*' -and $script:wrote -eq 1)
$script:world = 'new'
$r = & $gw -Apply -Done { param($s) $s -eq 'new' }
Check 'already in the wanted state: nothing written' ($r.Outcome -eq 'Already' -and $script:wrote -eq 1)
$script:world = 'old'
$msg = ''
try { & $gw -Apply -Write { param($s) $script:wrote++ } } catch { $msg = $_.Exception.Message }
Check 'a write that does not take is a hard stop' ($msg -like '*did not take: read back old*unknown*' -and $script:wrote -eq 2)
$script:world = 'old'; $script:reads = 0; $script:wrote = 0
$flaky = { param($s) $script:wrote++; if ($script:wrote -ge 2) { $script:world = 'new' } }
$r = & $gw -Apply -Attempts 3 -Write $flaky
Check 'a retry reads again before each attempt' ($r.Outcome -eq 'Written' -and $script:wrote -eq 2 -and $script:reads -eq 4)
$r = Invoke-GuardedWrite -What 'read fails' -Read { throw 'boom' } -Expect { $true } -Verify { $true } -DependentSteps @() -Write { $script:wrote++ } -Apply 6>$null 3>$null
Check 'a failing read counts as state not found' ($r.Outcome -eq 'Skipped' -and $script:wrote -eq 2)
$msg = ''
try { Invoke-GuardedWrite -What 'no readback' -Read { 1 } -Expect { $true } -DependentSteps @() -Write { } -Apply 6>$null } catch { $msg = $_.Exception.Message }
Check 'a write without -Verify or -Done is a script bug' ($msg -like '*-Verify (or -Done) is required*')
Check 'the summary exit code is 3 when a write was skipped' ((Write-RunSummary 6>$null) -eq 3)
Check 'a state is described as compact JSON, a string as itself, nothing as (nothing)' ((Format-State ([pscustomobject]@{ Tag = 'Pilot'; N = 2 })) -eq '{"Tag":"Pilot","N":2}' -and (Format-State ' x ') -eq 'x' -and (Format-State $null) -eq '(nothing)' -and (Format-State @('a', 'b')) -eq '["a","b"]')
$threw = $false; try { Assert-Tenant -ExpectedTenantId 'TENANT-ONE' -Current { 'bbbb-2222' } } catch { $threw = $_.Exception.Message -like '*tenant id is*bbbb-2222*not what this script expects*' }
Check 'a wrong tenant stops the script' $threw
$threw = $false; try { Assert-Tenant -ExpectedTenantId 'tenant-one' -Current { 'TENANT-ONE' } } catch { $threw = $true }
Check 'the right tenant passes (case-insensitive)' (-not $threw)

# ---- a throw stops the calling script even when the script says "Continue" ----
$probe = Join-Path ([System.IO.Path]::GetTempPath()) ('guard-probe-' + [guid]::NewGuid().ToString('N') + '.ps1')
$exe = (Get-Process -Id $PID).Path
if ((Split-Path -Leaf $exe) -like 'dotnet*') { $exe = (Get-Command pwsh).Source }      # pwsh installed as a dotnet tool
try {
    Set-Content $probe @"
`$ErrorActionPreference = 'Continue'
Import-Module '$Module' -Force -ErrorAction Stop
Assert-Target -Name 'WRONG' -Allowed 'RIGHT'
Write-Output 'AFTER-THE-GUARD'
"@
    $ErrorActionPreference = 'Continue'      # Windows PowerShell 5.1 turns the child's stderr into a terminating error under 'Stop'
    $out = (& $exe -NoProfile -File $probe 2>&1 | Out-String) -replace '\s+', ' '
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Check 'a refused guard stops the script before the next line' ($out -notlike '*AFTER-THE-GUARD*' -and $code -ne 0)
    Check 'the refusal text reaches the screen' ($out -like '*GUARD refused*')
}
finally { Remove-Item -Force -LiteralPath $probe -ErrorAction SilentlyContinue }

Write-Host "$($count - $failed) of $count passed"
if ($failed -gt 0) { exit 1 }
