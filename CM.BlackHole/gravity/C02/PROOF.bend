# DMP C2: the proofs of LAWS.bend (Gravity slice G1). Agents write this file; `bend PROOF.bend --verdict` is the gate.
# Run through tools/gravity (`check`), which also runs the mutants in mutants.json. CM.BlackHole/docs/gravity.md says how.

import Base
import ./model.bend as M
import ./LAWS.bend as Laws

# Small facts
# -----------

def IsNone(m: Maybe<&2, M.Refusal>) -> Type:
  match m:
    case None{}:
      Unit
    case Some{x}:
      Empty

# a refusal is not None
def some_not_none(-x: M.Refusal, e: {Some{x} == None{} : Maybe<&2, M.Refusal>}) -> Empty:
  %Equal.sym(Maybe<&2, M.Refusal>, Some{x}, None{}, e) : IsNone(_)
  Unit{}

def IsFalse(b: Bool) -> Type:
  match b:
    case False{}:
      Unit
    case True{}:
      Empty

def true_not_false(e: {True{} == False{} : Bool}) -> Empty:
  %Equal.sym(Bool, True{}, False{}, e) : IsFalse(_)
  Unit{}

# not a < b means b <= a
def lt_false_le(a: Nat, b: Nat) -> {Nat.is_lt(a, b) == False{} : Bool} -> Laws.Holds(Nat.is_le(b, a)):
  match a b:
    case 0n 0n:
      e => Unit{}
    case 0n 1n+bp:
      e => Empty.absurd(Laws.Holds(Nat.is_le(1n+bp, 0n)), true_not_false(e))
    case 1n+ap 0n:
      e => Unit{}
    case 1n+ap 1n+bp:
      lt_false_le(ap, bp)

def Laws.table_heartbeat():
  {==}

def Laws.table_nothing_else():
  {==}

# not a <= b means b < a
def le_false_lt(a: Nat, b: Nat) -> {Nat.is_le(a, b) == False{} : Bool} -> Laws.Holds(Nat.is_lt(b, a)):
  match a b:
    case 0n 0n:
      e => Empty.absurd(Laws.Holds(Nat.is_lt(0n, 0n)), true_not_false(e))
    case 0n 1n+bp:
      e => Empty.absurd(Laws.Holds(Nat.is_lt(1n+bp, 0n)), true_not_false(e))
    case 1n+ap 0n:
      e => Unit{}
    case 1n+ap 1n+bp:
      le_false_lt(ap, bp)

def IsTrue(b: Bool) -> Type:
  match b:
    case True{}:
      Unit
    case False{}:
      Empty

def false_not_true(e: {False{} == True{} : Bool}) -> Empty:
  %Equal.sym(Bool, False{}, True{}, e) : IsTrue(_)
  Unit{}

# the model's permission check finds what the law calls listed
def same_perm(a: M.Perm, b: M.Perm) -> {M.perm_eq(a, b) == True{} : Bool} -> Laws.SamePerm(a, b):
  match a b:
    case M.SessionOpen{} M.SessionOpen{}:
      e => Unit{}
    case M.SessionOpen{} M.HeartbeatSend{}:
      e => Empty.absurd(Laws.SamePerm(M.SessionOpen{}, M.HeartbeatSend{}), false_not_true(e))
    case M.SessionOpen{} M.OtherPerm{}:
      e => Empty.absurd(Laws.SamePerm(M.SessionOpen{}, M.OtherPerm{}), false_not_true(e))
    case M.HeartbeatSend{} M.SessionOpen{}:
      e => Empty.absurd(Laws.SamePerm(M.HeartbeatSend{}, M.SessionOpen{}), false_not_true(e))
    case M.HeartbeatSend{} M.HeartbeatSend{}:
      e => Unit{}
    case M.HeartbeatSend{} M.OtherPerm{}:
      e => Empty.absurd(Laws.SamePerm(M.HeartbeatSend{}, M.OtherPerm{}), false_not_true(e))
    case M.OtherPerm{} M.SessionOpen{}:
      e => Empty.absurd(Laws.SamePerm(M.OtherPerm{}, M.SessionOpen{}), false_not_true(e))
    case M.OtherPerm{} M.HeartbeatSend{}:
      e => Empty.absurd(Laws.SamePerm(M.OtherPerm{}, M.HeartbeatSend{}), false_not_true(e))
    case M.OtherPerm{} M.OtherPerm{}:
      e => Unit{}

def or_true(a: Bool, b: Bool) -> {Bool.or(a, b) == True{} : Bool} -> Or({a == True{} : Bool}, {b == True{} : Bool}):
  match a:
    case True{}:
      e => Inl{{==}}
    case False{}:
      e => Inr{e}

def has_listed.fin(+h: M.Perm, -t: List<&2, M.Perm>, +p: M.Perm, rec: {M.has(t, p) == True{} : Bool} -> Laws.Listed(t, p), o: Or({M.perm_eq(h, p) == True{} : Bool}, {M.has(t, p) == True{} : Bool})) -> Laws.Listed(h <> t, p):
  match o:
    case Inl{x}:
      Inl{same_perm(h, p)(x)}
    case Inr{y}:
      Inr{rec(y)}

def has_listed(+xs: List<&2, M.Perm>, +p: M.Perm) -> {M.has(xs, p) == True{} : Bool} -> Laws.Listed(xs, p):
  match xs:
    case Nil{}:
      e => Empty.absurd(Laws.Listed(Nil{}, p), false_not_true(e))
    case h <> t:
      e => has_listed.fin(h, t, p, has_listed(t, p), or_true(M.perm_eq(h, p), M.has(t, p))(e))

