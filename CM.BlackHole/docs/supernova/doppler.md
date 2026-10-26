# Supernova evidence: the Doppler server and its TPM evidence checks

Security context: defensive security engineering under the charter in `CLAUDE.md`. This is the evidence for the Supernova level of the
company's own security server (DMP chapter 06), gathered on synthetic data: the in-memory simulator, `SimulatedTpm` and swtpm on the runner.

Component: the Doppler server's enrolment decision with TPM attestation (`DopplerServer`, `DopplerAdmin`), the TPM evidence checks and
parsers (`src/Doppler/Tpm`: `TpmEvidence`, `TpmCredential`, `TcgLog`, `TpmWire`), the door (`DopplerDoor`, `DopplerWire`) and the agent loop.
Assigned level: **3** (DMP 06, "First assignments"). Claimed by lane L4b goal 4 (2026-10-25):

**Level 3: claimed, not passed.** Every requirement below is met but the last. GRB-0003 is signed (2026-10-25). Its first run found six
findings (below, "Findings of the first GRB-0003 run"), all fixed here; that run does not count as the result because its runner changed model
partway (the owner, 2026-10-25 13:44Z). The run that counts is a fresh one on the Type IIb test bed; the level is passed when it reports nothing
that gets through.

## Level 3

| Requirement (DMP 06) | Evidence | State |
|---|---|---|
| Refusal suite: every refusal reason has a test that produces it, counted without content | `tests/Doppler/Supernova/DopplerRefusalSuiteTests.cs`: one scenario per `DopplerRefusal` value Doppler answers (13; `None` is no refusal, `Unanswered` and `BadAnswer` are seen by the agent only), each answered with its reason and counted once under its own key; a reason added without a scenario fails `Every_refusal_reason_has_a_scenario`. Malformed at each field: `DopplerLimitTests` and the fuzz oracles | met |
| Mutation testing of the decision code, no surviving mutant without a written reason | Stryker.NET (below, "Mutation testing") | met |
| Fuzzing of every parser that takes bytes from another machine, corpus kept and replayed by `tools/verify` | `tests/Doppler/Supernova/DopplerFuzzTests.cs` (the step-2 campaign draws new keys and signatures each run, so its inputs differ between runs while the mutation positions are seeded; F4 and F5 were found that way): targets `door`, `finish`, `tpmpublic`, `tpmattest`, `tpmsignature` (bytes from the agent's machine) and `tcglog` (the agent's own firmware log); a seeded campaign per target and the replay of `tests/Doppler/Supernova/corpus/<target>/*.bin`, both in the `test` group of `tools/verify`. Oracles: a parser throws only its format exception, the door never counts `door.failed`, and a changed step-2 request never enrols | met |
| Resource limits proved on the sandbox (oversized input, many slow connections) | `tests/Doppler/Supernova/DopplerLimitTests.cs`: every step-2 field one byte over its limit is malformed and at its limit parses, PCR 7 events over the limit never reach the replay, the table of open challenges and pending enrolments is bounded (the oldest open challenge makes room, so a flood with the shared knock secret cannot lock enrolled machines out of token renewal: GRB-0003 F2), a frame-limit payload of garbage is counted without content. Slow and many connections are the gate's limits in front of Doppler (Milky Way, GRB-0001) | met |
| No secret or content in any log or refusal record | `DopplerRefusalSuiteTests.No_count_key_or_audit_line_of_a_refusal_carries_content` (every reason), `DopplerDecisionTests.An_enrolment_a_token_and_a_revocation_leave_exactly_these_counts_and_audit_lines` (the success path: subject only, no key, token or challenge bytes) | met |
| One GRB run against the sandbox under a scope file, reported in the pull request | [GRB-0003-doppler-attestation.md](../grb/GRB-0003-doppler-attestation.md): signed 2026-10-25. First run: six findings, fixed (below); not the result (model change). The counted run: a fresh Fable run on the Type IIb test bed | open: the counted run |

## Findings of the test tier

