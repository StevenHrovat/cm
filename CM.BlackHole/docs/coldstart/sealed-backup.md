# The sealed backup of CMOS information (proposal)

Security context: this brief is defensive security engineering under the charter in `CLAUDE.md`. It describes how the operating organisation's own CMOS keeps its backups
under its own control.

The owner (2026-10-25 09:45Z): CMOS owns everything it generates, "the information generated through it and the applications and correlations enabled through the interweaving of those parts, that's
all new, that's all us". "We own it, we keep it, we never let it out." The operating organisation's hard data, files and knowledge stay with the operating organisation; CMOS only locks access to them and offers the interface (see [data-boundary.md](data-boundary.md)).
The consequence for backups: a plain copy of CMOS information in a Veeam repository, or on any other carrier, is a leaving. So CMOS's own backups are **kept inside CMOS or sealed under CMOS keys**. This is the mechanism.

## In one picture

```
  producer (the operations master, Doppler, Milky Way, a warehouse)             CMOS archive store (inside CMOS)        other carriers: archival disk, DR site, optionally a backup product
 ┌──────────────────────────────────────┐   sealed archive       ┌──────────────────────────┐             ┌──────────────────────────────┐
 │ 1 export the state (engine-native)   │ ─────────────────────> │ copy 1, chained manifest │ ──copies──> │ the same sealed files        │
 │ 2 sign the manifest (producer)       │                        └──────────────────────────┘             │ they hold ciphertext only    │
 │ 3 seal: random data key, AES-256-GCM │                                                                 └──────────────────────────────┘
 │ 4 wrap the data key twice            │
 └──────────────────────────────────────┘
      wrap A · online: opens only with BOTH masters' co-signed order        (day-to-day restores)
      wrap V · vault:  opens only with the cold-start vault, a White Key    (cold start)
```

The producer can **write** a backup but not **read** an old one: sealing uses public keys, and the private halves are somewhere else. A compromised CMOS part can therefore leak nothing from the past.

## The rules

