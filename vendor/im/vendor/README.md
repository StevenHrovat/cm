# vendor

IM holds DMP and Gravity in the versions it is intended for (DMP chapter 00, "The root chain"): byte-for-byte copies at one commit each, in a
folder of its own, next to a `PIN` file (`source`, `commit`, `version`, `sha256` over the folder's files except `PIN`, in ordinal path order,
each as `<relative path>\n<bytes>`). `tools/check-pin.sh` fails when a folder no longer matches its PIN; Gravity's `tools/check-pin.sh` and
CM's rule DMP-022 compute the same hash, so the three gates agree on what was pinned.

| Folder | What it is | Source |
|---|---|---|
| `dmp/` | DMP, the Dark Matter Protocol: the sentences the language and its proof are stated from; it carries `SETUP.md`, the one setup file of the four root repositories, and `tools/check-setup.sh` | [StevenHrovat/dmp](https://github.com/StevenHrovat/dmp) |
| `gravity/` | Gravity, the DMP prover: the Lean evidence chain whose shape `proof/` follows and whose check script `proof/tools/check.sh` mirrors | [StevenHrovat/gravity](https://github.com/StevenHrovat/gravity) |

A vendored copy is never edited here. To move IM to a newer DMP or Gravity: change it in its own repository, then `tools/pin.sh <name>
<source url> <local clone> <commit> [<version>]` (it re-copies `git archive <commit>` without `.github` and writes the PIN; Gravity has no
VERSION file, so pass its version), run `tools/check-pin.sh`, and say in the pull request which sentences changed and which theorems were
re-read against them. Nothing here points back up the chain: IM is whole without the network. `tools/pin.sh` and `tools/check-pin.sh` are
Gravity's scripts (Apache License 2.0), with IM's words.
