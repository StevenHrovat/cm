import Dmp.Recipe

/-!
# The unified theory: what survives every line of the recipe

The owner, 2026-10-26 15:25Z: DMP "can only be proven through an absolutely solid unified theory of Gravity across all the possible
layers it can create". The recipe (Recipe.lean) is every layout; this file proves, by induction over its lines, what every layout keeps.
Each theorem names the DMP section it carries.

1. **The stack's gate is a meet** (Axiom 1 at every level). A decision is never looser than any layer on its chain, and a sub-layer's
   decision is never looser than its parent's: sub-layers only tighten. An act passes a line only when every layer on its chain passed it.
2. **Every pass has two independent witnesses** (T38, G0). In a well-formed recipe every chain ends at a galaxy whose Duality is minted,
   so whatever passes anywhere was passed by two independent halves at least once on its chain; a Duality that is not minted is gated
   from above or the recipe refuses it.
3. **A pressed top collapses the whole chain** (T-P, K0). A root whose halves trace to one person is refused by the recipe; were it
   admitted, one influence would pass the root's gate alone, and the root gates nothing else.
4. **The frame rule** (member isolation, T32; containment, T37). Adding a line changes no existing chain and no existing decision: a new
   layer cannot loosen, tighten or reach anything already laid out. Containment is preserved by construction of the recipe: a line adds no
   admission, so an item contained before the line is contained after it.
5. **Siblings do not gate each other** (dwarf galaxies, "Who maintains it"). Two lines laid over the same line never appear on each
   other's chain: a dwarf's Duality decides nothing for its sibling, and each trusts only what is above both.
6. **The layout never votes** (C12, general covariance). Placement and routing order are not arguments of the decision: a reorganisation
   that moves information or work between places changes no gate answer. This holds by the shape of the signature, which is the point of
   C12: "a layer places information, never grants trust" is not a rule the model follows but a place the model does not have.
7. **The expected field is a function of the recipe** (T33, finding F2). Depth and mass of every line follow from the lines alone, so two
   configurations from the same recipe carry the same digest, and the owner can precompute it before anything runs.
8. **The stack potential never rises under the system's own acts** (K4 across layers). The potential of the stack is the sum of the
   layers' potentials, and a refresh at any layer lowers or keeps it.

**Prior art.** 1 is a meet in a security lattice (Denning 1976). 4 is the frame rule of separation logic (O'Hearn, Reynolds, Yang 2001):
a specification about one part holds in any larger context that does not touch it. 6 is noninterference in the sense of Goguen and
Meseguer (1982): the layout is a low input the decision does not depend on. 2 and 3 are the quorum intersection of Malkhi and Reiter
(1998) along a chain.
-/

namespace Dmp

/-! ## 1. The stack's gate is a meet -/

theorem meet_le_mem : ∀ (l : List Verdict) (v : Verdict), v ∈ l → (meet l).rank ≤ v.rank
  | [], _, h => absurd h List.not_mem_nil
  | x :: xs, v, h => by
    simp only [meet]
    cases h with
    | head => exact (gate_stricter x (meet xs)).1
    | tail _ h' => exact Nat.le_trans (gate_stricter x (meet xs)).2 (meet_le_mem xs v h')

theorem meet_pass_iff : ∀ (l : List Verdict), meet l = .pass ↔ ∀ v ∈ l, v = .pass
  | [] => by simp [meet]
  | x :: xs => by
    simp only [meet, List.mem_cons]
    rw [show Verdict.stricter x (meet xs) = gate x (meet xs) from rfl, gate_pass_iff, meet_pass_iff xs]
    constructor
    · rintro ⟨hx, hxs⟩ v hv
      cases hv with
      | inl h => exact h ▸ hx
      | inr h => exact hxs v h
    · intro h
      exact ⟨h x (Or.inl rfl), fun v hv => h v (Or.inr hv)⟩

/-- A line's decision is never looser than its own gate answer. -/
theorem Recipe.decide_le_own (r : Recipe) (v : Nat → Verdict) (i : Nat) : (r.decide v i).rank ≤ (v i).rank :=
  meet_le_mem _ (v i) (List.mem_map_of_mem (r.chain_head i))

/-- Axiom 1 at every level: a line's decision is never looser than any layer on its chain. -/
theorem Recipe.decide_le_chain (r : Recipe) (v : Nat → Verdict) (i j : Nat) (hj : j ∈ r.chain i) :
    (r.decide v i).rank ≤ (v j).rank :=
  meet_le_mem _ (v j) (List.mem_map_of_mem hj)

/-- An act passes a line only when every layer on the chain passed it. -/
theorem Recipe.decide_pass_iff (r : Recipe) (v : Nat → Verdict) (i : Nat) :
    r.decide v i = .pass ↔ ∀ j ∈ r.chain i, v j = .pass := by
  unfold Recipe.decide
  rw [meet_pass_iff]
  simp [List.mem_map]

