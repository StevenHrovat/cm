# AGENTS.md

For any coding agent working in this repository, on any system. The same file stands in DMP, Gravity, IM and CM, byte for byte.

## 1. Read DMP.SS1 first

DMP.SS1 is the setup-script subset of DMP: a subset of its actions and a superset of its rules (the owner, 2026-10-26). It is what an
agent obeys whenever it scripts something: setting up a machine, installing a tool, changing a system.

**State of the definition (2026-10-26): DMP.SS1 is not written down yet.** No chapter or extension of DMP names it. Until it is, read as
its stand-in, in this order:

1. `SETUP.md`: what every root repository needs, with versions, sources, hashes and install steps. It is at the root of DMP and at
   `vendor/dmp/SETUP.md` in Gravity, IM and CM.
2. DMP chapter 10, "Scripts an administrator runs" (`10-code-quality.md` in DMP, `vendor/dmp/10-code-quality.md` elsewhere), and the
   standard it links: <https://github.com/StevenHrovat/cm/blob/main/docs/process/owner-scripts.md>.
3. The rules in section 3 below.

If you find DMP.SS1 defined somewhere, that text wins over this stand-in. Do not invent its content.

## 2. Then the SS1 verifier

When the SS1 verifier is available (it is not yet: a design draft is planned in IM, `docs/ss1-verifier.md`), install its MCP executable
if you can and run every script you write through it before anything runs. If you cannot install it, you must say, before you run a
scripted action, that you hold an SS1 verifier for that action. Where a verifier exists for a script language you need it for every
action you script in that language. The first language is C#. For a language with no verifier yet, say so in your reply and follow the
rules below; do not claim a verification you did not run.

## 3. Rules on any system

- **Guarded scripts.** A script starts with a guard that checks the host, the context (normal or elevated shell) and the account it expects,
  and stops otherwise. It says whether it only reads or changes things, has a dry run when it changes something, and an explicit `-Apply`.
  It is idempotent and reads the live state before it writes and again after.
- **Command blocks for a person.** Directly above every block, a bold line: **host · context · account**. Absolute paths only, never
  relative ones. A step is a script, not a loose command.
- **No secrets.** Never write one into a file, a commit, a log, a pull request or a reply. Names of secrets are fine, values are not.
  Real values come from the environment or a settings store as parameters.
- **Walls around credentials.** Do not read, copy, export or use a credential store, key, token or password that your task did not hand
  you. Do not touch the machine, account or system your task does not name. A gated step (real accounts, production, anything
  irreversible) needs the owner's own words in the conversation first.
- **No company or personal data** in the repository: roles, not values.
- **DMP text and workflows.** Never merge a change to DMP text or to `.github/` workflows without the owner's own words. Mark the pull
  request as a draft and say so.
- **Look it up, do not guess.** A name, version, host or number that changes an outcome is read from its source first; quote it.
- **Prove before you push.** Run the repository's checks and report the result as it is, failures included: `tools/scrub-scan.sh` in all
  four, `tools/verify` in IM and CM, `tools/check-pin.sh` and `evidence/tools/check.sh` in Gravity, `tools/check-setup.sh .` in DMP.
