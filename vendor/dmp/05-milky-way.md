# 05 · Milky Way, the universal I/O layer

Milky Way is the one door into CMOS, out of it and between its parts (constitution C1). It speaks IM ([04](04-im.md)), checks every
message, and can simulate I/O so that everything, the cold start included, can be tested in a sandbox. This chapter holds what IM
version 0 needs of Milky Way: the gate, the quiet answer, the verifier, the heartbeat board, the simulator, the settings and the
contract with Doppler. The protocol bricks it proxies, the shield (classes, tokens on values, grants, the canary, recipient
profiles) and the galaxy group are later sections. Every section is a draft.

Security context: this chapter is defensive security engineering under the charter in [CHARTER.md](CHARTER.md). It describes the company's own
entrance, tested on synthetic data in the sandbox.

## The parts and where they live

| Part | Where | What it does |
|---|---|---|
| IM encoding and client side | the IM repository, `src/Im` (`Im`) | token, envelope, handshake frames and heartbeat payload; the client side of the handshake. Every constellation that speaks IM uses it |
| Trust | the IM repository, `src/Im/Trust` (`Im.Trust`) | the certificate roles, the signer interface, chain validation, the enrolment registry contract, the sandbox certificate authority |
| Gate | `CM.MilkyWay` (`CM.MilkyWay.Gate`) | one session per connection: knock, accept, response, established, then messages |
| Verifier | `CM.MilkyWay` (`CM.MilkyWay.Verify`) | the verify rules of chapter 04, the replay memory, the refusal log |
| Transport | `CM.MilkyWay` (`CM.MilkyWay.Transport`), device side `Im` (`ImTls`, `ImTlsClientConnection`, `ImStreamFraming`) | TLS 1.3 with mutual certificates, frames on a stream, admission and deadlines; no IM decision |
| Heartbeat board | `CM.MilkyWay` (`CM.MilkyWay.Board`) | the last verified heartbeat per device |
| Simulator | `CM.MilkyWay` (`CM.MilkyWay.Simulation`) | the in-memory network that tests run on |
| Health | `CM.MilkyWay` (`CM.MilkyWay.Health`) | the signed health report for Doppler (`IHealthSource` of `cm.os.Health`): counts and accepted token ids |

