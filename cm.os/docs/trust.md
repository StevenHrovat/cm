# Im.Trust: the trust contract of CMOS (draft, IM version 0)

Who may speak IM is decided by certificates from the **new** certificate authority (plan: [CM.BlackHole/docs/ca-plan.md](../../CM.BlackHole/docs/ca-plan.md))
and by Doppler's register of enrolled machines. This folder holds that decision as code both sides use, so Milky Way (the verifier)
and Black Hole (Doppler, the signer) never reference each other (constitution C1, rule DMP-008).

Split agreed between the two lanes on 2026-10-25: `cm.os/src/Trust` (this contract) is Black Hole's; `cm.os/src/Im` (envelope, token
claims and codec, handshake frames) is Milky Way's.

| Type | What it is | Used by |
|---|---|---|
| `TrustRole`, `TrustOids` | The five roles a certificate can carry, as EKU OIDs under one UUID arc (2.25...) that names the framework, not the operating organisation | issuing CA templates, verifier |
| `ITrustAnchors`, `TrustAnchors` | Does a leaf chain to the configured roots and carry the role, at a time passed in; custom root trust only (the old CA is never trusted), no network, revocation from an offline list; ECDSA P-256 leaves only | verifier (sender: Device, token: ImTokenSigner, handshake: MilkyWayGate), Doppler |
| `ChainVerdict`, `ChainRefusal` | The answer as a code (UntrustedRoot, Expired, NotYetValid, WrongRole, Revoked, Malformed); logs count codes, never content | verifier's refusal log |
| `IImSigner`, `EcdsaImSigner`, `ImSignature` | ECDSA P-256 / SHA-256, signatures in IEEE P1363 form (64 bytes); KeyId = lower-case hex SHA-256 of the SubjectPublicKeyInfo | Doppler (tokens), devices (handshake, messages) |
| `IEnrolmentRegistry`, `EnrolmentRecord`, `EnrolledRole` | Doppler's register: opaque subject id, role, KeyId of the device key, revoked flag; missing, revoked or key mismatch = unenrolled | verifier reads, Doppler writes |
| `DualTrust`, `DualVerdict`, `PresentedChain`, `TrustMaster` | The double chain of trust (ca-plan.md section 1a): the security chain against the security anchors, the operational chain against the operational anchors, the same role, byte-identical SubjectPublicKeyInfo on both leaves; both chains always checked, so refusals count per chain; `Over` refuses a root configured for both masters. Registers and tokens are checked on top, in IM version 1 | the verifier (IM v1, DMP 04), Doppler and the operations master |
| `ICertificateIssuer` | The issuing CA as Doppler sees it: issue a leaf for a verified request's public key with the role's profile (the issuer sets every extension), revoke | Doppler |
| `cm.os.Health`: `HealthReport`, `SignedHealthReport`, `IHealthSource`, `PeerWatch` | Signed health reports (counts and accepted token ids, never content) and the watch that accepts a peer's report only with the right role, chain, signature, a growing sequence and no future time; refused = Untrusted, silent = Stale | Doppler and Milky Way watching each other |
| `Sandbox.SandboxAuthority`, `Sandbox.SandboxEnrolmentRegistry` | In-memory two-tier CA and register for tests and the simulator; a second authority stands in for the old CA | tests of both lanes, simulator |

Rules the tests hold (`cm.os/tests/Trust`): a leaf with no EKU is refused for every role; a CA certificate used as a leaf is refused; a chain
to any other root, including a correctly formed one, is refused; time comes from the caller.
Changing a role OID or the signature algorithm is a contract change (C4): it lands at a stable promotion, with a new IM version.
