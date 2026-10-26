# vendor

Gravity holds the version of DMP it is based on (DMP chapter 00, "The root chain"): a byte-for-byte copy of the DMP repository at one
commit, in a folder of its own, next to a `PIN` file (`source`, `commit`, `version`, `sha256` over the folder's files except `PIN`, in
ordinal path order, each as `<relative path>\n<bytes>`). `tools/check-pin.sh` fails when the folder no longer matches its PIN; CM's rule
DMP-022 computes the same hash over the same folder, so the two gates agree on what was pinned.

| Folder | What it is | Source |
|---|---|---|
| `dmp/` | DMP, the Dark Matter Protocol: the sentences every theorem in `evidence/` is stated from | [StevenHrovat/dmp](https://github.com/StevenHrovat/dmp) |

The copy carries `SETUP.md`, the one setup file of the four root repositories (every toolchain with version, source and hash, every host,
the install steps), and `tools/check-setup.sh`, which `evidence/tools/check.sh` runs after the pin check: Gravity fails when it needs
something the file does not list. Both are DMP's; a change to them is a change in DMP, re-pinned here.

A vendored copy is never edited here. To move Gravity to a newer DMP: change DMP in its own repository, then `tools/pin.sh dmp
https://github.com/StevenHrovat/dmp <local clone of dmp> <commit>` (it re-copies `git archive <commit>` without `.github` and writes the
PIN), run `tools/check-pin.sh`, and say in the pull request which sentences changed and which theorems were re-read against them. Nothing
here points back up the chain: Gravity is whole without the network.
