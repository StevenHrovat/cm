<#
.SYNOPSIS
Builds the Doppler agent's Intune Win32 package (.intunewin). Check by default, dry run without -Apply.

.DESCRIPTION
Security context: defensive security engineering under the charter in CLAUDE.md (CM.BlackHole, Doppler, lane L4b goal 3).

Runs on an admin workstation or the dev host with the .NET 10 SDK and Microsoft's IntuneWinAppUtil.exe. It:
  1. publishes CM.BlackHole/agent self-contained for win-x64 as one cm-doppler-agent.exe,
  2. puts the exe, install.ps1, uninstall.ps1 and the agent.json you name into a source folder below -OutputFolder,
  3. runs IntuneWinAppUtil on that folder (setup file install.ps1),
  4. prints the agent's SHA-256 (Doppler's AllowedAgentPackages and the detection rule need it) and the Intune command lines.

agent.json is written outside the repository: by cm-doppler-testhost for the VM proof, from the settings store for a real gate. It
holds the knock secret. A .intunewin carries its own decryption key, so the package and the source folder are as secret as agent.json:
-OutputFolder must be outside the repository, and the script never prints agent.json's content.

Uploading or assigning the package in Intune is GATED (the owner's own words first) and is not done by this script.

Idempotent: a second run with the same inputs rebuilds the package; the printed hash is the one to use.

.PARAMETER Apply
Actually publish and package. Without it the script checks the inputs and prints WOULD lines.

.PARAMETER AgentConfig
The agent.json the package carries.

.PARAMETER OutputFolder
Where the source folder and the .intunewin go. Must be outside the repository.

.PARAMETER IntuneWinAppUtil
The path of IntuneWinAppUtil.exe (Microsoft Win32 Content Prep Tool).

.PARAMETER Version
The agent version (assembly and file version). Default 0.1.0.

.EXAMPLE
.\package.ps1 -ExpectedHost <this-computer> -AgentConfig <agent.json> -OutputFolder <folder> -IntuneWinAppUtil <path>          # check and dry run

.EXAMPLE
.\package.ps1 -ExpectedHost <this-computer> -AgentConfig <agent.json> -OutputFolder <folder> -IntuneWinAppUtil <path> -Apply
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$AgentConfig = '',
    [string]$OutputFolder = '',
    [string]$IntuneWinAppUtil = '',
    [string]$Version = '0.1.0',
    # The computer this script is meant for; it stops at once on any other (docs/process/owner-scripts.md). The operating organisation's host name is never in the repository.
    [Parameter(Mandatory)][string]$ExpectedHost
)

$ErrorActionPreference = 'Stop'
# The guard first (docs/process/owner-scripts.md): wrong machine, session or edition stops the script here, before anything else runs.
Import-Module (Join-Path $PSScriptRoot '..\lib\Guard.psm1') -Force -ErrorAction Stop
$null = Assert-Script -ScriptPath $PSCommandPath -Kind Changes -Applying:$Apply -ExpectedHost $ExpectedHost -Elevation Either

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Project = Join-Path $Repo 'CM.BlackHole\agent\CM.BlackHole.Agent.csproj'

function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Line([string]$Text, [string]$Color = 'Gray') { Write-Host "   $Text" -ForegroundColor $Color }
function Write-Ok([string]$Text) { Write-Line "OK       $Text" 'Green' }
function Write-Missing([string]$Text) { Write-Line "MISSING  $Text" 'Yellow' }
function Write-Would([string]$Text) { Write-Line "WOULD    $Text" 'Yellow' }
function Write-Did([string]$Text) { Write-Line "DID      $Text" 'Green' }

