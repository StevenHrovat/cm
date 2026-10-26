# Owner scripts: the safe-script standard

An **owner script** is any PowerShell script an administrator runs by hand: everything under `tools/` and `shapes/` that ends in `.ps1` (a shape script imports the guard as `..\..\tools\lib\Guard.psm1`). DMP chapter 10 ("Scripts an administrator runs") makes the standard a rule;
`DMP-011` in `tools/dmp` fails the PR check for a script that breaks it, and the `scripts` step of `tools/verify` runs the guard's self-test and the Windows PowerShell 5.1 parse of every script.
The guard is `tools/lib/Guard.psm1`; its policy block at the top is empty here, so a deployment sets it.

**Roles, not values (DMP-006).** The repository names no machine. The expected host is a mandatory `[string]$ExpectedHost` parameter of the script, so the command block that the administrator runs supplies it, and the bold line above that block says the same.

## The header every script starts with

Nothing but the help comment, `[CmdletBinding()]`, `param(...)`, `$ErrorActionPreference` and `Set-StrictMode` may come before it. The import path is relative to the script (`tools/x/a.ps1` -> `'..\lib\Guard.psm1'`, `tools/a.ps1` -> `'lib\Guard.psm1'`):

```powershell
param([Parameter(Mandatory)][string]$ExpectedHost, [switch]$Apply)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\lib\Guard.psm1') -Force -ErrorAction Stop
$null = Assert-Script -ScriptPath $PSCommandPath -Kind Changes -Applying:$Apply -ExpectedHost $ExpectedHost -Elevation Required
```

- `-Kind ReadOnly | Changes`: whether the script changes anything. A `Changes` script has a dry run: `[switch]$Apply` or `[CmdletBinding(SupportsShouldProcess)]` (`-Applying:(-not $WhatIfPreference)`).
- `-ExpectedHost $ExpectedHost` (with the mandatory parameter), or `-AnyHostBecause '<reason>'` for a script that really runs anywhere.
- `-Elevation Required | Forbidden | Either`: asked for by name.
- Optional: `-User`, `-MachineDomain` (the policy is empty here, so both are skipped with a note unless the script names them), `-Edition Any`, `-RepoNotRequiredBecause '<reason>'`.

## Every command block states its host and context

Every command block handed to the administrator carries **one bold line directly above it**: host, session (normal or elevated) and account, for example
**`<computer> · normal Windows PowerShell 5.1 (not elevated) · <admin account>`**. The first act of the script asserts exactly that line: host = `-ExpectedHost`, normal or elevated = `-Elevation Forbidden` or `Required`, account = `-User` / the policy.
A step has a script or it is not handed out: no loose `git` line. The pull of a checkout is **`tools/pull-branch.ps1 -ExpectedHost <computer> [-Branch main] [-Apply]`**: `-Elevation Forbidden`, refuses a dirty checkout, another branch and anything that is not a fast-forward (an elevated pull leaves files owned by the administrator token and breaks the checkout for normal sessions).
A hand-off that needs a pull and then an elevated step is two blocks, each with its own bold line.

## Command blocks use absolute paths

The administrator hops between threads and may stand in the wrong worktree: every block names the script by its absolute path, never `.\tools\...`, and may set `$env:CM_EXPECT_REPO = '<checkout folder>'`; the guard then refuses a script from any other folder, also on a dry run. The `Guard OK` line prints the full script path.

## What the guard checks

One error lists every problem; nothing has run yet (a `throw` ends the script even under `$ErrorActionPreference = 'Continue'`). Host, machine domain (policy), account (policy), elevation (by SID, German Windows safe), edition (5.1), worktree (`CM_EXPECT_REPO`) always;
the repo (clean, `main`, equal to `origin/main` as last fetched, or pinned with `$env:CM_EXPECT_COMMIT = '<sha>'`, or `$env:CM_ALLOW_BRANCH = '<branch>'` for an unmerged fix) only when the script applies. The repo state is read from `.git` itself, so it also works in an elevated shell. The pins are set by the person on the command line; the lint refuses a script that sets them.
The signature check (pinned certificate) is written and off; it combines with `AllSigned` like this: sign after merge from `main`, pin the thumbprint, then `AllSigned` machine by machine. No machine's policy is changed by this repository.

## A name must not lie, and an install proves itself

