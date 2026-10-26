/-!
# R1 step 2: C2's laws as Lean propositions, written blind from the sentences

Sources, and nothing else: DMP 01-constitution.md, sections "The words" and "C2 · Every message verifies itself";
DMP 04-im.md, sections "The verify rules" and "Message kinds and permissions". No verifier is written here. The file defines an
abstract interface for "a verifier" (`R1lVerifier`) and states C2.L1, C2.L2, C2.L3, C2.L3b and C2.L4 as `Prop`s over it
(`r1l_L1` ... `r1l_L4`). A later step builds a concrete instance from another model and proves the laws for it. Lean 4 core only,
no assumed facts: every theorem below is proved.

## The interface in one paragraph

A verifier is one fixed configuration (`settings`: the four values the rules name), a view of the message as the rules see it (frame
length, the three strict parses, the token's fields, the two signature checks, the envelope's `sent at`, message id and kind), the
outside facts the rules consult as functions of time (the token signer chain to a trusted `ImTokenSigner` root, Doppler's enrolment),
two observations of the replay memory (which pairs it remembers unexpired at a time, and how many), and the function `verify` that
maps an input, the verifier's time and the memory before to the answer (verified, or refused with a reason) and the memory after.

## Readings chosen where a sentence is ambiguous

1. Time. Instants are integers (`Int`) in a unit the instance fixes; the durations in the settings (`tokenMaxLifetime`, the clock
   skew) are natural numbers in the same unit. "Now" is the verifier's time, passed to `verify` with each input.
2. "Every input gets exactly one answer" (L2) holds by the shape of the interface: `verify` is a total function, so each input, time
   and memory gets one answer and one memory after. An input is a frame plus the session it arrives in, if any (rule 10 needs that).
3. "the first row ... whose test fails, in the table's order, with that row's reason" (L2): the order is the rule number (1 to 16).
   A row lists one or more reasons; "that row's reason" is read as one of the reasons that row lists. Which one is left open.
4. "such a refusal leaves the replay memory as it was" (L2) is read for every refusal, rule 16's included (rule 16 is a row of the
   table, and the rule's own rationale says "a refused message never uses up an id").
5. L2 is also read as complete: when no row fails, the answer is verified (chapter 04: "A message that passes all of them is
   verified"; and "only rule 16 may still refuse" leaves no other refusal). So a verifier that always refuses violates L2.
6. Rule 16's test. The row says "The pair (subject key id, message id) has not been seen; it is remembered until `sent at` plus the
   clock skew", with reasons `Replayed` and `ReplayCacheFull`. Its test is read as: the memory does not remember the pair unexpired
   at now, and the memory holds fewer unexpired messages than `milkyway.im.replayCapacity` (there is room to remember it). The
   second half comes from the reason `ReplayCacheFull`, the rationale paragraph and L3b.
7. "inside its validity window" (L1) is read as the window the rules check: now is not before `not before` minus the clock skew
   (rule 7) and now is before `expires at` (rule 8, no skew). A strict window without skew would contradict rule 7.
8. "a token signed by a trusted token signer" (L1) is read as rules 4 and 5 together: the signer chain validates to a trusted root in
   the role `ImTokenSigner` at the verifier's time, and the token signature is valid under the signer certificate's key.
9. "names an enrolled, unrevoked subject whose key signed the message" (L1): Doppler's enrolment knows the token's subject, has not
   revoked it, and holds the token's key id for it; the token's subject key matches that key id (rule 3) and the envelope signature is
   valid under the token's subject key (rule 11). L1 does not mention the role, so L1 does not demand the role match (rule 9 does).
10. "it carries the permission the kind needs" (L1): the kind has a row in the table of kinds and the token carries that row's
   permission.
11. Kinds and permissions are names (`String`), because a token may list any permission name and a message may carry any kind name
   ("A kind that is not in this table is refused as unknown, even when the token lists a permission of that name").
12. The table of kinds has two rows, in order: "(handshake)" needs `im.session.open`, `im.heartbeat` needs `im.heartbeat.send`. The
   handshake row is written in parentheses and L4 says "no other kind exists in IM version 0" besides the heartbeat, so the handshake
   row is modelled as a row that is not a message kind: no message's kind name matches it. The third column ("Who gets it",
   Doppler's policy) is not something the verifier checks and is not modelled.
13. L4 is read as a law on the answers: a verified message is a heartbeat (kind `im.heartbeat`) whose token carries
   `im.heartbeat.send`. The table's own content is stated as two theorems below (`r1l_needs_heartbeat`, `r1l_needs_only_heartbeat`).
14. "the same signed message" (L3) is read as the same frame (the signed envelope as received), whatever session it arrives in. A
   stronger reading, the same pair (subject key id, message id), is what rule 16 keys on; it is not the law's wording.
15. "at any later or earlier time" (L3) is read as any time at all, the same instant included.
16. "Through one verifier" (L3) is read as one `R1lVerifier` value: one configuration, one view of trust and enrolment, one `verify`.
17. "remembers as many unexpired messages as milkyway.im.replayCapacity allows" (L3b) is read as: the count of unexpired remembered
   messages at now is at least the capacity. "nothing verifies" is read as: no input verifies at that time with that memory. "it never
   forgets early to make room" is read, for that same full memory, as: the memory after still remembers every pair it remembered
   unexpired at now.
18. Rule 6: the lifetime is `expires at` minus `not before`. Rule 12: "within the clock skew of now, both ways" includes the bounds.
   Rule 10: outside a session it holds trivially.
19. Doppler's enrolment and the trust anchors are read at the verifier's time (rule 4 says "at the verifier's time"; rule 9 names no
   time, and the same time is used).

## What the sentences leave open

- Which reason a row with several reasons gives (rows 2, 3, 4, 9, 15, 16); e.g. whether a seen pair in a full memory is `Replayed`
  or `ReplayCacheFull`.
- What the memory after a verified answer contains, beyond what L3 and L3b force (rule 16's "remembered until `sent at` plus the clock
  skew" is not part of any law's wording); how "unexpired" relates to the clock skew at a given time; whether memory may be shared
  between verifiers.
- The setting name of the clock skew (the rules say "the clock skew" only); whether settings may change while a verifier runs (here
  they are fixed per verifier); whether the clock skew is the same for rules 7, 12 and 16 (here one value).
- Whether time moves forward between calls; whose clock "now" is; the unit of time.
- What "parses strictly" means byte by byte (rules 2, 3, 15), and when `UnknownVersion` rather than `Malformed` applies.
- How the handshake itself is verified (it needs `im.session.open`), and how a session's subject is fixed; only rule 10's check is in.
- Whether Doppler's enrolment can change between two calls at the same instant (here it is a function of time only).
- C2's first paragraph ("the verifier checks every message"; "No public port opens before the verifier has passed its test level on
  the sandbox") is not a law and is not formalised; neither is the refusal log, nor what reaches a consumer.
- The interface is abstract: whether an instance's observation functions (`remembers`, `unexpired`, the parses, the signature checks)
  say what their names say is the instance's obligation, not something these laws can check.
-/

namespace Dmp

/-! ## The settings the rules name -/

/-- The values the verify rules and C2.L3b name. Durations are in the same unit as time. -/
structure R1lSettings where
  /-- `milkyway.im.maxFrameBytes` (rule 1). -/
  maxFrameBytes : Nat
  /-- `milkyway.im.tokenMaxLifetime` (rule 6). -/
  tokenMaxLifetime : Nat
  /-- "the clock skew" (rules 7, 12, 16); its setting name is not given in the sections read. -/
  clockSkew : Nat
  /-- `milkyway.im.replayCapacity` (C2.L3b, rule 16's `ReplayCacheFull`). -/
  replayCapacity : Nat

/-! ## The answer -/

/-- The refusal reasons of chapter 04's verify table, spelled as there. -/
inductive R1lReason where
  | Oversize
  | Malformed
  | UnknownVersion
  | UntrustedTokenSigner
  | CertificateExpired
  | BadTokenSignature
  | TokenPolicy
  | TokenNotYetValid
  | TokenExpired
  | NotEnrolled
  | Revoked
  | EnrolmentMismatch
  | SessionMismatch
  | BadMessageSignature
  | Stale
  | UnknownKind
  | WrongPermission
  | Replayed
  | ReplayCacheFull
  deriving DecidableEq, Repr

/-- The answer to one input: verified, or refused with a reason. -/
inductive R1lAnswer where
  | verified
  | refused (reason : R1lReason)
  deriving DecidableEq, Repr

/-! ## The interface: what a verifier is, to the verify rules -/

/-- An abstract verifier. Every field is what some sentence names; nothing here says how a verifier computes its answer. -/
structure R1lVerifier where
  /-- A frame: the signed envelope (with its token and payload) as received. "The same signed message" is the same frame. -/
  Frame : Type
  /-- An input: a frame plus the context it arrives in. -/
  Input : Type
  /-- A token subject (an identity in Doppler's enrolment). -/
  Subject : Type
  /-- An enrolled role. -/
  Role : Type
  /-- A subject key id (the SHA-256 of the subject key). -/
  KeyId : Type
  /-- A message id. -/
  MsgId : Type
  /-- The replay memory. -/
  Memory : Type
  /-- The settings this verifier runs with. -/
  settings : R1lSettings
  /-- The frame of an input. -/
  frame : Input → Frame
  /-- The session the input arrives in: `some s` inside a session whose subject is `s`, `none` outside any session. -/
  session : Input → Option Subject
  /-- Rule 1: the frame's length in bytes. -/
  frameBytes : Frame → Nat
  /-- Rule 2: the signed envelope parses strictly (magic, version 0, lengths, alphabets, nothing trailing). -/
  envelopeParses : Frame → Prop
  /-- Rule 3, first half: the token inside parses strictly. -/
  tokenParses : Frame → Prop
  /-- Rule 3, second half: the token's subject key is ECDSA P-256 and its SHA-256 is the token's subject key id. -/
  subjectKeyBinds : Frame → Prop
  /-- The token's subject. -/
  subject : Frame → Subject
  /-- The token's role. -/
  role : Frame → Role
  /-- The token's subject key id. -/
  keyId : Frame → KeyId
  /-- The token's `not before`. -/
  notBefore : Frame → Int
  /-- The token's `expires at`. -/
  expiresAt : Frame → Int
  /-- Rule 4: at the given time, the token's signer chain validates to a trusted root in the role `ImTokenSigner`. -/
  signerChainValid : Int → Frame → Prop
  /-- Rule 5: the token signature is valid under the signer certificate's key. -/
  tokenSignatureValid : Frame → Prop
  /-- The token lists the named permission. -/
  tokenCarries : Frame → String → Prop
  /-- Rule 11: the envelope signature is valid under the token's subject key. -/
  envelopeSignatureValid : Frame → Prop
  /-- The envelope's `sent at`. -/
  sentAt : Frame → Int
  /-- The envelope's message id. -/
  msgId : Frame → MsgId
  /-- The envelope's kind name. -/
  kind : Frame → String
  /-- Rule 15: the payload parses strictly as its kind says. -/
  payloadParses : Frame → Prop
  /-- Rule 9: at the given time, Doppler's enrolment knows the subject. -/
  enrolKnows : Int → Subject → Prop
  /-- Rule 9: at the given time, Doppler's enrolment has revoked the subject. -/
  enrolRevoked : Int → Subject → Prop
  /-- Rule 9: at the given time, Doppler's enrolment holds this role for the subject. -/
  enrolRole : Int → Subject → Role → Prop
  /-- Rule 9: at the given time, Doppler's enrolment holds this key id for the subject. -/
  enrolKeyId : Int → Subject → KeyId → Prop
  /-- The memory, at the given time, remembers the pair (subject key id, message id) and that entry has not expired. -/
  remembers : Memory → Int → KeyId → MsgId → Prop
  /-- How many unexpired messages the memory remembers at the given time. -/
  unexpired : Memory → Int → Nat
  /-- The verifier: an input, the verifier's time and the memory before give the answer and the memory after. -/
  verify : Input → Int → Memory → R1lAnswer × Memory

/-! ## Chapter 04's table of kinds -/

/-- The first column of the table of kinds: the handshake (written "(handshake)", not a message kind) or a message kind's name. -/
inductive R1lTableKind where
  | handshake
  | message (name : String)

/-- One row of the table of kinds: the kind and the one permission it needs. -/
structure R1lKindRow where
  kind : R1lTableKind
  needs : String

/-- The table of kinds of IM version 0, rows in the text's order. -/
def r1lKindTable : List R1lKindRow :=
  [ { kind := R1lTableKind.handshake, needs := "im.session.open" },
    { kind := R1lTableKind.message "im.heartbeat", needs := "im.heartbeat.send" } ]

/-- Some row of `rows` is the message kind named `k` and needs the permission `p`. -/
def r1lNeedsIn : List R1lKindRow → String → String → Prop
  | [], _, _ => False
  | row :: rows, k, p => (row.kind = R1lTableKind.message k ∧ row.needs = p) ∨ r1lNeedsIn rows k p

/-- The message kind named `k` is in the table of kinds and needs the permission `p`. -/
def r1lNeeds (k p : String) : Prop :=
  r1lNeedsIn r1lKindTable k p

/-! ## Chapter 04's verify table: the rows, their order, their reasons, their tests -/

/-- The sixteen verify rules, one constructor per row. -/
inductive R1lRule where
  | frameSize
  | envelope
  | token
  | signerChain
  | tokenSignature
  | tokenPolicy
  | notYetValid
  | notExpired
  | enrolment
  | inSession
  | messageSignature
  | fresh
  | knownKind
  | permission
  | payload
  | replay
  deriving DecidableEq, Repr

/-- The row's number in chapter 04; the table's order is this number's order. -/
def r1lRuleNumber : R1lRule → Nat
  | .frameSize => 1
  | .envelope => 2
  | .token => 3
  | .signerChain => 4
  | .tokenSignature => 5
  | .tokenPolicy => 6
  | .notYetValid => 7
  | .notExpired => 8
  | .enrolment => 9
  | .inSession => 10
  | .messageSignature => 11
  | .fresh => 12
  | .knownKind => 13
  | .permission => 14
  | .payload => 15
  | .replay => 16

/-- The rows in the table's order. -/
def r1lVerifyTable : List R1lRule :=
  [ .frameSize, .envelope, .token, .signerChain, .tokenSignature, .tokenPolicy, .notYetValid, .notExpired,
    .enrolment, .inSession, .messageSignature, .fresh, .knownKind, .permission, .payload, .replay ]

/-- The refusal reasons each row lists. -/
def r1lRuleReasons : R1lRule → List R1lReason
  | .frameSize => [.Oversize]
  | .envelope => [.Malformed, .UnknownVersion]
  | .token => [.Malformed, .UnknownVersion]
  | .signerChain => [.UntrustedTokenSigner, .CertificateExpired]
  | .tokenSignature => [.BadTokenSignature]
  | .tokenPolicy => [.TokenPolicy]
  | .notYetValid => [.TokenNotYetValid]
  | .notExpired => [.TokenExpired]
  | .enrolment => [.NotEnrolled, .Revoked, .EnrolmentMismatch]
  | .inSession => [.SessionMismatch]
  | .messageSignature => [.BadMessageSignature]
  | .fresh => [.Stale]
  | .knownKind => [.UnknownKind]
  | .permission => [.WrongPermission]
  | .payload => [.Malformed, .UnknownVersion]
  | .replay => [.Replayed, .ReplayCacheFull]

/-- A row's test on a frame `f`, arriving in session `sess`, at the verifier's time `now`, with memory `m`. The test holds when the
row passes. -/
def r1lFrameTest (v : R1lVerifier) (f : v.Frame) (sess : Option v.Subject) (now : Int) (m : v.Memory) : R1lRule → Prop
  | .frameSize => v.frameBytes f ≤ v.settings.maxFrameBytes
  | .envelope => v.envelopeParses f
  | .token => v.tokenParses f ∧ v.subjectKeyBinds f
  | .signerChain => v.signerChainValid now f
  | .tokenSignature => v.tokenSignatureValid f
  | .tokenPolicy =>
      v.expiresAt f - v.notBefore f ≤ (v.settings.tokenMaxLifetime : Int) ∧ v.notBefore f ≤ v.expiresAt f
  | .notYetValid => v.notBefore f - (v.settings.clockSkew : Int) ≤ now
  | .notExpired => now < v.expiresAt f
  | .enrolment =>
      v.enrolKnows now (v.subject f) ∧ ¬ v.enrolRevoked now (v.subject f) ∧
        v.enrolRole now (v.subject f) (v.role f) ∧ v.enrolKeyId now (v.subject f) (v.keyId f)
  | .inSession => ∀ s, sess = some s → v.subject f = s
  | .messageSignature => v.envelopeSignatureValid f
  | .fresh => now - (v.settings.clockSkew : Int) ≤ v.sentAt f ∧ v.sentAt f ≤ now + (v.settings.clockSkew : Int)
  | .knownKind => ∃ p, r1lNeeds (v.kind f) p
  | .permission => ∃ p, r1lNeeds (v.kind f) p ∧ v.tokenCarries f p
  | .payload => v.payloadParses f
  | .replay => ¬ v.remembers m now (v.keyId f) (v.msgId f) ∧ v.unexpired m now < v.settings.replayCapacity

/-- A row's test on an input. -/
def r1lTest (v : R1lVerifier) (i : v.Input) (now : Int) (m : v.Memory) (k : R1lRule) : Prop :=
  r1lFrameTest v (v.frame i) (v.session i) now m k

/-! ## The laws -/

/-- What C2.L1 demands of a verified input, one field per phrase of the sentence. -/
structure R1lAuthorised (v : R1lVerifier) (i : v.Input) (now : Int) : Prop where
  /-- "a token signed by a trusted token signer" -/
  signedByTrustedSigner : v.signerChainValid now (v.frame i) ∧ v.tokenSignatureValid (v.frame i)
  /-- "names an enrolled ... subject" -/
  subjectEnrolled : v.enrolKnows now (v.subject (v.frame i))
  /-- "names an ... unrevoked subject" -/
  subjectUnrevoked : ¬ v.enrolRevoked now (v.subject (v.frame i))
  /-- "whose key signed the message" -/
  subjectKeySigned :
    v.enrolKeyId now (v.subject (v.frame i)) (v.keyId (v.frame i)) ∧ v.subjectKeyBinds (v.frame i) ∧
      v.envelopeSignatureValid (v.frame i)
  /-- "the token is inside its validity window" -/
  insideValidityWindow :
    v.notBefore (v.frame i) - (v.settings.clockSkew : Int) ≤ now ∧ now < v.expiresAt (v.frame i)
  /-- "it carries the permission the kind needs" -/
  carriesNeededPermission : ∃ p, r1lNeeds (v.kind (v.frame i)) p ∧ v.tokenCarries (v.frame i) p

/-- C2.L1, quoted: "C2.L1 Verified means authorised. A message is verified only if a token signed by a trusted token signer names
an enrolled, unrevoked subject whose key signed the message, the token is inside its validity window, and it carries the permission
the kind needs." -/
def r1l_L1 (v : R1lVerifier) : Prop :=
  ∀ (i : v.Input) (now : Int) (m : v.Memory),
    (v.verify i now m).1 = R1lAnswer.verified → R1lAuthorised v i now

/-- C2.L2, quoted: "C2.L2 One answer, fail closed. Every input gets exactly one answer. The answer is the first row of chapter 04's
table whose test fails, in the table's order, with that row's reason, and such a refusal leaves the replay memory as it was; when no
row of rules 1 to 15 fails, only rule 16 (replay) may still refuse."

"Exactly one answer" is the shape of `verify` (a total function). The first conjunct: when row `k` fails and every row before it
passes, the answer is a refusal with one of row `k`'s reasons and the memory after is the memory before. The second conjunct: when
no row fails, the answer is verified. -/
def r1l_L2 (v : R1lVerifier) : Prop :=
  (∀ (i : v.Input) (now : Int) (m : v.Memory) (k : R1lRule),
      ¬ r1lTest v i now m k →
      (∀ j : R1lRule, r1lRuleNumber j < r1lRuleNumber k → r1lTest v i now m j) →
      ∃ r, r ∈ r1lRuleReasons k ∧ v.verify i now m = (R1lAnswer.refused r, m)) ∧
  (∀ (i : v.Input) (now : Int) (m : v.Memory),
      (∀ k : R1lRule, r1lTest v i now m k) → (v.verify i now m).1 = R1lAnswer.verified)

/-- C2.L3, quoted: "C2.L3 No replay. Through one verifier, the same signed message is verified at most once: after it was
verified, the same message is refused at any later or earlier time with the memory the first answer left." -/
def r1l_L3 (v : R1lVerifier) : Prop :=
  ∀ (i : v.Input) (now : Int) (m : v.Memory),
    (v.verify i now m).1 = R1lAnswer.verified →
    ∀ (i' : v.Input) (t' : Int), v.frame i' = v.frame i →
      (v.verify i' t' (v.verify i now m).2).1 ≠ R1lAnswer.verified

/-- C2.L3b, quoted: "C2.L3b A full replay memory refuses. When the verifier already remembers as many unexpired messages as
milkyway.im.replayCapacity allows, nothing verifies: it never forgets early to make room." -/
def r1l_L3b (v : R1lVerifier) : Prop :=
  ∀ (i : v.Input) (now : Int) (m : v.Memory),
    v.settings.replayCapacity ≤ v.unexpired m now →
      (v.verify i now m).1 ≠ R1lAnswer.verified ∧
      ∀ (kid : v.KeyId) (mid : v.MsgId), v.remembers m now kid mid → v.remembers (v.verify i now m).2 now kid mid

/-- C2.L4, quoted: "C2.L4 The permission table. A heartbeat needs im.heartbeat.send, and no other kind exists in IM version 0." -/
def r1l_L4 (v : R1lVerifier) : Prop :=
  ∀ (i : v.Input) (now : Int) (m : v.Memory),
    (v.verify i now m).1 = R1lAnswer.verified →
      v.kind (v.frame i) = "im.heartbeat" ∧ v.tokenCarries (v.frame i) "im.heartbeat.send"

/-! ## Sanity theorems -/

/-- The verify table lists the rows 1 to 16 in order. -/
theorem r1l_verify_table_order :
    r1lVerifyTable.map r1lRuleNumber = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16] := rfl

/-- The table of kinds: a heartbeat needs `im.heartbeat.send`. -/
theorem r1l_needs_heartbeat : r1lNeeds "im.heartbeat" "im.heartbeat.send" :=
  Or.inr (Or.inl ⟨rfl, rfl⟩)

/-- The table of kinds: no other message kind exists, and the heartbeat needs nothing else. -/
theorem r1l_needs_only_heartbeat (k p : String) (h : r1lNeeds k p) :
    k = "im.heartbeat" ∧ p = "im.heartbeat.send" := by
  have h' : (R1lTableKind.handshake = R1lTableKind.message k ∧ "im.session.open" = p) ∨
      ((R1lTableKind.message "im.heartbeat" = R1lTableKind.message k ∧ "im.heartbeat.send" = p) ∨ False) := h
  rcases h' with ⟨hk, _⟩ | ⟨hk, hp⟩ | hf
  · cases hk
  · cases hk
    exact ⟨rfl, hp.symm⟩
  · exact hf.elim

/-- A refusal is not a verified answer. -/
theorem r1l_refused_ne_verified (r : R1lReason) : R1lAnswer.refused r ≠ R1lAnswer.verified := by
  intro h
  cases h

/-- Under C2.L2, when rows 1 to 15 pass, the answer is verified or a refusal by rule 16 that leaves the memory as it was: the
sentence's last clause, derived. -/
theorem r1l_L2_only_replay_after_fifteen (v : R1lVerifier) (h : r1l_L2 v) (i : v.Input) (now : Int) (m : v.Memory)
    (h15 : ∀ k : R1lRule, k ≠ R1lRule.replay → r1lTest v i now m k) :
    (v.verify i now m).1 = R1lAnswer.verified ∨
      ∃ r, r ∈ r1lRuleReasons R1lRule.replay ∧ v.verify i now m = (R1lAnswer.refused r, m) := by
  rcases Classical.em (r1lTest v i now m R1lRule.replay) with h16 | h16
  · left
    apply h.2 i now m
    intro k
    rcases Classical.em (k = R1lRule.replay) with hk | hk
    · subst hk
      exact h16
    · exact h15 k hk
  · right
    apply h.1 i now m R1lRule.replay h16
    intro j hj
    apply h15 j
    intro hjr
    subst hjr
    exact absurd hj (Nat.lt_irrefl _)

/-- Under C2.L2, an oversize frame is refused `Oversize` and the memory is left as it was. -/
theorem r1l_L2_oversize (v : R1lVerifier) (h : r1l_L2 v) (i : v.Input) (now : Int) (m : v.Memory)
    (hbig : v.settings.maxFrameBytes < v.frameBytes (v.frame i)) :
    v.verify i now m = (R1lAnswer.refused R1lReason.Oversize, m) := by
  have hfail : ¬ r1lTest v i now m R1lRule.frameSize := by
    intro hle
    have hle' : v.frameBytes (v.frame i) ≤ v.settings.maxFrameBytes := hle
    exact Nat.lt_irrefl _ (Nat.lt_of_lt_of_le hbig hle')
  have hfirst : ∀ j : R1lRule, r1lRuleNumber j < r1lRuleNumber R1lRule.frameSize → r1lTest v i now m j := by
    intro j hj
    cases j <;> simp [r1lRuleNumber] at hj
  obtain ⟨r, hr, heq⟩ := h.1 i now m R1lRule.frameSize hfail hfirst
  have hr' : r = R1lReason.Oversize := by simpa [r1lRuleReasons] using hr
  subst hr'
  exact heq

/-- Under C2.L2, a verified input passes every row. -/
theorem r1l_L2_verified_passes_every_row (v : R1lVerifier) (h : r1l_L2 v) (i : v.Input) (now : Int) (m : v.Memory)
    (hv : (v.verify i now m).1 = R1lAnswer.verified) : ∀ k : R1lRule, r1lTest v i now m k := by
  have key : ∀ n : Nat, ∀ k : R1lRule, r1lRuleNumber k < n → r1lTest v i now m k := by
    intro n
    induction n with
    | zero =>
      intro k hk
      exact absurd hk (Nat.not_lt_zero _)
    | succ n ih =>
      intro k hk
      rcases Nat.lt_or_eq_of_le (Nat.le_of_lt_succ hk) with hlt | heq
      · exact ih k hlt
      · rcases Classical.em (r1lTest v i now m k) with ht | hf
        · exact ht
        · have hfirst : ∀ j : R1lRule, r1lRuleNumber j < r1lRuleNumber k → r1lTest v i now m j := by
            intro j hj
            apply ih j
            rw [← heq]
            exact hj
          obtain ⟨r, _, hr⟩ := h.1 i now m k hf hfirst
          rw [hr] at hv
          exact absurd hv (r1l_refused_ne_verified r)
  intro k
  apply key 17
  cases k <;> simp [r1lRuleNumber]

/-- Consistency of the readings: C2.L2 (with the rows as modelled) gives C2.L1. -/
theorem r1l_L2_gives_L1 (v : R1lVerifier) (h : r1l_L2 v) : r1l_L1 v := by
  intro i now m hv
  have t := r1l_L2_verified_passes_every_row v h i now m hv
  have h3 : v.tokenParses (v.frame i) ∧ v.subjectKeyBinds (v.frame i) := t R1lRule.token
  have h4 : v.signerChainValid now (v.frame i) := t R1lRule.signerChain
  have h5 : v.tokenSignatureValid (v.frame i) := t R1lRule.tokenSignature
  have h7 : v.notBefore (v.frame i) - (v.settings.clockSkew : Int) ≤ now := t R1lRule.notYetValid
  have h8 : now < v.expiresAt (v.frame i) := t R1lRule.notExpired
  have h9 : v.enrolKnows now (v.subject (v.frame i)) ∧ ¬ v.enrolRevoked now (v.subject (v.frame i)) ∧
      v.enrolRole now (v.subject (v.frame i)) (v.role (v.frame i)) ∧
      v.enrolKeyId now (v.subject (v.frame i)) (v.keyId (v.frame i)) := t R1lRule.enrolment
  have h11 : v.envelopeSignatureValid (v.frame i) := t R1lRule.messageSignature
  have h14 : ∃ p, r1lNeeds (v.kind (v.frame i)) p ∧ v.tokenCarries (v.frame i) p := t R1lRule.permission
  obtain ⟨hKnows, hUnrevoked, _, hKeyId⟩ := h9
  exact
    { signedByTrustedSigner := ⟨h4, h5⟩
      subjectEnrolled := hKnows
      subjectUnrevoked := hUnrevoked
      subjectKeySigned := ⟨hKeyId, h3.2, h11⟩
      insideValidityWindow := ⟨h7, h8⟩
      carriesNeededPermission := h14 }

/-- Consistency of the readings: C2.L1 gives C2.L4 (the only kind with a row is the heartbeat). -/
theorem r1l_L1_gives_L4 (v : R1lVerifier) (h : r1l_L1 v) : r1l_L4 v := by
  intro i now m hv
  have ha : R1lAuthorised v i now := h i now m hv
  obtain ⟨p, hn, hc⟩ := ha.carriesNeededPermission
  obtain ⟨hk, hp⟩ := r1l_needs_only_heartbeat _ _ hn
  subst hp
  exact ⟨hk, hc⟩

/-- Consistency of the readings: C2.L2 gives C2.L4. -/
theorem r1l_L2_gives_L4 (v : R1lVerifier) (h : r1l_L2 v) : r1l_L4 v :=
  r1l_L1_gives_L4 v (r1l_L2_gives_L1 v h)

/-- The verifier that refuses everything and keeps its memory: built from any interface by replacing `verify`. -/
def r1lAlwaysRefuse (v : R1lVerifier) : R1lVerifier :=
  { v with verify := fun _ _ m => (R1lAnswer.refused R1lReason.Malformed, m) }

/-- Refusing everything satisfies C2.L1 (it only constrains verified answers). -/
theorem r1l_alwaysRefuse_L1 (v : R1lVerifier) : r1l_L1 (r1lAlwaysRefuse v) := by
  intro i now m hv
  exact absurd hv (r1l_refused_ne_verified R1lReason.Malformed)

/-- Refusing everything satisfies C2.L3. -/
theorem r1l_alwaysRefuse_L3 (v : R1lVerifier) : r1l_L3 (r1lAlwaysRefuse v) := by
  intro i now m hv
  exact absurd hv (r1l_refused_ne_verified R1lReason.Malformed)

/-- Refusing everything satisfies C2.L3b: it never verifies and keeps every remembered pair. -/
theorem r1l_alwaysRefuse_L3b (v : R1lVerifier) : r1l_L3b (r1lAlwaysRefuse v) := by
  intro i now m _
  exact ⟨r1l_refused_ne_verified R1lReason.Malformed, fun _ _ hrem => hrem⟩

/-- Refusing everything satisfies C2.L4. -/
theorem r1l_alwaysRefuse_L4 (v : R1lVerifier) : r1l_L4 (r1lAlwaysRefuse v) := by
  intro i now m hv
  exact absurd hv (r1l_refused_ne_verified R1lReason.Malformed)

end Dmp
