/-
R1 step 1: a Lean 4 model of the Milky Way IM verifier, written BLIND from the C# alone.

Written from these C# files and nothing derived from any other model, spec text or proof of the same verifier:
  CM.MilkyWay/src/Verify/ImVerifier.cs   ImVerifier.Verify, ImVerifier.Check, ImVerifier.CheckToken, VerifyResult, VerifiedMessage
  CM.MilkyWay/src/Verify/ReplayMemory.cs ReplayMemory.TryRemember, ReplayOutcome
  CM.MilkyWay/src/Verify/ImRefusal.cs    ImRefusal (the 19 codes the verifier can return)
  CM.MilkyWay/src/Verify/RefusalLog.cs   IImRefusalLog (only counts; not modelled, see note E5)
  CM.MilkyWay/src/MilkyWaySettings.cs    ClockSkew, TokenMaxLifetime, MaxFrameBytes, MaxPayloadBytes, ReplayCapacity
  im: src/Im/ImEnvelope.cs, ImToken.cs, ImWire.cs, ImCrypto.cs   what the verifier calls (parsers, key and chain helpers, ImTime)
  im: src/Im/Trust/ChainVerdict.cs, Enrolment.cs, ITrustAnchors.cs, TrustAnchors.cs   the trust and enrolment answers

Shape. `r1mVerify facts now cfg mem` is one call of `ImVerifier.Verify(frame, sessionSubject)`. It follows `Check` rule by rule in the
C#'s order (rules 1 to 16, with rules 4 to 9 in `r1mCheckToken`), stops at the first refusal, and returns the answer (a refusal code or
the verified message) together with the replay memory after the call.