# C2.L1: walk the rules
# ---------------------
# a<k> mirrors rule k: given that rules k to 15 found nothing, it returns what those rules established.

def absurd_some(-T: Type, -x: M.Refusal, e: {Some{x} == None{} : Maybe<&2, M.Refusal>}) -> T:
  Empty.absurd(T, some_not_none(x, e))

def a15(payload: M.Parse) -> {M.r15(payload) == None{} : Maybe<&2, M.Refusal>} -> Unit:
  match payload:
    case M.POk{}:
      e => Unit{}
    case M.PMalformed{}:
      e => absurd_some(Unit, M.Malformed{}, e)
    case M.PUnknownVersion{}:
      e => absurd_some(Unit, M.UnknownVersion{}, e)

def a14(ok: Bool, payload: M.Parse, +perms: List<&2, M.Perm>) -> {M.has(perms, M.HeartbeatSend{}) == ok : Bool} -> {M.r14(ok, payload) == None{} : Maybe<&2, M.Refusal>} -> Laws.Listed(perms, M.HeartbeatSend{}):
  match ok:
    case True{}:
      h => e => has_listed(perms, M.HeartbeatSend{})(h)
    case False{}:
      h => e => absurd_some(Laws.Listed(perms, M.HeartbeatSend{}), M.WrongPermission{}, e)

def a13(kind: M.MsgKind, +perms: List<&2, M.Perm>, payload: M.Parse) -> {M.r13(M.need(kind), perms, payload) == None{} : Maybe<&2, M.Refusal>} -> Laws.Permitted(kind, perms):
  match kind:
    case M.Heartbeat{}:
      e => a14(M.has(perms, M.HeartbeatSend{}), payload, perms)({==})(e)
    case M.Unlisted{}:
      e => absurd_some(Laws.Permitted(M.Unlisted{}, perms), M.UnknownKind{}, e)

def Fresh(+sent: Nat, +now: Nat, +skew: Nat) -> Type:
  Laws.Holds(Bool.not(M.stale(sent, now, skew)))

def a12(bad: Bool, +sent: Nat, +now: Nat, +skew: Nat, kind: M.MsgKind, +perms: List<&2, M.Perm>, payload: M.Parse) -> {M.stale(sent, now, skew) == bad : Bool} -> {M.r12(bad, kind, perms, payload) == None{} : Maybe<&2, M.Refusal>} -> Fresh(sent, now, skew) & Laws.Permitted(kind, perms):
  match bad:
    case True{}:
      h => e => absurd_some(Fresh(sent, now, skew) & Laws.Permitted(kind, perms), M.Stale{}, e)
    case False{}:
      h => e =>
        %Equal.sym(Bool, M.stale(sent, now, skew), False{}, h) : Laws.Holds(Bool.not(_)) & Laws.Permitted(kind, perms)
        (Unit{}, a13(kind, perms, payload)(e))

def a11(msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, payload: M.Parse, +now: Nat, +skew: Nat) -> {M.r11(msg_sig, sent, kind, perms, payload, now, skew) == None{} : Maybe<&2, M.Refusal>} -> Laws.Holds(msg_sig) & Fresh(sent, now, skew) & Laws.Permitted(kind, perms):
  match msg_sig:
    case True{}:
      e => (Unit{}, a12(M.stale(sent, now, skew), sent, now, skew, kind, perms, payload)({==})(e))
    case False{}:
      e => absurd_some(Laws.Holds(False{}) & Fresh(sent, now, skew) & Laws.Permitted(kind, perms), M.BadMessageSignature{}, e)

def a10(session: M.Session, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, payload: M.Parse, +now: Nat, +skew: Nat) -> {M.r10(session, msg_sig, sent, kind, perms, payload, now, skew) == None{} : Maybe<&2, M.Refusal>} -> Laws.Holds(msg_sig) & Fresh(sent, now, skew) & Laws.Permitted(kind, perms):
  match session:
    case M.SNone{}:
      a11(msg_sig, sent, kind, perms, payload, now, skew)
    case M.SSame{}:
      a11(msg_sig, sent, kind, perms, payload, now, skew)
    case M.SOther{}:
      e => absurd_some(Laws.Holds(msg_sig) & Fresh(sent, now, skew) & Laws.Permitted(kind, perms), M.SessionMismatch{}, e)

def a9(enrol: M.Enrol, session: M.Session, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, payload: M.Parse, +now: Nat, +skew: Nat) -> {M.r9(enrol, session, msg_sig, sent, kind, perms, payload, now, skew) == None{} : Maybe<&2, M.Refusal>} -> Laws.EnrolledUnrevoked(enrol) & Laws.Holds(msg_sig) & Fresh(sent, now, skew) & Laws.Permitted(kind, perms):
  match enrol:
    case M.EOk{}:
      e => (Unit{}, a10(session, msg_sig, sent, kind, perms, payload, now, skew)(e))
    case M.ENotEnrolled{}:
      e => absurd_some(Laws.EnrolledUnrevoked(M.ENotEnrolled{}) & Laws.Holds(msg_sig) & Fresh(sent, now, skew) & Laws.Permitted(kind, perms), M.NotEnrolled{}, e)
    case M.ERevoked{}:
      e => absurd_some(Laws.EnrolledUnrevoked(M.ERevoked{}) & Laws.Holds(msg_sig) & Fresh(sent, now, skew) & Laws.Permitted(kind, perms), M.Revoked{}, e)
    case M.EMismatch{}:
      e => absurd_some(Laws.EnrolledUnrevoked(M.EMismatch{}) & Laws.Holds(msg_sig) & Fresh(sent, now, skew) & Laws.Permitted(kind, perms), M.EnrolmentMismatch{}, e)

