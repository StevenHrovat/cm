# 11 · Test design

Security context: this chapter is defensive security engineering under the charter in [CHARTER.md](CHARTER.md). It describes how CM proves, on its own estate, that
the company can be restarted from nothing.

The chapter is reserved for test design in the outline (Supernova levels as test tiers, sandbox first, the cold start rehearsed). The Supernova
levels and GRB runs are written in [06](06-black-hole.md) for now. This first version holds the part the owner asked for on 2026-10-25: the
criteria for a full, company-wide stable rollout. All sections are draft; the rule they explain is constitution **C24**.

**Terms (the owner, 10:13Z, proposed for C17 in DM1).** CMOS has no single owner. It is governed by two bodies that check each other at every level: **The Administrator** (always capitalised, with its article; everyone allowed to fully administer CMOS, allowed both by the organisation and by CMOS) and the **Lighthole Council (LHC)**, the members bound together by the minting ritual. Where this chapter and the files it links say "the owner" for a decision, that is the person speaking today; the rules themselves are written for The Administrator and the LHC. Open: whether the LHC members are exactly the five White Key holders.

## Stable rollout: the cold-start criteria (proposal)

The owner's criteria (2026-10-25 09:28Z and 09:32Z, in his words shortened): *CMOS goes stable company-wide only when every cold-start
scenario we can think of has been executed successfully in a sandbox, at least one cold start procedure has been tested on the current
infrastructure, and the most critical cold-start procedures have been simulated through a Milky Way simulator node.* He added: a closed list of the ten worst
scenarios, a pass line for each, an app through which the White Key holders can do every planned step, and a bundle with the repositories, a cross-checked guide and an offline coding agent.

Stable company-wide rollout needs all of these, on one build of the cold-start surface (the section after next):

