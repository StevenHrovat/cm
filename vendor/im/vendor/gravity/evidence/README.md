# The evidence chain (Lean, CM up to DMP)

The second detector of the gravitational wave test ([DMP chapter 06](https://github.com/StevenHrovat/dmp/blob/main/06-black-hole.md)):
proofs in Lean 4, core only (no Mathlib), from the code up to DMP's sentences. The intent chain is Gravity's own models (Bend, from DMP
down to CM); the two never import or name each other's files.

## What is here

`Dmp/` is the framework every section's evidence model builds on. It states the bounds of the vendored DMP (`../vendor/dmp/`, the
version in its `VERSION` and `PIN`; the axioms and the theorems named below have not changed in wording since DMP 0.9.0) as Gravity's
laws K0 to K4 do and proves what the gravitational wave test needs of every component:

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
| `Layer.lean` | the words (layer, halves, independent), G0, T-P, T29, T38 | the layer, one signature for every information layer: independence is "no shared fail-prone set" (Malkhi and Reiter 1998), symmetric, and a longer class list only removes it (T29); the person is always a declared class, or an empty list would mint a pressed pair (finding F1); the same part twice is never a Duality (T38); the layer's gate passes only when both halves passed on live evidence, an influence confined to one fail-prone set never passes an act alone on a minted pair, and a pair that is not minted collapses (one shared cause passes any act); Ground.lean's pair is the person-only special case |
| `Recipe.lean` | T35 (the recipe), C12, C17, T38, T3a, T-P | the recipe: every layout CMOS can take as a numbered list of lines (galaxy, information layer, singularity, dwarf galaxy, surface), each laid over an earlier line; well-formedness (an earlier parent, a galaxy at the root and nowhere else, three nodes per singularity, a Duality that is not minted is laid over a line that gates it), decided and proven equal to its proposition; the chain of a line up to its galaxy and its structure (head, bounds, one unfolding, fuel-stable); the stack's gate as the meet of the layers' gates along the chain; Gravity's three verdicts (proven, unproven, out of spec) |
| `Theory.lean` | Axiom 1 at every level, T38, G0, T-P, T32 (member isolation), T37, "Who maintains it" (dwarf galaxies), C12, T33, K4 | the unified theory, by induction over the recipe: the stack's gate is a meet (a decision is never looser than any layer on its chain, sub-layers only tighten, a pass needs every layer); every pass in a well-formed recipe was passed by a minted Duality on its chain, with both halves on live evidence; a pressed root is refused, and would collapse to one person; the frame rule (a new line changes no existing chain or decision; growing is one checked line at a time); siblings never gate each other (dwarf galaxies share only what is above both); the layout never votes (placement is no argument of the gate, so a reorganisation changes no decision); the VIBE digest (depth, mass) is a function of the lines alone (T33, precomputable, finding F2); the stack potential is the sum of the layers' and never rises under a refresh anywhere (K4), zero exactly when every layer's is |
| `Complete.lean` | the words (DMP-complete), G0 (mint prep), C22 | DMP-complete as Gravity's verdict: `verdict = proven` exactly when the recipe is well formed and the lemma ledger carries every line (soundness and completeness of the checker), `out of spec` exactly when the recipe is refused, `unproven` exactly when a well-formed line is not carried; what a proven configuration keeps (two independent witnesses behind every pass, sub-layers only tighten, no pressed root); mint prep is incremental (a proven configuration plus a checked, agreed line is proven, and nothing already proven is re-opened); one uncarried line unproves the whole |
| `Layouts.lean` | worked examples | five configurations and their verdicts, computed by the kernel (`rfl`): a galaxy with a knowledge layer, a three-node singularity and a dwarf galaxy is proven; the same with a pressed root, or a two-node singularity, or a one-part layer with nothing above it, is out of spec; a one-part knowledge layer under a minted galaxy is proven (T38); a dwarf the chains do not carry makes it unproven; the digest of the proven layout |
| `Cmb.lean` | T33 (the published digest), T-P (trust never crosses instances), C24 (the cold start), chapter 06 (CMB); the owner 2026-10-26 18:19Z and 18:23Z | the Cosmic Microvibe Background, one file that proves its own recipe: the recipe written as line codes and read back agrees with the original on every declared class, and two recipes that agree there have the same well-formedness, chains, decisions, digest and verdict (the file determines everything Gravity sees); the digest is not injective (two proven layouts share it and decide differently, computed by the kernel), so the recipe is in the file and the digest beside it; a half signs only when its own build recomputes the file's verdict and digest, a seal needs both, one build signs one verdict per recipe, a sealed proven file is a DMP-complete recipe, and the sealing Duality (line 0, a minted galaxy in every well-formed recipe) is independent by theorem; the cold start: refused under other pins, the measured field selects redeploy (exactly on a match, which on a sealed file means the live system spans the proven recipe's field) or the Big Bang (from the same proven recipe); the empty recipe is proven (finding F9) |
| `R2Stable.lean` | C12 layers, T24c, T32 (the owner 2026-10-26 14:06Z) | every layer heals and the stack settles: self-stabilizing shortest-path trees (Dijkstra; Dolev, Israeli, Moran; synchronous Bellman-Ford) reach exact distances from any corrupted state in k+1 rounds, only the fixed root holds 0 (no root takeover); Lensing as a potential game (Monderer, Shapley) settles within the starting potential and never cycles; a concrete equilibrium above the minimum shows settling is not optimality |

Parameters are thousandths (`0.33` is `330`): "we only have to adjust very small decimal values" (the owner, 2026-10-26).

## Running it

`tools/check.sh` (needs `lake` from elan; the toolchain is pinned in `lean-toolchain`): checks that the vendored DMP matches its PIN
(`../tools/check-pin.sh`; a chain stated from sentences nobody pinned proves nothing), builds the package, refuses escape hatches, and
asks Lean's kernel which axioms every theorem rests on; anything beyond `propext`, `Quot.sound` and `Classical.choice` fails. On the
runners: `.github/workflows/lean-evidence.yml` of this repository, which then re-checks the whole export with an independently written kernel (nanoda, in
Rust) so that the root this chain shares with Gravity (BendTT's kernel is proven in Lean) is broken. Locally, any Lean 4 install with
the pinned toolchain works; the runner stays the gate.

## How a configuration is checked

Write its recipe as a term (see `Dmp/Layouts.lean`: one `Line` per layer, each laid over an earlier line, with its Duality's witnesses by
failure class), state the verdict you claim (`example : layout.verdict classes agree = .proven := rfl`) and run `tools/check.sh`: the
kernel computes the verdict, and `Complete.lean` is the proof that `proven` means DMP-complete. `agree` is the lemma ledger's answer per
line (C22); a line the chains do not both carry makes the configuration unproven, wherever it sits.

## What is not here yet

The R1 cold reads and mutants (R1 steps 5 and 6), `tools/gravity cross` (the coincidence check against the Gravity models), and the
section models after C2.