def A9(enrol: M.Enrol, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, +now: Nat, +skew: Nat) -> Type:
  Laws.EnrolledUnrevoked(enrol) & Laws.Holds(msg_sig) & Fresh(sent, now, skew) & Laws.Permitted(kind, perms)

def a8(bad: Bool, +exp: Nat, enrol: M.Enrol, session: M.Session, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, payload: M.Parse, +now: Nat, +skew: Nat) -> {Nat.is_le(exp, now) == bad : Bool} -> {M.r8(bad, enrol, session, msg_sig, sent, kind, perms, payload, now, skew) == None{} : Maybe<&2, M.Refusal>} -> Laws.Holds(Nat.is_lt(now, exp)) & A9(enrol, msg_sig, sent, kind, perms, now, skew):
  match bad:
    case True{}:
      h => e => absurd_some(Laws.Holds(Nat.is_lt(now, exp)) & A9(enrol, msg_sig, sent, kind, perms, now, skew), M.TokenExpired{}, e)
    case False{}:
      h => e => (le_false_lt(exp, now)(h), a9(enrol, session, msg_sig, sent, kind, perms, payload, now, skew)(e))

def A8(+nbf: Nat, +exp: Nat, enrol: M.Enrol, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, +now: Nat, +skew: Nat) -> Type:
  Laws.Holds(Nat.is_le(nbf, Nat.add(now, skew))) & Laws.Holds(Nat.is_lt(now, exp)) & A9(enrol, msg_sig, sent, kind, perms, now, skew)

def a7(bad: Bool, +nbf: Nat, +exp: Nat, enrol: M.Enrol, session: M.Session, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, payload: M.Parse, +now: Nat, +skew: Nat) -> {Nat.is_lt(Nat.add(now, skew), nbf) == bad : Bool} -> {M.r7(bad, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew) == None{} : Maybe<&2, M.Refusal>} -> A8(nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew):
  match bad:
    case True{}:
      h => e => absurd_some(A8(nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew), M.TokenNotYetValid{}, e)
    case False{}:
      h => e => (lt_false_le(Nat.add(now, skew), nbf)(h), a8(Nat.is_le(exp, now), exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew)({==})(e))

def a6(bad: Bool, +nbf: Nat, +exp: Nat, enrol: M.Enrol, session: M.Session, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, payload: M.Parse, +now: Nat, +skew: Nat) -> {M.r6(bad, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew) == None{} : Maybe<&2, M.Refusal>} -> A8(nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew):
  match bad:
    case True{}:
      e => absurd_some(A8(nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew), M.TokenPolicy{}, e)
    case False{}:
      e => a7(Nat.is_lt(Nat.add(now, skew), nbf), nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew)({==})(e)

def a5(tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: M.Enrol, session: M.Session, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, payload: M.Parse, +now: Nat, max_life: Nat, +skew: Nat) -> {M.r5(tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew) == None{} : Maybe<&2, M.Refusal>} -> Laws.Holds(tok_sig) & A8(nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew):
  match tok_sig:
    case True{}:
      e => (Unit{}, a6(M.policy_bad(nbf, exp, max_life), nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew)(e))
    case False{}:
      e => absurd_some(Laws.Holds(False{}) & A8(nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew), M.BadTokenSignature{}, e)

def A4(chain: M.Chain, tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: M.Enrol, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, +now: Nat, +skew: Nat) -> Type:
  Laws.SignedByTrustedSigner(chain, tok_sig) & A8(nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew)

def a4(chain: M.Chain, tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: M.Enrol, session: M.Session, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, payload: M.Parse, +now: Nat, max_life: Nat, +skew: Nat) -> {M.r4(chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew) == None{} : Maybe<&2, M.Refusal>} -> A4(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew):
  match chain:
    case M.COk{}:
      a5(tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)
    case M.CExpired{}:
      e => absurd_some(A4(M.CExpired{}, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew), M.CertificateExpired{}, e)
    case M.CUntrusted{}:
      e => absurd_some(A4(M.CUntrusted{}, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew), M.UntrustedTokenSigner{}, e)

def a3(tok: M.Parse, chain: M.Chain, tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: M.Enrol, session: M.Session, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, payload: M.Parse, +now: Nat, max_life: Nat, +skew: Nat) -> {M.r3(tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew) == None{} : Maybe<&2, M.Refusal>} -> A4(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew):
  match tok:
    case M.POk{}:
      a4(chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)
    case M.PMalformed{}:
      e => absurd_some(A4(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew), M.Malformed{}, e)
    case M.PUnknownVersion{}:
      e => absurd_some(A4(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew), M.UnknownVersion{}, e)

def a2(env: M.Parse, tok: M.Parse, chain: M.Chain, tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: M.Enrol, session: M.Session, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, payload: M.Parse, +now: Nat, max_life: Nat, +skew: Nat) -> {M.r2(env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew) == None{} : Maybe<&2, M.Refusal>} -> A4(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew):
  match env:
    case M.POk{}:
      a3(tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)
    case M.PMalformed{}:
      e => absurd_some(A4(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew), M.Malformed{}, e)
    case M.PUnknownVersion{}:
      e => absurd_some(A4(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew), M.UnknownVersion{}, e)

