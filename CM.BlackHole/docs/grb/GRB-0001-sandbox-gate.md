# GRB-0001 · The Milky Way gate and verifier on the sandbox, with Doppler

Security context: this is an authorised test of our own controls under the defensive charter in `CLAUDE.md`. It never targets a third
party, never develops malware, never harvests credentials, never evades detection on anyone else's systems and never touches a system
that is not listed under Targets.

| Field | Value |
|---|---|
| Id | GRB-0001 |
| State | **signed** 2026-10-25 05:12Z, with the TLS additions below |
| Signatory | the owner of the framework |
| Signature | the owner's words in the conversation of the Milky Way lane L3b, 2026-10-25 05:12Z: "I sign GRB-0001 with the TLS additions, and the helper may run it." (message cmsg_01Q5KHMD78Sa1TBrCksHzT1yFFVfpMMbWRLa4YCKNgr41j) |
| Component and claimed level | the Milky Way IM verifier and gate (IM version 0), Supernova level 3 for internal use, first step toward level 4 (DMP chapter 06) |
| Who runs it | a fresh helper session that did not build the verifier or the transport and never saw the builders' notes, with DMP chapters 04 and 05, this file and the simulator's public interface only; it writes its tests in a throw-away test project and reports back (allowed by the owner in the signature) |
| Time window | 2026-10-25 05:12Z to 2026-11-08 05:12Z, sandbox only |

## Targets

| Target (role) | Where it runs | Interface |
|---|---|---|
| the Milky Way gate and its verifier | the simulator in `CM.MilkyWay`, started by the sandbox pair test project | IM version 0 over the simulated internal port |
| the Doppler server and agent | the same simulator | enrolment, attestation, token issue over IM |
| the trust contract | `Im.Trust` with the sandbox authority | in-process |
| the transport (TLS addition) | `GateTransport` in `CM.MilkyWay` and the device side in `Im`, over the simulator's in-memory stream (`InMemoryDuplex`) | TLS 1.3 with a certificate on each side, then IM frames |

## Out of scope

- every production system and every system of the running company, including the cluster, the dev host's other services and the network;
- every system of a third party;
- the old certificate authority; real users, groups, mailboxes and devices;
- any real socket: the run uses the simulator only, the in-memory stream of the TLS addition included.

## Methods allowed

Malformed, truncated and oversized envelopes; forged, expired, not yet valid, replayed and wrong-permission tokens; device certificates
of a foreign authority (the old-CA stand-in), of the wrong role, revoked, or without EKU; messages from unenrolled and revoked subjects;
a valid token with a different device key; handshake out of order; floods within the simulator's limits; clock skew at the edges of
validity. Tools: the CM test projects and the simulator.

TLS additions (signed with the file): connecting without a device certificate, or with one of a foreign authority, of the wrong
role or expired; showing one certificate to TLS and another inside IM, on either side; older TLS versions and bytes that are not TLS;
slow and stalled connections and more connections than the limit, all inside the in-memory transport.

## Data

Synthetic only. No real host name, address, person or credential.

## Stop conditions

Anything outside the simulator answers; a real value appears; the signatory says stop.

## Report

Into the Milky Way pull request that claims the level, and summarised under Result.

## Result

**Run 2026-10-25, passed, see an earlier change and its follow-up.** The run was independent: a session that did not build the gate, with
the specification, this file and the `src` folders only. It ran in two parts.

- **Part 1** (05:20Z to 05:40Z) read this file as it stood on `main`, without the TLS additions. It tried 105 cases.
- **Part 2** ran on the pull request branch with the TLS additions. It ran part 1 again and added 26 transport, door and low-s methods:
  132 of 132 passed, in three runs.

Across both parts, nothing got through that DMP 04 or 05 says must be refused. Every refusal was counted under its reason, and no count
held content. There are three design findings:

- **F1:** rule 4 trusts the role `ImTokenSigner`, so the issuing CA endpoint is inside the verifier's decision. Doppler's watch caught
  the test token.
- **F2:** whoever holds the knock secret can fill the knock memory for one skew window.
- **F3:** a fake gate sees the device certificate before the device refuses it.

There are also eight observations. Full report: [GRB-0001-report.md](GRB-0001-report.md).
