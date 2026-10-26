# 14 · Process

How work reaches this repository and who does what.

## Roles

The **CM coordinator** keeps the project working and coherent, owns the repository, DMP, the process and the migration, and
runs operations. A **constellation coordinator** owns one part of the tree and builds it with build threads. The **owner** is the creator of CMOS, an IT-Systemadministrator and one of The Administrator, and steers by using the result and reading
the documents, never the code.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## Every pull request goes through CM

A constellation coordinator turns a section of work into an appropriately sized pull request against this repository. The CM
coordinator runs the pipeline, tests it and merges it. Coordinators are not all running at once.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## Custom scripts become issues

Wherever a native system can be plugged in or extended, that is better than building. Whenever an agent has to write a custom
script to interact with CMOS, it opens an issue on this repository asking for that tooling in the framework, so the framework
grows instead of a pile of scripts.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## The cadence

Ideas are mixed by the coordinator; what sticks becomes a concept note; a weekly tactical session with the owner decides the
next thing; the nightly builds it; troubleshooting happens on the `dev` channel; a weekly stable promotion brings the
contracts, Milky Way first, to the newest standard. The build plan is a list of steps in dependency order with completion
goals, never a time plan.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## Completion goals, never time plans

For the code side a plan says what is done when, in order, never by when. Only dates that come from outside appear as dates. (This was
constitution C11 until DMP 0.9.0; it is a convention, not a rule about trust, so it lives here. The number C11 is retired and never reused.)

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## The licence

The framework is open source under Apache 2.0. (This sentence stood in constitution C9 until DMP
0.9.0; it is a decision, not a theorem, so it lives here. C9 keeps "roles, not values".)

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-001

## Advanced testing

The authority grant (chapter 01, C6) lets agents write to the running company's systems only after the owner declares advanced
testing reached for that system, in the owner's words. The entry criteria per system class: the system's Supernova class has
passed on the sandbox; one attended write on a test target has gone through plan, gate and audit; for data lanes, two matching
shadow runs. Until then every write needs the owner's own words per action.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## The data gate

A feature that directly touches company data has two stages of audience: **IT and trusted advisors**, then **general testing**
(constitution C16). It is registered in `DMP/data-features.txt` with its paths and its stage from the first commit that reaches company
data; code that reaches company data outside a registered feature is refused. The register spans the suite: a feature that still lives in another
repository is listed as `<repository>#<pull request>` or `<repository>:<path>`; this repository checks its stage line, and
its pages are checked where they live until the module moves here. It moves to general testing in one pull request that changes
its stage to `general` and adds the pair from [templates/data-gate](https://github.com/StevenHrovat/cm/blob/main/templates/data-gate/README.md): how the data is handled, and the GRB
report that proves it under a signed scope file. The rule checks that both pages exist, come from the artifact template and that the report
names its scope file; the review checks that they are true. The authority grant (C6) and advanced testing still apply on top: the stage
says who may use a feature, not what it may write.

Status: stable since DMP 0.3.0
Owner: Black Hole coordinator
Enforced by: DMP-013

## Briefs and security context

A brief names its files and its completion goals. A security-shaped brief carries the line "Security context: this brief is
defensive security engineering under the charter in [CHARTER.md](CHARTER.md)", and a brief for a test carries its scope sentence (signatory,
targets, time window). A classifier block is a reason to add the context, never to change the model.

Status: stable since DMP 0.1.0
Owner: Black Hole coordinator
Enforced by: DMP-002

## What the owner does by hand

A step that only the owner can do comes with a short numbered how-to: where to click, what to paste, how the owner knows it
worked. Gated steps are said to be gated, and the thread stops there and waits.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## Ask the nearest tier first

Whenever an agent is unsure, before it asks the owner it asks Tier 1, the MW net (constitution C12, C13), and then outward tier by
tier. The answer names the record it used, or the agent says in one line that the search came up empty. Only then does it ask the
owner, and then it asks with the empty search or the thin record in hand. A fact or number that changes an outcome carries its
source. A second opinion from the owner stays welcome at any time; this rule fixes the order, not the right to ask. The sister rule
is the next section: the nearest tier answers first, and what no tier knew goes back.

Status: draft
Redrafted: 2026-10-26 (was stable since DMP 0.2.0).
Owner: CM coordinator
Enforced by: DMP-002

## Knowledge goes back into Tier 1

Whenever an agent learns something worth keeping that no tier held, it hands it to the MW net's intake and leaves the organizing to
the MW net. The rule does not depend on how the intake works or how the information is stored: colleagues by role, no secret, and
nobody bypasses the intake.

Status: draft
Redrafted: 2026-10-26 (was stable since DMP 0.1.0).
Owner: CM coordinator
Enforced by: DMP-002

## Combine every path

