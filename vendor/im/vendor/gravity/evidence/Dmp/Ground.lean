/-
G0 and T-P, as K0 states them: the dual gate's independence holds only when the two witnesses trace to two people (minted). A pressed
instance (both witnesses trace to one person) is out of spec: one influence can bend both witnesses at once (T29, shared cause), which
exposes the singularity. "Can a pressed CMOS instance ever be axiomatically sound. I say no, so Gravity abhors it" (the owner, 2026-10-26).
-/
import Dmp.Gate

namespace Dmp

/-- Who controls each witness. -/
structure Pair where
  owner1 : Nat
  owner2 : Nat
  deriving DecidableEq, Repr

/-- G0: the pair is minted when its witnesses trace to two people. -/
def Pair.minted (p : Pair) : Prop := p.owner1 ≠ p.owner2

/-- K0: an instance is out of spec when its pair is not minted. -/
def Pair.outOfSpec (p : Pair) : Prop := ¬ p.minted

/-- A principal's influence: it can set the judgment of every witness it controls. -/
def controls (p : Pair) (who : Nat) (w : Fin 2) : Prop :=
  if w.val = 0 then p.owner1 = who else p.owner2 = who

/-- On a minted pair no one controls both witnesses. -/
theorem minted_no_single_control (p : Pair) (h : p.minted) (who : Nat) :
    ¬ (controls p who 0 ∧ controls p who 1) := by
  intro ⟨h0, h1⟩
  simp [controls] at h0 h1
  exact h (h0.trans h1.symm)

/-- On a pressed pair one principal controls both witnesses. -/
theorem pressed_single_control (p : Pair) (h : p.outOfSpec) : controls p p.owner1 0 ∧ controls p p.owner1 1 := by
  unfold Pair.outOfSpec Pair.minted at h
  have h' : p.owner1 = p.owner2 := Classical.byContradiction h
  simp [controls, h']

/-- The collapse: on a pressed pair the one person behind both witnesses can make any act with good evidence pass, alone. This is why
    Gravity's verdict on a pressed instance is "out of spec", never "proven relative to the pressing". -/
theorem pressed_collapse (e1 e2 : Evidence) (t : Nat) (h1 : e1.ok t = true) (h2 : e2.ok t = true) :
    ∃ j : Evidence → Verdict, gate (guard j e1 t) (guard j e2 t) = .pass := by
  refine ⟨fun _ => .pass, ?_⟩
  simp [guard, h1, h2]
  decide

end Dmp
