# The White Key liveness proof and the stale-key succession (proposal)

Security context: this brief is defensive security engineering under the charter in `CLAUDE.md`. It describes how the operating organisation's own data is
kept safe, and kept reachable, if the people who oversee CMOS are gone for good, and how CMOS defends itself against being tricked into thinking so.

This is a proposal from the owner's messages of 2026-10-25 09:36Z, 09:37Z and 09:42Z. The first message was relayed with the opposite meaning (a
destruction path) and the owner corrected it; this text follows the correction and the later decisions. It stays draft until the owner answers the questions at the end.

## What the owner wants

1. **Proofs of life.** White Key holders prove liveness to the system **once a month** (the procedure), and once a year all of them sign the system's challenge at the **ceremony**. There is no lower bound
   on how often liveness can be proven. Every system a holder has access to keeps asking the holder, all day, until the proof is made.
2. **A missed proof is not a disaster.** If the yearly ceremony does not take place, the White Keys stay valid for one more year and then expire.
3. **Any sign of life ends a stale state at once.** Any live key, any activity, disproves staleness, and a stale state is then revoked immediately.
4. **Expiry opens a way forward.** The highest authority of the operating organisation can take CMOS offline, put it into nightly mode and redeploy it with a **fresh set of White Keys**. New
   information warehouses (Sagittarius, Crater, the knowledge warehouse) are deployed **on top of the old ones** and import their data: "it's basically a cold start, while rotating out the White Keys".
5. **Only a proven stale state releases the locks.** The encryption that locks the data in the warehouses dissolves **only** if it is proven that the White Keys went stale. Nothing is released
   on a claim by CMOS or Doppler, because they can be outsmarted; hence the year.
6. **A staleness certificate.** When everything enters the stale state, the operations master and Doppler add a staleness certificate on top and sign everything with it from then on. If the stale state turns out to
   be false, everything signed under it can be deactivated instantly, so nothing built during a false stale state can be misused.
7. **Once the old logs are dissolved they are destroyed**, and the fresh warehouses start from the imported data.
8. **Time comes from two national authorities**, NIST and PTB Braunschweig, each from two independent servers, for all involved systems.

## The one idea that carries the design

**Release is hard, veto is easy.** Everything that can release the locks needs many independent facts to agree, a long wait and a last call; anything that
shows the oversight is alive stops it with one signature and kills what was built under the claim. If an attacker gets any part wrong, the data stays locked. If the people are alive,
one of them can always stop it.

```
 monthly liveness proof ─┐       yearly ceremony (a majority signs; keys renewed)
 any White Key signature ┴─> VETO at any moment: stale count restarts, request void, staleness certificate revoked
      │
      ▼  no signature of any kind, counted by each warehouse's own accepted time (four time sources)
   1 year ──> overdue: alarms to every holder, the executive and the DPO; the masters MAY lodge a stale request
      │         (ordinary operation continues, nothing is marked)
   2 years total ──> keys expire; the warehouse's own count says "stale"
      ▼
   last call (30 days, a setting): announced on every channel; any White Key signature still vetoes
      ▼
   STALE PROVEN: both masters add the staleness certificate and sign everything with it from now on
      ▼
   release: both masters' co-signed confirmation bound to the lodged request + the succession authority names the new deployment's keys
      ▼
   fresh warehouses deployed on top, data imported, locks re-wrapped to the new deployment's keys only; announced; logged
      ▼
   cooling period (30 days, a setting): a sign of life still voids the episode and the old deployment resumes intact
      ▼
   old wraps retired and old logs destroyed (irreversible, last)
```

## Rules (each one answers a way to be tricked)

