# The cold-start catalogue (draft, for the owner's signature)

Security context: this brief is defensive security engineering under the charter in `CLAUDE.md`. It lists the worst cases for the operating organisation's
own estate so that CMOS can be shown to survive them. Nothing in it is executed until a scope file and the owner's words say so.

This is the closed list that constitution C24 and [DMP 11](../../../DMP/11-test-design.md) refer to. The owner asked for "the top 10 most scary,
awful, horrible, mind-melting scenarios we can think of" (2026-10-25 09:32Z). The list is closed on purpose: "every scenario we can think of"
is not checkable, a signed list is. A new idea is added as a new revision; it never blocks a run already underway and it only needs its own
runs, not a re-run of the unchanged scenarios.

Every value that names a place is a role (C9). The reference estate this list assumes, in roles: a three-node Hyper-V cluster with
Storage Spaces Direct in the primary data centre; a backup server outside the domain; a DR site in an external data centre with one
standalone Hyper-V host and one domain controller; a core switch stack; two admins, an executive copy of the White Key and a vault at the
main building; a Microsoft 365 / Entra tenant; the operations master server, Doppler (Linux), Milky Way, the CA chains, Sagittarius, the knowledge warehouse and the monitoring module.
Sources: the White Key rules in [ca-plan 1b](../ca-plan.md). The knowledge warehouse has no cold-start article yet; the write-back goes to its intake.

## The common spine

Every scenario uses the same seven phases; a scenario says which it needs and what is hard in each.

| Phase | What happens | Rule that applies |
|---|---|---|
| A · Declare and secure | A White Key holder declares the cold start. Isolation state is read. Every White Key read is journalled locally (the announcement waits for a channel). | White Key read/write rule (ca-plan 1b) |
| B · Ground | Power, the core network, time, name resolution, the console paths. Nothing here depends on Milky Way. | ca-plan 1a "never depends on Milky Way" |
| C · Identity and trust | Roots from the White Keys (the only place their keys exist), issuing CAs, the domain, Doppler, the operations master, the credential store. | one White Key may restore signed state; a new certificate needs a majority |
| D · Data | Restore from backups whose content was signed when made; SQL; files; the warehouses. | restore of signed state is a single-key act |
| E · Services | Everything on the recovery list, in tier order; Milky Way admits again only when both chains hold. | fail closed, twice |
| F · Prove | The assistant runs the three pass-line checks and writes the recovery report (DMP 11). | pass line |
| G · Return and write back | Isolation lifted by a White Key, announcements sent, lessons to the knowledge warehouse. | The knowledge warehouse intake |

## The ten scenarios

Reading key. **Gone** = what the scenario takes away. **Survives** = what we may rely on. **Hard** = the trap that makes it horrible.
**Keys** = which White Key acts are needed (one key = reversible acts or restoring signed state; majority = permanent writes).
**Tiers** = where it must run: S sandbox (always), M also through the Milky Way simulator node (it passes through IM or trust), R a candidate for the real run.

### CS-01 · The data centre is gone

- **Gone:** the primary data centre: cluster, backup server, core switches, firewalls, the local part of every chain.
- **Survives:** the DR site (replicas of the cluster VMs, one domain controller), the vault copies, the White Keys, M365.
- **Hard:** the DR site is one standalone host. It cannot run the whole estate, so the recovery list must be tiered by what fits, and the
  catalogue must state the capacity it assumes. The replica is up to a day old; the domain controller there is the only one left (forest
  recovery rules apply if its state is doubtful). Remote access to the DR site goes through the external operator.
- **Keys:** one for the vault and restored signed state; a majority if a root or issuing CA has to be reissued because the issuing CAs were in the lost site.
- **Tiers:** S, R (the capacity question is measurable without destroying anything).
- **Finding to close before the first run:** the sum of memory and storage of the tier 0 and tier 1 VMs against the DR host. Unknown today.

### CS-02 · The wiper: everything reachable is destroyed, backups included

- **Gone:** hypervisors, domain, the online backup repositories and their replicas, because the attacker held the credentials the backup server
  trusts. Every online copy is suspect.
- **Survives:** offline copies only: the weekly archival disk, the vault copies; the roots survive only on the White Keys (owner, 10:01Z).
- **Hard:** which copy is clean is not known; the newest offline copy may be a week old; the attacker may have been inside for longer than the
  retention of the clean copies; the credential store is suspect, so every secret rotates. Restoring a domain controller from a copy that
  predates the intrusion brings the intrusion's golden-ticket material only if it is older than the rotation, so the Kerberos service account
  resets twice, with the replication interval between.
- **Keys:** one to restore signed state; a majority for new issuing CAs (the chains are re-issued, not trusted back).
- **Tiers:** S, M (chain re-issue and re-enrolment of every device over IM), R candidate.
- **Note on the data boundary:** CMOS state is backed up by CMOS in signed, encrypted files (data-boundary.md), so it does not depend on the online repositories the attacker owns; company data does.
- **Note on the pass line:** "no loss beyond the last backup" is relative. Here the last surviving backup is the offline one. The proposal adds a standing alarm (DMP 11): the age of the newest offline copy has a limit, a setting, so the weekly disk cannot silently become the operating organisation's real recovery point.

