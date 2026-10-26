import Im.Grammar
/-!
# The laws every sentence keeps

Each law is a DMP sentence stated at the level of one IM sentence, with the section it carries:

- **L1, both grants or refuse** (Axiom 1, T1): a sentence passes a door only when both tokens carry its kind; the acts a part may do are
  the intersection of what each witness granted.
- **L2, fail closed** (Axiom 1, C2.L2): a door that holds no grant passes nothing; there is no other way through.
- **L3, one permission per kind** (DMP chapter 04): every kind names exactly one permission, and no two kinds share one.
- **L4, legal names** (DMP chapter 04, "Encoding"): every kind's wire name is a legal `id8` name.
- **L5, a write says what it expects** (C15): a well-formed write sentence carries `absent` or `present`, never `any`.

The grants and the verdict are abstract here (`Grants`, `decide`), the way Gravity's `agree` is: which token carries which kind is the
work of Doppler and the operations master (DMP chapter 04, version 1), and the verifier's rule order (rules 1 to 16) stays where it is.
What this file proves is what any door that decides by these grants keeps, for every sentence of the grammar.
-/
namespace Im

/-- The two tokens of a sentence's sender (IM version 1): which kinds each carries. -/
structure Grants where
  security : Kind → Bool
  operational : Kind → Bool

inductive Verdict where
  | refuse | pass
  deriving DecidableEq, Repr

/-- The door's decision on a sentence: both grants, or refuse (T1: the intersection). -/
def decideSentence (g : Grants) (s : Sentence) : Verdict :=
  if g.security s.kind && g.operational s.kind then .pass else .refuse

/-- L1: a sentence passes only when both tokens carry its kind. Over every grant and every sentence. -/
theorem pass_needs_both_grants (g : Grants) (s : Sentence) (h : decideSentence g s = .pass) :
    g.security s.kind = true ∧ g.operational s.kind = true := by
  unfold decideSentence at h
  cases hs : g.security s.kind <;> cases ho : g.operational s.kind <;> simp_all

/-- L1, the other way: with both grants the door passes; one grant alone never does. -/
theorem one_grant_never_passes (g : Grants) (s : Sentence) (h : g.security s.kind = false ∨ g.operational s.kind = false) :
    decideSentence g s = .refuse := by
  unfold decideSentence
  cases h with
  | inl h => simp [h]
  | inr h => simp [h]

/-- The empty grant. -/
def noGrants : Grants := ⟨fun _ => false, fun _ => false⟩

/-- L2: a door that holds no grant passes no sentence of the space (the kernel tries every shape). -/
theorem nothing_granted_nothing_passes :
    (Shape.all.all fun sh => decideSentence noGrants ⟨sh.kind, [97], sh.mode, sh.expect⟩ == .refuse) = true := by decide +kernel

/-- L3: one permission per kind, and no two kinds share one. -/
theorem permissions_distinct :
    (Kind.all.all fun k => Kind.all.all fun d => (k == d) || (k.wire != d.wire)) = true := by decide +kernel

/-- L4: every kind's wire name is a legal `id8` name. -/
theorem kind_names_ok : (Kind.all.all fun k => nameOk k.wire) = true := by decide +kernel

/-- L5: a well-formed write says what it expects. -/
theorem write_says_expect (s : Sentence) (hwf : s.wfB = true) (hw : s.kind.verb = .write) : s.expect ≠ .any := by
  obtain ⟨⟨l, v⟩, p, m, e⟩ := s
  simp at hw
  subst hw
  cases e <;> simp_all [Sentence.wfB]

/-- L5, the other way: a read or an execute carries no expectation. -/
theorem read_execute_expect_any (s : Sentence) (hwf : s.wfB = true) (hv : s.kind.verb ≠ .write) : s.expect = .any := by
  obtain ⟨⟨l, v⟩, p, m, e⟩ := s
  cases v <;> cases e <;> simp_all [Sentence.wfB]

end Im