def a1(oversize: Bool, env: M.Parse, tok: M.Parse, chain: M.Chain, tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: M.Enrol, session: M.Session, msg_sig: Bool, +sent: Nat, kind: M.MsgKind, perms: List<&2, M.Perm>, payload: M.Parse, +now: Nat, max_life: Nat, +skew: Nat) -> {M.r1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew) == None{} : Maybe<&2, M.Refusal>} -> A4(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew):
  match oversize:
    case True{}:
      e => absurd_some(A4(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew), M.Oversize{}, e)
    case False{}:
      a2(env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)

def AuthOrder(chain: M.Chain, tok_sig: Bool, +nbf: Nat, +exp: Nat, enrol: M.Enrol, msg_sig: Bool, kind: M.MsgKind, perms: List<&2, M.Perm>, +now: Nat, +skew: Nat) -> Type:
  Laws.SignedByTrustedSigner(chain, tok_sig) & Laws.EnrolledUnrevoked(enrol) & Laws.Holds(msg_sig) & Laws.Holds(Nat.is_le(nbf, Nat.add(now, skew))) & Laws.Holds(Nat.is_lt(now, exp)) & Laws.Permitted(kind, perms)

def reorder(-chain: M.Chain, -tok_sig: Bool, -nbf: Nat, -exp: Nat, -enrol: M.Enrol, -msg_sig: Bool, -sent: Nat, -kind: M.MsgKind, -perms: List<&2, M.Perm>, -now: Nat, -skew: Nat, x: A4(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew)) -> AuthOrder(chain, tok_sig, nbf, exp, enrol, msg_sig, kind, perms, now, skew):
  (s, r1) = x
  (le, r2) = r1
  (lt, r3) = r2
  (en, r4) = r3
  (ms, r5) = r4
  (fr, pm) = r5
  (s, en, ms, le, lt, pm)

def When.any(b: Bool, -T: Type, x: T) -> Laws.When(b, T):
  match b:
    case True{}:
      x
    case False{}:
      Unit{}

def l1.go(r: Maybe<&2, M.Refusal>, mem: List<&2, M.Entry>, +key: Nat, +id: Nat, +sent: Nat, +now: Nat, +skew: Nat, cap: Nat, -T: Type) -> ({r == None{} : Maybe<&2, M.Refusal>} -> T) -> Laws.When(M.is_verified(M.decide(r, mem, key, id, sent, now, skew, cap)), T):
  match r:
    case Some{w}:
      t => Unit{}
    case None{}:
      t => When.any(M.is_verified(M.replay.go(M.forget(mem, now), key, id, Nat.add(sent, skew), cap)), T, t({==}))

def Laws.verified_means_authorised(f, now, cfg, mem):
  match f:
    case M.Facts{oversize, env, tok, chain, tok_sig, +nbf, +exp, enrol, session, msg_sig, +sent, kind, +perms, payload, +key, +id}:
      match cfg:
        case M.Cfg{max_life, +skew, cap}:
          l1.go(M.r1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew), mem, key, id, sent, now, skew, cap, AuthOrder(chain, tok_sig, nbf, exp, enrol, msg_sig, kind, perms, now, skew))(e => reorder(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew, a1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)(e)))

# C2.L3: no replay
# ----------------

def or_not_left(a: Bool, -b: Bool) -> Laws.Holds(Bool.not(Bool.or(a, b))) -> {a == False{} : Bool}:
  match a:
    case True{}:
      h => Empty.absurd({True{} == False{} : Bool}, h)
    case False{}:
      h => {==}

def eq_refl(n: Nat) -> {Nat.is_eq(n, n) == True{} : Bool}:
  match n:
    case 0n:
      {==}
    case 1n+p:
      eq_refl(p)

def proj_fresh(-chain: M.Chain, -tok_sig: Bool, -nbf: Nat, -exp: Nat, -enrol: M.Enrol, -msg_sig: Bool, -sent: Nat, -kind: M.MsgKind, -perms: List<&2, M.Perm>, -now: Nat, -skew: Nat, x: A4(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now, skew)) -> Fresh(sent, now, skew):
  (s, r1) = x
  (le, r2) = r1
  (lt, r3) = r2
  (en, r4) = r3
  (ms, r5) = r4
  (fr, pm) = r5
  fr

# the second time, the entry the first time left is still there, so rule 16 says Replayed
def l3.replayed(+key: Nat, +id: Nat, +sent: Nat, +skew: Nat, +now2: Nat, +cap: Nat, +m: List<&2, M.Entry>, lt: {Nat.is_lt(Nat.add(sent, skew), now2) == False{} : Bool}) -> Laws.Holds(Bool.not(M.is_verified(M.replay.go(M.forget(M.Entry{key, id, Nat.add(sent, skew)} <> m, now2), key, id, Nat.add(sent, skew), cap)))):
  %Equal.sym(Bool, Nat.is_lt(Nat.add(sent, skew), now2), False{}, lt) : Laws.Holds(Bool.not(M.is_verified(M.replay.go(M.forget.keep(_, M.Entry{key, id, Nat.add(sent, skew)}, M.forget(m, now2)), key, id, Nat.add(sent, skew), cap))))
  %Equal.sym(Bool, Nat.is_eq(key, key), True{}, eq_refl(key)) : Laws.Holds(Bool.not(M.is_verified(M.replay.seen(Bool.or(Bool.and(_, Nat.is_eq(id, id)), M.seen(M.forget(m, now2), key, id)), M.Entry{key, id, Nat.add(sent, skew)} <> M.forget(m, now2), key, id, Nat.add(sent, skew), cap))))
  %Equal.sym(Bool, Nat.is_eq(id, id), True{}, eq_refl(id)) : Laws.Holds(Bool.not(M.is_verified(M.replay.seen(Bool.or(Bool.and(True{}, _), M.seen(M.forget(m, now2), key, id)), M.Entry{key, id, Nat.add(sent, skew)} <> M.forget(m, now2), key, id, Nat.add(sent, skew), cap))))
  Unit{}

