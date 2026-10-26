# The IM verifier of DMP chapter 04 ("The verify rules"), as a model for Gravity.
#
# Written from the DMP text, not from the C# (CM.MilkyWay/src/Verify/ImVerifier.cs). It decides what the
# C# decides from the same facts. What the world computes outside the verifier arrives as facts: whether
# the frame is too long, what the strict parsers said, what the certificate chain check said, whether a
# signature verified, what Doppler's enrolment register holds. Those are the assumptions of the proof
# (CM.MilkyWay/docs/supernova/assumptions.md, A1 to A17). Times are Unix milliseconds as Nat.

import Base

# Facts from the world
# --------------------

# what a strict parser answered (rules 2, 3, 15)
type Parse is Data:
  POk{}
  PMalformed{}
  PUnknownVersion{}

# what the certificate chain check of the token signer answered (rule 4)
type Chain is Data:
  COk{}
  CExpired{}
  CUntrusted{}

# what Doppler's enrolment register says about the token's subject (rule 9)
type Enrol is Data:
  EOk{}
  ENotEnrolled{}
  ERevoked{}
  EMismatch{}

# rule 10: no session, or a session whose subject is the token's, or another one
type Session is Data:
  SNone{}
  SSame{}
  SOther{}

# the kinds of IM version 0; any other kind string is Unlisted
type MsgKind is Data:
  Heartbeat{}
  Unlisted{}

# permissions a token may carry; any other permission string is OtherPerm
type Perm is Data:
  SessionOpen{}
  HeartbeatSend{}
  OtherPerm{}

type Facts is Data:
  Facts{oversize: Bool, env: Parse, tok: Parse, chain: Chain, tok_sig: Bool, nbf: Nat, exp: Nat, enrol: Enrol, session: Session, msg_sig: Bool, sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, key: Nat, id: Nat}

# milkyway.im.tokenMaxLifetime, milkyway.im.clockSkew, milkyway.im.replayCapacity
type Cfg is Data:
  Cfg{max_life: Nat, skew: Nat, cap: Nat}

# Answers
# -------

# the refusal reasons of rules 1 to 16, in the order of the rules
type Refusal is Data:
  Oversize{}
  Malformed{}
  UnknownVersion{}
  UntrustedTokenSigner{}
  CertificateExpired{}
  BadTokenSignature{}
  TokenPolicy{}
  TokenNotYetValid{}
  TokenExpired{}
  NotEnrolled{}
  Revoked{}
  EnrolmentMismatch{}
  SessionMismatch{}
  BadMessageSignature{}
  Stale{}
  UnknownKind{}
  WrongPermission{}
  Replayed{}
  ReplayCacheFull{}

# one remembered message: the subject key id, the message id, and when it may be forgotten
type Entry is Data:
  Entry{key: Nat, id: Nat, until: Nat}

# the answer and the replay memory after it
type Out is Data:
  Verified{mem: List<&2, Entry>}
  Refused{why: Refusal, mem: List<&2, Entry>}

def is_verified(o: Out) -> Bool:
  match o:
    case Verified{m}:
      True{}
    case Refused{w, m}:
      False{}

def memory(o: Out) -> List<&2, Entry>:
  match o:
    case Verified{m}:
      m
    case Refused{w, m}:
      m

def verdict(o: Out) -> Maybe<&2, Refusal>:
  match o:
    case Verified{m}:
      None{}
    case Refused{w, m}:
      Some{w}

# The one table: kind to permission (DMP 04 "Message kinds and permissions")
# --------------------------------------------------------------------------

def need(k: MsgKind) -> Maybe<&2, Perm>:
  match k:
    case Heartbeat{}:
      Some{HeartbeatSend{}}
    case Unlisted{}:
      None{}

def perm_eq(a: Perm, b: Perm) -> Bool:
  match a b:
    case SessionOpen{} SessionOpen{}:
      True{}
    case HeartbeatSend{} HeartbeatSend{}:
      True{}
    case OtherPerm{} OtherPerm{}:
      True{}
    case _ _:
      False{}

def has(xs: List<&2, Perm>, +p: Perm) -> Bool:
  match xs:
    case Nil{}:
      False{}
    case h <> t:
      Bool.or(perm_eq(h, p), has(t, p))

# Time checks
# -----------

