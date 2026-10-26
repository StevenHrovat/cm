# Supernova evidence: the Milky Way IM verifier and gate

Security context: defensive security engineering under the charter in `CLAUDE.md`. This is the evidence for the Supernova level of the
company's own entrance (DMP chapter 06), gathered on synthetic data in the in-memory simulator.

Component: the IM verifier (`CM.MilkyWay.Verify`), the gate (`CM.MilkyWay.Gate`), the transport (`CM.MilkyWay.Transport`) and what sits
inside their decision (`Im`, `Im.Trust`). Assigned level: **4**. Claimed in an earlier change (lane L3b goal 3, 2026-10-25), for IM version 0 in the sandbox:

**Level 3: passed.** Every requirement below is met, the GRB run included (GRB-0001, 132 of 132 checks passed).
**Level 4: in progress.** The assumption audit has its second pass; the rest is open.

IM version 1 (dual trust, agreed with lane 4 in an earlier change) changes the token and the decision code. It is a new version, so this
evidence covers version 0 only and version 1 has to earn level 3 again: refusal suite, mutation run, fuzz targets and a new GRB run.

## Level 3

| Requirement (DMP 06) | Evidence | State |
|---|---|---|
| Refusal suite: every refusal reason has a test that produces it, counted without content | `tests/Supernova/RefusalSuiteTests.cs`: one scenario per `ImRefusal` value (34), each counted once and nothing else; a new reason without a scenario fails the suite. Malformed at each field: the fuzzer's parser oracles and `cm.os/tests/Im` | met |
| Mutation testing of the decision code, no surviving mutant without a written reason | Stryker.NET (below, "Mutation testing") | met |
| Fuzzing of every parser that takes bytes from another machine, corpus kept and replayed by `tools/verify` | `src/Simulation/ImFuzzer.cs` (token, envelope, heartbeat, frames, the verifier, the handshake), `tests/Supernova/FuzzTests.cs` (a seeded campaign per target and the corpus replay in the `im` group), corpus `tests/Supernova/corpus/`, long run `tools/fuzz` | met |
| Resource limits proved on the sandbox (oversized input, many slow connections) | `tests/ImTransportTests.cs`: oversize prefix, cut-short frame, a stalled connection before and after TLS, a silent session, connections over the limit and the separate limit for certificate-less ones; `tests/Supernova/BoundaryTests.cs` for every edge | met |
| No secret or content in any log or refusal record | `ImEvidenceTests` (frames and counts), `ImTransportTests.What_the_gate_counts_for_transport_refusals_holds_no_content` and the wire capture | met |
| One GRB run against the sandbox under a scope file, reported in the PR | [GRB-0001-sandbox-gate.md](../../../CM.BlackHole/docs/grb/GRB-0001-sandbox-gate.md), signed by the owner 2026-10-25 05:12Z; run by a separate session without the builder's notes, in two parts (main, then the TLS branch): 132 of 132 checks passed, three findings fixed, [report](../../../CM.BlackHole/docs/grb/GRB-0001-report.md) | met |

## Findings of the test tier (all fixed in #56)

The GRB run's own findings (F1 to F3 of GRB-0001: two DMP 04 wording gaps and the order of the role check in DMP 05) are in its report and were fixed in #56 and #64.

| # | Found by | What | Fix |
|---|---|---|---|
| F1 | reading for I6 while writing the canonical-form oracle | ECDSA accepts `(r, s)` and `(r, n - s)`: every signed IM structure had a second valid spelling (I6 broken; replay was safe because rule 16 keys on the message id) | low-s rule: `ImKeys.Sign` normalises, `ImKeys.Verify` refuses high s (DMP 04 "Encoding") |
| F2 | the fuzzer (heartbeat target) | `ImReader.Id8` decoded with the ASCII decoder, which turns every byte above 0x7F into `?`; `?` is printable, so the agent version had a second spelling | the reader refuses bytes above 0x7F before decoding |
| F3 | the fuzzer (token target) | a subject key with explicit curve parameters made the key import throw `PlatformNotSupportedException`, which escaped the token parser (the verifier still failed closed) | the key checks treat every platform refusal as "not canonical P-256" |
| F4 | the first mutation run | the edges of the time and size rules were not pinned (a test one minute away passes for `<` and `<=` alike) | `BoundaryTests` |

## Mutation testing

Scope: the decision code. In `CM.MilkyWay`: the gate, the verifier, the replay memory, the refusal log and its families, the settings
check, health and the board, the transport. In `cm.os`: everything under `src/Im` (crypto, envelope, frames, token, wire, transport).
Simulation code (`src/Simulation`) is test infrastructure and out of scope.

Runs: Stryker.NET 4.16.0, MilkyWay on de09be2 and cm.os `Im` on ff97420, both against the cm.os and the MilkyWay test projects. Every
survivor of those runs was read by hand; the ones that showed a missing test got the test (de09be2 to 60659bb) and each new test was
checked against its mutant with a by-hand mutation (apply, run both test projects, restore). The numbers below are the Stryker runs; the
mutants killed by hand since then are listed under them.

