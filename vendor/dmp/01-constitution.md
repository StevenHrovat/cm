# 01 · Constitution

The rules that are true from the first day and do not bend. Every section is stable. A pull request that breaks one is
refused; the owner of the framework alone decides to change this chapter (until the first minting the owner's words stand for both
halves of the Duality, G0).

**How this chapter is built (DMP 0.9.0).** The owner asked (2026-10-25 19:38Z) for DMP to be reduced to its axioms about trust: "every
axiom just has to state how trust relates to the system ... if we can define trust, we can define all other theorems from it." So the
chapter is an axiom system. It opens with **the words** (names only; a definition forbids nothing), then the **three building blocks** as
three axioms (the Duality, Trust, Information; until DMP 0.9.7 they were three bounds and one direction, see "The three building blocks"
below), then **the ground** G0 (the minting, stated, not derived). Everything after
that is a **theorem**: the T sections are the theorems found while distilling the axioms, the C sections are the **constraints**. Every
theorem carries a `Derived from:` line that names only axioms, G0, the words and sections above it, so the order of the chapter is the order
of proof (rule DMP-021). C∞ stands last, as it always has (DMP-017).

**What a constraint is (DMP 0.9.7).** The owner (2026-10-26 22:30Z): "Let's try to turn them into the derived constraints that we try to achieve
with the axioms and theorems. That lets us easily spot if we undershoot our goal." A C section states, in one or two general sentences and
with no named system, what the axiom system must achieve. Its `Derived from:` line names the axioms and theorems that achieve it, and what
part is construction or configuration. Its `Coverage:` line says how far the proof chains reach it: **proven** (the Lean evidence chain in
Gravity reaches the whole constraint; the theorem names follow), **argued** (the text derives it, no Lean yet) or **undershoot** (a part of
the constraint that no theorem reaches, named, never hidden). Most constraints are reached in part; the line then says which part each state
applies to and opens with the weakest state. The check runs both ways: a constraint without a theorem is an undershoot of the
axiom system; a theorem no constraint needs is a theorem looking for its purpose. A constraint keeps the number of the rule it came from and
the constraints stand in clusters, not in number order. What a rule said about *how* (an agent's steps, a protocol's table) is not a
constraint and lives in the chapter that explains it; the table "Where the old rules went" below maps every sentence. The shape is
prior work: Goal Structuring Notation (Kelly, *Arguing Safety*, 1998; the GSN Community Standard), where a goal with no solution is drawn as
undeveloped, and the two-way rationale of Common Criteria (ISO/IEC 15408-1), where every objective meets a requirement and every requirement
serves an objective.

**The three building blocks (DMP 0.9.7).** The owner (2026-10-26 22:56Z): "One authority owns Trust, the other owns Information. To exist
inside of DMP, they are required to keep each other constantly in check and balance so that no element can overrule the other. Actually, can
we reduce the 4 Axioms to the 3 basic building blocks? Trust, Information and the DoT?"; (22:59Z) "No Information is ever trusted and no
information, besides its existence, can be derived from trust."; (23:03Z) "Also extends to the field and how it behaves: Trust is
gravitational, Information is hierarchical."; (23:10Z) "All natural systems, which exchange information, lay themselves out in hierarchical
order, which can be optimized for its highest total cohesion, so how much every part of the system has to work to get information, so how
many nodes it needs to hop. [...] So Gravity, if it is strong enough, is sufficient, as proven by GR, to contain all Information but Entropy,
while Information naturally organizes itself both to facilitate optimal access across its information layers, but also, as a consequence of
actively maintaining the median state, naturally keeps the DoT stable." Approved 23:02Z. So the chapter has three axioms, one per building block, and the four axioms
of DMP 0.9.0 went into them without losing a sentence:

| Was (DMP 0.9.0) | Is (DMP 0.9.7) | What the block adds |
|---|---|---|
| Axiom 1 · The dual gate, and Axiom 4 · The direction | Axiom 1 · The Duality | the halves are typed, one owns trust and one owns information; the direction is their balance |
| Axiom 2 · Evidence | Axiom 2 · Trust | no information is ever trusted; trust is gravitational (the field of T32 is how trust behaves, not a construction on top); gravity contains |
| Axiom 3 · The record | Axiom 3 · Information | from trust nothing but existence can be derived; admission and containment (the words); information is hierarchical and evolves towards the fewest hops |

The numbers of the three that stayed are kept, so every `Derived from:` line in this chapter and every comment in Gravity keeps its meaning;
a citation of Axiom 4 in an older text reads Axiom 1. The numbers do not rank the blocks: read in the owner's order (Trust, Information, the
Duality) the system is the same, since each axiom names only the words and the other two.

**Where the old rules went.** One row per rule recast; the rows grow with each approved cluster.