Abstractions (what the world computes outside the verifier comes in as facts, `R1mFacts`):
  A1  Every call into code outside the verifier is one fact field holding the answer the C# receives: either a value or the exception
      the call throws (`Except R1mFault _`). `R1mFault.format e` is an `ImFormatException` with code `e`; `R1mFault.other` is any other
      exception that the final `catch (Exception e) when (e is not OutOfMemoryException)` catches. The two catch blocks are
      `r1mFaultRefusal`: UnknownVersion -> unknownVersion, Malformed -> malformed, anything else -> malformed.
        envelope          ImEnvelopeCodec.Parse(frame, MaxPayloadBytes)          (rule 2; MaxPayloadBytes is used only here)
        token             ImTokenCodec.Parse(envelope.Token)                     (rule 3, including ImRoles.FromWire and the key checks)
        signerChain       ImChains.Load(token.SignerChain)                       (rule 4, first call)
        chainVerdict      trust.Validate(endEntity, intermediates, ImTokenSigner, now)   (rule 4)
        tokenSignature    ImKeys.Verify(ImKeys.SubjectPublicKeyInfo(endEntity), Input(Token, body), token.Signature)  (rule 5,
                          both calls in one fact: the export can throw, Verify answers a Bool or throws)
        enrolment         enrolment.Find(token.Subject)                          (rule 9); inside a record, `keyId` is the answer of
                          Convert.FromHexString(record.KeyId), which throws (FormatException, ArgumentNullException) on a bad string
        messageSignature  ImKeys.Verify(token.SubjectKey, Input(Message, body), envelope.Signature)   (rule 11)
        heartbeat         ImHeartbeat.Parse(envelope.Payload)                    (rule 15; read only when the kind is the heartbeat)
      The facts are not tied to each other (for example the token fact is not computed from the envelope's token bytes, and a parsed
      token's permission list need not be sorted). The model is therefore defined on a superset of what the world can produce; every
      decision the verifier makes is still the C#'s decision for those answers.
  A2  The frame itself appears only as its length (`frameLength`, rule 1); its bytes reach the verifier only through the facts.
  A3  The session subject (the optional second argument of Verify, rule 10) is a field of the facts.
  A4  Parsed values keep only the fields the verifier reads: the envelope's kind, message id and sent-at; the token's id, subject,
      role, subject key id, permissions, not-before and expires-at. Bodies, signatures, the subject key, the signer chain, the payload,
      the token's issued-at and nonce are consumed by the facts or never read. The enrolment record's own Subject is never read by the
      C# and is left out.
  A5  Time is Unix milliseconds. The verifier's clock reading `now` is a `Nat` (the value of ImTime.ToWire(time.GetUtcNow())). Values
      the C# reads from the wire (sent-at, not-before, expires-at) are signed 64-bit there and are `Int` here, because an attacker can
      send negative values. The settings in milliseconds are `Int` because the verifier never calls MilkyWaySettings.Validate, so a
      zero or negative setting reaches it.
  A6  `R1mConfig` holds the integers the C# derives from the settings: clockSkewMs = (long)ClockSkew.TotalMilliseconds,
      tokenMaxLifetimeMs = (Int128)TokenMaxLifetime.TotalMilliseconds (both truncate toward zero), maxFrameBytes, replayCapacity.
  A7  ReplayMemory's Dictionary plus PriorityQueue is one list of entries (scope, message id, expiry). The two C# structures stay in
      step: a key is enqueued only when it is absent from the dictionary and removed from the dictionary only when its queue element
      is dequeued, so each key has exactly one queue element with its own expiry and `current == expiry` always holds. The dequeue loop
      removes exactly the entries whose expiry is below `now`, whatever the queue's order among equal expiries. The list's order has
      no meaning. The C# key is hex(scope) + ":" + hex(id); hex is injective and ':' is not a hex digit, so two keys are equal exactly
      when both byte strings are equal, which is what the model compares.
  A8  `R1mChainRefusal`, `R1mRole` and the heartbeat status list the named enum values only.
  A9  CryptographicOperations.FixedTimeEquals(a, b) is byte-list equality (false on a length difference); its timing is not modelled.

Where the C# can behave differently from this model:
  E1  `now` is a DateTimeOffset in the C#: from year 1 to year 9999, so nowMs lies in [-62135596800000, 253402300799999]. The model
      has no reading before 1970 (Nat) and allows readings after year 9999, which have no C# counterpart.
  E2  long arithmetic. The C# computes nowMs - skew, nowMs + skew (rules 7 and 12) and SentAt + skew (rule 16) in unchecked 64-bit
      arithmetic; the model uses unbounded Int. Within the C#'s ranges none of them can wrap: |nowMs| < 2.6e14 and |skew| is at most
      TimeSpan's range (about 9.3e14 ms), and SentAt + skew is computed only after rule 12 has placed SentAt in [nowMs - skew,
      nowMs + skew] (which is empty for a negative skew, so rule 16 is reached only with skew >= 0). With a `now` beyond E1's range the
      C# has no counterpart anyway. Wire values in the model may also lie outside 64 bits; the C#'s parser can never produce those.
  E3  Int128 in rule 6 is exact for any two 64-bit times, as Int is. The (long) and (Int128) casts of the double TotalMilliseconds are
      folded into the config (A6): the model takes the integer the C# computes and does not model the double rounding.
  E4  Exceptions outside the facts. Not modelled: time.GetUtcNow() throwing (the C# would refuse Malformed; the model takes `now` as
      given); an exception from disposing the loaded signer chain at the end of CheckToken (`using`); OutOfMemoryException anywhere,
      which neither catch block catches, so the C# call throws instead of refusing; stack overflow and similar uncatchable failures.
      One exception path IS modelled exactly, because it is pure: after rule 16 has remembered the id, building the VerifiedMessage
      calls ImTime.FromWire(SentAt) = DateTimeOffset.FromUnixTimeMilliseconds, which throws ArgumentOutOfRangeException outside
      [-62135596800000, 253402300799999]; the final catch turns that into Malformed, and the replay memory keeps the new entry
      (`r1mFromWireOk`). This is reachable only with a clock skew large enough to carry SentAt past year 9999.
  E5  Verify's log. `log.Refused` and `log.Accepted` only count and are not modelled; if an injected log throws, the C# Verify throws
      after the memory has changed. Verify also throws InvalidOperationException for a verified kind other than the heartbeat; that is
      unreachable, because the kind table (rule 13) holds only the heartbeat, and the model leaves it out.
  E6  Concurrency. The model is one call at a time. The C# serialises TryRemember with a lock; the rest of Check reads no shared
      mutable state except the injected services, whose answers are facts.
  E7  Strings. .NET strings are UTF-16 and compared ordinally; Lean strings are Unicode scalar values. A session subject holding a lone
      surrogate cannot be written in Lean; in the C# it never equals a token subject (those are ASCII), the same answer as any other
      unequal string. The kinds and permissions come from the parser in a restricted ASCII alphabet.
  E8  Enum values outside the named ones (a cast integer from an injected trust or enrolment service) are not representable (A8). In
      the C# an unnamed ChainRefusal behaves as an untrusted signer and an unnamed record role as a role mismatch.
  E9  The replay capacity is read once, when the ImVerifier is constructed; the model reads it from the config on every call. They
      agree because the settings are init-only.
  E10 Rules 4 to 9 pass the full-precision DateTimeOffset `now` to trust.Validate; the model folds that into the chainVerdict fact.