def l3.second(r: Maybe<&2, M.Refusal>, +m: List<&2, M.Entry>, +key: Nat, +id: Nat, +sent: Nat, +skew: Nat, +now2: Nat, +cap: Nat) -> ({r == None{} : Maybe<&2, M.Refusal>} -> {Nat.is_lt(Nat.add(sent, skew), now2) == False{} : Bool}) -> Laws.Holds(Bool.not(M.is_verified(M.decide(r, M.Entry{key, id, Nat.add(sent, skew)} <> m, key, id, sent, now2, skew, cap)))):
  match r:
    case Some{w}:
      k => Unit{}
    case None{}:
      k => l3.replayed(key, id, sent, skew, now2, cap, m, k({==}))

def l3.full(full: Bool, +m: List<&2, M.Entry>, +key: Nat, +id: Nat, +sent: Nat, +skew: Nat, +now2: Nat, +cap: Nat, r: Maybe<&2, M.Refusal>) -> ({r == None{} : Maybe<&2, M.Refusal>} -> {Nat.is_lt(Nat.add(sent, skew), now2) == False{} : Bool}) -> Laws.When(M.is_verified(M.replay.full(full, m, key, id, Nat.add(sent, skew))), Laws.Holds(Bool.not(M.is_verified(M.decide(r, M.memory(M.replay.full(full, m, key, id, Nat.add(sent, skew))), key, id, sent, now2, skew, cap))))):
  match full:
    case True{}:
      k => Unit{}
    case False{}:
      k => l3.second(r, m, key, id, sent, skew, now2, cap)(k)

def l3.seen(hit: Bool, +m: List<&2, M.Entry>, +key: Nat, +id: Nat, +sent: Nat, +skew: Nat, +now2: Nat, +cap: Nat, r: Maybe<&2, M.Refusal>) -> ({r == None{} : Maybe<&2, M.Refusal>} -> {Nat.is_lt(Nat.add(sent, skew), now2) == False{} : Bool}) -> Laws.When(M.is_verified(M.replay.seen(hit, m, key, id, Nat.add(sent, skew), cap)), Laws.Holds(Bool.not(M.is_verified(M.decide(r, M.memory(M.replay.seen(hit, m, key, id, Nat.add(sent, skew), cap)), key, id, sent, now2, skew, cap))))):
  match hit:
    case True{}:
      k => Unit{}
    case False{}:
      l3.full(Nat.is_le(cap, List.length(&2, M.Entry, m)), m, key, id, sent, skew, now2, cap, r)

def l3.first(r1: Maybe<&2, M.Refusal>, +mem: List<&2, M.Entry>, +key: Nat, +id: Nat, +sent: Nat, +now: Nat, +skew: Nat, +cap: Nat, +now2: Nat, r2: Maybe<&2, M.Refusal>) -> ({r2 == None{} : Maybe<&2, M.Refusal>} -> {Nat.is_lt(Nat.add(sent, skew), now2) == False{} : Bool}) -> Laws.When(M.is_verified(M.decide(r1, mem, key, id, sent, now, skew, cap)), Laws.Holds(Bool.not(M.is_verified(M.decide(r2, M.memory(M.decide(r1, mem, key, id, sent, now, skew, cap)), key, id, sent, now2, skew, cap))))):
  match r1:
    case Some{w}:
      k => Unit{}
    case None{}:
      l3.seen(M.seen(M.forget(mem, now), key, id), M.forget(mem, now), key, id, sent, skew, now2, cap, r2)

def Laws.no_replay(f, now, now2, cfg, mem):
  match f:
    case M.Facts{oversize, env, tok, chain, tok_sig, +nbf, +exp, enrol, session, msg_sig, +sent, kind, +perms, payload, +key, +id}:
      match cfg:
        case M.Cfg{max_life, +skew, +cap}:
          l3.first(M.r1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew), mem, key, id, sent, now, skew, cap, now2, M.r1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now2, max_life, skew))(e => or_not_left(Nat.is_lt(Nat.add(sent, skew), now2), Nat.is_lt(Nat.add(now2, skew), sent))(proj_fresh(chain, tok_sig, nbf, exp, enrol, msg_sig, sent, kind, perms, now2, skew, a1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now2, max_life, skew)(e))))

# C2.L3b: a full memory refuses
# -----------------------------

# what forgetting leaves is exactly the unexpired entries
def len_keep(drop: Bool, +e: M.Entry, +rest: List<&2, M.Entry>, -n: Nat, eq: {List.length(&2, M.Entry, rest) == n : Nat}) -> {List.length(&2, M.Entry, M.forget.keep(drop, e, rest)) == Laws.Unexpired.count(Bool.not(drop), n) : Nat}:
  match drop:
    case True{}:
      eq
    case False{}:
      %eq : {1n+List.length(&2, M.Entry, rest) == 1n+_ : Nat}
      {==}

def len_forget(+mem: List<&2, M.Entry>, +now: Nat) -> {List.length(&2, M.Entry, M.forget(mem, now)) == Laws.Unexpired(mem, now) : Nat}:
  match mem:
    case Nil{}:
      {==}
    case h <> t:
      match h:
        case M.Entry{+k, +i, +u}:
          len_keep(Nat.is_lt(u, now), M.Entry{k, i, u}, M.forget(t, now), Laws.Unexpired(t, now), len_forget(t, now))

