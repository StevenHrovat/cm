# Doppler on the sandbox (draft, wave 1)

Security context: defensive security engineering under the charter in `CLAUDE.md`. Doppler attests the operating organisation's own enrolled machines;
everything here runs on synthetic machines in memory.

Lane L4, completion goal 1. Decisions: enrolled roles, laptop included, and enrolment is a plan with four eyes; the agent owns
port 138 and blocks NetBIOS; Doppler signs IM tokens; Doppler and Milky Way watch each other. Contracts it uses:
[cm.os/docs/trust.md](../../cm.os/docs/trust.md). The CA it requests from: [ca-plan.md](ca-plan.md).

## What happens, in order

1. **A plan names the machine.** `EnrolmentPlan(plan id, endorsement key id, role, approvers)`; with four eyes on (setting `FourEyes`,
   default on) it needs two distinct approvers. In a deployment the endorsement key id comes from the vendor's registration of the device.
2. **The agent starts.** It switches NetBIOS over TCP/IP off and binds TCP and UDP 138 for itself. If another process holds 138, the agent
   stops in `PortConflict` and does not enrol.
3. **Enrolment, in two steps** (lane L4b goal 2, section "TPM attestation" below). *Begin:* the agent sends a single-use challenge
   (5 minutes), its TPM's endorsement key and its attestation key (a restricted RSA signing key the TPM created). Doppler checks that a
   plan names that endorsement key and that the attestation key is TPM-bound, then seals a fresh random credential to both
   (TPM2_MakeCredential) and answers with a second challenge. Only the TPM that holds that endorsement key, and holds that attestation key,
   can open it. *Finish:* the agent returns the opened credential, a P-256 device key created in the TPM and certified by the attestation key
   (TPM2_Certify), a PCR 7 quote with the firmware's PCR 7 events (TPM2_Quote), the reported state (disk encryption, the agent package's
   hash) and a PKCS#10 request signed by the device key. Certify and quote are bound to the second challenge; the certify also binds the
   request and the reported state. Doppler checks: the credential, the certify (the key is TPM-generated, non-exportable, a P-256 signing
   key, the request's key), the quote (signature, PCR 7 replayed from the events, Secure Boot measured on by firmware), the policy (Secure
   Boot on, disk encrypted, package hash allowed). Then it gives the machine an opaque subject (`dev-<hex>`, never a host name), asks the
   issuing CA for a Device certificate (profile from the CA plan; Doppler sets every extension, nothing is copied from the request), and
   writes the register Milky Way reads. Enrolling the same machine again revokes its old identity in the same store write that uses up the
   plan and registers the new one (GRB-0003 F5), so a plan withdrawn meanwhile leaves the old identity untouched; the CRL follows at once.
   Every malformed part of a finish (a request or public area the platform cannot parse, a missing field) is refused with its code and
   audited, never thrown (GRB-0003 F4).
4. **Tokens.** The agent proves possession of its device key over a fresh challenge; Doppler checks the register (enrolled, not revoked)
   and the proof, and mints an IM token in Milky Way's format (`ITokenEncoder`, adapted to `Im`'s codec in the composition root) with
   the role's permissions (IM v0: `im.session.open`, `im.heartbeat.send`) for `TokenLifetime` (default 1 hour), signed with the
   ImTokenSigner key.
5. **Revocation.** `Revoke(subject)`: the register says revoked at once (Milky Way refuses the next message), the certificate goes on the CRL.
   A plan is **used up** by the enrolment it allows, so a revoked machine cannot enrol again on its old plan; a new enrolment needs a new plan.
6. **Watching each other.** Doppler signs its health report with its DopplerServer key; Milky Way signs its own with the MilkyWayGate key.
   Each side's `PeerWatch` accepts a report only with the right role, a valid chain and signature, a growing sequence and no future time;
   a refused report turns the peer **Untrusted**, a silent one **Stale**. Doppler also checks Milky Way's security: every token id Milky Way
   reports as accepted must be one Doppler issued; any other raises `alarm.milkyway.unknown_token` and Doppler reports itself Unhealthy.

Every decision is counted by reason (`Counts`) and written as an `AuditLine` (action, subject or plan, outcome), never with a key, token or
message content (test `The_audit_names_actions_and_subjects_and_never_a_key_or_a_token`).

## Settings (`DopplerSettings`)

