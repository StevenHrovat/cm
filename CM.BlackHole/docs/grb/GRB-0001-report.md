# GRB-0001 · Report of the independent run (2026-10-25 05:20Z to 05:40Z, part 2 to 06:10Z)

Security context: this is an authorised test of our own controls under the defensive charter in cm `CLAUDE.md` (strategic review 08) and the
scope file `CM.BlackHole/docs/grb/GRB-0001-sandbox-gate.md`. Signatory: the owner of the framework, by his words in the Black Hole lane
(cmsg_01Q5KHMD78Sa1TBrCksHzT1yFFVfpMMbWRLa4YCKNgr41j, 2026-10-25 05:12:57Z: "I sign GRB-0001 with the TLS additions, and the helper may run it").
Runner: a fresh session that did not build the verifier. Inputs used: DMP chapters 04 and 05 (06 for the level definition and the report format),
the scope file, and the simulator and component sources in `cm.os/src`, `CM.MilkyWay/src`, `CM.BlackHole/src`. Not read: the builder's notes, the
tests in `CM.MilkyWay/tests`, `CM.BlackHole/tests`, `tools/sandbox-pair/tests`, and an earlier change.

Everything ran in memory on the simulator (`SandboxWorld`, `SimulatedClock`, `SimulatedTpm`, `SimulatedDeviceHost`) with synthetic data. No socket,
no real host, no real credential. All tests ran to completion without a platform safety-filter stop.

## Result

| | |
|---|---|
| Part 1 (cm `main`, c271718) | 95 methods, 105 cases (theories expanded); **105 passed, 0 failed**, 12 s |
| Part 2 (PR branch `claude/project-thread-sdrosj`, 2e42d2e: the TLS additions, the door, low-s) | the part 1 suite plus 26 transport, door and low-s methods: **132 cases, 132 passed, 0 failed**, run three times, 14 to 17 s |
| Got through where the spec says refuse | **none**, in either part |
| Findings (design, not a breach of a rule) | 3: F1 and F2 (part 1), F3 (part 2) |
| Observations (spec and code differ in wording or a corner is undefined) | 8: O1 to O6 (part 1), O7 and O8 (part 2) |
| Level | the level 3 line "one GRB run against the sandbox under a scope file" is met; the level 4 line "done by an independent session with only the specification and the scope file" is met for this run |

Where the tests live: `tools/grb-0001/Grb0001.Tests` in the runner's clone (uncommitted, throwaway) and a copy in the project Library
(`grb-0001/tests/`), with the `.trx` run log next to this file. The project references the three `src` projects only.

## What was tried, by class (GRB-0001 "Methods allowed")

Counts are test cases; each case runs several frames. Every refusal below was also checked against the counter of its reason.

