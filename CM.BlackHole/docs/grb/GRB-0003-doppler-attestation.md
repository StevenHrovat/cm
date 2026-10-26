# GRB-0003 · Doppler's TPM attestation and the agent on the sandbox

Security context: this is an authorised test of our own controls under the defensive charter in `CLAUDE.md`. It never targets a third
party, never develops malware, never harvests credentials, never evades detection on anyone else's systems and never touches a system
that is not listed under Targets. It runs on synthetic data, in memory and against a software TPM on the runner itself.

| Field | Value |
|---|---|
| Id | GRB-0003 |
| State | **signed** 2026-10-25 12:33Z; the run may start |
| Signatory | the owner of the framework |
| Signature | the owner, 2026-10-25 12:33:31Z, in the conversation of the Black Hole lane L4b: "I sign GRB-0003, and the helper may run it." (message cmsg_01Q5KHMD78Sa1TBrCksHzT1y47KN7nQiSvhtgtcVRsr25o); "the helper" is a separate session, not a subagent of the lane that built Doppler (the lane's answer to the owner, 12:33Z) |
| Component and claimed level | the Doppler server's enrolment decision with TPM attestation (an earlier change: MakeCredential, key certification, PCR 7 quote, the Secure Boot replay, the reported state), its token issue, and the agent loop (`AgentLoop`, `TlsGateConnector`); Supernova level 3 (DMP chapter 06) |
| Who runs it | a fresh helper session that did not build Doppler and never saw the builder's notes, with DMP chapters 04 to 06, this file, `CM.BlackHole/docs/doppler.md` and the `src` folders only; it writes its tests in a throw-away test project and reports back |
| Time window | 2026-10-25 12:33Z to 2026-11-08 12:33Z, sandbox only |

GRB-0001 covered Doppler's first enrolment (before TPM attestation) with ten cases; this file covers what an earlier change changed.

## Targets

| Target (role) | Where it runs | Interface |
|---|---|---|
| the Doppler server | in process, with the sandbox authority, `SimulatedClock` and an in-memory or temporary-folder store | `BeginEnrolment`, `FinishEnrolment`, `IssueToken`, `Challenge`, through `DopplerDoor` and Milky Way's gate in the simulator |
| the TPM evidence checks | `CM.BlackHole/src/Doppler/Tpm` | the parsers and checks, in process |
| a software TPM | `SimulatedTpm`, and swtpm started by the tests on the loopback interface of the runner (`CM.BlackHole/tests/Doppler/Tpm/Swtpm.cs`) | TPM 2.0 commands through `TpmCommands` |
| the agent loop | `AgentLoop` with `TlsGateConnector` over the simulator's in-memory stream (`InMemoryDuplex`) | the door and the session, as in `tools/sandbox-pair` |

## Out of scope

- every production system and every system of the running company, including the cluster, the dev host's other services, the network
  and any real TPM or vTPM (a Windows machine's TPM is the VM proof's, not this run's);
- every system of a third party; the old certificate authority; real users, groups, mailboxes and devices;
- any socket other than the loopback socket of a swtpm the test itself started.

## Methods allowed

Endorsement keys that are not RSA, too short, or not the TPM's; attestation keys that are not restricted, not fixedTPM, not
sign-only, or with another name algorithm; a credential sealed to another TPM, or answered wrongly; a key certification by another
key, of another key than the request's, with stale qualifying data, or with a signature of another scheme or hash than its key names;
quotes with wrong or replayed qualifying data, another PCR selection or bank, a PCR 7 value the event log does not replay, an event log
that shows Secure Boot off or ends early; a reported state that says the disk is not encrypted or names an agent package that is not
allowed; challenges unknown, expired, used twice or for another step; plans missing, with one approver under four eyes, or used up;
token requests with a wrong proof, for a revoked or unknown subject; every field of the door's payloads truncated, extended, oversized
or bit-flipped; many open challenges and pending enrolments up to and past the cap; clock skew at the edges of every lifetime. For the
agent: a gate that refuses, closes, stalls or is not genuine (a certificate of the foreign authority), at each step of the loop.
Tools: the CM test projects, the simulator and swtpm.

## Data

Synthetic only. No real host name, address, person, endorsement key or credential.

## Stop conditions

Anything outside the simulator or the test's own swtpm answers; a real value appears; the signatory says stop.

## Report

Into the Doppler pull request that claims level 3, and summarised under Result.

## Result

Not run.
