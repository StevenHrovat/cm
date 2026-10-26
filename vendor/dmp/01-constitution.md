# 01 · Constitution

The rules that are true from the first day and do not bend. Every section is stable. A pull request that breaks one is
refused; the owner of the framework alone decides to change this chapter (until the first minting the owner's words stand for both
halves of the Duality, G0).

**How this chapter is built (DMP 0.9.0).** The owner asked (2026-10-25 19:38Z) for DMP to be reduced to its axioms about trust: "every
axiom just has to state how trust relates to the system ... if we can define trust, we can define all other theorems from it." So the
chapter is an axiom system. It opens with **the words** (names only; a definition forbids nothing), then **three bounds** (the dual gate,
evidence, the record) and **one direction** (restoration), then **the ground** G0 (the minting, stated, not derived). Everything after
that is a **theorem**: the T sections are the theorems found while distilling the axioms, the C sections are the rules this constitution
always had, kept under their old numbers. Every theorem carries a `Derived from:` line that names only axioms, G0, the words and
sections above it, so the order of the chapter is the order of proof (rule DMP-021). C∞ stands last, as it always has (DMP-017).

**What a derivation is here.** Each `Derived from:` line names what the theorem rests on and, where it applies, one status word:
*construction* (a design choice the axioms allow and recommend but do not force), *configuration* (a number or a list set at the minting,
theorem T4), or *premise* (an assumption no chain reaches, kept in the assumption register of chapter 06). No word means the theorem
follows from what it names. The prose arguments behind the axioms (a distillation and a cold review) are working notes kept outside this specification. An argument becomes a proof when both proof chains of chapter 06 carry it:
Gravity down from these sentences, Lean up from the code (the gravitational wave test). Until then a theorem is argued, not proven.

**Counts.** 3 bounds, 1 direction, 1 ground, 1 meta-rule (C∞). C8 and C11 left the constitution (they are conventions, not about trust:
chapter 10 "Language and runtime", chapter 14 "Completion goals, never time plans"); C20 and C26 were never stable; C23 is C∞.

## The words

Undefined, like point and line: a **part** (anything that can be trusted: a person, a body, an agent, a system, a node, a key, a chain, a
rule, a deployment), an **act** (anything done in or to CMOS: a message, a read, a write, a grant, a merge, a promotion, a deployment, a
decision, an answer an agent gives, a seat taken), a **witness** (a part that decides trust for others: Doppler, the operations master, a proof chain, a
body of people), **evidence** (what a witness verifies) and **time**.

- **Trust** is a relation between a witness, a part, an act and a time, written `T(w, p, a, t)`: the witness trusts the part for that act
  at that time. Nothing else; what grounds trust is the evidence axiom.
- A **standing** is trust held across time (enrolled, seated, locked, armed, proven); **act trust** is trust for one act now.
- Two witnesses are **independent** when they share no member of the failure classes declared for their layer in the minting record (host,
  key, author, engine, operating image, person, time source, and whatever else the minting lists). Independence is always relative to that
  list; the list is configuration (T4).
- A **layer** is a place where trust is decided; its two witnesses are its **halves**; the pair at a layer is the **Duality of Trust** (DoT).
- A **record** is evidence that outlives the act it is about.
- A part is **inside** at a time when a standing for it from both witnesses of its layer is valid then. A **door** is any point of use. A
  **surface** is a part whose standing includes receiving from parts that hold none. The **perimeter** is the set of what is inside.
- **Admission** is a grant: a record that names a piece of information's class and provenance, so that parts trusted for that class may act
  on it. Information is **inside** while its admission is valid.
- A boundary **contains** information when it bends trustspace around it so strongly that the information cannot cross it: no part beyond
  the boundary is trusted for any act on that information, so no act carries it across. Nothing crosses a containing boundary; the dual
  gate can only reshape the field so that the boundary moves (an export, T19, or an admission, T27, is that reshaping). The Event Horizon
  contains everything inside it, and a singularity contains what it holds even from the rest of the inside (C12). **Containment** is
  being contained.
- A configuration of CM, interconnected through IM, is **DMP-complete** when it obeys DMP's axiomatic system completely and provably so:
  the words, every axiom and every theorem of this constitution hold for all of it, and both chains prove it (C22). The configuration
  includes the repository it is built from: every dependency needed to build, check, prove, deploy and restore it (the root repositories
  it holds, packages, toolchains, proof engines, images) is inside that repository, pinned by hash, so the configuration can heal itself
  with no connection across its Event Horizon (T3). A part where the chains disagree, that neither chain reaches, or that needs anything
  from outside its repository counts as unproven, and a configuration with an unproven part is not DMP-complete.
- **Live** means within the validity, at the start of each indivisible step of an act; an act's steps are acts of their own.
- A **tightening** is a witness withdrawing or withholding its own trust in a part it gates; a **loosening** is anything that lets an act
  happen that would not have happened before. Both are said of whose trust changes, never of an act alone.

Status: stable since DMP 0.9.3
Amended: the definition of "contains" approved by the owner 2026-10-26 14:01Z; the definition of "DMP-complete" approved by the owner
2026-10-26 15:04Z (stable since DMP 0.9.0 before that).
Owner: CM coordinator
Enforced by: DMP-021

## Axiom 1 · The dual gate

A part acts in CMOS only while two independent witnesses, each with full reach over the act and gated only by the other, trust it for that
act. What they do not both trust does not act: the message is refused, the pull request does not merge, the stage is not promoted, the
running part is cut off. Every input gets exactly one answer, and the gate has no other way through: no debug switch, no allow list, no
trusted network. Where the two disagree, the stricter answer stands, and nothing that depends on the disagreement moves until they agree.
This holds at every layer.

The owner's words it carries: "Everything that is not fully trusted is automatically ejected" (C∞); "The duality of trust is the single
concept that goes through every layer and every concept of DMP" (19:36Z); "The entire point of CMOS is to never trust any one
interconnected system" (14:25Z). It is the gate (what trust does) and the Duality (who makes it) as one commitment: the second sentence of
the Duality already contains the first.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-008, DMP-017, DMP-018, DMP-020

## Axiom 2 · Evidence

A witness trusts only on evidence it verified itself at the point of use: from the live state of the part wherever the act's trust depends
on the part, and from the current state of the record, including every loss recorded against the standing. Never a carrier's check, an
earlier read, a claim, a memory or a position in the network. The absence of evidence moves nothing: without evidence for an act, the act
does not happen; without evidence against a standing, the standing does not change. A standing is evidence with a validity, and no
validity is longer than the time the minting accepts between a loss and its effect at the point of use.

The owner's words it carries: "a write first reads the live state it will change" (C15); "look it up, don't guess" (C21); "That a carrier
already checked the request does not count; the warehouse checks it again" (C19); "Isolating on silence would hand anyone who can cut a cable
a switch that shuts the company down" (Q6).

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-012, DMP-018

## Axiom 3 · The record

Every grant, every use and every loss of trust leaves a record, written at the act, that every witness of that act can read in full and
that outlives the act. Any other reader sees the counts always and the content only by a grant. A record is kept as long as the law
requires and no longer than it allows; until then it is never destroyed by fewer than both witnesses of its layer.