| Method | Cases | What was tried | Refused as | Got through |
|---|---|---|---|---|
| Malformed, truncated, oversized envelopes | 20 | every truncation of a valid envelope (all prefix lengths); a trailing byte; every single-bit flip of a valid envelope (one per byte); outer-length lies; version bytes 1 in envelope, token and payload; kind outside the alphabet (upper case, space, underscore, NUL, empty, Cyrillic look-alike); payload over the limit, status 3, agent version with a space, 33 characters, trailing byte, empty; token with unsorted, duplicate, zero, 17 permissions, a lying count, wrong key id, upper-case subject, unknown role, wrong magic, no chain, 5 certificates, trailing byte; P-384, RSA and explicit-parameter P-256 subject keys; 70-byte DER signature; 2000 random frames and 500 scrambled runs inside a valid envelope; oversize at the gate inside a session and before the knock | Malformed, UnknownVersion, Oversize; a flip inside the embedded signer certificate as UntrustedTokenSigner or CertificateExpired (rule 4 before 5, as specified) | none |
| Forged, expired, not yet valid, replayed, wrong-permission tokens | 25 | token signed by the foreign authority's ImTokenSigner; by trusted leaves of role Device, MilkyWayGate, Server, DopplerServer; by a leaf without EKU; by the device itself naming the real signer chain; body tampered under the original signature; signature under another domain tag; chain names signer B, signed by key A; signer certificate expired and not yet valid; chain without intermediate, with a foreign intermediate, with the issuing CA as leaf; expiry at the instant, 1 ms before and after, no skew on expiry; not-before at the skew edge and 1 ms past; lifetime at the policy and 1 ms over; inverted lifetime; long.Min/Max times; the same message twice, across two sessions; replay at the window edge and 1 ms past (both edges, future-dated too); a refused message resent with a good token and the same id; the same id from two devices; session-only, kind-named, prefix-named permissions; a handshake token without im.session.open; unknown kinds with a matching permission | UntrustedTokenSigner, BadTokenSignature, CertificateExpired, TokenExpired, TokenNotYetValid, TokenPolicy, Replayed, Stale, WrongPermission, UnknownKind | **a token from a second ImTokenSigner leaf of the trusted CA verifies** (F1, expected by rule 4; see Doppler row); a chain that includes the root verifies (O2) |
| Device certificates: foreign authority, wrong role, revoked, without EKU | 11 | foreign-authority device certificate with a real token for its key; certificates of role Server, MilkyWayGate, ImTokenSigner, DopplerServer as the device; no EKU; client-auth EKU only; certificate on the sandbox CRL; expired and not yet valid; chain without the intermediate; the issuing CA as the device; random bytes as a certificate | UntrustedDevice, CertificateExpired, Malformed, always after a valid knock with the refused frame | none; a device certificate that expires mid-session does not end the session (O3) |
| Unenrolled and revoked subjects | 5 | unenrolled device with a real token (handshake and direct); revoked before the handshake; revoked mid-session; role mismatch and key-id mismatch between token and registry; a corrupt registry record (key id not hex); a token of one world in another world | NotEnrolled, Revoked, EnrolmentMismatch, Malformed (fail closed), UntrustedTokenSigner | none; revocation takes effect on the next message and the board keeps only the last verified beat |
| A valid token with a different device key | 5 | device B presents A's token in the handshake; message signed by B carrying A's token (direct and inside A's session); authority-signed token naming subject A with key B; role swapped in an authority-signed token; a valid message of B inside A's session; message signature under the Response tag, with no tag, over another body, all zero | DeviceKeyMismatch, BadMessageSignature, EnrolmentMismatch, SessionMismatch | none; outside a session (the internal proxy path, rule 10 not applied) B's valid message verifies, as specified |
| Handshake out of order | 8 | message, established, ack, refused, accept, response, empty frame before a knock; second knock and a message while waiting for the response; knock and response inside an established session; a response replayed into a fresh connection; a response signed without the challenge or by another enrolled key; response 1 ms after the handshake deadline, exactly at it, a knock after its connection's deadline, an established session an hour later; the client against a foreign gate, a Server-role gate, a flipped accept, an accept of another knock; the established frame bound to the session id | quiet before a knock; OutOfOrder, BadResponseSignature, HandshakeTimeout; the client refuses with UntrustedGate, BadGateSignature, WrongSession | none |
| The quiet answer (I5) | 8 | random frames of every length 0 to 200; wrong secret, flipped secret, empty secret; secret rotation (current, previous, retired); knock time edges; knock replay; every single-bit flip of a valid knock, 72 and 74 bytes, MAC under another tag; an oversize first frame; a scan of every frame the gate sent for the knock secret, the subject, the agent version and the token | no byte back, counted as KnockRefused, KnockStale, KnockReplayed, Oversize, Malformed | none |
| Floods within the simulator's limits | 7 | replay memory filled to capacity (50) then one more, from the same and another device, health report state, recovery after the window; 1500 unsignable or unverifiable messages against a capacity of 10; knock memory filled with valid knocks (20), one more, recovery; 1000 knocks without the secret against a capacity of 5; 200 devices with a session and three beats each; 5000 closed connections before a knock; the board keeps the newest by sent-at | ReplayCacheFull (health Degraded, then Healthy again); nothing without a valid signature or secret reaches either memory | **whoever holds the shared secret can hold the knock memory full for one skew window** (F2) |
| Doppler: enrolment, attestation, token issue, the mutual watch | 10 | the full pair (TPM, agent, Doppler, registry, gate, board, both health watches); a token from a second ImTokenSigner leaf; no plan, one approver under four eyes, policy (Secure Boot, disk, TPM, package), a quote over other measurements, certify by another TPM, CSR for another key, unknown, consumed and expired challenges; token for an unenrolled subject, proof by another key, proof for another subject, challenge reuse, revoked subject; revocation reaching Milky Way mid-session and at the next handshake; re-enrolment revoking the previous identity; token lifetime within policy and expiry then renewal; health reports replayed, signed by a foreign gate, tampered, signed with the wrong role; the agent's port 138 rule and a port conflict; audit lines and counts scanned for token, key and secret hex | NoApprovedPlan, PolicyFailed, BadQuote, BadCertify, KeyMismatch, UnknownChallenge, ExpiredChallenge, NotEnrolled, BadProof, Revoked; Milky Way: Revoked, TokenExpired; health: Replay, Chain, Signature | the F1 token opened a session, **and Doppler's watch raised `alarm.milkyway.unknown_token` and reported Unhealthy on the next health exchange**: the compensating control works |
| Counts, never content (I3) and no switch | 5 | the refusal log interface by reflection; counters snapshot, health report canonical bytes and keys; exception messages; `VerifiedMessage` has no public constructor; settings have no skip, allow, debug or trusted property | | none |

Invariants of DMP 04: I1 (verified implies authorised) held in every case above; I2 (no replay inside the window) held including both edges; I3 held by
construction and by scan; I4 (no algorithm choice) held for P-384, RSA, explicit-parameter P-256 and a 70-byte signature; I5 held for every frame tried
before a valid knock; I6 (canonical form) held for envelope, token and heartbeat.

## Part 2: the TLS additions, on the PR branch

Lane 3b pointed out that the signed scope file (with the transport row and the "TLS additions" methods) lives on the PR branch, not on `main`;
part 1 had read `main`. Part 2 ran on the branch at 2e42d2e with the branch's DMP 04 and 05 (sections "Transport encryption", "Resource limits",
"The enrolment door", the low-s rule) and the sources `CM.MilkyWay/src/Transport`, `CM.MilkyWay/src/Simulation` (`SandboxWorld.ConnectStream`,
`InMemoryDuplex`, the timer-driven `SimulatedClock`), `cm.os/src/Im/ImTransport.cs`, `ImDoor.cs`. Not read: `CM.MilkyWay/tests`, `cm.os/tests`,
`CM.MilkyWay/docs/supernova`, the PR body. The part 1 suite was run again on the branch; three expectations moved with the branch's own rules
(hand-made signatures must be low-s; an enrolment plan is used up by its enrolment; `Challenge()` fails closed when the table is full) and are
recorded as such in the tests, not as findings. All TLS ran over the in-memory pipe; no socket.