| Rule | Was | The constraint now | Moved out |
|---|---|---|---|
| C1 | One door | C1 · No act bypasses the gate | the code rule (no constellation references another): chapter 02 "The tree", DMP-008 |
| C2 | Every message verifies itself | C2 · Every message is its own evidence; the Law block is unchanged | "no public port opens before the verifier passed its class": chapter 06 "Where the class gates" |
| C3 | Every value carries its class | C3 · A value reaches only parts trusted for its class | the token on every value and the identification axis: chapter 05 (the shield) |
| C18 | IM stays backwards compatible once it is locked | C18 · A weakness is evidence against what it claims, never against the part | the lock, additions only, the corpus, the contained path: chapter 04 "Compatibility after the lock", DMP-015 |
| C19 | A request is served only when it is secure and meant | C19 · Two halves on every served request: trust and information, each verified by the server itself | the two masters and how the signatures travel: chapter 04 "Serving at a warehouse", DMP-016 |
| C12 | Information is tiered | C12 · Position never grants, and routing reaches everything inside | the tiers, the layers in use, Lensing, credentials, the three-node floor, the owner's words: chapter 05 "The information tiers and the layers" |
| C13 | The nearest tier is asked first | C13 · The record inside is read before anyone outside is asked | the agent's steps: chapter 14 "Ask the nearest tier first", "Knowledge goes back into Tier 1" |
| C14 | Every path is combined | C14 · An answer rests on every piece of evidence its author can verify | the list of paths and the `Paths combined:` line: chapter 14 "Combine every path" |
| C15 | A write first reads what it changes | C15 · A write lands only on a state its writer verified live | retries, hard and soft failure: chapter 10 "Guarded writes" |
| C21 | Look it up, don't guess | C21 · A statement is a write | the sources per kind of name and the `Looked up:` line: chapter 14 "Look it up, don't guess" |
| C6 | The gates and the authority grant | C6 · Who may act on what is a bounded grant in the record; the grant block unchanged | nothing; "the gates hold" is C∞ |
| C17 | The first level belongs to The Administrator and LHC | C17 · The top layer is a Duality like every other; the two names stay as definitions | the level-1 approval record and the foundation bricks were already chapter 02 |
| C22 | Prove forward and back: two chains, neither trusted alone | C22 · "Proven" is an act with two witnesses | which engine carries which chain and the lemma ledger were already chapter 06 |
| C∞ | The stopgap | C∞ · The root constraint, unchanged in substance, with its Coverage line | nothing |
| C4 | Contracts change at stable promotions only | C4 · A change to what a part speaks is a new part | the register and the promotion point were already chapter 02 and 05 |
| C5 | Financial data freezes at production | merged into C4 (productive information changes only through a recorded act of both witnesses); the number C5 is retired | nothing |
| C7 | One repository deploys every part | C7 · What runs is what the record says | the shapes and the folder layout were already chapter 02, DMP-004 |
| C16 | Company data reaches general testing only with its proof | C16 · Widening who may use a feature on information is a loosening | the two stages, the register, the template and the owner's words: chapter 14 "The data gate" |
| C24 | Stable company-wide needs a survived cold start | C24 · Restorable is shown, never claimed | the catalogue, the pass line, the assistant and the bundle were already chapter 11 |
| C25 | Every deployment obeys DMP | C25 · A deployment is gated on the exact thing deployed, and removes no route | nothing; the history stays in the section |
| C9 | Open source: roles, not values | C9 · What is published holds nothing that is inside | nothing; the licence was already chapter 14 |
| C10 | The defensive charter | C10 · CMOS is trusted only for acts on its own estate | nothing; the wording stays in CHARTER.md |

**What a derivation is here.** Each `Derived from:` line names what the theorem rests on and, where it applies, one status word:
*construction* (a design choice the axioms allow and recommend but do not force), *configuration* (a number or a list set at the minting,
theorem T4), or *premise* (an assumption no chain reaches, kept in the assumption register of chapter 06). No word means the theorem
follows from what it names. The prose arguments behind the axioms (a distillation and a cold review) are working notes kept outside this specification. An argument becomes a proof when both proof chains of chapter 06 carry it:
Gravity down from these sentences, Lean up from the code (the gravitational wave test). Until then a theorem is argued, not proven.

**Counts.** 2 bounds, 1 direction that is also the gate: three axioms, one per building block (the Duality, Trust, Information); 1 ground, 1 root constraint (C∞). C8 and C11 left the constitution (they are conventions, not about trust:
chapter 10 "Language and runtime", chapter 14 "Completion goals, never time plans"); C20 and C26 were never stable; C23 is C∞.

## The words

Undefined, like point and line: a **part** (a Duality of Trust: two halves, one owning trust and one owning information; nothing further is
said about what a part is made of), an **act** (anything done in or to CMOS: a message, a read, a write, a grant, a merge, a promotion, a
deployment, a decision, an answer an agent gives, a seat taken), a **witness** (a part that decides trust for others), **evidence** (what a
witness verifies) and **time**.

- **Trust** is a relation between a witness, a part, an act and a time, written `T(w, p, a, t)`: the witness trusts the part for that act
  at that time. Nothing else; what grounds trust is the evidence axiom.
- A **standing** is trust held across time (enrolled, seated, locked, armed, proven); **act trust** is trust for one act now.
- Two witnesses are **independent** when they share no member of the failure classes declared for their layer in the minting record (host,
  key, author, engine, operating image, person, time source, and whatever else the minting lists). Independence is always relative to that
  list; the list is configuration (T4).
- A **layer** is a place where trust is decided; its two witnesses are its **halves**, one the **trust half** (it answers whether an act may
  happen: who, under what standing) and one the **information half** (it answers whether the act is meant and right: what, and whether it
  is true); the pair at a layer is the **Duality of Trust** (DoT).
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
2026-10-26 15:04Z; the halves typed as trust half and information half approved by the owner 2026-10-26 23:02Z; a part defined as a Duality
of Trust and nothing further, the owner 2026-10-26 23:28Z (stable since DMP 0.9.0 before that).
Owner: CM coordinator
Enforced by: DMP-021

## Axiom 1 · The Duality

A part acts in CMOS only while the two halves of its layer, two independent witnesses, each with full reach over the act and gated only by
the other, trust it for that act. One half owns trust and the other owns information; neither overrules the other, and the two keep each
other constantly in check and balance. What they do not both trust does not act: the message is refused, the pull request does not merge,
the stage is not promoted, the running part is cut off. Every input gets exactly one answer, and the gate has no other way through: no debug
switch, no allow list, no trusted network. Where the two disagree, the stricter answer stands, and nothing that depends on the disagreement
moves until they agree. This holds at every layer.

The balance is the direction of the system. Every part inside the declared boundary works towards the trust the Duality declared for that
boundary: what was lost without evidence against it, it restores by evidence; what the boundary claims and lacks, it builds by evidence;
what it cannot restore, it reports. No part lowers the bar of evidence, widens a grant or moves the boundary to get there. Axiom 2 and
Axiom 3 only forbid, and together with the gate they govern every step; where they stop a step, the balance waits. A system that does
nothing keeps the bounds and fails the balance: the balance is the one commitment that asks for something to happen, and it ranks below the
bounds (T3d).