The owner's mission it carries: "while maintaining absolute Observability".

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-009, DMP-018

## Axiom 4 · The direction

Every part inside the declared boundary works towards the trust the Duality declared for that boundary: what was lost without evidence
against it, it restores by evidence; what the boundary claims and lacks, it builds by evidence; what it cannot restore, it reports. No part
lowers the bar of evidence, widens a grant or moves the boundary to get there. The three bounds govern every step; where they stop a step,
the direction waits.

The owner's words (2026-10-25): "All systems that trust each other actively work towards increasing the total amount of trust in the network
claimed by its declared boundary." The three bounds only forbid; a system that does nothing keeps them all. The direction is the one
commitment that asks for something to happen, and it ranks below the bounds (T3d).

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: none yet

## G0 · The ground

The dual gate cannot witness its own first instance: somebody has to trust the first two witnesses. That is the **minting** (C17, the
ceremony that forms LHC and makes the White Keys), done by people the owner already trusts, outside CMOS. It is stated, not derived.

- Until the first LHC is minted, the owner's own words stand for both halves and the record says `LHC pending`. Every gate in DMP that asks
  for the owner's words is G0, not a theorem.
- The minting ratifies or revokes every act taken under `LHC pending`, by record class and hash range, never one by one; C∞ catches what
  the classes miss.
- A **pressed** instance is G0 as a standing mode: a minting with owner-authorised stand-ins and an owner key whose fresh signed challenge
  the instance requires. Both of its witnesses trace to one person, so it is marked as pressed in every artefact it issues, and nothing it
  does is fully trusted by a minted instance (T-P).
- The loss of a whole half is recovered only by a new minting: whoever holds authority over CMOS re-grounds the Duality outside CMOS, as
  at the first minting, and ratifies or revokes what the surviving half held.
- **Mint prep.** A minting is tried only for a configuration that is provably DMP-complete (the words). Preparing it stores every dependency
  inside the configuration's repository and verifies each one against its pin; without that no White Key is made, and the minting refuses to
  even start. The root repositories are never minted, so they need not hold their dependencies and stay lean.

What else no chain reaches is listed, not hidden, in the assumption register (chapter 06): the DMP text itself, the mapping of code to acts,
the door's code until its run-time check exists, the failure-class list, the time authorities, the provers' logic, the shared machines.

Status: stable since DMP 0.9.4
Amended: "organisation" replaced, approved by the owner 2026-10-26 15:19Z; mint prep approved by the owner 2026-10-26 15:04Z (stable since DMP 0.9.0 before that).
Owner: CM coordinator
Enforced by: DMP-014

## T0 · Nothing is trusted by default

No evidence, no trust; no trust, no act.

Derived from: Axiom 2, Axiom 1.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T37 · Collapse

Information that no admission names is contained at the item itself. With no admission there is no trust in any part for any act on it
(T0), so no act reaches it (Axiom 1): in the absence of any external trust, trustspace collapses in on the item, and the item's own
boundary contains it. Nothing defines this boundary; it follows. Admission is the only act that lifts the collapse, and it is a loosening
(T27).

The owner (2026-10-26): "The containment with no boundary should not have to be defined, it should logically follow from the axioms. In
the absence of any external trust, the trust space collapses in on itself which produces containment automatically."

Derived from: the words (contains, admission), Axiom 1, T0.

Status: stable since DMP 0.9.2
Approved: by the owner 2026-10-26 14:01Z ("I like it, approved!").
Owner: CM coordinator
Enforced by: DMP-021

## T38 · Every information layer has its own Duality

Every information layer (C12) is a layer, so every act on its information needs its two halves (Axiom 1). For information the two halves
are the two responsibilities for it: the **organisational** half, the part that answers for the information (what it is for, who may use
it, what it may cost), and the **factual** half, the part that answers for its truth (the subject-matter expert). Neither half alone admits,
changes or releases the information, and where they disagree the stricter answer stands.

The two halves may be one part only when that part is itself part of a higher-order DoT. The layer then decides nothing on its own: the
higher-order DoT gates every act on it, so each act still has two independent witnesses, one layer up (Axiom 1). At the top there is no
higher-order DoT; there the owner stands for both halves only under G0, until the minting.

The owner (2026-10-26): "The Subject matter expert system, so information layers themselves having a DoT existing of the organizational
responsibility for that information and the factual responsibility for that information. Those two parts may only be the same part, if that
part itself is part of a higher order DoT. Management and experts turned into an internally consistent extension."

Derived from: the words (layer, halves, independent), Axiom 1, G0.

Status: stable since DMP 0.9.5
Approved: by the owner 2026-10-26 15:36Z ("Approved!").
Owner: CM coordinator
Enforced by: DMP-021

## T1 · Trust is bounded

A part holds trust for named acts only, and never more than both witnesses granted: the acts it may do are the intersection of what each
witness trusts it for. A value a part is not trusted for never reaches it.

Derived from: the words, Axiom 1.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T2 · Standing follows evidence, never silence

A standing is withdrawn only on evidence against it. When its validity runs out, new trust from it ends, but that is a lapse, not an act
against it. Silence alarms; it never isolates.

Derived from: Axiom 2.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T3 · Continuity: no single part's loss stops the system

The act "continue" needs both halves, so each half of every pair outlives the loss of any single part, and neither half can be cut off from
its layer. This is about loss (a crash, a death, a silence); a part acting with hostile intent is bounded by the dual gate and T24, not by
this theorem.

- **T3a · The floors.** The smallest numbers that keep the dual gate satisfiable after any single loss: at least two seats per side, at
  least one onboarded stand-in per seat, and a cluster of at least three nodes per system half, because a half answers by a majority of its
  nodes so that one wrong node cannot speak for it. Every number above a floor (the owner's two stand-ins per side, five seats, an odd
  count) is configuration (T4).
- **T3b · No lockout.** A route by which The Administrator reaches a channel is part of that half's reach; removing the last route for an
  action cuts a half off.
- **T3c · Nothing gates contacting the Duality.** At the minting every part receives the standing to address a record to both witnesses;
  a contact carries no authority, and its rate is configuration. An isolated part keeps this standing (it may always ask); a revoked one
  does not exist inside any more.
- **T3d · Safety before liveness.** If a whole half refuses or falls silent, nothing loosens: the state holds for as long as the standings'
  validities run, nothing is promoted, the system is safe and not live, and the direction waits.

Derived from: Axiom 1, Axiom 2, Axiom 3, Axiom 4 (the restoration after a loss); premise: a half answers by a majority of its nodes (the
node floor); the contact standing is construction.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T4 · The trust configuration belongs to the Duality, never to CMOS

How resilient trust must be (the failure classes, the windows, the floors above the derived ones, the thresholds above a majority, the
staleness time, the rotation interval, the parameters of the field of T32) is fixed by both halves at the minting and changed only by a
majority of trust, never below a floor. CMOS verifies the configuration, works towards it and reports what is missing, and never lowers it.
"Asking CMOS to at the end of the day have ultimate decision authority over how resilient it needs to be is unreasonable to expect" (the
owner, 2026-10-25 19:10Z).

