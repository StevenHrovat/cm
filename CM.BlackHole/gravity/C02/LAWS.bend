# DMP C2 · Every message verifies itself: the laws (Gravity slice G1).
#
# The owner states these; agents prove them in PROOF.bend and never edit this file. Each law quotes the
# sentence of C2's Law: block it formalises, and C2's "Laws file:" line holds this file's sha256 (rule
# DMP-018), so a change here is a change of the constitution. Words used below:
#   Holds(b)    the fact b is true
#   When(b, T)  if b is true, then T holds
# The model is ./model.bend, written from DMP chapter 04. What the world computes (parsers, certificate
# chains, signatures, the enrolment register) arrives as facts; those are the proof's assumptions.

import Base
import ./model.bend as M

def Holds(b: Bool) -> Type:
  match b:
    case True{}:
      Unit
    case False{}:
      Empty

def When(b: Bool, T: Type) -> Type:
  match b:
    case True{}:
      T
    case False{}:
      Unit

# The permission table, as approved (C2.L4): a heartbeat needs im.heartbeat.send; every other kind is unknown.
def SamePerm(a: M.Perm, b: M.Perm) -> Type:
  match a b:
    case M.SessionOpen{} M.SessionOpen{}:
      Unit
    case M.HeartbeatSend{} M.HeartbeatSend{}:
      Unit
    case M.OtherPerm{} M.OtherPerm{}:
      Unit
    case _ _:
      Empty

def Listed(xs: List<&2, M.Perm>, +p: M.Perm) -> Type:
  match xs:
    case Nil{}:
      Empty
    case h <> t:
      Or(SamePerm(h, p), Listed(t, p))

def Permitted(k: M.MsgKind, perms: List<&2, M.Perm>) -> Type:
  match k:
    case M.Heartbeat{}:
      Listed(perms, M.HeartbeatSend{})
    case M.Unlisted{}:
      Empty

# What "authorised" means in C2.L1, written from DMP, not from the model.
def SignedByTrustedSigner(c: M.Chain, sig: Bool) -> Type:
  match c:
    case M.COk{}:
      Holds(sig)
    case M.CExpired{}:
      Empty
    case M.CUntrusted{}:
      Empty

def EnrolledUnrevoked(e: M.Enrol) -> Type:
  match e:
    case M.EOk{}:
      Unit
    case M.ENotEnrolled{}:
      Empty
    case M.ERevoked{}:
      Empty
    case M.EMismatch{}:
      Empty

def Authorised(f: M.Facts, +now: Nat, cfg: M.Cfg) -> Type:
  match f:
    case M.Facts{oversize, env, tok, chain, tok_sig, +nbf, +exp, enrol, session, msg_sig, sent, kind, perms, payload, key, id}:
      match cfg:
        case M.Cfg{max_life, +skew, cap}:
          SignedByTrustedSigner(chain, tok_sig) & EnrolledUnrevoked(enrol) & Holds(msg_sig) & Holds(Nat.is_le(nbf, Nat.add(now, skew))) & Holds(Nat.is_lt(now, exp)) & Permitted(kind, perms)

# C2.L1 Verified means authorised. A message is verified only if a token signed by a trusted token signer
# names an enrolled, unrevoked subject whose key signed the message, the token is inside its validity
# window, and it carries the permission the kind needs.
law verified_means_authorised:
  for +f   : M.Facts
  for +now : Nat
  for +cfg : M.Cfg
  for  mem : List<&2, M.Entry>
  When(M.is_verified(M.verify(f, now, cfg, mem)), Authorised(f, now, cfg))

# C2.L3 No replay. Through one verifier, the same signed message is verified at most once: after it was
# verified, the same message is refused at any later or earlier time with the memory the first answer left.
law no_replay:
  for +f    : M.Facts
  for +now  : Nat
  for +now2 : Nat
  for +cfg  : M.Cfg
  for  mem  : List<&2, M.Entry>
  When(M.is_verified(M.verify(f, now, cfg, mem)), Holds(Bool.not(M.is_verified(M.verify(f, now2, cfg, M.memory(M.verify(f, now, cfg, mem)))))))