| Method (TLS additions) | Cases | What was tried | Refused as | Got through |
|---|---|---|---|---|
| Transport: certificates | 7 | TLS 1.3 mutual, handshake, heartbeat, counters (baseline); no device certificate, then an enrolled device's knock, door and response; device certificates of the foreign authority, of role Server with client-auth EKU, MilkyWayGate, ImTokenSigner, client-auth only; expired and not yet valid; the device against a gate whose TLS certificate is foreign, Server or DopplerServer; TLS certificate of device A with the IM response of device B; a relay holding a genuine MilkyWayGate certificate forwarding a genuine gate's accept | gate: UntrustedDevice, TransportCertificateExpired, TransportNoCertificate, TransportMismatch; device: TransportRefused, TransportMismatch | none; the relay is caught by the device (the accept's gate certificate is not the one TLS showed) |
| Transport: not TLS 1.3 | 2 | TLS 1.2 only; a plaintext IM frame, an HTTP request line, 512 random bytes written before TLS; the options pinned (1.3 only, no resume, no renegotiation, encryption required, client certificate required, no revocation fetch, custom root trust, no downloads, role word as name), the negotiated suite is an AEAD suite of 1.3 | TransportRefused; the peer gets a TLS alert or a close | none |
| Transport: slow, stalled, too many | 7 | a connection that sends nothing; TLS up then silence; knock answered then silence; a length prefix then silence; the first bytes of a client hello then silence (the deadline covers TLS); an idle established session, with heartbeats moving the deadline; connection limit 3 with a fourth closed without a byte and the slot coming back; uncertified limit 2 with a third closed after TLS while an enrolled device still gets in; 20 devices at once at the limit, one more refused, all 20 heartbeats verified | HandshakeTimeout, IdleTimeout, ConnectionLimit | none |
| Transport: framing | 3 | a prefix announcing 2 GiB before the knock (quiet, four bytes read) and an oversize frame inside a session (refused); a zero-length frame; a frame cut short then closed; inside TLS, garbage, a wrong-secret knock, a message and a door frame before the knock | Oversize, Malformed, KnockRefused: quiet before a valid knock, as on the frame-only path | none |
| Transport: confidentiality on the wire | 1 | a tap on the gate's end of the pipe through a full session: token, knock secret, subject, agent version, the IMM and IMT magics, the device certificate DER and its SubjectPublicKeyInfo | none of them appears in clear | |
| Low-s (I6 on the branch) | 2 | the high-s twin of a valid message, token, response and accept signature; id8 bytes above 0x7F in the agent version (would decode to a printable `?`), in a kind and in a subject | BadMessageSignature, BadTokenSignature, BadResponseSignature, the device's BadGateSignature; Malformed | none; the one spelling still verifies and the twin used up no id |
| The enrolment door | 3 | a door frame before the knock, after the accept (answered through a fake Doppler door, nothing verified, nothing on the board, counted DoorFrame), then a response with a door-gained identity on the same connection; a door frame after established; a third frame against a limit of 2; a payload over the setting; Doppler answering nothing or too much; the handshake deadline across door frames; a gate without a door; a limit of 0; over TLS without a certificate the door works and the response is still refused; door payload bytes do not appear in the health report | quiet, OutOfOrder, DoorLimit, Malformed, DoorRefused, HandshakeTimeout, DoorClosed, TransportNoCertificate | none |
| Doppler on the branch | 2 (changed or new) | re-enrolment on the used-up plan (refused, the first identity untouched), then on a new plan (the old identity revoked); open challenges bounded at the setting, failing closed and recovering when they expire or are consumed | NoApprovedPlan; `Challenge()` returns null, counted `challenge.refused.full` | none |

## Findings

**F3 (part 2), a fake gate learns the device certificate before the device refuses it.** The device's own check of the gate certificate
(`ITrustAnchors`, role MilkyWayGate) runs after the TLS handshake has completed at the record layer, as .NET's `SslStream` validates the
remote certificate in managed code once the handshake is done. A party that terminates TLS with any certificate, even a self-signed one,
therefore completes the handshake, holds the device's certificate (opaque subject, key id, the new authority's name) and only then is
refused (`T22`). Nothing of IM leaves the device (no knock, no token), the certificate is public data by design and the subject is opaque
(C3), so the exposure is a fingerprint of "a CMOS device of this authority with this key id" to whoever can stand in the path. The gate side
has the same shape (`T03`: the gate completes TLS with an untrusted client certificate, then refuses before any IM byte is read), with no
further exposure. Recommendation: say so in DMP 05 under "Transport encryption" (the role check replaces the name check and happens after
the record layer's handshake), and keep it in the assumption audit; the mitigation, if one is wanted, is wave 2's listener pinning the gate
key ids a device accepts (the device already receives them with its enrolment) so that the check can run inside the handshake.

**F1, the verifier trusts the role, not Doppler's key (rule 4).** Any leaf the trusted CA issues with the `ImTokenSigner` EKU can sign tokens the gate
accepts, so the issuing CA endpoint is inside the verifier's decision. DMP 04 says so implicitly ("the token's signer chain validates to a trusted root
in the role ImTokenSigner"); the ca-plan makes Doppler the only requester at the CA. The second control is in place and tested: Doppler's health watch
flags any accepted token id it did not issue and turns Unhealthy. Recommendation for the Milky Way pull request: write this inheritance into DMP 06
("the issuing CA endpoint sits inside the verifier's decision") and consider, for wave 2, pinning the accepted signer key ids from Doppler
(`DopplerServer.AuthorityKeyIds` exists; the verifier does not consume it). Not a breach of a rule; a design note.

**F2, the knock memory is a shared resource.** Valid knocks (right secret, fresh time) fill one memory of `replayCapacity` nonces for one clock skew.
A client holding the shared secret can fill it and keep every new session out for that window; messages of established sessions are not affected and
knocks without the secret never reach the memory. DMP 04 lists per-connection and per-subject rate limits as wave 2; this run confirms the gap is real
at the knock, not only at messages. Recommendation: a separate `milkyway.im.knockCapacity` setting and a note in the invariants list; the shared
secret stays "a gate, not an identity", so rotation after misuse is the answer, not detection at the gate.

## Observations

- **O1** A heartbeat payload with version byte 1 is counted `UnknownVersion`; DMP 04 rule 15 names `Malformed`. Both refuse; align the text or the code.
- **O2** A token chain that also carries the root (3 certificates) verifies; DMP 04 says the chain "never" carries the root. Harmless; either refuse a
  chain longer than the configured depth or soften the text to "need not".
- **O3** The device certificate is checked at the handshake only; a certificate that expires or is revoked at the CA mid-session does not end the
  session. The token (at most 24 h by policy, 1 h from Doppler) and the enrolment registry (checked per message, rule 9) bound the exposure, and
  Doppler revokes registry and certificate together. Matches the DMP; worth one sentence there.
- **O4** `issued at` is not checked by any rule (a token issued in the far future verifies). Matches the DMP; say so, or add it to rule 6.
- **O5** The handshake deadline is enforced when a frame arrives; a connection that sends nothing is never closed by the gate, which has no I/O. The
  wave 2 listener must own idle timeouts.
- **O6** Rule 4 is checked before rule 5, so a bit flip inside the embedded signer certificate is counted as a chain refusal, not as a bad token
  signature; the spec's order, recorded here so the counters are read right.

- **O7** The resource counters of the transport (`Active`, `Uncertified`) move on the gate's side of TLS, after the device sees its handshake
  complete; a test (or a listener's metric) that reads them right after the client's handshake sees the previous value. Not a security
  matter; recorded because part 2 hit it twice before waiting on an answered knock.
- **O8** The branch's DMP 04 says the quiet answer "still holds inside TLS"; it does, for every frame tried (`T12`), and the TLS alert on a
  refused certificate is the one byte sequence a scanner now gets back (as the DMP already says).

## Stop conditions

None met in either part: nothing outside the simulator answered, no real value appeared, the signatory did not say stop. Both parts ran to completion without a platform safety-filter stop.