**S1 · What is backed up is state, not machines.** CMOS VMs are not backed up as VMs. They are redeployed from the one repository (C7); their **state** (databases, registers, the settings store, the journals, the knowledge base, the warehouses' own indexes) is exported by the part itself with its engine's native, application-consistent method and then sealed. This removes the virtual TPM state and the runtime keys from every backup, which is the worst thing to leave on a carrier. It also makes the cold start a **redeploy and restore**, which the assistant already does.

**S2 · Seal before it leaves the producer.** The archive is sealed on the producer host, before it is written to any carrier, with a fresh random data key per archive (authenticated encryption, AES-256-GCM or stronger). The data key is wrapped **twice**:

- **wrap A (online):** to an unseal key that is usable only with a co-signed order from **both masters** (C19 shape: The operations master and Doppler), for routine restores and drills. One master alone, or one stolen host, cannot unseal anything.
- **wrap V (vault):** to a public key whose private half lives **only in the cold-start vault**, so a cold start needs no running master: a White Key holder opens the vault (a read, audited), and restoring signed state is a single-key act (ca-plan 1b).

Wraps use a hybrid key exchange (a classical curve plus a post-quantum KEM) because archives live for years and may be collected now to be read later; the exact algorithms follow the algorithm rule of ca-plan 5 and are checked against what .NET 10 supports on the target platform before the format is fixed.

**S3 · Signed when made, chained so that "the last backup" is provable.** Every archive has a manifest: dataset, producer role, time, build, a monotonic sequence number, the hash of the previous manifest of that dataset, and the hash of the sealed payload; it is signed by the producer (both chains where the producer holds both). The chain makes a deleted, truncated or rolled-back archive visible, and a counter in the producer's TPM makes a rollback to an older sealed set visible too. This is what makes pass-line P2 mechanical: the newest manifest is "the last backup", its age is known, and the restore is compared with its hash.

**S4 · Custody inside CMOS, copies on carriers.** The **primary** home is the CMOS archive store, a store inside CMOS with its own access rule. The archival disk (the weekly rotation), the DR site and, if the owner allows, a backup product are **carriers of sealed copies**: they hold ciphertext whose keys are not on them. A carrier never needs a key, so a carrier never has to be trusted; it only has to be there. The count and places of copies are recorded in the manifest chain, which is what the standing alarm on the age of the newest offline copy reads.

**S5 · Recreation material is not in the archive store.** The roots, signer keys, the sealed credential store's key rings and the White Key records go only to the named copies (data-boundary.md). They are not backed up by this mechanism; they have the ceremony and the vault.

**S6 · The format is locked early and lives long.** A sealed archive may be read years later, so its format is versioned, becomes backwards compatible the day the first archive is written for production (the same logic as C18), and the **bundle carries the unsealer** at every format version, with a test that opens a sample archive of each. A format change is a new version, never an edit.

**S7 · Restore is a job like any other.** Verify the manifest chain and signature first, then unseal, then restore through the guarded write (C15): read the live state, assert it, write, read back. A dry run unseals nothing and checks everything else. A restore on one key (wrap V) is for signed state only, and a restore that replaces a chain's keys is a majority act.

**S8 · The release re-wraps headers, not data.** When the stale-key succession releases the locks ([ceremony-and-staleness.md](ceremony-and-staleness.md)), the archives' data keys are re-wrapped to the new deployment's keys; the payloads are untouched, so this is small and fast. The old wraps are retired in the same step as the old logs, after the cooling period.

## If Veeam is used for CMOS anyway: two options

The owner (09:47Z): Veeam exports everything to backup files (the full backup file `.vbk`, its metadata `.vbm`, and in a normal chain the incremental files too), so those can be encrypted with any archiving program and that is done. The coordinator's alternative: Veeam's own job encryption with the key held by CMOS.
Both are workable. They differ in **when the plaintext exists, and where the key is while the job runs**.

| | **V1 · Wrap the files after Veeam writes them** (the owner's) | **V2 · Veeam job encryption, key held by CMOS** |
|---|---|---|
| Mechanism | The backup files are sealed by CMOS after the job (the sealing of S2, not a zip tool) and the plaintext is deleted | The job encrypts as it writes; the password or key comes from a CMOS-controlled source (the vault, or a key-management server if the installed version supports one; **to check on the installed version**) |
| Plaintext on the repository | **Yes, from the write until the seal.** The repository is then Veeam's working set (merges, retention and synthetic fulls need to read and rewrite the files), so a repository that Veeam manages stays plain. It works only as a **staging repository inside the CMOS boundary**, with Veeam's retention kept short and the history kept in the sealed archives | **No.** The files are ciphertext on the repository, on the replicas and on every carrier from the first byte |
| Key while the job runs | Not at Veeam. Sealing uses public keys; the private halves stay in the vault and behind both masters | **At the backup server**, because Veeam needs it to write, merge and restore. A stolen disk or replica is safe; a compromised backup server is not |
| Chain handling | Veeam cannot manage sealed files: restore means unsealing into a staging repository and importing the backup | Veeam manages everything as today |
| Integrity | Authenticated encryption and a signed, chained manifest (S3) | Veeam's own integrity checks; no CMOS manifest chain unless added |
| Cost | A staging repository, a seal step for large files, an unseal-and-import on restore. Many terabytes are slow to seal | Almost nothing to build; the key custody and the Veeam version's key options to check |
| What an ordinary archiver would lose | A password archiver is not authenticated encryption in most formats and may leave file names visible; use the sealing of S2 | n/a |

**What both share and S1 avoids:** with VM-level, application-aware backup the Veeam server reads the guest and sees CMOS information in the clear while it runs. Either Veeam is part of the CMOS boundary for those jobs (a dedicated backup server owned by CMOS, not the operating organisation's), or the producer exports and seals its own state and Veeam is not in the path (S1). So:

- **Recommended:** S1 for CMOS state, with Veeam out of the path. It is the smallest and the only one where the backup server never sees CMOS information.
- **If the owner wants Veeam's VM-level restore points for CMOS hosts that hold no root or signer key** (for convenience): use **V2 on a dedicated CMOS-owned backup instance** (job encryption, the key in the vault under wrap V), and **add V1's seal for every copy that leaves that instance** for the archival disk and the DR site. Encryption at the source and sealing at the exit together; neither alone covers both the plaintext window and the key custody.
- **If the owner wants to go further for the fun of it:** V3 (the RAM landing store) is V1 with the staging repository in memory. It buys "no plaintext on a persistent medium" at the price of an NFS endpoint inside the boundary and a gateway; it is a later slice, after the freeze, and S1 stays the cold-start path either way.
- **Never** any of the options for recreation material in VM images (data-boundary.md, S5).

### V3 · The RAM landing store: V1 with a staging repository that never touches a disk

The owner (09:48Z), for the fun of it and because everything in CMOS is small: for backup mode, Crater connects through Milky Way to a Sagittarius cache layer, which is switched into a **data-receiving layer** and offered as an **NFS store in RAM** that Veeam mounts. Veeam writes its backup into ephemeral memory; the file is compressed and sealed (S2) and goes out; if it does not fit, deduplicate until it does.
This is V1's staging repository made ephemeral: **no plaintext ever reaches a persistent medium**, and the landing store is served by Milky Way inside the CMOS boundary. It is design only and comes after the feature freeze.

```
 Veeam server (+ its gateway role) ──NFS (private link)──> Milky Way NFS endpoint (clustered) ──> landing store in RAM ──seal (S2)──> archive store ──> carriers
                                         ▲ provisioned by Crater through Milky Way on a Sagittarius cache host
                                         ▼ wiped and unmounted when the sealed copies are verified
```

How it works, in order: Crater asks Milky Way for a landing store of a stated size; the cache host exports a RAM-backed directory; the Veeam repository points at it; the job runs **one unit at a time** (one VM or one dataset); each finished unit is sealed as a stream, copied to at least two carriers, hashed back, and only then **evicted from RAM**, after which the manifest chain advances (S3) and the next unit starts.

**Risks and the answers:**

1. **A power loss in the middle loses the backup.** RAM is volatile by design, so the unit in flight is lost. The owner's answer (09:51Z): this is a small risk, because CMOS always runs on **at least a two-node Sagittarius cluster** for every data-serving function and keeps its **full redundant state and its entire command log until the backup has been read back bit-accurate**. A lost write is retried, and the last verified backup plus the command log rebuilds the state. This is the rule: **nothing is retired, trimmed or relied on until the sealed copy has been read back and compared bit for bit on two carriers**; only then does the manifest chain advance and the log before it become trimmable. Two things to keep honest: the command log is itself CMOS information, so it must also be shipped sealed to the carriers continuously, otherwise the same event that destroys the cluster (CS-01, CS-08) destroys the log with it and the pass line P2 is back to "the last backup"; and the log's coverage is only as good as what it records, which is a test (replay the log onto the last backup in the lab and compare with the live state). A power loss is also a drill of CS-07. The landing store must never be a dependency of the cold start: the assistant restores from sealed archives directly, with a minimal landing store of its own (a RAM disk on the rebuilt host) if VM-level restore is wanted.
2. **A forward chain needs the previous full in the repository, and the store is empty at every run.** So every landing run is a **full** of one unit, never an incremental on a previous run. History lives in the sealed archives, not in Veeam's chain. Restore is the reverse: unseal into the landing store and let Veeam import the backup.
3. **Veeam and NFS.** The owner thought Veeam speaks NFS natively; checked against Veeam's user guide ("NFS Share", "Gateway Servers", "Ports", read through a search result, not on the installed version 13): **yes, an NFS share is a supported repository type, but not directly.** A **gateway server** does the talking: a Windows or Linux machine added as a managed server that runs Veeam's data mover and mounts the share (the guide describes it as the machine that must have direct access to the share and sit close to it; whether the backup server itself may take the gateway role is **to check**). What follows for us:
   - **Who serves the share: Milky Way** (the owner, 09:54Z: all I/O goes through Milky Way, which is clustered and already inside CMOS). The NFS endpoint is a Milky Way I/O brick, so the landing store is the standard door, not a special server, and it inherits the cluster's availability. Two sides stay distinct, though: the *share* is served by Milky Way, and Veeam's *gateway role* is Veeam's own data mover on a Veeam-managed Windows or Linux machine (it can be the backup server itself), which mounts the share. The Veeam side sees the plaintext in any VM-level backup anyway (it reads the guest), so that machine is inside the boundary for the CMOS jobs or those jobs are not run (S1).
   - A **Windows gateway supports NFS 3.0 and 4.1 only** (not 4.0 or 4.2) and **cannot use Kerberos privacy (`krb5p`)**; a Linux gateway can. Ports 111 and 2049 (plus the mount, status and lock ports for NFS 3, which can be fixed).
   - NFS has no encryption of its own. The link between gateway and landing store is private and unrouted, or protected by `krb5p` on a Linux gateway (which needs a Kerberos realm that is not the operating organisation's domain, since the backup server is a workgroup machine) or by a tunnel. A tunnel is the simpler choice and also gives the in-flight encryption below.
   - The NFS server inside Milky Way is a protocol brick under C17 (`cm.io`), written in C# (DMP chapter 10, "Language and runtime") or a product that does the job behind Milky Way (a kernel NFS server on a Linux host with a RAM filesystem, controlled by Crater). Writing an NFS server in C# is the larger job; which one is open.
4. **Deduplication cannot guarantee the data fits.** Compressed or already-encrypted data does not deduplicate, and Veeam already compresses. The design does not rely on it. **The fit is done by the unit size**: the store only needs the largest single unit plus headroom, not the sum. The fallbacks, in order:
   1. one unit at a time (above), with a capacity check before each run from the last run's size plus growth, alarmed when the largest unit exceeds the free memory by a setting;
   2. deduplication and, if it helps, a smaller unit (split a VM's disks);
   3. an **ephemeral encrypted scratch** on local flash, with a per-run random key kept only in RAM, so a power loss destroys the key and with it the plaintext (same property as RAM);
   4. otherwise the unit is marked failed, alarmed, and shows as a gap in the manifest chain. Never a silent partial backup.

   **Compression while streaming** (the owner's idea, 09:51Z): the landing store compresses each block as it arrives and the seal never compresses again. Veeam already compresses inside the job, and compressed data compresses little further, so to get a real gain the Veeam job's own compression is set low or off, and a stronger streaming compressor runs in the landing store (the CPU cost moves from the Veeam proxy to the landing host and slows the stream). It is less efficient per second and more efficient per byte; whether it is worth it is **a measurement on the lab data** (ratio, throughput, memory), not a decision. Compress first, then encrypt, because encrypted data does not compress.
5. **In-flight encryption, optional ("because it's cool").** Encrypt each block as it is streamed into RAM, so the landing store holds ciphertext at rest in memory, with a per-run key that is **not** kept in the same VM (a virtual TPM, or the sealing service). It buys something real in one case: a memory dump, a checkpoint or a live migration of the landing VM captures only ciphertext. It does not help against an attacker who controls the landing host while it runs, which holds the key. It is cheap to add behind a switch (a setting, off by default) and it composes with the tunnel in point 3. Order on the way in: compress, then encrypt in flight; the final seal (S2) is a separate, longer-lived layer with different keys.
6. **Memory errors and the shared host.** A bit flip must not seal a bad copy: the sealed archive is hashed back against what Veeam wrote, and the host needs error-correcting memory. The cache host is also the RAM cache of the warehouses (never the only copy of anything), so backup mode and cache mode compete for memory and are scheduled apart.
7. **Compress, then seal, once.** Veeam already compresses inside the job; the seal does not compress again.
8. **Dedup finds duplicates in CMOS's own information.** Worth logging as a by-product, since the owner hopes it improves the data structures; it is not a design dependency.

## Moonshot, much later: Veeam speaks IM

The owner (09:55Z): since IT fully owns and answers for backups of every kind, could Veeam be extended to speak IM natively? Parked as a moonshot, not part of this proposal and not a criterion for anything.
Veeam is closed source, so it cannot be changed from inside. The realistic route, in order:

1. **A Milky Way adapter** that wraps Veeam's management REST API (jobs, restore points, restores) and its repository I/O (the NFS landing store above) in signed IM: every command to Veeam arrives as an IM order, every answer leaves as one, and the repository traffic is already inside Milky Way. Veeam stays unchanged; the door is Milky Way's, which is what C1 asks for anyway.
2. **A later step, only if the adapter proves too thin:** a plug-in or a purpose-built component on the Veeam side where the product offers an extension point (to be checked, since it is not known what the installed version offers).

What it would give: backup commands that need the dual signature like any other write, a backup run that leaves the same audit line as a CMOS job, and one fewer foreign door. What it would not do: make Veeam's own code trusted; it stays a foreign component behind the adapter. Place in the backlog, after V3 and after IM version 1 is locked (C18), because it would bind the adapter to the locked format.

**As a side project (owner, 09:57Z):** once CMOS frees up some time, try the integration together with Veeam, and use Gravity and Supernova to prove it and to help Veeam test its side, with the compliance benefit as a welcome side effect. Route: Veeam's partner or API program and its published interfaces only; no reverse engineering of the product. Still a backlog item, not a criterion, and nothing is contacted or built by this proposal.

## Where it lives and what it costs

- **Tree:** a part of `cm.os` (the foundation tier) named `Seal`, owned by Black Hole like `Trust`, with a `Scope:` README; it needs no new level-1 node. The exporters live with each producer.
- **Supernova level 3** (it handles keys and decides what may be unsealed); the format parser is fuzzed (bytes from a carrier are untrusted); the refusal suite covers a forged manifest, a broken chain, a rolled-back counter, a wrong key, a truncated payload, a wrong wrap, and one master alone asking for wrap A.
- **What exists to reuse:** the operations master's SQL restore drill and its backup-restore jobs, the guarded-write module, the CA tooling for the vault ceremony, the ordering of two masters' signatures from IM version 1. What is new: the export-and-seal step per producer, the archive store, the manifest chain and the unsealer.
- **Veeam:** CMOS VMs leave their VM-level jobs once the first sealed archives of their state restore in the lab (a delete of old restore points, a gated step). Company data in Veeam is unchanged.
- **Cost, honestly:** this is "rolling our own" for CMOS state only. The owner's alternative, wrapping Veeam's encryption, would keep the key on the backup server, which does not meet "never leaves" for CMOS information (data-boundary.md).

## How it is tested

| Test | Floor |
|---|---|
| Seal, store, restore: a round trip for each producer's state, byte-identical | S floor 1 and the lab |
| Cold start from the vault wrap only, with both masters dead (CS-08) | M, lab, R1 |
| Day-to-day restore needs both masters; one master, or either alone, refused | S floor 1, M |
| Tamper, truncate, reorder, replay an old archive, roll back the counter, swap the manifest | S floor 1 (refusal suite) |
| Fuzz the unsealer with bytes from a carrier | Supernova 3 |
| A carrier gone or lying: the chain still shows what is missing | S floor 1 |
| Unsealer of format version 1 opens a sample archive 12 months later (a standing test) | verify |
| Re-wrap after a stale-key release; the old wraps retired after the cooling period | M |
| V3: power cut mid-unit leaves the manifest chain unchanged and the unit failed, not partial; a unit larger than the store is refused before it starts | lab |

Status: draft
