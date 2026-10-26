# Doppler agent: package, install, VM proof (lane L4b goal 3)

The agent is `CM.BlackHole/agent` (`cm-doppler-agent.exe`): a Windows service (LocalSystem) that runs Doppler's `AgentLoop` with the
Windows TPM (`PcpTpm`, keys in the Platform Crypto Provider, non-exportable) and Milky Way's TLS transport. Its logic is tested on
Linux (`tools/sandbox-pair/tests/AgentLoopPairTests.cs`: the real gate over an in-memory pipe and over real TCP; `CM.BlackHole/tests`:
the TPM command layer against swtpm). What only a Windows machine with a TPM can show is proved on a test VM, below.

| File | What it is |
|---|---|
| `package.ps1` | publishes the agent self-contained (win-x64, one exe) and builds `install.intunewin` with IntuneWinAppUtil; prints the agent's SHA-256 and the Intune command lines. Check / dry run / `-Apply` |
| `install.ps1` | the Intune install command (SYSTEM): program folder, data folder with a SYSTEM+Administrators ACL, NetBIOS off (old values kept), the service, the detection marker. Dry run / `-Apply`, idempotent |
| `uninstall.ps1` | the Intune uninstall command: service gone, TPM keys deleted (`forget`), NetBIOS restored, folders and marker gone. Dry run / `-Apply`, idempotent |
| `intune-like.ps1` | runs the install or uninstall command line on a VM as Intune does (SYSTEM, package folder, 32-bit PowerShell), without Intune |
| `testhost/` | `cm-doppler-testhost`: Milky Way's gate and Doppler with a throw-away sandbox CA on one TCP port, in memory, with a console for `plan`, `revoke`, `status`; writes the test package's `agent.json` |

## The agent's commands

```
cm-doppler-agent            the service (installed by install.ps1)
cm-doppler-agent run        the same loop in a console
cm-doppler-agent diagnose   every enrolment step against this TPM, with keys of its own; prints the endorsement key id
cm-doppler-agent status     endorsement key id, agent hash, enrolment state (run as SYSTEM: the keys are SYSTEM's)
cm-doppler-agent forget     deletes the agent's and the diagnose TPM keys (the uninstaller runs it)
```

The service logs to the Windows Application event log (source `cm-doppler-agent`), one line each time the outcome changes:
outcomes, refusal codes and TPM or provider status codes only, never a key, token, certificate or the knock secret.

## Intune (Win32 app)

| Field | Value |
|---|---|
| Install command | `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Apply` |
| Uninstall command | `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1 -Apply` |
| Install behaviour | System |
| Return codes | 0 success, 1 failed |
| Detection | registry `HKEY_LOCAL_MACHINE\SOFTWARE\CM\DopplerAgent`, value `PackageSha256`, string equals the hash `package.ps1` prints, 64-bit key |
| Requirements | Windows 10/11 x64, TPM 2.0 |

The package carries `agent.json`, which holds the knock secret; a `.intunewin` carries its own decryption key, so the package is as
secret as `agent.json` and is kept outside the repository. Uploading and assigning it in Intune is **gated** (the owner's words first).

## The VM proof

Acceptance: on a clean Windows test VM with a vTPM, installed with exactly the Intune command line, the agent enrols, attests with
the TPM and its heartbeat arrives verified at the gate; an unenrolled VM is refused.

Gated: step 1 (a VM on shared infrastructure). Everything else runs on the test VM and on the dev host's thread ports (5200+).

1. **(gated)** A Generation 2 Windows 11 or Windows Server 2025 VM with Secure Boot and a vTPM (`Enable-VMTPM`), BitLocker on
   the system drive (Doppler refuses an enrolment without it, for every role), able to reach the test host's port. `test-vm.ps1`
   does it on one Hyper-V host the owner names (the owner, 2026-10-25: the DR host first, then the cluster, the host with the lowest
   load; the operations master ranks them by load). From the admin machine, as a Hyper-V administrator of the host:
   `test-vm.ps1 -HostName <host> -VmRoot <folder on its data volume> -SwitchName <switch> -IsoPath <Windows 11 Enterprise evaluation ISO on the host>`
   (Check, read only), then the same with `-Action Create` (WOULD lines) and `-Action Create -Apply`: a workgroup VM `CM-DOPPLER-T01`,
   8 GB fixed, 4 vCPU, 80 GB, Secure Boot, vTPM, unattended setup with one local administrator whose generated password lives only
   in a DPAPI file of the operator. After 15 to 30 minutes `-Action Prepare -Apply` turns BitLocker on (TPM protector) and detaches
   the ISOs. Step 4 and 5 can then use `-Action InstallAgent -PackageFolder <output>\source -Apply` (copies the package and
   `intune-like.ps1` in over PowerShell Direct and runs the Intune command line as SYSTEM). `-Action Remove -Apply` deletes it all.
2. On the dev host: `dotnet run --project tools/doppler-agent/testhost -- --gate-host <name the VM uses> --listen <address>
   --port 5200 --agent-config <folder outside the repo>\agent.json`. It writes the test package's `agent.json` (this run's knock
   secret, the sandbox root) and keeps running; leave it open.
3. `package.ps1 -ExpectedHost <this-computer> -AgentConfig <that agent.json> -OutputFolder <folder outside the repo> -IntuneWinAppUtil <path> -Apply`. In the
   test host: `allow <the SHA-256 package.ps1 printed>`.
4. Copy `<output>\source` to the VM. In an elevated PowerShell there: `cm-doppler-agent.exe diagnose` from that folder: every line
   `ok`; it prints the endorsement key id.
5. Unplanned first: `intune-like.ps1 -ExpectedHost <this-computer> -PackageFolder <source> -Apply` installs (exit 0); the test host prints a refused enrolment
   audit line and `status` shows no register entry (the unenrolled VM is refused).
6. In the test host: `plan <endorsement key id>`. Within the agent's backoff (5, 10, 20 minutes; `Restart-Service CmDopplerAgent` retries at once) the host prints the
   enrolment audit line; `status` shows the register entry and a heartbeat with `verified True`, sequence counting up each minute.
7. In the test host: `revoke <subject>`. The next heartbeat is refused at the gate (`status`: the sequence stops, a refusal counts).
8. `intune-like.ps1 -ExpectedHost <this-computer> -PackageFolder <source> -Action Uninstall -Apply`: exit 0; the service, the folders, the marker and the TPM keys
   are gone, NetBIOS is as before. A second uninstall and a second install each exit 0 (idempotent).

What to report back: the `diagnose` output, the install and uninstall output, and the test host's `status` after steps 5, 6 and 7.
The test host keeps everything in memory: if it restarts, run `cm-doppler-agent forget` as SYSTEM on the VM (or uninstall) and plan again.
