# Acknowledgements: the prior work Gravity stands on

The owner, 2026-10-26 18:44Z: "We are giving everyone access to DMP through Apache 2.0, if any prior work can be used to help
build/improve it, we should use it and thank them kindly." And: "It also has to be Apache 2.0."

Two rules follow, and this file is their record:

1. **Use prior work, and say so.** Before an algorithm, a model or a proof shape is invented here, the literature is searched. Whatever
   is used is cited where it is used (the "Prior art" paragraph at the top of the Lean file) and listed below with what was taken, where
   it is used, and our thanks. Nothing here is invented beyond the reading these works allow; where we add something, the finding list
   (`findings.md`) says what.
2. **What enters the repository is Apache 2.0.** Code or data taken into this repository carries the Apache License 2.0 (or a licence
   the Apache License 2.0 can carry); anything else is put to the owner before it is taken in. Tools that only run in the check are
   listed with their licence as well, so that the whole chain is known.

Ideas and papers are not licensed and need no licence; they are cited and thanked. Code and tools are listed with their licence.

## Ideas and papers

| Work | What we took | Where it is used | Thanks |
|---|---|---|---|
| Dahlia Malkhi and Michael Reiter, "Byzantine Quorum Systems", *Distributed Computing* 11 (1998), 203–213 | Fail-prone systems: a family of sets that may fail together, and the quorum condition that no fail-prone set holds a whole quorum. Our failure-class list is such a family; two witnesses are independent exactly when no fail-prone set of a declared class holds both. | `evidence/Dmp/Layer.lean` (independence, the Duality, the collapse of a pair that is not minted); `evidence/Dmp/Theory.lean` (two independent witnesses behind every pass, a pressed root collapses) | The Duality of Trust has a forty-year-old mathematics; we are glad it does. |
| Dorothy E. Denning, "A Lattice Model of Secure Information Flow", *Communications of the ACM* 19(5) (1976), 236–243 | A security lattice in which a flow is allowed only where every class on the path allows it. Our verdicts `refuse < hold < pass` form such a lattice and the stack's gate is the meet along a chain, so "sub-layers only tighten" is "a meet never exceeds its arguments". | `evidence/Dmp/Recipe.lean` (`meet`, `Recipe.decide`); `evidence/Dmp/Theory.lean` section 1 | The simplest statement of Axiom 1 at every level is hers. |
| Peter O'Hearn, John Reynolds and Hongseok Yang, "Local Reasoning about Programs that Alter Data Structures", *CSL 2001*, LNCS 2142, 1–19 | The frame rule: a specification about one part holds in any larger context that does not touch it. Adding a line to a recipe changes no existing chain and no existing decision. | `evidence/Dmp/Theory.lean` section 4 (`Recipe.extend_preserves_decisions`); `evidence/Dmp/Complete.lean` (mint prep is incremental) | Member isolation and containment under growth are the frame rule; we only had to notice. |
| Joseph A. Goguen and José Meseguer, "Security Policies and Security Models", *IEEE Symposium on Security and Privacy* (1982), 11–20 | Noninterference: a low input the decision does not depend on. The layout (placement, routing order) is such an input of the gate; here it is not an argument at all. | `evidence/Dmp/Theory.lean` section 6 (`Config.relayout_never_votes`, proof by reflexivity); finding F6 | "A layer places information, never grants trust" (C12) is their theorem in our words. |
| Edsger W. Dijkstra, "Self-stabilizing Systems in Spite of Distributed Control", *Communications of the ACM* 17(11) (1974), 643–644 | Self-stabilisation: a system that reaches a legitimate state from any state. Every layer heals. | `evidence/Dmp/R2Stable.lean` | The idea that a layer heals on its own, without a coordinator, is his. |
| Shlomi Dolev, Amos Israeli and Shlomo Moran, "Self-Stabilization of Dynamic Systems Assuming Only Read/Write Atomicity", *Distributed Computing* 7 (1993), 3–16 | Self-stabilising shortest-path (BFS) trees: exact distances from any corrupted state in k+1 rounds, and only the fixed root holds distance 0 (no root takeover). | `evidence/Dmp/R2Stable.lean` | The proof shape of "no root takeover" is theirs. |
| Richard Bellman, "On a Routing Problem", *Quarterly of Applied Mathematics* 16 (1958), 87–90; Lester R. Ford Jr., *Network Flow Theory* (RAND P-923, 1956) | Synchronous relaxation of distances to a root. | `evidence/Dmp/R2Stable.lean` | The depth of a layer is a shortest path; the algorithm is seventy years old and still the right one. |
| Dov Monderer and Lloyd S. Shapley, "Potential Games", *Games and Economic Behavior* 14 (1996), 124–143 | Potential games: every improvement path ends, within the starting potential, and never cycles. Lensing (the self-reorganising core) is such a game; the equilibrium it reaches need not be the optimum (finding F2). | `evidence/Dmp/R2Stable.lean`; `evidence/Dmp/Theory.lean` section 8 (the stack potential never rises) | Settling was a theorem before we needed it. |
| The parent-array encoding of a rooted forest (folklore; e.g. Knuth, *The Art of Computer Programming* vol. 1, 2.3) | Each line names an earlier line; structural induction is strong induction on the line number. | `evidence/Dmp/Recipe.lean` (`Recipe`, `chainF`) | The oldest tree there is. |

## Code and tools

Nothing from these is copied into the repository; the proofs are written here. They run the check. All are Apache License 2.0.

| Tool | Licence | What it does for us | Where |
|---|---|---|---|
| Lean 4 (Lean FRO; Leonardo de Moura, Sebastian Ullrich and contributors), https://github.com/leanprover/lean4 | Apache License 2.0 | The language and the first kernel that checks every theorem of the evidence chain; core only, no Mathlib. Toolchain pinned in `evidence/lean-toolchain`. | `evidence/`, `evidence/tools/check.sh`, `.github/workflows/lean-evidence.yml` |
| lean4export (Lean FRO), https://github.com/leanprover/lean4export | Apache License 2.0 | Exports the checked declarations out of Lean for an independent kernel (finding F3 names it as the one shared step between the two kernels). | `.github/workflows/lean-evidence.yml` |
| nanoda_lib (ammkrn), https://github.com/ammkrn/nanoda_lib | Apache License 2.0 | The second, independently written kernel (Rust) that re-checks the whole export, so that the two kernels share no author. | `.github/workflows/lean-evidence.yml` |
| GitHub Actions `actions/checkout`, `actions/upload-artifact` | MIT License | Check out the repository and keep the reports on the runner. Run only, nothing taken in; pinned by commit. | `.github/workflows/` |

## The vendored specification

`vendor/dmp/` is DMP itself (https://github.com/StevenHrovat/dmp, Apache License 2.0), byte for byte at the commit its `PIN` names; it is
the specification this chain is stated from, not prior work, and `tools/check-pin.sh` verifies it before anything else runs.

## How to add an entry

When a file takes an idea from the literature, write it in the file's "Prior art" paragraph with author, title, venue and year, and add
one row above: what was taken, where it is used, and a line of thanks. When a tool or a piece of code is taken in, check its licence
first (Apache License 2.0, or one the Apache License 2.0 can carry); anything else goes to the owner before it enters.
