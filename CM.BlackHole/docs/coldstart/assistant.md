# The cold-start assistant (concept, draft)

Security context: this brief is defensive security engineering under the charter in `CLAUDE.md`. It describes a tool that helps the holders of the
company's own White Keys restart the operating organisation's own estate.

The owner (2026-10-25 09:32Z): doing a cold start by hand "is nearly impossible with an infrastructure that complex", so **everything that needs
doing is fully possible through that app**. This is the concept for it. Working title: the **cold-start assistant** (a star name is for full-size
modules and is the owner's to give).

## What it is, in one picture

```
  White Key bundle (encrypted disk)                          the holder's machine, no network needed
 ┌──────────────────────────────────┐   unlock = a White Key read   ┌─────────────────────────────────────┐
 │ assistant, runbooks, repositories│ ─────────────────────────────>│  Cold-Start Assistant               │
 │ guide, media list, offline agent │                                │                                     │
 └──────────────────────────────────┘                                │  Assess ─> Plan ─> Rehearse ─> Run  │
                                                                      │                      └─> Prove      │
   hardware token + PIN (the White Key) ──signs orders───────────────>│  journal (hash-chained, signed)     │
                                                                      └───────┬───────────────────┬─────────┘
                                                              reads / guarded writes       advice only
                                                                      v                           v
                                                   the estate, step by step            offline agent (never executes)
```

It is a **runner for signed runbooks**, not a pile of scripts. The knowledge of how to cold start lives in the runbooks (data); the app
carries them out, checks them and proves the result.

## Modes

| Mode | What it does | Writes? |
|---|---|---|
| **Assess** | Finds out what is alive: power, network, storage, hosts, domain, time, the CMOS parts, the chains (CRL and certificate dates, isolation state). Reads only. | never |
| **Plan** | Takes the holder's scenario (CS-01 to CS-10 or "I don't know") and the assessed state, and produces the plan: phases A to G, in order, with every step's read, expected state, verification and the White Key acts it needs. | never |
| **Rehearse** | Runs the plan against the sandbox, the simulator node or a lab copy, or as a dry run against the real estate (WOULD lines). | only in the lab |
| **Run** | Executes the plan step by step, each step through the guarded write of C15 (read the live state, assert it, write, read back). A step that needs more than one origin waits for the signatures. | yes, guarded |
| **Prove** | Runs the three pass-line checks (recovery list healthy, data matches the backup manifests, both chains intact) and writes the recovery report. | never |

## A runbook step

A step is data, so the guide, the app and the tests are generated from the same thing:

| Field | Meaning |
|---|---|
| id, phase, scenarios | where it sits, which scenarios use it |
| precondition (read) | the live state it reads before anything else, and what it asserts (C15: absent for a create, a named value for an overwrite) |
| action | an executor: a built-in action, a script (parsed under PowerShell 5.1, dry run first), a product command, or **human**: a physical act with a screen that says what to do and a check that it was done (read a disk serial, a link light, a console prompt) |
| acts | which White Key acts it needs: none, one key, a majority, or "the other master" |
| verify | the read that proves the result |
| soft or hard | what to do when the precondition fails: stop, or skip and report (exit code 3, never a clean run) |
| rollback | what undoes it, or "none: irreversible", which forces a majority |
| time and risk | an estimate and a risk line for the holder |

**Coverage rule (the owner's "everything is possible through the app").** Every step of every catalogue scenario is a step in a runbook; a
step done outside the app is a **finding** on the run record, and the number of such findings only goes down (a ratchet). The only steps the app does not carry out
itself are *human* steps, and even those are shown, timed and verified by it.

## White Keys in the app

- The app never holds a White Key. Signing happens on the hardware token: the app shows the exact bytes of the order, the token signs them, the holder confirms with the PIN. A holder sees what they sign.
- A step that needs a majority collects signatures from distinct holders (IM v1 multi-signature orders, PR #78), one at a time or on separate machines through a signed order file carried by hand. Distinct and sorted, as the order format says.
- One key does the reversible acts (isolate, lift, override, open the vault, restore signed state). The app refuses to offer a permanent write on one key; it does not hide the step, it shows who is missing.
- The app is **not an origin of trust**. It counts nothing as approval except a verified signature.
- Reads are journalled locally with who, when, what and counts. Announcement to the other holders cannot happen while the network is dead, so the app **queues the announcement and prompts the holder to make a call to the other holders** (a human step, recorded) and sends the rest when a channel exists.

## The journal and the recovery report

An append-only, hash-chained journal records every step: input, read, decision, write, read-back, signatures, time and the build hash of the app. It is the evidence for the pass
line and the material for the the knowledge warehouse write-back. The recovery report is generated from it: the clock (start and stop, DMP 11), the backup age per dataset, the chain checks, the manual steps found,
and the signatures of the executor and a second person. No value of the operating organisation is printed into a document that leaves the holder's hands; the report carries the facts as roles and counts.

## Offline by construction

- Runs from the bundle on a clean machine with **no network adapter needed**; it never calls home; no cloud dependency of any kind (the Claude API included).
- Self-contained .NET 10 (DMP chapter 10, "Language and runtime"), a Windows desktop front end in the operations master client's design language (dark, dense, one clear *next action* panel for a tired holder), and the **same steps as a command-line mode** (the owner's rule: a feature ships its command with its UI), so a holder who prefers a console, or a machine without a display, loses nothing.
- Texts in English and German through the localisation rule; German is the working language of the holders.
- Scripts the app runs follow the safe-script standard: the guard first, absolute paths, groups by SID, parsed under 5.1 and dry-run before they enter the bundle (DMP 10).

## Where it lives and how it is judged

- **Placement.** As a part of the Black Hole constellation (`CM.BlackHole`, a level-2 part, next to the CA tooling and the White Key handling it depends on), with its runbooks in
  the same folder. A new level-1 node would need the owner's approval (the C17 proposal); a part inside an existing constellation does not. Open question 1 in DMP 11.
- **Level.** Supernova **level 3** (it holds no key but it unlocks and drives privileged restores on machines, and it decides when to ask for a signature). Everything that verifies an order or a chain inherits level 4 from `Im` and `Im.Trust`.
- **The GRB run** for it is the "cold read" run: an independent session with only the bundle and the app, in the lab, must complete a scenario; a step it cannot complete is a finding.
- **Reuse.** It adds no new restore engine: Veeam jobs, the CA ceremony scripts (`tools/blackhole`), the guarded-write module, the operations master backup-restore jobs and the existing cold-start of the operations master server are called as executors.

## What it must never do

1. Hold, export or log a White Key, a PIN, a vault password or a secret.
2. Offer a permanent write that has only one origin.
3. Boot the old root of the former PKI (it is never booted; the one audit boot is a gated, separate act).
4. Reach the network on its own, or depend on Milky Way, the operations master or Doppler to run (they may be what it is rebuilding).
5. Let the offline agent execute anything.

Status: draft
