# CM, Canis Major

*One repository from which every part of an organisation's infrastructure deploys.*

CM is the framework. **CMOS** is what an organisation runs when CM runs its whole infrastructure. CM is the third of four sibling repositories:

| Repository | Role |
|---|---|
| [DMP](https://github.com/StevenHrovat/dmp) | the Dark Matter Protocol (*General Trustability*): the specification every pull request here is judged against |
| [IM](https://github.com/StevenHrovat/im) | the Interstellar Medium: the machine- and audit-readable codec, transport and trust core |
| [Gravity](https://github.com/StevenHrovat/gravity) | the DMP prover (*Bending Trustspace*): the Lean evidence chain |
| **CM** (this repository) | the framework that DMP, through IM, translates intent into |

CM holds pinned, byte-for-byte copies of the other three in [`vendor/`](vendor/README.md) (rule DMP-022 fails when a copy drifts from its pin), so
it carries what it needs to prove itself.

## What is here

| Folder | What it is |
|---|---|
| `cm.io`, `cm.os` | the foundation: protocol and format bricks (`cm.io`) and the operating layer on top (`cm.os`) |
| `CM.MilkyWay` | the universal I/O layer: transport, verifier, gate, health, simulation |
| `CM.BlackHole` | the security review: issuing CA plan and tooling, cold-start criteria, the Gravity laws, GRB scope files |
| `CM.Pictor`, `CM.Observatory` | tree nodes, set up and tested; their product code comes later |
| `DMP/` | the machine registers the gate reads (tree, routes, workflows, warehouses, IM locks, data features) |
| `tools/dmp` | the DMP gate: rules `DMP-001` to `DMP-022`, each proved green on this repository and red on a copy with one breach |
| `tools/verify` | the single check: the `dmp` group, then build and tests |
| `tools/lib/Guard.psm1` | the safe-script guard every administrator script starts with |
| `tools/runner/setup-runner.ps1` | sets up GitHub Actions self-hosted runners on a Windows machine (dry run first; not yet run on Windows) |
| `templates/` | document templates (data gate, GRB) |

The folder layout is the composition tree in `DMP/tree.txt`: `cm.io` <- `cm.os` <- the `CM.<Constellation>` folders, each with `src/` and `tests/`,
none referencing another constellation.

## What is not here yet

Further constellations (a warehouse engine and a probe framework), the connector modules, and the observability specification are carried over in
later pull requests, each rebuilt generic with synthetic data only. Vendoring every other dependency (NuGet packages, toolchains) is the preparation
for a mint, not part of this seed: [docs/dependencies.md](docs/dependencies.md).

## Build and check

```
dotnet build CM.slnx
dotnet test CM.slnx
tools/verify          # tools\verify.ps1 on Windows
```

Rules for contributors and agents: [CLAUDE.md](CLAUDE.md). Licence: [Apache 2.0](LICENSE).
