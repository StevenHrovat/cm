import Dmp.R1Tier

/-!
# Every layer heals, and the stack settles

The owner, 2026-10-26 14:06Z: each information layer is a spanning tree across composable infrastructure, rooted at the DoT; and
14:08Z: build on existing giants. Two of them carry this file.

1. **Self-stabilizing shortest-path trees** (Dijkstra 1974; Dolev, Israeli and Moran 1993), in their synchronous distance-vector form
   (Bellman-Ford). Every node sets its value to one more than the smallest value among its neighbours; the root sets 0. From *any*
   starting state, however corrupted, after `k + 1` rounds every node at distance `k` from the root holds exactly `k`. The root is fixed
   and signed (the DoT), never elected as in the Spanning Tree Protocol, so no node can take it over: after the first round only the
   root holds 0.
2. **Potential games** (Monderer and Shapley 1996). Lensing accepts a reorganisation of the layer stack only when it lowers the VIBE
   potential (the sum over all layers). Every run of accepted reorganisations is at most as long as the starting potential, so the stack
   always settles and never oscillates. Settling means an equilibrium, not the global best: a concrete stack is stuck above its minimum.
-/

namespace Dmp

/-! ## Self-stabilizing trees -/

/-- The smallest value in a list, or the cap `B` when the list is empty or every value is above it. -/
def stMin (B : Nat) : List Nat → Nat
  | [] => B
  | x :: xs => min x (stMin B xs)

theorem st_min_le (B : Nat) : ∀ (l : List Nat) (x : Nat), x ∈ l → stMin B l ≤ x
  | [], _, h => absurd h List.not_mem_nil
  | y :: ys, x, h => by
    simp only [stMin]
    cases h with
    | head => exact Nat.min_le_left _ _
    | tail _ h' => exact Nat.le_trans (Nat.min_le_right _ _) (st_min_le B ys x h')

theorem st_min_cases (B : Nat) : ∀ (l : List Nat), stMin B l = B ∨ ∃ x ∈ l, stMin B l = x
  | [] => Or.inl rfl
  | y :: ys => by
    simp only [stMin]
    by_cases hy : y ≤ stMin B ys
    · rw [Nat.min_eq_left hy]
      exact Or.inr ⟨y, List.mem_cons_self, rfl⟩
    · rw [Nat.min_eq_right (Nat.le_of_lt (Nat.lt_of_not_le hy))]
      cases st_min_cases B ys with
      | inl h => exact Or.inl h
      | inr h =>
        obtain ⟨x, hx, he⟩ := h
        exact Or.inr ⟨x, List.mem_cons_of_mem _ hx, he⟩

/-- One synchronous round: the root (the DoT) holds 0; every other node one more than its smallest neighbour, capped at `B`. -/
def stStep (B root : Nat) (nbrs : Nat → List Nat) (d : Nat → Nat) (v : Nat) : Nat :=
  if v = root then 0 else min B (1 + stMin B ((nbrs v).map d))

/-- `m` rounds from the state `d`. -/
def stRounds (B root : Nat) (nbrs : Nat → List Nat) : Nat → (Nat → Nat) → (Nat → Nat)
  | 0, d => d
  | m + 1, d => stStep B root nbrs (stRounds B root nbrs m d)

/-- `v` is within `k` hops of the root. -/
def stWithin (root : Nat) (nbrs : Nat → List Nat) : Nat → Nat → Prop
  | 0, v => v = root
  | k + 1, v => v = root ∨ ∃ u ∈ nbrs v, stWithin root nbrs k u

theorem st_within_root (root : Nat) (nbrs : Nat → List Nat) : ∀ k, stWithin root nbrs k root
  | 0 => rfl
  | _ + 1 => Or.inl rfl

theorem st_within_mono (root : Nat) (nbrs : Nat → List Nat) : ∀ (k : Nat) (v : Nat), stWithin root nbrs k v → stWithin root nbrs (k + 1) v
  | 0, v, h => Or.inl h
  | k + 1, v, h => by
    cases h with
    | inl hr => exact Or.inl hr
    | inr hu =>
      obtain ⟨u, hu, hk⟩ := hu
      exact Or.inr ⟨u, hu, st_within_mono root nbrs k u hk⟩

/-- Real paths come through: from any start, a node within `k` hops holds at most `k` from round `k + 1` on, for ever. -/
theorem st_upper (B root : Nat) (nbrs : Nat → List Nat) (d : Nat → Nat) :
    ∀ (k : Nat) (v : Nat) (m : Nat), stWithin root nbrs k v → stRounds B root nbrs (m + k + 1) d v ≤ k
  | 0, v, m, h => by
    have hv : v = root := h
    show stStep B root nbrs _ v ≤ 0
    unfold stStep
    rw [if_pos hv]
    exact Nat.le_refl 0
  | k + 1, v, m, h => by
    show stStep B root nbrs (stRounds B root nbrs (m + k + 1) d) v ≤ k + 1
    unfold stStep
    by_cases hv : v = root
    · rw [if_pos hv]
      exact Nat.zero_le _
    · rw [if_neg hv]
      cases h with
      | inl hr => exact absurd hr hv
      | inr hu =>
        obtain ⟨u, hu, hk⟩ := hu
        have hd := st_upper B root nbrs d k u m hk
        have hm : stMin B ((nbrs v).map (stRounds B root nbrs (m + k + 1) d)) ≤ stRounds B root nbrs (m + k + 1) d u :=
          st_min_le B _ _ (List.mem_map.mpr ⟨u, hu, rfl⟩)
        exact Nat.le_trans (Nat.min_le_right _ _) (by omega)