# rule 6: expires before it starts, or lives longer than the policy allows
def policy_bad(+nbf: Nat, +exp: Nat, max_life: Nat) -> Bool:
  Bool.or(Nat.is_lt(exp, nbf), Nat.is_lt(max_life, Nat.sub(exp, nbf)))

# rule 12: sent more than the skew before now, or more than the skew after now
def stale(+sent: Nat, +now: Nat, +skew: Nat) -> Bool:
  Bool.or(Nat.is_lt(Nat.add(sent, skew), now), Nat.is_lt(Nat.add(now, skew), sent))

# Rules 1 to 15: the first that fails, in order, or None
# ------------------------------------------------------
# Each step matches the fact it decides and hands the rest on; nothing skips a step. Bend calls only
# defs written above, so rule 15 comes first and rule 1, the entry, last.

def r15(payload: Parse) -> Maybe<&2, Refusal>:
  match payload:
    case POk{}:
      None{}
    case PMalformed{}:
      Some{Malformed{}}
    case PUnknownVersion{}:
      Some{UnknownVersion{}}

def r14(ok: Bool, payload: Parse) -> Maybe<&2, Refusal>:
  match ok:
    case True{}:
      r15(payload)
    case False{}:
      Some{WrongPermission{}}

def r13(p: Maybe<&2, Perm>, perms: List<&2, Perm>, payload: Parse) -> Maybe<&2, Refusal>:
  match p:
    case None{}:
      Some{UnknownKind{}}
    case Some{q}:
      r14(has(perms, q), payload)

def r12(bad: Bool, kind: MsgKind, perms: List<&2, Perm>, payload: Parse) -> Maybe<&2, Refusal>:
  match bad:
    case True{}:
      Some{Stale{}}
    case False{}:
      r13(need(kind), perms, payload)

def r11(msg_sig: Bool, +sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, +now: Nat, +skew: Nat) -> Maybe<&2, Refusal>:
  match msg_sig:
    case True{}:
      r12(stale(sent, now, skew), kind, perms, payload)
    case False{}:
      Some{BadMessageSignature{}}

def r10(session: Session, msg_sig: Bool, +sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, +now: Nat, +skew: Nat) -> Maybe<&2, Refusal>:
  match session:
    case SNone{}:
      r11(msg_sig, sent, kind, perms, payload, now, skew)
    case SSame{}:
      r11(msg_sig, sent, kind, perms, payload, now, skew)
    case SOther{}:
      Some{SessionMismatch{}}

def r9(enrol: Enrol, session: Session, msg_sig: Bool, +sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, +now: Nat, +skew: Nat) -> Maybe<&2, Refusal>:
  match enrol:
    case EOk{}:
      r10(session, msg_sig, sent, kind, perms, payload, now, skew)
    case ENotEnrolled{}:
      Some{NotEnrolled{}}
    case ERevoked{}:
      Some{Revoked{}}
    case EMismatch{}:
      Some{EnrolmentMismatch{}}

def r8(bad: Bool, enrol: Enrol, session: Session, msg_sig: Bool, +sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, +now: Nat, +skew: Nat) -> Maybe<&2, Refusal>:
  match bad:
    case True{}:
      Some{TokenExpired{}}
    case False{}:
      r9(enrol, session, msg_sig, sent, kind, perms, payload, now, skew)

def r7(bad: Bool, +exp: Nat, enrol: Enrol, session: Session, msg_sig: Bool, +sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, +now: Nat, +skew: Nat) -> Maybe<&2, Refusal>:
  match bad:
    case True{}:
      Some{TokenNotYetValid{}}
    case False{}:
      r8(Nat.is_le(exp, now), enrol, session, msg_sig, sent, kind, perms, payload, now, skew)

def r6(bad: Bool, +nbf: Nat, +exp: Nat, enrol: Enrol, session: Session, msg_sig: Bool, +sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, +now: Nat, +skew: Nat) -> Maybe<&2, Refusal>:
  match bad:
    case True{}:
      Some{TokenPolicy{}}
    case False{}:
      r7(Nat.is_lt(Nat.add(now, skew), nbf), exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew)

def r5(tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: Enrol, session: Session, msg_sig: Bool, +sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, +now: Nat, max_life: Nat, +skew: Nat) -> Maybe<&2, Refusal>:
  match tok_sig:
    case True{}:
      r6(policy_bad(nbf, exp, max_life), nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew)
    case False{}:
      Some{BadTokenSignature{}}