def l3b.full(full: Bool, +m: List<&2, M.Entry>, key: Nat, id: Nat, until: Nat) -> Laws.When(full, Laws.Holds(Bool.not(M.is_verified(M.replay.full(full, m, key, id, until))))):
  match full:
    case True{}:
      Unit{}
    case False{}:
      Unit{}

def l3b.seen(hit: Bool, +m: List<&2, M.Entry>, key: Nat, id: Nat, until: Nat, +cap: Nat) -> Laws.When(Nat.is_le(cap, List.length(&2, M.Entry, m)), Laws.Holds(Bool.not(M.is_verified(M.replay.seen(hit, m, key, id, until, cap))))):
  match hit:
    case True{}:
      When.any(Nat.is_le(cap, List.length(&2, M.Entry, m)), Laws.Holds(True{}), Unit{})
    case False{}:
      l3b.full(Nat.is_le(cap, List.length(&2, M.Entry, m)), m, key, id, until)

def l3b.dec(r: Maybe<&2, M.Refusal>, +mem: List<&2, M.Entry>, +key: Nat, +id: Nat, +sent: Nat, +now: Nat, +skew: Nat, +cap: Nat) -> Laws.When(Nat.is_le(cap, Laws.Unexpired(mem, now)), Laws.Holds(Bool.not(M.is_verified(M.decide(r, mem, key, id, sent, now, skew, cap))))):
  match r:
    case Some{w}:
      When.any(Nat.is_le(cap, Laws.Unexpired(mem, now)), Laws.Holds(True{}), Unit{})
    case None{}:
      %len_forget(mem, now) : Laws.When(Nat.is_le(cap, _), Laws.Holds(Bool.not(M.is_verified(M.replay.go(M.forget(mem, now), key, id, Nat.add(sent, skew), cap)))))
      l3b.seen(M.seen(M.forget(mem, now), key, id), M.forget(mem, now), key, id, Nat.add(sent, skew), cap)

def Laws.full_memory_refuses(f, now, max_life, skew, cap, mem):
  match f:
    case M.Facts{oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, +sent, kind, perms, payload, +key, +id}:
      l3b.dec(M.r1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew), mem, key, id, sent, now, skew, cap)

# C2.L2: the first failing rule
# ------------------------------
# T<k> is chapter 04's table from rule k on (LAWS.bend's Table, cut); g<k> shows that the model's rule k
# answers what the table's first failing row from rule k on says.

def T15(+payload: M.Parse) -> List<&2, Laws.Row>:
  Laws.Row{Laws.malformed(payload), M.Malformed{}} <> (Laws.Row{Laws.unknown_version(payload), M.UnknownVersion{}} <> Nil{})

def T13(+kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse) -> List<&2, Laws.Row>:
  Laws.Row{Laws.unlisted(kind), M.UnknownKind{}} <> (Laws.Row{Laws.lacks(kind, perms), M.WrongPermission{}} <> T15(payload))

def T12(+sent: Nat, +now: Nat, +skew: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse) -> List<&2, Laws.Row>:
  Laws.Row{Bool.or(Nat.is_lt(Nat.add(sent, skew), now), Nat.is_lt(Nat.add(now, skew), sent)), M.Stale{}} <> T13(kind, perms, payload)

def T11(+msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +skew: Nat) -> List<&2, Laws.Row>:
  Laws.Row{Bool.not(msg_sig), M.BadMessageSignature{}} <> T12(sent, now, skew, kind, perms, payload)

def T10(+session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +skew: Nat) -> List<&2, Laws.Row>:
  Laws.Row{Laws.other_session(session), M.SessionMismatch{}} <> T11(msg_sig, sent, kind, perms, payload, now, skew)

def T9(+enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +skew: Nat) -> List<&2, Laws.Row>:
  Laws.Row{Laws.not_enrolled(enrol), M.NotEnrolled{}} <> (Laws.Row{Laws.revoked(enrol), M.Revoked{}} <> (Laws.Row{Laws.mismatched(enrol), M.EnrolmentMismatch{}} <> T10(session, msg_sig, sent, kind, perms, payload, now, skew)))

def T8(+exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +skew: Nat) -> List<&2, Laws.Row>:
  Laws.Row{Nat.is_le(exp, now), M.TokenExpired{}} <> T9(enrol, session, msg_sig, sent, kind, perms, payload, now, skew)

def T7(+nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +skew: Nat) -> List<&2, Laws.Row>:
  Laws.Row{Nat.is_lt(Nat.add(now, skew), nbf), M.TokenNotYetValid{}} <> T8(exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew)

def T6(+nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +max_life: Nat, +skew: Nat) -> List<&2, Laws.Row>:
  Laws.Row{Bool.or(Nat.is_lt(exp, nbf), Nat.is_lt(max_life, Nat.sub(exp, nbf))), M.TokenPolicy{}} <> T7(nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew)

def T5(+tok_sig: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +max_life: Nat, +skew: Nat) -> List<&2, Laws.Row>:
  Laws.Row{Bool.not(tok_sig), M.BadTokenSignature{}} <> T6(nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)

def T4(+chain: M.Chain, +tok_sig: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +max_life: Nat, +skew: Nat) -> List<&2, Laws.Row>:
  Laws.Row{Laws.untrusted(chain), M.UntrustedTokenSigner{}} <> (Laws.Row{Laws.cert_expired(chain), M.CertificateExpired{}} <> T5(tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew))