| # | Found by | What | Fix |
|---|---|---|---|
| F1 | the fuzzer (`tcglog` target) | a `Spec ID Event03` shorter than its fixed 16-byte header made `TcgLog` read past the event and throw `ArgumentOutOfRangeException` instead of its format exception (the agent's own log, so no remote input reached it; the server only replays PCR 7 events it was sent) | `TcgLog` refuses a spec event under 16 bytes; the input is in the kept corpus |
| F2 | the first mutation run | the EK id helper `KeyIdOfSpki` had a separate branch for an empty SPKI whose mutants no test could tell apart (an empty SPKI and its hash both reach no plan) | the id is the SHA-256 of the SPKI as sent, `?? []` for a missing one, in one expression; pinned by `A_missing_or_empty_endorsement_key_is_no_approved_plan_and_not_a_crash` |
| F3 | the first mutation run | time and size edges were not pinned (a challenge, a pending enrolment and an issued token at exactly their lifetime; the attestation key at 1024 bytes, the EK SPKI at 2048; KDFa over several HMAC blocks; the credential's 1 to 32 bytes; the SHA-1 name algorithm switch) | `DopplerDecisionTests`, `TpmEvidenceEdgeTests`, `TpmCredentialEdgeTests` |
| F4 | the fuzzer (`finish` target, in CI) | the type of a PCR 7 event is the agent's word (the TPM measures only the data's digest), so a separator sent under another type replayed fine; with no separator left, a SecureBoot event that software extended after the hand-over would have counted as the firmware's. Not exploitable alone (the firmware also measures SecureBoot as 0 when it is off, which gives `Off`), but the separator rule did not hold | PCR 7 must hold exactly one separator, verdict `NoSeparator` otherwise. This closed only the relabelled separator; the GRB run then showed the type still decided (G1 below), and since then a separator is known by its data too (`A_separator_sent_under_another_type_still_counts_so_the_hand_over_cannot_move`) |
| F5 | the fuzzer (`finish` target, in CI) | a CSR whose key names an unknown curve made the platform throw `PlatformNotSupportedException`, which escaped the decision (the door failed closed and counted `door.failed`) | refused as `BadRequest`; `A_signing_request_whose_key_names_an_unknown_curve_is_a_bad_request_and_not_a_door_failure` |
| F6 | the same CI run | the campaign itself: a mutation can give back the original bytes, and the honest request then used up the plan, so the rest of the campaign crashed; and its oracle counted a change of an event type Doppler does not read as a finding | unchanged mutations are skipped, the plan is renewed after an enrolment, and the oracle allows a changed type only where neither type is a separator and the event is not the SecureBoot variable (`OnlyUnreadEventTypesDiffer`, since GRB-0003 F1 `OnlyEventTypesDiffer`: no decision rests on a type any more); 9,000 extra step-2 mutations ran clean |

## Findings of the first GRB-0003 run (2026-10-25)

An independent helper session ran GRB-0003 with the specification, the scope file and the `src` folders only: 154 cases, 16 red, all of them
findings. Its report and throw-away tests are in the project Library (`grb-0003/`), not in this repository. The run is not the counted result
(its runner changed model partway); the findings are real and fixed here.

| # | Severity | What | Fix |
|---|---|---|---|
| G1 | high | The Secure Boot verdict rested on event types the TPM does not sign: a Secure-Boot-off machine relabelled the firmware's SecureBoot = 0 and separator in the log it sent, extended SecureBoot = 1 and a separator after boot, and enrolled (simulator C12, C13; swtpm H09) | no decision rests on a type: a separator is known by its data too, the SecureBoot variable by its data whatever its type (`SecureBootPolicy`); `TpmEvidenceEdgeTests` (relabelling cases). What PCR 7 alone cannot show (a firmware that measured nothing into it, C11 and H08) is the documented limit in [../doppler.md](../doppler.md), "What PCR 7 cannot prove", pinned by a test |
| G2 | medium | A holder of the shared knock secret filled the challenge table with no plan, TPM or certificate, and every machine's token renewal was refused for a challenge lifetime, repeatably | a full table pushes out the oldest open challenge (`challenge.evicted`); pending enrolments (which need a plan) are never pushed out. `A_flood_of_door_challenges_does_not_take_token_renewal_away_from_an_enrolled_machine` |
| G3 | medium | The agent took the door's enrolment certificate without checking it: a foreign authority's certificate, or one naming another subject, became the machine's identity and wedged it for good | the agent checks the chain to its roots in role Device, the subject and the key; otherwise `BadAnswer` and it stays unenrolled (`Grb0003RegressionTests`) |
| G4 | low | An off-curve device point and a request with an unknown algorithm identifier threw out of `FinishEnrolment` (the door counted `door.failed`, no code, no audit); null parts on the in-process path did the same | refused as `BadCertify`, `BadRequest` or `BadQuote` with their counters (`Grb0003RegressionTests`) |
| G5 | low | A plan withdrawn between the revocation write and the plan-consuming write revoked the old identity and enrolled nothing | one store write revokes the old identity, uses up the plan and registers the new one; an issued certificate that is not registered goes on the CRL |
| G6 | low | A garbled state file, or one with a certificate of another key, crashed `AgentLoop.Step` | such a record is treated as absent and the agent enrols afresh |
| G7 | low (found re-running the run's suite against the fixes) | with G3's check in place, an answer whose certificate bytes load but whose validity date does not parse made `cm.os` `TrustAnchors.Validate` throw from the platform's reader (the same call Milky Way's verifier makes) | `TrustAnchors.Validate` answers `Malformed`; `A_leaf_whose_validity_date_does_not_parse_is_malformed_not_an_exception` (checked against its mutant by hand) |

Observations O1 to O12 of the report need no code change here; O4 (approver spelling under four eyes) and O2 (the SHA-1 default) are the
owner's policy questions and are listed in the report.

## Mutation testing

Scope: the decision code. `DopplerServer.cs`, `DopplerAdmin.cs`, `Tpm/TpmEvidence.cs`, in `Tpm/TcgLog.cs` the server's `SecureBootPolicy`
(the PCR 7 replay and the Secure Boot reading), and in `Tpm/TpmCredential.cs` what the server runs: `Make` with KDFa, CFB and RSA-OAEP with a
label. Out of scope: `TcgLog`'s full log parser (it runs on the agent over the device's own firmware log; Doppler receives the events of one PCR,
and the parser is fuzzed), `TpmCredential.Activate` and the OAEP decryption (the software TPM's side, never used with a real TPM), `src/Doppler/Simulation` (test infrastructure), `src/Doppler/Windows` (Windows-only platform calls, proved on the test VM),
`TpmCommands` and `CommandTpm` (command marshalling, proved against swtpm in CI), and exception constructors (`ignore-methods: *Exception.ctor`:
the message text is read by no decision).

Runs: Stryker.NET 5.0.0, `coverage-analysis: perTest`, against `CM.BlackHole.Tests`. Every survivor was read by hand; the ones that showed a
missing test got the test, and those tests were checked against their mutant by hand (apply, run, restore).

| Code | Killed | Survived | No coverage | Score |
|---|---|---|---|---|
| DopplerServer | 187 | 6 (1 killed by hand since) | 1 | 96.4% (run after the GRB-0003 fixes) |
| DopplerAdmin | 36 | 2 | 0 | 94.7% |
| TpmEvidence | 75 | 6 | 0 | 92.6% (run after the GRB-0003 fixes) |
| TpmCredential (server side) | 67 | 9 | 1 | 87.0% |
| SecureBootPolicy (in TcgLog) | 47 | 7 | 1 | 85.5% (run after the GRB-0003 fixes: the type-free separator and variable rules add mutants, all killed) |

Killed by hand after the post-GRB run: the certificate an enrolment issued and could not register because its plan was withdrawn meanwhile
goes on the revocation list (`DopplerServer` 217), by `A_certificate_issued_for_an_enrolment_that_then_loses_its_plan_goes_on_the_revocation_list`.
Line numbers below are those of the post-GRB run.

Killed by hand after the earlier runs: the missing EK SPKI (`DopplerServer` 128, the `?? []`), and both mutants of the SHA-1 name algorithm switch
(`TpmEvidence` 15: `allowSha1 || …` and `alg != Sha1`), by `A_SHA1_named_attestation_key_is_read_only_when_SHA1_is_allowed_and_no_other_name_algorithm_is_ever`;
in `SecureBootPolicy` a SecureBoot measurement after the separator (`beforeSeparator` kept `true`) and one in an event of another type (the type
check removed; since GRB-0003 there is no type check, on purpose), by `A_SecureBoot_variable_measured_only_after_the_separator_is_not_measured`.
All were checked by hand: each mutant applied alone fails at least one Supernova test. The `SecureBootPolicy` row is the final run, with
those tests and the one-separator rule (F4) in place; its one compile error is `Count` to `Sum` on the separator count, which does not compile.

The first runs left most of `SecureBootPolicy.Evaluate` untested: one condition combined the event type with `TryReadVariable(..., out ...)`, and
Stryker's mutants of that `||` left the out variables unassigned, so they did not compile and Stryker rolled the whole method back. The condition is
now two lines (with a comment saying why); 47 of its mutants are killed instead of 16.

Every remaining survivor has one of these reasons. "Same outcome" means the mutant changes no decision a test or a peer can see.

| Class | Reason | Where |
|---|---|---|
| A | Null guard: the mutant removes `ArgumentNullException.ThrowIfNull` and the next call throws the same `ArgumentNullException` | `DopplerServer` 316 (`ObserveMilkyWay`: `PeerWatch.Observe` guards the same argument) |
| B | Hygiene the sandbox cannot observe | `TpmEvidence` 46 (`rsa.Dispose()` on a refused EK: the key is unreachable and collected) |
| C | Redundant second check: the same input is refused by a later rule | `TpmEvidence` 61, both logical mutants (a device key that is not ECC P-256 passes the first line under the mutant and is refused by `EccP256SubjectPublicKeyInfo`, which checks type and curve again and throws the format exception, caught as a refusal); `SecureBootPolicy` 224 (the `continue` after a separator: a separator's four bytes never parse as a variable, so the next line skips it) |
| D | Not reachable by construction | `DopplerServer` 149 (`evict: false` to `true` in `BeginEnrolment`: Begin consumes its own challenge first, so the table always has a free slot when it asks; eviction could only matter between two concurrent requests) and the no-coverage mark at 189 (the guard around the quote check: no known input makes `CheckQuote` throw a cryptographic or not-supported exception, its parsers throw their format exception; the guard keeps G4's rule for an input nobody has found); `TpmCredential` 120, four survivors and the no-coverage mark (the OAEP length check: the message is always the 32-byte seed and the EK is at least 2048 bits, so 32 never exceeds 190) |
| E | Same outcome by construction of an immutable store | `DopplerAdmin` 42 (`done` starts `true`: the update always runs and sets it), `DopplerAdmin` 46 (always `Remove`: removing an absent key from an `ImmutableDictionary` returns the same state), `DopplerServer` 284 (`>= 0`: an update with no pending entry sets no item and leaves the same state), 225 (`replaced.Count >= 0`: a sync with nothing revoked and not on the list does nothing), 201 (`used` starts `true`: the update always runs and sets it first) |
| F | Same number or same bytes | `TpmCredential` 100 (CFB loop `<=`: one more pass with no byte left writes nothing), 131 (three flags of the encryption: `isUnsigned` of the encoded message, which starts with 0, `isUnsigned` of the exponent, which starts with 0x01, and `isBigEndian` of the exponent, whose bytes 01 00 01 read the same both ways; the EK exponent is 65537 in every TCG EK template; with another exponent the mutant only makes a blob the TPM cannot activate, which fails closed as `BadActivation`), 176 (`LeftPad` with `>`: a padded copy of an array that is already full length is the same bytes) |
| G | A variable `TryReadVariable` refuses is skipped exactly like a variable that is not SecureBoot | `SecureBootPolicy` 227 (the refusal check removed: the out values stay at their defaults, an empty vendor and name), 242 (the default name set to another string: on a refusal it is never read), 244 (`<= 32`: a variable of exactly 32 bytes has an empty name) and its no-coverage mark (`true` for data under 32 bytes: the defaults again), 248 three survivors (a 256-character name is never `SecureBoot`; a data length equal to the event's is refused by the size sum; `true` leaves the defaults) |
| H | Stryker's block removal returns `default`, which is the refusal itself | `TpmEvidence` 32, 71, 93 (`catch (TpmFormatException) { return null; }` to `{}`: the method then returns null by Stryker's added `return default`) |

A survivor or a no-coverage mark is closed only by a test that kills it or a row in this table.

For the record, the out-of-scope code above left 11 survivors and 1 no-coverage mark in `TcgLog`'s parser (the agent side: boundaries of the digest
count, the event size and the Spec ID algorithm count, all covered by the `tcglog` fuzz target's oracle) and 7 and 1 in `TpmCredential.Activate` and
the OAEP decryption (the software TPM). They are not decision code of the server.

## How to re-run

```
tools/verify                       # refusal suite, limits, decision edges, fuzz campaigns and corpus replay are in the test group
```

Mutation testing (not in `tools/verify`, it takes about 15 minutes): Stryker.NET 5.0.0 as a global tool (`dotnet tool install -g dotnet-stryker`),
run from `CM.BlackHole/tests` in a separate worktree so a build in the main checkout does not replace the test assembly under it. Mutate whole
files: Stryker.NET reads a `{a..b}` span after a pattern as character offsets, not line numbers, so a line span filters everything out.

```
cd CM.BlackHole/tests && dotnet stryker -p CM.BlackHole.csproj --ignore-methods "*Exception.ctor" \
  -m "**/Doppler/DopplerServer.cs" -m "**/Doppler/DopplerAdmin.cs" -m "**/Doppler/Tpm/TpmEvidence.cs" \
  -m "**/Doppler/Tpm/TpmCredential.cs" -m "**/Doppler/Tpm/TcgLog.cs" -r json -r cleartext
```

The two TPM files are mutated whole, and the report is then read for the in-scope code only: `SecureBootPolicy` in `TcgLog.cs`, and `Make`,
`KdfA`, `Cfb`, `OaepEncrypt`, `Mgf1`, `Xor` and `LeftPad` in `TpmCredential.cs`.
