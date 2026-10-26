# Dependencies

**The root repository stays lean (the owner, 2026-10-26).** Only the three sibling repositories are vendored and pinned (`vendor/`, rule DMP-022).
Storing and verifying every other dependency is **mint prep**: a minting does not start until the system is provably DMP-complete, and the well-known
keys (WKHs) cannot be minted without it. What the mint-prep step must cover, and what is practical for each:

| Dependency | Mint-prep approach |
|---|---|
| NuGet packages (about 85 `.nupkg`, roughly 80 MB for `CM.slnx`) | copy exactly what `dotnet restore` fetched into `vendor/nuget`, pin it, and point a `NuGet.config` at it as the only source (tried once offline: it restores) |
| .NET SDK 10 | not vendorable in git (hundreds of MB); pin the exact SDK in `global.json` and record its download hash |
| Lean 4 toolchain (Gravity) | not vendorable in git; `vendor/gravity/evidence/lean-toolchain` names the version, Gravity has no other Lean dependency (no mathlib); record the toolchain's hash |
| swtpm, PowerShell | operating-system tools; record version and package hash |
| GitHub Actions used by the workflows | pin by commit SHA |
| Telegram Bot API (bridge lane B, `docs/process/zcode-bridge.md`) | external HTTPS service, no package: call it with `HttpClient`; record the API version used. The bot token is a secret and never enters the repository. A wrapper library is not adopted; if one is ever needed it must be Apache 2.0 or MIT and pinned in `Directory.Packages.props` |
| Z code desktop app (build laptop only) | proprietary, not vendorable; record the installed version and installer hash. Its remote-control link is a credential |
| Z.ai hosted model service (what Z code calls) | hosted service, not vendorable; sees only the DMP, Gravity and IM repositories |
| Browser and app driving interface (`cm.ii` candidate) | not chosen yet. Owner note 2026-10-26: CM must be able to drive other websites remotely, as an MCP. Candidate prior art to check for licence and fit at mint prep: Playwright (believed Apache 2.0) |

Until then a build may reach nuget.org and the action hosts; that is the known gap of the lean seed.
