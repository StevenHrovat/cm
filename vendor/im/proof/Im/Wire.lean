import Im.Grammar
/-!
# The sentence on the wire: one canonical spelling (I6)

A sentence travels as the payload of an IM envelope whose `kind` field is the sentence's kind (`<layer>.<verb>`), so the payload carries
the rest: magic `"IMS"`, version `1`, the path as `bytes16` (a `u16` length, big-endian, then the bytes), the mode byte and the expect
byte. Decoding is strict (DMP chapter 04, "Encoding"): exactly these fields, legal bytes, nothing trailing, or the payload is malformed
(`none`).

What is proved: encode then decode gives the sentence back, for every well-formed sentence with any legal path (the path is universally
quantified, not enumerated); whatever decodes is well formed and carries the kind the envelope named; a trailing byte, a wrong version
and an ill-formed shape are malformed for every shape of the space (the kernel tries each).
-/
namespace Im

def Mode.byte : Mode → Nat | .dry => 0 | .apply => 1
def Expect.byte : Expect → Nat | .any => 0 | .absent => 1 | .present => 2
def Mode.ofByte : Nat → Option Mode | 0 => some .dry | 1 => some .apply | _ => none
def Expect.ofByte : Nat → Option Expect | 0 => some .any | 1 => some .absent | 2 => some .present | _ => none

/-- The payload of a sentence: "IMS", version 1, bytes16(path), mode, expect. -/
def Sentence.encode (s : Sentence) : List Nat :=
  [73, 77, 83, 1, s.path.length / 256, s.path.length % 256] ++ s.path ++ [s.mode.byte, s.expect.byte]

/-- Exactly two bytes left: the adjectives. -/
def tail2 : List Nat → Option (Nat × Nat)
  | [m, e] => some (m, e)
  | _ => none

/-- The two adjective bytes, read strictly. -/
def adjectives (m e : Nat) : Option (Mode × Expect) :=
  match Mode.ofByte m, Expect.ofByte e with
  | some mo, some ex => some (mo, ex)
  | _, _ => none

theorem tail2_pair (m e : Nat) : tail2 [m, e] = some (m, e) := rfl
theorem adjectives_bytes (m : Mode) (e : Expect) : adjectives m.byte e.byte = some (m, e) := by cases m <;> cases e <;> rfl

/-- After the header: the path of the declared length, then exactly the two adjective bytes; the length bytes must be canonical. -/
def Sentence.assemble (k : Kind) (hi lo : Nat) (rest : List Nat) : Option Sentence :=
  match tail2 (rest.drop (256 * hi + lo)) with
  | none => none
  | some (m, e) =>
    match adjectives m e with
    | none => none
    | some (mo, ex) =>
      if (rest.take (256 * hi + lo)).length = 256 * hi + lo ∧ hi < 256 ∧ lo < 256 then some ⟨k, rest.take (256 * hi + lo), mo, ex⟩
      else none

/-- The header, then the rest. -/
def Sentence.parse (k : Kind) : List Nat → Option Sentence
  | 73 :: 77 :: 83 :: 1 :: hi :: lo :: rest => Sentence.assemble k hi lo rest
  | _ => none

/-- Strict decoding of a payload for the kind the envelope named: parsed, and well formed. -/
def Sentence.decode (k : Kind) (b : List Nat) : Option Sentence :=
  match Sentence.parse k b with
  | some s => if s.wfB then some s else none
  | none => none

theorem pathOk_length (p : Name) (h : pathOk p = true) : p.length ≤ 1024 := by
  unfold pathOk at h
  simp only [Bool.and_eq_true, decide_eq_true_eq] at h
  exact h.2

/-- I6, for every well-formed sentence with any legal path: decoding the encoding gives the sentence back. -/
theorem decode_encode (s : Sentence) (hwf : s.wfB = true) : Sentence.decode s.kind s.encode = some s := by
  obtain ⟨k, p, m, e⟩ := s
  have hp : pathOk p = true := by
    have h := hwf
    simp only [Sentence.wfB, Bool.and_eq_true] at h
    exact h.1
  have hlen : 256 * (p.length / 256) + p.length % 256 = p.length := Nat.div_add_mod p.length 256
  have hlt : p.length ≤ 1024 := pathOk_length p hp
  have hhi : p.length / 256 < 256 := by omega
  have hlo : p.length % 256 < 256 := Nat.mod_lt _ (by decide)
  unfold Sentence.decode Sentence.parse Sentence.encode
  simp only [List.cons_append, List.nil_append]
  unfold Sentence.assemble
  rw [hlen, List.drop_left, List.take_left]
  simp only [tail2_pair, adjectives_bytes]
  simp [hhi, hlo, hwf]

theorem assemble_kind (k : Kind) (hi lo : Nat) (rest : List Nat) (s : Sentence) (h : Sentence.assemble k hi lo rest = some s) :
    s.kind = k := by
  unfold Sentence.assemble at h
  split at h
  · exact absurd h (by simp)
  · split at h
    · exact absurd h (by simp)
    · split at h
      · simp only [Option.some.injEq] at h
        rw [← h]
      · exact absurd h (by simp)

theorem parse_kind (k : Kind) (b : List Nat) (s : Sentence) (h : Sentence.parse k b = some s) : s.kind = k := by
  unfold Sentence.parse at h
  split at h
  · exact assemble_kind _ _ _ _ _ h
  · exact absurd h (by simp)

/-- Whatever decodes is well formed: a door never sees an ill-formed sentence. -/
theorem decode_wf (k : Kind) (b : List Nat) (s : Sentence) (h : Sentence.decode k b = some s) : s.wfB = true := by
  unfold Sentence.decode at h
  cases hp : Sentence.parse k b with
  | none => simp [hp] at h
  | some t =>
    simp only [hp] at h
    by_cases hw : t.wfB = true
    · simp [hw] at h
      subst h
      exact hw
    · simp [hw] at h

/-- Whatever decodes carries the kind the envelope named. -/
theorem decode_kind (k : Kind) (b : List Nat) (s : Sentence) (h : Sentence.decode k b = some s) : s.kind = k := by
  unfold Sentence.decode at h
  cases hp : Sentence.parse k b with
  | none => simp [hp] at h
  | some t =>
    simp only [hp] at h
    by_cases hw : t.wfB = true
    · simp [hw] at h
      subst h
      exact parse_kind k b _ hp
    · simp [hw] at h

/-- A sample sentence per shape, with the path `a`. -/
def Shape.sample (sh : Shape) : Sentence := ⟨sh.kind, [97], sh.mode, sh.expect⟩

/-- A trailing byte is malformed, for every shape (the kernel tries each). -/
theorem trailing_byte_malformed :
    (Shape.all.all fun sh => Sentence.decode sh.kind (sh.sample.encode ++ [0]) == none) = true := by decide +kernel

/-- A wrong version byte is malformed, for every shape. -/
theorem wrong_version_malformed :
    (Shape.all.all fun sh => Sentence.decode sh.kind ([73, 77, 83, 0] ++ sh.sample.encode.drop 4) == none) = true := by decide +kernel

/-- An ill-formed shape (a write that expects `any`, a read that expects something) never decodes, for every shape. -/
theorem ill_formed_never_decodes :
    (Shape.all.all fun sh => sh.wfB || (Sentence.decode sh.kind sh.sample.encode == none)) = true := by decide +kernel

end Im