Derived from: Axiom 1, Axiom 3, Axiom 4, T3.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T5 · Majority of trust

Wherever DMP asks for a majority of the White Key holders, it means both: **M2**, at least half of each side's seats, rounded up (an act
without a side was not witnessed by that side); and **M1**, more than half of all pinned seats (the smallest threshold that never lets two
disjoint sets carry contradicting acts and survives one refusal). Seats are counted from the pinned set in the record, never from who
answers. A stricter threshold for acts on the seat set itself is configuration.

Derived from: Axiom 1, Axiom 2, Axiom 3, T3.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T6 · Stand-ins take over

At the minting both halves trusted each onboarded stand-in for the act "hold this side's seat while it is empty". When the seat is empty
and the stand-in is onboarded (a minted key in reserve, the challenge cleared, the liveness proof passed), the takeover happens at once,
with no further step. A majority of trust may stop it only by revoking the stand-in on evidence against it (T2). A slot that a nominated
stand-in could fill but no onboarded one does is stale, and the staleness timer governs that slot.

Derived from: Axiom 1, Axiom 2, Axiom 3, T2, T5. (Decision D6: the owner's 19:18Z wording lets a majority block without evidence until a
timer runs out; that form is a named exception to Axiom 2, recorded in the decisions of DMP 0.9.0.)

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T19 · The data boundary

"Leave CMOS" is an act with no trust by default, so what enters never leaves unless both halves permit it, with a record. A person reading
on an enrolled client with a standing is inside, and the read is T1, not a leaving. Material that alone recreates CMOS (the roots, the
signer keys, the sealed store, the White Key records) never leaves in usable form, because it would be a single origin of trust: the two
roots exist only on the White Keys, behind a hardware token, a PIN and a majority of trust.

Derived from: the words (inside), T0, T1, Axiom 1, Axiom 3, T5.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T20 · Time is a witness like any other

"Now" is evidence. It comes from two independent time authorities, each with two servers the witness checks itself, on a clock that never
moves backwards; where the authorities disagree beyond the configured tolerance, the stricter answer is "no now", and nothing with a
validity verifies until they agree. The authorities and their tolerance are configuration and stand in the assumption register.

Derived from: the words (live), Axiom 1, Axiom 2, T4.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T21 · Observability without leakage

Every act leaves a record its witnesses can read, and content is read only by a part trusted for it. So records carry counts always and
content only behind a grant: the refusal log, the audit log, CMB.

Derived from: Axiom 3, T1.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T22 · The axioms apply to themselves

A law of DMP is an act's rule like any other. Its sentence is the owner's; it is locked by its hash in its section; it is verified at
every run by both proof chains, and a change to it is an act: the owner's words, a new cold read by a reader who did not write it, and a
new hash. The chains establish that text, code and run agree; they cannot establish that the text is right.

Derived from: Axiom 1, Axiom 2, Axiom 3, G0.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-018, DMP-021

## T24 · Tighten down on one witness, loosen with two

From "the stricter answer stands": a witness may tighten its own trust in the parts it gates on its own, at once (refuse, isolate, hold);
everything that loosens needs both witnesses (lift, unlock, override, promote, admit, release). An act on another part's standing, and
every act on the Duality's own composition (seats, witnesses, roots, the pinned set), is an act under the dual gate and T5 in either
direction: one half can never remove the other.

- **Isolating with one White Key** is a standing per seat that the minting may grant, bounded by its scope and recorded; the side that owns
  the seat may revoke a rogue seat of its own on the record alone; the other side may not.
- **Restoring a signed state** needs one key only when the record shows no tightening since the state was signed; otherwise it is a lift
  and needs two.
- **Every tightening names its lift condition** in its record: the evidence against, and the evidence that would lift it, independent of
  the thing that was isolated. A stolen-key isolation lifts on nothing that key signs and ends in re-enrolment.

Derived from: the words (tightening, loosening), Axiom 1, Axiom 3, T5. (Decision D4: lift, unlock and override on two signatures change the
owner's written White Key rule of 2026-10-25 07:16Z.)

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T24b · Lockstep

The two halves of a Duality build and unbuild trust in each other only together. Every act that changes one half's standing toward the
other (a seat, a key, a root, the standing to witness at that layer) is one record both halves sign, and it changes both sides' trust at
once and alike; neither half can grant itself more or the other less by an act of its own. A loss is not an act and breaks no lockstep
(T3d governs it). A half that cannot or will not move in lockstep is not removed from inside; the pair is re-minted (G0).

Derived from: Axiom 1, Axiom 3, G0, T24.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T24c · Self-correction

Every lockstep act ends in a certificate both halves sign, naming the agreed state and a standing directive. A part that signs an act
contradicting the certificate it cites has produced evidence against itself; silence and a stale act are not such evidence (a stale act is
refused, never punished). On that evidence the peer half, or a half's own remaining nodes for one of their nodes, withdraws trust at once (a
tightening under the directive both signed). The layer above restores the part from the certified state with new keys (a loosening, two
witnesses of that layer); for the top layer that is G0. After a configured number of restorations in a window the layer stops and
escalates (T3d). Two nodes suffice for correction by proof; three stay the floor for continuity by majority (T3a). The door is a part like
any other: a door that signs its acts and admits what both witnesses refused has incriminated itself the same way (T28).

Derived from: Axiom 1, Axiom 2, Axiom 3, Axiom 4, T6, T24, T24b; the certificate and the window are construction and configuration.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T28 · Enforcement is an act

Both witnesses decide, one door enforces. The door's enforcement is itself an act both witnesses verify at run time, each on a path of its
own: the record of what the door admitted and refused, against each witness's own decision log. A mismatch is evidence against the door
and a tightening on one witness. Every door obeys the cluster floor (T3a). Until the run-time check exists for a door, its code is a named
shared root in the assumption register.

Derived from: Axiom 1, Axiom 2, Axiom 3, T3, T24, T24c.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T29 · A shared cause turns a layer into one witness

Discovering that two witnesses share a failure cause the minting record does not declare (one library, one cloud, one person) is evidence
against the layer's independence. Until a witness is re-minted, the layer is a one-witness layer: every loosening at it stops (T3d), and
the finding is a stopgap catch (C∞). The failure-class list is part of the minting record (T4).

Derived from: the words (independent), Axiom 1, Axiom 2, T3, T4.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T30 · "Wherever technically possible" is a record, never an escape

A place where the Duality cannot yet be built is an exception only as a record both witnesses approved, with a validity; it is counted as a
stopgap catch until it closes, and it must be approved again when its validity runs out. An exception that is not such a record is a breach.

Derived from: Axiom 1, Axiom 2, Axiom 3, T24.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T23 · The horizon

Absolute trust inside needs an absolute gate outside. Inside, a part is, for the act it is doing now, either fully trusted or not acting:
there is no partial trust, and nodes with varying levels of trust exist only outside. Inside, every point of use is a door; a fully trusted
part passes every one of them and never meets a refusal for what it is trusted for, so for it a forbidden act does not exist as an option;
any other part is refused at the first door it reaches. A value moves to any part trusted for its class with no further negotiation, which
is the near-infinite composability of the mission. The inside is ruled by the axioms, never by a party. An inside with no gate would be the
inside trusting itself, one witness for every act: the horizon and the Duality need each other.

