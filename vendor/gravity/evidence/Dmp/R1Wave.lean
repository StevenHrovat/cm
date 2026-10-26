import Dmp.R1Bridge

/-!
# The wave clock: time that steps out from the root DoT (proposal, the owner 2026-10-26 12:29Z and 12:32Z)

The owner: "instead of a normal clock, the entire trust system steps forward in time in an outward wave propagated by the root DoT",
and "a time-dilation in layer ... for every 10 DoT steps, the next layer down steps once."

R1 found that the C2 verifier verifies the same frame twice when its clock goes back between calls (`r1bW_clock_rollback_replays`).
This file models the wave clock and proves that with it the blind model never does.

- A root step carries its number and the two witnesses' signatures. A node takes a step only when both signed it and it is higher than
  the highest step the node holds (`wvAccept`): one witness never moves time, and time never goes back, whatever arrives.
- Layer k steps once per 10^k root steps (`wvLayer`); each layer steps once per ten steps of the layer above (`wv_layer_succ`).
- A node's verifier time is its layer's step. Over any sequence of calls at such times, the same frame verifies at most once
  (`wv_verified_at_most_once`), for the model written blind from the C# (R1Model) through the bridge (R1Bridge).
- A window given in root steps and read at layer k is rounded inward (start up, end down), so dilation only tightens: a token is
  never valid at an outer layer outside its root window (`wv_dilation_only_tightens`).

Every top-level name starts with wv.
-/

namespace Dmp

/-! ## The root's steps -/

/-- A root step as it reaches a node: its number and whether each witness's signature on it verifies. -/
structure WvStep where
  n : Nat
  sig1 : Bool
  sig2 : Bool

/-- A node takes a step only when both witnesses signed it and it is ahead of what the node holds. -/
def wvAccept (held : Nat) (s : WvStep) : Nat :=
  if s.sig1 && s.sig2 && decide (held < s.n) then s.n else held

/-- Time never goes back: whatever step arrives, the node holds at least what it held. -/
theorem wv_never_back (held : Nat) (s : WvStep) : held ≤ wvAccept held s := by
  unfold wvAccept
  split
  · rename_i h
    simp at h
    omega
  · exact Nat.le_refl _

/-- One witness never moves time: a step that lacks either signature changes nothing. -/
theorem wv_one_witness_never_moves (held : Nat) (s : WvStep) (h : s.sig1 = false ∨ s.sig2 = false) :
    wvAccept held s = held := by
  unfold wvAccept
  rcases h with h | h <;> simp [h]

/-- The steps a node holds after a sequence of arrivals. -/
def wvHeld (held : Nat) : List WvStep → Nat
  | [] => held
  | s :: rest => wvHeld (wvAccept held s) rest

theorem wv_held_never_back (held : Nat) (ss : List WvStep) : held ≤ wvHeld held ss := by
  induction ss generalizing held with
  | nil => exact Nat.le_refl _
  | cons s rest ih => exact Nat.le_trans (wv_never_back held s) (ih _)

/-! ## Dilation: layer k steps once per 10^k root steps -/

/-- The step of layer `k` when the root is at step `root`. -/
def wvLayer (k root : Nat) : Nat := root / 10 ^ k

theorem wv_layer_root (root : Nat) : wvLayer 0 root = root := by
  simp [wvLayer]

/-- For every ten steps of a layer, the next layer down steps once. -/
theorem wv_layer_succ (k root : Nat) : wvLayer (k + 1) root = wvLayer k root / 10 := by
  simp [wvLayer, Nat.pow_succ, Nat.div_div_eq_div_mul]

/-- Every layer's time is monotone in the root's. -/
theorem wv_layer_monotone (k : Nat) {a b : Nat} (h : a ≤ b) : wvLayer k a ≤ wvLayer k b :=
  Nat.div_le_div_right h

/-- The same with any ratio `b` per layer: `b = 10` is the owner's dilation, `b = 1` is no dilation (every layer steps with the root). -/
def wvLayerBy (b k root : Nat) : Nat := root / b ^ k

theorem wv_layer_by_ten (k root : Nat) : wvLayerBy 10 k root = wvLayer k root := rfl

/-- Without dilation every layer reads the root's step itself. -/
theorem wv_no_dilation (k root : Nat) : wvLayerBy 1 k root = root := by
  simp [wvLayerBy]

/-- With or without dilation, a layer's time never goes back when the root's does not. -/
theorem wv_layer_by_monotone (b k : Nat) {a c : Nat} (h : a ≤ c) : wvLayerBy b k a ≤ wvLayerBy b k c :=
  Nat.div_le_div_right h