def T3(+tok: M.Parse, +chain: M.Chain, +tok_sig: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +max_life: Nat, +skew: Nat) -> List<&2, Laws.Row>:
  Laws.Row{Laws.malformed(tok), M.Malformed{}} <> (Laws.Row{Laws.unknown_version(tok), M.UnknownVersion{}} <> T4(chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew))

def T2(+env: M.Parse, +tok: M.Parse, +chain: M.Chain, +tok_sig: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +max_life: Nat, +skew: Nat) -> List<&2, Laws.Row>:
  Laws.Row{Laws.malformed(env), M.Malformed{}} <> (Laws.Row{Laws.unknown_version(env), M.UnknownVersion{}} <> T3(tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew))

def T1(+oversize: Bool, +env: M.Parse, +tok: M.Parse, +chain: M.Chain, +tok_sig: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +max_life: Nat, +skew: Nat) -> List<&2, Laws.Row>:
  Laws.Row{oversize, M.Oversize{}} <> T2(env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)

def g15(+payload: M.Parse) -> {M.r15(payload) == Laws.first(T15(payload)) : Maybe<&2, M.Refusal>}:
  match payload:
    case M.POk{}:
      {==}
    case M.PMalformed{}:
      {==}
    case M.PUnknownVersion{}:
      {==}

def g14(+ok: Bool, +payload: M.Parse) -> {M.r14(ok, payload) == Laws.first(Laws.Row{Bool.not(ok), M.WrongPermission{}} <> T15(payload)) : Maybe<&2, M.Refusal>}:
  match ok:
    case True{}:
      g15(payload)
    case False{}:
      {==}

def eq_same(a: M.Perm, b: M.Perm) -> {M.perm_eq(a, b) == Laws.same(a, b) : Bool}:
  match a b:
    case M.SessionOpen{} M.SessionOpen{}:
      {==}
    case M.SessionOpen{} M.HeartbeatSend{}:
      {==}
    case M.SessionOpen{} M.OtherPerm{}:
      {==}
    case M.HeartbeatSend{} M.SessionOpen{}:
      {==}
    case M.HeartbeatSend{} M.HeartbeatSend{}:
      {==}
    case M.HeartbeatSend{} M.OtherPerm{}:
      {==}
    case M.OtherPerm{} M.SessionOpen{}:
      {==}
    case M.OtherPerm{} M.HeartbeatSend{}:
      {==}
    case M.OtherPerm{} M.OtherPerm{}:
      {==}

def has_carries(+xs: List<&2, M.Perm>, +p: M.Perm) -> {M.has(xs, p) == Laws.carries(xs, p) : Bool}:
  match xs:
    case Nil{}:
      {==}
    case h <> t:
      %has_carries(t, p) : {Bool.or(M.perm_eq(h, p), M.has(t, p)) == Bool.or(Laws.same(h, p), _) : Bool}
      %eq_same(h, p) : {Bool.or(M.perm_eq(h, p), M.has(t, p)) == Bool.or(_, M.has(t, p)) : Bool}
      {==}

def g13(+kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse) -> {M.r13(M.need(kind), perms, payload) == Laws.first(T13(kind, perms, payload)) : Maybe<&2, M.Refusal>}:
  match kind:
    case M.Heartbeat{}:
      %has_carries(perms, M.HeartbeatSend{}) : {M.r14(M.has(perms, M.HeartbeatSend{}), payload) == Laws.first(Laws.Row{Bool.not(_), M.WrongPermission{}} <> T15(payload)) : Maybe<&2, M.Refusal>}
      g14(M.has(perms, M.HeartbeatSend{}), payload)
    case M.Unlisted{}:
      {==}

def g12(+bad: Bool, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse) -> {M.r12(bad, kind, perms, payload) == Laws.first(Laws.Row{bad, M.Stale{}} <> T13(kind, perms, payload)) : Maybe<&2, M.Refusal>}:
  match bad:
    case True{}:
      {==}
    case False{}:
      g13(kind, perms, payload)

def g11(+msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +skew: Nat) -> {M.r11(msg_sig, sent, kind, perms, payload, now, skew) == Laws.first(T11(msg_sig, sent, kind, perms, payload, now, skew)) : Maybe<&2, M.Refusal>}:
  match msg_sig:
    case True{}:
      g12(M.stale(sent, now, skew), kind, perms, payload)
    case False{}:
      {==}

def g10(+session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +skew: Nat) -> {M.r10(session, msg_sig, sent, kind, perms, payload, now, skew) == Laws.first(T10(session, msg_sig, sent, kind, perms, payload, now, skew)) : Maybe<&2, M.Refusal>}:
  match session:
    case M.SNone{}:
      g11(msg_sig, sent, kind, perms, payload, now, skew)
    case M.SSame{}:
      g11(msg_sig, sent, kind, perms, payload, now, skew)
    case M.SOther{}:
      {==}

def g9(+enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +skew: Nat) -> {M.r9(enrol, session, msg_sig, sent, kind, perms, payload, now, skew) == Laws.first(T9(enrol, session, msg_sig, sent, kind, perms, payload, now, skew)) : Maybe<&2, M.Refusal>}:
  match enrol:
    case M.EOk{}:
      g10(session, msg_sig, sent, kind, perms, payload, now, skew)
    case M.ENotEnrolled{}:
      {==}
    case M.ERevoked{}:
      {==}
    case M.EMismatch{}:
      {==}