# C2.L4 The permission table. A heartbeat needs im.heartbeat.send, and no other kind exists in IM version 0.
law table_heartbeat:
  {M.need(M.Heartbeat{}) == Some{M.HeartbeatSend{}} : Maybe<&2, M.Perm>}

law table_nothing_else:
  {M.need(M.Unlisted{}) == None{} : Maybe<&2, M.Perm>}

# C2.L2 One answer, fail closed. Every input gets exactly one answer. The answer is the first row of
# chapter 04's table whose test fails, in the table's order, with that row's reason, and such a refusal
# leaves the replay memory as it was; when no row of rules 1 to 15 fails, only rule 16 (replay) may still
# refuse.
#
# "Exactly one answer" needs no statement of its own: Bend accepts only functions that answer every input.
# The memory clause is why a forged message cannot fill or poison the replay memory (mutant M3).
type Row is Data:
  Row{fails: Bool, why: M.Refusal}

def first(rows: List<&2, Row>) -> Maybe<&2, M.Refusal>:
  match rows:
    case Nil{}:
      None{}
    case r <> rest:
      match r:
        case Row{fails, why}:
          match fails:
            case True{}:
              Some{why}
            case False{}:
              first(rest)

def malformed(p: M.Parse) -> Bool:
  match p:
    case M.POk{}:
      False{}
    case M.PMalformed{}:
      True{}
    case M.PUnknownVersion{}:
      False{}

def unknown_version(p: M.Parse) -> Bool:
  match p:
    case M.POk{}:
      False{}
    case M.PMalformed{}:
      False{}
    case M.PUnknownVersion{}:
      True{}

def untrusted(c: M.Chain) -> Bool:
  match c:
    case M.COk{}:
      False{}
    case M.CExpired{}:
      False{}
    case M.CUntrusted{}:
      True{}

def cert_expired(c: M.Chain) -> Bool:
  match c:
    case M.COk{}:
      False{}
    case M.CExpired{}:
      True{}
    case M.CUntrusted{}:
      False{}

def not_enrolled(e: M.Enrol) -> Bool:
  match e:
    case M.EOk{}:
      False{}
    case M.ENotEnrolled{}:
      True{}
    case M.ERevoked{}:
      False{}
    case M.EMismatch{}:
      False{}

def revoked(e: M.Enrol) -> Bool:
  match e:
    case M.EOk{}:
      False{}
    case M.ENotEnrolled{}:
      False{}
    case M.ERevoked{}:
      True{}
    case M.EMismatch{}:
      False{}

def mismatched(e: M.Enrol) -> Bool:
  match e:
    case M.EOk{}:
      False{}
    case M.ENotEnrolled{}:
      False{}
    case M.ERevoked{}:
      False{}
    case M.EMismatch{}:
      True{}

def other_session(s: M.Session) -> Bool:
  match s:
    case M.SNone{}:
      False{}
    case M.SSame{}:
      False{}
    case M.SOther{}:
      True{}

def unlisted(k: M.MsgKind) -> Bool:
  match k:
    case M.Heartbeat{}:
      False{}
    case M.Unlisted{}:
      True{}

def same(a: M.Perm, b: M.Perm) -> Bool:
  match a b:
    case M.SessionOpen{} M.SessionOpen{}:
      True{}
    case M.HeartbeatSend{} M.HeartbeatSend{}:
      True{}
    case M.OtherPerm{} M.OtherPerm{}:
      True{}
    case _ _:
      False{}

def carries(xs: List<&2, M.Perm>, +p: M.Perm) -> Bool:
  match xs:
    case Nil{}:
      False{}
    case h <> t:
      Bool.or(same(h, p), carries(t, p))

# rule 14 for a listed kind: the token does not carry the permission the table names
def lacks(k: M.MsgKind, perms: List<&2, M.Perm>) -> Bool:
  match k:
    case M.Heartbeat{}:
      Bool.not(carries(perms, M.HeartbeatSend{}))
    case M.Unlisted{}:
      False{}

