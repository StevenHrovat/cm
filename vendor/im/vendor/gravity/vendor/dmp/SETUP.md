# SETUP: what the root repositories need to run

One file for the four root repositories, DMP, Gravity, IM and CM. It is born here in DMP and travels down the root chain byte for byte with
the vendored copy of DMP (DMP 00, "The root chain"; `vendor/dmp/SETUP.md` in the other three), so every repository shows the same text. An
agent or a person sets up any of the four from this file alone. Each repository runs the check at the end on every verify: a repository
that needs something this file does not list fails.

The owner, 2026-10-26: "every layer of DMP (so DMP/IM/CM/Gravity) should share a universal setup file that everyone (agent and human) can
just read and know exactly what is needed to run everything required. This needs to be tracked anyway according to the axiom system."
Why the axiom system asks for it: the words (DMP-complete: every dependency needed to build, check, prove, deploy and restore is inside
the configuration's repository, pinned by hash), G0 (mint prep stores and verifies every dependency), T3 (the configuration heals itself
with no connection across its Event Horizon). The root repositories stay lean (DMP 00): they name their toolchains here, with version,
source and hash, instead of carrying them; a configuration about to be minted carries them (mint prep).

## Toolchains

One row per tool. `Id` is what the per-repository table and the check refer to. `sha256` is the hash of the download named in `Source`;
`-` means not recorded yet (a gap below).

| Id | Tool | Version | Source | sha256 | Install |
|---|---|---|---|---|---|
| lean | Lean 4 (`lean`, `lake`), Linux x64 | v4.34.0 | https://github.com/leanprover/lean4/releases/download/v4.34.0/lean-4.34.0-linux.tar.zst | caaa98356098c85dc0fcbbd28e1ec66f39eb6551829972b752ff20e1286b646b | L1, or L2 |
| dotnet | .NET SDK, Linux x64 (Windows x64: `dotnet-sdk-10.0.403-win-x64.zip`, sha512 `7a114c31…874ef48` in the same folder) | 10.0.403 | https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.403/dotnet-sdk-10.0.403-linux-x64.tar.gz (what `dotnet-install.sh --version 10.0.403` fetches) | sha512 b7b3d8461cf98b07d2552ce9199702fa3ad010ac2671a5f8cd5136f7534e50f6855ba7bbb02b11494563f1d04156276a31cda1103b900e5eecd0d9473dc7b9c5 | D1 |
| lean4export | lean4export (Lean FRO), the export of checked declarations | tag `v4.34.0` when it exists, else the default branch (gap G1) | https://github.com/leanprover/lean4export | - | E1 |
| nanoda | nanoda_lib (ammkrn), the second kernel, Rust | default branch (gap G1) | https://github.com/ammkrn/nanoda_lib | - | E1 |
| rust | rustup, cargo (to build nanoda) | stable | https://sh.rustup.rs | - | R1 |
| python | python3 with the `zstandard` package (to unpack the Lean archive where no `zstd` binary and no `curl` is allowed) | 3.x, zstandard 0.25.0 | https://pypi.org/project/zstandard/ | - | L2 |
| shell | bash, find, sha256sum, tar, git, jq | any current | the operating system | - | none |
| powershell | Windows PowerShell 5.1 (the scripts an administrator runs), `pwsh` on Linux for the parse check | 5.1; pwsh 7.x | the operating system | - | none |

## Hosts

Every network host a setup or a check reaches. A sandbox or a proxy that must allowlist hosts allowlists these and nothing else.

| Host | Why | Needed by |
|---|---|---|
| github.com, objects.githubusercontent.com | the four repositories; release assets (Lean) | all |
| raw.githubusercontent.com | `elan-init.sh` (install L1) | gravity CI |
| api.github.com | the runner setup (`cm/tools/runner`) | cm |
| release.lean-lang.org | elan's toolchain download (install L1); the GitHub release is the fallback (L2) | gravity, im, cm |
| dot.net, builds.dotnet.microsoft.com, ci.dot.net | `dotnet-install.sh` and the SDK it fetches | im, cm |
| api.nuget.org | NuGet packages (`dotnet restore`) | im, cm |
| sh.rustup.rs, static.rust-lang.org, index.crates.io, static.crates.io | rustup and cargo (nanoda) | gravity CI |
| pypi.org, files.pythonhosted.org | the `zstandard` package (L2) | where L1 is blocked |

## Install steps

Every step is idempotent: run it again and nothing changes. Paths are examples; a session puts tools in its own scratch folder.

