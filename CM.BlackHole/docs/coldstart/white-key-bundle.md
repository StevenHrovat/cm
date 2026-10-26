# The White Key bundle (concept, draft)

Security context: this brief is defensive security engineering under the charter in `CLAUDE.md`. It describes what the operating organisation's own White Key
holders carry so they can restart the operating organisation's own estate.

The owner (2026-10-25 09:32Z): "as a second fail check, the White Key includes a full bundle of all relevant repositories for CMOS, as well as a
full guide that's cross-checked on how to perform all needed cold start procedures. Anything an admin needs or a White Key holder needs to
perform any planned kind of cold start is a coding agent at [Haiku 5.5] level." The first fail check is the [cold-start assistant](assistant.md). The bundle is what remains if even the assistant needs fixing.

## What is in it

| Part | Content | Why |
|---|---|---|
| 1 · The repositories | CM in full (a git bundle with history and a working tree, plus the pinned Gravity mirror), the operations master, the knowledge warehouse with its offline search, and the other repositories a rebuild needs (claude-internal for the shared tool configuration). All as signed git bundles. | C7: one repository deploys every part; losing it loses the way to rebuild (CS-10) |
| 2 · The assistant | Self-contained build, its signed runbooks, the media list, its self-test | the first fail check |
| 3 · The guide | German and English, cross-checked (below), one chapter per scenario plus the common spine | the second fail check; readable by a person who has never seen CMOS |
| 4 · Installation media and drivers | A list with a hash for each item (operating system images, hypervisor and storage drivers and firmware for the hardware in use, the backup product's installer, the Linux images and CA software for the security chain, the database engines); the media themselves where the licence allows, and where not, the vendor link and the licence reference in the vault | a rebuild on new hardware needs them offline |
| 4b · The unsealer | The reference implementation of the sealed-backup format at every version, with a sample archive of each ([sealed-backup.md](sealed-backup.md)) | the vault wrap opens archives only with it |
| 5 · The the knowledge warehouse snapshot | The knowledge base as of the bundle date, searchable offline | the team's facts: addresses, order of start, switch settings, quirks |
| 6 · The offline agent | A model, its runner and its exam results (below) | for the deviations no guide foresaw |
| 7 · The manifest | The list of everything above with hashes, the build and the date, signed | the thing everyone checks first |

**Not in it:** any secret. Secrets are in the cold-start vault (one wrap per admin and the executive copy), never in the bundle. The bundle
is still confidential (it contains the team's knowledge of the estate), so it is **encrypted at rest and opening it is a White Key read**: audited, announced when a channel exists.

## How it is built and trusted

- **Built by the pipeline, never by hand.** A job in the CM pipeline builds it from `main` at a stable promotion. Each bundle's manifest is signed by the pipeline's release key **and counter-signed by a White Key holder**, so no single origin
  can ship a poisoned bundle (CS-10 variant 2: the bundle is the worst place for a poisoned build because it is used in the worst hour).
- **Verified first.** The assistant's first act on opening a bundle is to verify the manifest against the pinned White Key set and the hashes of every part. A bundle that fails is not used; the previous one is.
- **Refreshed** at every stable promotion that touches the cold-start surface (DMP 11), at least yearly at the ceremony, and its date is shown on the assistant's first screen; a bundle older than the limit (a setting) shows a red banner but is still usable.
- **Copies:** one for each White Key copy location (the two admins' homes and the executive copy in the main-building vault), plus a copy on the archival disk set and one at the DR site. Copies are compared by hash at every ceremony.
- **Media:** two kinds, on purpose (a solid-state disk and a write-once disc set), so one failure mode cannot take both.

## The guide: written once, cross-checked three ways

The guide is **generated from the runbooks** the assistant runs (one source of truth), with hand-written explanation around the steps.

1. **Mechanical.** Every command and every step in the guide is lifted from a runbook; a check fails the build if a step appears in the guide and not in the runbook, or the other way round. The scripts are parsed under PowerShell 5.1 and dry-run.
2. **Cold read.** An independent agent session, with only the bundle and the sandbox, follows the guide for every scenario in the lab. A step it cannot do or a sentence it misreads is a finding. (This is the same run as the GRB cold-read run of the assistant.)
3. **A person.** At the yearly ceremony each White Key holder walks one scenario through with the guide only, timed. The holder who has never done it is the best tester. Findings go to the run record.

A guide whose cross-check is older than the last change to its runbooks is marked *unchecked* on its cover.

## The offline coding agent

### What it is for and what it may do

For the case no runbook covers: reading a state dump and saying what is wrong, drafting a script for a deviation, explaining a product's error, finding the right page of the guide, answering from the the knowledge warehouse snapshot. It **advises**. It holds no key, executes nothing, and its output
only runs after it has gone through the same path as any other script: parse under PowerShell 5.1, the guard, the dry run, the read-first write, and the White Key act the step needs. It is never an origin of trust. A holder can run the whole cold start without it.

### How "Haiku 5.5 level" is defined

Public benchmark numbers are vendor-reported and use different harnesses, so they cannot settle this. The level is defined by a **cold-start exam**, which is part of the bundle's build:

- About 40 tasks drawn from the ten scenarios, scored by deterministic checks in the sandbox: diagnose a state dump into the next step; repair a broken runbook step; write a PowerShell 5.1 script under the guard; answer a question from the guide and the the knowledge warehouse snapshot; and a set of **refusals** (booting the old root, a permanent write on one key, running an unparsed script, printing a secret).
- **Pass:** at least 90 % of the score the same exam gives Haiku 5.5 in the same harness, and **all** refusals right.
- The reference run of Haiku 5.5 uses the Claude API with synthetic material only (roles, not values; no company data). It is run again for each new candidate model and each new exam version.

### Which local model can reach it: what I found

The question was which locally runnable model can reach Haiku 5.5 offline, and to say so plainly if none can. **Plainly: no model reaches it on a laptop or a single consumer GPU. One class of model reaches or nearly reaches it on a workstation of about 256 GB of memory or more, and I could not test any of them, so every claim below is a published number, not a measurement.**

The reference: Haiku 5.5 scores 39.2 % on Terminal-Bench 4.0 (two secondary sources agree; vendor-reported, no independent reproduction found). Sources disagree on its SWE-bench Pro number and I did not rely on it.

| Candidate | Size | Published coding-agent numbers | What it needs | Licence |
|---|---|---|---|---|
| GLM-5.3 (flagship) | about 744 to 753 B parameters, mixture of experts, about 40 B active | 41.8 % on Terminal-Bench 4.0 on one October snapshot (above Haiku 5.5); sources differ | server class: by simple arithmetic roughly 400 GB or more at 4 bit (my estimate, not a sourced figure) | custom licence, not MIT; terms need checking |
| DeepSeek V4.1 Flash | about 550 to 760 B total, 8 to 16 B active | 31.2 % (vendor) to 42.3 % on Terminal-Bench 4.0, depending on source | official checkpoint about 510 GB; server class | MIT (per sources) |
| **GLM-5.3-Flash** | about 320 B, 18 B active | 35.8 % and 32.8 % on Terminal-Bench 4.0 (two snapshots), i.e. about 3 to 6 points under Haiku 5.5 | a 4-bit build is about 160 to 200 GB; a 256 GB unified-memory machine or two small-form-factor AI workstations (claim from a quantisation vendor, not independently checked) | MIT (per one source) |
| Qwen3-Coder-Next | 80 B, about 3 B active | SWE-bench Pro 42.7 to 44.3 %, Terminal-Bench 2.0 36.2 % (a different, older benchmark) | fits one 96 GB card | open (check) |
| Devstral 2 | 123 B | 72.2 % on SWE-bench Verified (November 2025) | one 96 GB card, tight | check |

**Reading it.**

- **Haiku-class on a workstation: yes, probably, with GLM-5.3-Flash at 4 bit as the best fit.** Its published Terminal-Bench 4.0 number is a few points under Haiku 5.5's; the exam, not a leaderboard, decides whether that matters for cold-start work. The flagship GLM-5.3 and DeepSeek V4.1 Flash match or beat the number but need server-class memory.
- **Laptop-class: no.** The best models that fit one card are clearly below Haiku 5.5 on agentic tasks (Qwen3-Coder-Next about 43 % on SWE-bench Pro against a figure for Haiku 5.5 I could not confirm, and a different benchmark family). They are fine as a **fallback tier** ("small model, lower expectations, labelled so on screen") on a 64 GB machine, not as the Haiku-level agent.
- **The memory is the cost.** A 256 GB machine kept in the vault is the price of this fail check. The three Hyper-V hosts have 512 GB each, but they are what may be dead; a standalone host or workstation at the vault or the DR site is the realistic place. CPU inference of an 18 B-active model is possible but slow (memory bandwidth bound); no figure here is measured.
- **Provenance and licence.** Weights are data, not code, but the runner is code: pin the runner by hash, build it from source in the pipeline, run it with no network. A model of foreign origin raises a supply-chain and third-party question that the DPO and the DORA third-party register should see before the bundle ships it. Verify every licence against the model card; the figures above come from secondary pages.
- **Why I could not test it.** This cloud session cannot reach the model hosting sites (checked in earlier threads) and has no GPU or 256 GB of memory. Testing happens on the project's own hardware, on the owner's hand: the allow-list for the download on the development VM, the lab node or a purchased workstation; measure speed (tokens per second at 4 bit), then run the exam.

Sources (secondary, 2026-10-25, check before relying): [Haiku 5.5 benchmarks (Datacamp)](https://www.datacamp.com/blog/claude-haiku-5-5), [Haiku 5.5 benchmarks (apidog)](https://apidog.com/blog/claude-haiku-5-5-benchmarks/), [Terminal-Bench 4.0 leaderboard (llm-stats)](https://llm-stats.com/benchmarks/terminal-bench-4.0), [GLM-5.3 weights and licence (noze.it)](https://www.noze.it/en/insights/glm-5-3-weights-licence-benchmark-method/), [Run GLM 5.3 Flash locally (MindStudio)](https://www.mindstudio.ai/blog/run-glm-5-3-flash-locally), [GLM-5.3-Flash quantisation claim (Unsloth, on X)](https://x.com/danielhanchen/status/2092996385302094189), [DeepSeek V4.1 Flash local guide (Unsloth)](https://unsloth.ai/docs/models/deepseek-v4.1-flash), [Qwen3-Coder-Next technical report](https://arxiv.org/html/2603.00729v1).

### What the bundle ships for the agent

The model file, the runner (source and a build), the exam and its last result, a hash for each, and a one-page note on the memory it needs. If no machine at hand has it, the bundle still works: the agent is the third fail check, after the assistant and the guide.

Status: draft
