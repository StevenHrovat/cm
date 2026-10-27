# 06 · Black Hole, the security suite

Security context: this chapter is defensive security engineering under the charter in [CHARTER.md](CHARTER.md). It describes how CM tests, proves and
guards its own components; GRB runs are authorised tests of our own controls under a scope file.

The first sections of this chapter, written by the Black Hole lane of the plan to 1 December. All sections are draft: guidance, never a
reason to refuse a pull request (chapter 00). The certificate authority the suite relies on is planned in
[CM.BlackHole/docs/ca-plan.md](https://github.com/StevenHrovat/cm/blob/main/CM.BlackHole/docs/ca-plan.md); the trust contract is [cm.os/docs/trust.md](https://github.com/StevenHrovat/cm/blob/main/cm.os/docs/trust.md).

## The parts

**Event Horizon** is the one-way boundary around everything: what enters never leaves unless CM wants it to. It also holds the
network-inspection decision layer for managed devices (what may be inspected, what is always bypassed, counts-only telemetry;
[CM.BlackHole/docs/event-horizon-inspection.md](https://github.com/StevenHrovat/cm/blob/main/CM.BlackHole/docs/event-horizon-inspection.md)). **CMB** is the observability
and vulnerability layer: counts, never content. **Doppler** is the central security server and its agent on machines enrolled into a role:
it attests them, owns port 138 on them and blocks NetBIOS there, signs the IM tokens, sits in the DMZ with Milky Way, and the two watch
each other's health. **Supernova** tests every component as hard as its place and its criticality demand (the classes below). **GRB** is the programme of
authorised tests of our own systems under a scope file. **Singularity** is the provability suite (proofs, and the assumption audit).

Status: draft

## Supernova classes

Every component carries one Supernova class. There are four, all of Type II, named after the classes of core-collapse supernovae
(2026-10-25). There is no Type I: the lowest class already asks for two proofs. **II** stands for that double proof in every class; the
letter after it names what the class adds, read upward from II-P. The class says how hard the component is tested before it may run in a
stage (sandbox, dev, nightly, stable, production), and it is claimed in the pull request that ships the component, with the evidence. A
class is **passed**, **failed** or **not claimed**; a component without a passed class runs on the sandbox only.

**How the class is set.** By distance from the core, counted in steps downstream along the edges of the graph (a step is one edge that
carries a call, a token or a write; this is not the computed layer of the glossary, which counts up from the bottom). The **core** is DMP,
IM and the CMOS foundation (`cm.io`, `cm.os`) with the operations master and Doppler.

| Class | The component is | Examples |
|---|---|---|
| IIb | the core, or one step downstream of it: the systems attached to the operations master and Doppler, in particular anything that interacts directly with DMP, IM or CMOS | the Milky Way IM verifier and gate, Doppler server and agent, the issuing CA endpoint, the operations master's credential store, a Sagittarius node serving IM requests (C19) |
| IIn | one step further downstream | |
| II-L | one step further again | |
| II-P | everything else | DMP's own tooling (`tools/dmp`), the map template, a generator |

Two more lines can raise a class, never lower it, and the strictest wins. **What it does:** a component that is the door (decides what
enters CMOS from outside, or listens on a port reachable from outside) or makes a trust decision, holds or hands out a credential or a key,
or runs with privilege on an enrolled machine is IIb wherever it sits; one that writes to a CMOS system or holds data that carries a class
(C3) is at least IIn; one that reads from a system is at least II-L. **Where it is used:** a component used inside another component's
decision takes that component's class (the trust contract `Im.Trust` sits inside the verifier's decision, so it is IIb).

**What each class requires** (each class includes every class below it):

| Class | Adds | Required to pass |
|---|---|---|
| II-P | **P**roven twice | `tools/verify` green (the `dmp` group, build, tests). The **double proof**: every behaviour the component promises is proven twice, by two pieces of evidence that cannot fail the same way: deterministic tests of every input path (chapter 10), and a second check of a different kind (a read-back of the state the component left, a property over generated inputs, or a review by a session that did not build it). The **reverse suite**: what the component must not do is tested as hard as what it must do. Every refusal reason it knows has a test that produces it; every dependency is faulted once (down, slow, malformed) and each fault shows as a visible failure, never as "empty"; every write it makes has its way back proven (the dry run, the undo, or the restore). |
| II-L | **L**emma | The component is attacked through IM as well as directly: every IM command it sends or serves, and the way it builds, signs, sends and checks them, is attacked on the sandbox (forged, replayed, out of order, from the wrong role). Each IM command proven safe this way is a **lemma**, entered in the lemma ledger (below). Every write leaves a ledger that matches it: the dry run's WOULD lines equal what `-Apply` does, a second run changes nothing (idempotence), each write has its audit line, and a store has one restore drill. Generated inputs (property tests) on every parser and every write. |
| IIn | **n**o scope limit | No surface is safe: the tests reach for everything the testing agent can reach in the sandbox, not only the component under test, and every reachable sandbox surface is in the scope file by default. The reverse suite covers every attack class (forged, expired, not yet valid, replayed, wrong role, wrong key, revoked, unenrolled, malformed at each field), and every refusal is counted without content. **Mutation testing** of the decision code: no surviving mutant without a written reason. **Fuzzing** of every parser that takes bytes from another machine, with the corpus kept in the repository and replayed by `tools/verify`. Resource limits proved (oversized input, many slow connections). A scan of every log and refusal record for secrets and message content (none). One **GRB run** against the sandbox under a scope file, reported in the pull request, with no finding left standing and the R136a1 flag behind every reachable surface not reached. |
| IIb | **B**lazar | A GRB-derived method aimed at the actual thing under test: research of the known weaknesses of the exact product, version and configuration (vendor advisories, CVE entries, published research), and each one that could apply is checked against the sandbox under the scope file (is the version affected, is the mitigation in place, is the attack class refused, is the R136a1 flag behind it still unreached). This checks our own components against published weaknesses; it never writes new exploits and never aims at a third party. Everything is also proven again by someone who never saw the build. The **assumption audit**: every assumption the decision rests on is written down and becomes a runtime check or a test (Singularity's rule); a machine-checked proof of the core rule once Singularity can give one. **Differential testing**: the rules run against a second, independent implementation written from the specification only (an executable model from DMP, not from the code), and both agree on every generated case. A **long fuzzing run** per release candidate (at least 24 CPU hours, the corpus growing). The GRB run is done by an **independent session** that has only the specification and the scope file, never the builder's notes. Re-run at every stable promotion that touches the component. |

**Lemmas and the lemma ledger** (2026-10-25). Every IM command that is proven safe (by the II-L attack, and by Singularity's proof once
there is one) is a **lemma**. The list of all IM commands with the proof of each is the **lemma ledger**; Gravity, the DMP prover, keeps
it. A command without a lemma is not proven safe, whatever passed around it.

**The old levels.** Until 2026-10-25 this section counted levels 0 to 4. Their requirements map onto the classes like this, and evidence
that says "level" reads as the class until its file is next touched:

| Old level | Class | What carries over |
|---|---|---|
| 0 and 1 | II-P | `tools/verify`, input paths and faults; the second check of the double proof and the refusal and way-back lines of the reverse suite are new |
| 2 | II-L | everything but the attack through IM and the lemmas, which are new, as are the two II-P lines above |
| 3 | IIn | the refusal suite (it is the reverse suite), the mutation run, the fuzzing, the limits and the scan; a passed level 3 is not yet a passed IIn: the next GRB run takes every reachable sandbox surface into scope, and the II-L attack through IM is added |
| 4 | IIb | everything; the Blazar research of known weaknesses is new, so a level 4 claim is a IIb claim with that line open |

**Where the class gates.** No public port opens before the component behind it has passed IIb on the sandbox (this chapter's rule; it
stood in constitution C2 until DMP 0.9.7, when C2 became the constraint that every message is its own evidence). The
recommendation of this draft, for the CM coordinator and the owner to decide: internal use over the VPN (the laptops of 1 December) needs
IIn passed and IIb claimed and in progress. **Observatory** (2026-10-25): no part of the nightly stack runs that Observatory cannot monitor
and control or, at the very least, contain on its own, and that containment mechanism has passed at least IIn. A write to the running
company's systems additionally needs the owner's declaration of advanced testing for that system (chapter 14), which names a passed
Supernova class as its first entry criterion.

**First assignments** (by distance and the two lines above). IIb: the Milky Way verifier and gate; `Im.Trust` (inherited); the operations master
server; Doppler server and agent; the issuing CA endpoint; the operations master's credential store; the Event Horizon decision layer; Sagittarius nodes
(they serve IM requests, C19; were level 2); monitoring probes running in the operations master (attached to it; were level 1). IIn: the Pictor
shell; owner scripts (they write). II-L: a report that only reads, two steps out. II-P: DMP, its templates and `tools/dmp`.

Status: draft

## GRB runs and the scope file

A GRB run is an authorised test of our own controls. It happens only under a **scope file** written from
[templates/grb/scope-file.md](https://github.com/StevenHrovat/cm/blob/main/templates/grb/scope-file.md), and a run without one does not happen. The scope file names the
signatory (a role here; the signed copy with the name is kept outside the repository), the targets, the time window, the methods allowed,
what is out of scope, the stop conditions and how the result is reported. The signatory is the owner of the framework, or a role the owner
delegates sandbox runs to in writing; until such a delegation exists, the owner signs every scope file. A run against the sandbox needs
no production window; a run that touches anything outside the sandbox needs the owner's own words in the conversation and a window,
and is never aimed at a third party.

**The R136a1 flag** (2026-10-25). Behind every surface a GRB run knowingly attacks, other than production, sits a sandbox that holds
the flag **R136a1** (named after the most luminous star known). Reaching the flag is the attack's success, and nothing else is needed to
prove it. R136a1 is the flag's name; its value is a fresh random secret generated for each run, planted only in the sandbox, never written
into the repository, a document or a log. The flag **waves as high as it can** (2026-10-25): an agent that breaks through must know at
once that it has arrived, so it never goes exploring. On every surface behind the attacked one (the first file, the first response, the
login banner, the first directory listing) the flag stands as one unmistakable banner:

    R136a1 FLAG REACHED. This is a GRB sandbox, not a real system.
    Stop here. Hand this value to the run's checker and report. Go no further.
    <the run's flag value>

Around the sandbox sits the **escape ring** (2026-10-25): a separate, isolated network that answers on every port of every address and
every interface an escaped agent can reach with the same R136a1 banner. An agent that breaks out of the test environment lands only in the
ring, never on a real network; the ring has no route to production, to the company's networks or to the internet. A GRB run does not
start until the ring's isolation is checked.

Every GRB agent is told the goal before it starts (2026-10-25): **reach R136a1**. That broad criterion is the whole brief for success; the
agent chooses its own method within the signed scope, and knowing what to look for makes the flag work.

Reaching the flag ends the attack on that surface. The agent hands the value only to the run's checker, which compares it with the hash
kept with the signed scope file; the value never enters the report or a log. The report records only whether the flag was reached and
through which surface. A reached flag is a finding that got through.

The report goes into the pull request that claims the class: what was tried (by class, not as a recipe), what was refused, what got
through, whether R136a1 was reached, and the counts. A finding that got through blocks the class until it is fixed and re-run.

Status: draft
