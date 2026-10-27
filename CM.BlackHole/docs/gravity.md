# Gravity: proving DMP (concept)

Security context: defensive security engineering under the charter in `CLAUDE.md`. This is about proving that the framework's own
controls do what DMP says, on the operating organisation's own systems.

Status: slice G1 built (C2 against the Milky Way verifier, on unmodified Bend 2); "The constant" drafted (2026-10-26: the fixed laws K0 to K4, the VIBE field, the tunable parameters, Lean as the evidence chain); C2's law sentences approved by the owner and locked (C2's `Law:` block, rule DMP-018). Gravity is decided as CMOS's proving engine, a fork of Bend 2 by HigherOrderCO (Apache 2.0), next to
Lean, inside Singularity. This file is how Gravity proves DMP and
what the first slice is. The page for the owner is the artifact "Gravity, the DMP Prover"; the reasoning behind the fork and the theorems for the shield are kept with the owner.

## CORE: the cascade Gravity serves

CORE, the Cascading Ordered Rules Engine (the owner's name, 2026-10-25), is the order in which CMOS proves itself. This section is the intent chain; the evidence chain that runs back up is in "Two chains" below. Gravity is the
engine that checks each step of it. It has three layers, and each is proven against the one above:

1. **DMP**: the rules as ordered plain sentences. This is the common-sense surface people read and change. Every sentence that can be
   made exact becomes a law (a `Law:` block, then `LAWS.bend`). The rest stays checked, measured or reviewed (the assurance words).
2. **IM**: the protocol. A model of it is written from DMP's chapter, never from the code, and proven to meet DMP's laws. This step
   down has to be unmistakably correct, so it has three guards:
   - the law file states DMP's own terms ("authorised", the table, the order of the rules) separately from the model;
   - a cold read translates the laws back into sentences the owner compares with his own;
   - named mutants show that every law actually catches a bug.
3. **CM**: the code and the estate. The proven protocol is the specification CM is held to:
   - today by conformance (the C# against the proven model, tested);
   - by the evidence chain in Lean, which reads the C# upward ("The constant" below; the owner, 2026-10-26);
   - by exhaustive checks of each configuration snapshot.
   "CM proven" therefore means: the code is proved, or conformance-tested against a proved model; the configuration is checked; the
   world is measured. The world itself is never proven.

Authority runs downwards and evidence runs both ways: a lower layer never changes an upper one to go green, while the evidence chain ("Two chains" below) proves the same layers again from CM upwards. When a lower layer cannot meet a law, DMP
gains a new assertion with the owner's approval; it never loses one. Each layer has its own language (sentences, Bend laws, the IM
model, C#), so no layer proves itself. This is the separation the owner pointed to as the way around Goedel.

Where G1 sits, for C2:

| Step | G1 |
|---|---|
| DMP | C2's `Law:` block, L1 to L4 and L3b, approved by the owner; `LAWS.bend` locked by its sha256 in C2 (DMP-018) |
| DMP to laws | `LAWS.bend`: `Authorised`, the table of rules, `Expect` and `Unexpired`, written from DMP; cold read done before the lock (`C02/COLDREAD.md`) |
| laws to IM | `model.bend` (the verifier, rules 1 to 16) proven against `LAWS.bend` by Bend and the kernel; mutants M1 to M6 |
| IM to CM | `ImVerifier` against the model, 3000 sandbox cases, 0 mismatches (tested, not proven) |

Still open in C2's cascade:
- the rest of IM: the codec (G5), the handshake (I5, model-checked);
- a proof of the C# itself rather than conformance.

The other constitution sections enter the cascade one at a time, in the order of "The constitution by reach".

## The constant (draft, DMP 0.9.0; the owner, 2026-10-26)

"Gravity has to be a known constant. So we have to find our own general relativity ... as precise as possible, so we only have to adjust
very small decimal values" (the owner, 2026-10-26 01:56Z). This section is that constant: what Gravity holds fixed, what it lets be tuned,
and the one property that makes the split safe. It rests on DMP 0.9.0 (an earlier change, draft until the owner's "approved"); section ids below are
the consolidated constitution's.

### What is fixed: the signature and the laws

The constant is a signature (the things Gravity speaks about) and the laws over it. Both change only with the constitution, never with a
setting, and both chains state them from the same DMP sentences.

| Symbol | What it is | From |
|---|---|---|
| `Part`, `Act`, `Door` | a component instance, an act it can perform (T17: enforcement is an act), the entry a request comes through | the words, T17 |
| `Witness` = {`W1`, `W2`} | the two witnesses of the dual gate | Axiom 1 |
| `Standing p s t` | part `p` holds standing `s` at time `t`, with a validity window `[from, until)` | the words |
| `Verdict w a t` | witness `w`'s answer on act `a`, read at `t` | Axiom 1, Axiom 2 |
| `Evidence e t` | what a verdict read: its source, the time it was read, its window | Axiom 2 |
| `Record r` | the entry written at an act: who, what, target, both verdicts, time | Axiom 3 |
| `Time` | natural numbers in milliseconds on one injected clock; only order and differences are used | T9 (clock) |
| `Ground` = `minted` or `pressed` | whether the two witnesses trace to two people or to one | G0, T-P |

The laws, as Gravity states them (each one a law per section under "How a section gets laws"; both chains state them again):

- **K1, the dual gate (Axiom 1).** `performed a t → Verdict W1 a t = pass ∧ Verdict W2 a t = pass ∧ independent W1 W2`. Where the two
  verdicts differ, the stricter stands: `decide a t = min (Verdict W1 a t) (Verdict W2 a t)` in the order `refuse < hold < pass`.
- **K2, evidence (Axiom 2).** `Verdict w a t = pass → ∃ e, read e = t ∧ inWindow e t ∧ source e ∈ live`. A cached verdict, an upstream
  flag or a caller's claim is not in `live`.
- **K3, the record (Axiom 3).** `performed a t → ∃ r, recordOf r a ∧ writtenAt r = t`; a refusal leaves one too. No path performs an act
  whose record fails to write.
- **K4, the balance (Axiom 1; DMP 0.9.7 folded the old Axiom 4, the direction, into Axiom 1 · The Duality).** Under the system's own acts the potential (below) never rises: `ownAct t t' → Φ t' ≤ Φ t`. Only losses
  and ageing raise it.
- **K0, the ground (G0, T-P).** `independent W1 W2 ↔ ground = minted`. On `pressed`, K1's independence is false by construction, so every
  law that rests on it is unprovable there and Gravity's verdict on the instance is **out of spec** (T-P, the owner 2026-10-26 02:21Z:
  "Gravity abhors it").

### What is fixed too: the field's formulas (T26)

The VIBE field is defined once. Only the parameters in the next table are configuration.

| Quantity | Definition |
|---|---|
| depth `d(p)` | length of the shortest grant chain from the pair to `p` in the grant graph the record holds (never network hops); `d(pair) = 0` |
| mass `m(p)` | `1 + |{q : q derives through p}|` |
| freshness `f(s, t)` | `clamp((until − t) / (until − lastVerified), 0, 1)`, the minimum over the witnesses' stamps, and never above the grantor's: `f(s, t) ≤ f(grantor s, t)` |
| shortfall `σ(p, t)` | `Σ_{s ∈ declared p} w_s · (1 − f(held p s, t))`, with `f = 0` for a declared standing that is not held |
| VIBE `V(p, t)` | `m(p) · σ(p, t)` |
| potential `Φ(t)` | `Σ_p V(p, t)`; `Φ(t) = 0` exactly when the boundary holds what it declares |
| temperature `Θ(t)` | `Σ_p m(p) · (1 − f̄(p, t)) / Σ_p m(p)`, with `f̄` the mean freshness of `p`'s held standings; the one number every reader but the two witnesses sees |

### What is tunable: the parameters (small decimals, proven-safe bounds)

| Parameter | Meaning | Default | Allowed | What it may change | Tighter when |
|---|---|---|---|---|---|
| `ρ` refresh threshold | a standing is refreshed when its freshness falls below `ρ` | 0.33 | 0.20 to 0.50 | when a refresh is scheduled | higher (refresh sooner) |
| `λ` window growth | a standing's window at depth `d` is `w₀ · (1 + λ)^d` (windows lengthen with depth, T26) | 0.10 | 0.00 to 0.25 | how long an outer standing lasts before it needs a witness | lower (shorter windows) |
| `κ` evidence gradient | the evidence level a part must give is `e_min + ⌈κ · (D − d(p))⌉`, `D` the boundary's depth (nearer the pair, more evidence; chapter 06) | 0.50 | 0.25 to 1.00 | how much proof a part near the pair must show, never less than `e_min` | higher (more evidence) |
| `μ` stability margin | the system is stable when the witnesses' verification rate is at least `(1 + μ)` times the ageing rate | 0.10 | 0.00 to 0.50 | when the "below stability" alarm fires and the edge is let go stale first | higher (alarm earlier) |
| `θ` temperature alarm | the alarm when `Θ(t) > θ` | 0.10 | 0.05 to 0.30 | how loud the readout is | lower (alarm earlier) |
| `w_s` shortfall weight | per declared standing kind | 1.00 | 0.10 to 1.00 | the order of refreshes among equally heavy parts | never (a reorder, both witnesses either way) |
| `w₀`, `e_min`, skew, lifetimes | base window, minimum evidence level, clock skew, token lifetimes | per section (C2 holds skew and lifetime) | per section | as their sections say; `e_min` higher, `w₀` and lifetimes lower; skew never |

Each parameter is a setting with its default and its plain description (T9: values are configuration). A value outside its interval is
refused at the door of the settings write. Inside its interval a move is a tightening only in the direction the last column names, and
one witness may make it at once (T16: "a witness may tighten [...] on its own"); a move the other way is a loosening and needs both
witnesses, exactly like widening an interval (T16: "everything that loosens needs both witnesses"). A parameter the column marks "never"
needs both witnesses in either direction. The settings write knows the direction from the old and the new value, so the door enforces it. **The defaults and
intervals are assumptions (Annahme)**: an the knowledge warehouse search for a trust-field, freshness or refresh-threshold source found nothing that applies
(the hits are about other kinds of freshness), so each number is a starting point to be measured on the nightly and replaced by its
measurement, with the source written beside it.

### The property that makes it a constant: the parameters never vote

**N1 (the laws do not see the parameters).** For every parameter vector `π` inside its intervals, every act that passes under `π` keeps
K1, K2 and K3: `decide_π a t = pass → K1 ∧ K2 ∧ K3` for `a` at `t`. The laws mention no parameter, so no tuning inside the intervals can
make an act pass that the laws refuse.

**N1b (the schedule never votes).** `ρ`, `μ`, `θ` and `w_s` do not occur in `decide` at all: for two vectors that differ only in them,
`decide_π a t = decide_π' a t`. They change which standing is refreshed when, in which order, and how loudly the readout reports.

**N1c (the demands never change the laws).** `κ` and `e_min` set the evidence a part must give, and `λ` sets the window of standings not
yet issued; moving any of them inside its interval changes which acts have enough evidence, never what counts as evidence (K2) or that
both witnesses must pass (K1). The direction matters for who may move them, not for the laws: raising `κ` or `e_min`, or lowering `λ`, is
a tightening one witness may make; the opposite move lowers what a part must show and is a loosening under both witnesses (T16, the
table's last column). N1c is therefore not a claim that every move tightens; it is the claim that no move, in either direction, lets an
act pass that K1 or K2 refuses.

Together these are T26's "the weight never votes" (T20) as theorems, and they are what lets the owner "only adjust very small decimal
values": a tuning changes how fast and in which order the system restores itself, how much proof the parts near the pair show, and how
loudly it reports; it never changes the laws an act must keep.

**N2 (stability within bounds).** For every parameter vector inside its intervals, K4 holds and the schedule's next refresh lowers `Φ`
or leaves it equal; with `μ ≥ 0` and the witnesses verifying at least as often as standings age, `Φ` is bounded.

N1, N1b, N1c and N2 are the first theorems the Lean framework proves (chapter 06 "The gravitational wave test", phase 3 of the owner's plan), each
in both chains: Gravity from the DMP sentences down, Lean from the C# up.

### The two chains with Lean upward

| | Intent chain (down) | Evidence chain (up) |
|---|---|---|
| Language | Gravity (Bend 2 fork, `bend --verdict`) | Lean 4, core only (no Mathlib), one Lake package per section, toolchain pinned with the BendTT build's (v4.34.0) |
| Reads | DMP's sentences, then the IM chapter | the C# and what it observably does, never the intent model or `LAWS.bend` |
| States the laws from | the DMP sentences (`LAWS.bend`) | the DMP sentences again, by another author |
| Accepts a proof when | Bend's checker and the BendTT kernel accept it | `lean` accepts it, `#print axioms` lists nothing beyond `propext`, `Quot.sound`, `Classical.choice`, and the independent checker accepts the export |
| Folder | `CM.BlackHole/gravity/<section>/` | `CM.BlackHole/evidence/<section>/` |

### The shared root and how it is broken

BendTT's kernel is proven in Lean, so a fault in Lean's kernel could make both chains accept the same wrong thing. That is a shared cause
(T18), and it is declared here rather than hidden. Three measures break it:

1. **An independent kernel re-checks the evidence chain.** Every Lean proof is exported (`lean4export`) and checked again by a kernel
   written separately in another language (nanoda, in Rust; the knowledge warehouse idea I-0245). Lean's own kernel and nanoda must both accept it.
2. **The statement is pinned.** A proof is only as good as the statement it proves; the statement file is the DMP sentences restated,
   hashed beside the section like `LAWS.bend`, and a comparison tool (Lean's Comparator, I-0245) checks that the proven theorem is the
   pinned statement and nothing weaker.
3. **The models cross where the kernels cannot.** The coincidence check of the gravitational wave test runs the Gravity model and the Lean
   model on the same finite fact sets and compares answers, so a kernel fault that accepts a wrong proof still has to produce the same
   wrong answers in two models written from different sources.

What stays shared after that: the DMP text (on purpose), the C# (both read it), the world functions (A1 to A17), and the machines. A
proof chain that cannot run these measures yet says so in its row of the two-chain ledger.

### Evaluated: a built-in translation from IM to pure .NET C#

The owner's idea (2026-10-26 02:06Z): "Built in translation from IM to pure .NET C# ... might make writing proofs much easier ... without
changing the dynamics of their interacting surfaces." Every hop in the Milky Way net passes from a core component through a core component
to a core component, so whatever the translation produces sits on every path.

**What it would give.** If the C# that carries IM (the envelope and token types, the byte layout, the kind and permission tables, the
refusal reasons) is generated from one source, the code matches that source exactly, both ends of a hop run the same generated surface,
and a proof about the source holds for the generated code up to the generator (translation validation: the generator's output is checked
against the source on every build, so the generator itself does not have to be trusted blindly).

**What it must not break.** If the C# were generated from the intent model, the evidence chain would read a translation of the intent
model and prove it again: one source, two engines, one cause (T18), and C22's independence would be gone. So the source cannot be either
chain's model.

**Recommendation: yes, for the surface, from DMP's own tables, never for the decision logic.**

- Generate from the tables in DMP chapter 04 (kinds, fields, byte layout, permissions, refusal reasons), which both chains already read as
  text: a table is data, not a model, so neither chain owns it.
- Generate only the surface: types, codecs, the permission table, the refusal enum. The decision logic (the verifier's rule order, the
  replay memory, the gate) stays written by hand, so the evidence chain still reads code that nobody derived from the intent model.
- Each generated file carries the sha256 of its table and of the generator; a `dmp` rule refuses a hand edit and a stale hash. The
  round trip (table to C# to table) is checked on every build.
- Both chains model the generated surface from the table; the coincidence check confirms they agree on it, and the evidence chain's Lean
  model of the codec is the upward read of the generated C#.
- The interacting surfaces stay as they are: the generator reproduces today's wire bytes first (a byte-for-byte test against the current
  codec on the conformance campaign) before anything moves to it.

Where it lands: cm.io (the codec lives there), after the IM version negotiation of DM1, so that two ends on different IM versions negotiate
which generated surface they speak. It is not in this phase's build; it is the first item after the Milky Way node's evidence chain.

## Two chains: intent down, evidence up (design, nothing built)

The owner's requirement (2026-10-25): "The entire point of CMOS is to never trust any one interconnected system." The cascade runs
both ways. The **intent chain** goes down: DMP, then IM, then CM, which is what G1 built. The **evidence chain** goes up: from CM
through IM back to DMP. The two are independent end to end and check each other's state. Re-running the forward checks does not
count as a second chain.

### What makes the evidence chain independent

| | Intent chain (down) | Evidence chain (up) |
|---|---|---|
| Starts from | DMP's sentences | CM: the C# and what it observably does |
| IM model written from | DMP chapter 04, never the code | the C# (`ImVerifier`, `ReplayMemory`), never DMP chapter 04 or the intent model |
| Laws written from | the DMP sentences (`LAWS.bend`) | the DMP sentences again, by another author who never sees `LAWS.bend` |
| Engine | Gravity (Bend 2, BendTT kernel, whose proof is in Lean) | Lean 4, core only (no Mathlib), every theorem's axioms reported by the kernel and re-checked by an independent kernel (the owner, 2026-10-26: "lean to go from CM up towards DMP") |
| Author | a proof session | a blind session that sees neither the intent model nor its proofs |
| Cases | the conformance campaign | its own generator, or the exhaustive check below |

The owner chose Lean for the evidence chain (2026-10-26). This replaces the earlier recommendation of Dafny, which shared no kernel with
Gravity. Lean shares one root with Gravity, because BendTT's kernel is proven in Lean; that root is declared, and broken, in "The
constant", "The shared root and how it is broken".

Rules that keep the chains apart (a `dmp` rule, number at merge):
- `CM.BlackHole/gravity/<section>/` (intent) and `CM.BlackHole/evidence/<section>/` (evidence) never import or name each other's files;
- the evidence chain's prompt and inputs are listed in its folder, so the cold read can confirm what it saw.

### Where the chains cross-check

They meet at each layer, and each meeting is a check that turns the gate red:

1. **IM (checked, exhaustively).** The verifier's facts form a finite set once times are reduced to their order relations
   (the enumerations, the permission subsets, the orderings of not-before, expiry, sent at, now, skew and lifetime). The intent
   model and the evidence model are both run on every point of that set, and on bounded sequences for the replay memory. They must
   agree on every point. This is a proof of agreement by exhaustion: "checked", not "tested".
2. **DMP (reviewed and checked).** Both law files are translated back into sentences by a cold read, and both must match the DMP
   text. Both are also run as Boolean predicates on the same fact sets, where they must agree.
3. **CM (measured, at run time).** Milky Way logs the facts of each decision (never content, like the refusal log). A process
   outside the gate, in Black Hole, replays a sample through both models and compares them with what the gate decided.

### What a mismatch triggers

- **In the gate (build time):** red. Nobody presumes either chain right. A mismatch is a defect in one chain, in the C#, or in a
  DMP sentence that two readers understood differently. In the last case DMP gains an assertion with the owner's approval; it never
  loses one.
- **At run time:** the stricter answer stands, and an alarm goes to Event Horizon. "Stricter" means the message is refused if either
  model refuses it. Whether the replay later moves into the gate's own path, so that a mismatch refuses the message on the spot, is
  the owner's call when C1 and C2 reach Supernova level 5. I recommend alarm and quarantine of the affected kind first, in-path later.
- **Severity:** a failed law, a caught-but-unexplained mutant or a chain mismatch is a C∞ (C23) critical: it cannot be rated down and
  blocks merge and promotion. The finding names the most specific law it breaks (`C2.L1` style). A defect that no law catches, only
  C∞, is a stopgap catch: G1's mutant M3 was one until L2 gained its memory clause. Stopgap catches and the axiom count are kept as
  two separate counts in the planned ledger (`CM.BlackHole/evidence/LEDGER.md`).

### Shared roots that remain (said, not hidden)

- The DMP text: both chains end at it on purpose, because it is the human anchor.
- The C#: both chains read it, the intent chain through conformance and the evidence chain as its source.
- The world functions (parsers, crypto, chain validation; A1 to A17).
- The machines that run both chains. The run-time check runs on a different host from the gate.

### First slice of the evidence chain (R1, after the owner approves G1's sentences)

1. A blind session writes `CM.BlackHole/evidence/C02/Verifier.dfy` from the C# alone.
2. Another blind session writes the C2 laws in Lean from the approved sentences alone.
3. Lean proves the first against the second; the kernel's axiom report and the independent checker accept it.
4. `tools/gravity` gains `cross`: the exhaustive agreement check between the Bend model and the Lean model, each run over the same finite fact set and their answers compared.
5. Cold reads of both law files, compared with C2's sentences.
6. A mutant set of its own; each mutant must break the evidence chain too.

The DMP side of the principle is drafted as C22 "Prove forward and back: two chains, neither trusted alone" (an earlier change, awaiting the owner's own words): a layer counts as proven only when both chains agree, and a disputed part counts as unproven until they do.

## Two chains everywhere (C22, universal)

The owner approved C22 with this addition: "This approach applies universally throughout CM, it has to be implemented wherever it is
technically possible." So the two chains are a mechanism every CM component goes through. They are not a feature of C2.

### The mechanism, per component

1. **Intent chain:** a model written from the component's DMP text, laws from its sentences, proofs in Gravity, conformance against the C#.
2. **Evidence chain:** a model written from the C# (or read from the live system) by a blind session, the same sentences stated
   again by another author, proofs or checks in a second engine (Lean for code; a second, separately written reader for
   configuration).
3. **Cross-check** where the chains meet: exhaustively over a finite or abstracted input space where one exists, else on a shared
   campaign, plus cold reads at DMP and a run-time replay where the component decides things at run time.
4. **The two-chain ledger:** `CM.BlackHole/evidence/LEDGER.md`, one row per component, with the strongest pair it has reached:
   - both proved;
   - proved and checked;
   - checked both ways;
   - measured both ways;
   - not possible, with the technical reason.

   A `dmp` rule (number at merge) reads the ledger:
   - every component in `DMP/tree.txt` has a row;
   - "not possible" carries a reason;
   - a row only moves up, unless the owner approves otherwise.
   This is how "wherever it is technically possible" is enforced: an empty cell is a defect, and a reason is the only way to leave
   one empty.
   The rule cites C22's universality sentence (the owner's words, an earlier change). The ledger mechanics live here, not in the
   constitution.

### Where it is technically possible (first reading, 2026-10-25)

| Component | Intent chain | Evidence chain | Strongest pair | What limits it |
|---|---|---|---|---|
| IM verifier (`CM.MilkyWay` Verify) | Bend model from DMP 04, proved (G1) | Lean from the C# (R1) | both proved, cross-checked exhaustively | world functions (parsers, crypto) are assumptions |
| IM codecs (`cm.os` Im: envelope, token, heartbeat, frames) | grammar from DMP 04, round trip and strictness (G5) | a second parser written from the C# byte layout, or EverParse | both proved | none beyond the engines |
| Handshake (`CM.MilkyWay` Gate) | TLA+ from DMP 04, model-checked | state machine read from the C#, model-checked separately | model-checked both ways, traces cross-checked | bounded state space, not unbounded proof |
| Transport (TLS 1.3 setup) | settings from DMP 05, checked | settings read from the running endpoint, checked | checked both ways | the TLS library itself is the world (measured) |
| Doppler (enrolment, tokens, revocation) | register as a state machine from DMP, proved (revocation never undone, tokens only for enrolled subjects) | Lean from the C# | both proved | TPM hardware and its attestation are the world |
| Event Horizon (inspection policy) | policy tables from DMP, proved (every category decided, fail closed) | tables read from the C#, checked | proved and checked | domain lists change; each snapshot is checked |
| Trust (`cm.os` Trust: chains, roles) | role rules from DMP, proved | rules read from the C#, plus a second chain validator | proved and checked | X.509 parsing is the world unless a second library is used |
| Sagittarius fees and month end | fee rules from the contract text in DMP, proved over decimals | Lean from `FeeEngine`, plus replay of past months | both proved, past months measured | the contracts' wording; a person confirms each translation |
| Write gate, shield (C3, C5, C6) | proved (part 10, G6, G7) | Lean from the C# once it exists | both proved | not built yet (after MW0) |
| `tools/dmp` rules | each rule's sentence, checked on mutant repositories | the rule code read back into sentences, cold read | checked both ways | rule text is human; reviewed |
| Configuration of the estate (shapes, settings) | expected snapshot from DMP and shapes, checked | snapshot read live by a separate reader, checked | checked both ways | only as good as the snapshot's coverage |
| Owner scripts (PowerShell) | the script standard (DMP-011), checked | the script's syntax tree read back into what it writes, checked | checked both ways | run-time effect on systems is measured |
| Pictor, the monitoring module, cm.io | from their DMP sections once stable | from the C# | to be read when their sections are stable | no laws written yet |
| The running world (hosts, vendors' clouds, hardware, people) | DMP's expectations, measured | observations from a second source, measured | measured both ways | not provable; two independent measurements are the most there is |
| The engines (Bend, Lean, the independent Lean checker) | | | each checks the other's results | an engine cannot prove itself; the two chains are the check on them |

The order follows "Order after G1". R1 (C2's evidence chain) comes first, then each component's evidence chain is built beside its
intent chain, never later than it.

## Where DMP stands today

- `tools/dmp` checks eleven rules on `main` (DMP-001 to DMP-011) and two more are in drafts (DMP-012, DMP-013). All read files.
  None proves what the code does.
- Four constitution sections say `Enforced by: none yet`: C2, C3, C4, C5 (and chapter 14 "Completion goals, never time plans", which was C11
  until DMP 0.9.0). Chapter 00 calls a stable section without a check
  a defect.
- Chapter 04 lists invariants "for proving" (I1 to I6); today they are tests. The assumption audit of the verifier
  (`CM.MilkyWay/docs/supernova/assumptions.md`, A1 to A17) is the list a proof will rest on.

## Bend 2 on 2026-10-25 (read at commit 295e996, release 2.0.39)

- Laws in `LAWS.bend` ("the human states them"), proofs in `PROOF.bend`, explicit proof terms, no tactics or proof search.
- `bend --verdict` re-checks with BendTT, a kernel formalised in Lean (`bend2/bendtt.lean`, no `sorry`; claims: no checked
  definition has type Empty, live code halts; adequacy documented in 2.0.39).
- Not proven: Bend's own checker, the parser and the translation into the kernel. The compiler is "99% AI-written and not yet fully
  audited". Laws hold about Bend's semantics; backends may differ; floating point is axiomatic.
- No Windows (WSL works), clang 14+, targets C, Metal, CUDA, JavaScript. 40 releases from 17 September to 23 October, breaking
  changes in patch releases.
- Precise label: Bend 2 is proof-carrying, not self-verifying. Its trust rests on the Lean-proven kernel, and so on Lean.

## The assurance words

Every `dmp` rule declares the kind of evidence it gives, and the runner prints each section's profile. Nobody types "proven".

| Word | Meaning |
|---|---|
| proved | a law checked by Gravity's kernel for every input; about a model, so always with conformance and an assumption list |
| model-checked | TLA+ over every state of a protocol up to a bound |
| checked | a decision procedure over a finite thing (repository, history, configuration snapshot); exhaustive for that thing |
| tested | sampled: tests, fuzzing, Supernova, differential runs |
| measured | evidence from the running world: counts, drift, alarms |
| reviewed | a person or a blind session reads it |
| out of spec | Gravity's verdict on an instance whose ground is pressed, or on any configuration in which one influence can collapse the two witnesses into one (T-P, K0); never a weaker pass |

"Prove the whole infrastructure" means: prove the code CM writes, check each configuration snapshot exhaustively, measure the world.

## The constitution by reach

| Section | Reach (strongest first) | Today |
|---|---|---|
| C1 One door | checked (references, egress inventory), proved (T6 once the shield exists), measured | DMP-008 |
| C2 Every message verifies itself | proved (I1, rule order, I2), model-checked (I5, handshake), tested (conformance, parsers) | DMP-018 (laws locked; the proofs run in the gravity group) |
| C3 Every value carries its class | proved (part 10 T6 to T11, after MW0), tested, measured (labels right) | none yet |
| C4 Contracts change at promotions | checked (register hash on the history), model-checked (promotion protocol) | none yet |
| C5 Financial data freezes | proved (write gate), checked (released hash), measured | none yet |
| C6 Gates and grant | proved (write gate W1, W2), checked (DMP-009, DMP-012), measured, reviewed | DMP-009 |
| C7 One repository | checked (tree), measured (running estate against the repository) | DMP-004 |
| Chapter 10 "Language and runtime" (was C8) | checked; needs an exception for `.bend` and `.lean` in the law folders (it allows "a prover") | DMP-005 |
| C9 Roles, not values | checked, measured | DMP-001, DMP-006 |
| C10 Charter | checked (text present), reviewed | DMP-002 |
| Chapter 14 "Completion goals, never time plans" (was C11) | checked (no unmarked date in a plan), reviewed | none yet |
| C12 Knowledge back to the knowledge warehouse | measured, reviewed | DMP-002 |
| C13 the knowledge warehouse asked first | checked (the knowledge warehouse line in a PR body), reviewed | DMP-002 |

## How a section gets laws

1. A `Law:` block in the section: numbered plain sentences ("C2.L1 ..."), the statement of record. Constitution laws are approved
   like the constitution, by the owner, who reads the sentences and the cold read's back-translation, never the Bend code.
2. `CM.BlackHole/gravity/<section>/LAWS.bend`: one `law` per sentence, the sentence quoted above it. Its hash is written into the
   section (`Laws file: <path> sha256 <hex>`); DMP-018 refuses a law file that changed without the section, and a sentence that is
   not quoted word for word above its law. (Bend wants `LAWS.bend` beside the `PROOF.bend` that
   imports it, and an import path may hold only plain names, so a law file cannot live under `DMP/` and import a model under
   `CM.BlackHole/`: the three files share one folder, and the DMP section holds the hash instead of the file.)
3. The same folder holds the model written from the DMP chapter (never from the code), `PROOF.bend` and `mutants.json` (the named
   mutants); `tools/gravity` runs them and the conformance harness. The engine stays in the Gravity repository.
4. Cold read before the lock: a fresh session reads only `LAWS.bend` and writes what it guarantees; every named mutant must break a
   proof; every law gets a witness that its assumptions can hold.
5. DMP-018 checks the lock on every run (hash, sentences, the folder, the section's `Enforced by:`, that `tools/verify` still runs
   the gravity group). The `gravity` group in `tools/verify` on the Linux runner runs `bend --verdict` at the pinned
   Gravity tag, the mutants, the conformance check, and compares the `.bendtt` export's assumptions with the section's list.
   G1 adds the group (`tools/gravity check` and `conform`); it says "skipped" where Bend or Node is missing, which today is the CI
   runner, so until the runner has Bend a PR that touches a law folder pastes the group's output (see "Running it").

## Slice G1: C2 against the Milky Way verifier (built)

Files: `CM.BlackHole/gravity/C02/` (`model.bend`, `LAWS.bend`, `PROOF.bend`, `mutants.json`), `tools/gravity` (the runner, C#).

The laws of record are C2's `Law:` block in `DMP/01-constitution.md` (C2.L1 verified means authorised, C2.L2 one answer, fail
closed, C2.L3 no replay, C2.L3b a full replay memory refuses, C2.L4 the permission table). The owner approved them on 2026-10-25;
`LAWS.bend` quotes each sentence word for word above its law and C2 holds the file's sha256 (DMP-018).

Cold read before the lock (2026-10-25, `C02/COLDREAD.md`): a fresh session that never saw the sentences read `LAWS.bend` with every
comment stripped and wrote what each law guarantees. Its reading matches every sentence. What it found the laws do not say, kept as
the next laws to consider, not as defects of the five:

- No liveness: nothing says a correct message is ever verified (a verifier that refuses everything satisfies L1 to L3b; with
  `replayCapacity` 0 nothing verifies). Conformance against `ImVerifier` covers this today; a law would make it proof.
- L3 and L3b say the message is refused, not with which reason (`Replayed`, `ReplayCacheFull`); L2 limits it to those two.
- L3 covers the identical message; the replay memory's key (key and id) is what makes a resent copy the "same signed message".
- L2 does not say a verified message is written into the memory; L3 only needs that it is refused afterwards.
- L4 fixes the table; L1 checks the permission with its own copy of that table (written from DMP, on purpose not from the model).

What runs (output of 2026-10-25 on a Linux session, Bend 2.0.39 at commit 295e996, Lean 4.34.0 for the kernel):

- `tools/gravity check`: Bend's checker and then the BendTT kernel (built from `bendtt.lean`) accept every law; the elaboration the
  kernel reads is 91 KB. Six mutants, each building, breaking a proof, and told apart from the model by a concrete witness:
  M1 rule 9 skipped (L1), M2 skew on expiry (L1), M3 message signature checked after the replay memory, so a forged message poisons
  the memory and the genuine one is refused as Replayed (L2), M4 a full memory forgets instead of refusing (L3b), M5 the permission
  looked up for the wrong kind (L1), M6 rules 11 and 12 swapped (L2). M3 is why L2 speaks of the memory: without that clause no law
  told it from the model.
- `tools/gravity conform`: 3000 cases of a seeded campaign in the sandbox world (every enrolment state, foreign and expired signers,
  token windows at and beyond each edge, wrong keys, sessions, kinds, payloads, resent frames, byte flips, a memory of 6), sent as
  one sequence through one `ImVerifier` and through the model compiled to JavaScript: 0 mismatches, every one of the 19 refusal
  reasons and Verified reached. With mutant M2 put into the model, the same run reports 99 mismatches. The campaign is deterministic
  up to the world's fresh keys, so the counts per reason move by a few between runs; the verdict does not.

Assumptions and limits, beside A1 to A17:

- The facts come from the same world functions the C# uses (`FactProbe`: the strict parsers, chain validation, signatures, the
  register), each computed on its own; conformance tests the verifier's logic (order, time arithmetic, table, memory), not those.
- A token whose signer chain does not load counts as a token that does not parse (rule 3): same answer at the same place as the
  C#'s Malformed inside rule 4. Where a world function throws in rule 5, 9 or 11 the C# answers Malformed and the model has no such
  answer; the harness counts those cases ("outside the model") and checks that the C# says Malformed exactly there (none so far).
- Bend's JavaScript backend holds a Nat up to 2^48 - 1 and literals up to 2^32 - 1, so times are written relative to the world's
  start less 2^31 ms; a case with a time outside that range is counted and left out of both sides (1 in 3000). Running a
  program with big Nats in Bend's interpreter instead of compiling it takes minutes per case. Both go on G2's list: a Nat
  without the 2^48 bound in the fork's backend would remove the range limit.
- Still not proven after G1: the parsers (rules 2, 3, 15, I6), the handshake (I5), the C# itself, the world.

### Running it

Bend 2 is not a CM dependency. On Linux or WSL: install Bend 2 with bun at release 2.0.39, commit 295e996 (above, "Bend 2 on 2026-10-25"), Lean 4.34.0 for
`--verdict` (elan toolchain, or `lean` and `leanc` on the PATH), and Node. Then `CM_BEND="bun <bend>/bend2/main.ts" tools/verify`, or
only the group: `dotnet run --project tools/gravity/src/Cm.Gravity.csproj -- check .` and `-- conform [--cases N] [--seed S] .`.
A proof agent never edits `LAWS.bend`; `?name` in a proof prints the goal, and `bend PROOF.bend -o PROOF.bendtt` writes what the
kernel reads.

## Order after G1

G2 the Gravity repository (tracking fork, empty patch branch, NOTICE credit). G3 the assumption audit tool reading the
`.bendtt` export. G4 a second proof of C2.L1 in Lean, written separately. G5 the IM codec (round trip, strictness), in Gravity or
with EverParse. G6 the write gate (C5, C6, C15). G7 the shield (C3, C1) after MW0. G8 the first configuration check over a snapshot.

## Fork cost

The tracking fork costs one agent job per upstream release (about one a day at today's pace): build, upstream's gates,
CM's laws, tag. A .NET backend would mean owning a compiler (a second emitter beside `comp.ts`, 191 KB) and is not proposed;
conformance tests give the tie to the C#, and the Lean evidence chain reads the C# upward. The checker and kernel are never patched unless a
Supernova need forces it.
