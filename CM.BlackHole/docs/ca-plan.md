# The new certificate authority: plan (draft)

Security context: this brief is defensive security engineering under the charter in `CLAUDE.md`. It plans the operating organisation's own certificate
authority for its own systems. Nothing in it is applied until the owner's words are in the conversation.

This is completion goal 1 of the CA lane. It rests on these decisions:

- A new CA is built, and the old one is neither trusted nor touched.
- Doppler signs the IM tokens.
- The authority grant (DMP constitution C6).
- The enrolled roles, the laptop included.

The code side of the trust decision is already built: [cm.os/docs/trust.md](../../cm.os/docs/trust.md). Every value below that names a
place (cluster, network, path, address, account) is a **parameter or a setting** and is never written here (C9).

**The owner's direction (2026-10-25), which this version follows:**

- The old root is not trusted, because nobody on the team has ever seen it. CMOS builds a **completely new CA from scratch**.
- The two run **in parallel**. Every system moves to the new CA step by step. Once it is proven that nothing needs the old CA any more, the
  old CA is **removed completely**. Ideally the old root is never booted again.
- The new root **never lives on an active hypervisor**. It is stored encrypted in several places at once:
  - the backup server, which replicates in full to the DR site;
  - the cold-start vault.
- It has a written power-on procedure that operations master can run.
- The algorithms are **the very best that everything supports**. Where it is not known what a system supports, read-only connectors find
  out before anything is chosen.
- **Two parallel, totally independent chains of trust** (the owner, later the same day). One comes from the operational master, the
  the operations master cluster. The other comes from the security master, the Doppler cluster. Unless every component of Milky Way is signed by both,
  trusted by both and accepted by both, it automatically loses access to the network (section 1a).
- **Each chain on its own platform, and isolation when they fall out** (the owner, 07:00 the same day):
  - Windows gear natively uses the Windows CA, which is the operational chain. Everything that is not Windows (network gear and the
    like) natively uses the Doppler CA, which is the security chain.
  - Doppler runs on Linux, so that a critical issue in Windows cannot break both chains.
  - Doppler and Milky Way stay separate systems.
  - The two worlds trust each other mutually. If that trust breaks, each isolates itself behind its own CA: Doppler takes the security
    chain and makes sure nothing gets out, and the operations master takes the management chain and makes sure nothing gets in.
  - Only the cold-start key lifts an isolation, and it is the tiebreaker generally (section 1b).
- **The White Key** (the owner's name, 07:16 the same day). The override is called the White Key: the security suite is Black Hole,
  and the only thing that opens it is white. The White Key is the cold-start key in its role as the override. "The cold-start key
  holders" means whoever holds a White Key. In a split brain, where both masters are alive but do not trust each other (a bad key
  rotation, for example), the White Key holders pick the winning side or override. Different algorithms per chain are fine with the
  owner, because CMOS operates those systems itself, and where it cannot, it documents how.

## 1. In one picture

```
 stored, never running (each: encrypted image on the backup server -> DR site, and in the cold-start vault)
 ┌──────────────────────────────────┐                          ┌──────────────────────────────────┐
 │ OPERATIONAL root (the operations master)        │                          │ SECURITY root (Doppler)          │
 │ Windows, AD CS                   │                          │ Linux                            │
 └──────┬───────────────────┬───────┘                          └───────┬───────────────────┬──────┘
        │ ceremony only     │                                          │ ceremony only     │
        v                   v                                          v                   v
 ┌──────────────┐  ┌───────────────────────┐            ┌───────────────────────┐  ┌──────────────┐
 │ Windows      │  │ Operational CMOS      │            │ Security CMOS         │  │ Network      │
 │ issuing CA   │  │ issuing CA            │            │ issuing CA            │  │ issuing CA   │
 │ (enterprise, │  │ the operations master requests, on a │            │ Doppler requests,     │  │ Doppler, on  │
 │ AD)          │  │ plan                  │            │ after TPM attestation │  │ a plan       │
 └──────┬───────┘  └───────────┬───────────┘            └───────────┬───────────┘  └──────┬───────┘
        │                      └─────────── the same device key ────┘                     │
        v                                  │ IM: both chains, both tokens, both registers  v
 Windows gear: domain machines,            v                                    network gear and everything
 DCs, Windows servers         every Milky Way component                         not Windows: switches, Wi-Fi,
        ^                                                                       firewall, printers
        └──────────────── mutual trust (each world trusts the other root) ─────────────────┘
            broken -> isolation: Doppler lets nothing out, the operations master lets nothing in; only the White Key lifts it

 old root (never booted) ─ old issuing CA (keeps running) ── phase by phase, every system moves ──> removed (phase 4)
```

- **Two independent roots on two platforms** (section 1a). The operational root is the operations master's, on Windows with AD CS. The security root is
  Doppler's, on Linux. They share no key, no ceremony secret, no issuing CA, no requester and no operating system.
- **Four issuing CAs** (section 6), two under each root:
  - the **Security CMOS issuing CA** is the one already planned and built against. Doppler is its only requester, and it is limited to
    the CMOS roles;
  - the **Network issuing CA** gives network gear, and everything else that is not Windows, its certificates. Doppler requests them on a
    plan;
  - the **Operational CMOS issuing CA** is the security one's twin, and the operations master is its only requester;
  - the **Windows issuing CA** (enterprise, in AD) takes over what the old issuing CA does for Windows today, which is domain
    certificates through auto-enrolment.
- **Isolation** (section 1b). Each world isolates itself when the trust between the two masters breaks, and only the White Key lifts
  an isolation.