An upgrade run under the name `-Check` failed in a normal shell and its installer rollback removed the old Git from a laptop. So `DMP-011` enforces two rules:
1. **`-Kind ReadOnly`, `-Check`, `-WhatIf`, a dry run and a stage named Check/Probe/Verify change nothing.** A `ReadOnly` script that calls a command which changes a system (`Set-`/`New-`/`Remove-`/`Add-`/`Install-`/`Enable-`/`Grant-`/`Register-`/`Start-`/`Stop-`..., `winget install`, `git commit|push|pull|checkout`, `sc create`, `reg add`...) fails; a call that only touches the script's own scratch file ends the line with `# lint-ok: mutates <why>`.
2. **Every install or upgrade sits behind the apply switch** (inside `if ($Apply)`, `ShouldProcess` or `Invoke-Change`) **and the program is run afterwards:** `$before = Get-ToolVersion -Name x`, install, `Assert-ToolWorks -Name x -Before $before`. If it worked before and does not now, the script stops with "the installer probably rolled back". Modules and Windows features are gated but need no proof. A line that is the apply step of a script whose dry run returned above it ends with `# lint-ok: install <why>`.

## Windows groups by SID, never by name

Windows localises its group names (`Administratoren`, `Benutzer` on a German system). Use `Get-Sid -Role Administrators` (`S-1-5-32-544`), `[Security.Principal.SecurityIdentifier]`, `icacls '*S-1-5-32-544:(OI)(CI)F'`. `DMP-011` fails a quoted English or German group name, `BUILTIN\...`, `NT AUTHORITY\...`, `net localgroup` and `icacls Name:(...)`; a label that only looks like a principal ends with `# lint-ok: principal-name <why>`.

## Before a script reaches the administrator

1. It parses under Windows PowerShell 5.1 (`tools/lib/tests/OwnerScripts.ParseCheck.ps1`: parse errors, PowerShell-7-only syntax, bare `[type]$x` command arguments); `tools/verify` runs it when PowerShell is installed.
2. The guard self-test and `dotnet test tools/dmp/tests` are green.
3. The thread has run it once, dry, on the machine it is for , and quotes the `Guard OK` line, or the hand-off says "untested" in its first line.
4. The block has its bold line and an absolute path.

## Every write reads first (constitution C15, DMP-012)

"A write first reads the live state it will change and asserts what it expects: absent for a create, a named value for an overwrite."
The tool is `Invoke-GuardedWrite` in the guard module:

```powershell
$null = Assert-Tenant -ExpectedTenantId $TenantId -Service Graph           # right after Connect-MgGraph
Invoke-GuardedWrite -What 'Set the group tag on device D' `
    -Read   { (Get-AutopilotDevice -Serial $serial).GroupTag } `                  # live, from the tenant, on every attempt
    -Expect { param($s) -not $s } `                                               # the state that may be overwritten
    -Done   { param($s) $s -eq 'Pilot' } `                                        # already wanted: nothing written
    -Verify { param($s) $s -eq 'Pilot' } `                                        # read back after the write
    -DependentSteps 'Assign the profile', 'Enrol the device' `                    # @() when nothing later needs it
    -Attempts 3 `                                                                 # each attempt reads again first
    -Write  { param($s) Set-AutopilotDevice -Serial $serial -GroupTag 'Pilot' } `
    -Apply:$Apply
exit (Write-RunSummary)                                                     # lists skipped writes, exit code 3 if any
```

Outcomes: `Written`, `WouldWrite` (dry run, with the state found), `Already`, `Skipped` (soft, no dependents). A mismatch with dependents
stops the script and names them; a write that does not read back as wanted always stops it ("the state of the target is unknown now").
A read that throws counts as "state not found". The lint (DMP-012) refuses a new changing command outside a `-Write` block, a guarded
write without `-Read`, `-Expect`, `-DependentSteps` and `-Verify`/`-Done`, a `-Read` block that calls no command, and a Graph or
Exchange connection without `Assert-Tenant`. Older writes are a per-script count in `tools/lib/unguarded-writes.txt`; convert a
write when you next touch the script and lower the number.

## Exemptions (ratchet)

`tools/lib/script-exemptions.txt` lists scripts that cannot call the guard (they run unattended on enrolled devices, or bootstrap a machine before the repository exists), with the reason. The list only goes down (`DMP-011` fails on a stale line and a larger count).