When the agent cannot get the full picture from one source (constitution C14), it lists the read paths and write paths that touch
the question before it asks anyone: the information tiers, the systems it can read (through the administration tool or Milky Way),
the repositories and `DMP/tree.txt`, and the trail of past writes (audit log, job steps, plans, dry runs, git history, the owner's
earlier words in the project). It joins them and answers with a line `Paths combined: ...` and, if a gap remains, `Still unknown:
...`. A write path is never used by writing; its trail and its dry run are the read. Only the unknown part goes to the owner, with
the combination in hand.

Status: stable since DMP 0.3.0
Owner: CM coordinator
Enforced by: DMP-002

## Look it up, don't guess

Two candidates that are close (two first names that differ by two letters, two hosts that differ by one digit, a group with and without a suffix) are
never resolved from memory (constitution C21). The agent reads the source of truth itself, through the operations master or the system that owns the
value, and writes the match with its source: `Looked up: <record> (<source>)`. When more than one record fits, it lists all of them
and asks which is meant. It does this before the first write and before the first question to the owner, so a question carries the
candidates, not a guess.

Status: stable since DMP 0.4.0
Owner: CM coordinator
Enforced by: DMP-002

## Rules need the expert (proposal)

A loose proposal from the owner (2026-10-25 06:34Z), written down so it can be tried and tightened. Every rule that a person creates by
hand also needs the approval of the person CMOS judges most competent on its topic, the subject-matter expert (SME). The main
administrator or the administrative team may override that; the rule is then only a **proposal** until the SME has weighed in.

The owner's answers of 2026-10-25 06:44Z are folded in below; what is still open is marked. Since DMP 0.9.5 the constitution grounds it:
the SME is the factual half of the topic's information layer and the responsible manager its organisational half (T38).

1. **What counts.** A rule written by hand: a DMP section, a CMOS policy, a gate, an automation rule. Not counted: a rule that a tool derives from data, or a change of wording that leaves the rule as it was.
2. **Who is the SME.** CMOS names one person per topic and shows why, in this order: (a) the **owner of the topic**; (b) if there is none, the person that the manager of the department most relevant to the topic assigns as most competent; (c) if that cannot be found, the C-suite. Anything unclear **rises up the escalation chain**: the person in charge, their manager, and so on up to the C-suite; for CMOS itself the holders of the **cold-start keys**. Open: how CMOS knows "owner of the topic" (owner roles in Tier 1, the operations master's data) and the department-to-topic table.
3. **What "weighed in" means.** An explicit approval or an active rejection with a reason, on the pull request of the proposed rule. Anything else is not a weigh-in: a comment that is not an active rejection, and silence, leave the rule a proposal. An unanswered proposal gets a reminder to the SME and then rises up the escalation chain on a clock (the interval is a setting); "no" alone does not count as a rejection. (DMP 0.9.0 decision D5, the owner 2026-10-26: tacit approval is gone, because the absence of evidence moves nothing, Axiom 2.)
4. **A rejection cannot be overridden, but it can be replaced.** While the *current* SME actively rejects with a reason, the rule can never rise above a proposal, whatever the override. Only the current SME's rejection counts: if later a different, similarly competent SME gives a fully informed opinion and approves the rule, the earlier rejection no longer blocks it. A rejection stays in the record.
5. **What a proposal is in force.** Like a draft: shown, logged and visible to the SME, never a reason to refuse a change. Open: a rule that protects something (a security gate) may need to be in force at once; the override below is meant to cover it.
6. **The override.** The holders of the cold-start keys can always keep the system in its current state. An override therefore adds nothing new: the holders cannot delete what they may not delete (the audit trail), but they can prevent anything unwanted from being added. The override is an act with a name and a time in the audit log, by a person, not by an agent.
7. **The owner as SME.** The owner of the framework is also the SME of a topic only as long as no other, similarly competent person can be found. Then the owner's approval counts as the SME's.
8. **Existing rules.** The rule applies from the day it is adopted. C12 and C13 stand; the SME of their topic is asked once to weigh in, as the first use.
9. **Where it belongs.** This section is a draft in chapter 14 because the constitution has no draft sections (DMP-007). If the owner adopts it, it moves to the constitution as C14 in a pull request that only the owner approves, and `DMP/VERSION` moves.
10. **Ask first, and the MW net is the routing point (owner, 06:49Z; restated in tiers 2026-10-26).** Tier 1 is always asked first. The MW net is also the central routing point: a question is addressed to it, and it routes it on, to the SME of the topic and up the escalation chain, over whatever transport carries it (Milky Way, the operations master). So C13 is amended to: ask the nearest tier first; if no tier can answer, ask the SME of the topic where one can be found, then up the chain, and the owner last. The amendment of C13 is a constitution change and follows only with the owner's approval.

Status: draft