/-- A rational ratio `num / den` per layer (the owner, 12:38Z: "try with e"): layer `k` steps once per (num/den)^k root steps.
e is irrational; `2721 / 1001` is one of its continued-fraction convergents (2.7182817...), and any closer one works the same. -/
def wvLayerQ (num den k root : Nat) : Nat := root * den ^ k / num ^ k

/-- The e layering used in the comparison. -/
def wvLayerE (k root : Nat) : Nat := wvLayerQ 2721 1001 k root

/-- With any ratio, a layer's time never goes back when the root's does not. -/
theorem wv_layer_q_monotone (num den k : Nat) {a c : Nat} (h : a ≤ c) : wvLayerQ num den k a ≤ wvLayerQ num den k c :=
  Nat.div_le_div_right (Nat.mul_le_mul_right _ h)

theorem wv_layer_e_monotone (k : Nat) {a c : Nat} (h : a ≤ c) : wvLayerE k a ≤ wvLayerE k c :=
  wv_layer_q_monotone 2721 1001 k h

/-- For any layer clock `g`, a window [nb, ex) in root steps counts at that layer only when the whole local step lies inside it
(rounding inward). Whatever `g` is (ratio 10, e or 1), this only tightens. -/
def wvValidAtLayer (g : Nat → Nat) (nb ex root : Nat) : Prop := ∀ x, g x = g root → nb ≤ x ∧ x < ex

theorem wv_any_layer_only_tightens (g : Nat → Nat) (nb ex root : Nat) (h : wvValidAtLayer g nb ex root) :
    nb ≤ root ∧ root < ex :=
  h root rfl

/-- A window [nb, ex) in root steps, read at a layer whose step is `d` root steps: the start rounds up, the end rounds down. -/
def wvValidAt (d nb ex root : Nat) : Prop :=
  (nb + d - 1) / d ≤ root / d ∧ root / d < ex / d

/-- Dilation only tightens: valid at an outer layer means valid at the root. -/
theorem wv_dilation_only_tightens (d nb ex root : Nat) (hd : 0 < d) (h : wvValidAt d nb ex root) : nb ≤ root ∧ root < ex := by
  obtain ⟨h1, h2⟩ := h
  constructor
  · have hup : nb ≤ d * ((nb + d - 1) / d) := by
      have := Nat.lt_mul_div_succ (nb + d - 1) hd
      have h3 := Nat.div_add_mod (nb + d - 1) d
      have h4 := Nat.mod_lt (nb + d - 1) hd
      omega
    have hdn : d * (root / d) ≤ root := Nat.mul_div_le root d
    have := Nat.mul_le_mul_left d h1
    omega
  · apply Classical.byContradiction
    intro hn
    have hle : ex ≤ root := by omega
    have := Nat.div_le_div_right (c := d) hle
    omega

/-! ## The verifier on wave time: at most once, over any sequence of calls -/

/-- What stays true after `x` verified: its replay entry is still in the memory, or the time has passed the entry's expiry. -/
def wvGuard (e : R1mEntry) (mem : R1mMemory) (last : Int) : Prop := e ∈ mem.entries ∨ e.expiresAt < last

/-- Any call at a time not before the last keeps the guard. -/
theorem wv_guard_step (s : R1lSettings) (E : R1bEnrolment) (e : R1mEntry) (mem : R1mMemory) (last t : Int) (f : R1mFacts)
    (hg : wvGuard e mem last) (ht : last ≤ t) : wvGuard e (r1bRun s E f t mem).2 t := by
  rcases Int.lt_or_le e.expiresAt t with hlt | hge
  · exact Or.inr hlt
  · rcases hg with hin | hlt
    · left
      unfold r1bRun
      split
      · rename_i hnow
        have hcast : ((t.toNat : Nat) : Int) = t := Int.toNat_of_nonneg hnow
        apply r1b_verify_keeps
        simp only [r1mPrune, List.mem_filter, hcast]
        exact ⟨hin, by simp; omega⟩
      · exact hin
    · omega

