# The evidence chain (Lean, CM up to DMP)

The second detector of the gravitational wave test ([DMP chapter 06](https://github.com/StevenHrovat/dmp/blob/main/06-black-hole.md)):
proofs in Lean 4, core only (no Mathlib), from the code up to DMP's sentences. The intent chain is Gravity's own models (Bend, from DMP
down to CM); the two never import or name each other's files.

## What is here

`Dmp/` is the framework every section's evidence model builds on. It states DMP 0.9.0's bounds as Gravity's laws K0 to K4 do and
proves what the gravitational wave test needs of every component:

| File | DMP | Theorems |
|---|---|---|
| `Gate.lean` | Axiom 1 (K1), Axiom 2 (K2) | an act passes exactly when both witnesses pass; one witness never acts; the stricter answer stands; a witness passes only on live, in-window evidence read at the step; stale evidence never passes |
| `Record.lean` | Axiom 3 (K3) | every request leaves exactly one entry, at the act, with both verdicts; an act is performed only with both witnesses on good evidence; a run keeps every record |
| `Ground.lean` | G0, T-P (K0) | on a minted pair no one controls both witnesses; on a pressed pair one person does, and can pass any act alone (the collapse): out of spec |
| `Standing.lean` | T24, T24c | one witness never loosens; one witness tightens; loosening needs both; self-correction needs one |
| `Floor.lean` | T31, T34 | the three roads to the entropy floor, each from its own construction: the gate road (no act on a contained item but re-admission or destruction), the evidence road (no plaintext once the key's validity ran out, under any gate), the record road (destroyed at its date, under any gate and key) |
| `Field.lean` | T32, Gravity's constant | N1 the laws do not see the parameters; N1b the schedule never votes; N1c a stricter demand passes less, and after a one-witness write of κ and e_min the gate passes nothing it refused before (only both witnesses lower a demand, T24); K4 the system's own acts never raise the potential; N2 the potential is bounded |
| `R1Model.lean` | C2 (R1 step 1) | the Milky Way verifier and replay memory, written blind from the C# alone; sanity checks only |
| `R1Laws.lean` | C2 (R1 step 2) | C2.L1 to L4 as propositions over an abstract verifier, written blind from the approved sentences alone |
| `R1Bridge.lean` | C2 (R1 step 3) | the model as an instance of the laws: L1, L3, L3b and L4 hold; L2 holds on clean inputs, with rule 16 pruning; a witness for each place the model and the literal L2 part, and a replay under a clock that goes back |
| `R1Wave.lean` | C2, proposal (the owner 2026-10-26) | the wave clock: root steps signed by both witnesses, taken only forward; layer k steps once per 10^k root steps (or with the root, no dilation); on wave time the blind model verifies a frame at most once over any sequence of calls; dilation only tightens a token window |
| `R1Lens.lean` | C2, proposal (the owner 2026-10-26 12:47Z) | lensing, the self-reorganising core: a reorganisation moves a node only with both signatures and a forward epoch; delivering it to exactly the affected nodes is enough and necessary; the per-layer profile (the VIBE field's shape) is the same for every trust order; with a moving layout the rising floor verifies a frame at most once over any times and windows, while forgetting by high-water time replays once a window widens (concrete case); stamps count root steps |
| `R1Mass.lean` | C2, T5, T32, proposal (the owner 2026-10-26 13:20Z) | mass pulls, the DoT signs: the net proposes a layout from the signed state, each witness recomputes it with its own build; an accepted layout is what both builds compute, a layout either does not compute is refused, one signed state accepts at most one layout; a refused proposal never moves a node; one hub holds a mass majority, so the weight never votes |
| `R1Tier.lean` | C12, C13 redraft (the owner 2026-10-26 13:31Z) | information tiers: the Milky Way net answers first, then the VIBE sources in field order, the exit node only when nothing inside the Event Horizon answers; no route returns an item inside a privacy singularity; routing equals routing over sources with every singular item removed, so an answer reveals nothing a singularity holds |
| `R2Stable.lean` | C12 layers, T24c, T32 (the owner 2026-10-26 14:06Z) | every layer heals and the stack settles: self-stabilizing shortest-path trees (Dijkstra; Dolev, Israeli, Moran; synchronous Bellman-Ford) reach exact distances from any corrupted state in k+1 rounds, only the fixed root holds 0 (no root takeover); Lensing as a potential game (Monderer, Shapley) settles within the starting potential and never cycles; a concrete equilibrium above the minimum shows settling is not optimality |

Parameters are thousandths (`0.33` is `330`): "we only have to adjust very small decimal values" (the owner, 2026-10-26).

## Running it

`tools/check.sh` (needs `lake` from elan; the toolchain is pinned in `lean-toolchain`): builds the package, refuses escape hatches, and
asks Lean's kernel which axioms every theorem rests on; anything beyond `propext`, `Quot.sound` and `Classical.choice` fails. On the
runners: `.github/workflows/lean-evidence.yml` of this repository, which then re-checks the whole export with an independently written kernel (nanoda, in
Rust) so that the root this chain shares with Gravity (BendTT's kernel is proven in Lean) is broken. Locally, any Lean 4 install with
the pinned toolchain works; the runner stays the gate.

## What is not here yet

The R1 cold reads and mutants (R1 steps 5 and 6), `tools/gravity cross` (the coincidence check against the Gravity models), and the
section models after C2.