### CS-03 · The people are gone: both admins lost, only the executive copy left

- **Gone:** both admins and their homes (their White Key copies, their vault wraps, their knowledge).
- **Survives:** the executive copy of the White Key in the main-building vault, the bundle, a competent but unfamiliar person, the estate itself.
- **Hard:** the owner agreed to **five** White Keys at arming (09:53Z): the two sysadmins and three members of the C-suite, majority three. With both sysadmins gone the three C-suite keys are still a majority, so a new CA certificate or a re-arm is possible, but only if all three can be reached and each can use the key; with three keys in the set the gap would be real (one left, a majority is two). The scenario therefore also runs with **only one C-suite holder reachable** (one key: reversible acts and restoring signed state only) and shows what the week allows. The holders are not builders: the assistant and the guide must carry them, and the yearly challenge applies to them too.
- **Keys:** a majority (three of five) if the three C-suite holders are reachable; one key if only one is. The scenario passes in the one-key case only if the recovery needs nothing more; if the sandbox shows it does, the key set or the assistant is wrong, not the test.
- **Tiers:** S, M (signed orders with one valid signature of three; refusal of the permanent kinds), R (the cold-start rehearsal is done by someone other than the builder, with only the bundle).

### CS-04 · The chains turn on each other: split brain, or a lockout by expiry

- **Gone:** nothing is destroyed; trust is. Both masters are alive and refuse each other (a bad key rotation, a mis-issued signer), or an
  expired CRL or CA certificate makes every verifier refuse every message (fail closed, twice).
- **Survives:** the data, the console paths, the White Keys.
- **Hard:** the isolation is correct behaviour and only a White Key lifts it; picking a winner is a permanent write (majority); no timeout
  helps. The expiry variant is silent until the moment it bites, and the monitoring that should warn runs on the same chains.
- **Keys:** one to override (lift both isolations); a majority to pick a winner.
- **Tiers:** S, M (this is exactly what the simulator is for: orders, thresholds, conflicts, refusals), R not needed.

### CS-05 · A root is lost or burned

- **Gone:** one root's key material on too many White Keys to reach the threshold (the roots exist only on the White Keys, owner 10:01Z; there is no image or backup to fall back on), or one root's key is known to be exposed.
- **Survives:** the other root and its chain.
- **Hard:** a new root for that chain only, a new pin in every agent, that chain reissued while the other still holds and nothing is admitted
  on one chain's word alone. The old root of the former PKI must not come back by accident (it is never booted). The ceremony host is a
  borrowed machine.
- **Keys:** a majority (a new CA certificate).
- **Tiers:** S, M (re-pin, re-enrol, token reissue), R candidate for the ceremony only on a throw-away root.

### CS-06 · Identity is dead: the forest and the tenant

- **Gone:** every domain controller (corruption replicated to all, including the DR one), and separately the cloud side: administrators
  locked out of the tenant by a conditional-access mistake, a lost second factor or a compromised administrator.
- **Survives:** backups of the controllers; the break-glass material in the vault; the owner's and the mailbox account exemptions.
- **Hard:** forest recovery is a fixed sequence (one controller restored, the rest rebuilt, roles seized, the Kerberos service account reset
  twice, machine accounts re-joined where the secure channel is broken); the tenant cannot be fixed from an offline bundle at all, so the
  step is a documented break-glass path that works from a clean machine; devices that join through the cloud need their own re-join plan.
- **Keys:** one to restore signed state; the tenant writes are gated steps of their own (C6).
- **Tiers:** S (lab floor with a synthetic forest), M for the CMOS side, R candidate (restored isolated copy of a controller, never connected).

### CS-07 · Lights out: everything off for a week, switches included

- **Gone:** power, then everything that holds state in RAM or configuration only: the core switch stack, the access switches and the
  firewalls come back with whatever configuration they last saved.
- **Survives:** the data on disk, the vault, the White Keys.
- **Hard:** the network starts before anything else and nothing can configure it remotely; switch configuration backups, console access
  and the order of start (network, storage, cluster, domain, time, then the rest) are the whole problem. After a week the clocks, the CRLs
  (seven days), the token signers (ninety days, renewed at sixty), machine-account passwords and the replay memories are all in a state no
  normal restart ever sees. A restart after more days than a CRL's overlap means every verifier refuses every chain.
- **Keys:** one (lift the isolation that the expiry may have caused); a majority if a signer or issuing CA certificate has lapsed.
- **Tiers:** S, M (time is injected, so a week passes in a second; this is the cheapest and most valuable simulator case), R candidate (a planned, scoped lights-out of the lab hosts).

### CS-08 · The brain is dead: The operations master, Doppler, Milky Way and their databases, with their keys unreadable

- **Gone:** the operations master server and its SQL database, Doppler, the Milky Way hosts, the Sagittarius and the knowledge warehouse nodes, together with the machine keys that
  protected their key rings and settings (a restore onto new hardware cannot decrypt what a machine key protected).