The encoding sits in `cm.os` and not in Milky Way because senders in other constellations (Doppler's agent, the Pictor shell) must
build IM without referencing Milky Way (DMP-008). The **verify rules sit only in Milky Way**: no other part decides whether a message
is genuine.

Status: draft

## The gate

The gate is written **without I/O**: a gate session takes one frame and answers with frames to send, or the quiet answer, or
"refused and closed". The listeners on TCP 443 and 138 (wave 2) only move bytes between a socket and a gate session; the simulator
does the same in memory. That way the code that decides is the code that is tested, and the listener adds no logic.

A session moves through four states: waiting for the knock, waiting for the response, established, closed. A frame that does not
fit the state closes the session. Each session has a deadline for the handshake (`milkyway.im.handshakeTimeout`) and closes at the
first refusal; a device that is refused reconnects and starts again.

Status: draft

## The quiet answer

To anything that is not a valid knock, the gate answers the way a closed firewall port would: with nothing. No banner, no error,
no byte; the connection is closed. This reduces noise from scanners and is **never relied on**: a port that accepts a connection can
be found by anyone who times or fingerprints it. The strength is the verify rules, which hold when the port is found.

Status: draft

## Transport encryption

Every stream into Milky Way, on 443 and on 138, is **TLS 1.3 with a certificate on each side** before the first IM byte. The gate shows
its `MilkyWayGate` certificate; the device must show its `Device` certificate. Both are the same keys and certificates the two sides
sign IM with, from the new certificate authority (`Im.Trust`). Rules:

- **One version, no fallback.** TLS 1.3 only; a peer that offers anything older does not connect. No renegotiation, no session
  resumption (every connection proves its certificate again), encryption required. The cipher suites are TLS 1.3's own, all AEAD.
- **Each side checks the other itself.** The certificate the peer shows is checked by `ITrustAnchors` in the role it must carry
  (the gate checks `Device`, the device checks `MilkyWayGate`) at the time of the injected clock. The platform's own chain verdict is
  ignored, the machine's certificate store is never consulted (so the old CA is not trusted here either), and nothing is downloaded:
  no intermediate, no revocation list. A Server certificate that also carries client authentication is still not a Device.
- **No name check.** The device asks for the role word `milky-way`, never a host name; the gate is recognised by the role on its
  certificate, which only the new authority issues. That role check replaces TLS's name check.
- **The role check runs once TLS's own handshake is done.** The platform (.NET's `SslStream`) hands the peer certificate to the check
  after the record layer has completed the handshake. A party that terminates TLS with any certificate therefore holds the device's
  certificate (an opaque subject, its key id, the authority's name) before the device refuses it; nothing of IM has left the device
  then, and the certificate is public by design (constitution C3). The gate side has the same shape and no exposure beyond it. If the
  fingerprint ever matters, the listener of wave 2 can pin the gate key ids a device receives at enrolment so the check runs inside the
  handshake (GRB-0001 finding F3; assumption A17).
- **The two identities are one.** The gate binds the session to the TLS certificate: the device certificate in the IM response must be
  the one TLS showed (`TransportMismatch` otherwise). The device does the same the other way: the gate certificate in the accept frame
  must be the one TLS showed (`ImClientError.TransportMismatch`). A device therefore cannot run TLS with one certificate and IM with
  another, and nobody can relay a genuine gate's accept through a TLS session of their own.
- **A machine before enrolment shows no certificate.** It has no Device certificate yet, and Doppler's enrolment runs over IM, so
  the gate asks for a certificate but lets a connection without one through TLS. Such a connection may knock and use Doppler's
  enrolment door (lane L4, chapter 04 when it lands), and nothing else: its handshake response is refused (`TransportNoCertificate`)
  before anything in it is read, so it can never establish a session or send a message. These connections have their own, smaller
  limit (`milkyway.im.maxUncertifiedConnections`), so they can never take the slots of enrolled devices. A certificate that is shown
  and is not a Device certificate of the new authority is refused by TLS itself.
- **Failures are counted, not explained.** Garbage or an old version is `TransportRefused`; a certificate of a foreign authority or of
  another role is `UntrustedDevice`; an expired one is `TransportCertificateExpired`. The peer gets TLS's alert and the connection
  closes.
- **The verify rules do not lean on TLS.** TLS is a layer in front of the rules, never instead of one: the verifier checks every
  message in full however it arrived. TLS adds confidentiality and keeps certificate-less traffic away from every IM step past the
  knock and the door.

What TLS changes about the quiet answer: a TLS listener answers a client hello, so a scanner learns that something speaks TLS 1.3 there.
Inside TLS the quiet answer still holds: before a valid knock the gate sends no IM byte. The quiet answer was never relied on.

The transport is written once (`GateTransport`) and runs over any stream: the listeners of wave 2 hand it a socket, the simulator an
in-memory pipe (`InMemoryDuplex`). It opens no port itself.

Status: draft

## Resource limits

What one connection or many can cost the gate, and how each is bounded. Every limit is a setting.

| Limit | How | Setting |
|---|---|---|
| frame size | the 4-byte length is checked before the frame is read; one byte over the limit costs four bytes | `milkyway.im.maxFrameBytes` |
| handshake time | one deadline from the moment the connection is admitted, covering TLS and the IM handshake; a connection that sends nothing, or stalls after TLS, is closed at it | `milkyway.im.handshakeTimeout` |
| silence | an established session that sends nothing for this long is closed; every answered message moves the deadline | `milkyway.im.idleTimeout` |
| connections at once | one more is closed at once without a byte and counted (`ConnectionLimit`); slots come back when connections close or time out | `milkyway.im.maxConnections` |
| connections without a certificate | a smaller share of the above for machines before enrolment; one more is closed after TLS and counted (`ConnectionLimit`) | `milkyway.im.maxUncertifiedConnections` |
| replay memory | fails closed when full (verify rule 16) | `milkyway.im.replayCapacity` |

Deadlines run on the injected clock, so the tests move simulated time and wait for the gate to finish, never on the wall clock. Not built
yet (wave 2, with the listeners): rate limits per subject and per source address.

Status: draft

## The verifier

One implementation, `ImVerifier`, checks every message by the rules of chapter 04 in their order and fails closed. It reads time only
from an injected `TimeProvider`, trust only from `Im.Trust`, and enrolment only from Doppler's registry contract. The result of a
check is either a **verified message**, a type only the verifier can create, or a refusal reason. Every consumer (the heartbeat board
first) accepts only verified messages, so an unverified message cannot reach a consumer by mistake: the compiler refuses it.

The verifier is the component the whole project agrees to over-protect: it gets the highest Supernova class (IIb), and the proof work
starts with it.

Status: draft

## The heartbeat board

The board keeps, per device subject, the last verified heartbeat: role, sequence, status, agent version, when it was sent and when
it arrived, marked **verified**. It accepts only verified messages and keeps the newest by `sent at`. It is what the operations board
shows for Promise B once the listener exists; until then the sandbox tests read it.

Status: draft

## The simulator

The simulator connects IM clients to a gate in memory: a client sends a frame, the gate session answers, nothing touches a network
or a real system. It ships a **sandbox world**: a sandbox certificate authority (from `Im.Trust`), a gate certificate, Doppler's
token signer, an in-memory enrolment registry, and devices that can be enrolled, unenrolled, revoked, or given expired, forged or
wrong-permission tokens. Connections come in two forms: frame-only (frames handed to a gate session in memory) and stream
(`InMemoryDuplex`, a pair of in-memory streams that TLS and the frame transport run over exactly as over a socket). The simulated
clock moves only when a test moves it, and the deadlines of the transport fire then. The tests of the `im` group run there. The simulator is how every later Milky Way part is tested first,
the cold start included.

Status: draft

## Settings

Every value an administrator could tune or must supply is a setting with a default and a plain description (chapter 10). The list
is `MilkyWaySettings.Describe()`, which the settings page of the administration tool shows.

| Key | Default | What it is |
|---|---|---|
| `milkyway.im.clockSkew` | 2 minutes | how far a sender's clock may be off; bounds message freshness and the replay memory |
| `milkyway.im.tokenMaxLifetime` | 24 hours | the longest token the verifier accepts |
| `milkyway.im.handshakeTimeout` | 10 seconds | how long a handshake may take before the session is refused |
| `milkyway.im.maxFrameBytes` | 65536 | the longest frame read from a connection |
| `milkyway.im.maxPayloadBytes` | 4096 | the longest message payload |
| `milkyway.im.replayCapacity` | 100000 | how many message ids the replay memory holds before it fails closed |
| `milkyway.im.maxConnections` | 1000 | how many connections the gate serves at once; one more is closed without a byte |
| `milkyway.im.maxUncertifiedConnections` | 50 | how many of those may come without a device certificate (before enrolment: knock and door only) |
| `milkyway.im.idleTimeout` | 15 minutes | how long an established session may stay silent; longer than the heartbeat interval |
| `milkyway.im.knockSecrets` | (none, secret) | the current and the previous shared secret of the knock, from the settings store |
| `milkyway.im.trustRoots` | (none) | the root certificates the gate trusts, from the settings store |
| `milkyway.im.maxDoorFrames` | 8 | how many enrolment-door frames one session may send before the response; 0 closes the door |
| `milkyway.im.maxDoorBytes` | 49152 | the longest door payload (an enrolment's TPM evidence and Secure Boot event log) |
| `milkyway.listen.external` | 443 | the external port (wave 2) |
| `milkyway.listen.internal` | 138 | the internal port over the VPN (wave 2) |

Status: draft

## The contract with Doppler

Milky Way and Doppler never reference each other (C1); they meet in `cm.os`:

- **Doppler issues tokens** with `Im.ImTokenCodec.Issue(claims, signer)`, where the signer is an `IImSigner` from `Im.Trust`
  holding the token signing key (certificate role `ImTokenSigner`). Doppler decides the permissions per role.
- **Doppler answers the door**: `IImDoor` (in `Im`) takes the payload of a door frame and returns Doppler's answer or nothing
  (chapter 04, "The enrolment door"). The gate never reads the payload.
- **Doppler keeps the enrolment**: `IEnrolmentRegistry` (in `Im.Trust`) answers, for a subject, its role, its key id and whether it
  is revoked. The verifier consults it for every message (rule 9).
- **Milky Way trusts certificates** only through `ITrustAnchors` (in `Im.Trust`), with the roles `ImTokenSigner` for token signers,
  `Device` for senders and `MilkyWayGate` for the gate.

- **Milky Way names the token** of every verified message (`VerifiedMessage.TokenId`), so its health report to Doppler can list the
  token ids it accepted and Doppler can raise an alarm for an id it never issued (the two watch each other; health contract in
  `cm.os/src/Health`, lane L4).
- **Milky Way reports its health** with `MilkyWayHealth`, the gate's sink in front of the heartbeat board: component `milkyway`,
  signed with the gate key (role `MilkyWayGate`), a growing sequence, counts only (`board.entries`, `accepted.session`,
  `accepted.heartbeat`, `refused.<reason>`) and the ids of the tokens accepted since the last report (sessions and messages),
  never a subject or a payload. It reports degraded while the replay memory refuses messages because it is full.

Changing any of these is a contract change (constitution C4): at a stable promotion, in both lanes' pull requests.

Status: draft

## Tests: the im group

`tools/verify` runs a group `im` (the tests marked `Category=im` in `CM.MilkyWay.Tests`) before the other tests. It proves, in the
simulator: a valid device completes the handshake and its heartbeat is on the board marked verified; expired, not-yet-valid, forged
(wrong authority, tampered token, tampered message, wrong device key), unenrolled, revoked, replayed, stale, wrong-permission and
unknown-kind messages are refused and counted by reason; a scanner gets the quiet answer; the refusal log admits only enumerations;
and no secret, key, token or payload appears in anything the gate or the verifier emits. Over the transport (`ImTransportTests`): a
valid device connects with TLS 1.3 and its heartbeat is verified; nothing of the token, the payload or the device certificate is on the
wire in clear; a connection without a certificate reaches the knock but its response is refused, under its own limit; a foreign,
wrong-role or expired certificate, an older TLS version and non-TLS bytes are refused and counted; a TLS certificate that differs from the IM one is refused on both sides; stalled, silent, oversize, cut-short and surplus
connections are closed by their limit. The Supernova test tier (chapter 06) adds the refusal suite, generated inputs, the fuzz corpus and
mutation testing: `CM.MilkyWay/docs/supernova/`.

Status: draft