/-- Fake low values are flushed out: after `m` rounds, a value below both `m` and the cap is backed by a real path of that length. -/
theorem st_lower (B root : Nat) (nbrs : Nat → List Nat) (d : Nat → Nat) :
    ∀ (m : Nat) (v : Nat), stRounds B root nbrs m d v < m → stRounds B root nbrs m d v < B →
      stWithin root nbrs (stRounds B root nbrs m d v) v
  | 0, _, h, _ => absurd h (Nat.not_lt_zero _)
  | m + 1, v, hm, hB => by
    show stWithin root nbrs (stStep B root nbrs (stRounds B root nbrs m d) v) v
    have hm' : stStep B root nbrs (stRounds B root nbrs m d) v < m + 1 := hm
    have hB' : stStep B root nbrs (stRounds B root nbrs m d) v < B := hB
    by_cases hv : v = root
    · rw [hv]
      exact st_within_root root nbrs _
    · unfold stStep at hm' hB' ⊢
      rw [if_neg hv] at hm' hB' ⊢
      have hlt : 1 + stMin B ((nbrs v).map (stRounds B root nbrs m d)) < B := by
        cases Nat.le_total B (1 + stMin B ((nbrs v).map (stRounds B root nbrs m d))) with
        | inl hle => rw [Nat.min_eq_left hle] at hB'; exact absurd hB' (Nat.lt_irrefl _)
        | inr hle => rw [Nat.min_eq_right hle] at hB'; exact hB'
      have hmin : min B (1 + stMin B ((nbrs v).map (stRounds B root nbrs m d))) = 1 + stMin B ((nbrs v).map (stRounds B root nbrs m d)) :=
        Nat.min_eq_right (Nat.le_of_lt hlt)
      rw [hmin] at hm' ⊢
      cases st_min_cases B ((nbrs v).map (stRounds B root nbrs m d)) with
      | inl he => rw [he] at hlt; omega
      | inr he =>
        obtain ⟨x, hx, hex⟩ := he
        obtain ⟨u, hu, hux⟩ := List.mem_map.mp hx
        rw [hex] at hm' hlt ⊢
        have hk := st_lower B root nbrs d m u (by omega) (by omega)
        rw [hux] at hk
        rw [Nat.add_comm]
        exact Or.inr ⟨u, hu, hk⟩

/-- Self-stabilization: from any starting state, however corrupted, a node whose distance to the root is `k` (within `k` hops and not
within fewer) holds exactly `k` in every round from `k + 1` on, as long as `k` is below the cap. -/
theorem st_self_stabilizing (B root : Nat) (nbrs : Nat → List Nat) (d : Nat → Nat) (k v m : Nat) (hk : stWithin root nbrs k v)
    (hmin : ∀ j, j < k → ¬ stWithin root nbrs j v) (hB : k < B) : stRounds B root nbrs (m + k + 1) d v = k := by
  have hup := st_upper B root nbrs d k v m hk
  apply Nat.le_antisymm hup
  apply Nat.not_lt.mp
  intro hlt
  exact hmin _ hlt (st_lower B root nbrs d (m + k + 1) v (by omega) (by omega))

/-- No takeover of the root: from the first round on, only the root holds 0, whatever the other nodes started with or claim. -/
theorem st_no_root_takeover (B root : Nat) (nbrs : Nat → List Nat) (d : Nat → Nat) (m v : Nat) (hB : 0 < B)
    (h : stRounds B root nbrs (m + 1) d v = 0) : v = root := by
  have hw := st_lower B root nbrs d (m + 1) v (by omega) (by omega)
  rw [h] at hw
  exact hw

/-! ## The stack settles: a potential game -/

/-- A run of accepted reorganisations: each one strictly lowers the potential (the VIBE potential summed over every layer). -/
def stImproving {S : Type} (pot : S → Nat) : S → List S → Prop
  | _, [] => True
  | s, t :: ts => pot t < pot s ∧ stImproving pot t ts

/-- Every run of accepted reorganisations is at most as long as the starting potential: the stack settles, it never oscillates. -/
theorem st_settles {S : Type} (pot : S → Nat) : ∀ (s : S) (ts : List S), stImproving pot s ts → ts.length ≤ pot s
  | _, [], _ => Nat.zero_le _
  | s, t :: ts, h => by
    have ht := st_settles pot t ts h.2
    simp only [List.length_cons]
    exact Nat.le_trans (Nat.succ_le_succ ht) h.1

/-- A run never comes back to a state it passed: no reorganisation is ever undone and redone. -/
theorem st_no_cycle {S : Type} (pot : S → Nat) (s : S) (ts : List S) (h : stImproving pot s ts) : ∀ t ∈ ts, pot t < pot s := by
  induction ts generalizing s with
  | nil => intro t ht; exact absurd ht List.not_mem_nil
  | cons u us ih =>
    intro t ht
    cases ht with
    | head => exact h.1
    | tail _ ht' => exact Nat.lt_trans (ih u h.2 t ht') h.1

/-- Settling is an equilibrium, not the global best. Three stack states in a row, each reachable only from its neighbour: potentials
1, 2 and 0. From state 0 the only move goes up, so it is never accepted, yet state 2 is lower. -/
def stPot : Nat → Nat
  | 0 => 1
  | 1 => 2
  | _ => 0

def stMoves : Nat → List Nat
  | 0 => [1]
  | 1 => [0, 2]
  | _ => [1]

theorem st_equilibrium_not_optimal : (∀ t ∈ stMoves 0, ¬ stPot t < stPot 0) ∧ stPot 2 < stPot 0 := by decide

end Dmp
