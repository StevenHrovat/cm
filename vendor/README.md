# vendor

The three sibling repositories CM is built against, copied byte for byte and pinned: CM holds what it needs to be axiomatically sound. Each folder
carries a `PIN` file (`source`, `commit`, `version`, `sha256` over the folder's files except `PIN`, in ordinal path order, each as
`<relative path>\n<bytes>`). Rule DMP-022 of `tools/dmp` fails when a folder no longer matches its PIN. A vendored copy is never edited here: change it
in its source repository, then re-copy it and re-pin it.

| Folder | What it is | Source |
|---|---|---|
| `dmp/` | DMP, the Dark Matter Protocol: the chapters the gate (`tools/dmp`) checks | [StevenHrovat/dmp](https://github.com/StevenHrovat/dmp) |
| `im/` | IM, the Interstellar Medium: the codec, transport and trust core every constellation references | [StevenHrovat/im](https://github.com/StevenHrovat/im) |
| `gravity/` | Gravity, the DMP prover: the Lean evidence chain `tools/gravity` conforms to | [StevenHrovat/gravity](https://github.com/StevenHrovat/gravity) |

Re-pin: replace the folder with `git archive <commit>` of the source (without `.github`), then
`dotnet run --project tools/dmp/src/Cm.Dmp.Check.csproj -- pin . <name> <source> <commit> <version>`.
The other dependencies (NuGet packages, toolchains) are not vendored in this seed: [docs/dependencies.md](../docs/dependencies.md).