- **Survives:** the backups, the vault, the repositories in the bundle.
- **Hard:** the secrets in the sealed store are the way back into every other system, and the store is the thing that is gone; the vault holds only
  the root material and the cold-start secrets, so every other credential has to be re-entered, rotated or re-enrolled in an order that does not need
  the thing being restored. Every device must re-enrol on both chains with a fresh attestation. Financial data freezes at production (C5): the
  restore of Sagittarius must be bit-for-bit auditable, not merely "working".
- **Keys:** one to unlock the vault and restore signed state; a majority if a chain has to be reissued.
- **Tiers:** S, M, **R first choice**: restore the three services and their databases from the offline copy into an isolated virtual network on the lab node, performed from the bundle by a person who did not build them.

### CS-09 · The storage is gone under living hosts

- **Gone:** the storage pool or its volumes: corruption after a power event, a firmware defect shared by all three identical hosts, or a
  malicious delete at the hypervisor layer. The hosts boot; the virtual machines do not.
- **Survives:** the backup server and the DR replicas, if they are not on the same defect.
- **Hard:** the same firmware or driver on every host means the rebuilt cluster can hit the same defect; the volumes are managed only through
  PowerShell or the admin centre; restore volume is large (the whole estate, in tier order); backup chains may be inconsistent after the
  event. Hardware cannot be re-ordered inside the week, so the scenario states the spare capacity it assumes.
- **Keys:** one; a majority only if the chains' hosts are rebuilt with new certificates.
- **Tiers:** S (lab floor), M for the CMOS part, R candidate (a restore drill of the instant-recovery kind already exists for the backup server; this one adds the rebuilt pool).

### CS-10 · The recipe is gone or poisoned

- **Gone, variant 1:** the source repository and its hosting organisation (account suspended, organisation deleted, outage lasting days,
  a compromised maintainer) and the mirror is stale.
- **Poisoned, variant 2:** a stable promotion, or an agent with write rights, ships a defect that bricks Milky Way on every device or
  deletes at scale; the update path is the attack path.
- **Survives:** the bundle's repository copies and their signatures.
- **Hard:** C7 says one repository deploys every part, so losing it loses the way to rebuild anything; the update channel that would fix
  a bad build is the broken thing; the build pipeline must be rebuilt from the bundle on a clean machine; for variant 2 the previous stable
  build must be restorable and pinned by hash, and a runaway agent's writes must be undoable from the audit trail.
- **Keys:** one to roll back to a pinned state (restoring approved state); a majority to publish a new trust root for builds.
- **Tiers:** S, M (a poisoned update over IM is refused and rolled back), R not needed.

## Coverage: every plausible way in

The owner asked that every seemingly plausible interaction that can lead to a cold start is covered. The list above is by consequence; this
table is by trigger. **Rule:** every trigger on this table maps to at least one scenario, and a trigger that maps to none is a new scenario in
the next revision.

| Trigger | Scenarios |
|---|---|
| Fire, flood, building loss, theft of the racks | CS-01, CS-09 |
| Power loss, long utility failure, failed UPS | CS-07, CS-09 |
| Switch stack lost or misconfigured, lost configuration backups | CS-07 |
| Ransomware or wiper, credentials of the backup path stolen | CS-02, CS-06 |
| Compromised administrator or insider | CS-02, CS-06, CS-10 |
| Agent error: a job or an agent deletes or rewrites at scale | CS-10, CS-08 |
| Bad key rotation, expired CRL or CA, expired token signer | CS-04, CS-07 |
| Loss of a root, forgotten ceremony password, exposed key | CS-05 |
| Loss of White Key copies, death or absence of both admins | CS-03 |
| Firmware or driver defect across identical hosts | CS-09 |
| Backup corruption or an unnoticed failed backup chain | CS-02, CS-08, CS-09 |
| Cloud tenant lockout or compromise; identity provider outage | CS-06 |
| Hosting organisation or repository lost; poisoned stable promotion | CS-10 |
| External provider outage that lasts days (data centre, ISP, DR site operator) | CS-01, CS-07 |
| A missed White Key ceremony (see [ceremony-and-staleness](ceremony-and-staleness.md)) | CS-03, CS-07 |

## Where the hardest ones collide

Two scenarios happening together are not extra scenarios but the reason for the hardest checks:

- CS-03 inside CS-07: the executive holder restarts a dark estate alone. This is the one the owner described as "hit by a bus and the next day the operating organisation burns down".
- CS-02 inside CS-06: the wiper that also takes the tenant. The tenant steps are gated and cannot be offline.
- CS-04 after CS-08: the rebuilt masters disagree about who is right. The White Key majority decides.

Each of the three is run once as a combined case in the sandbox once the single cases pass.

## Sign-off

| Field | Value |
|---|---|
| Revision | 1 (draft) |
| State | draft / signed |
| Signatory | the owner of the framework |
| Signature | where the signed copy is kept (outside the repository) and the reference of the owner's words in the conversation |
| Next review | at every stable promotion that touches the cold-start surface (DMP 11), and once a year at the White Key ceremony |

Status: draft