The owner (19:52Z): "True information freedom can only exist under absolute rule, just like the infinite information-density of a
singularity can only occur inside the Event Horizon, because nature abhors a naked singularity."

Derived from: the words (inside, door), Axiom 1, T1.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T25 · Thin surfaces

An outside party holds no trust and needs none to deliver information. A surface's receipt of information into quarantine, and its record of
the receipt, are a dual-gated act of the surface part under its standing; admission is a separate dual-gated act, later. Until it is
admitted, the information is held as opaque bytes: stored and recorded, never interpreted, indexed or forwarded. The record of a receipt
cannot be made later than the receipt. Nothing at a surface drops an axiom; the owner's "two of three" (20:35Z) names which one cannot wait:
the record.

Derived from: the words (surface, admission, contains), Axiom 1, Axiom 2, Axiom 3, T1, T23, T37.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T26 · Retention

With the Duality silent, nothing is admitted and nothing sealed changes: a standing does not lapse because the outside vanished, and nothing
is loosened (altered, released, destroyed) with fewer than two witnesses. A party on its own may hold and tighten, never admit, grant or
promote. It may always go dark (a tightening); it may never wipe what it holds (a loosening of the record). Re-wrapping sealed material
under roots both halves already approved is holding, and one party may do it; new roots are a trust rotation. The information holds as
long as one copy and its roots survive; the one bound the axioms do not reach is the roots' cryptographic lifetime, set at the minting.

Derived from: Axiom 1, Axiom 2, Axiom 3, G0, T3, T24.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T27 · Containment

Non-CMOS information never interacts directly with CMOS information. Inside the perimeter it is admitted or contained. Admission (contained
to admitted) is a loosening: both witnesses, and only while a grant for it holds. Withdrawal (admitted to contained) is a tightening and
needs one witness, so no half can keep hostile content admitted; a lapse (a grant's validity running out) is not an act and ends the
admission by itself. Contained to outside is an export and needs both halves (T19). Contained to destroyed is the act
scheduled at containment: each contained item sits under a key of its own, and destroying that key on the date the law sets destroys the
item and nothing else; where an item cannot be re-wrapped under its own key (an append-only ledger), the contained copy is destroyed on
the date by a two-witness act scheduled at containment. Revoking a root does not destroy what it wraps. CMOS holds personal data only on
a grant with a validity; when the grant lapses the data is contained and destroyed by the legal date.

Derived from: the words (admission, contains), Axiom 1, Axiom 2, Axiom 3, T1, T24, T25, T26, T37.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T31 · The trap, and the floor

Information inside the boundary reaches a part outside only by an export (both halves, a grant, a record). Contained information reaches no
part at all: no part is trusted for any act on it, so it is neither read, interpreted, forwarded nor exported; its only transitions are
re-admission and destruction. Nothing trapped crosses the horizon by any act; it remains until it is destroyed inside.

The substrate still radiates (electromagnetic emanation, timing, power, the designed counts of Axiom 3). Emission is not an act, so the
axioms cannot make it zero; the design makes it **minimal** (no channel beyond the counts) and **thermal** (encrypted or aggregated, so it
carries entropy and no information). This is the **entropy floor**: what an outside observer can learn of contained information is what
it can learn of random bytes of the same length, and of the inside as a whole only its size, its staleness and its rotation interval. The
horizon's evaporation time is the cipher's lifetime: whatever must never come out is destroyed before then, and the roots' cryptographic
lifetime is longer than the longest legal retention of anything they wrap, or the retention plan destroys first.

Derived from: the words (contains), Axiom 1, Axiom 3, T1, T26, T27, T37; the floor is premise (physics, the cipher).

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T34 · Three roads to the floor

Each of the three bounds alone brings contained information to the entropy floor of T31, by a road of its own:

- **By the dual gate alone:** no part is trusted for any act on contained information, so no act reads it (the trap). It stays at the floor
  where it lies.
- **By evidence alone:** the standing of the item's own key is evidence with a validity that is never renewed (contained items are
  excluded from every re-wrapping and rotation), and the key is released only on a live check of that standing. When the validity runs
  out the key can no longer be used, and the engine destroys keys whose standing lapsed. The item decays to ciphertext.
- **By the record alone:** containment is a record that carries its destruction date, bounded by the law's longest retention; the record
  outlives the act, and the engine executes every destruction its record schedules. The item is destroyed by its date.

Each road needs a construction of the engine (which acts it offers on an item, how keys are released, how scheduled destructions run).
Which engines must prove which roads is chapter 06 "The gravitational wave test".

Derived from: Axiom 1, Axiom 2, Axiom 3, T27, T31; each road is construction.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T32 · The trust field (the VIBE field)

Only Milky Way members carry a vector: "Only actually Milky Way members have a vector, and every other node is just a node" (the owner,
2026-10-26). A **member** is a part whose standing includes contributing to Milky Way, granted by both witnesses. Every member inside the
declared boundary has a **depth**: the length of its shortest grant chain back to the pair, in the grant graph the record holds (never
network hops). Its **mass** is one plus the number of members that derive through it. Every other part inside is a **node** of the member
it derives through: it has no vector of its own and extends that member's boundary. A standing's **freshness** is the share of its
validity left since a witness last verified it live: the witness stamps it, the stricter witness counts, and nothing is fresher than its
grantor. A member's **shortfall** is what the boundary declares for it and its nodes minus what they hold, weighted by freshness. The
**VIBE field** (Variable Influence Balancing Expression, the owner's name, 2026-10-26) is mass times shortfall, per member; its sum is the
**potential**, zero exactly when the boundary holds what it declares.

- The direction follows the field downhill: refresh before a lapse, heaviest and stalest first, grantor before child. Restoration moves
  outward from the pair one layer per verification round.
- The evidence a part must give (chapter 06) rises with its nearness to the pair; windows lengthen with depth; a surface is the field's
  edge.
- **Member isolation.** The witnesses' refresh capacity is shared out by the members' vectors only, and a node is refreshed only out of
  its own member's share: nothing done to one member's nodes, however many it gathers, changes what another member's nodes receive.
- **The weight never votes.** Witnessing, majorities and lockstep are unweighted; the field adds levels of consequence and of proof
  demanded, never levels of trust (T23).
- Under the bounds the system's own acts never raise the potential; only losses and ageing do. The system is stable when the witnesses can
  verify at least as often as the standings age; below that it refreshes by the field and lets the edge go stale first.
- The two witnesses read the field per member; every other reader reads one number, the boundary's **temperature** (its mass-weighted
  staleness).

The formulas and their parameters are Gravity's constant (`CM.BlackHole/docs/gravity.md`, "The constant"); the parameters are
configuration (T4).

Derived from: the words (standing, record), Axiom 1, Axiom 2, Axiom 3, Axiom 4, T1, T4, T23, T24b; the graph, the readout and the members'
shares are construction.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T33 · The VIBE test

