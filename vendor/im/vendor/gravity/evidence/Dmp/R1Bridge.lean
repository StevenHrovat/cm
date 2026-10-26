import Dmp.R1Model
import Dmp.R1Laws

/-!
# R1 step 3: the bridge. The blind model (R1Model) as an instance of the blind laws' interface (R1Laws)

Written by the coordinating session after both blind files had landed, with both in view. Every top-level name starts with r1b.

## The instance

- An input is the model's facts (`R1mFacts`); its frame is the same facts without the session subject, so "the same signed message"
  (L3) is the same answers from every call outside the verifier, in any session.
- Time: the laws' time is an `Int` of milliseconds, the model's clock a `Nat` (note E1). The bridge runs the model for `now ≥ 0` and
  refuses a time before 1970 as Malformed with the memory unchanged; the theorems that need it say `0 ≤ now`.
- Settings: the laws' four values become the model's `R1mConfig` (`r1bConfig`); the laws cannot state a negative or zero setting,
  which the model allows.
- Doppler's enrolment: the laws read it as a function of time and subject (`enrolKnows` etc.), the model as one fact per input. The
  bridge takes the register as a world function `E` and hands the model `E now (token subject)` in place of the input's own enrolment
  fact; this is the C#'s `enrolment.Find(token.Subject)` at the verifier's time.
- The signer chain and the signature checks are read from the facts (the laws allow `signerChainValid` to depend on time; the model
  folds the time into the fact, E10). Rule 3's key binding is part of the model's token parse (A1), so `subjectKeyBinds` is "the
  token parses".
- The replay memory: `remembers m t k i` holds when an entry for (k, i) has `t ≤ expiry` (the model forgets only `expiry < t`), and
  `unexpired m t` counts the entries the model keeps at `t`.

## Results

- C2.L1, C2.L3, C2.L3b and C2.L4 hold for the model, for every input, time, memory, setting and register (`r1b_L1`, `r1b_L3`,
  `r1b_L3b`, `r1b_L4`).
- C2.L2 holds on clean inputs (`R1bClean`, `R1bCleanWorld`) at times from 1970 (`r1b_L2_clean`): rows 1 to 15 word for word, and when
  every row passes the message is verified. Row 16 refuses with one of its reasons, but leaves the memory pruned of expired entries,
  not as it was.