Write-Step 'Check'
$ready = $true
if (Get-Command dotnet -ErrorAction SilentlyContinue) { Write-Ok "dotnet $(& dotnet --version)" } else { Write-Missing 'the .NET 10 SDK (dotnet)'; $ready = $false }
if ($IntuneWinAppUtil -and (Test-Path -LiteralPath $IntuneWinAppUtil)) { Write-Ok 'IntuneWinAppUtil.exe' } else { Write-Missing '-IntuneWinAppUtil <path of IntuneWinAppUtil.exe>'; $ready = $false }
if (-not $OutputFolder) { Write-Missing '-OutputFolder (outside the repository)'; $ready = $false }
else {
    $out = [IO.Path]::GetFullPath($OutputFolder)
    if ($out.StartsWith($Repo, [StringComparison]::OrdinalIgnoreCase)) { Write-Missing "-OutputFolder is inside the repository ($Repo); the package carries the knock secret"; $ready = $false }
    else { Write-Ok "output folder $out" }
}
if (-not ($AgentConfig -and (Test-Path -LiteralPath $AgentConfig))) { Write-Missing '-AgentConfig <agent.json>'; $ready = $false }
else {
    # Check the shape without printing any value.
    $config = Get-Content -LiteralPath $AgentConfig -Raw | ConvertFrom-Json
    $problems = @()
    if ($config.version -ne 1) { $problems += 'version is not 1' }
    if (-not $config.gateHost) { $problems += 'no gateHost' }
    if (-not ($config.gatePort -ge 1 -and $config.gatePort -le 65535)) { $problems += 'no gatePort' }
    try { if ([Convert]::FromBase64String([string]$config.knockSecret).Length -lt 32) { $problems += 'knockSecret shorter than 32 bytes' } } catch { $problems += 'knockSecret is not base64' }
    if (@($config.trustRoots).Count -lt 1) { $problems += 'no trustRoots' }
    if ($problems) { Write-Missing "agent.json: $($problems -join ', ')"; $ready = $false } else { Write-Ok 'agent.json has gateHost, gatePort, knockSecret, trustRoots (values not shown)' }
}
if (-not $ready) { Write-Line 'Fill in what is MISSING, then run again.' 'Yellow'; exit 1 }

$source = Join-Path $out 'source'
$publish = Join-Path $out 'publish'
Write-Step 'Package'
if (-not $Apply) {
    Write-Would "dotnet publish $Project -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:Version=$Version -o $publish"
    Write-Would "copy cm-doppler-agent.exe, install.ps1, uninstall.ps1 and agent.json into $source"
    Write-Would "IntuneWinAppUtil -c $source -s install.ps1 -o $out -q"
    Write-Line 'Dry run. Add -Apply to build the package.' 'Yellow'
    exit 0
}

& dotnet publish $Project -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -o $publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish returned $LASTEXITCODE." }
Write-Did "published cm-doppler-agent.exe $Version"
if (Test-Path -LiteralPath $source) { Remove-Item -LiteralPath $source -Recurse -Force }
New-Item -ItemType Directory -Force -Path $source | Out-Null
Copy-Item -LiteralPath (Join-Path $publish 'cm-doppler-agent.exe') -Destination $source
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1'), (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $source
Copy-Item -LiteralPath $AgentConfig -Destination (Join-Path $source 'agent.json')
Write-Did "source folder $source"
& $IntuneWinAppUtil -c $source -s install.ps1 -o $out -q
if ($LASTEXITCODE -ne 0) { throw "IntuneWinAppUtil returned $LASTEXITCODE." }
Write-Did "package $(Join-Path $out 'install.intunewin')"

$hash = (Get-FileHash -LiteralPath (Join-Path $source 'cm-doppler-agent.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Step 'For Doppler and Intune'
Write-Line "agent package SHA-256 (Doppler AllowedAgentPackages): $hash"
Write-Line 'Install command:   %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Apply'
Write-Line 'Uninstall command: %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1 -Apply'
Write-Line 'Install behaviour: System. Return codes: 0 success, 1 failed.'
Write-Line "Detection rule: registry HKEY_LOCAL_MACHINE\SOFTWARE\CM\DopplerAgent, value PackageSha256, string equals $hash (64-bit)"
Write-Line 'Upload and assignment in Intune are gated: the owner''s own words first.'
exit 0