**R1 · Time is counted by the warehouse itself, from four sources.** The time that counts is the warehouse's own accepted time, never a time CMOS or Doppler claims. All involved systems take their
time from NIST and PTB Braunschweig, **two independent servers from each**. Accepted time moves only when both servers of each authority agree with each other and with the other authority within a tolerance, and
it never moves backwards. If one authority is unreachable or the four disagree, time does not advance (fail safe: the data stays locked). A TPM monotonic counter makes the accepted time a ratchet that
cannot be rolled back by a restore. A TPM counter counts events, not time, and the TPM clock does not run while the machine is off, so the counter protects against rollback and the four sources measure the year.
Two things to check before this is built (I cannot verify them from here): whether each authority offers *authenticated* time (NTS) on the servers to be used, since four unauthenticated answers
are four spoofable answers for anyone on the path; and that the warehouses may reach the sources outbound (an allowed exception at the Event Horizon). Moving **all** systems' time to these sources changes the domain time hierarchy, which is a production change and therefore a gated step of its own.

**R2 · Any live activity disproves staleness, immediately.** Anything signed by a valid White Key resets the staleness count of every warehouse that sees it: the monthly proof, the ceremony, a cold-start declaration, an order, a vault open. A
signature over a fresh nonce issued by the warehouse cannot be replayed. A veto only prolongs and revokes; it never releases. A veto during an episode revokes the staleness certificate (R7).

**R3 · Monthly proof, yearly ceremony.** The proof is a *life sign*: one key signs a fresh challenge, cheap, no majority; it resets the veto clock and renews nothing. The ceremony is the full yearly act: a majority (recommended: all
reachable holders) signs, and each signer's certificate is renewed to two years after the ceremony; a holder who did not sign is not renewed and shows as expiring a year ahead. A system kept alive by life signs of one key alone raises a yearly "majority lost" alarm to the executive and the DPO.
The prompts: every system a holder can use shows the open proof until it is made. Two dangers of constant prompts, and the answers: (a) they train holders to sign whatever pops up, so a life sign is **domain-separated** (its bytes carry a context that can never be read as an order), is
shown on the token's own display, and the prompt itself never carries the bytes to sign; (b) a prompt that asks for a PIN is what a phishing page imitates, so the prompt appears only in the operations master console and the assistant, never in mail or chat.

**R4 · The request is lodged, recorded and bound.** After more than one year without a signature the two masters may lodge a stale request. The warehouse stores it, announces it, and accepts a release only if the confirmation names the hash of that request and is
signed by **both** authorities (C19: the secure one and the meant one). The binding is to the **request record and the long-lived authority identity of each master, not to a signer key**: token signers rotate every 90 days (ca-plan 7), so "the very same key" cannot be the same key a year later. The warehouse checks both
signatures itself; a carrier's check does not count.

**R5 · The year and the last call are the defence against being outsmarted.** Even with both masters and the network under an attacker's control, they must (a) fake or suppress time at the warehouse for more than a year against four
sources of two authorities, (b) suppress every holder's signature for that year, and (c) survive a 30-day announced last call during which one signature from anywhere stops it. At that point the attacker already holds the operating organisation.

**R6 · The new deployment is named by an authority that is neither master.** A release that hands the data to "a fresh deployment" is only as safe as the way the warehouse learns which deployment is rightful. If CMOS or Doppler named it, a tricked master could name an
attacker's. So the warehouse takes the successor from a **succession authority registered at arming**: an m-of-n of the operating organisation's leadership as hardware keys (recommended: the executive, a deputy and the DPO, two of three), signing the public keys of the **new White Key set**. This is the "current highest authority" made concrete and is
a second class of key next to the White Keys. With five White Keys, three of them held by the C-suite, the same people may hold both kinds; the keys stay separate, because a White Key proves liveness and a succession key names a successor. Open question 2.