- **L1 · Lean through elan.** `curl -sSfL https://raw.githubusercontent.com/leanprover/elan/master/elan-init.sh | sh -s -- -y --no-modify-path --default-toolchain leanprover/lean4:v4.34.0`, then put `~/.elan/bin` on the PATH. `lean --version` says 4.34.0.
- **L2 · Lean from the GitHub release (when `curl` or elan is blocked).** Download the asset of the `lean` row (in a cloud session: with Python's `urllib` through the proxy and its CA bundle, since `curl` to release assets is refused there), check its sha256 against the row, unpack with Python `zstandard` plus `tarfile` (there is no `zstd` binary), and put `lean-4.34.0-linux/bin` on the PATH.
- **D1 · .NET SDK.** `curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh && bash dotnet-install.sh --version 10.0.403 --install-dir ~/.dotnet`, then put `~/.dotnet` on the PATH and set `DOTNET_CLI_TELEMETRY_OPTOUT=1`. A cloud session has no `dotnet` on the PATH until this ran (or an environment setup script ran it).
- **E1 · The second kernel.** Clone lean4export, check out the tag of the Lean version (or the default branch, gap G1), write the toolchain into its `lean-toolchain`, `lake build`; clone nanoda_lib, `cargo build --release`. Gravity's `.github/workflows/lean-evidence.yml` is the reference.
- **R1 · Rust.** `curl -sSf https://sh.rustup.rs | sh -s -- -y`, then `~/.cargo/bin` on the PATH.

## What each repository needs

The `Toolchains` cell lists ids of the toolchain table only, separated by commas (the check reads it). Where a toolchain is needed only
on the runner or only for one group, the `Runs` cell says so.

| Repository | Toolchains | Runs | Check |
|---|---|---|---|
| dmp | shell | `tools/scrub-scan.sh` | `tools/check-setup.sh .` |
| gravity | shell, lean, lean4export, nanoda, rust | `tools/check-pin.sh`, `evidence/tools/check.sh`; lean4export, nanoda and rust only in the `lean-evidence` workflow | `vendor/dmp/tools/check-setup.sh .` |
| im | shell, dotnet, lean | `dotnet test Im.slnx`, `proof/tools/check.sh`, `tools/scrub-scan.sh` | `vendor/dmp/tools/check-setup.sh .` |
| cm | shell, dotnet, powershell, lean | `tools/verify`; powershell for the scripts group, lean for the gravity group (both say "skipped" where missing) | `vendor/dmp/tools/check-setup.sh .` |

## Lessons (dated, so a later reader knows what was true when)

- 2026-10-25: a cloud session has no `dotnet`; `dotnet-install.sh` from `dot.net` works; `dotnetcli.azureedge.net` is refused by the proxy (Tier 1: the knowledge store, procedures, ".NET-10-SDK in einem Cloud-Thread installieren").
- 2026-10-26 (the owner): `ci.dot.net` must be reachable for `dotnet-install`.
- 2026-10-26: `curl` to a GitHub release asset is refused by the session sandbox; Python `urllib` through the proxy with its CA bundle works; the archive unpacks with Python `zstandard` (no `zstd` binary). `gh release download` fails (GraphQL is blocked) and `gh api` needs the repository attached.
- 2026-10-26: Gravity's full check (`evidence/tools/check.sh`, 232 theorems) runs in 12 s on a 4-core cloud box with the L2 toolchain.

## Gaps (the check prints them; each closes with one pull request here)

- G1 · lean4export and nanoda are cloned at their default branch in Gravity's CI: pin both by commit and record the hash.
- G2 · `im/.github/workflows/verify.yml` names `actions/checkout@v4` and `actions/setup-dotnet@v4` by tag; pin by commit SHA as Gravity does.
- G3 · NuGet packages are fetched from `api.nuget.org` at build time; vendoring them is mint prep (`cm/docs/dependencies.md`).
- Closed 2026-10-26: the .NET SDK hash (the release metadata at `builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json` publishes sha512 per file).

## The check

`tools/check-setup.sh <repository root>` is vendored with this file and runs in every repository's verify. It reads the tables above and fails
(exit 1, one line per finding) when:

1. the repository's pinned Lean version (`lean-toolchain`, `evidence/lean-toolchain`, `proof/lean-toolchain`) is not the `lean` row's version;
2. the repository's .NET target (`net10.0` in `Directory.Build.props`, `global.json` if present) is not the `dotnet` row's major version;
3. a workflow or a script under `.github/` or `tools/` reaches a host that is not in the hosts table;
4. a `vendor/*/PIN` names a source host that is not in the hosts table;
5. a toolchain the per-repository table names for this repository has no row above.

It never installs anything and never reaches the network.