The owner's words it carries: "Everything that is not fully trusted is automatically ejected" (C∞); "The duality of trust is the single
concept that goes through every layer and every concept of DMP" (2026-10-25 19:36Z); "The entire point of CMOS is to never trust any one
interconnected system" (14:25Z); "One authority owns Trust, the other owns Information. To exist inside of DMP, they are required to keep
each other constantly in check and balance so that no element can overrule the other" (2026-10-26 22:56Z); "All systems that trust each
other actively work towards increasing the total amount of trust in the network claimed by its declared boundary" (2026-10-25). It is the
gate (what trust does), the Duality (who makes it) and the balance (where it moves) as one commitment: the second sentence of the Duality
already contains the first. Until DMP 0.9.7 the gate was Axiom 1 and the balance Axiom 4; an older citation of Axiom 4 reads Axiom 1.

Status: stable since DMP 0.9.7
Amended: Axiom 1 · The dual gate and Axiom 4 · The direction of DMP 0.9.0 merged into one, the halves typed as trust and information,
approved by the owner 2026-10-26 23:02Z.
Owner: CM coordinator
Enforced by: DMP-008, DMP-017, DMP-018, DMP-020

## Axiom 2 · Trust

A witness trusts only on evidence it verified itself at the point of use: from the live state of the part wherever the act's trust depends
on the part, and from the current state of the record, including every loss recorded against the standing. Never a carrier's check, an
earlier read, a claim, a memory or a position in the network. No information is ever trusted: what a part says, carries, stores or forwards
is never evidence of itself, however it is signed or where it came from; evidence is what the witness verifies, and a message, a document
or a record is at most the thing verified, never the verifier. The absence of evidence moves nothing: without evidence for an act, the act
does not happen; without evidence against a standing, the standing does not change. A standing is evidence with a validity, and no
validity is longer than the time the minting accepts between a loss and its effect at the point of use.

Trust is gravitational. It is a field, never a position: every standing has a source (the witness that granted it), a depth (its grant
chain back to the pair) and a freshness that decays with the time since its last live verification. The field bends trustspace, so that a
boundary contains what no trust reaches (the words), and restoration moves along it, from the pair outward (T32). The field adds levels of
consequence and of proof demanded, never levels of trust: the weight never votes. Gravity contains: a boundary strong enough bends
trustspace so that no act carries information across it, and what still leaves is entropy and nothing else (T31, the floor); where trust
is strong enough, containment needs no wall.

The owner's words it carries: "a write first reads the live state it will change" (C15); "look it up, don't guess" (C21); "That a carrier
already checked the request does not count; the warehouse checks it again" (C19); "Isolating on silence would hand anyone who can cut a cable
a switch that shuts the company down" (Q6); "No Information is ever trusted" (2026-10-26 22:59Z); "Trust is gravitational" (23:03Z); "Gravity, if it is strong enough, is sufficient,
as proven by GR, to contain all Information but Entropy" (23:10Z).

Status: stable since DMP 0.9.7
Amended: Axiom 2 · Evidence of DMP 0.9.0 with "no information is ever trusted", the field and containment, approved by the owner
2026-10-26 23:02Z, 23:03Z and 23:10Z.
Owner: CM coordinator
Enforced by: DMP-012, DMP-018

## Axiom 3 · Information

Every grant, every use and every loss of trust leaves a record, written at the act, that every witness of that act can read in full and
that outlives the act. Any other reader sees the counts always and the content only by a grant. From trust nothing can be derived about
information but its existence: a standing, a refusal or a count says that information is there, never what it says. Content moves only by
a grant to a part trusted for its class (an admission, the words), and where no trust reaches, information is contained (the words, T27,
T37). A record is kept as long as the law requires and no longer than it allows; until then it is never destroyed by fewer than both halves
of its layer.

Information is hierarchical. Every piece of information sits in one layer; every layer has its own Duality (T38) and is a tree rooted
there, which routing walks from the nearest place outward and crosses the horizon last (C12); a sub-layer only tightens what the layer
above it allows, and nothing in a layer is reached except through its tree. The tree carries no trust: where a piece of information sits
never decides who may act on it.

Information evolves. A system whose parts exchange information lays itself out in that hierarchy and reorganises towards its highest total
cohesion: the fewest hops in total that its parts need to reach the information they ask for, taken over the composition of requests that
is representative of the system during its evolution. The layout is the median of that demand, recomputed by both halves of the layer and
never by one (C12), and it moves only when the demand has; by maintaining that state the information keeps its Duality stable.

The owner's mission it carries: "while maintaining absolute Observability". The owner's words: "no information, besides its existence, can
be derived from trust" (2026-10-26 22:59Z); "Information is hierarchical" (23:03Z); "Information naturally organizes itself both to
facilitate optimal access across its information layers, but also, as a consequence of actively maintaining the median state, naturally
keeps the DoT stable" (23:10Z).

Status: stable since DMP 0.9.7
Amended: Axiom 3 · The record of DMP 0.9.0 with "from trust only existence", admission, containment, the hierarchy and its evolution,
approved by the owner 2026-10-26 23:02Z, 23:03Z and 23:10Z.
Owner: CM coordinator
Enforced by: DMP-009, DMP-018

## G0 · The ground

The dual gate cannot witness its own first instance: somebody has to trust the first two witnesses. That is the **minting** (C17, the
ceremony that forms LHC and makes the White Keys): at least five parts become the White Key Holders, and that minting draws the boundary of
the instance. No further claim is made about those parts, so that any system can remint and redeploy. It is stated, not derived.

- Until the first LHC is minted, the owner's own words stand for both halves and the record says `LHC pending`. Every gate in DMP that asks
  for the owner's words is G0, not a theorem. The owner can be a Duality, but does not have to be. When the owner is one, that Duality, both
  halves, approves every change to the base DMP the owner's CMOS is derived from (the owner, 2026-10-26 23:32Z).
- The minting ratifies or revokes every act taken under `LHC pending`, by record class and hash range, never one by one; C∞ catches what
  the classes miss.
- A **pressed** instance is G0 as a standing mode: a minting with owner-authorised stand-ins and an owner key whose fresh signed challenge
  the instance requires. Both of its witnesses trace to one part, so it is marked as pressed in every artefact it issues, and nothing it
  does is fully trusted by a minted instance (T-P).
- The loss of a whole half is recovered only by a new minting: whoever holds authority over CMOS re-grounds the Duality outside CMOS, as
  at the first minting, and ratifies or revokes what the surviving half held.