- **The parallel run has five phases**, 0 to 4 (section 2). Each phase ends on evidence, never on a date.
- **The algorithms** (section 5): different families for the two roots. Which root gets which family follows the inventory, because
  network gear now sits under the security root. This replaces the old defaults: RSA 4096 for the CAs and P-256 for the leaves.
- **The old root is never booted** (section 9). That is possible because the old chain is retired by taking away the trust in it.
  Nothing has to be revoked with the old root's key.

## 1a. Two independent chains of trust

**The rule.** A Milky Way component is admitted only while **all six** of these hold. Each check runs on every message, so a component
that stops meeting any one of them loses access at its next message:

| | Operational chain (the operations master) | Security chain (Doppler) |
|---|---|---|
| **Signed** | a leaf from the Operational CMOS issuing CA | a leaf from the Security CMOS issuing CA |
| **Trusted** | the chain validates to the operational root, not revoked, right role | the chain validates to the security root, not revoked, right role |
| **Accepted** | The operations master's register says the component is in this role (in its inventory, on a plan) and an operational token signed by the operations master is valid | Doppler's register says the component is enrolled and attested, and a security token signed by Doppler is valid |

**Each master attests a different fact**, which is what makes the chains independent in substance as well as in keys:

- Doppler says "this machine is what it claims and is healthy now": TPM attestation and the measurement policy.
- The operations master says "this machine is supposed to exist, in this role": the inventory, a plan, four eyes.

An attacker who owns one master (a forged enrolment at Doppler, or a forged inventory entry in the operations master) still gets nothing. The other
master has to agree independently.

**How a component holds both.**

- **One device key.** It is generated in the TPM and is non-exportable.
- **Two certificates for that same key**, one from each chain. Both leaves must certify the same public key: the verifier checks that
  their SubjectPublicKeyInfo is identical, so one proof of possession covers both.
- **Two tokens.** The security token is signed by Doppler's token signer (security chain). The operational token is signed by the operations master's
  token signer (operational chain). The permissions a component gets are the intersection of the two.
- Independence lives in the issuers and the registers. Two keys on one TPM would add no independence.
- **Each master checks the TPM evidence itself.** If the operations master certified the device key only because Doppler said it was a TPM key, the
  operational chain would trust Doppler and the chains would not be independent. So the agent sends the operations master the same attestation
  evidence it sends Doppler (the endorsement key, the attestation key's credential activation, the certify of the device key). The operations master
  verifies it before it issues. The verification is a plain library with no dependency on either master: today it is
  `CM.BlackHole/src/Doppler/Tpm`, and it moves to `cm.os` when the operations master uses it. Both masters running the same verification code is a
  shared dependency, but not a shared decision. A defect there is a Supernova level 3 finding for both masters.
- **Transport.** TLS presents one client certificate, so the security leaf stays the TLS client certificate, as built. The operational
  leaf and the operational token travel inside IM (the dual handshake, DMP 04).

**What stays independent:**

- The two roots run on **different operating systems and different CA software**: the operational chain on Windows with AD CS, and the
  security chain and Doppler on Linux (section 4, Q5). A critical issue in Windows, or in AD CS, cannot break both chains.
- The two roots are created in separate ceremonies, with separate disk and backup passwords and separate vault wraps.
- They use separate issuing CA VMs, on separate hosts where the cluster allows it.
- They use separate requesters: Doppler never asks the operational CA and the operations master never asks the security CA.
- The two signature algorithm families are different by recommendation (section 5, Q4), so a break in one family does not open both chains.
- Neither master can mint, revoke or renew anything in the other chain.

**What it costs: fail closed, twice.** Access ends if either chain fails: an issuing CA is down, a CRL is expired or a register cannot be
reached. That is the intent, but an outage of one master locks out every component. Mitigations, all in the build:

- CRL overlap and verifier caching longer than any planned maintenance;
- The monitoring module alarms days before any CRL or CA certificate expires, on both chains;
- both masters run as clusters, and they watch each other's health (`cm.os.Health`, already built for Doppler and Milky Way);
- the cold-start path and console administration never depend on Milky Way, so a lockout is recoverable by hand.

**Scope.** The double rule is for everything that speaks IM, meaning every Milky Way component. Outside IM, each system uses the chain
that is native to its platform, because Windows and network gear cannot require two chains at once for TLS or 802.1X:

- **Windows gear** (domain machines, domain controllers, Windows servers) uses the operational chain, through the Windows issuing CA.
- **Everything that is not Windows** (switches, Wi-Fi, firewall, printers, appliances) uses the security chain, through the Network
  issuing CA.
- **Mutual trust between the two worlds.** Windows gear trusts the security root, so it can talk to network gear. Network gear trusts
  the operational root, so it can accept Windows clients, for example for 802.1X. This is done by adding each root to the other world's
  trust stores. It is never cross-certification, so either side can withdraw it on its own.

Where RADIUS runs (on the Windows side today, or on the network side) is a row of the inventory, and it decides which chain its server
certificate comes from.

**Where it lands in code:**

- `Im.Trust` (Black Hole): the dual verdict `DualTrust` is built. Both chains are validated, each against its own anchors and role, with the same public key, and a root configured for both masters is refused.
- IM (Milky Way) carries both chains and both tokens under one device signature. This is **IM version 1**, not an extension of v0,
  agreed with the Milky Way lane: v0 has no negotiation, so a gate runs exactly one version, and v0 stays in the sandbox. It is drafted as
  a DMP 04 section. Its refusal counters get a chain dimension, and the permissions are the intersection of the two tokens.
- v1 changes the decision code, so the Supernova evidence for v0 (GRB, mutation, fuzzing) has to be produced again for v1.
- The gate checks both registers.
- The operations master gets a token signer and a register.
- The sandbox pair test grows an operations master stand-in. It proves that a component missing any one of the six checks is refused.

## 1b. Isolation, and the White Key as the tiebreaker

**When it starts.** A master isolates its world when it has **evidence** that the trust relation with the other master is broken:

- the other master's signed health report is refused (`PeerWatch` says Untrusted: wrong chain, bad signature, replay, a time from the
  future);
