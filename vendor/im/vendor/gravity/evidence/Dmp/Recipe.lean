import Dmp.Layer

/-!
# The recipe: every layout CMOS can take, and nothing else

The owner, 2026-10-26 15:25Z: DMP "can only be proven through an absolutely solid unified theory of Gravity across all the possible
layers it can create as a hyperdimensional information layout recipe". And T35: compliance belongs to an assembly, "a recipe for how
to assemble them to form a self balancing CMOS galaxy".

A recipe is a numbered list of lines. Each line adds one layer and names the line it is laid over, which is always an earlier line.
The kinds are DMP's: a **galaxy** (a root Duality, C17, "Who maintains it"), an **information** layer overlaid on the field (C12, with its
own Duality, T38), a privacy **singularity** (C12: a cluster of at least three nodes), a **dwarf** galaxy minted under another's roots
("Who maintains it"), and a thin **surface** (T25). The type is closed: a layout that is not a term of it does not exist, so a theorem
proved for every term is proved for every layout (the possibility space is the recipe, and only the recipe).

This file holds the recipe and its structure only (the lines, the chain of a line up to its root, well-formedness, the stack's gate as
the meet along the chain, Gravity's three verdicts). What survives every line is Theory.lean.

**Prior art.** A recipe is a parent array (each line names an earlier line): the standard encoding of a rooted forest in which structural
induction is strong induction on the line number. The stack's gate is the meet in the verdict lattice `refuse < hold < pass` (Denning's
lattice of security classes, 1976: a flow is allowed only where every class on the path allows it), so "sub-layers only tighten" is the
statement that a meet never exceeds any of its arguments.
-/

namespace Dmp

/-- The kinds of line a recipe has. -/
inductive Kind where
  | galaxy
  | information
  | singularity
  | dwarf
  | surface
  deriving DecidableEq, Repr

/-- One line of a recipe: a layer of a kind, its Duality, the line it is laid over (none only for a galaxy), and for a singularity the
    number of nodes in its cluster. -/
structure Line where
  kind : Kind
  dot : DoT
  parent : Option Nat
  cluster : Nat

/-- A recipe: `n` lines, numbered from 0. Lines at or beyond `n` are not part of it. -/
structure Recipe where
  n : Nat
  line : Nat → Line

/-- A line is well formed when it refers only to an earlier line, a galaxy has no parent and nothing else lacks one, a singularity's
    cluster meets the floor (T3a: three nodes), and T38 holds: a Duality that is not minted is laid over a line that gates it. -/
def Recipe.lineOk (fcl : List FailureClass) (r : Recipe) (i : Nat) : Prop :=
  (match (r.line i).parent with
   | none => (r.line i).kind = .galaxy
   | some j => j < i ∧ (r.line i).kind ≠ .galaxy) ∧
  ((r.line i).kind = .singularity → 3 ≤ (r.line i).cluster) ∧
  ((r.line i).dot.minted fcl ∨ (r.line i).parent ≠ none)

/-- A recipe is well formed when every one of its lines is. -/
def Recipe.wf (fcl : List FailureClass) (r : Recipe) : Prop := ∀ i, i < r.n → r.lineOk fcl i

/-- The same, decided. -/
def Recipe.lineOkB (fcl : List FailureClass) (r : Recipe) (i : Nat) : Bool :=
  (match (r.line i).parent with
   | none => decide ((r.line i).kind = .galaxy)
   | some j => decide (j < i) && decide ((r.line i).kind ≠ .galaxy)) &&
  (!decide ((r.line i).kind = .singularity) || decide (3 ≤ (r.line i).cluster)) &&
  ((r.line i).dot.mintedB fcl || decide ((r.line i).parent ≠ none))

theorem Recipe.lineOkB_iff (fcl : List FailureClass) (r : Recipe) (i : Nat) : r.lineOkB fcl i = true ↔ r.lineOk fcl i := by
  unfold lineOkB lineOk
  rw [Bool.and_eq_true, Bool.and_eq_true, Bool.or_eq_true, Bool.or_eq_true, DoT.mintedB_iff]
  cases h : (r.line i).parent <;> simp [Decidable.imp_iff_not_or, and_assoc]

def Recipe.wfB (fcl : List FailureClass) (r : Recipe) : Bool := (List.range r.n).all fun i => r.lineOkB fcl i

theorem Recipe.wfB_iff (fcl : List FailureClass) (r : Recipe) : r.wfB fcl = true ↔ r.wf fcl := by
  unfold wfB wf
  simp only [List.all_eq_true, List.mem_range, lineOkB_iff]

/-! ## The chain of a line -/

/-- The chain of line `i`: `i`, its parent, the parent's parent, up to a line with no parent. `fuel` bounds the walk; `chain` gives
    it `i` steps, which is enough whenever parents are earlier lines. -/
def Recipe.chainF (r : Recipe) : Nat → Nat → List Nat
  | 0, i => [i]
  | fuel + 1, i =>
    i :: (match (r.line i).parent with
          | none => []
          | some j => r.chainF fuel j)