- **Mint prep.** A minting is tried only for a configuration that is provably DMP-complete (the words). Preparing it stores every dependency
  inside the configuration's repository and verifies each one against its pin; without that no White Key is made, and the minting refuses to
  even start. The root repositories are never minted, so they need not hold their dependencies and stay lean.

The owner (2026-10-26 23:28Z): "The boundary is not drawn by people, it is drawn by the minting of at least 5 constituent parts that become the
WKHs. No further claim is made. Otherwise no system could remint and redeploy. The part is defined as a Duality of Trust, one owns trust, the
other information. No further statements about its constituents."; (23:32Z) "The owner CAN, but does not have to, be a DoT. If the owner
is, the DoT has to approve changes to the base DMP that the owner's CMOS is derived from."

What else no chain reaches is listed, not hidden, in the assumption register (chapter 06): the DMP text itself, the mapping of code to acts,
the door's code until its run-time check exists, the failure-class list, the time authorities, the provers' logic, the shared machines.

Status: stable since DMP 0.9.4
Amended: "organisation" replaced, approved by the owner 2026-10-26 15:19Z; mint prep approved by the owner 2026-10-26 15:04Z; the minting
of at least five parts draws the boundary, the owner 2026-10-26 23:28Z; the owner may be a Duality, 23:32Z (stable since DMP 0.9.0 before
that).
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

## T39 · Trust and information gate each other

No trust without information: a witness trusts only on evidence it verified itself, and that evidence is the live state of the part and the
current state of the record, which is information (Axiom 2, Axiom 3). No information without trust: information is inside only while an
admission holds, an admission is a grant both halves made, and content reaches only a part trusted for its class; what no trust reaches is
contained where it lies (Axiom 3, Axiom 1, T37). So the two blocks gate each other from the axioms themselves, and only a Duality that is
both trusted for its acts and holds the information admitted to it is a part of a minted CMOS. A part trusted with no information has nothing
it may act on; information with no part trusted for it is contained; neither is inside.

The owner (2026-10-26 23:42Z): "They also logically gate each other from the axioms themselves, no trust without information and no
information without trust. Logically only a DoT, that is both trusted AND has the right information, can be a part of a minted CMOS."

Derived from: the words (part, inside, admission, contains), Axiom 1, Axiom 2, Axiom 3, G0 (minted), T0, T37, T38.

Status: stable since DMP 0.9.7
Approved: by the owner 2026-10-26 23:42Z (his words above; the section states them).
Owner: CM coordinator
Enforced by: DMP-021

## T40 · A minting originates only from G0 and a DMP-complete state

A minting has exactly two sources and needs both: the ground, G0 (the owner's words until the first minting, and the at least five parts
that become the White Key Holders), and a DMP-complete state of the configuration being minted (the words: every axiom and theorem holds
for all of it and both chains prove it; mint prep stores and verifies every dependency first). Neither alone mints: G0 without a
DMP-complete state has nothing proven to ground, and a DMP-complete state without G0 has no first two witnesses (Axiom 1 cannot witness its
own first instance). A reminting after a loss (G0) and a redeployment of a sealed state are the same act with the same two sources, so any
system can remint and redeploy, and none can mint from less.

The owner (2026-10-26 23:46Z): "Minting can only originate from both G0 and a DMP-complete state."

Derived from: the words (DMP-complete), G0 (the minting, mint prep), Axiom 1 (the first two witnesses are not derived), Axiom 2 (the proof
of completeness is evidence verified at the minting, never a claim), T39.

Status: stable since DMP 0.9.7
Approved: by the owner 2026-10-26 23:46Z (his words above; the section states them).
Owner: CM coordinator
Enforced by: DMP-014, DMP-021

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
  validities run, nothing is promoted, the system is safe and not live, and the balance waits.