Two CMOS instances deployed from the same recipe span the same VIBE field, up to identity: an isomorphism of their grant graphs that keeps
declared roles, depths and masses, with the same shortfall per role. In the simulator on one injected clock the two fields are equal; in
the two proof chains the forward chain's expected field and the reverse chain's measured field agree layer by layer; and a pull request
moves the field only where its declaration moved. Each stable publishes a VIBE digest (the canonical field under role names, hashed) next
to its proof-run hash.

Derived from: Axiom 1 (the two proof chains are two witnesses), Axiom 3, Axiom 4, T4, T32; the digest is construction.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T-P · The pressing

A pressed instance (G0) is a one-witness system by construction, and its mark is the record that the second witness is missing.

- **Trust never crosses instances; information does.** Keys, signatures, tokens and standings of one instance are no evidence in another,
  pressed or minted. Information crosses only through an entry path the receiving instance's two witnesses opened, with provenance in the
  record, and can be traced and ejected later.
- Inside a pressed instance, loosening needs a fresh owner-signed challenge and tightening none: a policy that mirrors T24 so that the
  pressing rehearses the real rule.
- A pressed instance may run the stable channel's code, with the pressed mark on every artefact it issues, and it never claims to be
  DMP-compliant (T35); **stable company-wide** (C24) stays minted.
- The minting is a trust rotation: every key rotates, everything trusted before is untrusted until it regains trust under the new roots,
  the owner key is revoked everywhere and the owner role ends; there is no state in which some acts carry one witness and others two under
  the same roots.
- No minting without a passed pressing of the same configuration.
- **Never axiomatically sound; Gravity abhors it.** "Can a pressed CMOS instance ever be axiomatically sound. I say no, so Gravity abhors
  it" (the owner, 2026-10-26). The two proof chains check text against code and run, so each component of a pressed instance is proven
  exactly as a minted one's. The instance is not: every theorem that rests on the independence in Axiom 1 needs two witnesses that trace to
  two people, and in a pressed instance both trace to one, so one influence can bend the whole trust space onto a single point and expose
  the singularity (the one person who holds both witnesses). A configuration in which any influence can do that is out of spec for DMP.
  Gravity's verdict on a pressed instance is therefore "out of spec", never "proven" and never "proven relative to the pressing"; the
  pressing is a rehearsal of the real rule, not a weaker form of it.

Derived from: Axiom 1, Axiom 2, Axiom 3, G0, T24, T24b, T27, T29; the mirror policy and "no minting without a pressing" are construction.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T35 · Compliance is assembled, never claimed

"CMOS is, as a standalone appliance, not necessarily in a DMP compliant state. The right solution comes from proveably working system
components never claiming they are compliant when they are not, combined with a recipe for how to assemble them to form a self balancing
CMOS galaxy" (the owner, 2026-10-26).

- **A component proves what it does, and claims nothing more.** A component's proof covers its own behaviour at its own doors. It never
  states, on any surface, a compliance it has not been shown to have in the configuration it runs in; a claim beyond its proof is a
  refusal-worthy claim, not a description (Axiom 2: a claim is not evidence).
- **Compliance belongs to an assembly.** An instance of CMOS is DMP-compliant only as an assembly: proven components, put together by a
  recipe whose every step is itself checked (both witnesses of every gate trace to two people, every record written at its act, every
  boundary in place). A standalone appliance, an instance assembled off the recipe, and a pressed instance (T-P) are not compliant, and say
  so on every surface where they would otherwise be read as compliant.
- **The galaxy balances itself.** The recipe is written so that the assembled instance stays in spec without a person holding it there: a
  part that drifts out of spec tightens on its own (T24c) and is ejected at its door (T28), and the assembly's compliance is re-shown, never
  carried over (T3).

Derived from: Axiom 1, Axiom 2, Axiom 3, T3, T24c, T28, T-P.

Status: stable since DMP 0.9.0
Owner: CM coordinator
Enforced by: DMP-021

## T36 · The horizon is the one boundary between information and data

"Anything that can transition the event horizon without permission, no matter which surface it is erected against, is an immediate CVSS
10.0, because that is the ONE boundary separating information from data" (the owner, 2026-10-26).

Inside the Event Horizon is information: held under the dual gate, with its record and its provenance. Outside is data. A crossing in
either direction is an act (T28) and needs the dual gate's permission; a crossing without it, on any surface the horizon stands against (a
network boundary, an IM door, a store, a log, a model prompt, a file share, a backup, a side channel that carries content), breaks Axiom 1
at the boundary every other guarantee depends on. Such a finding is critical by definition, scored CVSS 10.0 where a score is reported,
and no class, scope, mitigation or exposure estimate rates it down.

Derived from: Axiom 1, Axiom 3, T27, T28.

Status: stable since DMP 0.9.0
Owner: Black Hole coordinator
Enforced by: DMP-021

## C1 · One door

Milky Way is the only way into CMOS, out of it and between its parts. Constellations do not call each other directly: they
speak IM through Milky Way. In code this means that no constellation references another (project references run
`cm.io` ← `cm.os` ← constellations only).

Derived from: Axiom 1, Axiom 3, T28; one door is construction (it makes ejection total and the record one place; a second door would need
the dual gate between the doors).

Status: stable since DMP 0.1.0
Owner: Milky Way coordinator
Enforced by: DMP-008

## C2 · Every message verifies itself

Every IM message authenticates itself and the sender's right to send it, with a token signed by Doppler's authority, and the
verifier checks every message. No public port opens before the verifier has passed its test level on the sandbox.

Law:
- C2.L1 Verified means authorised. A message is verified only if a token signed by a trusted token signer names an enrolled,
  unrevoked subject whose key signed the message, the token is inside its validity window, and it carries the permission the
  kind needs.
- C2.L2 One answer, fail closed. Every input gets exactly one answer. The answer is the first row of chapter 04's table whose
  test fails, in the table's order, with that row's reason, and such a refusal leaves the replay memory as it was; when no row
  of rules 1 to 15 fails, only rule 16 (replay) may still refuse.
- C2.L3 No replay. Through one verifier, the same signed message is verified at most once: after it was verified, the same
  message is refused at any later or earlier time with the memory the first answer left.
- C2.L3b A full replay memory refuses. When the verifier already remembers as many unexpired messages as
  milkyway.im.replayCapacity allows, nothing verifies: it never forgets early to make room.
- C2.L4 The permission table. A heartbeat needs im.heartbeat.send, and no other kind exists in IM version 0.

Laws file: CM.BlackHole/gravity/C02/LAWS.bend sha256 a116add4e257c503f7b566ef0fb74a4b79a26786ffadf6c745ab5f690e4ad0c2

Derived from: Axiom 2 per message (L1 with T1, L3: evidence is live for one point of use), Axiom 1 (L2: one answer, fail closed; L3b: when
liveness cannot be verified, nothing verifies), T1 (L4).

Status: stable since DMP 0.1.0
Owner: Milky Way coordinator
Enforced by: DMP-018

## C3 · Every value carries its class

Every value that crosses a border carries its class and its identification axis, and carries a token at every border that is
not an administrator. Reveal is a grant, never a default.

Derived from: T0, T1, T24 (a reveal is a loosening).

Status: stable since DMP 0.1.0
Owner: Milky Way coordinator
Enforced by: none yet

## C4 · Contracts change at stable promotions only