/-- With the guard in place, the frame that left the entry never verifies again. -/
theorem wv_guard_refuses (s : R1lSettings) (E : R1bEnrolment) (x f : R1mFacts) (tok : R1mToken) (env : R1mEnvelope)
    (hx1 : x.envelope = .ok env) (hx2 : x.token = .ok tok) (mem : R1mMemory) (last t : Int)
    (hg : wvGuard { scope := tok.subjectKeyId, msgId := env.messageId, expiresAt := env.sentAt + (s.clockSkew : Int) } mem last)
    (ht : last ≤ t) (hf : r1bFrame f = r1bFrame x) : (r1bRun s E f t mem).1 ≠ .verified := by
  intro hv
  obtain ⟨_, env', tok', _, henv', htok', _, _, _, _, _, _, _, _, _, _, hsent, _, _, _, hc, _, _⟩ :=
    r1b_run_verified s E f t mem hv
  have he : f.envelope = x.envelope := by
    have h := congrArg R1mFacts.envelope hf
    exact h
  have htk : f.token = x.token := by
    have h := congrArg R1mFacts.token hf
    exact h
  rw [he, hx1] at henv'
  rw [htk, hx2] at htok'
  cases henv'
  cases htok'
  rcases hg with hin | hlt
  · have := r1b_contains_of_mem _ _ t hin (by show t ≤ env.sentAt + (s.clockSkew : Int); omega)
    simp_all
  · simp at hlt
    omega

/-- A sequence of calls, each a frame and the verifier's time, run from a memory. -/
def wvRun (s : R1lSettings) (E : R1bEnrolment) : List (R1mFacts × Int) → R1mMemory → List R1lAnswer
  | [], _ => []
  | (f, t) :: rest, mem => (r1bRun s E f t mem).1 :: wvRun s E rest (r1bRun s E f t mem).2

/-- Times that never go back, starting from `last`. -/
def wvForward : Int → List (R1mFacts × Int) → Prop
  | _, [] => True
  | last, (_, t) :: rest => last ≤ t ∧ wvForward t rest

/-- After a frame verified, no later call with the same frame verifies, in any session and with any traffic in between, as long as
the verifier's time never goes back. -/
theorem wv_never_again (s : R1lSettings) (E : R1bEnrolment) (x : R1mFacts) (tok : R1mToken) (env : R1mEnvelope)
    (hx1 : x.envelope = .ok env) (hx2 : x.token = .ok tok) :
    ∀ (rest : List (R1mFacts × Int)) (mem : R1mMemory) (last : Int),
      wvGuard { scope := tok.subjectKeyId, msgId := env.messageId, expiresAt := env.sentAt + (s.clockSkew : Int) } mem last →
      wvForward last rest →
      ∀ p ∈ (rest.zip (wvRun s E rest mem)), r1bFrame p.1.1 = r1bFrame x → p.2 ≠ .verified := by
  intro rest
  induction rest with
  | nil => intro _ _ _ _ p hp; simp at hp
  | cons c rest ih =>
    intro mem last hg hfw p hp hf
    obtain ⟨f, t⟩ := c
    obtain ⟨ht, hrest⟩ := hfw
    simp only [wvRun, List.zip_cons_cons, List.mem_cons] at hp
    rcases hp with hp | hp
    · subst hp
      exact wv_guard_refuses s E x f tok env hx1 hx2 mem last t hg ht hf
    · exact ih _ t (wv_guard_step s E _ mem last t f hg ht) hrest p hp hf

/-- The strong form of C2.L3 on wave time: if a frame verifies at time t0, then over any later sequence of calls whose times never go
back, it never verifies again. Wave time never goes back (`wv_held_never_back`, `wv_layer_monotone`), whatever the local clock does,
because the local clock is not read. -/
theorem wv_verified_at_most_once (s : R1lSettings) (E : R1bEnrolment) (x : R1mFacts) (t0 : Int) (mem : R1mMemory)
    (hv : (r1bRun s E x t0 mem).1 = .verified) (rest : List (R1mFacts × Int)) (hfw : wvForward t0 rest) :
    ∀ p ∈ (rest.zip (wvRun s E rest (r1bRun s E x t0 mem).2)), r1bFrame p.1.1 = r1bFrame x → p.2 ≠ .verified := by
  obtain ⟨_, env, tok, _, henv, htok, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, hmem⟩ := r1b_run_verified s E x t0 mem hv
  apply wv_never_again s E x tok env henv htok rest _ t0 _ hfw
  left
  rw [hmem]
  simp

/-- Wave time for a node at layer `k`: the layer step of what it holds after the arrivals so far. Consecutive calls read
non-decreasing times. -/
theorem wv_node_time_forward (k held : Nat) (ss ss' : List WvStep) :
    wvLayer k (wvHeld held ss) ≤ wvLayer k (wvHeld (wvHeld held ss) ss') :=
  wv_layer_monotone k (wv_held_never_back _ _)

end Dmp