Derived from: Axiom 1 (the gate, and the balance: the restoration after a loss), Axiom 2, Axiom 3; premise: a half answers by a majority of its nodes (the
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

Derived from: Axiom 1 (the gate and the balance), Axiom 3, T3.

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

Derived from: Axiom 1 (the gate and the balance), Axiom 2, Axiom 3, T6, T24, T24b; the certificate and the window are construction and configuration.

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

Each of the three axioms alone brings contained information to the entropy floor of T31, by a road of its own:

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

- The balance follows the field downhill: refresh before a lapse, heaviest and stalest first, grantor before child. Restoration moves
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

Derived from: the words (standing, record), Axiom 1 (the gate and the balance), Axiom 2 (the field), Axiom 3, T1, T4, T23, T24b; the graph, the readout and the members'
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

Derived from: Axiom 1 (the two proof chains are two witnesses; the balance), Axiom 3, T4, T32; the digest is construction.

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

## C1 · No act bypasses the gate

Every act between parts of CMOS, into it and out of it passes a point of use that the dual gate decides and the record covers. No part
reaches another by a path the gate does not see, and no part reaches the outside by one.

Derived from: Axiom 1 (the gate has no other way through), Axiom 3 (every use leaves a record), T23 (inside, every point of use is a door),
T28 (enforcement is itself an act both witnesses verify). That the door is **one** door (one universal I/O layer; parts never call each other
directly) is construction: it makes ejection total and the record one place, and a second door would need the dual gate between the doors.

Coverage: proven for the gate and the stack (`Gate.one_witness_never_acts`, `Record.performed_only_with_both`: nothing is performed without
both verdicts in the record; `Recipe.decide_pass_iff`: an act passes a line only when every layer on its chain passed it); argued for a running
system: that no component reaches another beside its door is shown per component by the evidence chain (chapter 06), not by the theory.

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:29Z (stable since DMP 0.1.0 before that).
Owner: Milky Way coordinator
Enforced by: DMP-008

## C2 · Every message is its own evidence

A receiver trusts a message only on what the message carries and what the receiver verifies itself, live: the sender's standing, the
sender's key on the message, the right for that kind of act, and the time. Every input gets exactly one answer and fails closed; the same
message is never acted on twice; when the receiver cannot verify liveness, nothing verifies.

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

Derived from: Axiom 2 per message (L1 with T1; L3: evidence is live for one point of use), Axiom 1 (L2: one answer, fail closed; L3b:
when liveness cannot be verified, nothing verifies), T1 (L4), T20 (the validity window is "now" as evidence). The laws above are the
constraint stated for IM version 0 and lock its instance (one kind, one permission, one signer role); the general sentence is the constraint.

Coverage: proven for the laws, from the sentences (`r1l_L2_gives_L1`, `r1l_L1_gives_L4`, `r1l_L2_gives_L4`, `r1l_verify_table_order`, and
the counterexamples `r1l_alwaysRefuse_L1` to `r1l_alwaysRefuse_L4`) and from the model of the code (`r1b_L1`, `r1b_L2_clean`, `r1b_L3`,
`r1b_L3b`, `r1b_L4`), and at most once over any sequence of calls under the wave clock and under Lensing (`wv_verified_at_most_once`,
`ln_verified_at_most_once`); argued for the general sentence beyond version 0. Known and recorded, not hidden: a clock that moves back
lets the same frame verify twice (`r1bW_clock_rollback_replays`); L3 holds as worded and T20 forbids the clock.

Status: stable since DMP 0.9.7
Amended: recast as a constraint, the Law block unchanged, approved by the owner 2026-10-26 23:29Z (stable since DMP 0.1.0 before that).
Owner: Milky Way coordinator
Enforced by: DMP-018

## C3 · A value reaches only parts trusted for its class

Every piece of information inside carries the class and the provenance its admission named, wherever it goes. A part not trusted for that
class never receives the value, only its count. Revealing a value to parts of a further class is a loosening, never a default.

Derived from: the words (admission names the class and the provenance), T0, T1 (a value a part is not trusted for never reaches it), T21
(counts always, content by grant), T24 (a reveal is a loosening), T27. A token on every value at a border, and the identification axis a value
carries, are construction of the shield (chapter 05).

Coverage: undershoot. The evidence chain models admission as one bit on an item (`Floor.road_gate`: a contained item is never read) and the
gate per act; nothing models a value's class or provenance travelling with it from part to part, and `Standing.loosening_needs_both` speaks of
a standing, not of a class of readers. The missing theorem: a value's class is never widened by one witness, and no path delivers a value to a
part outside its class (Denning's lattice: a flow is allowed only where every class on the path allows it).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:29Z (stable since DMP 0.1.0 before that).
Owner: Milky Way coordinator
Enforced by: none yet

## C4 · A change to what a part speaks is a new part

A contract, what a part promises on its surface, takes effect only through the dual gate at a point both witnesses named, never mid-act
and never silently; a thing has one name. The same holds for the information a productive part holds: it changes only through a recorded
act of both witnesses. Before a part is productive its information carries no trust and may be shaped freely.

Derived from: T0 (a changed contract is a new part, trusted by nothing by default; information before production has no standing), Axiom 1
(its trust has two witnesses: the promotion's two chains, the reviewed release), Axiom 3 (the change and the data are auditable because they
are records), T1 (a name is part of what was granted), T22 (a change to a law is an act in the same shape). The stable promotion as the point,
the register of contracts and the production point are construction (chapter 02 "Foundation bricks are complete", chapter 05 "Settings").

Coverage: argued. The nearest theorems are the frame rule (`Recipe.extend_preserves_decisions`: what is added changes nothing that exists)
and `Standing.loosening_needs_both`; nothing models a contract or its version.

Status: stable since DMP 0.9.7
Amended: recast as a constraint and merged with C5 (financial data freezes at production), approved by the owner 2026-10-26 23:49Z (both stable since
DMP 0.1.0 before that).
Owner: CM coordinator
Enforced by: none yet

## C6 · Who may act on what is a bounded grant in the record

No part acts beyond its grant, and a grant is a record both witnesses can read, with a bound. An act on a system or on information outside
CMOS's own estate needs both halves of the layer it belongs to; a widening of what a part may do on its own is a loosening; anything
irreversible outside that estate needs the halves' words per act. The standing grant to agents is the record below, word for word, and
rule DMP-009 refuses a change to it.

<!-- authority-grant:begin -->
> Agents may build, change and deploy CMOS's own estate on their own authority, using credentials held in the operations master, under plan, audit and four eyes where a second administrator exists. An act on a system or on information outside CMOS's own estate needs both halves of the layer it belongs to (T38), its organisational and its factual responsibility. Each half may declare advanced testing reached for its layer; until both have, every act there needs their words per act, and anything irreversible outside CMOS's own estate always does. Until the minting the owner's own words stand for both halves (G0).
<!-- authority-grant:end -->

Derived from: T1 (trust is bounded: the acts a part may do are what both witnesses granted), Axiom 3 (the grant is a record, tamper-evident),
T38 (every layer outside CMOS's own estate has its organisational and its factual half), T24 (declaring advanced testing reached, and every
other widening, is a loosening), G0 (until the minting the owner's words are both halves). Which layers exist, who holds their halves, and the
wording of the grant are configuration (T4).

Coverage: proven for the shape (`Gate.gate_pass_iff` and `meet_pass_iff`: an act passes only what every witness on the chain passed, the
intersection of the grants; `Recipe.pass_has_two_independent_witnesses`: whatever passes was passed by a minted Duality on its chain;
`Standing.loosening_needs_both`); argued for the grant as a readable record with a bound (no theorem models a grant's wording or scope).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, the grant unchanged, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.1.0, grant abstracted 0.9.5 before that).
Owner: CM coordinator
Enforced by: DMP-009

## C7 · What runs is what the record says

Every part that runs is deployed from one recorded, pinned description of the whole, the recipe, in whatever shape the place needs. The
configuration that exists is the one both witnesses can read and the chains proved; nothing runs that the record does not describe.

Derived from: Axiom 3 (the recipe is the record of what exists), the words (a DMP-complete configuration includes the repository it is built
from, every dependency pinned), G0 (mint prep verifies every pin), T33 (the same recipe spans the same field), T35 (compliance is assembled
from a recipe, never claimed). One repository, the folder layout as the composition tree and the shapes are construction (chapter 02, DMP-004).

Coverage: proven for the recipe (`Recipe.file_determines`: the recipe read back gives the same verdict, digest and every decision;
`Recipe.digest_congr`; `Cmb.redeploy_is_recipe_field`, `Cmb.bigBang_keeps_the_proof`; `Recipe.verdict_proven_iff`); argued that a running
system is the one its recipe describes (that is the evidence chain per deployment, C25).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.1.0 before that).
Owner: CM coordinator
Enforced by: DMP-004

## C9 · What is published holds nothing that is inside

Nothing inside leaves except by an export. What CMOS publishes (its code, its text, its public tree) holds no secret, no fact about the
organisation that runs it and no person beyond a role; real values stay inside and are passed as parameters. The public tree holds no
trust for anything inside.

Derived from: T19 ("leave CMOS" is an act with no trust by default), T27 (contained to outside is an export and needs both halves), T31
(contained information reaches no part outside by any act), T0, T1. The scan of the tree, the settings store and the licence are
construction and decisions (DMP-001, DMP-006, chapter 14 "The licence").

Coverage: proven for the trap (`Floor.road_gate`: a contained item is never read, forwarded or sent; `Floor.destroyed_stays`); argued for
a publication being an export (no theorem models a public tree).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.1.0 before that).
Owner: CM coordinator
Enforced by: DMP-001, DMP-006

## C10 · CMOS is trusted only for acts on its own estate

CMOS acts only on the systems and the information that whoever holds authority over it holds authority over. No act aims at a third
party's systems, no act harvests or evades on anyone else's, and no test touches a productive system without a record naming the
signatory, the targets and the window. A request that would break this is refused. The wording is the charter, [CHARTER.md](CHARTER.md).

Derived from: T1 (trust is bounded to named acts on named parts), T0 (an act outside the estate has no trust), G0 (authority comes from
whoever holds authority over CMOS, outside CMOS), Axiom 3 (the scope file is the record). That every security-shaped brief carries its
one-line context is construction (DMP-002).

Coverage: argued. The gate shape (`Gate.one_witness_never_acts`) is the only theorem that bears; nothing models the estate's boundary.

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.1.0 before that).
Owner: Black Hole coordinator
Enforced by: DMP-002

## C12 · Position never grants, and routing reaches everything inside

Every piece of information inside the Event Horizon is reachable by one routing that asks the nearest place first and crosses the horizon
last, only when nothing inside answers. Where a piece of information sits, and how a layer places information or work, never decides who
may act on it: that comes only from its admission and the dual gate. A new kind of information is a new layer that inherits the gate, the
record and the placement and changes nothing else. A private place is one routing cannot see into: neither an answer nor its absence reveals
what it holds, its record shows counts and never content, and no single loss removes it.

Derived from: the words (admission, contains, layer, door), Axiom 1, Axiom 2 (a position in the network is never evidence, so a place cannot
grant), Axiom 3 (counts always, content by grant), T3 (no single part's loss stops a place that holds information), T21, T27, T36 (every
crossing of the horizon is an act, so it comes last and only when needed), T37 (what no admission names is contained where it lies), T38
(every information layer has its own Duality), C1 (every route runs through the one door). The tiers, the net that routes, the layers in use,
the reorganisation by the field, and the floor of three nodes per private place are construction and configuration (chapter 05 "The
information tiers and the layers", chapter 14 "Ask the nearest tier first").

Coverage: proven for position never grants (`Config.relayout_never_votes`, by reflexivity: placement is not an argument of the gate;
`Field.n1b_schedule_never_votes`), for the routing order and the invisible private place (`tr_milky_way_first`, `tr_exit_last`,
`tr_never_singular`, `tr_singularity_invisible`, `tr_first_scrub`), for a new layer changing nothing existing (`Recipe.extend_preserves_decisions`,
`Recipe.extend_wf`) and for a layer reorganising only on both witnesses' own recomputation and healing from any state (`ms_accepted_agrees`,
`ms_wrong_layout_refused`, `ln_signed_moves`, `st_self_stabilizing`, `st_no_root_takeover`); argued for "no single loss removes it" (the node
floor is T3's premise and has no theorem).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.1.0, redrafted 0.9.3 before that).
Owner: CM coordinator
Enforced by: DMP-002

## C13 · The record inside is read before anyone outside is asked

Before a question crosses the horizon or reaches a person, every record inside that could answer it has been read, and the answer names the
record it rests on or says that none held it. What was learned outside is written back inside as a record, so that the next asker finds it
inside.

Derived from: Axiom 2 (evidence the asker verified itself: the record first, never a memory), Axiom 3 (the record is there to be read), Axiom 1
(the balance: what the boundary lacks is built by evidence: the write-back), T36 (a crossing is an act and comes last), C12 (one routing, nearest first).
The order of the tiers and the steps an agent takes are construction (chapter 14 "Ask the nearest tier first", "Knowledge goes back into
Tier 1").

Coverage: proven for the order (`tr_milky_way_first`, `tr_exit_last`); argued for "names the record" and for the write-back (no theorem
models an asker or an intake).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.2.0, redrafted 0.9.3 before that).
Owner: CM coordinator
Enforced by: DMP-002

## C14 · An answer rests on every piece of evidence its author can verify

When one source does not give the full picture, the answer rests on every read path its author can verify itself, joined, and says what is
still unknown. A write path is read through its trail or its dry run, never by writing to find something out.

Derived from: Axiom 2 (only evidence the author verified itself), Axiom 1 (the balance asks for the complete answer, the bounds only forbid
the unsourced one; and a write to find something out is an act with no trust behind it, T0), Axiom 3 (the trail of past writes is
the record). Which paths exist and the lines an agent writes are construction (chapter 14 "Combine every path").

Coverage: argued. No theorem models an author joining sources; the bound (no unsourced answer) is C21's, the completeness is the balance's.

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.3.0 before that).
Owner: CM coordinator
Enforced by: DMP-002

## C15 · A write lands only on a state its writer verified live

In the owner's words (2026-10-25): "a write first reads the live state it will change and asserts what it expects: absent for a create, a
named value for an overwrite." The read is live from the target at the point of use, never a value read earlier; when the expected state is
not found, nothing is written; every write is read back and must show the wanted state. The target of a write (the system, the tenant, the
host, the account) is part of what was granted.

Derived from: Axiom 2 (live state at the point of use; an earlier read is never evidence; the absence of the expected state is the absence of
evidence, and nothing moves), Axiom 3 (the read-back is the record of the act), T1 (the target is part of the grant). Retries, hard and soft
failure and the script pattern are construction (chapter 10 "Guarded writes").

Coverage: proven for the shape (`Gate.stale_never_passes`: evidence past its validity never passes; `Gate.guard_pass_needs_evidence`;
`Record.step_records_at_act`: the act and its record are one step, no path performs one without the other); argued for the expected state and
the read-back (no theorem models a write's expected state).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.3.0 before that).
Owner: CM coordinator
Enforced by: DMP-012

## C16 · Widening who may use a feature on information is a loosening

A feature that acts on an organisation's information reaches a wider circle of users only through two independent witnesses: one that
explains how the information is handled and which protections keep it safe, and one that attacked it and proved the protections, sharing
nothing with the first. Both records are written so that both halves of the information's layer can read them.

Derived from: Axiom 1 (two witnesses: the builder who explains and the session that attacks), Axiom 3 (both pages from one template so both
halves can read them), T24 (a wider circle is a loosening), T38 (the organisational and the factual half of the information's layer). The
two stages, the register and the template are construction (chapter 14 "The data gate", DMP-013).

Coverage: proven for the shape (`Standing.loosening_needs_both`); argued for the independence of builder and attacker (a failure class the
minting declares, T4).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.3.0 before that).
Owner: Black Hole coordinator
Enforced by: DMP-013

## C17 · The top layer is a Duality like every other

CMOS has no single owner. Whoever administers it does so as one half, checked by the other half, the body of people the minting binds;
neither half acts on the composition of CMOS alone. A new part at the top of the composition needs the words of both halves, recorded;
until the first minting the owner's words stand for both (G0). Every system keeps every other system in check while keeping it running in
the best shape it can.

The two halves at the top have names (2026-10-25). **The Administrator**, always written with a capital The, is a mythical entity, not a
person: everyone who is allowed to fully administer CMOS, allowed both by whoever holds authority over CMOS and by CMOS itself. **LHC**,
the Lighthole Council, is the minting ritual of CMOS; the fellowship of people it binds together, its members, is the other half.

Derived from: Axiom 1 at the governance layer, G0 (`LHC pending`), T24b (lockstep: neither half grants itself more), T24c (every system
keeps every other in check), T38 (the layer's two responsibilities). The approval record of a new top-level part, and what the levels below
it are, are construction (chapter 02 "Adding a node", "Foundation bricks are complete"); the two names are definitions.

Coverage: proven for the shape (`Ground.minted_no_single_control`, `Layer.one_person_is_no_duality`, `Layer.same_part_is_no_duality`,
`Recipe.root_minted`: the galaxy at the end of every chain is minted; `Recipe.pressed_root_not_wf`); argued for "a new part at the top needs
both halves' words" (the register is construction, DMP-014).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.3.0, "organisation" replaced 0.9.4 before that).
Owner: CM coordinator
Enforced by: DMP-014

## C18 · A weakness is evidence against what it claims, never against the part

No change on the inside cuts a part off: whatever spoke to CMOS under a contract it was locked to keeps a route (T3b). When an algorithm or a
format that contract rests on becomes unsafe, trust in what the weak part claims is withdrawn, a tightening; the part's own standing is not
withdrawn without evidence against the part. Until a second, independent path vouches for the part's identity, its traffic is contained and
carries no more rights than it had.

Derived from: T2 (a standing is withdrawn only on evidence against it; an unsafe algorithm is evidence against the algorithm), T3 (its
no-lockout clause: removing the last route cuts a half off), T24 (the withdrawal is a tightening, one witness), Axiom 2 (a path the part sits on is a position in the
network, never evidence by itself; the second path is evidence the receiver verifies), T1 (no more rights than before), T27 (contained until
then). That a locked contract is never broken by its own side, that a later version only adds, and that a corpus of every locked version
replays through every build, are construction (chapter 04 "Compatibility after the lock").

Coverage: undershoot. Containment is proven for an item (`Floor.road_gate`) and tightening on one witness for a standing
(`Standing.one_witness_tightens`); nothing in the evidence chain models a route, a locked contract or the difference between evidence against
an algorithm and evidence against a part, and T3b has no theorem at all. The missing theorem, shared with C25: after any single act every half
keeps at least one route.

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 22:56Z (stable since DMP 0.3.0 before that).
Owner: Milky Way coordinator
Enforced by: DMP-015

## C19 · Two halves on every served request: trust and information, each verified by the server itself

A part serves a request only when the request carries both halves of its layer's Duality: the half that owns trust (the request may be
served: who asks, under what standing) and the half that owns information (the request is meant: what is asked, and whether it is right to
serve it). Neither half overrules the other; they keep each other constantly in check, and the serving part verifies both itself at the
point of use; that a carrier already checked the request does not count. A right to speak for one half counts only when the other half has
signed that right too.

The owner (2026-10-26): "One authority owns Trust, the other owns Information. To exist inside of DMP, they are required to keep each other
constantly in check and balance so that no element can overrule the other."

Derived from: Axiom 1 (two witnesses, each with full reach, gated only by the other, the stricter stands; the halves keep each other in
balance), Axiom 2 (a carrier's check is not the server's evidence), T24b (a grant to sign for a half is a lockstep act), T24c (a half
that contradicts the state both certified has incriminated itself), T38 (the two responsibilities of an information layer). Which parts hold
the two halves, and how their signatures travel in a message, are construction (chapter 04 "Serving at a warehouse").

Coverage: proven for the shape (`Gate.gate_pass_iff`, `Gate.act_needs_both_with_evidence`, `Layer.DoT.gate_pass_needs_both`,
`Recipe.pass_has_two_live_witnesses`: a pass at any line means two independent halves both passed on live evidence); argued for "never taken
from a carrier", for the co-signed right to speak, and for the halves being one of each kind: the model types no half, so "a Duality holds one
trust half and one information half" is a theorem still to add.

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:30Z (stable since DMP 0.3.0 before that).
Owner: Black Hole coordinator
Enforced by: DMP-016

## C21 · A statement is a write

What a part states to a person or to the record is an act. It rests on a source the part read itself and names; an unsourced statement does
not land. Where more than one record fits, every one is named and none is picked. C14 covers the question the author knows it cannot
answer; this constraint covers the answer it only thinks it knows.

Derived from: the words (an answer an agent gives is an act), Axiom 1 (an act needs trust), Axiom 2 (trust needs evidence the part verified
itself; a memory is not evidence), Axiom 3 (the source named is the record), T0. The lines an agent writes (`Looked up:`) are construction
(chapter 14 "Look it up, don't guess").

Coverage: argued. No theorem models a statement as an act; until DMP 0.9.7 this rested on a premise, now the words carry it.

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.6.0 before that).
Owner: CM coordinator
Enforced by: DMP-002

## C22 · "Proven" is an act with two witnesses

"The entire point of CMOS is to never trust any one interconnected system" (the owner, 2026-10-25). That a part is proven is therefore
decided by two independent chains: one from the intent down to the code, one from the code up to the intent. They share nothing they could
fail together beyond the roots they declare, each starts from its own source, and neither takes a result from the other. A part counts as
proven only where both agree; a mismatch leaves it unproven, the gate turns red, the stricter answer stands at run time, and nothing that
depends on it moves until both agree again. The chains prove that text, code and run agree; the text itself is verified only by a reader
who did not write it. "This approach applies universally throughout CM, it has to be implemented wherever it is technically possible"
(the owner, 2026-10-25).

Derived from: Axiom 1 (two independent witnesses for the act "this part is proven"), Axiom 2 (each reads its own source), T22 (a change
to a law is an act with a cold read), T29 (a shared cause turns the proof layer into one witness), T30 (where the second chain cannot be
built yet is a record with a validity, never an escape). Which engine carries which chain, the lemma ledger and the alarm are construction
(chapter 06 "The gravitational wave test").

Coverage: proven for the verdict (`Recipe.verdict_proven_iff`: proven exactly when the recipe is well formed and the ledger carries every
line; `Recipe.one_unproven_line_unproves`; `Cmb.sealed_needs_both`, `Cmb.sealed_proven_complete`); argued for the chains' independence, which
is checked per engine (gravity finding F3: the exporter is one step both kernels share, T29).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.4.0 before that).
Owner: CM coordinator
Enforced by: DMP-002

