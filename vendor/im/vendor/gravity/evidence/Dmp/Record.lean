/-
Axiom 3 (the record), as K3 states it: every grant, use and refusal leaves a record written at the act; no path performs an act whose
record is not written.
-/
import Dmp.Gate

namespace Dmp

/-- A request in front of the gate: the time and the evidence each witness reads. -/
structure Request where
  t : Nat
  e1 : Evidence
  e2 : Evidence

/-- One record entry: when, both verdicts, and the outcome. Counts and verdicts only, never content. -/
structure Entry where
  t : Nat
  v1 : Verdict
  v2 : Verdict
  outcome : Verdict
  deriving DecidableEq, Repr

/-- The state the gate keeps: the record, and how many acts were performed. -/
structure State where
  log : List Entry
  performed : Nat

/-- One step of the gate: both witnesses judge through the evidence guard, the stricter answer stands, and the record entry is written in the
    same step as the act (the act and its record are one transition, so no path performs the one without the other). -/
def step (j1 j2 : Evidence → Verdict) (s : State) (r : Request) : State :=
  let v1 := guard j1 r.e1 r.t
  let v2 := guard j2 r.e2 r.t
  let o := gate v1 v2
  { log := s.log ++ [⟨r.t, v1, v2, o⟩],
    performed := if o = .pass then s.performed + 1 else s.performed }

/-- K3: every request leaves exactly one record entry, refusals included. -/
theorem step_records_once (j1 j2 : Evidence → Verdict) (s : State) (r : Request) :
    (step j1 j2 s r).log.length = s.log.length + 1 := by
  simp [step]

/-- K3: the entry is written at the act, with both verdicts and the outcome. -/
theorem step_records_at_act (j1 j2 : Evidence → Verdict) (s : State) (r : Request) :
    (step j1 j2 s r).log.getLast? =
      some ⟨r.t, guard j1 r.e1 r.t, guard j2 r.e2 r.t, gate (guard j1 r.e1 r.t) (guard j2 r.e2 r.t)⟩ := by
  simp [step]

/-- K1, K2 and K3 in one step: an act is performed only with both witnesses passing on good evidence, and its record says so. -/
theorem performed_only_with_both (j1 j2 : Evidence → Verdict) (s : State) (r : Request)
    (h : (step j1 j2 s r).performed = s.performed + 1) :
    r.e1.ok r.t = true ∧ r.e2.ok r.t = true := by
  simp only [step] at h
  split at h
  · rename_i hp
    have := act_needs_both_with_evidence j1 j2 r.e1 r.e2 r.t hp
    exact ⟨this.1, this.2.1⟩
  · omega

/-- Running many steps never loses a record: the record grows by one entry per request. -/
def run (j1 j2 : Evidence → Verdict) (s : State) : List Request → State
  | [] => s
  | r :: rs => run j1 j2 (step j1 j2 s r) rs

theorem run_records_all (j1 j2 : Evidence → Verdict) (rs : List Request) :
    ∀ s : State, (run j1 j2 s rs).log.length = s.log.length + rs.length := by
  induction rs with
  | nil => intro s; simp [run]
  | cons r rs ih =>
    intro s
    simp only [run]
    rw [ih, step_records_once]
    simp
    omega

end Dmp
