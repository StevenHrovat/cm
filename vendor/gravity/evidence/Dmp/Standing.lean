/-
T24 (scoped): a part may tighten its own standing on one witness; loosening needs both. T24c: a part that drifts tightens on its own.
Standings are levels (higher is looser); the update below is the only way a level changes.
-/
import Dmp.Gate

namespace Dmp

/-- A change of standing from `old` to `new`, signed by the witnesses whose flags are true. -/
def update (old new : Nat) (w1 w2 : Bool) : Nat :=
  if new ≤ old then (if w1 || w2 then new else old)
  else (if w1 && w2 then new else old)

/-- T24: one witness alone never loosens. -/
theorem one_witness_never_loosens (old new : Nat) (w : Bool) :
    update old new w false ≤ old ∧ update old new false w ≤ old := by
  unfold update
  cases w <;> constructor <;> split <;> simp_all <;> omega

/-- T24: one witness is enough to tighten. -/
theorem one_witness_tightens (old new : Nat) (h : new ≤ old) : update old new true false = new ∧ update old new false true = new := by
  simp [update, h]

/-- T24: loosening happens only with both witnesses. -/
theorem loosening_needs_both (old new : Nat) (w1 w2 : Bool) (h : old < update old new w1 w2) : w1 = true ∧ w2 = true := by
  unfold update at h
  split at h
  · split at h <;> omega
  · split at h
    · rename_i hb; simp at hb; exact hb
    · omega

/-- T24c: self-correction is a tightening, so it needs no second witness. -/
theorem self_correction_on_one (old new : Nat) (h : new ≤ old) (w1 : Bool) (hw : w1 = true) : update old new w1 false = new := by
  subst hw; simp [update, h]

end Dmp
