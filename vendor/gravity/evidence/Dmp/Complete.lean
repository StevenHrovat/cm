import Dmp.Theory

/-!
# DMP-complete, as Gravity's verdict

DMP, the words: a configuration "is **DMP-complete** when it obeys DMP's axiomatic system completely and provably so: the words,
every axiom and every theorem of this constitution hold for all of it, and both chains prove it (C22). [...] A part where the chains
disagree, that neither chain reaches, or that needs anything from outside its repository counts as unproven, and a configuration with
an unproven part is not DMP-complete." G0, mint prep: "A minting is tried only for a configuration that is provably DMP-complete."

This file makes the verdict of Recipe.lean the gate mint prep calls: `verdict = proven` is proven equivalent to "the recipe is well
formed and the lemma ledger carries every line" (soundness and completeness of the checker against the specification), `out of spec`
is exactly a recipe the theory refuses, and `unproven` is exactly a well-formed recipe with a line the chains do not both carry. What
Theory.lean proved of every well-formed recipe then holds of every configuration Gravity marks proven, and a proven configuration grows
one checked, agreed line at a time (mint prep is incremental).

What `agree i` stands for: the lemma ledger's answer for line `i` (C22, chapter 06): the two chains reach the layer's components and
agree on them, and every dependency of the layer is inside the repository, pinned (the words). The ledger is an input here; the chains
that fill it are the gravitational wave test, component by component.
-/

namespace Dmp

/-- The words: DMP-complete is "every axiom and theorem holds for all of it" (the recipe is well formed, so Theory.lean applies to every
    line) "and both chains prove it" (the ledger carries every line). -/
def Recipe.dmpComplete (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool) : Prop :=
  r.wf fcl ∧ ∀ i, i < r.n → agree i = true

theorem range_all_iff (n : Nat) (agree : Nat → Bool) : (List.range n).all agree = true ↔ ∀ i, i < n → agree i = true := by
  simp [List.all_eq_true, List.mem_range]

/-- Soundness and completeness of the verdict: Gravity says proven exactly for a DMP-complete configuration. -/
theorem Recipe.verdict_proven_iff (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool) :
    r.verdict fcl agree = .proven ↔ r.dmpComplete fcl agree := by
  unfold Recipe.verdict Recipe.dmpComplete
  rw [← Recipe.wfB_iff, ← range_all_iff]
  cases hw : r.wfB fcl <;> cases ha : (List.range r.n).all agree <;> simp

/-- Out of spec exactly when the recipe is not well formed: a pressed root, a two-node singularity, a Duality nobody gates, a line laid
    over a later one. -/
theorem Recipe.verdict_outOfSpec_iff (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool) :
    r.verdict fcl agree = .outOfSpec ↔ ¬ r.wf fcl := by
  unfold Recipe.verdict
  rw [← Recipe.wfB_iff]
  cases hw : r.wfB fcl <;> cases ha : (List.range r.n).all agree <;> simp

/-- Unproven exactly when the recipe is well formed and some line is not carried by both chains. -/
theorem Recipe.verdict_unproven_iff (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool) :
    r.verdict fcl agree = .unproven ↔ r.wf fcl ∧ ∃ i, i < r.n ∧ agree i = false := by
  have hnot : ¬ (∀ i, i < r.n → agree i = true) ↔ ∃ i, i < r.n ∧ agree i = false := by
    constructor
    · intro h
      apply Classical.byContradiction
      intro hno
      apply h
      intro i hi
      apply Classical.byContradiction
      intro hne
      exact hno ⟨i, hi, Bool.eq_false_iff.mpr hne⟩
    · rintro ⟨i, hi, hf⟩ h
      have := h i hi
      simp [hf] at this
  unfold Recipe.verdict
  rw [← Recipe.wfB_iff, ← hnot, ← range_all_iff]
  cases hw : r.wfB fcl <;> cases ha : (List.range r.n).all agree <;> simp

/-- Every verdict is one of the three, and only one. -/
theorem Recipe.verdict_trichotomy (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool) :
    r.verdict fcl agree = .proven ∨ r.verdict fcl agree = .unproven ∨ r.verdict fcl agree = .outOfSpec := by
  cases r.verdict fcl agree <;> simp

/-! ## What a proven configuration keeps -/

/-- In a configuration Gravity marks proven, whatever passes any line was passed by a minted Duality on its chain (T38, G0). -/
theorem Recipe.proven_pass_has_two_independent_witnesses (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool)
    (hp : r.verdict fcl agree = .proven) (v : Nat → Verdict) (i : Nat) (hi : i < r.n) (h : r.decide v i = .pass) :
    ∃ j ∈ r.chain i, (r.line j).dot.minted fcl ∧ v j = .pass :=
  r.pass_has_two_independent_witnesses fcl ((r.verdict_proven_iff fcl agree).mp hp).1 v i hi h

/-- In a proven configuration a sub-layer's decision is never looser than its parent's (sub-layers only tighten). -/
theorem Recipe.proven_sublayer_only_tightens (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool)
    (hp : r.verdict fcl agree = .proven) (v : Nat → Verdict) (i j : Nat) (hi : i < r.n) (hpar : (r.line i).parent = some j) :
    (r.decide v i).rank ≤ (r.decide v j).rank :=
  r.sublayer_only_tightens fcl ((r.verdict_proven_iff fcl agree).mp hp).1 v i j hi hpar

/-- A proven configuration has no pressed root: every galaxy in it is minted. -/
theorem Recipe.proven_no_pressed_root (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool)
    (hp : r.verdict fcl agree = .proven) (j : Nat) (hj : j < r.n) (hroot : (r.line j).parent = none) : (r.line j).dot.minted fcl :=
  r.root_minted fcl ((r.verdict_proven_iff fcl agree).mp hp).1 j hj hroot

/-! ## Mint prep is incremental -/

/-- A proven configuration stays proven under a new line that is well formed and carried by both chains, and the new line changes no
    existing decision (the frame rule): mint prep checks one line at a time and never re-opens what was proven. -/
theorem Recipe.proven_extend (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool) (hp : r.verdict fcl agree = .proven) (l : Line)
    (hl : (r.extend l).lineOk fcl r.n) (ha : agree r.n = true) :
    (r.extend l).verdict fcl agree = .proven ∧ ∀ v i, i < r.n → (r.extend l).decide v i = r.decide v i := by
  have hc := (r.verdict_proven_iff fcl agree).mp hp
  constructor
  · rw [Recipe.verdict_proven_iff]
    refine ⟨r.extend_wf fcl hc.1 l hl, ?_⟩
    intro i hi
    have hi' : i < r.n + 1 := hi
    by_cases h : i = r.n
    · subst h; exact ha
    · exact hc.2 i (by omega)
  · intro v i hi
    exact r.extend_preserves_decisions fcl hc.1 l v i hi

/-- A line the chains do not carry makes the whole configuration unproven, wherever it sits: no part is proven by its neighbours. -/
theorem Recipe.one_unproven_line_unproves (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool) (i : Nat) (hi : i < r.n)
    (ha : agree i = false) : r.verdict fcl agree ≠ .proven := by
  intro hp
  have := ((r.verdict_proven_iff fcl agree).mp hp).2 i hi
  simp [ha] at this

end Dmp
