import Dmp.Ground
import Dmp.Field

/-!
# The layer: one signature for every information layer

DMP, the words: "A **layer** is a place where trust is decided; its two witnesses are its **halves**; the pair at a layer is the
**Duality of Trust** (DoT)." And: "Two witnesses are **independent** when they share no member of the failure classes declared for
their layer in the minting record (host, key, author, engine, operating image, person, time source, and whatever else the minting
lists). Independence is always relative to that list; the list is configuration (T4)."

The owner, 2026-10-26 15:25Z: Gravity has to hold "across all the possible layers it can create as a hyperdimensional information
layout recipe". Before the recipe (Recipe.lean) comes the one thing every line of it adds: a layer, and what it means for its two
halves to be two.

**Prior art this file stands on.** A failure-class list is a *fail-prone system* in the sense of Malkhi and Reiter ("Byzantine Quorum
Systems", Distributed Computing 11, 1998): a family of sets of servers, each of which may fail together. Here the fail-prone sets are
"every witness on host h", "every witness signed by key k", "every witness that traces to person p", one set per declared class and
member. Two witnesses are independent exactly when no fail-prone set holds both, which is the dissemination-quorum condition for a
quorum of size two. Nothing here is invented beyond that reading.

What this file proves:

1. Independence is "no shared fail-prone set" (the Malkhi-Reiter reading), it is symmetric, and declaring more classes only removes
   independence, never adds it (T29: discovering a shared cause tightens; T4: the list is the minting's).
2. The person is always a declared class. Without that, an empty list would make any two witnesses independent, including one witness
   written twice, and a pressed pair (G0, T-P) would count as minted. So `DoT.minted` reads the list as `person :: list`: the one
   class that is not configuration.
3. The same part twice is never a Duality (T38's "the two halves may be one part only when that part is itself part of a higher-order
   DoT"; the higher-order gating is Recipe.lean's business).
4. The layer's gate is the dual gate of its halves behind the evidence guard (K1, K2): an act passes only when both halves passed on
   live evidence; and on a minted pair an influence confined to one fail-prone set never passes an act alone, because the other half
   is outside that set and the stricter answer stands. On a pair that is not minted, one fail-prone set holds both halves: the shared
   cause of T29, and the collapse of Ground.lean.
5. The pair of Ground.lean (two owners) is the special case of a Duality with the person as the only class.
-/

namespace Dmp

/-- A failure class the minting record declares. Class `0` is the person; the others (host, key, author, engine, operating image, time
    source, ...) are whatever the minting lists (T4). -/
abbrev FailureClass := Nat

/-- The one class every list carries: the person a witness traces to (G0: "two people"; T-P: "both trace to one"). -/
def person : FailureClass := 0

/-- A witness as the minting record describes it: for every failure class, the member it belongs to (which host, which key, which
    person). -/
structure Witness where
  member : FailureClass → Nat

/-- The fail-prone set of class `c`, member `m` (Malkhi and Reiter): every witness that fails, or is bent, when that member is. -/
def failProne (c m : Nat) (w : Witness) : Prop := w.member c = m

/-- The words: two witnesses are independent when they share no member of any declared failure class. -/
def independent (fcl : List FailureClass) (w1 w2 : Witness) : Prop := ∀ c ∈ fcl, w1.member c ≠ w2.member c

/-- The same, decided. -/
def independentB (fcl : List FailureClass) (w1 w2 : Witness) : Bool := fcl.all fun c => w1.member c != w2.member c

theorem independentB_iff (fcl : List FailureClass) (w1 w2 : Witness) : independentB fcl w1 w2 = true ↔ independent fcl w1 w2 := by
  simp [independentB, independent, List.all_eq_true, bne_iff_ne]

/-- The Malkhi-Reiter reading: independent means no fail-prone set of a declared class holds both witnesses. -/
theorem independent_iff_no_shared_failProne (fcl : List FailureClass) (w1 w2 : Witness) :
    independent fcl w1 w2 ↔ ∀ c ∈ fcl, ∀ m, ¬ (failProne c m w1 ∧ failProne c m w2) := by
  constructor
  · intro h c hc m ⟨h1, h2⟩
    exact h c hc (h1.trans h2.symm)
  · intro h c hc heq
    exact h c hc (w1.member c) ⟨rfl, heq.symm⟩

theorem independent_symm (fcl : List FailureClass) (w1 w2 : Witness) (h : independent fcl w1 w2) : independent fcl w2 w1 :=
  fun c hc heq => h c hc heq.symm

/-- T4, T29: a longer list is stricter. Independence under a list holds under every list it contains, never the other way round. -/
theorem independent_of_superlist (fcl fcl' : List FailureClass) (w1 w2 : Witness) (hsub : ∀ c, c ∈ fcl → c ∈ fcl')
    (h : independent fcl' w1 w2) : independent fcl w1 w2 :=
  fun c hc => h c (hsub c hc)

/-- T29: discovering a shared cause the minting did not declare is evidence against the layer's independence: once the class is
    declared, the pair is a one-witness layer. -/
theorem shared_cause_breaks_independence (fcl : List FailureClass) (c : FailureClass) (w1 w2 : Witness)
    (hshared : w1.member c = w2.member c) : ¬ independent (c :: fcl) w1 w2 :=
  fun h => h c List.mem_cons_self hshared

/-- With no class declared, any two witnesses are independent, the same witness twice included. This is why the person is not
    configuration (`DoT.minted` below): the list alone cannot carry G0. -/
theorem empty_list_makes_everything_independent (w1 w2 : Witness) : independent [] w1 w2 :=
  fun _ hc => absurd hc List.not_mem_nil

/-- The same part is never independent of itself under any declared class. -/
theorem same_part_never_independent (fcl : List FailureClass) (c : FailureClass) (w : Witness) :
    ¬ independent (c :: fcl) w w :=
  shared_cause_breaks_independence fcl c w w rfl

/-! ## The Duality of Trust at a layer -/

/-- The two halves of a layer. -/
structure DoT where
  w1 : Witness
  w2 : Witness

/-- G0, K0: a Duality is minted when its halves are independent under the declared classes, the person always among them. -/
def DoT.minted (fcl : List FailureClass) (d : DoT) : Prop := independent (person :: fcl) d.w1 d.w2

def DoT.mintedB (fcl : List FailureClass) (d : DoT) : Bool := independentB (person :: fcl) d.w1 d.w2

theorem DoT.mintedB_iff (fcl : List FailureClass) (d : DoT) : d.mintedB fcl = true ↔ d.minted fcl :=
  independentB_iff _ _ _

/-- T38: the two halves may not be one part. (When they are, only a higher-order Duality can gate the layer: Recipe.lean.) -/
theorem same_part_is_no_duality (fcl : List FailureClass) (w : Witness) : ¬ (DoT.mk w w).minted fcl :=
  same_part_never_independent fcl person w

/-- T-P: two halves that trace to one person are not minted, whatever else the list declares. -/
theorem one_person_is_no_duality (fcl : List FailureClass) (d : DoT) (h : d.w1.member person = d.w2.member person) :
    ¬ d.minted fcl :=
  shared_cause_breaks_independence fcl person d.w1 d.w2 h

/-- A pair that is not minted has a shared cause: one fail-prone set of a declared class (or the person) holds both halves. -/
theorem not_minted_has_shared_cause (fcl : List FailureClass) (d : DoT) (h : ¬ d.minted fcl) :
    ∃ c ∈ person :: fcl, ∃ m, failProne c m d.w1 ∧ failProne c m d.w2 := by
  apply Classical.byContradiction
  intro hno
  apply h
  intro c hc heq
  exact hno ⟨c, hc, d.w1.member c, rfl, heq.symm⟩

/-- Ground.lean's pair, read as a Duality: each owner is the person its witness traces to, and no other class is declared. -/
def Pair.toDoT (p : Pair) : DoT := ⟨⟨fun _ => p.owner1⟩, ⟨fun _ => p.owner2⟩⟩

/-- Ground.lean's `Pair.minted` is the Duality with the person as the only class. -/
theorem Pair.minted_iff_toDoT (p : Pair) : p.minted ↔ p.toDoT.minted [] := by
  constructor
  · intro h c hc heq
    cases hc with
    | head => exact h heq
    | tail _ hc => exact absurd hc List.not_mem_nil
  · intro h heq
    exact h person List.mem_cons_self heq

/-! ## The layer's gate -/

/-- The layer's gate: each half judges behind the evidence guard (K2) and the dual gate decides (K1). -/
def DoT.gate (d : DoT) (judge : Witness → Evidence → Verdict) (e1 e2 : Evidence) (t : Nat) : Verdict :=
  Dmp.gate (guard (judge d.w1) e1 t) (guard (judge d.w2) e2 t)

/-- K1 and K2 at a layer: an act passes only when both halves passed on live, in-window evidence read at the step. -/
theorem DoT.gate_pass_needs_both (d : DoT) (judge : Witness → Evidence → Verdict) (e1 e2 : Evidence) (t : Nat)
    (h : d.gate judge e1 e2 t = .pass) :
    e1.ok t = true ∧ e2.ok t = true ∧ guard (judge d.w1) e1 t = .pass ∧ guard (judge d.w2) e2 t = .pass :=
  act_needs_both_with_evidence _ _ e1 e2 t h

/-- An influence confined to one fail-prone set: it rewrites the judgment of every witness in the set and touches no other. -/
def bend (c m : Nat) (forced : Evidence → Verdict) (judge : Witness → Evidence → Verdict) : Witness → Evidence → Verdict :=
  fun w => if w.member c = m then forced else judge w

/-- On a minted pair an influence confined to one fail-prone set of a declared class leaves one half untouched, so the gate's answer
    is never looser than that half's own: a single cause can tighten, never pass an act alone (K1: the stricter answer stands). -/
theorem DoT.confined_influence_never_passes_alone (fcl : List FailureClass) (d : DoT) (hm : d.minted fcl)
    (c : FailureClass) (hc : c ∈ person :: fcl) (m : Nat) (forced : Evidence → Verdict) (judge : Witness → Evidence → Verdict)
    (e1 e2 : Evidence) (t : Nat) :
    (d.gate (bend c m forced judge) e1 e2 t).rank ≤ (guard (judge d.w1) e1 t).rank ∨
    (d.gate (bend c m forced judge) e1 e2 t).rank ≤ (guard (judge d.w2) e2 t).rank := by
  have hne := hm c hc
  by_cases h1 : d.w1.member c = m
  · right
    have h2 : ¬ d.w2.member c = m := fun h2 => hne (h1.trans h2.symm)
    have : bend c m forced judge d.w2 = judge d.w2 := by simp [bend, h2]
    unfold DoT.gate
    rw [this]
    exact (gate_stricter _ _).2
  · left
    have : bend c m forced judge d.w1 = judge d.w1 := by simp [bend, h1]
    unfold DoT.gate
    rw [this]
    exact (gate_stricter _ _).1

/-- On a pair that is not minted, the shared cause reaches both halves: one influence passes any act with good evidence alone, the
    collapse of Ground.lean at every layer. -/
theorem DoT.not_minted_collapses (fcl : List FailureClass) (d : DoT) (h : ¬ d.minted fcl) (judge : Witness → Evidence → Verdict)
    (e1 e2 : Evidence) (t : Nat) (h1 : e1.ok t = true) (h2 : e2.ok t = true) :
    ∃ c ∈ person :: fcl, ∃ m, d.gate (bend c m (fun _ => .pass) judge) e1 e2 t = .pass := by
  obtain ⟨c, hc, m, hm1, hm2⟩ := not_minted_has_shared_cause fcl d h
  refine ⟨c, hc, m, ?_⟩
  unfold DoT.gate bend failProne at *
  simp [hm1, hm2, guard, h1, h2]
  decide

end Dmp
