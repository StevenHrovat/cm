import Dmp.R1Wave

/-!
# Lensing: the core reorganises itself, and replay stays closed

The owner, 2026-10-26 12:47Z: "the layout should follow from the bending of the trustspace that is projected by the core DoT. Every
reorganization of the network has to be signed by the DoT and then propagated outward from it until it reaches all nodes that are
affected, but not any further."

What this file proves, on top of the wave clock (`R1Wave.lean`):

1. A reorganisation moves a node only when both witnesses signed it and its epoch is ahead of the node's; epochs never go back.
2. Propagation to exactly the affected nodes is enough, and missing any affected node is not.
3. The per-layer profile of the layout (how many nodes sit in each layer, so the shape of the VIBE field) does not depend on the
   trust order at all: any reorganisation keeps it.
4. Replay with a moving layout. A layer move changes a node's window. The toy run (`/mnt/project-files/cm/wave/sim2.py`) found
   that forgetting by "high-water time minus the current window" lets frames through twice once a window widens (22,500 replays);
   a rising floor (forget only below it, refuse every stamp below it) lets none through. Here: the floor rule verifies a frame at
   most once over any sequence of calls with any times and any windows; the high-water rule has a concrete double accept.
5. Stamps and floors count root steps, never layer steps: an outward move makes a node's layer reading go back.
-/

namespace Dmp

/-! ## Signed reorganisations -/

/-- A reorganisation as it reaches a node: its epoch, whether each witness's signature verifies, and the node's new layer. -/
structure LnMove where
  epoch : Nat
  sig1 : Bool
  sig2 : Bool
  layer : Nat

/-- What a node holds of the layout: the epoch of the last reorganisation it took, and its layer. -/
structure LnNode where
  epoch : Nat
  layer : Nat

/-- A node takes a reorganisation only when both witnesses signed it and it is ahead of the node's epoch. -/
def lnAccept (n : LnNode) (m : LnMove) : LnNode :=
  if m.sig1 = true ∧ m.sig2 = true ∧ n.epoch < m.epoch then { epoch := m.epoch, layer := m.layer } else n

theorem ln_epoch_never_back (n : LnNode) (m : LnMove) : n.epoch ≤ (lnAccept n m).epoch := by
  unfold lnAccept
  by_cases h : m.sig1 = true ∧ m.sig2 = true ∧ n.epoch < m.epoch
  · rw [if_pos h]
    exact Nat.le_of_lt h.2.2
  · rw [if_neg h]
    exact Nat.le_refl _

/-- One witness never moves a node. -/
theorem ln_one_witness_never_moves (n : LnNode) (m : LnMove) (h : m.sig1 = false ∨ m.sig2 = false) : lnAccept n m = n := by
  unfold lnAccept
  have hn : ¬ (m.sig1 = true ∧ m.sig2 = true ∧ n.epoch < m.epoch) := by
    intro hc
    cases h with
    | inl h1 => rw [h1] at hc; exact Bool.noConfusion hc.1
    | inr h2 => rw [h2] at hc; exact Bool.noConfusion hc.2.1
  rw [if_neg hn]

/-- An old or replayed reorganisation never moves a node, signed or not. -/
theorem ln_stale_never_moves (n : LnNode) (m : LnMove) (h : m.epoch ≤ n.epoch) : lnAccept n m = n := by
  unfold lnAccept
  have hn : ¬ (m.sig1 = true ∧ m.sig2 = true ∧ n.epoch < m.epoch) := fun hc => Nat.lt_irrefl _ (Nat.lt_of_lt_of_le hc.2.2 h)
  rw [if_neg hn]

/-- A node that has taken a reorganisation holds exactly the layer it carried. -/
theorem ln_signed_moves (n : LnNode) (m : LnMove) (h1 : m.sig1 = true) (h2 : m.sig2 = true) (he : n.epoch < m.epoch) :
    lnAccept n m = { epoch := m.epoch, layer := m.layer } := by
  unfold lnAccept
  rw [if_pos ⟨h1, h2, he⟩]

/-! ## Propagation reaches the affected nodes and no further -/

/-- A node is affected by a reorganisation from layout `old` to layout `new` when its layer changes. -/
def lnAffected {N : Type} (old new : N → Nat) (n : N) : Prop := old n ≠ new n

/-- Delivering the new layer to exactly the affected nodes gives every node its new layer: nothing beyond them needs it. -/
theorem ln_reach_affected_enough {N : Type} (old new : N → Nat) (n : N) [Decidable (lnAffected old new n)] :
    (if lnAffected old new n then new n else old n) = new n := by
  by_cases h : lnAffected old new n
  · rw [if_pos h]
  · rw [if_neg h]
    exact Decidable.of_not_not h