- the other master's chain no longer validates (its root or issuing CA revoked or missing);
- Doppler sees Milky Way accept a token id that Doppler never issued (the alarm already built);
- an isolation order signed with a White Key.

**Silence alone does not start an isolation.** A master that only goes quiet (Stale) raises alarms, and IM already fails closed, because
nothing new is admitted without both chains. Isolating on silence would hand anyone who can cut a cable or stop a VM a switch that shuts
the operating organisation down. The owner agreed (Q6, 2026-10-25).

**What each master does.**

| | Security isolation (Doppler) | Operational isolation (the operations master) |
|---|---|---|
| Aim | nothing gets out | nothing gets in |
| Acts on | Milky Way (the gate admits nothing new and ends sessions), Event Horizon (outbound closed), the network gear under the security chain (egress closed by the firewall's own rules) | the management paths: no inbound remote administration except the console, all write jobs paused, no new operational certificates or tokens |
| Keeps working | the console paths and the cold-start path | the same |

Both can be in isolation at the same time, because each acts on its own evidence.

**How it ends: only the White Key.** No timeout and no automatic recovery lifts an isolation. A White Key holder signs a statement,
saying which isolation to lift, the reason, and the time. Both masters verify it against the pinned public keys of the White Keys, and
each use is audited and announced to the holders of the other copies. Generally, whenever the two masters disagree and need a
tiebreaker, a statement signed with a White Key decides.

**Split brain.** Both masters can be alive and each distrust the other, for example after a bad key rotation. Neither then outranks the
other. Each holds its isolation until a White Key holder signs one of two statements:

- **pick a winner:** one side's chain is the one to keep, and the other master rebuilds its trust from it. Rebuilding is a permanent
  write, so it needs a **majority of the White Keys** (arming, below). A master's own signature does not count, because the masters are
  the ones in dispute;
- **override:** lift both isolations as they stand. Lifting is reversible, so one White Key is enough.

**The White Key rule** (the owner, 2026-10-25 07:19):

- **Read.** A White Key holder has permanent, unlimited read access to the entire system for as long as that White Key exists. When a
  copy is revoked (lost, replaced), its access ends.
- **Write.** A White Key holder can never be the single origin of trust for a permanent write.

The owner wrote "permanent write (maybe)". This plan reads it as **permanent** (Q7). Under that reading, the White Key alone may do
what is reversible or returns to a state that was already approved:

- isolate;
- lift an isolation;
- override a split brain;
- unlock the cold-start vault;
- restore from a backup whose content was signed when it was made (the origin of trust is the backup's signature, and the White Key
  only opens it).

It never authors anything new on its own. An unarmed system (dev, nightly) accepts only the reversible orders. A new key, a trust change, a rebuilt chain, or any change to data needs a second origin: the
other master, a second White Key holder, or the normal plan with four eyes.

Why not every write: in a total lockout, one admin alone must be able to act (the cold-start rule). If a lift needed two origins,
a lockout with one reachable holder could not be ended.

**Reads leave a trace.** Every White Key read is audited (who, when, what, counts) and announced to the holders of the other copies,
the same as every other White Key use. Content that carries a class (personal data, mail) keeps the privacy shield's rules for how it is
shown. The access is unlimited; it is not invisible.

**The White Key** (lane L10: the cold-start key of the cold-start vault, in its role as the override). It is secured by hardware
and knowledge (a hardware token and its PIN). There are three hardware copies: one each at the homes of the two admins, and the
executive copy in a vault at the operating organisation's main building. For a reversible act, any one copy is enough. An attacker would need a
break-in, and even then the use cannot stay silent, because it is announced to the other holders.

**Arming: the White Keys are the entry ticket to stable** (the owner, 08:02 the same day). A CMOS system enters a stable deployment
only once it is **armed**:

1. At least **three White Keys** are created, and both masters record each one's public key. The keys are pinned, not chained, so they
   still work when the two masters distrust each other.
2. **Every holder clears a challenge** set by the CMOS system, proving the technical ability to use the key: the token, its PIN, and
   the signing procedure, on the console path that works in a lockout.
3. Only then is the system armed, and the stable deployment goes ahead.

**Once armed, the majority rules.** Disarming the system, issuing a new CA certificate (a new root, or a new issuing CA certificate
from a root), adding or revoking a White Key, and every other permanent write that the White Keys authorise need **a majority of all
White Keys of that system**, so two of three. The majority has absolute authority over the minority: when holders sign statements that
contradict each other, the side with the majority of signatures wins. A single key keeps only the reversible acts above.

**Keeping the majority alive.** Once two of three keys are lost, no majority can be formed again, so the system could never be
disarmed or given a new CA certificate. Two guards against that:

- the challenge repeats (recommended yearly, a setting), so a lost or forgotten key is found while a majority still exists and can
  replace it;
- the count is odd and may grow: five keys make a majority of three and survive two losses (Q10).

**How it is switched on, in order:**

1. "Would isolate": alarms only, on the sandbox and in production.
2. Enforcement on the sandbox, with a GRB run against the triggers.
3. Enforcement in production.

Step 3 writes to production network gear automatically, so it needs the owner's words, and the isolation decision is Supernova level 4,
because it decides what may leave and enter.

## 1c. A CA per stage: sandbox, dev, nightly, stable

The roots of sections 1a and 4, with their ceremonies, are the **stable** CAs. The other stages follow the build stages (the owner, 08:02
the same day):

| Stage | Its CA | Lifetime | Trusted by |
|---|---|---|---|
| sandbox | in memory (`SandboxAuthority`), per test | the test | the test |
| dev and nightly | an **ephemeral stage CA**: a pair of roots (security and operational), created by the stable system when the deployment starts | the deployment, at most the nightly cycle | that deployment only |
| stable | the two roots of this plan, after arming (section 1b) | years | the operating organisation |

Nightly and dev systems run on the stable systems, so the stable system creates their CAs. **Recommendation (Q9):** the stage CA is a
root of its own. It is not a sub-CA signed by a stable issuing CA, for two reasons:

- the stable issuing CAs have path length 0 and cannot sign a sub-CA;
- a stable-signed stage CA would make every certificate of less-tested nightly code trusted by stable verifiers.

Instead, the stable masters **vouch** for the stage roots with a signed, audited record: the fingerprints, the deployment, created at,
and expires at. The nightly deployment pins exactly those roots, and stable never trusts them. A stage CA needs no ceremony, no White
Key and no vault copy. It is thrown away with its deployment.

## 2. The parallel run, in phases

| Phase | What happens | Ends when (evidence) | Gated |
|---|---|---|---|
| **0 · Inventory** | Read-only. What trusts the old root, which certificates of the old chain are in use, where, until when; and which algorithms every consumer supports (section 3) | every consumer class has a row with its source; the algorithm decision (section 5) is written | reading the old issuing CA's own database is "anything on the old CA" (Q3) |
| **1 · New roots and issuing CAs** | The two root ceremonies, then the four issuing CAs (two CMOS, Windows, Network) from the scripts | every issuing CA certificate is dumped and checked (path length 0; the EKU limit on the CMOS ones) | **yes**: creating VMs, the ceremony |
| **2 · Trust in parallel** | The operational root is added to the trust stores **next to** the old one: Active Directory for domain machines, an Intune profile for Entra-joined devices, configuration for network gear. A test group goes first | every consumer class trusts both roots, and one test certificate per class from the new chain is accepted | **yes**: distributing a root to real managed devices, and forest writes |
| **3 · Move, class by class** | Each consumer gets its certificate from the new chain: domain controllers, servers (web, RDP, WinRM, SQL, monitoring), RADIUS, Wi-Fi and network gear, printers, Intune profiles. On the Windows issuing CA the old templates are rebuilt one by one and auto-enrolment moves over; network gear and other non-Windows systems get theirs from the Network issuing CA. The old templates are unpublished (they stop issuing; nothing is deleted) | the inventory shows **zero certificates of the old chain in use**, and watching shows no use of the old chain for a period (a setting; recommended 60 days) | each class's switch is a write to real systems: the owner's OK per class; tenant and forest writes are gated |
| **4 · Remove the old CA** | The old root is taken out of every trust store, first for the test group and then for everyone. The old issuing CA is decommissioned: final CRL, AD objects removed, role uninstalled. The old root's stored copy is destroyed, which destroys its key | no failure for the same period after the trust is gone; the owner says so | **yes**: the old CA, every step |

"Watching" in phase 3 means:

- The monitoring module reads the chain that every known TLS endpoint presents.
- The operations master counts chain-building events on Windows machines (CAPI2) that name the old root.
- Doppler and Milky Way refuse the old root already today, so they have nothing to watch.

Counts only, never content.

## 3. The inventory (phase 0, read only)

Every consumer class gets one row with two facts, and each fact has a source:

1. **Use:** does it trust or use the old chain, with which certificate, and until when?
2. **Support:** which signature algorithms does it verify (RSA key sizes, ECDSA curves, SHA-2 sizes), and which can it hold for its
   own key?

A vendor page counts as evidence for support. A read of the device is better.

| Consumer class | Read through (read only) | Exists today? |
|---|---|---|
| Active Directory: certification authorities, AIA, CDP, enrolment services, NTAuth, templates | LDAP, the operations master AD connector | AD connector yes; the PKI container read is new |
| Group policies: auto-enrolment, trusted roots | The operations master AD/GPO read | partly |
| Domain controllers: KDC and LDAPS certificates | CIM/WinRM read of the machine store | new |
| Windows servers: certificates bound to web, RDP, WinRM HTTPS, SQL, monitoring, backup | CIM/WinRM, Action1 inventory | new |
| TLS endpoints of known services (from the CMDB and the monitoring sensors, never a sweep of the network) | a the monitoring module probe that reads the presented chain | new probe kind |
| RADIUS/NPS: EAP server certificate, which client certificates it accepts | NPS configuration read | new |
| Switches, Wi-Fi, firewall and VPN: trust stores, own certificates, supported algorithms by firmware | the read-only switch connector (lane 5), the same for the others | lane 5 for the switch stack; others new |
| Printers and multifunction devices | their management interface, read | new |
| Intune: trusted-certificate, SCEP and PKCS profiles, the certificate connector | Graph read | Graph connector yes; these reads are new |
| Entra ID: certificate-based authentication, trusted CAs | Graph read | new |
| Clients (domain and Entra-joined): machine store contents | Intune / Defender inventory | partly |
| The old chain itself: the published root CRL and issuing CA CRL (next update), the issuing CA certificate (expiry) | HTTP or LDAP read of the published files, **not** the CA | new |
| The old issuing CA's list of issued, unexpired certificates | `certutil -view` on that CA, read only | **Q3** |

Each new read is a connector capability in the operations master, with its own pull request, documented in the operations master `docs/CONNECTORS.md`. The inventory
is an operations master page: one row per consumer, a red cell where a fact has no source yet. Phase 0 is done when no red cell is left in the
**Use** and **Support** columns.

## 4. The roots

Everything in this section applies to **each** of the two roots separately (section 1a): two VMs, two ceremonies, two sets of passwords.
The table describes the operational root (Windows, AD CS). The **security root** differs only in its platform:

- a Linux VM without a network adapter;
- the disk encrypted with LUKS, with a passphrase only and no TPM unlock;
- the CA software from Q5;
- a key backup encrypted with a passphrase from the vault.

The storage rule, the ceremony host, the power-on job and "the operations master never holds the passwords" are the same. Doppler's power-on job for
its root is Doppler's own, so neither master can start the other's root.


| Item | Value |
|---|---|
| Type | AD CS standalone root CA, never joined to any domain, a Generation 2 VM **without a network adapter** |
| Key and hash | from the algorithm decision (section 5); the script takes them as parameters and has no default |
| Validity | 20 years. When a post-quantum root is needed, it is added in parallel in the same way as this plan |
| Root CRL | valid 12 months, overlap 4 weeks, signed at the yearly ceremony. The old root's 120-month CRL is not copied: a revocation must not take ten years to matter |
| Disk | the guest's system disk is BitLocker-encrypted with **a password protector only**: no TPM, no virtual TPM and no automatic unlock. The VHDX is therefore an encrypted file wherever it lies, and it boots only when an admin types the password at the console |
| Where it is stored (**superseded 10:01Z: the key material exists only on the White Keys, the list here is the earlier plan**; never on an active hypervisor) | (1) the backup server, as a backup encrypted again by the backup job; (2) the DR site, through the backup server's full replication; (3) the cold-start vault: the VM image plus `certutil -backup` of key and database, password-protected. The `certutil` backup is the authoritative copy: if every image is lost, a new NIC-less VM restores from it |
| Where it runs | a **ceremony host** (a parameter): Hyper-V on a machine that is not a cluster node, not domain-joined and holds nothing else. Recommended: a spare laptop kept for this. The VM exists there only during a ceremony |
| Ceremony | yearly (CRL) and for an issuing CA certificate. Either admin alone can run it (the cold-start rule), and the second admin is informed. Every ceremony is a plan with an audit line |

**The power-on procedure** is an operations master job (wave 2) with a dry run:

1. Restore the newest image from the backup server to the ceremony host.
2. Check its hash against the one recorded at the last ceremony.
3. Start it without a network adapter.
4. **Stop and wait for the admin.** The admin types the BitLocker password at the VM console and runs the ceremony's steps.
5. Shut the VM down and take a new image.
6. Hand the image to the backup job and record its hash.
7. Wipe the VM from the ceremony host.

The operations master can therefore power the root on, but it never holds the BitLocker password or the `certutil` backup password. Both live in the
cold-start vault, with one wrap per admin. A compromised the operations master can start the VM, but it cannot unlock it or sign anything with it.

**Stronger option, not recommended for now (Q2):** keep the root key in a hardware security module, so that no copy of the key exists in
any image at all. Hyper-V cannot pass a USB device through to a VM, so with an HSM the root would be a physical offline machine, not a VM.
That departs from the owner's picture. The recommendation is the VM with the password-only disk and the vault copies.

## 5. Algorithms: the very best that everything supports

**The rule.** For each CA, the strongest signature algorithm that **every consumer below it** verifies, by the inventory's evidence.

**The two roots (Q4, the owner agreed on the intent).** The two roots get different algorithm families, so that a break in one family
does not open both chains. Within its family, each takes the strongest choice:

- **ECDSA P-384 with SHA-384** for one root;
- **RSA 4096 with SHA-384 or SHA-512** for the other.

**Which root gets which family follows the inventory**, because network gear is now under the security root, and network gear is where
ECDSA support is least certain:

- if every class of network gear verifies ECDSA P-384, the security root is ECDSA P-384 and the operational root is RSA 4096;
- if not, the families swap: the security root is RSA 4096, which is proven to work with today's gear (below), and the operational root
  is ECDSA P-384, which Windows verifies.

The leaves and both token signers stay ECDSA P-256 where the consumer allows it, because IM verifies one algorithm.

- **The root** is verified by every consumer of both issuing CAs, so it takes the weakest consumer's best.
- **The CMOS issuing CA's consumers are CMOS's own code**, which verifies ECDSA P-384 and SHA-384 on .NET. It can therefore be stronger
  than the native issuing CAs, but only if the root's algorithm allows it.
- **Leaves** follow what each consumer can hold.

| Candidate (best first) | Root and issuing CAs | Leaves | Chosen when |
|---|---|---|---|
| A | ECDSA P-384, SHA-384 | ECDSA P-256 or P-384, RSA 3072 where a product needs RSA | every consumer class verifies ECDSA P-384 signatures |
| B | RSA 4096, SHA-384 (or SHA-512) | as in A | a consumer that cannot verify ECDSA must keep working with the new chain |

**What is already known** (the knowledge warehouse, `systems/pki/zweistufige-pki`): the old root is RSA 4096 with SHA-512, and every system that uses
the old chain today accepts that. So candidate B is proven compatible. Candidate A needs evidence per class, and it usually fails, if at
all, on printers, older switches and older RADIUS clients.

**Post-quantum.** Before the decision, check whether AD CS and the consumers support post-quantum signatures (ML-DSA). Network gear and
RADIUS clients are not expected to for years, so this root is classical. Its successor is added in parallel later, the same way.

**Decision point:** at the end of phase 0, written into this section and into an operations master decision, with the inventory rows that decided
it. The issuing CA script refuses stages Role and Root until the algorithm is given explicitly.

## 6. The issuing CAs

| Issuing CA | Under | Platform | Requester | Serves | Its own certificate |
|---|---|---|---|---|---|
| **Security CMOS** | security root | Linux, the CA software from Q5 | Doppler only, after TPM attestation | the security leaf of every Milky Way component | path length 0; EKU limited to the five CMOS role OIDs (`Im.Trust.TrustOids`) plus server and client authentication |
| **Network** | security root | Linux, the same software, its own key and instance | Doppler, on a plan | network gear and everything else that is not Windows: switches, Wi-Fi, firewall, printers, appliances | path length 0; EKU limited to the purposes the inventory finds (server and client authentication) |
| **Operational CMOS** | operational root | Windows, AD CS standalone subordinate, Server Core, workgroup (`tools/blackhole/issuing-ca.ps1`) | The operations master only, through one JEA endpoint over WinRM HTTPS | the operational leaf of every Milky Way component | as the Security CMOS one |
| **Windows** | operational root | Windows, AD CS **enterprise** subordinate, domain-joined, Server Core | domain auto-enrolment, and admins by template | Windows gear: domain machines, domain controllers, Windows servers; the templates the inventory finds | path length 0; EKU limited to the purposes the inventory finds in use (no "any purpose") |

Common to all four:

- CRL valid 7 days, overlap 2 days. The requester fetches it hourly for the verifier and for Milky Way's HTTP CDP.
- Inbound only from its requester (the Windows one: enrolment from the domain). Administration is from the console or the admin network.
- Full CA audit, and the requester's audit line per issue. The monitoring module checks the service, that the CRL's next update is more than 2 days away,
  and that the CA certificate is more than 1 year from expiry. CMB counts issued per profile or template per day.
- Valid 10 years, renewed with a new key at 5.
- Placement is a parameter. The security ones sit next to Doppler in the DMZ or a segment of their own, never on the same host as the
  operational ones.

**Directory writes.** Only the Windows issuing CA writes Active Directory. Its install writes the forest's PKI container and needs
Enterprise Admin, so it is **gated**. It goes into NTAuth only if the inventory shows certificate logon to AD (smart card, PKINIT, NPS
account mapping). Publishing the security root into Windows trust stores (mutual trust) is a write to real managed devices, so it is
gated too.

**Why the CMOS ones are separate from the native ones.** The CMOS issuing CAs' EKU limit means that even a compromised CMOS issuing CA
cannot mint a certificate that logs on to AD, signs code or impersonates a switch. The native CAs need those broader purposes. The two
CMOS issuing CAs are twins because of section 1a: the same profiles, a different root, a different requester and a different platform.

## 7. Profiles and templates

**CMOS issuing CAs (profiles in code; Doppler's as built in `CM.BlackHole/src/Doppler`, the operations master's the same table).** Every leaf is requested by its chain's master and checked
against a profile before it is submitted. The subject of a device is the opaque id that Doppler assigns, never a host name or a person.
The key algorithms marked * follow section 5.

| Profile | EKUs on the leaf | Key | Lifetime | Subject and SAN |
|---|---|---|---|---|
| Device | role Device, client authentication | ECDSA P-256* in the device TPM, non-exportable | 1 year, renewed at 8 months | `CN=<opaque id>`, no DNS SAN |
| Server | role Server, server authentication | ECDSA P-256* (RSA 3072 where a product needs it) | 1 year | DNS SANs from the server's settings |
| ImTokenSigner | role ImTokenSigner only | ECDSA P-256, non-exportable on Doppler's virtual TPM | **90 days**, renewed at 60 | `CN=Doppler token authority <n>` |
| MilkyWayGate | role MilkyWayGate, server and client authentication | ECDSA P-256* | 1 year | the gate's DNS names (settings) |
| DopplerServer | role DopplerServer, server and client authentication | ECDSA P-256* | 1 year | Doppler's DNS names (settings) |

Each chain has its own token signer: Doppler's on the security chain, the operations master's on the operational chain. **Both token signers are
ECDSA P-256** (agreed with the Milky Way lane, 2026-10-25): IM verifies one token algorithm. The algorithm diversity of Q4 is at the CA
level only, where the chain engine verifies either family. The token signer is short-lived on purpose: its key is the most valuable
online key in CMOS. Revocation through the CRL alone could take up to 9 days, so the verifier also holds the current signer KeyIds that
Doppler announces, and a removed KeyId stops verifying at once.

**Windows issuing CA (AD CS templates).** One new template per template of the old issuing CA that the inventory finds in use, with the
key algorithm from section 5. The adjustments of today's templates (WMI encryption, auto-enrolment through group policy) are carried over
deliberately, not copied blindly. Each template has a row in the inventory: who uses it, and the switch-over test.

## 8. How test laptops get the chain

1. **For IM (no OS change):** both new roots' certificates (public) are pinned in the Doppler agent's configuration, shipped in its package
   and checked by its hash at start. `Im.Trust` uses custom root trust, so nothing is added to the laptop's certificate store.
2. **The device certificates:** the agent creates its key in the TPM. Doppler attests the device and returns the security leaf over IM; the operations master certifies the same key for the operational leaf once the device is in its inventory on a plan.
3. **For the OS (phase 2, gated):** an Intune trusted-certificate profile puts the operational root into the machine stores of a **test group**
   first, next to the old root. That is a tenant write that reaches devices, so the owner's words come first.
4. **The two home test laptops** get the agent from a local install by an admin. No tenant write is needed for that.

## 9. The old CA during the parallel run

**An audit boot of the old CA** (the owner, 08:02 the same day). The old root is started **once**, to see what it is and to inventory
it for the audit record. It is not started for anything the migration needs. The audit boot is **gated** (anything on the old CA) and
runs only on the owner's words:

- a **copy** of the old root's stored replica is started, never the replica itself ("do not delete or start" stays true for the
  replica);