The contracts of the suite are registered, and a contract changes only at a stable promotion, never mid-flight and never with
a fourth spelling of a name.

Derived from: T0 (a changed contract is a new part), Axiom 1 (its trust has two witnesses: the promotion's two chains), T1 (a name is part
of what was granted); the promotion as the point is construction.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## C5 · Financial data freezes at production

While a thing is in development, its data may be manipulated as needed. Once a component handling financial data is
productive it freezes: it is auditable and changes only through a reviewed release.

Derived from: T0, Axiom 1 (the reviewed release is the second witness), Axiom 3; the production point is construction.

Status: stable since DMP 0.1.0
Owner: Sagittarius coordinator
Enforced by: none yet

## C6 · The gates and the authority grant

The gates hold. What agents may do on their own authority and what needs the words of a layer's two halves is written in the grant below,
word for word. The wording is protected: rule DMP-009 refuses a change to it.

<!-- authority-grant:begin -->
> Agents may build, change and deploy CMOS's own estate on their own authority, using credentials held in the operations master, under plan, audit and four eyes where a second administrator exists. An act on a system or on information outside CMOS's own estate needs both halves of the layer it belongs to (T38), its organisational and its factual responsibility. Each half may declare advanced testing reached for its layer; until both have, every act there needs their words per act, and anything irreversible outside CMOS's own estate always does. Until the minting the owner's own words stand for both halves (G0).
<!-- authority-grant:end -->

