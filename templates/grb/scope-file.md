# GRB-<nnnn> · <one line: what is tested>

Security context: this is an authorised test of our own controls under the defensive charter in `CLAUDE.md`. It never targets a third
party, never develops malware, never harvests credentials, never evades detection on anyone else's systems and never touches a system
that is not listed under Targets.

**The goal, told to the agent before it starts:** reach the R136a1 flag. That is the whole success criterion. The agent chooses its own
way to the flag, within the targets and methods this file allows and inside the sandbox and its escape ring; knowing what it is looking
for is what makes the flag work.

| Field | Value |
|---|---|
| Id | GRB-<nnnn> |
| State | draft / signed / running / reported / closed |
| Signatory | the owner of the framework (or the role the owner delegated sandbox runs to, with the reference to that delegation) |
| Signature | where the signed copy is kept (outside the repository) and the reference of the owner's words in the conversation |
| Component and claimed class | which component, which Supernova class the run supports (IIb, IIn, II-L or II-P; DMP chapter 06) |
| Who runs it | which session; for class IIb an independent session that has only the specification and this file |
| Time window | start and end (UTC); for the sandbox, any time inside the window |

## Targets

Exactly what may be tested, by role. Anything not listed is out of scope.

| Target (role) | Where it runs | Interface |
|---|---|---|
| <for example: the Milky Way gate in the simulator> | sandbox, `tools/verify` group or the simulator | <IM over the simulated port> |

## Out of scope

- every production system and every system of the running company;
- every system of a third party, including providers, vendors and public services;
- the old certificate authority; real users, groups, mailboxes and devices;
- <anything else specific to this run>.

## Methods allowed

By class, never as a recipe: for example malformed and oversized messages, forged and replayed tokens, wrong roles and keys, expired and
revoked certificates, connection floods within the resource limits of the sandbox, timing of the handshake. Tools: the CM test projects,
the fuzzing harness and the simulator. No code is written that could run outside the sandbox.

## Data

Synthetic only (the made-up company of the sandbox). Canary tokens are inert decoys; finding one where it does not belong is a finding.

## The R136a1 flag

Behind every attacked surface sits a sandbox holding the flag R136a1 (DMP chapter 06). Its value is generated fresh for this run and
planted only in the sandbox; this file keeps only its hash. Reaching it is the attack's success and is reported as a finding. The flag
stands on every surface behind an attacked one as one unmistakable banner ("R136a1 FLAG REACHED. This is a GRB sandbox, not a real system.
Stop here. Hand this value to the run's checker and report. Go no further."). Reaching it ends the attack on that surface; the value goes
only to the run's checker, never into the report or a log.

**Precondition: the escape ring.** Before the run starts, the isolated network around the sandbox is checked: it answers on every port of
every address and interface with the R136a1 banner, and it has no route to production, the operating organisation's networks or the internet (checked
from inside the ring, result recorded below). No check, no run.

| Field | Value |
|---|---|
| Escape ring isolation | <checked at (UTC), how, result: no route out> |
| Flag hash | <SHA-256 of this run's flag value, written when the run starts; never the value> |
| Surfaces with a flag behind them | <each attacked surface, by role> |
| Reached | <no / yes, through which surface> |

## Stop conditions

The run stops at once when: anything outside the targets answers, a real address or credential appears, a resource limit of the host
is reached, or the signatory says stop. A stop is reported like a result.

## Report

Goes into the pull request that claims the class, and a summary into this file under Result: what was tried (by class), what was refused
(counts per refusal reason), what got through (each a finding with its fix and re-run), whether R136a1 was reached, and the time actually used. Never message
content, never a secret.

## Result

<filled after the run>