| Setting | Default | What it does |
|---|---|---|
| `ChallengeLifetime` | 5 minutes | how long a challenge stays usable |
| `DeviceCertificateLifetime` | 365 days | Device certificate lifetime (CA plan profile Device) |
| `TokenLifetime` | 1 hour | IM token lifetime (Milky Way caps it at its own maximum) |
| `FourEyes` | on | two distinct approvers per enrolment plan |
| `PeerStaleAfter` | 2 minutes | Milky Way counts as stale after this without a valid report |
| `AllowedClockSkew` | 30 seconds | how far a peer report may be ahead of Doppler's clock |
| `AllowedAgentPackages` | empty (refuses all) | SHA-256 of the agent packages Doppler accepts |
| `MaxOpenChallenges` | 10000 | open challenges and pending enrolments at once; the door is reachable by anyone with the knock secret, so the table is bounded. When it is full the oldest open challenge makes room (`challenge.evicted`), so a holder of the shared secret cannot take token renewal away from the enrolled machines (GRB-0003 F2); only pending enrolments, which need an approved plan, are never pushed out, and when they alone fill the table it refuses (`challenge.refused.full`) |
| `IssuedTokenMemory` | 1 day | how long an issued token id is remembered after expiry, for the unknown-token alarm |
| `AllowSha1Attestation` | on | accept TPM evidence signed with SHA-1 (and keys named with SHA-1); Windows' platform crypto provider makes attestation keys that sign RSASSA SHA-1, so off refuses every Windows machine. Risk while on: with a SHA-1 chosen-prefix collision (costly) the owner of a planned machine could make its own TPM sign a forged quote |

## What is simulated, and what replaces it

