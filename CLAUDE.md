# CM — agent guide

CM, Canis Major, is a framework: **one repository from which every part of an organisation's infrastructure deploys**. CMOS is what an organisation runs
when CM runs its whole infrastructure. This file holds the standing rules for every session in the repository; the specification every pull request is
judged against is DMP, the Dark Matter Protocol, vendored in [vendor/dmp/](vendor/dmp/README.md).

**This repository is agent-maintained.** Its owner steers by using the result and reading the docs, not the code. Concepts and pictures first, one
recommendation, not a menu. Any agent may always ask the owner when unsure; sharp questions are welcome.

## Defensive security charter

The full text is [vendor/dmp/CHARTER.md](vendor/dmp/CHARTER.md). In short: this repository and every brief in it serve the **defensive** security and
privacy engineering of an operating organisation's **own** IT, authorised by The Administrator. It never targets third parties, never develops exploits or
malware, never harvests credentials, never evades detection on anyone else's systems, and never runs a test against a production system without a written
scope file naming the signatory, the targets and the time window. Tests run on **synthetic data**. If a request in this repository would break the charter,
stop and say so.

## Knowledge and process (DMP chapter 14)

- **Ask the nearest tier first.** Before asking the owner, ask the nearest information tier (constitution C12, C13), then outward. Name the record you used,
  or say in one line that the search came up empty. A number or fact that changes an outcome needs a source.
- **Knowledge goes back into Tier 1.** What no tier held and you learned goes to the intake of the nearest tier, organised by that tier: colleagues by role,
  never a secret.
- **Combine every path** (C14). When one source cannot give the full picture, list the read and write paths that touch the question (tiers, live reads,
  repositories, the trail of past writes) and join them before asking. Say what is still unknown; read a write path through its trail or dry run, never by
  writing.
- **Look it up, don't guess** (C21). When two candidates are close, read the source of truth yourself and quote the match.
- **Prove forward and back** (C22). Every proof runs both ways: the intent chain DMP to IM to CM and an independent evidence chain from CM up to DMP. A layer is
  proven only when both agree; on a mismatch the stricter answer stands.
- **Every deployment obeys DMP** (C25). A deployment runs the DMP gate on the exact commit it deploys and never deploys on red. No deployment may leave an
  administrator action without a supported route.
- **What is not fully trusted is ejected** (C∞). Whatever does not comply with DMP, IM or CM is critical: it blocks the merge and the promotion.

## Rules

- **Roles, not values.** Nothing that identifies an organisation, person, host, address, tenant or secret enters this repository. Real values are parameters.
- **Every change is a pull request**; `main` needs the `verify` check.
- **C# on .NET 10.** Package versions live in `Directory.Packages.props` only. The folder layout is the composition tree (`DMP/tree.txt`): `cm.io` <- `cm.os`
  <- the `CM.<Constellation>` folders, each with `src/` and `tests/`, none referencing another constellation. IM is the vendored library `vendor/im`.
- **Vendored copies are never edited here** (`vendor/README.md`, DMP-022): change the source repository, re-copy, re-pin.
- **Scripts an administrator runs** have a read-only check, a dry run with WOULD lines, `-Apply`, and are idempotent. They start with the shared guard
  (`tools/lib/Guard.psm1`: expected host, elevation, account, Windows PowerShell 5.1, clean `main` checkout when applying), name groups by SID, and do their
  writes through `Invoke-GuardedWrite`. Every command block handed to the owner has an absolute script path and a bold `host · context · account` line above it.
- **Deterministic tests.** Inject `TimeProvider`; no wall clock, machine name or stopwatch in tests (DMP-010).
- **Completion goals, never time plans** for the code side.

## Build, test

```
tools/verify            # the gate: dmp group, scripts, build, im, pair, tests, gravity; exit 0 or 1 (tools/verify.ps1 on Windows)
dotnet build CM.slnx
dotnet test CM.slnx
```

Run `tools/scrub-scan.sh` before opening a pull request: it keeps identifiers of real organisations, hosts, people and credentials out of the repository.
