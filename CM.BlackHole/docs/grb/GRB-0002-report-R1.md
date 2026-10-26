# GRB-0002 · Report of run R1, the EH2 reverification (2026-10-25 07:23Z to 08:10Z)

Security context: this is an authorised test of our own controls under the defensive charter in cm `CLAUDE.md` and the scope file
`CM.BlackHole/docs/grb/GRB-0002-event-horizon-decision-layer.md`. The owner asked for this run in the operations master project ("EH1 not accepted
due to switch to Opus 4.8. Complete reverify by Fable with security posture.", 2026-10-25 07:23Z); the scope file itself is **not yet
signed**, so R1 is the reverification run, not the signed GRB run. The signed run re-executes `Grb0002AdversarialTests` and records its
counts in the scope file. Runner: a fresh session that did not build the decision layer. Inputs used: the design note, DMP chapters 01, 06
and 10, the sources it cites (fetched again), and the sources in `cm.os/src/Inspection` and `CM.BlackHole/src/EventHorizon`; the builder's
tests were read only after the adversarial suite was written, to count them and to run the mutation sweep against them.

Everything ran in memory on synthetic data. No socket, no real destination, no real device, no real DNS, nothing on port 5100. Microsoft
365, Intune and Defender were bypassed in every configuration tried. Nothing was built toward TLS interception or the data plane.

## Result

| | |
|---|---|
| cm `main` at 8fc0112 (an earlier change merged) | build green; Black Hole 86 passed, 7 skipped (Windows-only); the 29 Event Horizon tests green |
| Core claim (content only under the exact conjunction) | **held**, over the full cross product: switch × 16 content-enables × 10 hosts × 38 categories, no content outside the conjunction |
| Got through where the design says refuse | **none** |
| Defects in the surface around the rule | **9** (A2 to A5, B2, B4, C1, C2, C4 below), all fixed in the same pull request |
| Design corrections | **2** (F1 facts bound to the resolver and the IP half; F2 the Microsoft bypass scope) |
| Bookkeeping | **1** (the decision number was taken twice by other threads and re-filed) |
| Adversarial suite after the fixes | 54 cases green; builder's 29 green; 83 in total |
| Mutation sweep, builder's suite alone, first draft | 23 mutants over the policy and the matcher: 22 killed, **1 survived** (a whitespace-only category passed as categorised) |
| Mutation sweep, combined suite, hardened code | 35 mutants over the policy (17), the matcher (10), the settings (2), the counters (2), the finding (2) and the tag grammar (2): **35 killed, 0 survived**; mutant list in the pull request |
| Level | level 3 **claimed, not passed**: the refusal suite, the mutation run and the resource limits are in place; the signed GRB run is missing |

## What was tried, by class (GRB-0002 "Methods allowed")

| Method | Cases | What was tried | Result |
|---|---|---|---|
| The conjunction | 7 | the full cross product above; every default-settings object; `default(ConnectionFacts)`; null settings; the sensitive set replaced by an empty set and by a subset; ordinal (case-sensitive) and padded spellings in the settings and from the resolver; the literal bucket word `(uncategorised)` as a category | content only under the conjunction with the shipped sets; **A2/A3** an empty or narrowed sensitive set let a content-enabled sensitive category reach content; **A4** `Banking-Finance` with ordinal sets skipped the sensitive bypass; **A5** the bucket word passed step 3 |
| The matcher | 5 theories, 42 rows + 5 000 generated | boundary hosts (parent, sibling, suffix tricks, ports, paths, user@, NUL, newline, whitespace, multiple trailing dots); non-ASCII look-alikes (KELVIN SIGN, dotted I, fullwidth, zero-width space, ideographic full stop, punycode label); 5 000 seeded random LDH hosts outside the set and as members; malformed patterns (`*`, `*.`, `.com`, URL, CIDR, spaces, Kelvin); a 700-pattern set with a 253-byte host, 20 000 misses | **B2** `outloo` + KELVIN SIGN + `.office.com` matched (invariant lower-casing folds U+212A to `k`); **B4** every malformed pattern was swallowed silently and matched nothing: a URL became an unmatchable suffix, `*` and `*.` both became the suffix `*` (only a host literally ending in `.*` would have matched), a CIDR was accepted as a name; the bypass list degraded quietly instead of failing; cost bounded |
| Counts, never content | 5 | a host, an IPv4, an IPv6, free text, an e-mail address and a 500-character string as the category; 50 000 distinct categories; 64 threads × 2 000 records; the sealed finding with a host or a sentence as rule id or category; the whole sweep's snapshot scanned for every host and the words `example`, `outlook` | **C1** the host went into the counter key verbatim; **C2** 50 000 keys, no bound; **C4** any non-blank string was accepted as rule id and category; concurrency lost nothing; the scan of the snapshot found no host |
| Settings and contract | 4 | reflection over `InspectionSettings` (4 properties, 4 described rows, none named skip/allow/debug/trusted/force/override/all); the facts record has exactly `DestinationHost` and `Category`; the depth order; the policy source has exactly one exit to content | none; **F1** recorded from the contract's shape |

## Findings fixed in this pull request

- **A2, A3** `InspectionSettings.SensitiveCategories` is now the union of what is set and the default set: configuration adds, never removes.
- **A4, A5** categories are normalised once through `CategoryTag` (lower-case hyphenated words, 64 characters, ASCII); the policy, the settings, the counters and the finding use it; the bucket words are not categories.
- **B2** `DomainSet` matches ASCII LDH names only; a non-ASCII host never matches.
- **B4** `DomainSet` throws on a malformed pattern, so the feed job fails visibly.
- **C1, C2** `InspectionCounters` buckets a non-tag category under `(invalid-category)` and holds at most 1024 keys, the rest under `(overflow)`.
- **C4** `SealedFinding` enforces `eh.<words>` for the rule id and a category tag (or the uncategorised bucket) for the category.
- **M08 (mutation survivor)** a whitespace-only category is now a normalisation case, killed by A1.

## Design corrections (design note sections 2 and 4)

**F2, the Microsoft bypass scope was too narrow.** The design bypassed the M365 Optimize and Allow categories and called Default "eligible for
inspection". Microsoft's connectivity guidance in force (page dated 2026-08-26) applies "for all Required Microsoft 365 network endpoints
(domains) and categories" and lists "TLS termination or deep packet inspection of any Microsoft 365 domains" as known to break the service.
The Intune endpoints page says the Office 365 endpoint service "is insufficient" for Intune and must not be the source of the Intune list,
and marks the Store API, Endpoint Privilege Management and attestation endpoints "SSL inspection isn't supported". Defender for Endpoint's
page asks to exclude `*.endpoint.security.microsoft.com` from TLS inspection and MITM proxying. The rule EH-BYPASS-1 now covers every
required M365 endpoint of every category plus the Intune and Defender lists from their own pages; the settings description says so. The
same error is in the knowledge warehouse's article on network inspection (it allows only the category Default); a corrective draft
is opened there.

**F1, the facts are a client-influenced string.** `ConnectionFacts` is a host name and a category. A data plane that fills the name from the
client's SNI alone lets the client choose its bypass (set SNI to a Microsoft name, connect anywhere). The name must be the one the CMOS
resolver answered, bound to the address the device connects to, and the bypass set needs its IP half for Optimize traffic that has no
name (Teams media), otherwise that traffic is `Metadata(PinnedUncategorised)` instead of `Bypass(MicrosoftManaged)`. Recorded in the design
note for the data-plane brief; the contract's two fields are pinned by test D2 so the brief has to change them consciously.

## Observations

- **O1** The "placeholder" Microsoft patterns in `SandboxConnections` are real public Microsoft names (`*.protection.outlook.com`, …). They
  identify no company and carry no value (C9 holds); the word "placeholder" in the comment was inaccurate and the design note now says
  "public Microsoft names".
- **O2** `InspectionVerdict` is a public record struct, so `new(InspectDepth.InspectContent, SwitchOff)` can be constructed by anyone; a data
  plane must act only on verdicts it received from the policy, never construct one. Noted for the data-plane brief; not a defect of the layer.
- **O3** The pull request an earlier change reported "62 Black Hole tests"; the Event Horizon part is 29, the rest Doppler. The number was right for
  the whole project at that commit (now 93 with later merges).
- **O4** The matcher is a linear scan over the patterns (about 700 in the real lists); 20 000 misses against 700 patterns with a 239-byte
  host ran well inside the test budget. Fine for a per-connection decision; a trie is not needed yet.

## Stop conditions

None met: nothing outside the simulator answered, no real value appeared, no production system was touched, the owner did not say stop.
The run completed without a platform safety-filter stop.