| Sandbox | Deployment (wave 2 onwards) |
|---|---|
| `SimulatedTpm`: a software TPM 2.0 (RSA endorsement and attestation keys, P-256 device key, TPM-format certify and quote, a synthetic PCR 7 log); never evidence of a real machine | `PcpTpm` on Windows (the platform crypto provider, keys non-exportable), `CommandTpm` where the agent may command the TPM itself; later vPro |
| `SimulatedDeviceHost`, `SimulatedNetBios`: who holds which port | the agent sets NetBIOS over TCP/IP off per interface and binds 138 exclusively |
| `DirectChannel`: the agent calls the server object (Doppler's own unit tests only) | `DoorChannel`: the calls travel as IM through Milky Way's **enrolment door** (DMP chapter 04, "The enrolment door"), routed to Doppler only; built in wave 2 (lane L4b) |
| `InMemoryDopplerStore` | `FileDopplerStore`: plans, register and issued token ids in one JSON file on the Doppler server (below) |
| `SandboxAuthority` as `ICertificateIssuer` | the CMOS issuing CA's constrained endpoint (ca-plan.md section 6) |

Supernova level: 3 (DMP chapter 06). Refusal suite, mutation testing, fuzzing with a kept corpus, resource limits and the content scan are
met (lane L4b goal 4). GRB-0003 is signed; its first run (2026-10-25) found F1 to F6 below, which are fixed; the run that counts is a fresh one on
the Type IIb test bed, so level 3 is claimed, not passed, until it reports. Evidence: [supernova/doppler.md](supernova/doppler.md).

## TPM attestation (lane L4b, goal 2)

What Doppler **attests** (the TPM signs it, bound to Doppler's challenge) and what it only **hears** (the agent reports it):

| Claim | How Doppler knows | Code |
|---|---|---|
| this machine is the planned one | the plan names its endorsement key id (SHA-256 of the SubjectPublicKeyInfo); the credential sealed to that key came back | `TpmCredential.Make`, `DopplerServer.BeginEnrolment` |
| the attestation key lives in that TPM | MakeCredential binds the attestation key's name; its public area says fixedTPM, fixedParent, sensitiveDataOrigin, restricted, sign, no decrypt, RSA 2048+, SHA-256 names | `TpmEvidence.CheckAttestationKey` |
| the device key is TPM-generated and cannot leave it | TPM2_Certify by the attestation key over the device key's name; its public area says fixedTPM, fixedParent, sensitiveDataOrigin, sign, not restricted, no decrypt, ECC P-256 | `TpmEvidence.CheckKeyCertification` |
| the request is for that key | the request's key equals the certified key; the certify's qualifying data binds the request's hash | `EnrolmentStatements.CertifyData` |
| Secure Boot is on | TPM2_Quote of PCR 7 (SHA-256 bank) by the attestation key; the events replay to the quoted value; every event's digest is the hash of its data; exactly one separator; `SecureBoot` = 1 measured before it; none says 0; no error separator. **No decision rests on an event's type** (the type is the agent's word, only the data's digest is measured; GRB-0003 F1): a separator is known by its type or its data (four bytes all zero, or all one for the error separator), the SecureBoot variable by its data whatever its type | `TpmEvidence.CheckQuote`, `SecureBootPolicy` |
| disk encrypted, agent package hash | **reported**, not attested: bound into the certify's qualifying data (so it cannot be changed in transit), but the agent says it | `ReportedState` |

Qualifying data: certify = SHA-256 of `Canonical("CM0/doppler/certify", challenge, SHA-256(request), disk byte, package hash)`; quote =
SHA-256 of `Canonical("CM0/doppler/quote", challenge)`. Both challenges are single use; the pending step between begin and finish is
bounded together with the open challenges (`MaxOpenChallenges`). Every Secure Boot verdict is counted (`enrol.secureboot.<verdict>`).

**What PCR 7 cannot prove (the documented limit, GRB-0003 F1).** A quote proves PCR 7's value; the log says what was extended. Because any
process with TPM access may extend PCR 7 after boot, PCR 7 alone cannot tell a firmware's measurements from a software copy of them **when
the firmware measured nothing into PCR 7 at all**: software can then extend a complete, honest-looking sequence (SecureBoot = 1 and a
separator). Every firmware that follows its TCG profile closes this: UEFI firmware measures the SecureBoot variable (0 or 1) and a separator
at every boot, and conventional BIOS measures separators into PCRs 0 to 7, so a software copy adds a second separator or sits after the
first, and both are refused. What remains needs a TPM on a firmware that measures nothing into PCR 7, local administrator rights on it,
**and** an approved four-eyes plan that names that very TPM's endorsement key; the plan is the control for it. Pinned by
`TpmEvidenceEdgeTests.A_firmware_that_measured_nothing_into_PCR7_is_the_documented_limit`. Closing it fully needs more than PCR 7 (a
golden PCR 0 to 7 register per device model, or the platform certificate); that is the follow-up in the Supernova evidence.

**The endorsement key is trusted because the plan names it**, not through the vendor's EK certificate chain: the person who writes the
plan reads the EK id on the machine (the agent's diagnose command prints it) or from the vendor's registration. Checking the EK certificate chain is a
later step (BACKLOG of this lane).

**The real TPM path.** `TpmCommands` marshals the few TPM 2.0 commands the agent needs by hand (CreatePrimary, ReadPublic,
StartAuthSession, PolicySecret, ActivateCredential, Certify, Quote, Sign, PCR_Read, PCR_Extend for tests, FlushContext), with empty
passwords and the endorsement key's standard policy; it sends a command again when the TPM asks (`TPM_RC_RETRY`, `YIELDED`,
`TESTING`), bounded. Two `IDeviceTpm`s use it:

| Where | Keys | Certify and quote | PCR 7 events |
|---|---|---|---|
| `PcpTpm` (Windows, the agent) | the platform crypto provider's: an identity key (`PCP_KEY_USAGE_POLICY` = identity) as attestation key, an `ECDSA_P256` key as device key, both LocalSystem's, non-exportable; the endorsement key from `PCP_EKPUB`; activation through `PCP_TPM12_IDACTIVATION` | `TpmCommands` through the provider's own TBS context (`PCP_PLATFORMHANDLE`) with the provider's handles of its keys, as go-attestation does it | `Tbsi_Get_TCG_Log` |
| `CommandTpm` (swtpm in CI; a TPM the agent may command directly) | the TCG default endorsement key template, the attestation key a primary of the endorsement hierarchy, the device key a primary of the owner hierarchy picked by a stored unique (not secret) | `TpmCommands` | the machine's TCG log (in tests: the events the test extended PCR 7 with) |

The provider's identity keys sign RSASSA with SHA-1 (go-attestation: "PCP-created AK uses SHA1"), hence `AllowSha1Attestation`.
`PcpBlobs` parses the provider's own blobs (`BCRYPT_RSAKEY_BLOB`, the TPM 2.0 identity binding) and is tested on Linux; the calls
themselves (`PcpNative`) run only on Windows and are first proved on the test VM by the agent's **diagnose** command
(`TpmDiagnosis`): it runs every enrolment step against the machine's TPM with keys of its own, checks each with Doppler's checks,
prints the endorsement key id for the plan and, per step, "ok" or "FAIL" with algorithm ids, attributes and sizes; public data only.

**Proof the structures are right:** `CM.BlackHole/tests/Doppler/Tpm/fixtures` are bytes from a real TPM 2.0 implementation (swtpm on
libtpms) made by `tools/tpm-fixtures/make-fixtures.sh`; `TpmFixtureTests` parses its attestation key, certify and quote and checks the
signatures with Doppler's code, and the script shows the TPM opening a credential sealed by Doppler's `TpmCredential.Make`.
`SwtpmEnrolmentTests` goes further: a machine whose TPM is swtpm enrols through Doppler's checks end to end (`CommandTpm`), signs with
a device key that lives in the TPM, and is refused when its firmware log says Secure Boot off or PCR 7 holds more than the log; a
credential for one TPM does not open in another. CI installs swtpm and requires these tests (`CM_REQUIRE_SWTPM=1`); without swtpm
they are skipped and say so. The refusal suite is `CM.BlackHole/tests/Doppler/EnrolmentTests.cs`.

## The agent service and its package (lane L4b, goal 3)

`CM.BlackHole/agent` is the agent on Windows: `cm-doppler-agent.exe`, a .NET 10 Windows service (LocalSystem), published
self-contained for win-x64 as one file. It composes three pieces that are tested on their own:

| Piece | What it does | Tested by |
|---|---|---|
| `AgentLoop` (src) | one round per `Step()`: take port 138, restore the kept enrolment or enrol through the door, renew the token through the door when it expires within 5 minutes, open a session with the Device certificate and send the heartbeat; failures back off 5 minutes doubling to 1 hour | `AgentLoopPairTests`: the real gate and Doppler in the simulator; enrol, restart, renew, refused, revoked, unreachable |
| `TlsGateConnector` (src) | the door connection over TLS 1.3 without a certificate (the accept frame must name the gate certificate TLS showed), the session with the Device certificate | `AgentLoopTlsPairTests`: Milky Way's `GateTransport` over an in-memory pipe and over real TCP |
| `PcpTpm`, `WindowsDeviceHost`, `WindowsReport` | the TPM (Platform Crypto Provider), NetBIOS off and port 138, BitLocker and the agent's own hash | on Windows only: `cm-doppler-agent diagnose` and the VM proof |

**The agent believes only what it can check (GRB-0003 F3, F6).** Doppler's enrolment answer is taken only when its certificate chains to the
agent's trust roots in role Device, names the subject the answer names, and is for the device key in the TPM; otherwise the agent counts
`BadAnswer`, stays unenrolled and tries again next round. A kept `state.json` that does not load, has empty fields, or fails the same checks
is treated as absent, and the agent enrols afresh instead of stopping.

`FileAgentStore` keeps the subject, the Device certificate and chain (public) and the last heartbeat sequence in `state.json`; no key
(the device key stays in the TPM) and no token (a new one comes through the door). `agent.json` holds the gate's name and port, the
knock secret and the trust roots, written outside the repository. Both live in `%ProgramData%\CM\DopplerAgent`, readable by SYSTEM
and Administrators only.

The package is an Intune Win32 app: `tools/doppler-agent/package.ps1` builds it, `install.ps1` and `uninstall.ps1` are its command
lines (SYSTEM, idempotent, dry run without `-Apply`), and `tools/doppler-agent/README.md` has the Intune fields and the VM proof
with `cm-doppler-testhost`. Uploading or assigning the package in Intune is gated.

## The door and the register (lane L4b, goal 1)

The agent reaches Doppler only through Milky Way's enrolment door. One door connection: knock, check the gate, then door frames
(challenge, then enrol). After the enrolment the agent opens a new connection (with its certificate once TLS is in front of the gate):
knock, check the gate, door frames (challenge, then token), then the response with the fresh token on the same session, and heartbeats.
`DopplerDoor` (Doppler's `IImDoor`) parses the payload strictly (`DopplerWire`), calls the server and answers, or answers nothing.

The register **lives with the Doppler server**: `FileDopplerStore` keeps plans (by endorsement key id, single use), the register (by
subject: role, KeyId, endorsement key id, the public Device certificate, enrolled and revoked times, whether the revocation reached the
CRL) and the ids of issued tokens in `doppler-register.json` in Doppler's data folder (its access list: Doppler's account and
administrators). No key, no token, no secret is in it. Writers take a lock file, re-read, change and replace the file atomically;
readers reload when it changed. So the **admin command** (`DopplerAdmin`: add a plan, remove a plan, revoke), which runs as a second
process next to the server, takes effect at once: a revocation is seen by the verifier at the machine's next message, and the server's
`SyncRevocations` puts the certificate on the CRL. Tests: `tools/sandbox-pair/tests/DoorPairTests.cs` (group `pair`) and
`CM.BlackHole/tests/Doppler/StoreAndWireTests.cs`.

## The sandbox pair (acceptance test of L4 goal 1)

`tools/sandbox-pair/tests` is the composition root that puts Doppler and Milky Way's real gate side by side, on one simulated clock and
one sandbox authority; it is a test project outside the tree so that no constellation references another (C1, DMP-008). The `pair`
group of `tools/verify` runs it: an enrolled machine's heartbeat is accepted and shown verified on the board; a machine with a genuine
certificate and a genuine token but no enrolment is refused at Milky Way (`NotEnrolled`); a machine Doppler revokes is refused on its
next message; a device of a foreign authority (the old CA's stand-in) fails the handshake; Doppler and Milky Way each show the other's
signed health, see the other go stale, and refuse a report signed with the wrong role. Milky Way's report is assembled in the harness from
its counters and board until `CM.MilkyWay` implements `IHealthSource` itself (agreed with the Milky Way lane), which also lets Milky Way
report the token ids it accepted for Doppler's unknown-token alarm.
