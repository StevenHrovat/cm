import Dmp.R1Lens

/-!
# Mass pulls, the DoT signs

The owner, 2026-10-26 13:08Z, asked whether the Milky Way net may author reorderings, "from the DoT itself or from the majority of the
trust mass pulling the other nodes naturally inward". Agreed at 13:20Z: the mass pulls, the DoT signs. The field's pull (Infall)
computes the new layout as a pure function of the signed trust state; each of the two witnesses recomputes it with its own build and
signs only when its own result matches. A majority stays unweighted (T5); the weight never votes (T32).

What this file proves:

1. An accepted layout is one both witnesses' builds computed: a layout neither computed, or only one computed, is refused.
2. On one signed state at most one layout is accepted.
3. Why the weight may not vote: one hub with `n` children holds more than half of all mass, so a mass majority can be one part,
   which is one witness. One seat is never a seat majority once there are two seats.
-/

namespace Dmp

/-! ## Each witness recomputes the layout -/

/-- A reorganisation as the net authors it: the signed state it derives from and the layout it proposes, one layer per part. -/
structure MsProposal (S : Type) where
  state : S
  layout : List Nat

/-- A witness signs a proposal only when its own build of the layout law gives the proposed layout on the proposed state. -/
def msSigns {S : Type} (law : S → List Nat) (p : MsProposal S) : Bool := decide (law p.state = p.layout)

/-- A proposal is accepted when both witnesses, each with its own build, sign it. -/
def msAccepted {S : Type} (law1 law2 : S → List Nat) (p : MsProposal S) : Prop := msSigns law1 p = true ∧ msSigns law2 p = true

theorem ms_signs_iff {S : Type} (law : S → List Nat) (p : MsProposal S) : msSigns law p = true ↔ law p.state = p.layout := by
  unfold msSigns
  exact decide_eq_true_iff

/-- An accepted layout is what both builds compute, so the two builds agree on that state. -/
theorem ms_accepted_agrees {S : Type} (law1 law2 : S → List Nat) (p : MsProposal S) (h : msAccepted law1 law2 p) :
    p.layout = law1 p.state ∧ law1 p.state = law2 p.state := by
  have h1 := (ms_signs_iff law1 p).mp h.1
  have h2 := (ms_signs_iff law2 p).mp h.2
  exact ⟨h1.symm, h1.trans h2.symm⟩

/-- A layout that one build does not compute is refused, however much mass stands behind it. -/
theorem ms_wrong_layout_refused {S : Type} (law1 law2 : S → List Nat) (p : MsProposal S)
    (h : law1 p.state ≠ p.layout ∨ law2 p.state ≠ p.layout) : ¬ msAccepted law1 law2 p := by
  intro ha
  cases h with
  | inl h1 => exact h1 ((ms_signs_iff law1 p).mp ha.1)
  | inr h2 => exact h2 ((ms_signs_iff law2 p).mp ha.2)

/-- On one signed state at most one layout is accepted. -/
theorem ms_one_layout {S : Type} (law1 law2 : S → List Nat) (p q : MsProposal S) (hs : p.state = q.state)
    (hp : msAccepted law1 law2 p) (hq : msAccepted law1 law2 q) : p.layout = q.layout := by
  have h1 := (ms_signs_iff law1 p).mp hp.1
  have h2 := (ms_signs_iff law1 q).mp hq.1
  rw [← h1, ← h2, hs]

/-- An accepted proposal reaches a node as a reorganisation both witnesses signed: lensing (`R1Lens.lean`) takes it when it is newer. -/
def msMove {S : Type} (law1 law2 : S → List Nat) (p : MsProposal S) (epoch layer : Nat) : LnMove :=
  { epoch := epoch, sig1 := msSigns law1 p, sig2 := msSigns law2 p, layer := layer }

theorem ms_refused_never_moves {S : Type} (law1 law2 : S → List Nat) (p : MsProposal S) (n : LnNode) (epoch layer : Nat)
    (h : ¬ msAccepted law1 law2 p) : lnAccept n (msMove law1 law2 p epoch layer) = n := by
  apply ln_one_witness_never_moves
  unfold msMove
  simp only
  cases h1 : msSigns law1 p
  · exact Or.inl rfl
  · cases h2 : msSigns law2 p
    · exact Or.inr rfl
    · exact absurd ⟨h1, h2⟩ h

/-! ## Why the weight never votes -/

/-- Mass as T32 counts it: one plus the parts that derive through it. A hub with `n` children has mass `n + 1`, each child 1, so
the total is `2n + 1`, and the hub alone holds more than half. -/
theorem ms_hub_is_mass_majority (n : Nat) : 2 * (n + 1) > (n + 1) + n * 1 := by omega

/-- So a rule "more than half of the mass agrees" is met by the hub's signature alone: one part, one witness. -/
theorem ms_mass_vote_is_one_witness (n : Nat) : ∃ backers : List Nat, backers.length = 1 ∧ 2 * backers.sum > (n + 1) + n * 1 :=
  ⟨[n + 1], rfl, by simp only [List.sum_cons, List.sum_nil]; omega⟩

/-- Counted by seats (T5, unweighted), one seat is never more than half once there are at least two. -/
theorem ms_one_seat_never_majority (seats : Nat) (h : 2 ≤ seats) : ¬ (2 * 1 > seats) := by omega

end Dmp
