# Event Horizon: network inspection for CMOS-managed devices (design, draft)

Security context: this brief is **defensive** security engineering under the charter in `CLAUDE.md`. It designs inspection of
**the operating organisation's own managed devices and own network**, by its own IT, authorised by the IT owner. It never targets a third
party, never evades anyone else's detection, never harvests credentials. Everything here is built and proven on **synthetic
data in the sandbox** first. Nothing in it touches a real device, a real DNS server, a real switch or the tenant until the
owner's own words and the data-protection-officer's sign-off are in the conversation (section 8). The whole thing is a design
plus a sandboxed engine; the parts that reach the real world are listed as gated and are not built here.

Owner (code): Black Hole coordinator. Status: draft, reverified 2026-10-25 (EH2, GRB-0002 run R1).

## 0. What the owner asked for, and the honest frame

"We are going to implement deep packet inspection sooner or later anyway, so let's find a good way to do it now and build in
everything we can." And: "We control the entire infrastructure. We can change the settings of individual clients. We can spin
up new DNS servers and point the clients only at those. We can sandbox whatever we want."

That is true, and the design below assumes it: **fully composable infrastructure**. Per-device settings over Intune, CMOS DNS
servers the managed clients are pointed at exclusively, any component runnable in a sandbox. So the design is not limited by
what exists today. It is limited by exactly two things, and this note states both plainly and designs everything else freely:

1. **The parts we do not control: Microsoft's own service endpoints and protocols, and Microsoft's support stance.**
   Microsoft documents that a set of its service endpoints must **not** be TLS-decrypted or deep-inspected, and that doing so
   is unsupported and known to break the service (section 2). We cannot change that by controlling our own network. So those
   endpoints are a **hard bypass**: never inspected, by rule, enforced in code and tested.
2. **The law, the data-protection officer and (if one is ever elected) a works council.** Inspecting what staff send over the
   network is "a technical device suited to monitor behaviour". That is the real gate, not a technical one (section 8). The
   design is built so that this gate controls a single switch, and so that the most privacy-sensitive traffic is bypassed by
   default even when the switch is on.

The thing worth building now, under those two limits, is **not** "read everyone's traffic". It is a **sandboxed decision and
classification layer**: the component that decides, per connection, *whether a connection may be inspected at all*, that holds
the bypass lists as first-class data, that emits **counts, never content** by default, and that is proven to refuse to look at
what it must not look at. That component is useful on day one (it is the policy brain of any future proxy, and of the DNS and
firewall steering we do control), it is safe to build and prove in full against synthetic data, and it is the piece that the
DPO and the owner actually need to see before anything real is switched on. The data plane that moves real bytes is a later,
gated carriage that *asks* this brain and holds no policy of its own.

## 1. The shape, in one picture

```
   managed device (Intune-configured)                    CMOS segment (sandbox today)
 ┌──────────────────────────────┐                     ┌───────────────────────────────────────────────┐
 │ CMOS DNS client ─────────────┼── names ──────────▶ │ CMOS resolver (we control; wave 2, gated)       │
 │   points only at CMOS DNS     │                     │   answers, and tags each name with a category   │
 │ egress ──────────────────────┼── connections ────▶ │ Event Horizon data plane (LATER, gated):         │
 └──────────────────────────────┘                     │   a forward path that, per connection, asks ──┐  │
                                                       │                                               │  │
                                                       │   ┌───────────────────────────────────────┐  │  │
                                                       │   │ Event Horizon DECISION LAYER (built     │◀─┘  │
                                                       │   │ here, sandbox): InspectionPolicy        │     │
                                                       │   │  • bypass lists (hard + sensitive)      │     │
                                                       │   │  • verdict: Bypass / InspectMetadata /  │     │
                                                       │   │    InspectContent (+ reason)            │     │
                                                       │   │  • counts-only telemetry (CMB)          │     │
                                                       │   │  • sealed findings, no content          │     │
                                                       │   └───────────────────────────────────────┘     │
                                                       │   separate inspection CA (gated, section 5)      │
                                                       └───────────────────────────────────────────────┘
```

- Built in this pull request, fully on the sandbox: the **decision layer** (`InspectionPolicy`, the bypass lists, the verdict
  type, the counts-only telemetry, the sealed finding record) and a proof harness that drives it with synthetic connections.