/-- Sub-layers only tighten: a line's decision is never looser than its parent's. -/
theorem Recipe.sublayer_only_tightens (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (v : Nat → Verdict) (i j : Nat)
    (hi : i < r.n) (hp : (r.line i).parent = some j) : (r.decide v i).rank ≤ (r.decide v j).rank := by
  unfold Recipe.decide
  rw [r.chain_unfold fcl hwf i hi, hp]
  simp only [List.map_cons, meet]
  exact (gate_stricter _ _).2

/-! ## 2. Every pass has two independent witnesses -/

/-- Every chain of a well-formed recipe ends at a line with no parent: its galaxy. -/
theorem Recipe.chain_has_root (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) :
    ∀ i, i < r.n → ∃ j ∈ r.chain i, (r.line j).parent = none := by
  intro i
  induction i using Nat.strongRecOn with
  | _ i ih =>
    intro hi
    rw [r.chain_unfold fcl hwf i hi]
    cases hp : (r.line i).parent with
    | none => exact ⟨i, by simp, hp⟩
    | some p =>
      have hok := (hwf i hi).1
      rw [hp] at hok
      obtain ⟨j, hj, hr⟩ := ih p hok.1 (by omega)
      exact ⟨j, by simp [hj], hr⟩

/-- The galaxy at the end of every chain is minted: a root nobody gates must be a Duality (T38, G0). -/
theorem Recipe.root_minted (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (j : Nat) (hj : j < r.n)
    (hroot : (r.line j).parent = none) : (r.line j).dot.minted fcl := by
  have hok := (hwf j hj).2.2
  cases hok with
  | inl h => exact h
  | inr h => exact absurd hroot h

/-- T38 at the stack: whatever passes a line of a well-formed recipe was passed by a minted Duality on its chain. -/
theorem Recipe.pass_has_two_independent_witnesses (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (v : Nat → Verdict)
    (i : Nat) (hi : i < r.n) (h : r.decide v i = .pass) :
    ∃ j ∈ r.chain i, (r.line j).dot.minted fcl ∧ v j = .pass := by
  obtain ⟨j, hj, hroot⟩ := r.chain_has_root fcl hwf i hi
  have hjn : j < r.n := r.chain_lt fcl hwf i hi j hj
  exact ⟨j, hj, r.root_minted fcl hwf j hjn hroot, (r.decide_pass_iff v i).mp h j hj⟩

/-- With each line's answer its Duality's gate (K1, K2), a pass at any line means two independent halves both passed on live
    evidence read at the step, somewhere on the chain. -/
theorem Recipe.pass_has_two_live_witnesses (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl)
    (judge : Nat → Witness → Evidence → Verdict) (e1 e2 : Nat → Evidence) (t : Nat) (i : Nat) (hi : i < r.n)
    (h : r.decide (fun j => (r.line j).dot.gate (judge j) (e1 j) (e2 j) t) i = .pass) :
    ∃ j ∈ r.chain i, (r.line j).dot.minted fcl ∧ (e1 j).ok t = true ∧ (e2 j).ok t = true ∧
      guard (judge j (r.line j).dot.w1) (e1 j) t = .pass ∧ guard (judge j (r.line j).dot.w2) (e2 j) t = .pass := by
  obtain ⟨j, hj, hm, hv⟩ := r.pass_has_two_independent_witnesses fcl hwf _ i hi h
  obtain ⟨h1, h2, g1, g2⟩ := DoT.gate_pass_needs_both _ _ _ _ _ hv
  exact ⟨j, hj, hm, h1, h2, g1, g2⟩

/-! ## 3. A pressed top collapses the whole chain -/

/-- T-P, K0: a recipe whose root is not minted is not well formed. Gravity's verdict is out of spec, never "proven relative to the
    pressing". -/
theorem Recipe.pressed_root_not_wf (fcl : List FailureClass) (r : Recipe) (j : Nat) (hj : j < r.n)
    (hroot : (r.line j).parent = none) (hp : ¬ (r.line j).dot.minted fcl) : ¬ r.wf fcl :=
  fun hwf => hp (r.root_minted fcl hwf j hj hroot)

/-- Were a pressed root admitted, the one person behind both halves would pass the root's own gate alone (Layer.lean's collapse),
    and the root's decision is its own gate: nothing above it gates it. -/
theorem Recipe.pressed_root_collapses (fcl : List FailureClass) (r : Recipe) (j : Nat) (hroot : (r.line j).parent = none)
    (hp : ¬ (r.line j).dot.minted fcl) (judge : Witness → Evidence → Verdict) (e1 e2 : Evidence) (t : Nat)
    (h1 : e1.ok t = true) (h2 : e2.ok t = true) :
    ∃ c ∈ person :: fcl, ∃ m,
      r.decide (fun _ => (r.line j).dot.gate (bend c m (fun _ => .pass) judge) e1 e2 t) j = .pass := by
  obtain ⟨c, hc, m, hpass⟩ := DoT.not_minted_collapses fcl (r.line j).dot hp judge e1 e2 t h1 h2
  refine ⟨c, hc, m, ?_⟩
  rw [Recipe.decide_pass_iff]
  intro k _
  exact hpass

/-! ## 4. The frame rule -/

/-- The recipe with one more line laid at the end. -/
def Recipe.extend (r : Recipe) (l : Line) : Recipe :=
  { n := r.n + 1, line := fun i => if i = r.n then l else r.line i }

/-- Existing lines are unchanged by an extension. -/
theorem Recipe.extend_line (r : Recipe) (l : Line) (i : Nat) (hi : i < r.n) : (r.extend l).line i = r.line i := by
  simp [Recipe.extend, Nat.ne_of_lt hi]

/-- The chain of an existing line walks only existing lines (parents are earlier lines), so an extension leaves it unchanged. -/
theorem Recipe.extend_chainF (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (l : Line) :
    ∀ fuel i, i < r.n → (r.extend l).chainF fuel i = r.chainF fuel i := by
  intro fuel
  induction fuel with
  | zero => intros; rfl
  | succ f ih =>
    intro i hi
    simp only [Recipe.chainF, r.extend_line l i hi]
    cases hp : (r.line i).parent with
    | none => rfl
    | some p =>
      have hok := (hwf i hi).1
      rw [hp] at hok
      show i :: (r.extend l).chainF f p = i :: r.chainF f p
      rw [ih p (by omega)]

theorem Recipe.extend_chain (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (l : Line) (i : Nat) (hi : i < r.n) :
    (r.extend l).chain i = r.chain i :=
  r.extend_chainF fcl hwf l i i hi

/-- The frame rule: a new line changes no existing decision. A layer added to the stack can neither loosen nor tighten nor reach what was
    laid out before it (member isolation, T32), and nothing contained before it is reached after it (T37). -/
theorem Recipe.extend_preserves_decisions (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (l : Line) (v : Nat → Verdict)
    (i : Nat) (hi : i < r.n) : (r.extend l).decide v i = r.decide v i := by
  unfold Recipe.decide
  rw [r.extend_chain fcl hwf l i hi]

/-- An extension by a well-formed line is well formed, and only then: the recipe grows one checked line at a time. -/
theorem Recipe.extend_wf (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (l : Line)
    (hl : (r.extend l).lineOk fcl r.n) : (r.extend l).wf fcl := by
  intro i hi
  by_cases h : i = r.n
  · subst h; exact hl
  · have hi' : i < r.n := by simp [Recipe.extend] at hi; omega
    have hline : (r.extend l).line i = r.line i := r.extend_line l i hi'
    unfold Recipe.lineOk
    rw [hline]
    exact hwf i hi'

/-! ## 5. Siblings do not gate each other -/

/-- Two lines laid over the same line never appear on each other's chain. -/
theorem Recipe.sibling_not_on_chain (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (i k m : Nat) (hi : i < r.n) (hk : k < r.n)
    (hpi : (r.line i).parent = some m) (hpk : (r.line k).parent = some m) (hne : i ≠ k) : i ∉ r.chain k := by
  intro hmem
  rw [r.chain_unfold fcl hwf k hk, hpk] at hmem
  simp only [List.mem_cons] at hmem
  cases hmem with
  | inl h => exact hne h
  | inr h =>
    have hokk := (hwf k hk).1
    rw [hpk] at hokk
    have hoki := (hwf i hi).1
    rw [hpi] at hoki
    have := r.chain_le fcl hwf m (by omega) i h
    omega

/-- So a sibling's gate answer never enters the decision: whatever line `i` answers, line `k` decides the same (dwarf galaxies are
    contained from each other; they share only what is above both). -/
theorem Recipe.sibling_never_votes (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (i k m : Nat) (hi : i < r.n) (hk : k < r.n)
    (hpi : (r.line i).parent = some m) (hpk : (r.line k).parent = some m) (hne : i ≠ k) (v : Nat → Verdict) (a : Verdict) :
    r.decide (fun j => if j = i then a else v j) k = r.decide v k := by
  unfold Recipe.decide
  have hnot := r.sibling_not_on_chain fcl hwf i k m hi hk hpi hpk hne
  congr 1
  apply List.map_congr_left
  intro j hj
  have : j ≠ i := fun h => hnot (h ▸ hj)
  simp [this]

/-! ## 6. The layout never votes -/

/-- A placement: where each item or piece of work sits (a cluster, a node, a position in a routing order). -/
structure Placement where
  place : Nat → Nat
  order : List Nat

/-- A configuration: a recipe and a placement over it. -/
structure Config where
  recipe : Recipe
  placement : Placement

/-- The configuration's decision is its recipe's. -/
def Config.decide (c : Config) (v : Nat → Verdict) (i : Nat) : Verdict := c.recipe.decide v i

/-- General covariance: a reorganisation that changes only the placement changes no decision. The placement is not an argument of the
    gate, so the proof is by reflexivity; that absence is C12's "a layer places information, never grants trust". -/
theorem Config.relayout_never_votes (c : Config) (p : Placement) (v : Nat → Verdict) (i : Nat) :
    Config.decide { c with placement := p } v i = c.decide v i := rfl

/-! ## 7. The expected field is a function of the recipe -/

/-- The depth of a line: the length of its grant chain back to the galaxy (T32: never network hops). -/
def Recipe.depth (r : Recipe) (i : Nat) : Nat := (r.chain i).length - 1

/-- The mass of a line: one plus the number of lines that derive through it. -/
def Recipe.mass (r : Recipe) (i : Nat) : Nat :=
  1 + ((List.range r.n).filter fun k => k ≠ i && Decidable.decide (i ∈ r.chain k)).length

/-- The VIBE digest of a recipe (T33): depth and mass of every line, in line order. -/
def Recipe.digest (r : Recipe) : List (Nat × Nat) := (List.range r.n).map fun i => (r.depth i, r.mass i)

/-- Two well-formed recipes with the same lines have the same digest: the expected field is determined by the layout alone, so it can
    be precomputed before anything runs (finding F2). -/
theorem Recipe.digest_congr (fcl : List FailureClass) (r r' : Recipe) (hwf : r.wf fcl) (hn : r.n = r'.n)
    (hl : ∀ i, i < r.n → r.line i = r'.line i) : r.digest = r'.digest := by
  have hchain : ∀ fuel i, i < r.n → r.chainF fuel i = r'.chainF fuel i := by
    intro fuel
    induction fuel with
    | zero => intros; rfl
    | succ f ih =>
      intro i hi
      simp only [Recipe.chainF, hl i hi]
      cases hp : (r'.line i).parent with
      | none => rfl
      | some p =>
        have hok := (hwf i hi).1
        rw [hl i hi, hp] at hok
        show i :: r.chainF f p = i :: r'.chainF f p
        rw [ih p (by omega)]
  have hc : ∀ i, i < r.n → r.chain i = r'.chain i := fun i hi => hchain i i hi
  unfold Recipe.digest Recipe.depth Recipe.mass
  rw [← hn]
  apply List.map_congr_left
  intro i hi
  have hi' : i < r.n := List.mem_range.mp hi
  rw [hc i hi']
  congr 2
  congr 1
  apply List.filter_congr
  intro k hk
  rw [hc k (List.mem_range.mp hk)]

/-! ## 8. The stack potential never rises under the system's own acts -/

/-- The stack's potential: the sum of the layers' potentials (Field.lean's `phi`, one list of held standings per layer). -/
def stackPhi : List (List Held) → Nat
  | [] => 0
  | l :: ls => phi l + stackPhi ls

/-- The system's own act on the stack: a witness refreshes standing `i` of layer `k`. -/
def stackRefresh : Nat → Nat → List (List Held) → List (List Held)
  | _, _, [] => []
  | 0, i, l :: ls => refreshAt i l :: ls
  | k + 1, i, l :: ls => l :: stackRefresh k i ls

/-- K4 across layers: a refresh anywhere in the stack never raises the stack's potential. -/
theorem k4_stack_never_raises (k i : Nat) (s : List (List Held)) : stackPhi (stackRefresh k i s) ≤ stackPhi s := by
  induction s generalizing k with
  | nil => cases k <;> simp [stackRefresh, stackPhi]
  | cons l ls ih =>
    cases k with
    | zero =>
      simp only [stackRefresh, stackPhi]
      exact Nat.add_le_add_right (k4_refresh_never_raises i l) _
    | succ k' =>
      simp only [stackRefresh, stackPhi]
      exact Nat.add_le_add_left (ih k') _

/-- The stack's potential is zero exactly when every layer's is: the stack holds what it declares only when every layer does. -/
theorem stackPhi_zero_iff : ∀ (s : List (List Held)), stackPhi s = 0 ↔ ∀ l ∈ s, phi l = 0
  | [] => by simp [stackPhi]
  | l :: ls => by
    simp only [stackPhi, List.mem_cons]
    rw [Nat.add_eq_zero_iff, stackPhi_zero_iff ls]
    constructor
    · rintro ⟨h1, h2⟩ x hx
      cases hx with
      | inl h => exact h ▸ h1
      | inr h => exact h2 x h
    · intro h
      exact ⟨h l (Or.inl rfl), fun x hx => h x (Or.inr hx)⟩

end Dmp