- the copy runs on the ceremony host with **no network adapter**;
- it is read only: the CA configuration, key algorithm and length, validity, the CRL settings and the last CRL, the issued certificates
  (`certutil -view`, which should be the old issuing CA's certificate and little else), and the audit log if present;
- nothing is signed, published or renewed;
- the copy is wiped afterwards, and the findings go into the inventory (section 3) and an audit record.

The old issuing CA is inventoried the same way, read only, while it keeps running (Q3, agreed).

**Apart from the audit boot, the old root is never booted.** No step needs the old root's key:

- the old chain is retired by removing the old root from the trust stores, not by revoking the old issuing CA;
- the old root's CRL is valid for 120 months, so it does not expire during the run.

Phase 0 reads the old root CRL's next-update date from its published copy. If that date fell inside the parallel run, which is not
expected, the plan would change before phase 1. The replica of the old root in the backup system stays as it is ("do not delete or
start") until phase 4.

**The old issuing CA keeps running** until phase 4. It publishes its own CRLs, so the old chain keeps working for everything that has
not moved yet.

Until phase 4, none of these happens:

- no CRL is re-signed or extended on the old root;
- no renewal, and no template is deleted (unpublishing in phase 3 is a step of that phase, with the owner's OK);
- no cross-certification between the old and the new root, in either direction;
- CMOS components never trust the old root: the verifier refuses its chains (`cm.os` test "a leaf of another authority is refused as
  untrusted root").

**Phase 4, the removal (gated, each step):**

1. Remove the old root from AD (`certutil -dspublish` objects, NTAuth if present), from the group policies, from the Intune profiles and
   from the network gear's trust stores: test group first, then everyone.
2. On the old issuing CA, publish a final CRL, then remove the CA role and its AD objects.
3. Destroy the old root's stored copies, which destroys its key.
4. Delete the old auto-enrolment policy once nothing refers to it.

## 10. The build, in order (completion goals)

| # | Step | Who | Gated |
|---|---|---|---|
| 1 | The owner answers Q1 to Q3 (or the recommendations stand) | owner | no |
| 2 | Phase 0: the inventory connectors and the operations master inventory page; the algorithm decision written | The operations master threads | reads only; Q3 for the old issuing CA's list |
| 3 | The ceremony host prepared; the operational root ceremony (`issuing-ca.ps1 -Stage Root` prints the steps) and the security root ceremony (Linux tooling, after Q5); each image stored in the three places | owner | **yes** |
| 4 | The CMOS issuing CA: `-Stage Vm`, `-Stage Role`, the ceremony signs its request, `-Stage Install` | owner for the VM until the credential store holds a cluster credential, then the lane | **yes** for the VM and the ceremony |
| 5 | The Windows issuing CA: the same stages as an enterprise subordinate (a script variant, wave 2). The security root and its two issuing CAs on Linux (tooling after Q5) | owner | **yes** (forest write, VMs, ceremony) |
| 6 | Acceptance for CMOS: a sandbox device's certificate is accepted by the verifier; a certificate of the old CA is refused; a chain validates on a test laptop and on the Milky Way host; the IM token signature uses a key the new CA issued | lane, owner for the laptop | the laptop install is local |
| 7 | Phases 2 to 4, class by class, from the inventory | The operations master threads, the owner per class | per phase (section 2) |

The script is [tools/blackhole/issuing-ca.ps1](../../tools/blackhole/issuing-ca.ps1). It is a read-only Check by default, prints WOULD
lines in the dry run, acts only with `-Apply`, and is idempotent per stage. It takes every place and the algorithm as parameters and holds
no value of the operating organisation.

## 11. If something is compromised

| What | Response |
|---|---|
| The token signer key | Doppler stops announcing its KeyId, so the verifier refuses it at once. Doppler revokes it and issues a new signer; device tokens are reissued on their next renewal |
| A device key | revoke the leaf and the enrolment (`IEnrolmentRegistry` says revoked): refused at once, and the CRL follows |
| An issuing CA | a root ceremony revokes the issuing CA's certificate. A new issuing CA comes from the script, and its consumers re-enrol: devices by attestation, the domain by auto-enrolment |
| A root | a new root for that chain only, a new pin in the agent package, that chain reissued; the other chain still holds, so nothing is admitted on the broken chain's word alone; the cold-start vault holds the procedure |
| A copy of the root image | nothing is exposed without the BitLocker password, which lives only in the vault. Record it, and rotate the password at the next ceremony |

## 12. Questions for the owner (recommendations)

- **Q1 (agreed on the intent, 2026-10-25 07:00):** two roots, the double rule for everything that speaks IM, and each native world on its
  own chain. Windows gear is on the operational chain. Network gear and everything that is not Windows are on the security chain. The
  two worlds trust each other mutually. This gives four issuing CAs (section 6). The forest writes and the trust deployments stay gated
  until each comes up.
- **Q2 (agreed 2026-10-25 08:02; storage part replaced by the owner at 10:01Z, see [coldstart/data-boundary.md](coldstart/data-boundary.md)):** each root is a NIC-less VM with a password-only encrypted disk, run only on a ceremony host that is not a cluster node. **The root key material exists only as certificates (public) and on the White Keys; there is no image or key backup on the backup server, at the DR site or in the vault.** The VM is rebuilt from the repository at each ceremony and the key is brought from the White Keys. **Minting ceremony (owner, 10:03Z):** create the two CAs and their roots and the configuration, isolate completely, shut down, encrypt, store verified identical copies on all White Keys; CMOS enters productive mode only after it has verified that state on every key. Open: whether the identical copy opens with one key or needs three of five (see data-boundary.md).
- **Q3 (agreed, 2026-10-25 08:02, read as):** inventory the old CA for the audit record. That means a read-only `certutil -view` of
  the old issuing CA and one gated audit boot of a NIC-less copy of the old root (section 9).
- **Q4 (agreed, 2026-10-25):** different algorithm families for the two roots. Which root gets which family follows the inventory
  (section 5).
- **Q5.** The CA software for the security chain on Linux. AD CS is Windows only. Recommendation, to confirm with a sandbox spike before
  anything is built:
  - an established open-source CA engine for the two security issuing CAs, with Doppler in front as the only requester (candidates:
    Smallstep `step-ca`, EJBCA Community);
  - OpenSSL for the offline security root, because a root only signs a few times a year and the smallest well-understood tool is the
    safest.

  Not the firewall's built-in CA: the firewall stays a firewall, and the security master's keys do not belong on the edge device.
- **Q7 (agreed, 2026-10-25 08:02):** the White Key rule covers **permanent** writes only. One White Key may isolate, lift, override,
  unlock the vault and restore approved state. Once a system is armed, permanent writes need a majority of its White Keys (section 1b).
- **Q8.** The owner's idea: several Linux CA products, each Doppler instance picking one at random. **Recommended: no random pick.**
  Run one product, and keep a second one qualified as a warm standby. Why:
  - every issuing CA under the security root can mint certificates that everything trusts, so the chain is only as strong as its
    weakest product, and three products give an attacker three ways in;
  - each product would also need its own Supernova level 3.

  The standby gives the diversity benefit without that cost: a critical flaw in the first product is answered by switching, not by
  hoping the attacker picked the other one.
- **Q9.** Dev and nightly use an ephemeral stage CA, a root of its own that the stable masters vouch for with a signed record, never a
  sub-CA under the stable chain (section 1c). Recommended: yes.
- **Q10 (agreed, 2026-10-25 09:53: five keys).** Three White Keys stay the minimum the arming rule accepts, a yearly challenge, and an odd count that may grow. The owner agreed to **five** at arming: the two sysadmins and three members of the C-suite. A majority is three, and five survive two losses. With both sysadmins gone, the three C-suite keys are still a majority (catalogue CS-03). The C-suite holders have to clear the same challenge, so the assistant must carry a non-technical holder.
- **Q6 (agreed, 2026-10-25):** an isolation starts only on evidence (a refused signature, a broken chain, an unknown token, a White Key
  order), never on silence alone. It is switched on in three steps: alarms only, then the sandbox, then production on the owner's
  words (section 1b). In a split brain, the White Key holders pick the winning side or override.

Replaced by the owner's direction (2026-10-25): the earlier Q2 (root as a powered-off VM on the cluster) and Q3 (RSA 4096 CAs and P-256
leaves as fixed defaults).

## 13. Open, for later waves

- The dual handshake (section 1a): the dual verdict in `Im.Trust`, both chains and both tokens in IM, both registers at the gate,
  the operations master's token signer and register, the pair test with an operations master stand-in.
- TPM key attestation (EK certificate chains, then vPro) replaces the sandbox attestation.
- The JEA issue endpoint, and the HTTP CDP on Milky Way.
- The power-on job in the operations master, and the ceremony rehearsed in the simulator.
- The enterprise variant of the script, and the Linux ceremony and install tooling for the security root and its two issuing CAs (after
  Q5).
- Isolation (section 1b): the triggers in Doppler and the operations master, the actions at the gate, Event Horizon, the firewall and the management
  paths, the White Key statements (isolate, lift, pick a winner, override) and their verification, the would-isolate alarms first. It is also a DMP 06 section.
- Doppler's server on Linux: the server code is already plain .NET 10 with no Windows API. Only the agent, which runs on the Windows
  devices, is Windows-only.
- Network access control with Device certificates (lane L7, part 2).
- An HSM for the issuing CA keys.
- Arming (section 1b): the White Key registration in both masters, the holder challenge, majority statements (multi-signature orders in
  IM v1), the stable-entry check in the deployment pipeline.
- The stage CA (section 1c): creation and vouching by the stable masters at deployment, the throw-away at teardown.
- The audit boot of the old root (section 9), on the owner's words.
- The yearly ceremony, key validity of two years and the stale-key succession: [coldstart/ceremony-and-staleness.md](coldstart/ceremony-and-staleness.md) (proposal). The cold-start catalogue, assistant and bundle that arming and the stable gate rest on: [coldstart/](coldstart/catalogue.md), constitution C24.

Status: draft (Black Hole lane, 2026-10-25; the owner's direction, including the two chains of trust, the native platforms, isolation, the White Key, arming and the stage CAs, folded in the same day).