- **Not** built here, each a gated carriage with its own later brief: the CMOS resolver, the forward data plane that actually
  terminates or relays a connection, the inspection CA material, the Intune profiles. They are designed below so the shape is
  agreed, but no code that moves a real byte or trusts a real device is written in this pull request.

## 2. The hard bypass: what is never inspected, by rule (the part we do not control)

Microsoft's current network-connectivity guidance (fetched 2026-10-25, see Sources) is explicit:

> "Bypass Microsoft 365 domains from TLS decryption, traffic interception, deep packet inspection, and network packet and
> content filtering."

and lists, under "network scenarios ... known to cause connectivity issues":

> "TLS termination or deep packet inspection of any Microsoft 365 domains ... Forcing downgrade or failover of protocols (such
> as ... TLS1.3 → TLS1.2 ...)."

Intune's and Defender's endpoints are called out the same way: SSL inspection is **not supported** on the management
endpoints, and Defender for Endpoint "cannot function in an environment where SSL inspection is done on the traffic"
(certificate pinning rejects the interception certificate). These are not endpoints we can inspect by controlling our own
network: the clients pin, and Microsoft withdraws support. So:

**Rule EH-BYPASS-1 (hard bypass).** A connection whose destination is a Microsoft-managed endpoint is **never** inspected:
every **required** endpoint of the Microsoft 365 web service, in **all three** categories (Optimize, Allow **and Default**: the
current guidance applies "for all Required Microsoft 365 network endpoints (domains) and categories", and lists "TLS termination or
deep packet inspection of **any** Microsoft 365 domains" as known to break the service), plus the Intune, Defender for Endpoint,
Autopilot, Microsoft Store, Endpoint Privilege Management and attestation sets that Microsoft publishes outside that web service.
The policy returns `Bypass` with reason `MicrosoftManaged` and the data plane passes it through untouched (no TLS termination, no
protocol downgrade). This is enforced in code and has a test that fails if any such destination ever yields anything but `Bypass`.

*Corrected 2026-10-25 by the EH2 reverification (GRB-0002 run R1, finding F2):* the first draft bypassed only Optimize and Allow and
called Default "eligible for inspection". That matched Microsoft's older category text, not the connectivity guidance in force
(fetched 2026-10-25, page dated 2026-08-26). What stays eligible for inspection, once the gate is open, is traffic to destinations
that are **not** Microsoft-managed at all.

**Where the list comes from, and how it stays current.** Three sources, all data, none hard-coded:

1. The Microsoft 365 **Endpoints web service** (`https://endpoints.office.com/endpoints/Worldwide`) publishes the required domains and
   IP ranges as JSON, each tagged `category: Optimize | Allow | Default` and `required: true|false`, with a `version` method to poll (at
   most hourly) and a `changes` method; Microsoft gives 30 days' notice of additions. The CMOS job (the operations master/the monitoring module side, later) polls the
   version method and, on a change, writes **every `required` endpoint of every category** (domains **and** IP ranges) into the
   `BypassSet` setting.
2. The **Intune** network-endpoints page, whose consolidated FQDN and subnet list is the only correct source for Intune: Microsoft's own
   caution on that page says the Office 365 endpoint service "is insufficient and may lead to incorrect configurations" for Intune.
   The same page carries the Autopilot, Store API, Endpoint Privilege Management and Azure Attestation (MAA) endpoints, each marked
   "SSL inspection isn't supported".
3. The **Defender for Endpoint** connectivity pages: `*.endpoint.security.microsoft.com` (streamlined) and the standard URL list,
   with "Don't scan, inspect, intercept, or use a man-in-the-middle (MITM) proxy for this traffic."

Event Horizon loads the `BypassSet` and treats every entry as EH-BYPASS-1. The list is **data, a setting**, never hard-coded (it
changes monthly) — only the *rule* is in code. A malformed entry (a typo, a URL, a CIDR in the domain list, a non-ASCII letter) makes
the update **fail loudly** (`DomainSet` throws), so a Microsoft endpoint can never silently lose its bypass (GRB-0002 finding B4).
Until the job exists, a few real Microsoft wildcard patterns seed the sandbox `BypassSet` in the tests (public Microsoft names, no
company value, C9); the real list lives in the settings store.

**What the facts must be bound to (GRB-0002 finding F1, for the data-plane brief).** `ConnectionFacts` is a destination name and a
category. The name the policy sees must be the one the **CMOS resolver answered**, bound to the address the device then connects to,
never the client's TLS server name (SNI) alone: a client that could pick its own SNI would pick a Microsoft name and escape inspection.
The matcher also refuses any name that is not plain ASCII (a KELVIN SIGN folds to `k` under invariant lower-casing and matched the
first draft; GRB-0002 finding B2). Optimize traffic that goes to **IP ranges without a name** (Teams media) needs the IP half of the
`BypassSet`, which the decision layer does not model yet: the data-plane brief adds the destination address to the facts and an IP-range
set next to `DomainSet`, so that such a connection is `Bypass(MicrosoftManaged)` and not `Metadata(PinnedUncategorised)`.

## 3. The sensitive bypass: what we choose never to inspect (privacy by design)

Even with the governance switch on, the design bypasses whole classes of destination because inspecting them is
disproportionate and, for some, independently unlawful to intercept:

**Rule EH-BYPASS-2 (sensitive bypass).** A connection whose destination category is in the **sensitive set** returns `Bypass`
with reason `SensitiveCategory`. The sensitive set is a CMOS setting, default on, and includes at least:

| Category | Why bypassed |
|---|---|
| `banking-finance` | financial-secrecy expectation; disproportionate; no security need |
| `health-medical` | special-category data (Art. 9 GDPR); never intercepted |
| `government-tax` | citizen-to-state; disproportionate |
| `personal-webmail` | private correspondence; the strongest confidentiality expectation |
| `legal-counsel` | privileged |
| `works-council` *(reserved)* | if a council is ever elected, its channel is bypassed so the tool can never watch it |

The category of a destination comes from the CMOS resolver's tag (we control the names the managed clients resolve) and from a
category feed; a destination with **no** category is treated as `unknown` and, by rule EH-DEFAULT below, is **not**
content-inspected until it has been categorised and the category is outside the sensitive set. Fail-closed: unknown means do
not look at content.

A category is a **tag with a grammar** (`CategoryTag`: lower-case words joined by hyphens, at most 64 characters; no dots, spaces or
slashes), normalised once inside the policy. A string that is not a tag (a host, an address, free text, the telemetry's bucket word
`(uncategorised)`) is treated as *no category*. The sensitive set is a **floor**: configuration can add to it (the DPO's right) and can
never remove a default entry, by construction of `InspectionSettings` (GRB-0002 findings A2 to A5).

This is the "built in everything we can" that is actually worth building: the thing that makes the capability defensible is
precisely the list of what it refuses to see.

## 4. The verdict, the default, and counts-only telemetry

`InspectionPolicy.Decide(connection) -> InspectionVerdict`, where the verdict is one of:

- **`Bypass(reason)`** — pass through untouched. Reasons: `MicrosoftManaged`, `SensitiveCategory`, `PinnedUncategorised`,
  `SwitchOff`.
- **`InspectMetadata(reason)`** — the data plane may observe connection metadata only (destination name/category, timing,
  byte counts), **never** payload. Reason `MetadataOnly`. This is the default inspection depth and needs no content handling.
- **`InspectContent(reason)`** — the data plane may terminate TLS with the inspection CA and read payload. Reason
  `ContentPermitted`. Only reachable when: the governance switch is on, the destination is categorised, the category is in
  neither bypass set, and content inspection is enabled for that category.

**Rule EH-DEFAULT (fail-closed).** With the governance switch off (the shipped default), `Decide` returns `Bypass(SwitchOff)`
for every connection. With the switch on but a destination uncategorised or sensitive, the deepest allowed verdict is
`InspectMetadata` or `Bypass`, never `InspectContent`. Content inspection is never the default for anything.

**Telemetry is counts, never content (CMB, constitution C3, chapter 06 "counts, never content").** Event Horizon emits, per
window, counters keyed by `(category, verdict, reason)`: how many connections were bypassed and why, how many were
metadata-inspected, how many content-inspected. No destination name, no device subject, no payload, no IP leaves as an
individual record. A category that is not a tag is counted under `(invalid-category)` (a resolver bug stays visible as a count, never
as the string), and the counters hold at most 1024 distinct keys, the rest under `(overflow)`, so a feed that emits unique tags cannot
grow the process (GRB-0002 findings C1, C2). A **sealed finding** (a rule match, e.g. known-malware domain contacted) carries a
category, a rule id, a count and a severity — never the host, the person or the content; both strings have a grammar enforced in the
constructor (`eh.<words>` and a category tag), so free text cannot ride inside the record (GRB-0002 finding C4). The scan that level 3
requires (no secret, no content in any log or record) runs over everything Event Horizon emits.

**Access and audit.** Turning the switch on, changing the sensitive set, or enabling content inspection for a category is a
privileged, audited action (who, when, what, four-eyes where a second admin exists), exactly like every other CMOS write. The
bypass lists and the sensitive set are readable as plain settings so the DPO can see them.

## 5. The inspection trust material (gated, not built here)

To read content on managed devices the data plane would terminate TLS, which needs a certificate the managed devices trust for
arbitrary hostnames. The design keeps this **completely separate** from the CMOS identity CA of `ca-plan.md`:

- A **separate inspection root** and a **separate inspection issuing CA**, never the CMOS root. The CMOS root and its roles
  (Device, Server, ImTokenSigner, …) must never gain the power to impersonate arbitrary hostnames — that would make every
  IM/identity certificate a potential interception certificate. `Im.Trust` already refuses any role whose EKU a leaf does
  not carry; the inspection material carries none of the CMOS role OIDs, so CMOS trust refuses it and identity trust refuses
  the inspection material. The two trust worlds stay disjoint.
- The inspection root is distributed to **managed devices only**, through an **Intune trusted-certificate profile scoped to a
  named device group**. That is a tenant write that reaches real devices: **gated on the owner's words**. Removing the one
  profile switches interception off at the device, which is the kill switch the DPO will ask for.
- It is **never** published into Active Directory and never trusted by the CMOS servers themselves.

This whole section is an open design, deliberately left as questions for the owner and the DPO (section 8). No root, no CA, no
profile and no interception certificate is created in this pull request. The code built here does not terminate TLS at all; it
only *decides*.

## 6. What we do control, and should use before any content inspection

The owner is right that we control the infrastructure, and most of the security value is reachable **without** content
inspection, which is why the design leads with these:

- **CMOS DNS as the steering and visibility layer.** Point managed clients at CMOS resolvers only (Intune). The resolver tags
  every name with a category (feeding section 3), can refuse known-bad names (protective DNS), and gives counts-only
  visibility of *where* traffic goes — all without reading one byte of payload and without breaking any pinned endpoint. This
  is the single highest-value, lowest-risk piece and it is metadata-only by construction.
- **Firewall and SNI/metadata policy.** Allow/deny and count by destination and category at the egress, again no payload.
- **Endpoint security already paid for.** The tenant runs Defender XDR (endpoint, identity, Office, cloud-apps). Microsoft's
  own guidance is that these native controls replace most of what break-and-inspect was used for on untrusted traffic. The
  design treats content inspection as the **last** resort for the narrow non-Microsoft, categorised, non-sensitive set, not the first move.

So "deep packet inspection" in CMOS, done well, is: DNS + metadata everywhere (safe, built now as the decision layer + later
resolver), and content inspection only for a small, categorised, non-sensitive, governance-approved slice behind a separate CA
and a per-device kill switch.

## 7. How it maps onto the security module's sensitivity stages (and where it changes an earlier decision)

The security concept sets a stage order (data sources by sensitivity): stage 0 no
personal data, 1 devices/alerts, 2 admin changes, 3 logon behaviour (only after the works-council replacement gate), 4
e-mail/Teams/file **content** — "not planned".

- The **decision layer, DNS steering and metadata/counts** sit at **stage 1** (device and connection metadata, no content, no
  per-person view). Safe to build and, later, to run under the stage-1 rules.
- **Content inspection** is **stage 4** — the stage the concept marked "not planned". This note does **not** overturn that
  decision; it designs the capability and then **parks the content half behind the same gate stage 3/4 already carries**:
  management sign-off, DPO sign-off, staff notice, and (if ever) works-council agreement. The change versus the earlier
  decision is only that stage 4 moves from "not planned" to "designed, built on synthetic data, switched off, awaiting the
  governance gate". The owner should see and confirm that framing (section 8, Q1).

## 8. Gates: DORA, DSGVO/DPO, works council, and the owner (the real limits)

Nothing below is a legal opinion; the data protection officer (DPO) and, where needed, counsel confirm it.

- **The one switch.** Content inspection of staff traffic is one governance-controlled switch, default off, and bypasses the
  whole sensitive set even when on. This is the design's answer to proportionality (Art. 6(1)(f) GDPR, data minimisation).
- **Private use (an open question for the organisation).** Whether private use of company mail/web is allowed, tolerated or forbidden
  decides how heavy content inspection is in law (interception of private correspondence). A documented **ban on private use
  with a staff notice** is the cleanest basis. Until it is answered, content inspection stays off; metadata/DNS is
  unaffected.
- **DPIA (Art. 35).** Content inspection of staff traffic almost certainly needs a data-protection impact assessment; the DPO
  decides. The design hands the DPO exactly what a DPIA needs: purpose, the bypass lists, the sensitive set, retention,
  access, the kill switch, counts-only telemetry.
- **Records of processing (Art. 30)** and a **staff information notice (Art. 13/14)** before any content is read.
- **Works council.** Where the organisation has none, the replacement gate is
  management sign-off + DPO sign-off + staff notice. A council elected later inherits a system already minimised by design.
- **DORA.** Where the organisation is in scope as a financial entity, inspection is an ICT risk-management control, which DORA favours, but it
  is also itself an ICT system that must be governed, logged and access-controlled — which this design is.
- **The owner's words gate everything real:** the CMOS resolver pointing real clients, the Intune certificate profile, any
  content inspection of real traffic, and any touch of real DNS, switches (core stack read-only) or devices. None is done
  here.

### Questions for the owner (recommendations in brackets)

- **Q1.** Confirm the framing: content inspection moves from "not planned" to "designed + sandbox-proven + switched off,
  awaiting the governance gate", while DNS/metadata is built now. (Recommended: yes.)
- **Q2.** Separate inspection root/CA, distributed only to a named device group via Intune, never the CMOS root, one-profile
  kill switch. (Recommended: yes.)
- **Q3.** Sensitive set bypassed by default: banking, health, government/tax, personal webmail, legal. (Recommended: yes; the
  DPO may add to it, never remove.)
- **Q4.** Drive the private-use answer (Q-0103) to a documented decision with the DPO before any content inspection. (Owner +
  DPO.)

## 9. Build order (completion goals)

| # | Step | Where | Gated |
|---|---|---|---|
| 1 | The decision layer: `IInspectionPolicy`, the verdict type, the bypass + sensitive sets, counts-only telemetry, sealed findings; proven on synthetic connections in the sandbox | `cm.os/src/Inspection`, `CM.BlackHole/src/EventHorizon` | no (built here) |
| 2 | The endpoint-list job: poll the M365 web service, write the `BypassSet` setting | The operations master/the monitoring module side | no (reads a public service; later) |
| 3 | The CMOS resolver that tags names with categories; point a **sandbox** client at it | later brief | the real DNS change is **gated** |
| 4 | The forward data plane (metadata only first), asking the policy; Milky Way carriage | later brief | listener on a port is Supernova level 4 |
| 5 | The inspection CA material and the Intune profile for a test group | later brief | **gated** (owner + DPO) |
| 6 | Content inspection enabled for one non-sensitive category, behind the switch | later brief | **gated** (owner + DPO + staff notice) |

Only step 1 is in this pull request.

## 10. Supernova level

The decision layer makes a trust/authorisation decision (what may be inspected) and holds the data that keeps content from
being read, so it is **level 3**: a refusal suite (every bypass reason has a test that produces it; a sensitive or
uncategorised destination can never reach `InspectContent`), property tests on the category/bypass matching, a scan that no
content or secret appears in any counter or finding, and resource limits. The data plane, when it is built, is the door
(level 4).

**Status (2026-10-25 12:42Z, after the signed run R2):** level 3 is **passed**. What is in place: the builder's refusal
suite (29 tests), the independent adversarial suite `Grb0002AdversarialTests` (54 cases, section 11), a hand mutation run of the
decision code (35 mutants over the policy, matcher, settings, counters, finding and tag grammar; the result is in the R1 report;
against the builder's suite alone 22 of 23 policy and matcher mutants were killed and the one survivor, a whitespace-only category,
is now a normalisation rule), the resource limits of section 4, and the signed GRB run: the owner signed GRB-0002 on 2026-10-25 12:35Z
and run R2 is recorded in the scope file (chapter 06: "one GRB run against the sandbox under a scope file"). No parser takes bytes
from another machine here, so the fuzzing line does not apply to the decision layer; it applies to the data plane when it is built.

## 11. GRB

A sandbox GRB run under a scope file (`grb/GRB-0002-event-horizon-decision-layer.md`, from `templates/grb/scope-file.md`) drives the
decision layer with synthetic connections: the Microsoft-managed set, each sensitive category, uncategorised destinations, the switch
off and on, and tries to reach `InspectContent` for something it must never reach it for. The report (what was tried by class, what
was refused, the counts) goes in the pull request and in the scope file. Scope and signature: the owner, per chapter 06.

**Run R1 (2026-10-25, the EH2 reverification, before the signature; report `grb/GRB-0002-report-R1.md`).** The core claim held on
the first draft: over the full cross product (switch × content-enable × host × category) content appeared only under the promised
conjunction. The run found no breach of a rule and **nine hardening defects** in the surface around the rule (configuration could
empty the sensitive set; comparisons depended on the caller's comparer; the literal bucket word passed as a category; a KELVIN SIGN
look-alike matched a bypass; a typo in the bypass list was swallowed; a host as category reached the counter key; counters unbounded;
free text in a sealed finding; a whitespace category counted as categorised), **two design corrections** (the Microsoft bypass scope,
section 2; the facts must be resolver-bound and need the IP half, section 2) and **one bookkeeping error** (a decision number
collided with other threads' numbers). All code defects are fixed in the same pull request; the adversarial suite stays in
the repository as the refusal suite's second half.

**Run R2 (2026-10-25 12:36Z to 12:42Z, the signed run; signature "GRB-0002 is signed and approved.", the owner, 12:35Z).** On cm `main`
at 67ad3dc, whose decision-layer sources equal an earlier change's: `Grb0002AdversarialTests` 54 passed, the whole Event Horizon suite 83 passed, the
35-mutant sweep 35 killed and 0 survived, no new finding, no stop condition. The counts and the signature are in the scope file; level 3
for the decision layer is passed. The report row B4 of R1 was corrected in an earlier change (a bare `*` matched nothing, not everything).

## Sources

- Microsoft 365 network connectivity principles — "Optimizing connectivity to Microsoft 365 services" (the recommendations "for all
  Required Microsoft 365 network endpoints (domains) and categories", "Bypass Microsoft 365 domains from TLS decryption, traffic
  interception, deep packet inspection"), "Additional network considerations" (TLS termination or DPI "of any Microsoft 365 domains",
  protocol downgrade). learn.microsoft.com/microsoft-365/enterprise, page dated 2026-08-26, fetched 2026-10-25 twice (EH1, EH2).
- Microsoft 365 IP Address and URL web service — endpoints/version/changes methods, `category: Optimize|Allow|Default`.
  learn.microsoft.com/microsoft-365/enterprise/microsoft-365-ip-web-service, fetched 2026-10-25.
- Intune network endpoints (learn.microsoft.com/intune/fundamentals/endpoints, dated 2026-08-21): "SSL traffic inspection isn't
  supported" for `*.manage.microsoft.com`, `*.dm.microsoft.com`, DHA; the caution that the Office 365 endpoint service is insufficient
  for Intune; Store API, EPM and MAA "SSL Inspection isn't supported". Defender for Endpoint (learn.microsoft.com/defender-endpoint/
  configure-environment, dated 2026-09-21): exclude `*.endpoint.security.microsoft.com` from TLS inspection and MITM proxying. Fetched 2026-10-25.
- The knowledge warehouse: its articles on works-council and data protection, data sources by sensitivity, DORA, private use of mail and web, and the DPO.

Status: draft (Black Hole lane, 2026-10-25; reverified and hardened by the EH2 thread the same day).