def Recipe.chain (r : Recipe) (i : Nat) : List Nat := r.chainF i i

theorem Recipe.chainF_head (r : Recipe) (fuel i : Nat) : i ∈ r.chainF fuel i := by
  cases fuel <;> simp [chainF]

theorem Recipe.chain_head (r : Recipe) (i : Nat) : i ∈ r.chain i := r.chainF_head i i

/-- Every line on the chain is at or before the line it starts from. -/
theorem Recipe.chainF_le (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) :
    ∀ fuel i, i < r.n → ∀ j ∈ r.chainF fuel i, j ≤ i := by
  intro fuel
  induction fuel with
  | zero => intro i _ j hj; simp [chainF] at hj; omega
  | succ f ih =>
    intro i hi j hj
    simp only [chainF, List.mem_cons] at hj
    cases hj with
    | inl h => omega
    | inr h =>
      cases hp : (r.line i).parent with
      | none => simp [hp] at h
      | some p =>
        simp only [hp] at h
        have hok := (hwf i hi).1
        rw [hp] at hok
        have hpi : p < i := hok.1
        have := ih p (by omega) j h
        omega

theorem Recipe.chain_le (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (i : Nat) (hi : i < r.n) :
    ∀ j ∈ r.chain i, j ≤ i :=
  r.chainF_le fcl hwf i i hi

/-- Every line on the chain is a line of the recipe. -/
theorem Recipe.chain_lt (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (i : Nat) (hi : i < r.n) :
    ∀ j ∈ r.chain i, j < r.n :=
  fun j hj => Nat.lt_of_le_of_lt (r.chain_le fcl hwf i hi j hj) hi

/-- With enough fuel the walk is the same: parents are earlier lines, so from line `i` at most `i` steps reach a root. -/
theorem Recipe.chainF_eq_of_le (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) :
    ∀ i, i < r.n → ∀ f g, i ≤ f → i ≤ g → r.chainF f i = r.chainF g i := by
  intro i
  induction i using Nat.strongRecOn with
  | _ i ih =>
    intro hi f g hf hg
    have hok := (hwf i hi).1
    cases hp : (r.line i).parent with
    | none =>
      cases f <;> cases g <;> simp [chainF, hp]
    | some p =>
      rw [hp] at hok
      have hpi : p < i := hok.1
      cases f with
      | zero => omega
      | succ f' =>
        cases g with
        | zero => omega
        | succ g' =>
          simp only [chainF, hp]
          rw [ih p hpi (by omega) f' g' (by omega) (by omega)]

theorem Recipe.chainF_eq_chain (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (i : Nat) (hi : i < r.n) (fuel : Nat)
    (hf : i ≤ fuel) : r.chainF fuel i = r.chain i :=
  r.chainF_eq_of_le fcl hwf i hi fuel i hf (Nat.le_refl i)

/-- The chain unfolds one step: a line, then the chain of its parent. -/
theorem Recipe.chain_unfold (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (i : Nat) (hi : i < r.n) :
    r.chain i = i :: (match (r.line i).parent with
                      | none => []
                      | some j => r.chain j) := by
  have hok := (hwf i hi).1
  cases hp : (r.line i).parent with
  | none =>
    unfold chain
    cases i <;> simp [chainF, hp]
  | some p =>
    rw [hp] at hok
    have hpi : p < i := hok.1
    unfold chain
    cases i with
    | zero => omega
    | succ k =>
      simp only [chainF, hp]
      rw [r.chainF_eq_of_le fcl hwf p (by omega) k p (by omega) (Nat.le_refl p)]

/-! ## The stack's gate -/

/-- The meet of verdicts along a chain: the stricter answer at every level. The empty meet is `pass` (no level, nothing refused). -/
def meet : List Verdict → Verdict
  | [] => .pass
  | v :: vs => Verdict.stricter v (meet vs)

/-- The stack's decision on an act at line `i`: the meet of every layer's own gate answer along the chain of `i`. `v j` is what line
    `j`'s Duality answered (its `DoT.gate`). -/
def Recipe.decide (r : Recipe) (v : Nat → Verdict) (i : Nat) : Verdict := meet ((r.chain i).map v)

/-! ## Gravity's verdict on a recipe -/

/-- What Gravity says about a configuration: proven, unproven (a layer the two chains do not both reach or do not agree on), or out
    of spec (a layout the recipe refuses: a pressed top, a two-node singularity, a Duality nobody gates). -/
inductive Verdict3 where
  | proven
  | unproven
  | outOfSpec
  deriving DecidableEq, Repr

/-- The verdict: well formed and both chains agree on every line is proven; well formed with a line the chains do not both carry is
    unproven; anything else is out of spec. `agree i` is the lemma ledger's answer for line `i` (C22). -/
def Recipe.verdict (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool) : Verdict3 :=
  if r.wfB fcl then
    (if (List.range r.n).all agree then .proven else .unproven)
  else .outOfSpec

end Dmp
