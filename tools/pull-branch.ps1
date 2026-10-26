<#
.SYNOPSIS
Brings one checkout of the repository up to date (git fetch, then a fast-forward only) from a normal, NOT elevated Windows PowerShell. Dry run until -Apply.

.DESCRIPTION
Why this script exists: git refuses a folder that belongs to another account, and an elevated shell is a different token; pulling in an elevated shell (the owner, 2026-10-25) leaves .git files owned by the administrator account and breaks the checkout for every later normal session. So the pull has its own guarded step:
the guard fails at once when the computer name is not -ExpectedHost, when the session IS elevated, when the account is not an allowed account, or when this is not Windows PowerShell 5.1.
It updates the checkout this script lives in (the one the absolute path in the command block names; $env:CM_EXPECT_REPO makes the guard check that folder too) and nothing else. It refuses a checkout with uncommitted changes to tracked files, a checkout that is not on -Branch, and a history that is not a fast-forward; it never merges, resets, stashes or force-pulls.
Dry run (default) only asks the remote for its tip (git ls-remote) and prints the WOULD lines. Idempotent: when the checkout is already at the tip it says so and stops.

.PARAMETER ExpectedHost
The computer the command block says it runs on. The guard refuses any other. Mandatory, so the hand-off line "host . context" and the script assert the same thing.

.PARAMETER Branch
The branch to bring up to date. Default main. The checkout must already be on it (git switch is a separate, visible step).

.PARAMETER Apply
Fetch and fast-forward for real.

.EXAMPLE
$env:CM_EXPECT_REPO = 'C:\path\to\checkout'
& 'C:\path\to\checkout\tools\pull-branch.ps1' -ExpectedHost '<this computer>'          # dry run

.EXAMPLE
& 'C:\path\to\checkout\tools\pull-branch.ps1' -ExpectedHost '<this computer>' -Apply
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExpectedHost,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._/-]*$')][string]$Branch = 'main',
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
# The guard first (docs/process/owner-scripts.md): wrong machine, elevated shell or wrong account stops the script here. It does not require main or a clean checkout: updating the checkout is the job of this script, which checks both itself below.
Import-Module (Join-Path $PSScriptRoot 'lib\Guard.psm1') -Force -ErrorAction Stop
$facts = Assert-Script -ScriptPath $PSCommandPath -Kind Changes -Applying:$Apply -ExpectedHost $ExpectedHost -Elevation Forbidden -RepoNotRequiredBecause 'this script is the step that updates the checkout; it checks branch, clean tree and fast-forward itself'

if (-not $facts.Repo) { throw 'This script is not inside a git checkout; nothing to pull.' }
$root = $facts.Repo.Root

function Invoke-Git {
    # Runs git in the checkout; returns the output lines (callers wrap the call in @() so one line stays an array) and stops on a non-zero exit code.
    param([Parameter(Mandatory)][string[]]$GitArgs)
    # Windows PowerShell 5.1 turns git's stderr progress text ("From ...") into a terminating error under 'Stop'; the exit code is the truth.
    $ErrorActionPreference = 'Continue'
    $out = & git -C $root @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw ("git {0} failed (exit {1}): {2}" -f ($GitArgs -join ' '), $LASTEXITCODE, (($out | ForEach-Object { "$_" }) -join ' ')) }
    return @($out | ForEach-Object { "$_" })
}

$current = @(Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD'))[0]
if ($current -ne $Branch) { throw "The checkout $root is on '$current', not '$Branch'. Nothing was changed. Switch it first (git switch $Branch) or name the branch with -Branch." }
$dirty = @(Invoke-Git @('status', '--porcelain', '--untracked-files=no'))
if ($dirty.Count -gt 0) { throw "The checkout $root has uncommitted changes to tracked files; a pull would mix them with new work. Nothing was changed.`n  $($dirty -join "`n  ")" }

$localSha = @(Invoke-Git @('rev-parse', 'HEAD'))[0]
if (-not $Apply) {
    $remote = @(Invoke-Git @('ls-remote', 'origin', "refs/heads/$Branch"))
    if ($remote.Count -eq 0) { throw "origin has no branch '$Branch'." }
    $remoteSha = ($remote[0] -split '\s+')[0]
    if ($remoteSha -eq $localSha) { Write-Host "OK    $root is already at origin/$Branch ($($localSha.Substring(0, 7)))." -ForegroundColor Green }
    else { Write-Host "WOULD fetch origin $Branch and fast-forward $root from $($localSha.Substring(0, 7)) to $($remoteSha.Substring(0, 7)) (add -Apply)." -ForegroundColor Cyan }
    return
}

$null = Invoke-Git @('fetch', 'origin', $Branch)
$tipSha = @(Invoke-Git @('rev-parse', "origin/$Branch"))[0]
if ($tipSha -eq $localSha) { Write-Host "OK    $root is already at origin/$Branch ($($localSha.Substring(0, 7))). Nothing to do." -ForegroundColor Green; return }
$null = Invoke-Git @('merge', '--ff-only', "origin/$Branch")
Write-Host "DONE  $root is now at $($tipSha.Substring(0, 7)) (was $($localSha.Substring(0, 7)))." -ForegroundColor Green