# Chapter 04 "The verify rules", rules 1 to 15, one row per (test fails, reason), in the chapter's order.
def Table(oversize: Bool, +env: M.Parse, +tok: M.Parse, +chain: M.Chain, tok_sig: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, session: M.Session, msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, max_life: Nat, +skew: Nat) -> List<&2, Row>:
  [Row{oversize, M.Oversize{}}, Row{malformed(env), M.Malformed{}}, Row{unknown_version(env), M.UnknownVersion{}}, Row{malformed(tok), M.Malformed{}}, Row{unknown_version(tok), M.UnknownVersion{}}, Row{untrusted(chain), M.UntrustedTokenSigner{}}, Row{cert_expired(chain), M.CertificateExpired{}}, Row{Bool.not(tok_sig), M.BadTokenSignature{}}, Row{Bool.or(Nat.is_lt(exp, nbf), Nat.is_lt(max_life, Nat.sub(exp, nbf))), M.TokenPolicy{}}, Row{Nat.is_lt(Nat.add(now, skew), nbf), M.TokenNotYetValid{}}, Row{Nat.is_le(exp, now), M.TokenExpired{}}, Row{not_enrolled(enrol), M.NotEnrolled{}}, Row{revoked(enrol), M.Revoked{}}, Row{mismatched(enrol), M.EnrolmentMismatch{}}, Row{other_session(session), M.SessionMismatch{}}, Row{Bool.not(msg_sig), M.BadMessageSignature{}}, Row{Bool.or(Nat.is_lt(Nat.add(sent, skew), now), Nat.is_lt(Nat.add(now, skew), sent)), M.Stale{}}, Row{unlisted(kind), M.UnknownKind{}}, Row{lacks(kind, perms), M.WrongPermission{}}, Row{malformed(payload), M.Malformed{}}, Row{unknown_version(payload), M.UnknownVersion{}}]

def Rules(f: M.Facts, +now: Nat, cfg: M.Cfg) -> List<&2, Row>:
  match f:
    case M.Facts{oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, key, id}:
      match cfg:
        case M.Cfg{max_life, skew, cap}:
          Table(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)

# rule 16's two reasons
def ReplayReason(w: M.Refusal) -> Type:
  match w:
    case M.Replayed{}:
      Unit
    case M.ReplayCacheFull{}:
      Unit
    case _:
      Empty

def PassOrReplay(v: Maybe<&2, M.Refusal>) -> Type:
  match v:
    case None{}:
      Unit
    case Some{w}:
      ReplayReason(w)

def Expect(s: Maybe<&2, M.Refusal>, v: Maybe<&2, M.Refusal>, after: List<&2, M.Entry>, before: List<&2, M.Entry>) -> Type:
  match s:
    case Some{w}:
      {v == Some{w} : Maybe<&2, M.Refusal>} & {after == before : List<&2, M.Entry>}
    case None{}:
      PassOrReplay(v)

law first_failing_rule:
  for +f   : M.Facts
  for +now : Nat
  for +cfg : M.Cfg
  for +mem : List<&2, M.Entry>
  Expect(first(Rules(f, now, cfg)), M.verdict(M.verify(f, now, cfg, mem)), M.memory(M.verify(f, now, cfg, mem)), mem)

# C2.L3b A full replay memory refuses. When the verifier already remembers as many unexpired messages as
# milkyway.im.replayCapacity allows, nothing verifies: it never forgets early to make room.
def Unexpired.count(alive: Bool, n: Nat) -> Nat:
  match alive:
    case True{}:
      1n+n
    case False{}:
      n

def Unexpired(mem: List<&2, M.Entry>, +now: Nat) -> Nat:
  match mem:
    case Nil{}:
      0n
    case h <> t:
      match h:
        case M.Entry{k, i, +u}:
          Unexpired.count(Bool.not(Nat.is_lt(u, now)), Unexpired(t, now))

law full_memory_refuses:
  for +f        : M.Facts
  for +now      : Nat
  for  max_life : Nat
  for +skew     : Nat
  for +cap      : Nat
  for +mem      : List<&2, M.Entry>
  When(Nat.is_le(cap, Unexpired(mem, now)), Holds(Bool.not(M.is_verified(M.verify(f, now, M.Cfg{max_life, skew, cap}, mem)))))