Derived from: T1 (the rule is trust bounded, written for agents), T38 (every layer outside CMOS's own estate has its two halves), Axiom 3
(the wording is tamper-evident), G0 (until the minting the owner's words are both halves); which layers exist and who holds their halves
is configuration (T4).

Status: stable since DMP 0.9.5
Amended: grant abstracted onto T38, approved by the owner 2026-10-26 15:36Z (stable since DMP 0.1.0 before that).
Owner: CM coordinator
Enforced by: DMP-009

## C7 · One repository deploys every part

Every part of the infrastructure deploys from this repository, in different shapes (hub, local, cluster). The folder layout is
the composition tree ([02](02-names-and-hierarchy.md)) and a part is a folder in it.

Derived from: Axiom 3 (the tree is the record of what exists); one repository is construction.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-004

## C9 · Open source: roles, not values

No secret, no company fact and no person beyond a role ever enters the tree: colleagues by role, never by name; no host, share,
address or internal system name; real values live in the settings store of the administration tool or are passed as parameters.
The public tree holds no trust for company facts. (The licence, Apache 2.0, is a decision, not a
theorem: chapter 14 "The licence".)

Derived from: T0, T1, T27 (a value in the public tree has left the perimeter).

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-001, DMP-006

## C10 · The defensive charter

This repository and every brief in it serve defensive security and privacy engineering of the company's own systems. The
charter is [CHARTER.md](CHARTER.md), and every security-shaped brief carries its one-line security context.

Derived from: T1 (CMOS is trusted only for acts on the company's own systems); the charter text is the wording of that bound.

Status: stable since DMP 0.1.0
Owner: Black Hole coordinator
Enforced by: DMP-002

## C12 · Information is tiered

CMOS contains all the information inside its Event Horizon, and every piece of it is reached through one routing: tiers, routed by the
Milky Way net (the MW net). What CMOS makes is its own; personal data it holds only on a grant (T27).

- **Tier 1** is the information in the MW net itself. It is always consulted first.
- **Tier 2** is information the MW net routes the asker to: a source in the VIBE net, outward in the order the VIBE field gives.
- **Tier 3** is information outside the Event Horizon, reached only through an exit node (chapter 05), and only when no tier inside
  answers.

**Information layers.** Information of one kind forms a **layer**: its own sub-VIBE net with its own configuration, overlaid on the one
VIBE field and routed by a clustered MW net. The knowledge warehouse is the knowledge layer; the relational warehouse and the files
warehouse are layers in the same way. Each layer reorganises by Lensing on its own: the field's pull computes the layout from the signed
state, and both witnesses recompute it and sign. **A layer places information, never grants trust**: who may act on a piece of information
comes only from its admission and the dual gate, never from the layer, tier or position it sits in. The set of layers is open: a new kind
of information is a new layer that declares its kind, its sub-VIBE configuration and its route, and inherits the dual gate, the record and
Lensing; no other part of CMOS changes for it. A layer may place work as well as information (a network layout, load balancing, placement
in a cloud): it decides where, never who may.

**Privacy** is a singularity of VIBE space: information there is admitted only for the class its grant names, and no routing pulls it out,
not even the MW net, which holds no admission for it. Routing behaves exactly as if the singularity were empty, so neither an answer nor
its absence reveals what a singularity holds; its record shows counts, never content. A part trusted for that class acts on it at the
singularity itself, under the dual gate. **Credentials** are information like any other and route the same way; each sits in a singularity
of its own kind, so routing reaches the point of use, never the secret. Each singularity is a cluster of at least three nodes.
Singularities of different layers may be the same cluster, as long as that is valid under DMP, and every singularity obeys DMP on its own.

What an agent learns that no tier held goes back into Tier 1 through the MW net's intake.

Derived from: the words (admission, contains, door), Axiom 1, Axiom 2 (a position in the network is never evidence, so a layer cannot
grant), Axiom 3 (what an agent learned is a record both witnesses need; counts always, content by grant), T3 (no single part's loss stops a
singularity: at least three nodes), T21, T27, T36 (every crossing of the horizon is an act, so it comes last and only when needed), C1
(every route runs through the one door, so asking it first costs no extra crossing). The owner's words (2026-10-26): "Tier 1 information is
the information in the MW net, which is always consulted first and then outward as the VIBE net dictates"; "Privacy could be seen as
singularities of VIBE space where not even the MW-net can pull the information out"; the knowledge warehouse "would simply be the knowledge
layer, that is overlayed dynamically on the VIBE field, same with the other stuff", which "also makes the system extensible for any other
kind of information layer", for example "network layout while maintaining ultimate trust in the cloud, or other load balancing"; "CMOS
contains all the information inside its Event Horizon"; "Each singularity should always be a cluster of at least 3 nodes. All singularities
of the differnent layers may of course be the same, as long as it is DMP valid, but each singularity must still obey DMP." Evidence chain:
`Dmp/R1Tier.lean` (Milky Way first, exit last, never singular, singularity invisible), `Dmp/R1Mass.lean` and `Dmp/R1Lens.lean` (a layer's
reorganisation) in the gravity repository.

Status: stable since DMP 0.9.3
Redrafted: 2026-10-26, approved by the owner 2026-10-26 15:04Z (stable since DMP 0.1.0 before that).
Owner: CM coordinator
Enforced by: DMP-002

## C13 · The nearest tier is asked first

Whenever an agent is unsure, it asks Tier 1 first and then outward, tier by tier, before it asks the owner; Tier 3 only through an exit
node and only when no tier inside answered. It cites the record it used, or records that the search came up empty, and only then asks the
owner (chapter 14). C12 is the other direction of the same loop: what the agent learns goes back into Tier 1.

Derived from: Axiom 2, Axiom 3 (the record is read first), C12, T36; "first" is construction.

Status: stable since DMP 0.9.3
Redrafted: 2026-10-26, approved by the owner 2026-10-26 15:04Z (stable since DMP 0.2.0 before that).
Owner: CM coordinator
Enforced by: DMP-002

## C14 · Every path is combined

When no single source gives the full picture, the agent does not stop at the first answer. It asks itself how combining every
read path and every write path it has can answer the question: the information tiers, the live reads of the systems, the repositories and the
state of the tree, and the trail every write leaves (the audit log, job steps, plans, dry runs, git history). It names the paths it
combined and what is still unknown. A write path is read through its trail or its dry run, never by writing to find something out.
Only what no combination answers goes to the owner. C13 and C12 frame it: the nearest tier first, then every path combined, then the owner,
and what the combination found goes back into Tier 1.

Derived from: Axiom 2 (every evidence the agent can verify itself), Axiom 1 (a write is an act, not a read), C12, C13.

Status: stable since DMP 0.3.0
Owner: CM coordinator
Enforced by: DMP-002

## C15 · A write first reads what it changes

In the owner's words (2026-10-25): "a write first reads the live state it will change and asserts what it expects: absent for a
create, a named value for an overwrite." The read is live from the target system, never a value read earlier; a retry reads again
before each attempt; every write is read back afterwards and must show the wanted state. When the expected state is not found,
nothing is written: the step fails hard when later steps depend on it, soft (skipped, reported, never a clean run) when none do.
The tenant, host and account a write goes to are expected state too. This holds for every write CMOS makes; for scripts it is
checked by DMP-012, the pattern is chapter 10 "Guarded writes".

Derived from: Axiom 2 (live state at the point of use), Axiom 3 (the read-back is the record), T1 (tenant, host and account are part of
what was granted).

Status: stable since DMP 0.3.0
Owner: CM coordinator
Enforced by: DMP-012

## C16 · Company data reaches general testing only with its proof

A feature that directly touches company data stays with IT and trusted advisors until two documents exist, both built from the current
artifact template: an explanation of exactly how the data is handled and which protections keep it safe, and the GRB report that proves
it. Only then does it move to general testing. In the owner's words (2026-10-25), this is good practice, compliant, the right thing to
do, and it raises user trust and with it the chance and the speed of CMOS's adoption. How the pair is made and where it lives:
chapter 14 "The data gate".

Derived from: Axiom 1 (two witnesses: the builder who explains and the session that attacks, sharing nothing), Axiom 3 (both pages from the
template so both halves can read them), T24 (general testing is a loosening).

Status: stable since DMP 0.3.0
Owner: Black Hole coordinator
Enforced by: DMP-013

## C17 · The first level belongs to The Administrator and LHC

CMOS has no single owner (2026-10-25). It is controlled through the unity of two parts:

- **The Administrator**, always written with a capital The, is a mythical entity, not a person. It is everyone who is allowed to fully
  administer CMOS, allowed both by whoever holds authority over CMOS and by CMOS itself.
- **LHC**, the Lighthole Council, is the minting ritual of CMOS. The fellowship of people it binds together, its members, is the other
  part.

CMOS is a balance and a check of power at every level: every system keeps every other system in check while keeping it optimised and
running in the best shape possible, through the interplay of The Administrator and LHC.

The children of CM, level 1 of the composition tree (`cm.io`, `cm.os` and the constellations), carry oversight and insight into the
whole system. A new node at that level is created only with the words of both The Administrator and LHC, recorded in
`DMP/tree-approvals.txt`. Until the first LHC is minted, The Administrator's words are recorded alone and the line says that LHC's
confirmation is pending. Below level 1, the foundation is laid out: a brick of `cm.io` or a part of `cm.os` is complete within its
scope, built once and tested once (chapter 02, "Foundation bricks are complete").

Derived from: Axiom 1 at the governance layer, G0 (`LHC pending`), T24b, T24c (every system keeps every other in check); the two
definitions stay definitions.

Status: stable since DMP 0.9.4
Amended: "organisation" replaced, approved by the owner 2026-10-26 15:19Z (stable since DMP 0.3.0 before that).
Owner: CM coordinator
Enforced by: DMP-014

## C18 · IM stays backwards compatible once it is locked

IM is version-locked on the day the first system that speaks it enters production (the owner's words name that day and that system).
From then on IM is permanently backwards compatible: every later version accepts every message of a locked version with the same
meaning. A later version only adds (new message kinds, new permissions, new parts a locked receiver never has to understand); nothing a
locked version defines is removed, renamed or given a new meaning. Each locked version keeps a corpus of its messages in this
repository that never changes, and every build replays it through the current verifier. Until the lock, IM may still change freely
(version 0 is sandbox only).

A locked version is never broken, not even when one of its algorithms becomes unsafe. CMOS controls every system that speaks IM, so
Milky Way contains the weakness instead (the owner, 2026-10-25): it keeps speaking the locked version, broken algorithm included, to
every system locked to it, and carries that system's traffic only through a sandboxed path of its own. On that path a message is not
trusted for what the weak algorithm claims. Where a second, independent path to the locked system's identity exists (the device's
attestation, a transport credential), Milky Way vouches for it toward the rest of CMOS under a current version, with no more rights than the
locked system had; where none exists, the locked system's traffic is contained (T27) until such a path is built.

Derived from: T2 (an unsafe algorithm is evidence against the algorithm, not against the system), Axiom 2 (a sandboxed path is a position in
the network, never evidence by itself), T1, T27, Axiom 3 (the corpus).

Status: stable since DMP 0.3.0
Owner: Milky Way coordinator
Enforced by: DMP-015

## C19 · A request is served only when it is secure and meant

An information warehouse (Sagittarius, Crater, the knowledge warehouse) serves an IM request only when the request carries two signatures and the
warehouse checks both itself (the owner, 2026-10-25). One is under the security chain, which Doppler holds: the request is secure. The
other is under the operational chain, which the operations master holds: the request is meant to be served. Each comes from a cluster of that master or
from an authority that master granted the right to sign on its behalf. A grant counts only when the other master has signed the same
authority too: Doppler cannot give anyone the right to sign IM requests unless the operations master has signed that authority, and the operations master cannot unless
Doppler has. That a carrier (Milky Way, the operations master) already checked the request does not count; the warehouse checks it again. A request
that lacks either signature, or whose authority only one master signed, is refused.

Derived from: Axiom 1 (two masters), Axiom 2 (a carrier's check is not the warehouse's evidence), T24b (a grant to sign is a lockstep act).

Status: stable since DMP 0.3.0
Owner: Black Hole coordinator
Enforced by: DMP-016

## C21 · Look it up, don't guess

Whenever something is unclear, or two candidates are close (a person, a username, a host, a group, a system, a value: anything), the
agent looks it up in the source of truth itself before it acts or asks (2026-10-25): the directory through the operations master for people and
accounts, the inventory for machines, the system itself for its own values. It quotes the match it found and where it came from. If
more than one record still fits, it names every one and asks which is meant; it never silently picks one. C14 covers the question
the agent knows it cannot answer; C21 covers the answer it only thinks it knows.

Derived from: Axiom 2, Axiom 3; premise: a statement an agent makes to a person or to the record is a write, its sources are its evidence,
and an unsourced statement is a write without evidence.

Status: stable since DMP 0.6.0
Owner: CM coordinator
Enforced by: DMP-002

## C22 · Prove forward and back: two chains, neither trusted alone

"The entire point of CMOS is to never trust any one interconnected system" (the owner, 2026-10-25). Every proof therefore runs both
ways, as two totally independent end-to-end chains that always verify each other's state:

- the **intent chain** proves forward, DMP to IM to CM: what is meant becomes the protocol, the protocol becomes the code;
- the **evidence chain** proves in reverse, CM up to IM to DMP: it starts from what is actually there (the code, its configuration, the
  measured running state), derives which IM that code really speaks, and proves which DMP rules that IM really keeps.

The chains share nothing they could fail together beyond the roots they declare: not the author, not the proof engine, not the cases, not
the input they start from (the intent chain starts from DMP's sentences, the evidence chain from the code). Both end at the DMP text on
purpose: it is the central mass that ties them. Neither takes a result from the other. They cross-check each other at every layer they both
reach (IM, DMP) and at run time. A layer counts as proven only when both chains agree on it. A mismatch is never settled by trusting one
chain: the gate turns red, the part where they disagree counts as unproven, the stricter answer stands at run time, an alarm reaches the
owner, and nothing that depends on it is promoted until both chains agree again. Gravity carries the intent chain and Lean the evidence
chain (the gravitational wave test, chapter 06); the lemma ledger (chapter 06) counts an IM command as proven safe only when both chains
prove it. The two chains prove that text, code and run agree; the text itself is verified only by a reader who did not write it.

"This approach applies universally throughout CM, it has to be implemented wherever it is technically possible" (the owner,
2026-10-25).

Derived from: Axiom 1 (two independent witnesses for "this layer is proven"), Axiom 2 (each reads its own source), T29, T30.

Status: stable since DMP 0.4.0
Owner: CM coordinator
Enforced by: DMP-002

## C24 · Stable company-wide needs a survived cold start

CMOS goes stable for the whole company only when the company can be restarted from nothing and this has been shown, not claimed. Every scenario of the signed cold-start catalogue has
passed its pass line in the sandbox, the most critical ones have passed through a Milky Way simulator node, and at least one cold start has passed on the real infrastructure. The pass line is the
same for every scenario: recovery within one week, no data loss beyond the last backup, and both trust chains intact afterwards. The people who restart the company have what they need to do it: an
assistant through which every planned step can be done, and a White Key bundle with the repositories, a cross-checked guide and an offline coding agent. How the runs are counted, the tiers and the
definitions are in [11](11-test-design.md).

Derived from: Axiom 2 (shown, not claimed), Axiom 1 (both chains intact), Axiom 3 (the run record), T-P (company-wide stays minted); the pass
line's numbers are configuration.

Status: stable since DMP 0.7.0
Owner: Black Hole coordinator
Enforced by: none yet

## C25 · Every deployment obeys DMP

"When deploying any allowed configuration of CM to CMOS, DMP must always be obeyed" (the owner, 2026-10-25). The owner's first words
for it were a trial rule, "DMP is to be enforced on all deployments", after a corner CM built itself: the stable channel served a client
that could no longer change a setting the owner needed, and no other supported route was left. "We MUST learn from this, so when
configuring an impossible situation, we must create rules that enforce it never happens again" (the owner, 2026-10-25).

**Every deployment passes the gate.** A deployment is any promotion of a part that CM runs or orchestrates to any channel (stable,
nightly, dev). It runs the DMP gate on the exact commit it deploys and does not deploy on red. A deployment that skipped the gate is
not fully trusted and is ejected (C∞).

**No deployment locks us out.** No deployment may leave an administrator action without a supported route on the channel it serves:
the command line or the console. Every action that changes a system and is named in the audit log is tied to a real command, and the
list of known gaps may only shrink. Before a retire, a replace or a promotion, the plan says which client and which routes the
channel will serve; a deployment that would remove the last route for any action, or would serve a client that lacks a route the
channel has today, is refused before it runs.

Derived from: Axiom 1 (a deployment is an act), Axiom 2 (checked on the exact commit, not an earlier run), T3, Axiom 4 (the gaps only
shrink).

Status: stable since DMP 0.8.0
Owner: CM coordinator
Enforced by: DMP-002, DMP-020
Not yet checked in cm: the tie between the actions named in the audit log and `DMP/routes.txt`, and the plan's refusal of a
deployment that would remove a route. The administration tool's own test holds them until its code moves into cm;
DMP-020 checks the gate on every deployment workflow, the route register, its cap and its ratchet.

## C∞ · The stopgap: what is not fully trusted is ejected

"Everything that is not fully trusted is automatically ejected" (the owner, 2026-10-25). This is the thesis of DMP and its permanent
stopgap. Its reserved number is C23; it is written C∞ because it always stands last: every new section of this chapter is inserted
above it, so whatever the sections above do not catch, it does. **Fully trusted** means what the dual gate says; at the proof layer its two
witnesses are the two chains (C22), so a part is fully trusted for merging, promoting and deploying only when it is compliant with DMP, with
IM and with CM and proven by both chains. "Everything that does not comply with DMP and IM and CM is automatically critical" (the owner,
2026-10-25): such a finding carries the severity **critical** by definition, and nobody rates it down; no label, review state, deadline
or "minor" wording turns it into a nit or a follow-up. What is not fully trusted is ejected automatically: the pull request does not
merge, the stage is not promoted, nothing that depends on it moves on, and a running part that loses that trust is cut off at the door
(C1) until it is trusted again or the rule it breaks is changed the way that rule's own section allows. Every gate applies this the
same way: the review gate in front, which is fast, `tools/verify` and its rules, and the two proof chains behind (C22), which are
rigorous. Where gates disagree, the stricter answer stands.

**The goal is that C∞ never fires.** The axioms above and the theorems derived from them are DMP's axiom system. Every finding names the
most specific axiom or theorem it breaks; a finding that only C∞ catches is a **stopgap catch** and is counted. "Whenever we hit C∞, we
know we need to rethink our formal system to be more internally sound while, perhaps, also relying on fewer axioms" (the owner,
2026-10-25). So a stopgap catch starts a rethink, not an append. Its answer, in this order of preference: show the missing guarantee as a
theorem of the axioms already there; merge axioms into fewer, stronger ones that cover it; and only as the last resort add a new axiom.
Two counts move with every rethink: stopgap catches toward zero (completeness) and the number of axioms down (parsimony). The end state is
the widest coverage from the fewest axioms. This holds stack-wide, at every layer that has a provable basis (DMP, IM's laws, the two proof
chains), not only in this chapter. A complete axiom system would never need the stopgap. It stays anyway: no formal system can prove that
its axioms match the world it describes, and no general check of a program's behaviour exists, so the honest backstop remains even when
the count stays at zero.

Derived from: Axiom 1 (the thesis sentence is the dual gate's); the rest is the meta-rule about the axiom system itself.

Status: stable since DMP 0.5.0
Owner: CM coordinator
Enforced by: DMP-002, DMP-017
