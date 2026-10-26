# Im: the IM encoding (draft, IM version 0)

The byte format of IM and the client side of a session, used by every part that speaks IM: Milky Way (which also verifies), Doppler (which
issues tokens) and any device agent or client (which knocks, answers the challenge and sends messages). It is in `cm.os` so that no
constellation references another (constitution C1, rule DMP-008). The specification is [DMP 04](../../DMP/04-im.md); the verify rules are
in `CM.MilkyWay` only.

Owner: Milky Way (lane L2). The neighbouring folder `cm.os/src/Trust` (certificate roles, chain validation, the signer, the enrolment
contract) is Black Hole's (lane L4); the split was agreed between the two lanes on 2026-10-25.

| Type | What it is | Used by |
|---|---|---|
| `ImWriter`, `ImReader`, `ImFormatException` | canonical big-endian encoding, strict reading with limits, error codes without content | everything here |
| `ImTags`, `ImKeys`, `ImChains` | domain tags, the one signature algorithm (ECDSA P-256, SHA-256, 64-byte P1363) over SubjectPublicKeyInfo keys, chains in wire order | token, envelope, frames |
| `ImTokenClaims`, `ImTokenCodec`, `ImToken` | the token: Doppler calls `Issue(claims, signer, issuedAt)` with its `ImTokenSigner` key; readers call `Parse` | Doppler, the verifier |
| `ImRoles`, `ImKinds` | the wire spelling of the enrolled roles; the table of kinds and the permission each needs | Doppler (role to permissions), the verifier |
| `ImEnvelopeCodec`, `ImEnvelope`, `ImHeartbeat` | the signed envelope; the heartbeat payload | senders, the verifier |
| `ImFrames`, `ImClientSession` | the handshake frames; the client side of a session (checks the gate in the role `MilkyWayGate`) | device agents, the simulator |
| `ImStreamFraming`, `ImTls`, `ImTlsIdentity`, `ImTlsClientConnection` | frames on a stream (4-byte length first); TLS 1.3 with mutual certificates checked by `ITrustAnchors` in their roles; the device side of a session over TLS, bound to the gate certificate TLS showed (DMP 05, "Transport encryption"); `ImTlsIdentity.Of` exports a signer's key for TLS, `ImTlsIdentity.Bound` takes a certificate already joined to a key that cannot be exported (a TPM key) as it is | device agents, the gate's transport, the simulator |

What Doppler does with it: issue a token per enrolled device with `ImTokenCodec.Issue`, giving every enrolled role `im.session.open` and
`im.heartbeat.send` in version 0; keep `IEnrolmentRegistry` true (subject, role, KeyId of the device key, revoked). Changing a field, a tag
or the algorithm is a contract change (C4) and a new IM version.