/-- And missing an affected node leaves that node on its old layer, which is wrong. -/
theorem ln_reach_affected_needed {N : Type} (old new : N → Nat) (n : N) (h : lnAffected old new n) : old n ≠ new n := h

/-! ## The VIBE profile does not depend on the trust order -/

/-- The layout: the nodes in trust order fill fixed slot counts per layer, innermost first. -/
def lnSlots {α : Type} : List Nat → List α → List (List α)
  | [], _ => []
  | k :: ks, xs => xs.take k :: lnSlots ks (xs.drop k)

/-- When there are enough nodes, every layer holds exactly its slot count, whatever the order. -/
theorem ln_profile {α : Type} (ks : List Nat) (xs : List α) (h : ks.sum ≤ xs.length) : (lnSlots ks xs).map List.length = ks := by
  induction ks generalizing xs with
  | nil => rfl
  | cons k ks ih =>
    simp only [List.sum_cons] at h
    simp only [lnSlots, List.map_cons, List.length_take]
    have hk : min k xs.length = k := Nat.min_eq_left (by omega)
    rw [hk, ih (xs.drop k) (by rw [List.length_drop]; omega)]

/-- Two trust orders, so any reorganisation, give the same profile. -/
theorem ln_profile_any_order {α : Type} (ks : List Nat) (xs ys : List α) (hx : ks.sum ≤ xs.length) (hy : ks.sum ≤ ys.length) :
    (lnSlots ks xs).map List.length = (lnSlots ks ys).map List.length := by
  rw [ln_profile ks xs hx, ln_profile ks ys hy]

/-! ## Replay with a moving layout: the rising floor -/

/-- A frame as the replay rule sees it: its message id and its stamp in root steps. -/
structure LnFrame where
  id : Nat
  stamp : Int
deriving DecidableEq

/-- The replay memory: a floor that only rises, and the frames seen at or above it. -/
structure LnMem where
  floor : Int
  seen : List LnFrame

/-- One call at time `t` with the window `w` the node has in its current layer. The floor rises to `t - w` if that is higher;
memory forgets only below the floor; a stamp below the floor is refused; a frame already seen is refused. -/
def lnCheck (m : LnMem) (f : LnFrame) (t w : Int) : Bool × LnMem :=
  let fl := max m.floor (t - w)
  let kept := m.seen.filter (fun e => decide (fl ≤ e.stamp))
  if f.stamp < fl then (false, { floor := fl, seen := kept })
  else if f ∈ kept then (false, { floor := fl, seen := kept })
  else (true, { floor := fl, seen := f :: kept })

/-- What stays true after `f` verified: it is still remembered, or its stamp is below the floor. -/
def lnGuard (f : LnFrame) (m : LnMem) : Prop := f ∈ m.seen ∨ f.stamp < m.floor

theorem ln_floor_rises (m : LnMem) (g : LnFrame) (t w : Int) : m.floor ≤ (lnCheck m g t w).2.floor := by
  unfold lnCheck
  simp only
  split
  · exact Int.le_max_left _ _
  · split
    · exact Int.le_max_left _ _
    · exact Int.le_max_left _ _

theorem ln_guard_kept (f : LnFrame) (m : LnMem) (fl : Int) (hle : m.floor ≤ fl) (h : lnGuard f m) :
    f ∈ m.seen.filter (fun e => decide (fl ≤ e.stamp)) ∨ f.stamp < fl := by
  cases h with
  | inl hm =>
    by_cases hs : fl ≤ f.stamp
    · exact Or.inl (List.mem_filter.mpr ⟨hm, decide_eq_true hs⟩)
    · exact Or.inr (Int.lt_of_not_ge hs)
  | inr hl => exact Or.inr (Int.lt_of_lt_of_le hl hle)

/-- Any call, with any frame, any time and any window, keeps the guard. -/
theorem ln_guard_step (f g : LnFrame) (m : LnMem) (t w : Int) (h : lnGuard f m) : lnGuard f (lnCheck m g t w).2 := by
  have hk := ln_guard_kept f m (max m.floor (t - w)) (Int.le_max_left _ _) h
  unfold lnCheck lnGuard
  simp only
  split
  · exact hk
  · split
    · exact hk
    · cases hk with
      | inl hm => exact Or.inl (List.mem_cons_of_mem _ hm)
      | inr hl => exact Or.inr hl