| Code | Killed | Survived | No coverage | Score |
|---|---|---|---|---|
| MilkyWay gate | 88 | 1 | 0 | 98.9% |
| MilkyWay verifier | 54 | 2 | 0 | 96.4% |
| MilkyWay replay memory | 13 | 2 | 0 | 86.7% |
| MilkyWay refusal log | all | 0 | 0 | 100% |
| MilkyWay settings | 115 | 1 (killed by hand since) | 0 | 99.1% |
| MilkyWay health | 24 | 1 | 0 | 96.0% |
| MilkyWay board | all | 0 | 0 | 100% |
| MilkyWay transport | 7 | 2 | 0 | 77.8% |
| MilkyWay refusal families | 13 | 1 | 0 | 92.9% |
| cm.os ImCrypto | 51 | 12 | 7 | 72.9% |
| cm.os ImEnvelope | 15 | 1 | 0 | 93.8% |
| cm.os ImFrames | 68 | 7 | 1 | 89.5% |
| cm.os ImToken | 53 | 6 | 3 | 85.5% |
| cm.os ImTransport | 56 | 42 | 12 | 50.9% |
| cm.os ImWire | 115 | 9 | 3 | 90.6% |

Killed by hand after the runs (tests in 60659bb and 26dd797): the settings case where the door limit equals the frame limit, the
transport's settings check, the empty-frame write, the TLS chain policy (no downloads, custom root trust, no revocation check), the
keyless identity, a throwing trust anchor and a missing certificate in `ImTls.Check`, the foreign gate certificate on the device side,
and the garbage-key token.

Every remaining survivor has one of these reasons. "Same outcome" means the mutant changes no decision a test or a peer can see.

| Class | Reason | Where |
|---|---|---|
| A | Null guard: the mutant throws a `NullReferenceException` where the guard throws `ArgumentNullException`; the wire never yields null | MilkyWay health 29, transport 46; cm.os `ArgumentNullException.ThrowIfNull` lines in every `Im` file |
| B | Exception message text; no decision reads it | cm.os ImTransport, ImFrames, ImToken |
| C | `ConfigureAwait(false)` to `true`: there is no synchronisation context on the server or in the tests | MilkyWay transport 138; cm.os ImTransport |
| D | Hygiene the sandbox cannot observe: `Dispose`, zeroing of key bytes, `Flush`, the `owned` flag, `offline: true` (the policy already forbids downloads) | cm.os ImTransport, ImCrypto |
| E | Redundant second check: the same input is refused by an earlier or later rule | cm.os ImFrames 55/59 (knock length and the end check), ImToken 116 (outer bound and the inner length), ImWire 193 (empty id refused by the alphabet check) and 197 (0x7F refused by the alphabet), ImCrypto 60/62 (the round-trip export and the platform's curve names), ImCrypto 87/91 (the catch after the canonical-key check), ImTransport 37/39/46 (a short read is end of stream either way) |
| F | Not reachable by construction | MilkyWay gate 130 (no input before the knock throws), verifier 86/126 (only the heartbeat kind passes rule 13 in v0), replay memory 33 (scopes have a fixed length) and 39 (one queue entry per key), refusal families 82 (Malformed is also the default arm); cm.os ImFrames 270 (device and token are set together), ImCrypto 103/120 (the signature length is checked before), ImCrypto 123 (n − s is below 2^255), ImWire 109/115/239 (fixed field sizes, every limit at most 4096, an exhaustive enum), ImToken 161 (exhaustive enum), ImTransport 198/210/242/289/325 (paths a genuine peer and the sandbox never take: a null chain or verdict, the observe call for certificate-less peers, a non-exportable key, a gate answer other than ack or refused) |

Stryker's "no coverage" marks were not all reliable here: several were covered and killed when checked by hand. A survivor or a
no-coverage mark is closed only by a test that kills it or a row in this table.

IM version 1 will need a new run; the classes above are the starting list for its reasons.

## Level 4

| Requirement | State |
|---|---|
| Assumption audit | second pass: [assumptions.md](assumptions.md), A1 to A17; open: A2, A5, A10, A14, A15 |
| Machine-checked proof of the core rule | open (Singularity) |
| Differential testing against an independent executable model of DMP 04 | open; `ImCanonical` re-encodes from the DMP tables, which is a first, small model for the encoding only |
| A long fuzzing run (24 CPU hours) per release candidate | open; `tools/fuzz --iterations N --keep` is the tool |
| The GRB run by an independent session | open: GRB-0001 was a separate session but under the same project; level 4 asks for one with no shared context, and for IM version 1 |

## How to re-run

```
tools/verify                                                     # refusal suite, edges, fuzz campaigns and corpus replay are in the im group
dotnet run --project tools/fuzz/src/Cm.Fuzz.csproj -- --iterations 1000000 --keep   # long run; new inputs land in tests/Supernova/corpus
```

Mutation testing (not in `tools/verify`, it takes minutes): Stryker.NET 4.16.0 as a local tool. On a machine with only the .NET 10
runtime it needs `DOTNET_ROLL_FORWARD=Major`; without a terminal it prints nothing, so run it under `script -qfc "..."` on Linux. Run it in a
separate worktree so a build in the main checkout does not replace the test assembly under it:

```
cd CM.MilkyWay/tests && dotnet stryker -p CM.MilkyWay.csproj -r markdown -r json
cd cm.os/src && dotnet stryker -tp ../tests/cm.os.Tests.csproj -tp ../../CM.MilkyWay/tests/CM.MilkyWay.Tests.csproj -m "**/Im/**/*.cs" -r markdown -r json
```