def r4(chain: Chain, tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: Enrol, session: Session, msg_sig: Bool, +sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, +now: Nat, max_life: Nat, +skew: Nat) -> Maybe<&2, Refusal>:
  match chain:
    case COk{}:
      r5(tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)
    case CExpired{}:
      Some{CertificateExpired{}}
    case CUntrusted{}:
      Some{UntrustedTokenSigner{}}

def r3(tok: Parse, chain: Chain, tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: Enrol, session: Session, msg_sig: Bool, +sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, +now: Nat, max_life: Nat, +skew: Nat) -> Maybe<&2, Refusal>:
  match tok:
    case POk{}:
      r4(chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)
    case PMalformed{}:
      Some{Malformed{}}
    case PUnknownVersion{}:
      Some{UnknownVersion{}}

def r2(env: Parse, tok: Parse, chain: Chain, tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: Enrol, session: Session, msg_sig: Bool, +sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, +now: Nat, max_life: Nat, +skew: Nat) -> Maybe<&2, Refusal>:
  match env:
    case POk{}:
      r3(tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)
    case PMalformed{}:
      Some{Malformed{}}
    case PUnknownVersion{}:
      Some{UnknownVersion{}}

def r1(oversize: Bool, env: Parse, tok: Parse, chain: Chain, tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: Enrol, session: Session, msg_sig: Bool, +sent: Nat, kind: MsgKind, perms: List<&2, Perm>, payload: Parse, +now: Nat, max_life: Nat, +skew: Nat) -> Maybe<&2, Refusal>:
  match oversize:
    case True{}:
      Some{Oversize{}}
    case False{}:
      r2(env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)

# Rule 16: the replay memory
# --------------------------
# Forget what has expired (until < now), then: seen means Replayed, full means ReplayCacheFull (fail
# closed, never forget early), otherwise remember the message until sent + skew.

def forget.keep(drop: Bool, +e: Entry, rest: List<&2, Entry>) -> List<&2, Entry>:
  match drop:
    case True{}:
      rest
    case False{}:
      e <> rest

def forget(mem: List<&2, Entry>, +now: Nat) -> List<&2, Entry>:
  match mem:
    case Nil{}:
      Nil{}
    case h <> t:
      match h:
        case Entry{+k, +i, +u}:
          forget.keep(Nat.is_lt(u, now), Entry{k, i, u}, forget(t, now))

def seen(mem: List<&2, Entry>, +key: Nat, +id: Nat) -> Bool:
  match mem:
    case Nil{}:
      False{}
    case h <> t:
      match h:
        case Entry{k, i, u}:
          Bool.or(Bool.and(Nat.is_eq(k, key), Nat.is_eq(i, id)), seen(t, key, id))

def replay.full(full: Bool, +m: List<&2, Entry>, key: Nat, id: Nat, until: Nat) -> Out:
  match full:
    case True{}:
      Refused{ReplayCacheFull{}, m}
    case False{}:
      Verified{Entry{key, id, until} <> m}

def replay.seen(hit: Bool, +m: List<&2, Entry>, key: Nat, id: Nat, until: Nat, +cap: Nat) -> Out:
  match hit:
    case True{}:
      Refused{Replayed{}, m}
    case False{}:
      replay.full(Nat.is_le(cap, List.length(&2, Entry, m)), m, key, id, until)

def replay.go(+m: List<&2, Entry>, +key: Nat, +id: Nat, until: Nat, cap: Nat) -> Out:
  replay.seen(seen(m, key, id), m, key, id, until, cap)

# The verifier
# ------------

def decide(r: Maybe<&2, Refusal>, mem: List<&2, Entry>, +key: Nat, +id: Nat, +sent: Nat, +now: Nat, +skew: Nat, cap: Nat) -> Out:
  match r:
    case Some{w}:
      Refused{w, mem}
    case None{}:
      replay.go(forget(mem, now), key, id, Nat.add(sent, skew), cap)

def verify(f: Facts, +now: Nat, cfg: Cfg, mem: List<&2, Entry>) -> Out:
  match f:
    case Facts{oversize, env, tok, chain, tok_sig, +nbf, +exp, enrol, session, msg_sig, +sent, kind, perms, payload, +key, +id}:
      match cfg:
        case Cfg{max_life, +skew, cap}:
          decide(r1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew), mem, key, id, sent, now, skew, cap)
