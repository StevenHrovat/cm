/-
The VIBE field (T32) and Gravity's constant (Gravity's constant, in the design notes of this repository): N1 the laws do not see the parameters, N1b
the schedule never votes, N1c the demands never change the laws (and only both witnesses lower them, T24), K4 the system's own acts never raise the potential, N2 the potential is
bounded. Parameters are thousandths (the owner: "we only have to adjust very small decimal values"), so 0.33 is 330.
-/
import Dmp.Gate

namespace Dmp

/-- The tunable parameters, in thousandths. -/
structure Params where
  rho : Nat      -- refresh threshold
  lambda : Nat   -- window growth per depth
  kappa : Nat    -- evidence gradient
  mu : Nat       -- stability margin
  theta : Nat    -- temperature alarm
  eMin : Nat     -- minimum evidence level
  deriving DecidableEq, Repr

/-- The evidence level a part at depth `d` of a boundary of depth `D` must give: nearer the pair, more evidence. -/
def demand (π : Params) (D d : Nat) : Nat := π.eMin + (π.kappa * (D - d) + 999) / 1000

/-- The gate with the field's demand in front: a part short of the demanded evidence level is refused; otherwise K1 decides. -/
def gateP (π : Params) (D d level : Nat) (v1 v2 : Verdict) : Verdict :=
  if demand π D d ≤ level then gate v1 v2 else .refuse

/-- N1: under every parameter vector an act that passes has both witnesses passing (the laws do not see the parameters). -/
theorem n1_laws_hold (π : Params) (D d level : Nat) (v1 v2 : Verdict) (h : gateP π D d level v1 v2 = .pass) :
    v1 = .pass ∧ v2 = .pass := by
  unfold gateP at h
  split at h
  · exact (gate_pass_iff v1 v2).mp h
  · cases h

/-- N1b: the schedule's parameters (refresh threshold, stability margin, temperature alarm) never enter the decision. -/
theorem n1b_schedule_never_votes (π : Params) (rho mu theta : Nat) (D d level : Nat) (v1 v2 : Verdict) :
    gateP { π with rho := rho, mu := mu, theta := theta } D d level v1 v2 = gateP π D d level v1 v2 := by
  rfl

/-- The demand only grows with the evidence gradient. -/
theorem demand_mono (π π' : Params) (D d : Nat) (hk : π.kappa ≤ π'.kappa) (he : π.eMin ≤ π'.eMin) :
    demand π D d ≤ demand π' D d := by
  unfold demand
  have hm : π.kappa * (D - d) ≤ π'.kappa * (D - d) := Nat.mul_le_mul_right _ hk
  have hd : (π.kappa * (D - d) + 999) / 1000 ≤ (π'.kappa * (D - d) + 999) / 1000 :=
    Nat.div_le_div_right (by omega)
  omega

/-- N1c: a stricter demand passes no act the looser one refuses. -/
theorem n1c_stricter_demand_passes_less (π π' : Params) (D d level : Nat) (v1 v2 : Verdict)
    (hk : π.kappa ≤ π'.kappa) (he : π.eMin ≤ π'.eMin) (h : gateP π' D d level v1 v2 = .pass) :
    gateP π D d level v1 v2 = .pass := by
  unfold gateP at h ⊢
  have hm := demand_mono π π' D d hk he
  split at h
  · rename_i hle
    have : demand π D d ≤ level := Nat.le_trans hm hle
    simp [this, h]
  · cases h

/-! ### Who may move a parameter (T24, the "Tighter when" column of Gravity's constant) -/

/-- A settings write of a parameter whose higher value is tighter (κ, e_min, ρ, μ): one witness may raise it, lowering needs both.
Parameters whose lower value is tighter (λ, θ, w₀, lifetimes) are written with `update` (Standing.lean), whose levels are higher-is-looser. -/
def writeUp (old new : Nat) (w1 w2 : Bool) : Nat :=
  if old ≤ new then (if w1 || w2 then new else old)
  else (if w1 && w2 then new else old)

/-- T24 on a parameter: one witness alone never lowers a higher-is-tighter parameter, whichever witness it is. -/
theorem one_witness_never_lowers (old new : Nat) (w : Bool) :
    old ≤ writeUp old new w false ∧ old ≤ writeUp old new false w := by
  unfold writeUp
  cases w <;> constructor <;> split <;> simp_all <;> omega

/-- T24 on a parameter: a higher-is-tighter parameter goes down only with both witnesses. -/
theorem lowering_needs_both (old new : Nat) (w1 w2 : Bool) (h : writeUp old new w1 w2 < old) : w1 = true ∧ w2 = true := by
  unfold writeUp at h
  split at h
  · split at h <;> omega
  · split at h
    · rename_i hb; simp at hb; exact hb
    · omega

/-- N1c with T24: after one witness writes κ and e_min, the gate passes no act it refused before. Lowering the demand takes both. -/
theorem one_witness_never_weakens_demand (π : Params) (k e : Nat) (w : Bool) (D d level : Nat) (v1 v2 : Verdict)
    (h : gateP { π with kappa := writeUp π.kappa k w false, eMin := writeUp π.eMin e w false } D d level v1 v2 = .pass) :
    gateP π D d level v1 v2 = .pass :=
  n1c_stricter_demand_passes_less π _ D d level v1 v2 (one_witness_never_lowers π.kappa k w).1
    (one_witness_never_lowers π.eMin e w).1 h

/-! ### The potential -/

/-- A held standing as the field reads it: the mass of its part and its freshness in thousandths (at most 1000). -/
structure Held where
  mass : Nat
  fresh : Nat
  deriving DecidableEq, Repr

/-- The potential: the sum of mass times shortfall (1000 minus freshness). -/
def phi : List Held → Nat
  | [] => 0
  | h :: hs => h.mass * (1000 - h.fresh) + phi hs

/-- The system's own act on the field: a witness verifies the standing at position `i` live, which makes it fully fresh. -/
def refreshAt : Nat → List Held → List Held
  | _, [] => []
  | 0, h :: hs => { h with fresh := 1000 } :: hs
  | n + 1, h :: hs => h :: refreshAt n hs

/-- K4: the system's own acts never raise the potential. -/
theorem k4_refresh_never_raises (i : Nat) (l : List Held) : phi (refreshAt i l) ≤ phi l := by
  induction l generalizing i with
  | nil => cases i <;> simp [refreshAt, phi]
  | cons h hs ih =>
    cases i with
    | zero => simp [refreshAt, phi]
    | succ n =>
      simp only [refreshAt, phi]
      exact Nat.add_le_add_left (ih n) _

/-- The potential's ceiling: every standing at zero freshness. -/
def cap : List Held → Nat
  | [] => 0
  | h :: hs => h.mass * 1000 + cap hs

/-- N2: the potential is bounded by its ceiling, whatever the parameters and the ageing. -/
theorem n2_bounded (l : List Held) : phi l ≤ cap l := by
  induction l with
  | nil => simp [phi, cap]
  | cons h hs ih =>
    simp only [phi, cap]
    exact Nat.add_le_add (Nat.mul_le_mul_left h.mass (Nat.sub_le 1000 h.fresh)) ih

/-- The potential is zero exactly when every standing is fully fresh or carries no mass. -/
theorem phi_zero_of_all_fresh (l : List Held) (h : ∀ x ∈ l, x.fresh = 1000) : phi l = 0 := by
  induction l with
  | nil => rfl
  | cons x xs ih =>
    simp only [phi]
    have hx := h x (by simp)
    have hxs := ih (fun y hy => h y (by simp [hy]))
    simp [hx, hxs]

end Dmp
