/-
Axiom 1 (the dual gate) and Axiom 2 (evidence), as Gravity's laws K1 and K2 state them (Gravity's constant, in the design notes of this repository).

DMP, Axiom 1: an act needs both witnesses; where they disagree, the stricter answer stands.
DMP, Axiom 2: every check reads its evidence itself, live, at the step where the act happens, inside the evidence's validity window.
-/
namespace Dmp

/-- A witness's answer on an act. The order is `refuse < hold < pass`. -/
inductive Verdict where
  | refuse
  | hold
  | pass
  deriving DecidableEq, Repr

def Verdict.rank : Verdict → Nat
  | .refuse => 0
  | .hold => 1
  | .pass => 2

/-- The stricter of two verdicts. -/
def Verdict.stricter (a b : Verdict) : Verdict :=
  if a.rank ≤ b.rank then a else b

/-- K1: the gate's decision is the stricter of the two witnesses' verdicts. -/
def gate (v1 v2 : Verdict) : Verdict := Verdict.stricter v1 v2

/-- K1: an act passes exactly when both witnesses pass it. -/
theorem gate_pass_iff (v1 v2 : Verdict) : gate v1 v2 = .pass ↔ v1 = .pass ∧ v2 = .pass := by
  cases v1 <;> cases v2 <;> decide

/-- K1: one witness alone never acts, whichever of the two it is. -/
theorem one_witness_never_acts (v : Verdict) (h : v ≠ .pass) : gate v .pass ≠ .pass ∧ gate .pass v ≠ .pass := by
  cases v <;> simp_all <;> decide

/-- K1: the stricter answer stands: the decision is never looser than either witness. -/
theorem gate_stricter (v1 v2 : Verdict) : (gate v1 v2).rank ≤ v1.rank ∧ (gate v1 v2).rank ≤ v2.rank := by
  cases v1 <;> cases v2 <;> decide

/-- The order of the witnesses does not matter. -/
theorem gate_comm (v1 v2 : Verdict) : gate v1 v2 = gate v2 v1 := by
  cases v1 <;> cases v2 <;> decide

/-- What a check reads: when it was read, its validity window, and whether it came from the live state (not a cache, a flag set upstream,
    or a caller's claim). Times are milliseconds on one injected clock. -/
structure Evidence where
  readAt : Nat
  validFrom : Nat
  validUntil : Nat
  live : Bool
  deriving DecidableEq, Repr

/-- K2: evidence counts at `t` only when it is live, read at `t`, and inside its window. -/
def Evidence.ok (e : Evidence) (t : Nat) : Bool :=
  e.live && e.readAt == t && Nat.ble e.validFrom t && Nat.blt t e.validUntil

/-- A witness's judgment, whatever it is, only reaches the gate through the evidence guard. -/
def guard (judge : Evidence → Verdict) (e : Evidence) (t : Nat) : Verdict :=
  if e.ok t then judge e else .refuse

/-- K2: a witness passes only on evidence that is live, read at the step, and in its window, whatever its judgment. -/
theorem guard_pass_needs_evidence (judge : Evidence → Verdict) (e : Evidence) (t : Nat)
    (h : guard judge e t = .pass) : e.ok t = true := by
  unfold guard at h
  cases hok : e.ok t
  · simp [hok] at h
  · rfl

/-- K2: stale evidence (read at another step) never passes. -/
theorem stale_never_passes (judge : Evidence → Verdict) (e : Evidence) (t : Nat) (h : e.readAt ≠ t) :
    guard judge e t ≠ .pass := by
  intro hp
  have hok := guard_pass_needs_evidence judge e t hp
  simp [Evidence.ok] at hok
  exact h hok.1.1.2

/-- K1 and K2 together: an act passes only when both witnesses had good evidence and both passed. -/
theorem act_needs_both_with_evidence (j1 j2 : Evidence → Verdict) (e1 e2 : Evidence) (t : Nat)
    (h : gate (guard j1 e1 t) (guard j2 e2 t) = .pass) :
    e1.ok t = true ∧ e2.ok t = true ∧ guard j1 e1 t = .pass ∧ guard j2 e2 t = .pass := by
  have hb := (gate_pass_iff _ _).mp h
  exact ⟨guard_pass_needs_evidence j1 e1 t hb.1, guard_pass_needs_evidence j2 e2 t hb.2, hb.1, hb.2⟩

end Dmp
