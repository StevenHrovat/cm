# The one gate (DMP chapter 10): the dmp group first (so a breach is named even when the tree no longer builds), then
# the shape of the coordinator state dumps, then build, the im group (Milky Way's verifier, DMP chapters 04 and 05), the pair group, then the
# other tests, then the gravity group (CM.BlackHole/docs/gravity.md). Exit 0 or 1. Same steps as tools/verify.
# Optional: $env:CM_DMP_SCRUB_LIST = file with one forbidden value per line (supplied by the pipeline, never committed).
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
function Step($name, $block) { Write-Host "== $name"; & $block; if ($LASTEXITCODE -ne 0) { Write-Host "verify: FAILED at $name"; exit 1 } }
Step 'dmp'   { dotnet build tools/dmp/src/Cm.Dmp.Check.csproj --nologo -v quiet; if ($LASTEXITCODE -eq 0) { dotnet run --project tools/dmp/src/Cm.Dmp.Check.csproj --no-build -- check . } }
Step 'scripts' {
    $exe = (Get-Command powershell.exe, pwsh -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    if (-not $exe) { Write-Host 'scripts: skipped (no PowerShell found)'; $global:LASTEXITCODE = 0; return }
    & $exe -NoProfile -File tools/lib/tests/Guard.Tests.ps1
    if ($LASTEXITCODE -ne 0) { return }
    & $exe -NoProfile -Command "& tools/lib/tests/OwnerScripts.ParseCheck.ps1 -Files (Get-ChildItem tools, shapes -Recurse -Include *.ps1,*.psm1 | % FullName)"
}
Step 'build' { dotnet build CM.slnx --nologo -v quiet }
Step 'im'    { foreach ($p in 'cm.os/tests/cm.os.Tests.csproj', 'CM.MilkyWay/tests/CM.MilkyWay.Tests.csproj') { dotnet test $p --nologo --no-build -v quiet --logger 'console;verbosity=minimal' --filter 'Category=im'; if ($LASTEXITCODE -ne 0) { break } } }
Step 'pair'  { dotnet test tools/sandbox-pair/tests/Cm.SandboxPair.Tests.csproj --nologo --no-build -v quiet --logger 'console;verbosity=minimal' --filter 'Category=pair' }
Step 'test'  { dotnet test CM.slnx --nologo --no-build -v quiet --logger 'console;verbosity=minimal' --filter 'Category!=im&Category!=pair' }
Step 'gravity' { foreach ($c in 'check', 'conform') { dotnet run --project tools/gravity/src/Cm.Gravity.csproj --no-build -- $c .; if ($LASTEXITCODE -ne 0) { break } } }
Write-Host 'verify: OK'
