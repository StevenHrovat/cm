/-
T31 (the trap, the floor) and T34 (three roads to the floor): each of the three bounds alone brings contained information to the entropy
floor, by a road of its own. Each road below is proven from its own construction only: the gate road for any key and any record, the
evidence road for any gate and any record, the record road for any gate and any key. "Each engine has to prove that at least one of the
axioms alone really does guarantee that the data is fully destroyed or ... lowers to the floor" (the owner, 2026-10-26).
-/
import Dmp.Gate

namespace Dmp

/-- What an engine may do with an item. -/
inductive Op where
  | read
  | forward
  | send
  | readmit
  | destroy
  deriving DecidableEq, Repr

/-- An item of information as the engine holds it: contained or not, its key's validity (never renewed while contained), its record's
    destruction date, and whether the engine destroyed it. -/
structure Item where
  contained : Bool
  keyUntil : Nat
  destroyAt : Nat
  destroyed : Bool
  deriving DecidableEq, Repr

/-- What a read yields: the plaintext, or only ciphertext (indistinguishable from random bytes of the same length: the floor). -/
inductive Yield where
  | plain
  | cipher
  | nothing
  deriving DecidableEq, Repr

/-! ### Road 1: the dual gate alone (T31's trap) -/

/-- The gate's offer on a contained item: no part is trusted for any act on it but re-admission and destruction. -/
def offered (i : Item) (op : Op) : Bool :=
  if i.contained then (op == .readmit || op == .destroy) else true

/-- Road 1: on a contained item no read, forward or send (an export) is offered, whatever the key and the record say. -/
theorem road_gate (i : Item) (h : i.contained = true) (op : Op) (hop : op = .read ∨ op = .forward ∨ op = .send) :
    offered i op = false := by
  rcases hop with rfl | rfl | rfl <;> simp [offered, h] <;> decide

/-! ### Road 2: evidence alone (the key decays) -/

/-- The key is released only on a live check that its standing is still valid. -/
def keyReleased (i : Item) (t : Nat) : Bool := Nat.blt t i.keyUntil

/-- A read under any gate: `allowed` is whatever the gate said; without the key the bytes stay ciphertext. -/
def readUnder (allowed : Bool) (i : Item) (t : Nat) : Yield :=
  if i.destroyed then .nothing
  else if allowed && keyReleased i t then .plain else .cipher

/-- Road 2: once the key's validity has run out, no read yields plaintext, even under a gate that allows everything. -/
theorem road_evidence (allowed : Bool) (i : Item) (t : Nat) (h : i.keyUntil ≤ t) : readUnder allowed i t ≠ .plain := by
  unfold readUnder keyReleased
  have : Nat.blt t i.keyUntil = false := by
    cases hb : Nat.blt t i.keyUntil
    · rfl
    · exfalso
      rw [Nat.blt_eq] at hb
      omega
  split
  · decide
  · simp [this]

/-! ### Road 3: the record alone (scheduled destruction) -/

/-- The engine's tick: it executes every destruction its record schedules. -/
def tick (i : Item) (t : Nat) : Item :=
  if i.destroyAt ≤ t then { i with destroyed := true } else i

/-- Road 3: after the tick at or past the destruction date the item is destroyed, and nothing reads it, under any gate and any key. -/
theorem road_record (allowed : Bool) (i : Item) (t t' : Nat) (h : i.destroyAt ≤ t) :
    readUnder allowed (tick i t) t' = .nothing := by
  simp [tick, h, readUnder]

/-- A destruction is final: ticking again changes nothing. -/
theorem destroyed_stays (i : Item) (t : Nat) (h : i.destroyed = true) : (tick i t).destroyed = true := by
  unfold tick; split <;> simp [h]

end Dmp