Lean 4 core only, no Mathlib, no escape hatch. Every top-level name starts with r1m / R1m. The sanity theorems at the end are small
checks of the model, not laws.
-/

namespace Dmp

/-! ## Basic types -/

/-- Raw bytes (a byte[] or span in the C#). -/
abbrev R1mBytes := List UInt8

/-- `Im.ImFormatError`. -/
inductive R1mFormatError where
  | malformed
  | unknownVersion
  deriving DecidableEq, Repr

/-- An exception a call outside the verifier throws. `format e` is an `ImFormatException` with code `e`; `other` is every other
exception the final catch of `Check` catches (everything but OutOfMemoryException, which is out of the model, note E4). -/
inductive R1mFault where
  | format (e : R1mFormatError)
  | other
  deriving DecidableEq, Repr

/-- `Im.Trust.EnrolledRole`. -/
inductive R1mRole where
  | runner
  | devVm
  | test
  | build
  | laptop
  deriving DecidableEq, Repr

/-- `Im.Trust.ChainRefusal`; `accepted` is the C# value `None` (the chain was accepted). -/
inductive R1mChainRefusal where
  | accepted
  | untrustedRoot
  | expired
  | notYetValid
  | wrongRole
  | revoked
  | malformed
  deriving DecidableEq, Repr

/-- `CM.MilkyWay.Verify.ImRefusal`, the codes `Check` can return (rules 1 to 16 in order). The handshake, door and transport codes of
the C# enum are never returned by the verifier and are left out. -/
inductive R1mRefusal where
  | oversize
  | malformed
  | unknownVersion
  | untrustedTokenSigner
  | certificateExpired
  | badTokenSignature
  | tokenPolicy
  | tokenNotYetValid
  | tokenExpired
  | notEnrolled
  | revoked
  | enrolmentMismatch
  | sessionMismatch
  | badMessageSignature
  | stale
  | unknownKind
  | wrongPermission
  | replayed
  | replayCacheFull
  deriving DecidableEq, Repr

/-- `Im.ImHeartbeatStatus`. -/
inductive R1mHeartbeatStatus where
  | healthy
  | degraded
  | failing
  deriving DecidableEq, Repr

/-- `Im.ImHeartbeat`, the parsed heartbeat payload. `sequence` is a ulong in the C#. -/
structure R1mHeartbeat where
  sequence : Nat
  status : R1mHeartbeatStatus
  agentVersion : String
  deriving DecidableEq, Repr

/-- The fields of a parsed `ImEnvelope` the verifier reads (abstraction A4). `sentAt` is the sender's clock, Unix milliseconds. -/
structure R1mEnvelope where
  kind : String
  messageId : R1mBytes
  sentAt : Int
  deriving DecidableEq, Repr

/-- The fields of a parsed `ImToken` the verifier reads (abstraction A4). Times are Unix milliseconds. -/
structure R1mToken where
  tokenId : R1mBytes
  subject : String
  role : R1mRole
  subjectKeyId : R1mBytes
  permissions : List String
  notBefore : Int
  expiresAt : Int
  deriving DecidableEq, Repr

/-- One `EnrolmentRecord` as the verifier reads it. `keyId` is the answer of `Convert.FromHexString(record.KeyId)`: the decoded bytes,
or the exception a string that is not hex (or null) makes it throw. -/
structure R1mEnrolmentRecord where
  role : R1mRole
  revoked : Bool
  keyId : Except R1mFault R1mBytes

/-- The answers the C# receives from everything outside the verifier, one field per call (abstraction A1), plus the frame's length and
the session subject argument (A2, A3). A field is read only when the C# makes that call. -/
structure R1mFacts where
  /-- `signedEnvelope.Length` (rule 1). -/
  frameLength : Nat
  /-- The `sessionSubject` argument of Verify (rule 10); `none` outside a gate session. -/
  sessionSubject : Option String
  /-- `ImEnvelopeCodec.Parse(signedEnvelope, settings.MaxPayloadBytes)` (rule 2). -/
  envelope : Except R1mFault R1mEnvelope
  /-- `ImTokenCodec.Parse(envelope.Token)` (rule 3). -/
  token : Except R1mFault R1mToken
  /-- `ImChains.Load(token.SignerChain)` (rule 4). Only whether it throws matters to the verifier. -/
  signerChain : Except R1mFault Unit
  /-- `trust.Validate(signer.EndEntity, signer.Intermediates, TrustRole.ImTokenSigner, now).Refusal` (rule 4). -/
  chainVerdict : Except R1mFault R1mChainRefusal
  /-- `ImKeys.Verify(ImKeys.SubjectPublicKeyInfo(signer.EndEntity), ImTags.Input(ImTags.Token, token.Body), token.Signature)` (rule 5). -/
  tokenSignature : Except R1mFault Bool
  /-- `enrolment.Find(token.Subject)` (rule 9); `none` is a null record. -/
  enrolment : Except R1mFault (Option R1mEnrolmentRecord)
  /-- `ImKeys.Verify(token.SubjectKey, ImTags.Input(ImTags.Message, envelope.Body), envelope.Signature)` (rule 11). -/
  messageSignature : Except R1mFault Bool
  /-- `ImHeartbeat.Parse(envelope.Payload)` (rule 15), read only when the kind is the heartbeat. -/
  heartbeat : Except R1mFault R1mHeartbeat

/-- The settings the verifier uses, as the integers the C# derives from them (abstraction A6). The C# verifier never calls
`MilkyWaySettings.Validate`, so any value may arrive. Defaults: 65536, 120000, 86400000, 100000. -/
structure R1mConfig where
  /-- `settings.MaxFrameBytes` (rule 1). -/
  maxFrameBytes : Int
  /-- `(long)settings.ClockSkew.TotalMilliseconds` (rules 7, 12, 16). -/
  clockSkewMs : Int
  /-- `(Int128)settings.TokenMaxLifetime.TotalMilliseconds` (rule 6). -/
  tokenMaxLifetimeMs : Int
  /-- `settings.ReplayCapacity`, the capacity the ReplayMemory was built with (note E9). -/
  replayCapacity : Int
  deriving DecidableEq, Repr

/-- The C#'s default settings, as `R1mConfig`. -/
def r1mDefaultConfig : R1mConfig :=
  { maxFrameBytes := 65536, clockSkewMs := 120000, tokenMaxLifetimeMs := 86400000, replayCapacity := 100000 }

/-! ## The replay memory (`ReplayMemory`) -/

/-- One remembered id: the C# key hex(scope) + ":" + hex(id), with its expiry (abstraction A7). -/
structure R1mEntry where
  scope : R1mBytes
  msgId : R1mBytes
  expiresAt : Int
  deriving DecidableEq, Repr

/-- The state of one `ReplayMemory`: the entries of its dictionary (the priority queue holds the same entries, A7). -/
structure R1mMemory where
  entries : List R1mEntry
  deriving DecidableEq, Repr

/-- A new `ReplayMemory`. -/
def r1mMemoryEmpty : R1mMemory := { entries := [] }

/-- `ReplayOutcome`; `fresh` is the C# value `New`. -/
inductive R1mReplayOutcome where
  | fresh
  | seen
  | full
  deriving DecidableEq, Repr

/-- The dequeue loop of `TryRemember`: every entry whose expiry is below `now` is forgotten; one at `now` or later stays. -/
def r1mPrune (now : Int) (mem : R1mMemory) : R1mMemory :=
  { entries := mem.entries.filter (fun e => !(decide (e.expiresAt < now))) }

/-- `expiries.ContainsKey(key)`. -/
def r1mContains (scope msgId : R1mBytes) (mem : R1mMemory) : Bool :=
  mem.entries.any (fun e => decide (e.scope = scope ∧ e.msgId = msgId))

/-- `ReplayMemory.TryRemember(scope, id, expiresAt, now)` with the memory's capacity, step by step: forget what expired, then Seen if
the key is there, then Full if the count has reached the capacity, else remember it. The pruning stays even when the answer is
Seen or Full. -/
def r1mTryRemember (capacity : Int) (scope msgId : R1mBytes) (expiresAt now : Int) (mem : R1mMemory) :
    R1mReplayOutcome × R1mMemory :=
  let pruned := r1mPrune now mem
  if r1mContains scope msgId pruned then (R1mReplayOutcome.seen, pruned)
  else if (pruned.entries.length : Int) ≥ capacity then (R1mReplayOutcome.full, pruned)
  else (R1mReplayOutcome.fresh, { entries := pruned.entries ++ [{ scope := scope, msgId := msgId, expiresAt := expiresAt }] })

/-! ## The answer -/

/-- `VerifiedMessage`. `subjectKeyId` and `tokenId` are the bytes behind the C#'s lower-case hex strings; `sentAt` is the value given
to `ImTime.FromWire`; `receivedAt` is the verifier's `now`. -/
structure R1mVerified where
  subject : String
  role : R1mRole
  subjectKeyId : R1mBytes
  tokenId : R1mBytes
  kind : String
  messageId : R1mBytes
  sentAt : Int
  receivedAt : Nat
  heartbeat : Option R1mHeartbeat
  deriving DecidableEq, Repr

/-- `VerifyResult`: a verified message or a refusal, never both. -/
inductive R1mAnswer where
  | refused (reason : R1mRefusal)
  | verified (message : R1mVerified)
  deriving DecidableEq, Repr

/-- One call's output: the answer and the replay memory after the call. -/
structure R1mResult where
  answer : R1mAnswer
  memory : R1mMemory
  deriving DecidableEq, Repr

/-- `VerifyResult.Refuse(reason)`, with the memory as it stands. -/
def r1mRefuse (reason : R1mRefusal) (mem : R1mMemory) : R1mResult :=
  { answer := R1mAnswer.refused reason, memory := mem }

/-- The two catch blocks of `Check`: an `ImFormatException` with UnknownVersion is UnknownVersion, any other caught exception is
Malformed. -/
def r1mFaultRefusal : R1mFault → R1mRefusal
  | R1mFault.format R1mFormatError.unknownVersion => R1mRefusal.unknownVersion
  | R1mFault.format R1mFormatError.malformed => R1mRefusal.malformed
  | R1mFault.other => R1mRefusal.malformed

/-! ## The tables and pure helpers the verifier uses -/

/-- `ImKinds.Heartbeat`. -/
def r1mHeartbeatKind : String := "im.heartbeat"

/-- `ImKinds.HeartbeatPermission`. -/
def r1mHeartbeatPermission : String := "im.heartbeat.send"

/-- `ImKinds.Required.TryGetValue(kind, out permission)` (ordinal): the one table from kind to permission. -/
def r1mRequired (kind : String) : Option String :=
  if kind = r1mHeartbeatKind then some r1mHeartbeatPermission else none

/-- Rule 10's test: `sessionSubject is not null && !string.Equals(sessionSubject, token.Subject, StringComparison.Ordinal)`. -/
def r1mSessionMismatch (session : Option String) (subject : String) : Bool :=
  match session with
  | some s => !(decide (s = subject))
  | none => false

/-- Whether `ImTime.FromWire(ms)` (DateTimeOffset.FromUnixTimeMilliseconds) returns; outside this range it throws
ArgumentOutOfRangeException (note E4). -/
def r1mFromWireOk (ms : Int) : Bool :=
  decide (-62135596800000 ≤ ms ∧ ms ≤ 253402300799999)

/-! ## The verifier (`ImVerifier`) -/

/-- `ImVerifier.CheckToken(token, now)`, rules 4 to 9 in order. `.ok none` is the C#'s `null` (the token passes), `.ok (some r)` a
refusal, `.error f` an exception that leaves CheckToken and is caught by `Check`. -/
def r1mCheckToken (f : R1mFacts) (tok : R1mToken) (now : Nat) (cfg : R1mConfig) : Except R1mFault (Option R1mRefusal) :=
  let nowMs : Int := now
  -- Rule 4: the token signer chains to a trusted root in the role ImTokenSigner.
  match f.signerChain with
  | .error e => .error e
  | .ok () =>
    match f.chainVerdict with
    | .error e => .error e
    | .ok verdict =>
      if verdict ≠ R1mChainRefusal.accepted then
        .ok (some (if verdict = R1mChainRefusal.expired ∨ verdict = R1mChainRefusal.notYetValid
          then R1mRefusal.certificateExpired else R1mRefusal.untrustedTokenSigner))
      else
        -- Rule 5: Doppler's signature.
        match f.tokenSignature with
        | .error e => .error e
        | .ok false => .ok (some R1mRefusal.badTokenSignature)
        | .ok true =>
          -- Rule 6: the token's own shape of time (Int128 in the C#, exact).
          if tok.expiresAt < tok.notBefore ∨ tok.expiresAt - tok.notBefore > cfg.tokenMaxLifetimeMs then
            .ok (some R1mRefusal.tokenPolicy)
          -- Rule 7: not before, with the skew.
          else if nowMs + cfg.clockSkewMs < tok.notBefore then .ok (some R1mRefusal.tokenNotYetValid)
          -- Rule 8: not after, no skew on expiry.
          else if nowMs ≥ tok.expiresAt then .ok (some R1mRefusal.tokenExpired)
          else
            -- Rule 9: Doppler's enrolment.
            match f.enrolment with
            | .error e => .error e
            | .ok none => .ok (some R1mRefusal.notEnrolled)
            | .ok (some record) =>
              if record.revoked then .ok (some R1mRefusal.revoked)
              -- `record.Role != token.Role || !FixedTimeEquals(FromHexString(record.KeyId), token.SubjectKeyId)`: the hex decoding
              -- runs only when the roles are equal.
              else if record.role ≠ tok.role then .ok (some R1mRefusal.enrolmentMismatch)
              else
                match record.keyId with
                | .error e => .error e
                | .ok keyId =>
                  if keyId ≠ tok.subjectKeyId then .ok (some R1mRefusal.enrolmentMismatch) else .ok none

/-- Rule 15: `envelope.Kind == ImKinds.Heartbeat ? ImHeartbeat.Parse(envelope.Payload) : null`. -/
def r1mParsePayload (f : R1mFacts) (kind : String) : Except R1mFault (Option R1mHeartbeat) :=
  if kind = r1mHeartbeatKind then
    match f.heartbeat with
    | .error e => .error e
    | .ok h => .ok (some h)
  else .ok none

/-- Rule 16 and the construction of the VerifiedMessage. The id is remembered until `sentAt + skew`. -/
def r1mRememberAndAnswer (env : R1mEnvelope) (tok : R1mToken) (heartbeat : Option R1mHeartbeat) (now : Nat) (cfg : R1mConfig)
    (mem : R1mMemory) : R1mResult :=
  let nowMs : Int := now
  match r1mTryRemember cfg.replayCapacity tok.subjectKeyId env.messageId (env.sentAt + cfg.clockSkewMs) nowMs mem with
  | (R1mReplayOutcome.seen, mem') => r1mRefuse R1mRefusal.replayed mem'
  | (R1mReplayOutcome.full, mem') => r1mRefuse R1mRefusal.replayCacheFull mem'
  | (R1mReplayOutcome.fresh, mem') =>
    -- `new VerifiedMessage(..., ImTime.FromWire(envelope.SentAt), now, heartbeat)`: FromWire can throw, caught as Malformed, and the
    -- id stays remembered.
    if r1mFromWireOk env.sentAt then
      { answer := R1mAnswer.verified
          { subject := tok.subject, role := tok.role, subjectKeyId := tok.subjectKeyId, tokenId := tok.tokenId, kind := env.kind,
            messageId := env.messageId, sentAt := env.sentAt, receivedAt := now, heartbeat := heartbeat },
        memory := mem' }
    else r1mRefuse (r1mFaultRefusal R1mFault.other) mem'

/-- Rules 10 to 16 of `Check`, after the token passed. -/
def r1mCheckMessage (f : R1mFacts) (env : R1mEnvelope) (tok : R1mToken) (now : Nat) (cfg : R1mConfig) (mem : R1mMemory) : R1mResult :=
  let nowMs : Int := now
  let skew := cfg.clockSkewMs
  -- Rule 10: inside a session, the session's subject.
  if r1mSessionMismatch f.sessionSubject tok.subject then r1mRefuse R1mRefusal.sessionMismatch mem
  else
    -- Rule 11: the sender's signature, with the key the token names.
    match f.messageSignature with
    | .error e => r1mRefuse (r1mFaultRefusal e) mem
    | .ok false => r1mRefuse R1mRefusal.badMessageSignature mem
    | .ok true =>
      -- Rule 12: fresh, both ways.
      if env.sentAt < nowMs - skew ∨ env.sentAt > nowMs + skew then r1mRefuse R1mRefusal.stale mem
      else
        -- Rule 13: a kind in the table.
        match r1mRequired env.kind with
        | none => r1mRefuse R1mRefusal.unknownKind mem
        | some permission =>
          -- Rule 14: the permission the kind needs (`token.HasPermission`, ordinal Contains).
          if !(List.elem permission tok.permissions) then r1mRefuse R1mRefusal.wrongPermission mem
          else
            -- Rule 15: the payload parses as its kind says.
            match r1mParsePayload f env.kind with
            | .error e => r1mRefuse (r1mFaultRefusal e) mem
            | .ok heartbeat =>
              -- Rule 16 and the answer.
              r1mRememberAndAnswer env tok heartbeat now cfg mem

/-- `ImVerifier.Check(signedEnvelope, sessionSubject)`, rules 1 to 16 in order inside its try block. Every exception a fact carries
goes to the catch blocks (`r1mFaultRefusal`), with the memory as it stands at the throw. -/
def r1mCheck (f : R1mFacts) (now : Nat) (cfg : R1mConfig) (mem : R1mMemory) : R1mResult :=
  -- Rule 1: size before anything is read.
  if (f.frameLength : Int) > cfg.maxFrameBytes then r1mRefuse R1mRefusal.oversize mem
  else
    -- Rule 2: the envelope parses strictly.
    match f.envelope with
    | .error e => r1mRefuse (r1mFaultRefusal e) mem
    | .ok env =>
      -- Rule 3: the token parses strictly.
      match f.token with
      | .error e => r1mRefuse (r1mFaultRefusal e) mem
      | .ok tok =>
        -- Rules 4 to 9: the token.
        match r1mCheckToken f tok now cfg with
        | .error e => r1mRefuse (r1mFaultRefusal e) mem
        | .ok (some reason) => r1mRefuse reason mem
        | .ok none => r1mCheckMessage f env tok now cfg mem

/-- `ImVerifier.Verify(signedEnvelope, sessionSubject)`: `Check`, then one count in the refusal log, which is not modelled (note E5).
`now` is the verifier's clock reading in Unix milliseconds, `mem` the replay memory before the call. -/
def r1mVerify (f : R1mFacts) (now : Nat) (cfg : R1mConfig) (mem : R1mMemory) : R1mResult :=
  r1mCheck f now cfg mem

/-! ## Sanity checks of the model (not laws) -/

/-- Rule 1: an oversize frame is refused, and the memory is untouched. -/
theorem r1m_oversize_refused (f : R1mFacts) (now : Nat) (cfg : R1mConfig) (mem : R1mMemory)
    (h : (f.frameLength : Int) > cfg.maxFrameBytes) :
    r1mVerify f now cfg mem = { answer := R1mAnswer.refused R1mRefusal.oversize, memory := mem } := by
  unfold r1mVerify r1mCheck
  exact if_pos h

/-- Rule 2: an envelope whose version byte is unknown is refused as UnknownVersion, not Malformed, and the memory is untouched. -/
theorem r1m_envelope_unknown_version (f : R1mFacts) (now : Nat) (cfg : R1mConfig) (mem : R1mMemory)
    (h1 : ¬ (f.frameLength : Int) > cfg.maxFrameBytes) (h2 : f.envelope = .error (R1mFault.format R1mFormatError.unknownVersion)) :
    r1mVerify f now cfg mem = { answer := R1mAnswer.refused R1mRefusal.unknownVersion, memory := mem } := by
  unfold r1mVerify r1mCheck
  rw [if_neg h1, h2] <;> rfl

/-- The replay memory: the same scope and id a second time, inside its expiry, is Seen. -/
theorem r1m_remember_twice_seen :
    (r1mTryRemember 1 [] [] 10 0 (r1mTryRemember 1 [] [] 10 0 r1mMemoryEmpty).2).1 = R1mReplayOutcome.seen := by
  decide

/-- The replay memory keeps an entry at its expiry (the C# forgets only `expiry < now`). -/
theorem r1m_remember_kept_at_expiry :
    (r1mTryRemember 1 [] [] 10 10 (r1mTryRemember 1 [] [] 10 0 r1mMemoryEmpty).2).1 = R1mReplayOutcome.seen := by
  decide

/-- The replay memory forgets an entry once its expiry is in the past, which also frees its place. -/
theorem r1m_remember_forgets_after_expiry :
    (r1mTryRemember 1 [] [] 10 11 (r1mTryRemember 1 [] [] 10 0 r1mMemoryEmpty).2).1 = R1mReplayOutcome.fresh := by
  decide

/-- A replay memory with no room answers Full (fail closed); with capacity 0 even the first id. -/
theorem r1m_remember_capacity_zero_full :
    (r1mTryRemember 0 [] [] 10 0 r1mMemoryEmpty).1 = R1mReplayOutcome.full := by
  decide

end Dmp