/-- With the guard in place, the frame is refused, at any time and with any window. -/
theorem ln_guard_refuses (f : LnFrame) (m : LnMem) (t w : Int) (h : lnGuard f m) : (lnCheck m f t w).1 = false := by
  have hk := ln_guard_kept f m (max m.floor (t - w)) (Int.le_max_left _ _) h
  unfold lnCheck
  simp only
  split
  · rfl
  · rename_i hs
    cases hk with
    | inl hm => rw [if_pos hm]
    | inr hl => exact absurd hl hs

/-- A frame that verified leaves the guard behind. -/
theorem ln_accept_guards (f : LnFrame) (m : LnMem) (t w : Int) (h : (lnCheck m f t w).1 = true) : lnGuard f (lnCheck m f t w).2 := by
  unfold lnCheck at h ⊢
  simp only at h ⊢
  split
  · rename_i hs
    rw [if_pos hs] at h
    exact Bool.noConfusion h
  · rename_i hs
    split
    · rename_i hm
      rw [if_neg hs, if_pos hm] at h
      exact Bool.noConfusion h
    · exact Or.inl (List.mem_cons_self)

/-- A sequence of calls, each a frame, a time and the window of the node's layer at that moment, with what each call answered. -/
def lnRun : LnMem → List (LnFrame × Int × Int) → List (LnFrame × Bool)
  | _, [] => []
  | m, (g, t, w) :: cs => (g, (lnCheck m g t w).1) :: lnRun (lnCheck m g t w).2 cs

theorem ln_never_again (f : LnFrame) (m : LnMem) (cs : List (LnFrame × Int × Int)) (h : lnGuard f m) :
    ∀ p ∈ lnRun m cs, p.1 = f → p.2 = false := by
  induction cs generalizing m with
  | nil => intro p hp; exact absurd hp List.not_mem_nil
  | cons c cs ih =>
    obtain ⟨g, t, w⟩ := c
    intro p hp hpf
    cases hp with
    | head =>
      simp only at hpf
      rw [hpf]
      exact ln_guard_refuses f m t w h
    | tail _ hp' => exact ih _ (ln_guard_step f g m t w h) p hp' hpf

/-- At most once, with a moving layout: once a frame verified, no later call verifies it again, whatever the times (forward,
backward or equal) and whatever windows the node's layer moves give it. -/
theorem ln_verified_at_most_once (f : LnFrame) (m : LnMem) (t w : Int) (cs : List (LnFrame × Int × Int))
    (h : (lnCheck m f t w).1 = true) : ∀ p ∈ lnRun (lnCheck m f t w).2 cs, p.1 = f → p.2 = false :=
  ln_never_again f _ cs (ln_accept_guards f m t w h)

/-! ## Why the floor: the high-water rule replays once a window widens -/

/-- The rule the toy run rejected: forget by the high-water time minus the current window, with no floor that remembers the past. -/
def lnCheckHw (hw : Int) (seen : List LnFrame) (f : LnFrame) (t w : Int) : Bool × (Int × List LnFrame) :=
  let h := max hw t
  let kept := seen.filter (fun e => decide (h - w ≤ e.stamp))
  if f.stamp < h - w then (false, (h, kept))
  else if f ∈ kept then (false, (h, kept))
  else (true, (h, f :: kept))

def lnRunHw : Int → List LnFrame → List (LnFrame × Int × Int) → List Bool
  | _, _, [] => []
  | hw, seen, (g, t, w) :: cs => (lnCheckHw hw seen g t w).1 :: lnRunHw (lnCheckHw hw seen g t w).2.1 (lnCheckHw hw seen g t w).2.2 cs

/-- Frame `a` at time 10 with window 10, frame `b` at time 20 with window 10 (memory forgets `a`), then the node moves and its
window widens to 20: `a` again. -/
def lnWidening : List (LnFrame × Int × Int) := [(⟨1, 5⟩, 10, 10), (⟨2, 15⟩, 20, 10), (⟨1, 5⟩, 20, 20)]

/-- The high-water rule accepts frame `a` twice. -/
theorem ln_high_water_replays : lnRunHw 0 [] lnWidening = [true, true, true] := by decide

/-- The floor rule refuses the second `a`. -/
theorem ln_floor_refuses_widening : (lnRun ⟨0, []⟩ lnWidening).map Prod.snd = [true, true, false] := by decide

/-! ## Stamps count root steps, not layer steps -/

/-- An outward move from layer 1 to layer 2 makes the node's layer reading go back (15 to 1 at root step 150), so a floor or a stamp
kept in layer steps would go back with it. In root steps nothing moves. -/
theorem ln_outward_layer_reading_goes_back : wvLayerBy 10 2 150 < wvLayerBy 10 1 150 := by decide

end Dmp
