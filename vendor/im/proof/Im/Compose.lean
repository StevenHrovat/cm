import Im.Grammar
/-!
# The composition theorem: the door is what makes IM sound

DMP chapter 04, "What IM is (the axiomatic chain)": "IM is the interface over which a network of nodes that meet DMP is itself a node
that meets DMP"; "every boundary between scales is an IM door: a message passes only with both signatures (C19). A node has to keep its
guarantee only on what its door let in; behind the door a network keeps it on any input." The chapter cites these theorems by name
(`sound`, `galaxy_robust`, `galaxy_is_a_node`, `order_free`, `echo_alone_not_robust`).

**Source.** Written on 2026-10-26 in the owner's private repository (cm-work, pull request 137, draft, `CM.BlackHole/evidence/Dmp/Compose.lean`,
Apache License 2.0, the same owner) after the owner's words of 02:43Z to 03:08Z that day. It is carried here, into the language's own
proof, because a version of IM is accepted at a door only with its composition proof (chapter 04). Names kept; only the namespace is IM's.

What this does not model: timing, refusals used as a signal, and inference across nodes. Those are the next guarantees to state in the
same shape.
-/
namespace Im

/-- An IM message as the door sees it: the security signature (Doppler's chain), the operational signature (the operations master's
    chain), a body. -/
structure Msg where
  sec : Bool
  op : Bool
  body : Nat
  deriving DecidableEq, Repr

/-- Both signatures present (C19). -/
def Msg.ok (m : Msg) : Bool := m.sec && m.op

/-- The IM door: only messages with both signatures pass. -/
def door (ins : List Msg) : List Msg := ins.filter Msg.ok

/-- A behaviour keeps the guarantee on signed input: it relies on its door. -/
def Preserves (f : List Msg → List Msg) : Prop :=
  ∀ ins, (∀ m ∈ ins, m.ok = true) → ∀ m ∈ f ins, m.ok = true

/-- A behaviour keeps the guarantee on any input at all. -/
def Robust (f : List Msg → List Msg) : Prop :=
  ∀ ins, ∀ m ∈ f ins, m.ok = true

/-- A net: one monolithic node, or a galaxy of nets one scale down. -/
inductive Net where
  | node (f : List Msg → List Msg)
  | galaxy (members : List Net)

mutual
/-- A net's behaviour. A galaxy hands what passes its IM door to every member and gathers their outputs. -/
def netRun : Net → List Msg → List Msg
  | .node f, ins => f ins
  | .galaxy ms, ins => netRunAll ms (door ins)
/-- The gathered outputs of a galaxy's members. -/
def netRunAll : List Net → List Msg → List Msg
  | [], _ => []
  | n :: ns, ins => netRun n ins ++ netRunAll ns ins
end

/-- A net built from nodes that each keep the guarantee on signed input. -/
inductive Built : Net → Prop
  | node (f : List Msg → List Msg) (hf : Preserves f) : Built (.node f)
  | galaxy (ms : List Net) (h : ∀ n ∈ ms, Built n) : Built (.galaxy ms)

theorem door_ok (ins : List Msg) : ∀ m ∈ door ins, m.ok = true := by
  intro m hm
  exact (List.mem_filter.mp hm).2

theorem robust_preserves (f : List Msg → List Msg) (h : Robust f) : Preserves f :=
  fun ins _ m hm => h ins m hm

theorem runAll_ok (ms : List Net) (hms : ∀ n ∈ ms, Preserves (netRun n)) (ins : List Msg)
    (hin : ∀ m ∈ ins, m.ok = true) : ∀ m ∈ netRunAll ms ins, m.ok = true := by
  induction ms with
  | nil => intro m hm; simp [netRunAll] at hm
  | cons n ns ih =>
    intro m hm
    simp only [netRunAll, List.mem_append] at hm
    cases hm with
    | inl h => exact hms n (by simp) ins hin m h
    | inr h => exact ih (fun n' hn' => hms n' (by simp [hn'])) m h

/-- Behind the IM door a galaxy of preserving members keeps the guarantee on any input. -/
theorem galaxy_robust (ms : List Net) (hms : ∀ n ∈ ms, Preserves (netRun n)) : Robust (netRun (.galaxy ms)) := by
  intro ins m hm
  simp only [netRun] at hm
  exact runAll_ok ms hms (door ins) (door_ok ins) m hm

/-- The composition theorem: every net built from preserving nodes preserves, at every scale. -/
theorem sound (n : Net) (h : Built n) : Preserves (netRun n) := by
  induction h with
  | node f hf =>
    intro ins hin m hm
    simp only [netRun] at hm
    exact hf ins hin m hm
  | galaxy ms _ ih => exact robust_preserves _ (galaxy_robust ms ih)

/-- Self-similarity: a galaxy is itself a node that meets the spec, one scale up. -/
theorem galaxy_is_a_node (ms : List Net) (h : ∀ n ∈ ms, Built n) : Built (.node (netRun (.galaxy ms))) :=
  Built.node _ (sound _ (Built.galaxy ms h))

theorem runAll_perm {ms ms' : List Net} (p : ms.Perm ms') (ins : List Msg) :
    (netRunAll ms ins).Perm (netRunAll ms' ins) := by
  induction p with
  | nil => exact List.Perm.refl _
  | cons x _ ih =>
    simp only [netRunAll]
    exact ih.append_left _
  | swap x y l =>
    simp only [netRunAll]
    rw [← List.append_assoc, ← List.append_assoc]
    exact List.perm_append_comm.append_right _
  | trans _ _ ih1 ih2 => exact ih1.trans ih2

/-- No set order: rearranging a galaxy's members only rearranges what it emits. -/
theorem order_free {ms ms' : List Net} (p : ms.Perm ms') (ins : List Msg) :
    (netRun (.galaxy ms) ins).Perm (netRun (.galaxy ms') ins) := by
  simp only [netRun]
  exact runAll_perm p _

/-! ### Why the door: a preserving node alone is not sound -/

/-- A node that passes on what it receives. -/
def echo : List Msg → List Msg := fun l => l

/-- A message with only one signature. -/
def unsigned : Msg := ⟨false, true, 0⟩

theorem echo_preserves : Preserves echo := fun _ hin m hm => hin m hm

/-- Without the door, the echo passes a message with one signature through. -/
theorem echo_alone_not_robust : ¬ Robust echo := by
  intro h
  have hu := h [unsigned] unsigned (List.mem_singleton.mpr rfl)
  exact absurd hu (by decide)

/-- Behind the IM door the same echo is sound. -/
theorem im_makes_echo_sound : Robust (netRun (.galaxy [.node echo])) :=
  galaxy_robust _ (fun n hn => by
    simp at hn
    subst hn
    exact sound _ (Built.node _ echo_preserves))

end Im