def g8(+bad: Bool, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +skew: Nat) -> {M.r8(bad, enrol, session, msg_sig, sent, kind, perms, payload, now, skew) == Laws.first(Laws.Row{bad, M.TokenExpired{}} <> T9(enrol, session, msg_sig, sent, kind, perms, payload, now, skew)) : Maybe<&2, M.Refusal>}:
  match bad:
    case True{}:
      {==}
    case False{}:
      g9(enrol, session, msg_sig, sent, kind, perms, payload, now, skew)

def g7(+bad: Bool, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +skew: Nat) -> {M.r7(bad, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew) == Laws.first(Laws.Row{bad, M.TokenNotYetValid{}} <> T8(exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew)) : Maybe<&2, M.Refusal>}:
  match bad:
    case True{}:
      {==}
    case False{}:
      g8(Nat.is_le(exp, now), enrol, session, msg_sig, sent, kind, perms, payload, now, skew)

def g6(+bad: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +skew: Nat) -> {M.r6(bad, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew) == Laws.first(Laws.Row{bad, M.TokenPolicy{}} <> T7(nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew)) : Maybe<&2, M.Refusal>}:
  match bad:
    case True{}:
      {==}
    case False{}:
      g7(Nat.is_lt(Nat.add(now, skew), nbf), exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew)

def g5(+tok_sig: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +max_life: Nat, +skew: Nat) -> {M.r5(tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew) == Laws.first(T5(tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)) : Maybe<&2, M.Refusal>}:
  match tok_sig:
    case True{}:
      g6(M.policy_bad(nbf, exp, max_life), nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, skew)
    case False{}:
      {==}

def g4(+chain: M.Chain, +tok_sig: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +max_life: Nat, +skew: Nat) -> {M.r4(chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew) == Laws.first(T4(chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)) : Maybe<&2, M.Refusal>}:
  match chain:
    case M.COk{}:
      g5(tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)
    case M.CExpired{}:
      {==}
    case M.CUntrusted{}:
      {==}

def g3(+tok: M.Parse, +chain: M.Chain, +tok_sig: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +max_life: Nat, +skew: Nat) -> {M.r3(tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew) == Laws.first(T3(tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)) : Maybe<&2, M.Refusal>}:
  match tok:
    case M.POk{}:
      g4(chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)
    case M.PMalformed{}:
      {==}
    case M.PUnknownVersion{}:
      {==}

def g2(+env: M.Parse, +tok: M.Parse, +chain: M.Chain, +tok_sig: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +max_life: Nat, +skew: Nat) -> {M.r2(env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew) == Laws.first(T2(env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)) : Maybe<&2, M.Refusal>}:
  match env:
    case M.POk{}:
      g3(tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)
    case M.PMalformed{}:
      {==}
    case M.PUnknownVersion{}:
      {==}

def g1(+oversize: Bool, +env: M.Parse, +tok: M.Parse, +chain: M.Chain, +tok_sig: Bool, +nbf: Nat, +exp: Nat, +enrol: M.Enrol, +session: M.Session, +msg_sig: Bool, +sent: Nat, +kind: M.MsgKind, +perms: List<&2, M.Perm>, +payload: M.Parse, +now: Nat, +max_life: Nat, +skew: Nat) -> {M.r1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew) == Laws.first(T1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)) : Maybe<&2, M.Refusal>}:
  match oversize:
    case True{}:
      {==}
    case False{}:
      g2(env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew)

# when rules 1 to 15 pass, rule 16 says Replayed, ReplayCacheFull or nothing
def l2.full(full: Bool, +m: List<&2, M.Entry>, key: Nat, id: Nat, until: Nat) -> Laws.PassOrReplay(M.verdict(M.replay.full(full, m, key, id, until))):
  match full:
    case True{}:
      Unit{}
    case False{}:
      Unit{}

def l2.seen(hit: Bool, +m: List<&2, M.Entry>, key: Nat, id: Nat, until: Nat, +cap: Nat) -> Laws.PassOrReplay(M.verdict(M.replay.seen(hit, m, key, id, until, cap))):
  match hit:
    case True{}:
      Unit{}
    case False{}:
      l2.full(Nat.is_le(cap, List.length(&2, M.Entry, m)), m, key, id, until)

def l2.dec(r: Maybe<&2, M.Refusal>, +mem: List<&2, M.Entry>, +key: Nat, +id: Nat, +sent: Nat, +now: Nat, +skew: Nat, cap: Nat) -> Laws.Expect(r, M.verdict(M.decide(r, mem, key, id, sent, now, skew, cap)), M.memory(M.decide(r, mem, key, id, sent, now, skew, cap)), mem):
  match r:
    case Some{w}:
      ({==}, {==})
    case None{}:
      l2.seen(M.seen(M.forget(mem, now), key, id), M.forget(mem, now), key, id, Nat.add(sent, skew), cap)

def Laws.first_failing_rule(f, now, cfg, mem):
  match f:
    case M.Facts{+oversize, +env, +tok, +chain, +tok_sig, +nbf, +exp, +enrol, +session, +msg_sig, +sent, +kind, +perms, +payload, +key, +id}:
      match cfg:
        case M.Cfg{+max_life, +skew, cap}:
          %g1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew) : Laws.Expect(_, M.verdict(M.decide(M.r1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew), mem, key, id, sent, now, skew, cap)), M.memory(M.decide(M.r1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew), mem, key, id, sent, now, skew, cap)), mem)
          l2.dec(M.r1(oversize, env, tok, chain, tok_sig, nbf, exp, enrol, session, msg_sig, sent, kind, perms, payload, now, max_life, skew), mem, key, id, sent, now, skew, cap)