1. **The catalogue** ([CM.BlackHole/docs/coldstart/catalogue.md](https://github.com/StevenHrovat/cm/blob/main/CM.BlackHole/docs/coldstart/catalogue.md)) is signed by the owner, and every
   trigger on its coverage table maps to a scenario.
2. **Sandbox:** every scenario in the catalogue passed its pass line in the sandbox, including the three combined cases.
3. **Simulator:** the scenarios marked M passed through a Milky Way simulator node.
4. **Real:** at least one cold start passed on the real infrastructure (the tier R below).
5. **The assistant** exists, every step of every scenario is a runbook step in it (coverage rule), and its Supernova level and GRB run are passed ([assistant.md](https://github.com/StevenHrovat/cm/blob/main/CM.BlackHole/docs/coldstart/assistant.md)).
6. **The bundle** is built, verified and in every copy location: repositories, the cross-checked guide, the media list and the offline agent with its exam result ([white-key-bundle.md](https://github.com/StevenHrovat/cm/blob/main/CM.BlackHole/docs/coldstart/white-key-bundle.md)).
7. **The system is armed** (ca-plan 1b): at least three White Keys, every holder cleared the challenge, and the **monthly liveness proof, the yearly ceremony and the stale-key succession** exist and passed their tests ([ceremony-and-staleness.md](https://github.com/StevenHrovat/cm/blob/main/CM.BlackHole/docs/coldstart/ceremony-and-staleness.md)).

Item 7 is the arming rule that already gates stable, plus the owner's later yearly-ceremony safety feature; it is listed here so the stable gate is one list.

Status: draft

## The pass line

A run passes a scenario only if all three lines hold. They are the owner's words; the definitions are this proposal's.

**P1 · Recovery within one week.** The clock starts at the event if it is known, otherwise when it is detected (the earlier), and stops when the **recovery list** is healthy and the assistant's Prove step has written
the report: 168 hours of wall time, waiting for people included. The recovery list is a register of the services the company needs, in tiers (identity, network, time and trust first); the order is recorded and
checked, only the week is the line. Hardware that cannot be bought inside the week is not a recovery step: each scenario states the **spare capacity it assumes**, and a run on less than that fails the line.
In a drill, the clock is the test's injected clock, never a stopwatch (TD1).

**P2 · No data loss beyond the last backup.** For each dataset, everything present at the time of its last successful backup is present afterwards, shown by comparing the restored data with the backup's manifest; what was
created after that backup is the only loss. The run records the **backup age at the event** per dataset and compares it with the declared interval. Restores come from backups whose content was signed when made (ca-plan 1b), so a restore cannot import a forged state.
Because the line is relative to the last backup, a standing alarm is part of the proposal: **the age of the newest offline copy has a limit** (a setting, exposed), so the weekly archival disk cannot silently become the company's recovery point (CS-02).

**P3 · Both trust chains intact.** After recovery, and checked by the assistant's Prove step:

- C1 · both roots are the same roots as in the record (fingerprints equal), or were reissued by a White Key majority under a signed record;
- C2 · the dual verdict (`DualTrust`) accepts a probe component and refuses one missing any of the six checks of ca-plan 1a;
- C3 · both CRLs are current with their overlap, no issuing CA or token signer is within its warning period;
- C4 · neither master issued anything in the other's chain, and the old root of the former PKI is neither trusted nor booted;
- C5 · the White Key set is the pinned set, or a new one signed by a majority; the count is at least three; no isolation is left on without a reason;
- C6 · the audit trail is continuous from before the event to the report, and every White Key read and act in it is announced;
- C7 · no recreation material (roots, signer keys, the sealed credential store, the White Key records, the warehouses' data keys) is outside its named copies in usable form; for the two roots the only named copy is the White Keys, and elsewhere only their certificates exist ([data-boundary.md](https://github.com/StevenHrovat/cm/blob/main/CM.BlackHole/docs/coldstart/data-boundary.md)).

P2 names, per dataset, the backup mechanism: company data in the company's backup product, CMOS information in CMOS's own signed and encrypted state backups, recreation material in the named copies only.

A scenario that passes P1 and P2 and fails P3 has failed: a company that runs on broken trust has not been recovered.

Status: draft

## The three tiers

```
 S · sandbox                   M · Milky Way simulator node             R · the real infrastructure
 floor 1 software (verify)     the most critical scenarios, driven      at least one, gated:
 floor 2 lab (synthetic        through IM as if real systems            R1 isolated restore on a lab node
 company on lab hosts)         answered; injected clock                 R2 scoped lights-out (later, yearly)
 every scenario                the ones marked M in the catalogue       the owner's words, a scope file
```

**S · The sandbox, two floors.** Floor 1 is software and runs in `tools/verify`: the decision logic, orders, thresholds, time, refusals, the runbooks parsed and dry-run. Floor 2 is the **lab**: a synthetic company
(a made-up one, no company data) built from the same shapes by the same pipeline on lab hosts, isolated, in which the operating-system and product behaviour is real (disk encryption, domain restore, backup restore, switch start). A scenario passes in the sandbox when it passed floor 1, and floor 2 wherever it involves an operating system or a product.

**M · The Milky Way simulator node.** The scenarios that pass through IM or trust (the catalogue marks them M) are run through a simulator node: sandboxed I/O and simulated real-time data (chapter 05), the assistant driving it as it would drive real systems, the clock injected so a week or a year passes in seconds. The owner accepts Milky Way as the simulator
layer **once it has passed Supernova and GRB**: the basic I/O engine is tested hardest of anything, so what it shows for IM is as near to reality as one gets without doing it. Therefore the gate for tier M is: the IM verifier at Supernova level 4 passed, with an independent GRB run, **on the IM version in use** (version 1 voids the evidence of version 0).
What the simulator does not show is the hardware and the operating systems: disk encryption, domain restore, storage rebuild, the switches, people. The catalogue states per scenario what tier M covers and what is left to S floor 2 and R. One cheap check is recommended on top, and is not the owner's rule: **replay the recorded I/O of the real run through the simulator and compare the outcomes**, so the simulator is shown to match at least once.

**R · The real infrastructure.** At least one cold start passes on the current infrastructure. A real cold start of the production estate is the thing nobody wants to cause, so the proposal is a ladder:

- **R1 · Isolated restore.** The cold start is performed from the vault and the offline copy, from the bundle, by a holder other than the builder, onto the lab node of the real cluster, into an isolated virtual network with no route to production. Copies of production backups are restored, never connected; nothing
  in production changes. First choice: CS-08.
- **R2 · Scoped lights-out.** A planned shutdown and cold start of a named part of the real estate in place, under a scope file and a window. Later, yearly, at the ceremony.

R1 satisfies the owner's "at least one cold start procedure on the current infrastructure" and is the proposal's recommendation for the first; R2 is the yearly ritual after it. Both are **gated**: they create machines and restore copies of production data, so they need a scope file (`templates/grb/scope-file.md`), the owner's own words, and for company data the pair of pages of constitution C16. **Nothing is run by this proposal.**

Status: draft

## The cold-start surface and when a run stops counting

A run counts for a promotion only if the **cold-start surface** has not changed since. The surface is: `cm.os.Trust` and `cm.os.Im`, the Milky Way verifier and gate, Doppler, the CA scripts and ceremony tooling, the operations master's credential store and backup and restore jobs, the cold-start assistant and its runbooks, the bundle builder, the White Key handling and the warehouse
lock and release code. A change to any of it invalidates the runs of the scenarios that touch the changed part (the catalogue lists which); the unchanged ones keep their result. Every run is on a named build, so a result says what it was run on. The yearly ceremony re-runs one scenario for real by rotation.

A future rule (`DMP-019`, the next free number, not written) will check this from a register `DMP/cold-start.txt`: scenario, catalogue revision, tier, build, run record, result; and refuse a stable promotion whose catalogue has a scenario with no counting pass in the required tiers. Until it exists, the review of the promotion pull request is the check and C24 says `Enforced by: none yet`.

Status: draft

## The run record

Each run writes `CM.BlackHole/docs/coldstart/runs/CS-<nn>-<tier>-<n>.md` (the report generated by the assistant), with: the scenario, the tier and floor, the build hash, the environment (role names only), the injected or real clock and its start and stop,
the P1 result, the P2 table (dataset, backup age, matched or not), the six P3 checks, every deviation, **every step done outside the app** (each is a finding), the signatures of the executor and a second person, and where the journal is kept. A failing run is recorded too and is not deleted when the next run passes.

Status: draft

## Open questions for the owner (recommendations)

1. **Where does the assistant live?** Recommended: a part of `CM.BlackHole`, so it needs no new level-1 node (the C17 proposal says a level-1 node needs the owner's approval).
2. **Decided (owner, 09:53Z): five White Keys at arming**, the two sysadmins and three members of the C-suite, majority three. With both sysadmins gone the C-suite keys still form a majority (CS-03 tests it, and the one-holder case).
3. **Decided (owner, 09:53Z): the first real test is R1 with CS-08**, an isolated restore on the lab node, which has the spare capacity because everything already replicates there. Agreed as the plan; it is still **gated**: the run needs a scope file and the owner's words to start, and nothing is run by this proposal.
4. **Is R1 enough for "at least one on the current infrastructure"?** Recommended: yes for the first stable promotion; the yearly ritual then moves to R2.
5. **What is the stop of the clock?** Recommended: the recovery list healthy plus the report, with the capacity assumption written per scenario.
6. **The agent's hardware.** A 256 GB-class machine is the price of a Haiku-class offline agent; recommended to decide after a measured speed and exam run, not before.
7. **The stale-key succession** (separate file): the dates, who names the successor, when the staleness certificate starts, the cooling period before anything is destroyed, the veto strength, the time sources.
8. **Decided (owner, 09:53Z): sealed copies may exist anywhere within the CMOS system**, the archival disk and the DR site being the most sensible. **Decided (owner, 10:01Z): the two roots exist only as certificates and on the White Keys**, so no root image is in any repository. The minting ceremony (owner, 10:03Z) stores verified, identical copies on all White Keys, and CMOS goes productive only after checking that on every key. Open: whether that copy opens with one key or needs three of five (recommended), see [data-boundary.md](https://github.com/StevenHrovat/cm/blob/main/CM.BlackHole/docs/coldstart/data-boundary.md).

Status: draft