## C24 · Restorable is shown, never claimed

A configuration is declared fit for the whole organisation it serves only when its restoration from nothing has been shown: every scenario
of a signed catalogue passed its pass line in a sandbox, the most critical through a simulator, at least one on the real infrastructure,
and both witnesses stood intact afterwards. The people who restore have what they need to do it without the system that failed. The pass
line's numbers are configuration.

Derived from: Axiom 2 (shown, not claimed), Axiom 1 (both chains intact afterwards), Axiom 3 (the run record), T3 (continuity after a loss),
T35 (compliance is re-shown, never carried over), T-P (a whole organisation runs on a minted instance, never a pressed one). The catalogue,
the pass line, the assistant and the bundle are construction (chapter 11).

Coverage: proven for the cold start from a sealed record (`Cmb.redeploy_iff`, `Cmb.bigBang_keeps_the_proof`, `Cmb.plan_refused_elsewhere`:
the plan is chosen by the live field, never the proof, and refused under other pins); argued for the catalogue and the pass line (chapter 11;
gravity finding F10 is open on the restoring environment's capability).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.7.0 before that).
Owner: Black Hole coordinator
Enforced by: none yet

## C25 · A deployment is gated on the exact thing deployed, and removes no route

"When deploying any allowed configuration of CM to CMOS, DMP must always be obeyed" (the owner, 2026-10-25). A deployment, any promotion
of a part to any channel, runs the gate on the exact commit it deploys, never on an earlier run, and does not deploy on red; one that skipped
the gate is not fully trusted and is ejected. No deployment leaves an administrator action without a supported route on the channel it
serves: the list of known gaps only shrinks, and a deployment that would remove the last route for any action is refused before it runs.

The rule has a history (the owner, 2026-10-25): the stable channel once served a client that could no longer change a setting the owner
needed, with no other supported route left. "We MUST learn from this, so when configuring an impossible situation, we must create rules that
enforce it never happens again."

Derived from: Axiom 1 (a deployment is an act; the balance: the gaps only shrink), Axiom 2 (checked on the exact commit, not an earlier run),
T3 (its no-lockout clause: removing the last route cuts a half off). Channels, the route register, its cap and the workflow register are
construction (DMP-020).

Coverage: undershoot for no lockout: no theorem models a route, and T3's clause has none (shared with C18); the missing theorem is that
after any single act every half keeps at least one route to every action. Proven for the exact-commit shape (`Gate.stale_never_passes`,
`Recipe.verdict_proven_iff`, `Recipe.one_unproven_line_unproves`: one line the chains do not carry unproves the whole).

Status: stable since DMP 0.9.7
Amended: recast as a constraint, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.8.0 before that).
Owner: CM coordinator
Enforced by: DMP-002, DMP-020
Not yet checked in cm: the tie between the actions named in the audit log and `DMP/routes.txt`, and the plan's refusal of a
deployment that would remove a route. The administration tool's own test holds them until its code moves into cm;
DMP-020 checks the gate on every deployment workflow, the route register, its cap and its ratchet.

## C∞ · The root constraint: what is not fully trusted is ejected

"Everything that is not fully trusted is automatically ejected" (the owner, 2026-10-25). This is the thesis of DMP, the root constraint
that every other constraint refines, and its permanent stopgap. Its reserved number is C23; it is written C∞ because it always stands
last: every new section of this chapter is inserted above it, so whatever the sections above do not catch, it does. **Fully trusted** means what the dual gate says; at the proof layer its two
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

Coverage: proven for the thesis (`Gate.one_witness_never_acts`, `Gate.act_needs_both_with_evidence`, `Record.performed_only_with_both`: nothing
acts, and nothing is performed, without both witnesses on live evidence); the meta-rule is not a theorem and has no coverage to claim.

Status: stable since DMP 0.9.7
Amended: named the root constraint and given its Coverage line, approved by the owner 2026-10-26 23:49Z (stable since DMP 0.5.0 before that).
Owner: CM coordinator
Enforced by: DMP-002, DMP-017