- The literal C2.L2 does not hold for the model; the witnesses give one concrete case for each place:
  W1 `r1bW_row16_prunes`: a rule 16 refusal prunes the memory first (the C#'s `TryRemember`). Gravity's L2 reads "such a refusal"
     as a refusal by rows 1 to 15 and agrees with the C#; the blind laws read it for every row. The sentence allows both.
  W2 `r1bW_throw_at_rule5`: a call that throws at rules 4, 5, 9 or 11 makes the C# answer Malformed, which those rows do not list
     (gravity.md: an assumption at rule 4, "outside the model" at 5, 9 and 11).
  W3 `r1bW_clock_end`: every row passes, but a sent-at after year 9999 makes `ImTime.FromWire` throw after rule 16 remembered the id;
     the C# answers Malformed. Gravity's model has no such case. Reachable only with a clock skew of thousands of years, which
     MilkyWaySettings.Validate allows (it has no upper bound).
  W4 `r1bW_before_1970`: the model's own range (E1), not a finding about the C#.
- Beyond the laws, `r1bW_clock_rollback_replays`: with a clock that goes back between calls, the same frame verifies twice through one
  verifier, because another message's rule 16 pruned its entry. L3 holds as worded ("with the memory the first answer left"); the
  headline "verified at most once" needs a clock that never goes back.
-/

namespace Dmp

/-! ## The world and the settings -/

/-- Doppler's enrolment register as a world function: what `enrolment.Find(subject)` answers at a time. -/
abbrev R1bEnrolment := Int → String → Except R1mFault (Option R1mEnrolmentRecord)

/-- The laws' settings as the model's config. -/
def r1bConfig (s : R1lSettings) : R1mConfig :=
  { maxFrameBytes := s.maxFrameBytes, clockSkewMs := s.clockSkew, tokenMaxLifetimeMs := s.tokenMaxLifetime,
    replayCapacity := s.replayCapacity }

/-! ## What the laws observe of a frame -/

/-- The frame of an input: the facts without the session subject. -/
def r1bFrame (f : R1mFacts) : R1mFacts := { f with sessionSubject := none }

def r1bSubject (f : R1mFacts) : String := match f.token with | .ok t => t.subject | .error _ => ""
def r1bRole (f : R1mFacts) : R1mRole := match f.token with | .ok t => t.role | .error _ => .runner
def r1bKeyId (f : R1mFacts) : R1mBytes := match f.token with | .ok t => t.subjectKeyId | .error _ => []
def r1bNotBefore (f : R1mFacts) : Int := match f.token with | .ok t => t.notBefore | .error _ => 0
def r1bExpiresAt (f : R1mFacts) : Int := match f.token with | .ok t => t.expiresAt | .error _ => 0
def r1bPerms (f : R1mFacts) : List String := match f.token with | .ok t => t.permissions | .error _ => []
def r1bSentAt (f : R1mFacts) : Int := match f.envelope with | .ok e => e.sentAt | .error _ => 0
def r1bMsgId (f : R1mFacts) : R1mBytes := match f.envelope with | .ok e => e.messageId | .error _ => []
def r1bKind (f : R1mFacts) : String := match f.envelope with | .ok e => e.kind | .error _ => ""

/-- The model's refusal codes as the laws' reasons (one to one, same names). -/
def r1bReason : R1mRefusal → R1lReason
  | .oversize => .Oversize
  | .malformed => .Malformed
  | .unknownVersion => .UnknownVersion
  | .untrustedTokenSigner => .UntrustedTokenSigner
  | .certificateExpired => .CertificateExpired
  | .badTokenSignature => .BadTokenSignature
  | .tokenPolicy => .TokenPolicy
  | .tokenNotYetValid => .TokenNotYetValid
  | .tokenExpired => .TokenExpired
  | .notEnrolled => .NotEnrolled
  | .revoked => .Revoked
  | .enrolmentMismatch => .EnrolmentMismatch
  | .sessionMismatch => .SessionMismatch
  | .badMessageSignature => .BadMessageSignature
  | .stale => .Stale
  | .unknownKind => .UnknownKind
  | .wrongPermission => .WrongPermission
  | .replayed => .Replayed
  | .replayCacheFull => .ReplayCacheFull

def r1bAnswer : R1mAnswer → R1lAnswer
  | .verified _ => .verified
  | .refused r => .refused (r1bReason r)

/-- The facts the model is run on: the input's, with the register's answer at `now` for the token's subject. -/
def r1bFacts (E : R1bEnrolment) (f : R1mFacts) (now : Int) : R1mFacts :=
  { f with enrolment := E now (r1bSubject f) }

/-- One call through the bridge. A time before 1970 has no model clock (E1) and is refused, memory unchanged. -/
def r1bRun (s : R1lSettings) (E : R1bEnrolment) (f : R1mFacts) (now : Int) (m : R1mMemory) : R1lAnswer × R1mMemory :=
  if 0 ≤ now then
    let r := r1mVerify (r1bFacts E f now) now.toNat (r1bConfig s) m
    (r1bAnswer r.answer, r.memory)
  else (.refused .Malformed, m)

/-- The model as a verifier of the laws' interface. -/
def r1bVerifier (s : R1lSettings) (E : R1bEnrolment) : R1lVerifier where
  Frame := R1mFacts
  Input := R1mFacts
  Subject := String
  Role := R1mRole
  KeyId := R1mBytes
  MsgId := R1mBytes
  Memory := R1mMemory
  settings := s
  frame := r1bFrame
  session := fun f => f.sessionSubject
  frameBytes := fun f => f.frameLength
  envelopeParses := fun f => ∃ e, f.envelope = .ok e
  tokenParses := fun f => ∃ t, f.token = .ok t
  subjectKeyBinds := fun f => ∃ t, f.token = .ok t
  subject := r1bSubject
  role := r1bRole
  keyId := r1bKeyId
  notBefore := r1bNotBefore
  expiresAt := r1bExpiresAt
  signerChainValid := fun _ f => f.signerChain = .ok () ∧ f.chainVerdict = .ok .accepted
  tokenSignatureValid := fun f => f.tokenSignature = .ok true
  tokenCarries := fun f p => p ∈ r1bPerms f
  envelopeSignatureValid := fun f => f.messageSignature = .ok true
  sentAt := r1bSentAt
  msgId := r1bMsgId
  kind := r1bKind
  payloadParses := fun f => r1bKind f = r1mHeartbeatKind → ∃ h, f.heartbeat = .ok h
  enrolKnows := fun t x => ∃ r, E t x = .ok (some r)
  enrolRevoked := fun t x => ∃ r, E t x = .ok (some r) ∧ r.revoked = true
  enrolRole := fun t x ro => ∃ r, E t x = .ok (some r) ∧ r.role = ro
  enrolKeyId := fun t x k => ∃ r, E t x = .ok (some r) ∧ r.keyId = .ok k
  remembers := fun m t k i => ∃ e ∈ m.entries, e.scope = k ∧ e.msgId = i ∧ t ≤ e.expiresAt
  unexpired := fun m t => (r1mPrune t m).entries.length
  verify := r1bRun s E

/-! ## The model, rule by rule (forward lemmas) -/

section forward
variable (f : R1mFacts) (tok : R1mToken) (env : R1mEnvelope) (now : Nat) (cfg : R1mConfig) (mem : R1mMemory)

theorem r1b_tok_chain_fault (e : R1mFault) (h : f.signerChain = .error e) : r1mCheckToken f tok now cfg = .error e := by
  simp [r1mCheckToken, h]

theorem r1b_tok_verdict_fault (e : R1mFault) (h1 : f.signerChain = .ok ()) (h : f.chainVerdict = .error e) :
    r1mCheckToken f tok now cfg = .error e := by
  simp [r1mCheckToken, h1, h]

theorem r1b_tok_verdict_refused (v : R1mChainRefusal) (h1 : f.signerChain = .ok ()) (h : f.chainVerdict = .ok v)
    (hv : v ≠ .accepted) :
    ∃ r, (r = R1mRefusal.certificateExpired ∨ r = R1mRefusal.untrustedTokenSigner) ∧ r1mCheckToken f tok now cfg = .ok (some r) := by
  by_cases hx : v = R1mChainRefusal.expired ∨ v = R1mChainRefusal.notYetValid
  · exact ⟨_, Or.inl rfl, by simp [r1mCheckToken, h1, h, hv, hx]⟩
  · exact ⟨_, Or.inr rfl, by simp [r1mCheckToken, h1, h, hv, hx]⟩

end forward

/-- Rule 16, verified: the pair was not in the pruned memory, there was room, and the memory after is the pruned memory plus the pair. -/
theorem r1b_remember_verified (env : R1mEnvelope) (tok : R1mToken) (hb : Option R1mHeartbeat) (now : Nat) (cfg : R1mConfig)
    (mem : R1mMemory) (msg : R1mVerified) (h : (r1mRememberAndAnswer env tok hb now cfg mem).answer = .verified msg) :
    r1mContains tok.subjectKeyId env.messageId (r1mPrune now mem) = false ∧
    ¬ (((r1mPrune now mem).entries.length : Int) ≥ cfg.replayCapacity) ∧
    (r1mRememberAndAnswer env tok hb now cfg mem).memory =
      { entries := (r1mPrune now mem).entries ++
          [{ scope := tok.subjectKeyId, msgId := env.messageId, expiresAt := env.sentAt + cfg.clockSkewMs }] } := by
  unfold r1mRememberAndAnswer r1mTryRemember at *
  simp only [] at *
  by_cases hc : r1mContains tok.subjectKeyId env.messageId (r1mPrune (↑now) mem) = true
  · simp [hc, r1mRefuse] at h
  · by_cases hf : ((r1mPrune (↑now) mem).entries.length : Int) ≥ cfg.replayCapacity
    · simp [hc, hf, r1mRefuse] at h
    · by_cases hw : r1mFromWireOk env.sentAt = true
      · simp [hc, hf, hw]
      · simp [hc, hf, hw, r1mRefuse] at h

/-- Rule 16 keeps every entry the pruning keeps, whatever it answers. -/
theorem r1b_remember_keeps (env : R1mEnvelope) (tok : R1mToken) (hb : Option R1mHeartbeat) (now : Nat) (cfg : R1mConfig)
    (mem : R1mMemory) (e : R1mEntry) (he : e ∈ (r1mPrune now mem).entries) :
    e ∈ (r1mRememberAndAnswer env tok hb now cfg mem).memory.entries := by
  unfold r1mRememberAndAnswer r1mTryRemember
  simp only []
  by_cases hc : r1mContains tok.subjectKeyId env.messageId (r1mPrune (↑now) mem) = true
  · simp [hc, r1mRefuse, he]
  · by_cases hf : ((r1mPrune (↑now) mem).entries.length : Int) ≥ cfg.replayCapacity
    · simp [hc, hf, r1mRefuse, he]
    · by_cases hw : r1mFromWireOk env.sentAt = true
      · simp [hc, hf, hw, he]
      · simp [hc, hf, hw, r1mRefuse, he]

/-- Rules 4 to 9 pass exactly as the C# lets them. -/
theorem r1b_token_passes (f : R1mFacts) (tok : R1mToken) (now : Nat) (cfg : R1mConfig)
    (h : r1mCheckToken f tok now cfg = .ok none) :
    f.signerChain = .ok () ∧ f.chainVerdict = .ok .accepted ∧ f.tokenSignature = .ok true ∧
    ¬ (tok.expiresAt < tok.notBefore ∨ tok.expiresAt - tok.notBefore > cfg.tokenMaxLifetimeMs) ∧
    ¬ ((now : Int) + cfg.clockSkewMs < tok.notBefore) ∧ ¬ ((now : Int) ≥ tok.expiresAt) ∧
    ∃ r, f.enrolment = .ok (some r) ∧ r.revoked = false ∧ r.role = tok.role ∧ r.keyId = .ok tok.subjectKeyId := by
  unfold r1mCheckToken at h
  simp only [] at h
  split at h
  · simp at h
  rename_i u hchain
  split at h
  · simp at h
  rename_i verdict hverdict
  split at h
  · simp at h
  rename_i hacc
  split at h
  · simp at h
  · simp at h
  rename_i htsig
  split at h
  · simp at h
  rename_i hpol
  split at h
  · simp at h
  rename_i hnbf
  split at h
  · simp at h
  rename_i hexp
  split at h
  · simp at h
  · simp at h
  rename_i r henr
  split at h
  · simp at h
  rename_i hrev
  split at h
  · simp at h
  rename_i hrole
  split at h
  · simp at h
  rename_i k hk
  split at h
  · simp at h
  rename_i hkey
  refine ⟨hchain, ?_, htsig, hpol, hnbf, hexp, r, henr, by simpa using hrev, by simpa using hrole, ?_⟩
  · cases verdict <;> simp_all
  · simp_all

/-- Every call either refuses with the memory as it was, or passes rules 1 to 15 and ends in rule 16. -/
theorem r1b_trace (f : R1mFacts) (now : Nat) (cfg : R1mConfig) (mem : R1mMemory) :
    (∃ r, r1mVerify f now cfg mem = r1mRefuse r mem) ∨
    (∃ env tok hb, f.envelope = .ok env ∧ f.token = .ok tok ∧ ¬ ((f.frameLength : Int) > cfg.maxFrameBytes) ∧
      r1mCheckToken f tok now cfg = .ok none ∧ r1mSessionMismatch f.sessionSubject tok.subject = false ∧
      f.messageSignature = .ok true ∧ ¬ (env.sentAt < (now : Int) - cfg.clockSkewMs ∨ env.sentAt > (now : Int) + cfg.clockSkewMs) ∧
      env.kind = r1mHeartbeatKind ∧ r1mHeartbeatPermission ∈ tok.permissions ∧ r1mParsePayload f env.kind = .ok hb ∧
      r1mVerify f now cfg mem = r1mRememberAndAnswer env tok hb now cfg mem) := by
  unfold r1mVerify r1mCheck r1mCheckMessage
  split
  · exact Or.inl ⟨_, rfl⟩
  split
  · exact Or.inl ⟨_, rfl⟩
  rename_i env henv
  split
  · exact Or.inl ⟨_, rfl⟩
  rename_i tok htok
  split
  · exact Or.inl ⟨_, rfl⟩
  · exact Or.inl ⟨_, rfl⟩
  rename_i hct
  simp only []
  split
  · exact Or.inl ⟨_, rfl⟩
  rename_i hsess
  split
  · exact Or.inl ⟨_, rfl⟩
  · exact Or.inl ⟨_, rfl⟩
  rename_i hms
  split
  · exact Or.inl ⟨_, rfl⟩
  rename_i hfresh
  split
  · exact Or.inl ⟨_, rfl⟩
  rename_i perm hreq
  split
  · exact Or.inl ⟨_, rfl⟩
  rename_i hperm
  split
  · exact Or.inl ⟨_, rfl⟩
  rename_i hb hpay
  have hk : env.kind = r1mHeartbeatKind ∧ perm = r1mHeartbeatPermission := by
    unfold r1mRequired at hreq
    split at hreq <;> simp_all
  have hp : r1mHeartbeatPermission ∈ tok.permissions := by
    rw [← hk.2]
    simpa using hperm
  exact Or.inr ⟨env, tok, hb, henv, htok, ‹_›, hct, by simpa using hsess, hms, hfresh, hk.1, hp, hpay, rfl⟩

/-! ## The bridge, verified answers -/

theorem r1b_answer_verified (a : R1mAnswer) (h : r1bAnswer a = .verified) : ∃ msg, a = .verified msg := by
  cases a with
  | verified msg => exact ⟨msg, rfl⟩
  | refused r => cases r <;> simp [r1bAnswer, r1bReason] at h

/-- Everything a verified answer through the bridge implies, in the laws' terms of time and settings. -/
theorem r1b_run_verified (s : R1lSettings) (E : R1bEnrolment) (i : R1mFacts) (now : Int) (m : R1mMemory)
    (hv : (r1bRun s E i now m).1 = .verified) :
    0 ≤ now ∧ ∃ env tok r, i.envelope = .ok env ∧ i.token = .ok tok ∧
      i.signerChain = .ok () ∧ i.chainVerdict = .ok .accepted ∧ i.tokenSignature = .ok true ∧
      tok.notBefore - (s.clockSkew : Int) ≤ now ∧ now < tok.expiresAt ∧
      E now tok.subject = .ok (some r) ∧ r.revoked = false ∧ r.role = tok.role ∧ r.keyId = .ok tok.subjectKeyId ∧
      i.messageSignature = .ok true ∧ now - (s.clockSkew : Int) ≤ env.sentAt ∧ env.sentAt ≤ now + (s.clockSkew : Int) ∧
      env.kind = r1mHeartbeatKind ∧ r1mHeartbeatPermission ∈ tok.permissions ∧
      r1mContains tok.subjectKeyId env.messageId (r1mPrune now m) = false ∧
      ((r1mPrune now m).entries.length : Int) < (s.replayCapacity : Int) ∧
      (r1bRun s E i now m).2 = { entries := (r1mPrune now m).entries ++
          [{ scope := tok.subjectKeyId, msgId := env.messageId, expiresAt := env.sentAt + (s.clockSkew : Int) }] } := by
  unfold r1bRun at *
  split at hv
  · rename_i hnow
    have hcast : ((now.toNat : Nat) : Int) = now := Int.toNat_of_nonneg hnow
    obtain ⟨msg, hmsg⟩ := r1b_answer_verified _ hv
    rcases r1b_trace (r1bFacts E i now) now.toNat (r1bConfig s) m with ⟨r, hr⟩ |
        ⟨env, tok, hb, henv, htok, _, hct, _, hms, hfresh, hkind, hperm, _, heq⟩
    · rw [hr] at hmsg
      simp [r1mRefuse] at hmsg
    · obtain ⟨hchain, hverd, htsig, _, hnbf, hexp, r, henr, hrev, hrole, hkey⟩ := r1b_token_passes _ _ _ _ hct
      rw [heq] at hmsg
      obtain ⟨hc, hf, hmem⟩ := r1b_remember_verified _ _ _ _ _ _ _ hmsg
      have htok' : i.token = .ok tok := htok
      have hsubj : r1bSubject i = tok.subject := by simp [r1bSubject, htok']
      have henr' : E now tok.subject = .ok (some r) := by
        have : (r1bFacts E i now).enrolment = E now (r1bSubject i) := rfl
        rw [← hsubj, ← this]
        exact henr
      simp only [hcast, r1bConfig] at hnbf hexp hfresh hc hf hmem
      refine ⟨hnow, env, tok, r, henv, htok', hchain, hverd, htsig, by omega, by omega, henr', hrev, hrole, hkey, hms,
        by omega, by omega, hkind, hperm, hc, by omega, ?_⟩
      rw [if_pos hnow]
      show (r1mVerify (r1bFacts E i now) now.toNat (r1bConfig s) m).memory = _
      rw [heq]
      exact hmem
  · simp at hv

/-! ## The laws -/

/-- C2.L1 holds for the model: every verified answer is authorised. -/
theorem r1b_L1 (s : R1lSettings) (E : R1bEnrolment) : r1l_L1 (r1bVerifier s E) := by
  intro i now m hv
  obtain ⟨_, env, tok, r, _, htok, hchain, hverd, htsig, hnbf, hexp, henr, hrev, _, hkey, hms, _, _, hkind, hperm, _⟩ :=
    r1b_run_verified s E i now m hv
  have hsubj : r1bSubject i = tok.subject := by simp [r1bSubject, htok]
  have hkid : r1bKeyId i = tok.subjectKeyId := by simp [r1bKeyId, htok]
  have hnb : r1bNotBefore i = tok.notBefore := by simp [r1bNotBefore, htok]
  have hex : r1bExpiresAt i = tok.expiresAt := by simp [r1bExpiresAt, htok]
  have hpm : r1bPerms i = tok.permissions := by simp [r1bPerms, htok]
  have hkd : r1bKind i = env.kind := by simp [r1bKind, ‹i.envelope = .ok env›]
  exact
    { signedByTrustedSigner := ⟨⟨hchain, hverd⟩, htsig⟩
      subjectEnrolled := ⟨r, by show E now (r1bSubject i) = _; rw [hsubj]; exact henr⟩
      subjectUnrevoked := by
        rintro ⟨r', hr', hrv⟩
        show False
        have : E now (r1bSubject i) = .ok (some r') := hr'
        rw [hsubj, henr] at this
        cases this
        simp_all
      subjectKeySigned := ⟨⟨r, by show E now (r1bSubject i) = _; rw [hsubj]; exact henr, by
          show r.keyId = .ok (r1bKeyId i); rw [hkid]; exact hkey⟩, ⟨tok, htok⟩, hms⟩
      insideValidityWindow := by
        show r1bNotBefore i - _ ≤ now ∧ now < r1bExpiresAt i
        rw [hnb, hex]; exact ⟨hnbf, hexp⟩
      carriesNeededPermission := ⟨r1mHeartbeatPermission, by
          show r1lNeeds (r1bKind i) _; rw [hkd, hkind]; exact r1l_needs_heartbeat, by
          show _ ∈ r1bPerms i; rw [hpm]; exact hperm⟩ }

/-- C2.L4 holds for the model (from L1 and the table, by the laws file's own theorem). -/
theorem r1b_L4 (s : R1lSettings) (E : R1bEnrolment) : r1l_L4 (r1bVerifier s E) :=
  r1l_L1_gives_L4 _ (r1b_L1 s E)

/-- An entry that has not expired at `t` survives the pruning at `t`, so the memory still contains its pair. -/
theorem r1b_contains_of_mem (mem : R1mMemory) (e : R1mEntry) (t : Int) (he : e ∈ mem.entries) (ht : t ≤ e.expiresAt) :
    r1mContains e.scope e.msgId (r1mPrune t mem) = true := by
  simp only [r1mContains, r1mPrune, List.any_eq_true, List.mem_filter]
  exact ⟨e, ⟨he, by simp; omega⟩, by simp⟩

/-- C2.L3 holds for the model: after a message verified, the same frame never verifies again with the memory the answer left, at
any time and in any session. -/
theorem r1b_L3 (s : R1lSettings) (E : R1bEnrolment) : r1l_L3 (r1bVerifier s E) := by
  intro i now m hv i' t' hfr hv'
  obtain ⟨_, env, tok, _, henv, htok, _, _, _, _, _, _, _, _, _, _, _, hsent, _, _, _, _, hmem⟩ :=
    r1b_run_verified s E i now m hv
  obtain ⟨_, env', tok', _, henv', htok', _, _, _, _, _, _, _, _, _, _, hsent', _, _, _, hc', _, _⟩ :=
    r1b_run_verified s E i' t' _ hv'
  have hfr' : r1bFrame i' = r1bFrame i := hfr
  have he : i'.envelope = i.envelope := by
    have h := congrArg R1mFacts.envelope hfr'
    exact h
  have ht : i'.token = i.token := by
    have h := congrArg R1mFacts.token hfr'
    exact h
  rw [he, henv] at henv'
  rw [ht, htok] at htok'
  cases henv'
  cases htok'
  have hin : ({ scope := tok.subjectKeyId, msgId := env.messageId, expiresAt := env.sentAt + (s.clockSkew : Int) } : R1mEntry) ∈
      ((r1bVerifier s E).verify i now m).2.entries := by
    show _ ∈ (r1bRun s E i now m).2.entries
    rw [hmem]
    simp
  have := r1b_contains_of_mem _ _ t' hin (by show t' ≤ env.sentAt + (s.clockSkew : Int); omega)
  simp_all

/-- Every call keeps every entry the pruning at its time keeps: a refusal before rule 16 keeps the whole memory, rule 16 keeps the
pruned memory. -/
theorem r1b_verify_keeps (f : R1mFacts) (now : Nat) (cfg : R1mConfig) (mem : R1mMemory) (e : R1mEntry)
    (he : e ∈ (r1mPrune now mem).entries) : e ∈ (r1mVerify f now cfg mem).memory.entries := by
  rcases r1b_trace f now cfg mem with ⟨r, hr⟩ | ⟨env, tok, hb, _, _, _, _, _, _, _, _, _, _, heq⟩
  · rw [hr]
    simp only [r1mRefuse]
    simp only [r1mPrune, List.mem_filter] at he
    exact he.1
  · rw [heq]
    exact r1b_remember_keeps env tok hb now cfg mem e he

/-- C2.L3b holds for the model: with a full memory nothing verifies, and nothing unexpired is forgotten. -/
theorem r1b_L3b (s : R1lSettings) (E : R1bEnrolment) : r1l_L3b (r1bVerifier s E) := by
  intro i now m hfull
  have hfull' : s.replayCapacity ≤ (r1mPrune now m).entries.length := hfull
  constructor
  · intro hv
    obtain ⟨_, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, hroom, _⟩ := r1b_run_verified s E i now m hv
    omega
  · rintro kid mid ⟨e, he, hk, hi, ht⟩
    refine ⟨e, ?_, hk, hi, ht⟩
    have hp : e ∈ (r1mPrune now m).entries := by
      simp only [r1mPrune, List.mem_filter]
      exact ⟨he, by simp; omega⟩
    show e ∈ (r1bRun s E i now m).2.entries
    unfold r1bRun
    split
    · rename_i hnow
      have hcast : ((now.toNat : Nat) : Int) = now := Int.toNat_of_nonneg hnow
      have := r1b_verify_keeps (r1bFacts E i now) now.toNat (r1bConfig s) m e (by rw [hcast]; exact hp)
      exact this
    · exact he

/-! ## C2.L2 on clean inputs -/

/-- An input on which no call outside the verifier throws outside rules 2, 3 and 15, and whose sent-at fits a DateTimeOffset. -/
def R1bClean (f : R1mFacts) : Prop :=
  f.signerChain = .ok () ∧ (∃ v, f.chainVerdict = .ok v) ∧ (∃ b, f.tokenSignature = .ok b) ∧ (∃ b, f.messageSignature = .ok b) ∧
  ∀ e, f.envelope = .ok e → r1mFromWireOk e.sentAt = true

/-- A register that never throws and whose key ids always decode. -/
def R1bCleanWorld (E : R1bEnrolment) : Prop :=
  ∀ t x, (∃ o, E t x = .ok o) ∧ ∀ r, E t x = .ok (some r) → ∃ k, r.keyId = .ok k

/-- Rules 4 to 9 on a clean input: the token passes, or the first failing row among 4 to 9 refuses with one of its reasons. -/
theorem r1b_token_rows (s : R1lSettings) (E : R1bEnrolment) (i : R1mFacts) (now : Int) (m : R1mMemory) (tok : R1mToken)
    (hcl : R1bClean i) (hE : R1bCleanWorld E) (hnow : 0 ≤ now) (htok : i.token = .ok tok) :
    (r1mCheckToken (r1bFacts E i now) tok now.toNat (r1bConfig s) = .ok none ∧
      ∀ j, 4 ≤ r1lRuleNumber j → r1lRuleNumber j ≤ 9 → r1lTest (r1bVerifier s E) i now m j) ∨
    ∃ k r, 4 ≤ r1lRuleNumber k ∧ r1lRuleNumber k ≤ 9 ∧ r1bReason r ∈ r1lRuleReasons k ∧
      ¬ r1lTest (r1bVerifier s E) i now m k ∧
      (∀ j, 4 ≤ r1lRuleNumber j → r1lRuleNumber j < r1lRuleNumber k → r1lTest (r1bVerifier s E) i now m j) ∧
      r1mCheckToken (r1bFacts E i now) tok now.toNat (r1bConfig s) = .ok (some r) := by
  have hcast : ((now.toNat : Nat) : Int) = now := Int.toNat_of_nonneg hnow
  obtain ⟨hchain, ⟨v, hv⟩, ⟨b, hb⟩, _, _⟩ := hcl
  have hsub : r1bSubject i = tok.subject := by simp [r1bSubject, htok]
  obtain ⟨⟨o, ho⟩, hkeys⟩ := hE now tok.subject
  have henr : (r1bFacts E i now).enrolment = .ok o := by
    show E now (r1bSubject i) = _
    rw [hsub]; exact ho
  have hnb : r1bNotBefore i = tok.notBefore := by simp [r1bNotBefore, htok]
  have hex : r1bExpiresAt i = tok.expiresAt := by simp [r1bExpiresAt, htok]
  have hro : r1bRole i = tok.role := by simp [r1bRole, htok]
  have hki : r1bKeyId i = tok.subjectKeyId := by simp [r1bKeyId, htok]
  have hchain' : (r1bFacts E i now).signerChain = .ok () := hchain
  have hv' : (r1bFacts E i now).chainVerdict = .ok v := hv
  have hb' : (r1bFacts E i now).tokenSignature = .ok b := hb
  have t4 : v = .accepted → r1lTest (r1bVerifier s E) i now m .signerChain := fun h => by subst h; exact ⟨hchain, hv⟩
  have t5 : b = true → r1lTest (r1bVerifier s E) i now m .tokenSignature := fun h => by subst h; exact hb
  -- rule 4
  by_cases h4 : v ≠ .accepted
  · obtain ⟨r, hr, heq⟩ := r1b_tok_verdict_refused (r1bFacts E i now) tok now.toNat (r1bConfig s) v hchain' hv' h4
    refine Or.inr ⟨.signerChain, r, by decide, by decide, by rcases hr with rfl | rfl <;> decide, ?_, ?_, heq⟩
    · rintro ⟨_, h⟩
      have h' : i.chainVerdict = .ok .accepted := h
      rw [hv] at h'
      cases h'
      exact h4 rfl
    · intro j h1 h2
      cases j <;> simp only [r1lRuleNumber] at h1 h2 <;> omega
  have h4 : v = .accepted := Decidable.of_not_not h4
  subst h4
  -- rule 5
  cases b with
  | false =>
    refine Or.inr ⟨.tokenSignature, .badTokenSignature, by decide, by decide, by decide, ?_, ?_, ?_⟩
    · intro h
      have h' : i.tokenSignature = .ok true := h
      rw [hb] at h'
      cases h'
    · intro j h1 h2
      cases j <;> simp only [r1lRuleNumber] at h1 h2 <;> first | omega | exact t4 rfl | exact t5 rfl | exact t6 | exact t7 | exact t8
    · simp [r1mCheckToken, hchain', hv', hb']
  | true =>
  -- rule 6
  by_cases h6 : tok.expiresAt < tok.notBefore ∨ tok.expiresAt - tok.notBefore > (s.tokenMaxLifetime : Int)
  · refine Or.inr ⟨.tokenPolicy, .tokenPolicy, by decide, by decide, by decide, ?_, ?_, ?_⟩
    · intro h
      have h' : r1bExpiresAt i - r1bNotBefore i ≤ (s.tokenMaxLifetime : Int) ∧ r1bNotBefore i ≤ r1bExpiresAt i := h
      rw [hnb, hex] at h'
      omega
    · intro j h1 h2
      cases j <;> simp only [r1lRuleNumber] at h1 h2 <;> first | omega | exact t4 rfl | exact t5 rfl | exact t6 | exact t7 | exact t8
    · simp [r1mCheckToken, hchain', hv', hb', r1bConfig, h6]
  have t6 : r1lTest (r1bVerifier s E) i now m .tokenPolicy := by
    show r1bExpiresAt i - r1bNotBefore i ≤ (s.tokenMaxLifetime : Int) ∧ r1bNotBefore i ≤ r1bExpiresAt i
    rw [hnb, hex]; omega
  -- rule 7
  by_cases h7 : now + (s.clockSkew : Int) < tok.notBefore
  · refine Or.inr ⟨.notYetValid, .tokenNotYetValid, by decide, by decide, by decide, ?_, ?_, ?_⟩
    · intro h
      have h' : r1bNotBefore i - (s.clockSkew : Int) ≤ now := h
      rw [hnb] at h'
      omega
    · intro j h1 h2
      cases j <;> simp only [r1lRuleNumber] at h1 h2 <;> first | omega | exact t4 rfl | exact t5 rfl | exact t6 | exact t7 | exact t8
    · simp [r1mCheckToken, hchain', hv', hb', r1bConfig, h6, hcast, h7]
  have t7 : r1lTest (r1bVerifier s E) i now m .notYetValid := by
    show r1bNotBefore i - (s.clockSkew : Int) ≤ now
    rw [hnb]; omega
  -- rule 8
  by_cases h8 : now ≥ tok.expiresAt
  · refine Or.inr ⟨.notExpired, .tokenExpired, by decide, by decide, by decide, ?_, ?_, ?_⟩
    · intro h
      have h' : now < r1bExpiresAt i := h
      rw [hex] at h'
      omega
    · intro j h1 h2
      cases j <;> simp only [r1lRuleNumber] at h1 h2 <;> first | omega | exact t4 rfl | exact t5 rfl | exact t6 | exact t7 | exact t8
    · simp [r1mCheckToken, hchain', hv', hb', r1bConfig, h6, hcast, h7, h8]
  have t8 : r1lTest (r1bVerifier s E) i now m .notExpired := by
    show now < r1bExpiresAt i
    rw [hex]; omega
  have pre9 : ∀ j, 4 ≤ r1lRuleNumber j → r1lRuleNumber j < r1lRuleNumber .enrolment →
      r1lTest (r1bVerifier s E) i now m j := by
    intro j h1 h2
    cases j <;> simp only [r1lRuleNumber] at h1 h2 <;> first | omega | exact t4 rfl | exact t5 rfl | exact t6 | exact t7 | exact t8
  have nt9 : ¬ r1lTest (r1bVerifier s E) i now m .enrolment ↔
      ¬ ((∃ r, E now tok.subject = .ok (some r)) ∧ ¬ (∃ r, E now tok.subject = .ok (some r) ∧ r.revoked = true) ∧
        (∃ r, E now tok.subject = .ok (some r) ∧ r.role = tok.role) ∧
        (∃ r, E now tok.subject = .ok (some r) ∧ r.keyId = .ok tok.subjectKeyId)) := by
    show ¬ ((∃ r, E now (r1bSubject i) = .ok (some r)) ∧ ¬ (∃ r, E now (r1bSubject i) = .ok (some r) ∧ r.revoked = true) ∧
        (∃ r, E now (r1bSubject i) = .ok (some r) ∧ r.role = r1bRole i) ∧
        (∃ r, E now (r1bSubject i) = .ok (some r) ∧ r.keyId = .ok (r1bKeyId i))) ↔ _
    rw [hsub, hro, hki]
  cases o with
  | none =>
    refine Or.inr ⟨.enrolment, .notEnrolled, by decide, by decide, by decide, ?_, pre9, by simp [r1mCheckToken, hchain', hv', hb', r1bConfig, h6, hcast, h7, h8, henr]⟩
    rw [nt9]
    rintro ⟨⟨r, hr⟩, _⟩
    rw [ho] at hr
    cases hr
  | some rec =>
    have ho' := ho
    by_cases hrev : rec.revoked = true
    · refine Or.inr ⟨.enrolment, .revoked, by decide, by decide, by decide, ?_, pre9, by simp [r1mCheckToken, hchain', hv', hb', r1bConfig, h6, hcast, h7, h8, henr, hrev]⟩
      rw [nt9]
      rintro ⟨_, hn, _⟩
      exact hn ⟨rec, ho, hrev⟩
    by_cases hrole : rec.role = tok.role
    · obtain ⟨k, hk⟩ := hkeys rec ho
      by_cases hkey : k = tok.subjectKeyId
      · left
        refine ⟨by simp [r1mCheckToken, hchain', hv', hb', r1bConfig, h6, hcast, h7, h8, henr, hrev, hrole, hk, hkey], ?_⟩
        have t9 : r1lTest (r1bVerifier s E) i now m .enrolment := by
          apply Classical.byContradiction
          intro hn
          have := (nt9.1 hn)
          apply this
          refine ⟨⟨rec, ho⟩, ?_, ⟨rec, ho, hrole⟩, ⟨rec, ho, by rw [hk, hkey]⟩⟩
          rintro ⟨r, hr, hrv⟩
          rw [ho] at hr
          cases hr
          exact hrev hrv
        intro j h1 h2
        cases j <;> simp only [r1lRuleNumber] at h1 h2 <;> first | omega | exact t4 rfl | exact t5 rfl | exact t6 | exact t7 | exact t8 | exact t9
      · refine Or.inr ⟨.enrolment, .enrolmentMismatch, by decide, by decide, by decide, ?_, pre9,
          by simp [r1mCheckToken, hchain', hv', hb', r1bConfig, h6, hcast, h7, h8, henr, hrev, hrole, hk, hkey]⟩
        rw [nt9]
        rintro ⟨_, _, _, ⟨r, hr, hrk⟩⟩
        rw [ho] at hr
        cases hr
        rw [hk] at hrk
        cases hrk
        exact hkey rfl
    · refine Or.inr ⟨.enrolment, .enrolmentMismatch, by decide, by decide, by decide, ?_, pre9,
        by simp [r1mCheckToken, hchain', hv', hb', r1bConfig, h6, hcast, h7, h8, henr, hrev, hrole]⟩
      rw [nt9]
      rintro ⟨_, _, ⟨r, hr, hrr⟩, _⟩
      rw [ho] at hr
      cases hr
      exact hrole hrr

/-- Rules 1 to 15 on a clean input from 1970 on: the first failing row refuses with one of its reasons and the memory as it was, or
every row 1 to 15 passes and the call ends in rule 16. -/
theorem r1b_rows (s : R1lSettings) (E : R1bEnrolment) (i : R1mFacts) (now : Int) (m : R1mMemory)
    (hcl : R1bClean i) (hE : R1bCleanWorld E) (hnow : 0 ≤ now) :
    (∃ k r, k ≠ .replay ∧ r ∈ r1lRuleReasons k ∧ ¬ r1lTest (r1bVerifier s E) i now m k ∧
      (∀ j, r1lRuleNumber j < r1lRuleNumber k → r1lTest (r1bVerifier s E) i now m j) ∧
      r1bRun s E i now m = (.refused r, m)) ∨
    (∃ env tok hb, (∀ k, k ≠ .replay → r1lTest (r1bVerifier s E) i now m k) ∧ i.envelope = .ok env ∧ i.token = .ok tok ∧
      r1bRun s E i now m = (r1bAnswer (r1mRememberAndAnswer env tok hb now.toNat (r1bConfig s) m).answer,
        (r1mRememberAndAnswer env tok hb now.toNat (r1bConfig s) m).memory)) := by
  have hcast : ((now.toNat : Nat) : Int) = now := Int.toNat_of_nonneg hnow
  have hrun : r1bRun s E i now m = (r1bAnswer (r1mVerify (r1bFacts E i now) now.toNat (r1bConfig s) m).answer,
      (r1mVerify (r1bFacts E i now) now.toNat (r1bConfig s) m).memory) := by simp [r1bRun, hnow]
  have hcl' := hcl
  obtain ⟨hchain, _, _, ⟨mb, hmb⟩, hwire⟩ := hcl'
  -- rule 1
  by_cases h1 : (i.frameLength : Int) > (s.maxFrameBytes : Int)
  · refine Or.inl ⟨.frameSize, .Oversize, by decide, by decide, ?_, ?_, ?_⟩
    · intro h
      have h' : i.frameLength ≤ s.maxFrameBytes := h
      omega
    · intro j hj
      cases j <;> simp only [r1lRuleNumber] at hj <;> omega
    · rw [hrun]
      simp [r1mVerify, r1mCheck, r1bFacts, r1bConfig, h1, r1mRefuse, r1bAnswer, r1bReason]
  have t1 : r1lTest (r1bVerifier s E) i now m .frameSize := by
    show i.frameLength ≤ s.maxFrameBytes
    omega
  -- rule 2
  cases he : i.envelope with
  | error e =>
    refine Or.inl ⟨.envelope, r1bReason (r1mFaultRefusal e), by decide, ?_, ?_, ?_, ?_⟩
    · rcases e with (_ | _) | _ <;> decide
    · rintro ⟨_, h⟩
      have h' : i.envelope = _ := h
      rw [he] at h'
      cases h'
    · intro j hj
      cases j <;> simp only [r1lRuleNumber] at hj <;> first | omega | exact t1
    · rw [hrun]
      simp [r1mVerify, r1mCheck, r1bFacts, r1bConfig, h1, he, r1mRefuse, r1bAnswer]
  | ok env =>
  have t2 : r1lTest (r1bVerifier s E) i now m .envelope := ⟨env, he⟩
  -- rule 3
  cases ht : i.token with
  | error e =>
    refine Or.inl ⟨.token, r1bReason (r1mFaultRefusal e), by decide, ?_, ?_, ?_, ?_⟩
    · rcases e with (_ | _) | _ <;> decide
    · rintro ⟨⟨_, h⟩, _⟩
      have h' : i.token = _ := h
      rw [ht] at h'
      cases h'
    · intro j hj
      cases j <;> simp only [r1lRuleNumber] at hj <;> first | omega | exact t1 | exact t2
    · rw [hrun]
      simp [r1mVerify, r1mCheck, r1bFacts, r1bConfig, h1, he, ht, r1mRefuse, r1bAnswer]
  | ok tok =>
  have t3 : r1lTest (r1bVerifier s E) i now m .token := ⟨⟨tok, ht⟩, ⟨tok, ht⟩⟩
  -- rules 4 to 9
  rcases r1b_token_rows s E i now m tok hcl hE hnow ht with ⟨hct, tTok⟩ | ⟨k, r, hk4, hk9, hr, hnk, hpre, hct⟩
  rotate_right
  · refine Or.inl ⟨k, r1bReason r, ?_, hr, hnk, ?_, ?_⟩
    · intro h; subst h; simp [r1lRuleNumber] at hk9
    · intro j hj
      by_cases hj4 : 4 ≤ r1lRuleNumber j
      · exact hpre j hj4 hj
      · cases j <;> simp only [r1lRuleNumber] at hj4 <;> first | omega | exact t1 | exact t2 | exact t3
    · have hct' := hct
      simp only [r1bFacts, r1bConfig, he, ht] at hct'
      rw [hrun]
      simp [r1mVerify, r1mCheck, r1bFacts, r1bConfig, h1, he, ht, r1mRefuse, r1bAnswer, hct']
  have hct' := hct
  simp only [r1bFacts, r1bConfig, he, ht] at hct'
  have hstage : r1mVerify (r1bFacts E i now) now.toNat (r1bConfig s) m =
      r1mCheckMessage (r1bFacts E i now) env tok now.toNat (r1bConfig s) m := by
    simp [r1mVerify, r1mCheck, r1bFacts, r1bConfig, h1, he, ht, hct']
  have t4 := tTok .signerChain (by decide) (by decide)
  have t5 := tTok .tokenSignature (by decide) (by decide)
  have t6 := tTok .tokenPolicy (by decide) (by decide)
  have t7 := tTok .notYetValid (by decide) (by decide)
  have t8 := tTok .notExpired (by decide) (by decide)
  have t9 := tTok .enrolment (by decide) (by decide)
  have hsub : r1bSubject i = tok.subject := by simp [r1bSubject, ht]
  -- rule 10
  by_cases h10 : r1mSessionMismatch i.sessionSubject tok.subject = true
  · refine Or.inl ⟨.inSession, .SessionMismatch, by decide, by decide, ?_, ?_, ?_⟩
    · intro h
      have h' : ∀ x, i.sessionSubject = some x → r1bSubject i = x := h
      rw [hsub] at h'
      unfold r1mSessionMismatch at h10
      cases hs : i.sessionSubject with
      | none => rw [hs] at h10; simp at h10
      | some x => rw [hs] at h10; simp at h10; exact h10 (h' x hs).symm
    · intro j hj
      cases j <;> simp only [r1lRuleNumber] at hj <;> first | omega | exact t1 | exact t2 | exact t3 | exact t4 | exact t5 | exact t6 | exact t7 | exact t8 | exact t9
    · rw [hrun]
      simp [r1mVerify, r1mCheck, r1mCheckMessage, r1bFacts, r1bConfig, h1, he, ht, r1mRefuse, r1bAnswer, r1bReason, hct', h10]
  have t10 : r1lTest (r1bVerifier s E) i now m .inSession := by
    intro x hx
    show r1bSubject i = x
    rw [hsub]
    have hx' : i.sessionSubject = some x := hx
    unfold r1mSessionMismatch at h10
    rw [hx'] at h10
    simp at h10
    exact h10.symm
  have h10' : r1mSessionMismatch i.sessionSubject tok.subject = false := by simpa using h10
  -- rule 11
  cases mb with
  | false =>
    refine Or.inl ⟨.messageSignature, .BadMessageSignature, by decide, by decide, ?_, ?_, ?_⟩
    · intro h
      have h' : i.messageSignature = .ok true := h
      rw [hmb] at h'
      cases h'
    · intro j hj
      cases j <;> simp only [r1lRuleNumber] at hj <;> first | omega | exact t1 | exact t2 | exact t3 | exact t4 | exact t5 | exact t6 | exact t7 | exact t8 | exact t9 | exact t10 | exact t11 | exact t12 | exact t13 | exact t14
    · rw [hrun, hstage]
      simp [r1mCheckMessage, r1bFacts, r1bConfig, r1mRefuse, r1bAnswer, r1bReason, h10', hcast, hmb]
  | true =>
  have t11 : r1lTest (r1bVerifier s E) i now m .messageSignature := hmb
  have hsa : r1bSentAt i = env.sentAt := by simp [r1bSentAt, he]
  have hkd : r1bKind i = env.kind := by simp [r1bKind, he]
  have hpm : r1bPerms i = tok.permissions := by simp [r1bPerms, ht]
  -- rule 12
  by_cases h12 : env.sentAt < now - (s.clockSkew : Int) ∨ env.sentAt > now + (s.clockSkew : Int)
  · refine Or.inl ⟨.fresh, .Stale, by decide, by decide, ?_, ?_, ?_⟩
    · intro h
      have h' : now - (s.clockSkew : Int) ≤ r1bSentAt i ∧ r1bSentAt i ≤ now + (s.clockSkew : Int) := h
      rw [hsa] at h'
      omega
    · intro j hj
      cases j <;> simp only [r1lRuleNumber] at hj <;> first | omega | exact t1 | exact t2 | exact t3 | exact t4 | exact t5 | exact t6 | exact t7 | exact t8 | exact t9 | exact t10 | exact t11 | exact t12 | exact t13 | exact t14
    · rw [hrun, hstage]
      simp [r1mCheckMessage, r1bFacts, r1bConfig, r1mRefuse, r1bAnswer, r1bReason, h10', hcast, hmb, h12]
  have t12 : r1lTest (r1bVerifier s E) i now m .fresh := by
    show now - (s.clockSkew : Int) ≤ r1bSentAt i ∧ r1bSentAt i ≤ now + (s.clockSkew : Int)
    rw [hsa]; omega
  -- rule 13
  by_cases h13 : env.kind ≠ r1mHeartbeatKind
  · refine Or.inl ⟨.knownKind, .UnknownKind, by decide, by decide, ?_, ?_, ?_⟩
    · rintro ⟨p, hp⟩
      have hp' : r1lNeeds (r1bKind i) p := hp
      rw [hkd] at hp'
      exact h13 (r1l_needs_only_heartbeat _ _ hp').1
    · intro j hj
      cases j <;> simp only [r1lRuleNumber] at hj <;> first | omega | exact t1 | exact t2 | exact t3 | exact t4 | exact t5 | exact t6 | exact t7 | exact t8 | exact t9 | exact t10 | exact t11 | exact t12 | exact t13 | exact t14
    · rw [hrun, hstage]
      simp [r1mCheckMessage, r1bFacts, r1bConfig, r1mRefuse, r1bAnswer, r1bReason, h10', hcast, hmb, h12, r1mRequired, h13]
  have h13 : env.kind = r1mHeartbeatKind := Decidable.of_not_not h13
  have t13 : r1lTest (r1bVerifier s E) i now m .knownKind := by
    refine ⟨r1mHeartbeatPermission, ?_⟩
    show r1lNeeds (r1bKind i) _
    rw [hkd, h13]
    exact r1l_needs_heartbeat
  -- rule 14
  by_cases h14 : r1mHeartbeatPermission ∉ tok.permissions
  · refine Or.inl ⟨.permission, .WrongPermission, by decide, by decide, ?_, ?_, ?_⟩
    · rintro ⟨p, hp, hc⟩
      have hp' : r1lNeeds (r1bKind i) p := hp
      have hc' : p ∈ r1bPerms i := hc
      rw [hkd] at hp'
      rw [hpm] at hc'
      have := (r1l_needs_only_heartbeat _ _ hp').2
      subst this
      exact h14 hc'
    · intro j hj
      cases j <;> simp only [r1lRuleNumber] at hj <;> first | omega | exact t1 | exact t2 | exact t3 | exact t4 | exact t5 | exact t6 | exact t7 | exact t8 | exact t9 | exact t10 | exact t11 | exact t12 | exact t13 | exact t14
    · rw [hrun, hstage]
      simp [r1mCheckMessage, r1bFacts, r1bConfig, r1mRefuse, r1bAnswer, r1bReason, h10', hcast, hmb, h12, r1mRequired, h13, h14]
  have h14 : r1mHeartbeatPermission ∈ tok.permissions := Decidable.of_not_not h14
  have t14 : r1lTest (r1bVerifier s E) i now m .permission := by
    refine ⟨r1mHeartbeatPermission, ?_, ?_⟩
    · show r1lNeeds (r1bKind i) _
      rw [hkd, h13]
      exact r1l_needs_heartbeat
    · show _ ∈ r1bPerms i
      rw [hpm]
      exact h14
  -- rule 15
  cases hh : i.heartbeat with
  | error e =>
    refine Or.inl ⟨.payload, r1bReason (r1mFaultRefusal e), by decide, ?_, ?_, ?_, ?_⟩
    · rcases e with (_ | _) | _ <;> decide
    · intro h
      have h' : r1bKind i = r1mHeartbeatKind → ∃ x, i.heartbeat = .ok x := h
      rw [hkd] at h'
      obtain ⟨x, hx⟩ := h' h13
      rw [hh] at hx
      cases hx
    · intro j hj
      cases j <;> simp only [r1lRuleNumber] at hj <;> first | omega | exact t1 | exact t2 | exact t3 | exact t4 | exact t5 | exact t6 | exact t7 | exact t8 | exact t9 | exact t10 | exact t11 | exact t12 | exact t13 | exact t14
    · rw [hrun, hstage]
      simp [r1mCheckMessage, r1bFacts, r1bConfig, r1mRefuse, r1bAnswer, r1bReason, h10', hcast, hmb, h12, r1mRequired, h13, h14, r1mParsePayload, hh]
  | ok hbv =>
  have t15 : r1lTest (r1bVerifier s E) i now m .payload := fun _ => ⟨hbv, hh⟩
  refine Or.inr ⟨env, tok, some hbv, ?_, rfl, rfl, ?_⟩
  · intro k hk
    cases k <;> first | exact t1 | exact t2 | exact t3 | exact t4 | exact t5 | exact t6 | exact t7 | exact t8 | exact t9 | exact t10 | exact t11 | exact t12 | exact t13 | exact t14 | exact t15 | exact absurd rfl hk
  · rw [hrun, hstage]
    simp [r1mCheckMessage, r1bFacts, r1bConfig, r1mRefuse, r1bAnswer, r1bReason, h10', hcast, hmb, h12, r1mRequired, h13, h14, r1mParsePayload, hh]

/-- The pruned memory contains a pair exactly when the memory remembers it unexpired. -/
theorem r1b_contains_iff (k i : R1mBytes) (t : Int) (mem : R1mMemory) :
    r1mContains k i (r1mPrune t mem) = true ↔ ∃ e ∈ mem.entries, e.scope = k ∧ e.msgId = i ∧ t ≤ e.expiresAt := by
  simp only [r1mContains, r1mPrune, List.any_eq_true, List.mem_filter, decide_eq_true_eq]
  constructor
  · rintro ⟨e, ⟨he, hx⟩, hk, hi⟩
    refine ⟨e, he, hk, hi, ?_⟩
    simp at hx
    omega
  · rintro ⟨e, he, hk, hi, ht⟩
    exact ⟨e, ⟨he, by simp; omega⟩, hk, hi⟩

/-- Rule 16 through the bridge, after rules 1 to 15 passed. -/
theorem r1b_row16 (s : R1lSettings) (E : R1bEnrolment) (i : R1mFacts) (now : Int) (m : R1mMemory) (env : R1mEnvelope)
    (tok : R1mToken) (hb : Option R1mHeartbeat) (hnow : 0 ≤ now) (he : i.envelope = .ok env) (ht : i.token = .ok tok)
    (hwire : r1mFromWireOk env.sentAt = true) :
    (¬ r1lTest (r1bVerifier s E) i now m .replay → ∃ r, r ∈ r1lRuleReasons .replay ∧
      (r1bAnswer (r1mRememberAndAnswer env tok hb now.toNat (r1bConfig s) m).answer,
        (r1mRememberAndAnswer env tok hb now.toNat (r1bConfig s) m).memory) = (.refused r, r1mPrune now m)) ∧
    (r1lTest (r1bVerifier s E) i now m .replay →
      r1bAnswer (r1mRememberAndAnswer env tok hb now.toNat (r1bConfig s) m).answer = .verified) := by
  have hcast : ((now.toNat : Nat) : Int) = now := Int.toNat_of_nonneg hnow
  have hkid : r1bKeyId i = tok.subjectKeyId := by simp [r1bKeyId, ht]
  have hmid : r1bMsgId i = env.messageId := by simp [r1bMsgId, he]
  have htest : r1lTest (r1bVerifier s E) i now m .replay ↔
      r1mContains tok.subjectKeyId env.messageId (r1mPrune now m) = false ∧
        (r1mPrune now m).entries.length < s.replayCapacity := by
    show (¬ (∃ e ∈ m.entries, e.scope = r1bKeyId i ∧ e.msgId = r1bMsgId i ∧ now ≤ e.expiresAt) ∧
      (r1mPrune now m).entries.length < s.replayCapacity) ↔ _
    rw [hkid, hmid, ← r1b_contains_iff]
    simp
  rw [htest]
  unfold r1mRememberAndAnswer r1mTryRemember
  simp only [hcast, r1bConfig]
  constructor
  · intro hn
    by_cases hc : r1mContains tok.subjectKeyId env.messageId (r1mPrune now m) = true
    · exact ⟨.Replayed, by decide, by simp [hc, r1mRefuse, r1bAnswer, r1bReason]⟩
    · have hc' : r1mContains tok.subjectKeyId env.messageId (r1mPrune now m) = false := by simpa using hc
      have hf : (r1mPrune now m).entries.length ≥ s.replayCapacity := Nat.not_lt.mp (fun h => hn ⟨hc', h⟩)
      have hf' : ((r1mPrune now m).entries.length : Int) ≥ (s.replayCapacity : Int) := by omega
      exact ⟨.ReplayCacheFull, by decide, by simp [hc, hf', r1mRefuse, r1bAnswer, r1bReason]⟩
  · rintro ⟨hc, hl⟩
    have hl' : ¬ ((r1mPrune now m).entries.length : Int) ≥ (s.replayCapacity : Int) := by omega
    simp [hc, hl', hwire, r1bAnswer]

theorem r1b_rule_number_inj (a b : R1lRule) (h : r1lRuleNumber a = r1lRuleNumber b) : a = b := by
  cases a <;> cases b <;> simp [r1lRuleNumber] at h <;> rfl

/-- C2.L2 for the model, on clean inputs from 1970 on: rows 1 to 15 word for word; row 16 refuses with one of its reasons and leaves
the memory pruned of what had expired (not as it was); when every row passes, the message is verified. -/
theorem r1b_L2_clean (s : R1lSettings) (E : R1bEnrolment) (hE : R1bCleanWorld E) :
    (∀ i now m k, R1bClean i → 0 ≤ now → k ≠ R1lRule.replay → ¬ r1lTest (r1bVerifier s E) i now m k →
      (∀ j, r1lRuleNumber j < r1lRuleNumber k → r1lTest (r1bVerifier s E) i now m j) →
      ∃ r, r ∈ r1lRuleReasons k ∧ (r1bVerifier s E).verify i now m = (.refused r, m)) ∧
    (∀ i now m, R1bClean i → 0 ≤ now → ¬ r1lTest (r1bVerifier s E) i now m .replay →
      (∀ j, r1lRuleNumber j < r1lRuleNumber .replay → r1lTest (r1bVerifier s E) i now m j) →
      ∃ r, r ∈ r1lRuleReasons .replay ∧ (r1bVerifier s E).verify i now m = (.refused r, r1mPrune now m)) ∧
    (∀ i now m, R1bClean i → 0 ≤ now → (∀ k, r1lTest (r1bVerifier s E) i now m k) →
      ((r1bVerifier s E).verify i now m).1 = .verified) := by
  refine ⟨?_, ?_, ?_⟩
  · intro i now m k hcl hnow hne hk hpre
    rcases r1b_rows s E i now m hcl hE hnow with ⟨k', r, _, hr, hnk', hpre', hrun⟩ | ⟨_, _, _, hall, _⟩
    · have hkk : k' = k := by
        apply r1b_rule_number_inj
        rcases Nat.lt_trichotomy (r1lRuleNumber k') (r1lRuleNumber k) with hlt | heq | hgt
        · exact absurd (hpre k' hlt) hnk'
        · exact heq
        · exact absurd (hpre' k hgt) hk
      subst hkk
      exact ⟨r, hr, hrun⟩
    · exact absurd (hall k hne) hk
  · intro i now m hcl hnow hk hpre
    rcases r1b_rows s E i now m hcl hE hnow with ⟨k', _, hne, _, hnk', _, _⟩ | ⟨env, tok, hb, _, he, ht, hrun⟩
    · have : r1lRuleNumber k' < r1lRuleNumber .replay := by
        cases k' <;> simp [r1lRuleNumber] at hne ⊢
      exact absurd (hpre k' this) hnk'
    · obtain ⟨_, _, _, _, hwire⟩ := hcl
      have hw := hwire env he
      obtain ⟨r, hr, heq⟩ := (r1b_row16 s E i now m env tok hb hnow he ht hw).1 hk
      exact ⟨r, hr, by show r1bRun s E i now m = _; rw [hrun]; exact heq⟩
  · intro i now m hcl hnow hall
    rcases r1b_rows s E i now m hcl hE hnow with ⟨k', _, _, _, hnk', _, _⟩ | ⟨env, tok, hb, _, he, ht, hrun⟩
    · exact absurd (hall k') hnk'
    · obtain ⟨_, _, _, _, hwire⟩ := hcl
      have hw := hwire env he
      show (r1bRun s E i now m).1 = _
      rw [hrun]
      exact (r1b_row16 s E i now m env tok hb hnow he ht hw).2 (hall .replay)

/-! ## Where the model and the literal C2.L2 part (witnesses)

One concrete world: settings 100 bytes, lifetime 1000 ms, skew 10 ms, capacity 5; one enrolled subject "a" with key id [1]; a
heartbeat frame with message id [7] whose token and sent-at times are parameters. -/

def r1bW_settings : R1lSettings := { maxFrameBytes := 100, tokenMaxLifetime := 1000, clockSkew := 10, replayCapacity := 5 }

def r1bW_record : R1mEnrolmentRecord := { role := .runner, revoked := false, keyId := .ok [1] }

def r1bW_world : R1bEnrolment := fun _ _ => .ok (some r1bW_record)

/-- A heartbeat that passes every row the C# checks, with token window `nb` to `ex` and sent-at `sa`. -/
def r1bW_frame (nb ex sa : Int) : R1mFacts :=
  { frameLength := 10, sessionSubject := none,
    envelope := .ok { kind := "im.heartbeat", messageId := [7], sentAt := sa },
    token := .ok { tokenId := [9], subject := "a", role := .runner, subjectKeyId := [1], permissions := ["im.heartbeat.send"],
                   notBefore := nb, expiresAt := ex },
    signerChain := .ok (), chainVerdict := .ok .accepted, tokenSignature := .ok true, enrolment := .ok none,
    messageSignature := .ok true, heartbeat := .ok { sequence := 1, status := .healthy, agentVersion := "1" } }

/-- The world is clean. -/
theorem r1bW_world_clean : R1bCleanWorld r1bW_world := by
  intro t x
  exact ⟨⟨_, rfl⟩, fun r hr => by cases hr; exact ⟨[1], rfl⟩⟩

/-- The witness frame passes rows 1 to 15 whenever its times fit the rules. -/
theorem r1bW_rows (nb ex sa now : Int) (m : R1mMemory) (h6 : ex - nb ≤ 1000 ∧ nb ≤ ex) (h7 : nb - 10 ≤ now)
    (h8 : now < ex) (h12 : now - 10 ≤ sa ∧ sa ≤ now + 10) (j : R1lRule) (hj : j ≠ .replay) :
    r1lTest (r1bVerifier r1bW_settings r1bW_world) (r1bW_frame nb ex sa) now m j := by
  cases j
  · show 10 ≤ 100; decide
  · exact ⟨_, rfl⟩
  · exact ⟨⟨_, rfl⟩, ⟨_, rfl⟩⟩
  · exact ⟨rfl, rfl⟩
  · exact (rfl : (Except.ok true : Except R1mFault Bool) = .ok true)
  · show ex - nb ≤ ((1000 : Nat) : Int) ∧ nb ≤ ex; omega
  · show nb - ((10 : Nat) : Int) ≤ now; omega
  · exact h8
  · refine ⟨⟨r1bW_record, rfl⟩, ?_, ⟨r1bW_record, rfl, rfl⟩, ⟨r1bW_record, rfl, rfl⟩⟩
    rintro ⟨r, hr, hrv⟩
    cases hr
    cases hrv
  · intro x hx; cases hx
  · exact (rfl : (Except.ok true : Except R1mFault Bool) = .ok true)
  · show now - ((10 : Nat) : Int) ≤ sa ∧ sa ≤ now + ((10 : Nat) : Int); omega
  · exact ⟨_, r1l_needs_heartbeat⟩
  · exact ⟨_, r1l_needs_heartbeat, List.mem_singleton_self _⟩
  · exact fun _ => ⟨_, rfl⟩
  · exact absurd rfl hj

/-- Sanity: the witness frame verifies with an empty memory. -/
theorem r1bW_verifies : (r1bRun r1bW_settings r1bW_world (r1bW_frame 0 1000 50) 50 r1mMemoryEmpty).1 = .verified := by
  decide

/-- Witness 1 (row 16's memory). A memory that remembers the frame's pair (until 60) and one entry that expired at 0. At 50 the
frame is refused as Replayed, but the memory after has lost the expired entry: the C#'s `TryRemember` prunes before it answers
Seen or Full. The literal L2 ("such a refusal leaves the replay memory as it was", read for every row) fails. -/
theorem r1bW_row16_prunes : ¬ r1l_L2 (r1bVerifier r1bW_settings r1bW_world) := by
  intro h
  let m : R1mMemory := { entries := [⟨[1], [7], 60⟩, ⟨[2], [2], 0⟩] }
  have hfail : ¬ r1lTest (r1bVerifier r1bW_settings r1bW_world) (r1bW_frame 0 1000 50) 50 m .replay := by
    rintro ⟨hn, _⟩
    exact hn ⟨⟨[1], [7], 60⟩, List.mem_cons_self .., rfl, rfl, by decide⟩
  have hpre : ∀ j, r1lRuleNumber j < r1lRuleNumber .replay →
      r1lTest (r1bVerifier r1bW_settings r1bW_world) (r1bW_frame 0 1000 50) 50 m j := by
    intro j hj
    exact r1bW_rows 0 1000 50 50 m (by decide) (by decide) (by decide) (by decide) j (by intro he; subst he; simp at hj)
  obtain ⟨r, _, heq⟩ := h.1 _ 50 m .replay hfail hpre
  have hm := congrArg Prod.snd heq
  have hne : (r1bRun r1bW_settings r1bW_world (r1bW_frame 0 1000 50) 50 m).2 ≠ m := by decide
  exact hne hm

/-- Witness 2 (a throw outside rules 2, 3 and 15). The token signature check throws (the C#'s `ImKeys.Verify` or the key export):
the C# and the model answer Malformed at rule 5's place, but rule 5 lists only BadTokenSignature. The same holds at rules 4, 9 and 11
(gravity.md records rule 4's case as an assumption and counts rules 5, 9 and 11 as "outside the model"). -/
theorem r1bW_throw_at_rule5 : ¬ r1l_L2 (r1bVerifier r1bW_settings r1bW_world) := by
  intro h
  let f : R1mFacts := { r1bW_frame 0 1000 50 with tokenSignature := .error .other }
  have hfail : ¬ r1lTest (r1bVerifier r1bW_settings r1bW_world) f 50 r1mMemoryEmpty .tokenSignature := by
    intro h5
    have h5' : f.tokenSignature = .ok true := h5
    cases h5'
  have hpre : ∀ j, r1lRuleNumber j < r1lRuleNumber .tokenSignature →
      r1lTest (r1bVerifier r1bW_settings r1bW_world) f 50 r1mMemoryEmpty j := by
    intro j hj
    cases j <;> simp [r1lRuleNumber] at hj
    · show 10 ≤ 100; decide
    · exact ⟨_, rfl⟩
    · exact ⟨⟨_, rfl⟩, ⟨_, rfl⟩⟩
    · exact ⟨rfl, rfl⟩
  obtain ⟨r, hr, heq⟩ := h.1 f 50 r1mMemoryEmpty .tokenSignature hfail hpre
  simp [r1lRuleReasons] at hr
  subst hr
  have hne : r1bRun r1bW_settings r1bW_world f 50 r1mMemoryEmpty ≠ (.refused .BadTokenSignature, r1mMemoryEmpty) := by decide
  exact hne heq

/-- Witness 3 (the clock's end). Every row passes, but the sent-at lies after year 9999, which `ImTime.FromWire` cannot hold: the
C# remembers the id, then throws while building the verified message and answers Malformed. Reachable only when now plus the
clock skew passes year 9999; MilkyWaySettings.Validate puts no upper bound on the skew. -/
theorem r1bW_clock_end : ¬ r1l_L2 (r1bVerifier r1bW_settings r1bW_world) := by
  intro h
  let M : Int := 253402300799999
  have hall : ∀ k, r1lTest (r1bVerifier r1bW_settings r1bW_world) (r1bW_frame (M - 500) (M + 500) (M + 5)) M r1mMemoryEmpty k := by
    intro k
    by_cases hk : k = .replay
    · subst hk
      refine ⟨?_, by decide⟩
      rintro ⟨e, he, _⟩
      cases he
    · exact r1bW_rows _ _ _ _ _ (by decide) (by decide) (by decide) (by decide) k hk
  have hv := h.2 _ M r1mMemoryEmpty hall
  have hne : (r1bRun r1bW_settings r1bW_world (r1bW_frame (M - 500) (M + 500) (M + 5)) M r1mMemoryEmpty).1 ≠ .verified := by
    decide
  exact hne hv

/-- Witness 4 (the bridge's own edge). A time before 1970 has no model clock (note E1); the bridge refuses it, so a frame whose rows
all pass at -1 ms is not verified. This is the model's range, not a finding about the C#. -/
theorem r1bW_before_1970 : ¬ r1l_L2 (r1bVerifier r1bW_settings r1bW_world) := by
  intro h
  have hall : ∀ k, r1lTest (r1bVerifier r1bW_settings r1bW_world) (r1bW_frame (-100) 100 (-1)) (-1) r1mMemoryEmpty k := by
    intro k
    by_cases hk : k = .replay
    · subst hk
      refine ⟨?_, by decide⟩
      rintro ⟨e, he, _⟩
      cases he
    · exact r1bW_rows _ _ _ _ _ (by decide) (by decide) (by decide) (by decide) k hk
  have hv := h.2 _ (-1) r1mMemoryEmpty hall
  have hne : (r1bRun r1bW_settings r1bW_world (r1bW_frame (-100) 100 (-1)) (-1) r1mMemoryEmpty).1 ≠ .verified := by decide
  exact hne hv

/-- Observation beyond the laws (not a failure of L3 as worded): through one verifier, the same frame verifies twice when the clock
goes back between calls. Frame X (sent 50) verifies at 50 and is remembered until 60. Another fresh frame verifies at 100, and its
rule 16 prunes X's entry (60 < 100). The clock is set back to 55: X is fresh again (rule 12) and no longer remembered (rule 16), so it
verifies a second time. L3 holds because its sentence fixes "the memory the first answer left"; the headline "verified at most
once" assumes a clock that never goes back. -/
theorem r1bW_clock_rollback_replays :
    let X := r1bW_frame 0 1000 50
    let Y : R1mFacts := { r1bW_frame 0 1000 100 with
      envelope := .ok { kind := "im.heartbeat", messageId := [8], sentAt := 100 } }
    let m1 := (r1bRun r1bW_settings r1bW_world X 50 r1mMemoryEmpty).2
    let m2 := (r1bRun r1bW_settings r1bW_world Y 100 m1).2
    (r1bRun r1bW_settings r1bW_world X 50 r1mMemoryEmpty).1 = .verified ∧
    (r1bRun r1bW_settings r1bW_world Y 100 m1).1 = .verified ∧
    (r1bRun r1bW_settings r1bW_world X 55 m2).1 = .verified := by
  decide

end Dmp
