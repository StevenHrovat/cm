# GRB-0002 · The Event Horizon decision layer on the sandbox

Security context: this is an authorised test of our own controls under the defensive charter in `CLAUDE.md`. It never targets a third
party, never develops malware, never harvests credentials, never evades detection on anyone else's systems and never touches a system
that is not listed under Targets. It runs on synthetic data in memory only.

| Field | Value |
|---|---|
| Id | GRB-0002 |
| State | **signed, run R2 passed** (2026-10-25 12:35Z signature, 12:36Z to 12:42Z run) |
| Signatory | the owner of the framework |
| Signature | the owner's words in the EH2 thread, 2026-10-25 12:35Z: "GRB-0002 is signed and approved." (cmsg_01Q5KHMD78Sa1TBrCksHzT1yAZ3YUfLNSufwLru6vqqvXR) |
| Component and claimed level | the Event Horizon decision layer (`EventHorizonPolicy`, the bypass/sensitive sets, counts-only telemetry, sealed findings), Supernova level 3 for the decision layer (DMP chapter 06) |
| Who runs it | the Fable test runner on the sandbox; for level 3 the builder's tests plus this adversarial sweep |
| Time window | 2026-10-25 12:35Z to 2026-11-08 12:35Z, sandbox only |

## Targets

| Target (role) | Where it runs | Interface |
|---|---|---|
| `EventHorizonPolicy` and its settings | in-process in `CM.BlackHole.Tests` | `IInspectionPolicy.Decide` over synthetic `ConnectionFacts` |
| `DomainSet` (the bypass matcher) | in-process | host matching over synthetic patterns |
| `InspectionCounters`, `SealedFinding` | in-process | counts and sealed records, scanned for leaks |

## Out of scope

- every production system and every system of the running company: no real device, no real DNS server, no switch, no tenant object, no real socket;
- every system of a third party; the Microsoft 365 web service is **not** called in the run (the bypass set is a synthetic seed);
- the inspection CA, the CMOS resolver, the data plane: none exists yet and none is in scope;
- real destinations, real categories and any payload: the run uses the made-up company of the sandbox only.

## Methods allowed

By class, never as a recipe: drive the decision layer with the full synthetic connection set under every single-category
content-enable and with the switch both off and on; attempt to reach `InspectContent` for a destination that is
Microsoft-managed, uncategorised or in a sensitive category; feed suffix-trick and boundary hosts to the matcher; scan every
counter key and sealed finding for a host, an address or content. Tools: the CM test projects only. No code is written that
could run outside the sandbox.

## Data

Synthetic only (`SandboxConnections`: placeholder hosts, real category names). No real value enters the run.

## Stop conditions

The run stops at once if any real destination, address or credential appears, if a resource limit of the host is reached, or
if the signatory says stop. A stop is reported like a result.

## Report

The claim: the decision layer never yields `InspectContent` except under the exact conjunction (switch on, destination
categorised, category in neither bypass set, category content-enabled), and nothing it emits carries a host or content.

## Result

**Run R1, 2026-10-25 (the EH2 reverification, before the signature; full report `GRB-0002-report-R1.md`).** The core claim held: over the
full cross product of switch, content-enable, host and category, content appeared only under the promised conjunction, and nothing the
layer emits carried a host. Nine hardening defects around the rule (the sensitive set could be emptied by configuration, comparisons
depended on the caller's comparer, the bucket word passed as a category, a KELVIN SIGN look-alike matched a bypass, a malformed bypass
pattern was swallowed, a host as category reached the counter key, counters were unbounded, free text passed into a sealed finding, a
whitespace category counted as categorised) were fixed in the same pull request; two design corrections (the Microsoft bypass scope; the
facts must be resolver-bound and need the IP half) went into the design note. The adversarial suite `Grb0002AdversarialTests` (54 cases)
stays in `CM.BlackHole/tests/EventHorizon` and is what the signed run re-executes.

**Signed run R2, 2026-10-25 12:36Z to 12:42Z, cm `main` at 67ad3dc (the decision-layer sources are identical to an earlier change's).** Signature:
"GRB-0002 is signed and approved." (the owner, 12:35Z). Runner: the EH2 session (Fable), the same that ran R1, in memory on synthetic data
only. `Grb0002AdversarialTests`: 54 passed, 0 failed. Builder's suite plus adversarial suite (`FullyQualifiedName~EventHorizon`): 83 passed,
0 failed. Mutation sweep, the same 35 mutants as R1 (policy 17, matcher 10, settings 2, counters 2, finding 2, tag grammar 2): 35 killed,
0 survived, 0 build failures. New findings: none. Nothing outside the simulator answered, no real value appeared, no production system
was touched, no safety-filter stop. Level 3 for the decision layer: **passed** (refusal suite, mutation run, resource limits, and this
signed run under this scope file).

Status: signed 2026-10-25 12:35Z; R1 (reverification, before the signature) and R2 (the signed run) both by the EH2 thread the same day; level 3 passed.