**R7 · The staleness certificate.** When stale is **proven** (the last call is over with no signature), each master issues itself an episode certificate and from then on countersigns everything it signs with it; every object made during the episode (tokens, orders, the deployment, the imports, the re-wraps) carries both marks. Deactivation must be instant, and a CRL can take days,
so a marked object is valid only while the verifier holds a fresh, short-lived **episode-alive statement** (a lease of minutes); a verifier that cannot get one refuses marked objects (fail closed). A White Key signature publishes **episode void**, which is permanent for that episode id: every marked object dies at its next check, and the
old deployment, whose wraps and logs are still intact (R8), resumes. Ordinary operation during the overdue year is **not** marked: a false alarm must not disturb the operating organisation; the mark starts at the proof. Open question 3.

**R8 · Re-wrap, retire, destroy: in that order, the irreversible step last.** The warehouses are redeployed on top of the old ones and the data is imported into them; the data keys are re-wrapped to the new deployment's keys only. The old wraps and the old logs stay **untouched through the cooling period**; only after it, with no sign of life, are the old wraps retired
and the old logs destroyed (the owner: "once the logs are dissolved, they're destroyed"), and the new deployment re-encrypts under fresh data keys as a background job. The destruction of an audit trail conflicts with the draft in DMP 14 that no holder may delete it and with legal retention of business records
(the DPO and counsel to confirm that logs that are business records are covered, or kept sealed with the DPO); this is the owner's decision and the draft records it. The release is also a read of personal data: the DPO is informed.

**R9 · A blocked ceremony has a fallback, and the fallback is never silence.** Holders may be ill, a token broken, a site shut. The ceremony has a window (recommended: 30 days either side, a setting) because a fixed day is a date an attacker can plan around. Fallbacks, in order: a ceremony at another holder's location; a life sign by any single key; a ceremony that adds a replacement
key by a majority; the executive's escalation after the overdue alert. A declared incident or cold start pauses nothing by itself: it is a White Key's signature, which is already a veto (R2), so a disaster cannot cause the stale state by making people miss the date.

**R10 · This is never the business recovery path.** The stale path takes at least a year by design. Any scenario of the [catalogue](catalogue.md) that could only be recovered by waiting for it has failed its one-week pass line. CS-03 (both admins gone) must be recovered by the executive's White Key within the week; the stale path is what is left if even that has
been lost for good. A sandbox test shows the two cannot be confused: a cold start that spans the proof date does not become a stale state.

**R11 · What is locked, and the backups.** The warehouses hold only information CMOS generates for itself; the operating organisation's files and the databases of the in-house systems are not CMOS, so they are neither locked by this nor touched by the release: what the new deployment re-takes is the **access lock** CMOS puts on them, not the data ([data-boundary.md](data-boundary.md)). The backups of CMOS information are sealed archives ([sealed-backup.md](sealed-backup.md)); the release re-wraps their data keys to the new deployment (small headers, payloads untouched); a backup product's own encryption is outside the release.

## What it changes in other documents

- **ca-plan 1b**: the "yearly challenge" there becomes this ceremony plus the monthly proof; White Key certificates are valid two years from the last signature.
- **DMP 04, IM version 1**: new order kinds: *life sign*, *stale request*, *release confirmation*, *episode void*, plus the episode-alive statement. Their safety is time, veto and the two co-signatures, not a White Key threshold, so they are a different class from the kinds in PR #78. Owner: the Milky Way coordinator.
- **C19 (warehouse dual signature)**: the release confirmation is a C19-style dual-signed request bound to the lodged record. The warehouses need the counter, the four time sources, the veto check and the lease check.
- **Gravity**: "release implies no valid White Key signature for the full period as counted by the warehouse, a co-signed request bound to the lodged hash, and the succession authority's approval; and a signature after release and before the cooling period ends restores the old state" are laws of the kind Gravity proves (the owner: "if we can prove this is the only way it can happen"). Candidate for the second Gravity slice.

## How it is tested

Time is injected (TD1), so the whole year runs in seconds in the sandbox and in the simulator. Each line must fail to release or must hold:

| Attempt | Expected |
|---|---|
| Both masters sign a release with no lodged request, or one lodged before one year | refused |
| A request replayed after a veto, or bound to an older request's hash | refused |
| Time: one source lies forward; two servers of one authority disagree; one authority unreachable; the authorities disagree; a restore rolls the counter back | accepted time does not advance, refused |
| Time jumps by more than the step limit, or faster than the rate limit | alarm, not accepted |
| One valid White Key signature at any moment: in the year, the last call or the cooling period | count resets, request void, staleness certificate revoked |
| A replayed old White Key signature, or a life sign read as an order | refused (fresh nonce, context separated) |
| A signer key rotated since the request was lodged | still binds (authority identity, not key) |
| Release approved by the masters but not by the succession authority, or a successor the authority did not name | refused |
| A marked object when the episode-alive statement is missing or expired | refused (fail closed) |
| After `episode void`: every marked object, the new deployment's wraps, the imports | dead at the next check; the old deployment resumes with its logs |
| After a true release and the cooling period: only the successor's keys open the data; the old wraps and logs are gone | pass |
| A cold start that spans the proof date | no stale state |
| A true stale state from start to finish, then a fresh deployment adopting all three warehouses | pass (runs as a cold start with key rotation in the catalogue) |

The GRB run for this component is the first aimed at "make CMOS believe the people are gone". The warehouse's release check is Supernova level 4 (it decides what leaves the lock), which is why it must not trust the masters.

## Flaws found while drafting, and what the draft does about them

1. **A TPM counter is not a clock.** Used as a ratchet only (R1).
2. **Four time answers are only as good as their authentication.** Unauthenticated NTP can be spoofed by anyone on the path; whether NIST and PTB offer authenticated time on the chosen servers must be checked (R1).
3. **Slow-walked time.** A step limit stops a jump, not a fast drift; the rate limit, four sources of two authorities, the year, the last call and the cooling period are layered because none alone is enough.
4. **"The very same key" cannot be one key.** Token signers rotate every 90 days (R4).
5. **Who is the successor.** Without a source other than the masters, a tricked release hands the data to whoever they name (R6).
6. **The veto is easy for a thief too.** A stolen White Key can postpone succession for good. A nuisance, not a breach, and visible through the "majority lost" alarm; the owner may prefer a life sign to need two keys.
7. **Marking the overdue year would punish a false alarm.** Hence the mark starts at the proof, not at the lodging (R7); a CRL is too slow to deactivate, hence the lease (R7).
8. **Irreversible steps.** Retiring the old wraps and destroying the logs is the one thing that cannot be undone, so it comes last, after a cooling period in which a sign of life still reverses everything (R8).
9. **Constant prompts train blind signing.** A life sign is domain-separated and displayed on the token (R3).
10. **Disaster causing the stale state.** The scenarios where people cannot reach the date are exactly those in the catalogue; R9 and R10 keep them apart.
11. **Mixed state.** Three warehouses count separately and may reach the stale state on different days. Each releases alone and the new deployment adopts them one by one.
12. **Destroying an audit trail** conflicts with a DMP 14 draft and with record-keeping duties (R8).

## Questions for the owner (recommendations)

- **Q1 · The dates.** Is it: proof monthly, ceremony yearly, lodging allowed after one year without any signature, keys expire after two, release only after expiry and a 30-day last call, then a 30-day cooling period before anything is destroyed? Recommended: yes, one setting block.
- **Q2 · The succession authority.** Who may name the fresh deployment? Recommended: two of three hardware keys held by the executive, a deputy and the DPO, registered at arming, separate from the White Keys.
- **Q3 · When does the staleness certificate start?** Recommended: at the proof (after the last call), not at the lodging, so a false alarm does not mark a year of normal operation.
- **Q4 · The cooling period.** Recommended: logs and old wraps are destroyed only after 30 days of the new deployment with no sign of life.
- **Q5 · Veto strength.** Recommended: one key for a veto (the safe direction).
- **Q6 · Time sources.** Recommended: check NTS on the four servers first; if one authority offers none, say so and decide whether its pair is a cross-check only.

Status: draft
