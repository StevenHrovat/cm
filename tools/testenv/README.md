# Test environment on a Windows machine (for agents, and for the human who points them here)

**Goal:** from a plain Windows 10/11 machine to runners that execute your repositories' CI, in minutes, safely, with nothing you cannot undo.

**For the human:** you own the machine and decide. You say which repositories get runners and how much of the machine CI may use. You run the one elevated step (installing WSL). Everything else the agent does, and every change is a dry run first.

**For the agent:** do these steps in order. Do not invent steps, and do not route around a block: a refused command is a question for the owner (say what was blocked, what you would do next, and stop that action).

## What you get
- WSL2 with a hardened Ubuntu 24.04: no Windows drives mounted, no way to call Windows programs, so a workflow cannot reach the owner's Windows account.
- N GitHub Actions runners per repository, label `wsl`, as systemd services under one local user (`ci-runner`).
- A scheduled task that keeps the distro running after logon (WSL stops an idle distro, and with it every runner).
- A cap on memory and cores (`.wslconfig`), so the machine stays usable when CI saturates it.

## Steps
1. **Know the machine.** Computer name (`$env:COMPUTERNAME`), user, is `gh` signed in (`gh auth status`), is the repository admin right there. Windows PowerShell 5.1 (`powershell.exe`), not `pwsh`: the guard requires it.
2. **Check (read only):**
   `& '<checkout>\tools\testenv\setup-test-env.ps1' -ExpectedHost '<COMPUTERNAME>' -Repository 'owner/a','owner/b'`
3. **Dry run of what is missing:** same command with `-Stage All`. Read the WOULD lines to the owner in plain words.
4. **Owner runs the elevated part once** (an agent cannot elevate): `-Stage Wsl -Apply` in "Run as administrator". Reboot if it says so, then run it again until the distro exists.
5. **Everything else:** `-Stage All -Apply` in a normal shell. The registration tokens come from `gh`, live only in memory, and are never printed or stored.
6. **Prove it twice:** run the Check again. Every line must say it is done; run `-Stage All -Apply` a second time, it must say "already" everywhere. Then see the runners `online` on GitHub (Settings, Actions, Runners) and re-run one real workflow on them.

## Traps (all happened)
- The machine may run German Windows: never name a group by its localized name (the scripts use well-known SIDs).
- `wsl.exe` prints UTF-16: strip the NUL characters before you compare text.
- A runner that shows `offline` right after a restart is usually just connecting; give it a minute. A job stuck on a restarted runner clears after restarting that one runner's service.
- `actions/setup-dotnet` cannot write `/usr/share/dotnet`; the runner `.env` sets `DOTNET_INSTALL_DIR` per runner.
- Check workflows in a DMP repository (cm) must name a GitHub-hosted runner literally (DMP-020); only deploy and action workflows may use these runners.
- Never run code of a pull request from a fork on these runners. Use `runs-on: ${{ fromJSON(github.event_name == 'pull_request' && github.event.pull_request.head.repo.full_name != github.repository && '"ubuntu-latest"' || '["self-hosted","linux","wsl"]') }}` so forks stay on GitHub's machines.
- On battery the laptop sleeps and CI waits: keep it plugged in while CI matters.

## Undo
Runners: `setup-runner-wsl.sh undo` (removes them from GitHub; the folders stay). Task: `Unregister-ScheduledTask 'CI WSL keepalive'`. Distro: `wsl.exe --unregister Ubuntu-24.04` (deletes the distro and everything in it: the owner's call).

The long-term form of this is a single `.exe` that acts as an IM/MCP gateway; this script is the part an agent does by hand until then.
