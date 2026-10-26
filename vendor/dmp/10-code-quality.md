# 10 · Code quality

The code-quality rules, trimmed to what CM needs from its first commit.

## Language and runtime

C# on .NET 10, `net10.0` in `Directory.Build.props`, nullable reference types on, warnings are errors. A product folder holds
C# only; scripts for an administrator live under `tools/`. Every component is C# on .NET 10 unless a product does the job (a firewall, a
certificate authority, a database, a prover) or a decision names another language. (This was constitution C8 until DMP 0.9.0; it is a
convention, not a rule about trust, so it lives here. The number C8 is retired and never reused.)

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-005

## Package versions in one place

Package versions live in `Directory.Packages.props` only. Never put a `Version` on a `PackageReference`, and never a
`PackageVersion` anywhere else.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-003

## Deterministic tests

Inject `TimeProvider` (or the fixed clock) for every date or timeout; wait on a condition, never on a sleep or a stopwatch
duration; format and parse with the invariant or an explicit culture; do not depend on the machine name, the user, the time
zone or local paths. A test passes at 23:59 and 00:01 UTC, on any machine, and under a busy `tools/verify`. The wall clock,
the machine name and `Stopwatch` do not appear in test code.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-010

## Empty but tested

A part that exists in the tree has a test project that runs in `tools/verify`, even while the part has no behaviour. A part
without a test project is not in the tree.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-004

## Settings are exposed

Every value an administrator could reasonably tune or has to supply (intervals, windows, limits, paths, switches, secrets) is
a setting with a default and a plain description, not only a constant or a configuration key. Secrets never enter the
repository (the secret half is checked by DMP-006; the settings half is not yet).

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## Credentials are opaque in flight and checked at the door (proposal)

"If they enforce a new standard, then when we build CMOS, we might as well build it on that standard" (the owner, 2026-10-25).
The standard that prompted this: GitHub moved its app installation tokens to a stateless, signed format. They keep the `ghs_`
prefix but grew from 40 to about 520 characters ([GitHub changelog, 2026-10-02](https://github.blog/changelog/2026-10-02-stateless-github-app-installation-tokens-rolled-out/)),
and code that assumed 40 characters broke.

**In flight, a credential is opaque.** Code that carries, stores or forwards a token, a key or a password treats it as a value it
does not interpret: no fixed length, no parsing of its format, no length check, no truncation. It is stored in a field without a
length limit, and it never appears in a log, an error message, a finding or a model prompt; only its kind may.

**At the door, it is checked.** "Even though we do not inspect them during flight, we can still fully sandbox, maybe analyze them,
maybe have some programmatic checks that they are properly formed" (the owner, 2026-10-25). Where a credential enters CMOS (the
vault import, the settings store, a new connector), a checker runs in a sandbox with no network and writes nothing about the value.
It checks what the format allows: the known prefix, the allowed characters, a generous sanity bound instead of a fixed length, and,
where the format has them, its structure and its signature. Its answer is the kind, pass or fail, and the reason. A credential that
fails is not fully trusted and is ejected (C∞): it is not stored, and the import names the entry that failed without echoing it.

**Detectors follow the new shapes.** Secret scanners and redactors (DMP-006 in this repository, the knowledge store's scrubber, the
administration tool's digest sanitizer) match the prefix and the widest character set a format uses, so a longer or dotted token
is still found and redacted whole, never in part.

**The stateless signed token is the reference pattern.** A token that carries its own claims under the issuer's signature can be
checked without asking the issuer. IM's token (chapter 04) already works this way, and so does GitHub's new format. Revocation still
needs a lookup (IM verify rule 9 asks Doppler's enrolment), so a design that adopts the pattern, the vault and Redshift among them,
keeps a short lifetime and a revocation check next to the signature.

Status: draft

## Documentation moves with code

A change that affects architecture, an interface, a setup step or a decision updates the matching document in the same pull
request. Documents are written for the next agent: explicit over clever, boring over novel, every non-obvious decision written
down. Code, comments and docs are English first; documents for colleagues and management come in the organisation's language and in English.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## Scripts an administrator runs

A script the owner runs has a read-only Check, a dry run that prints WOULD lines, an explicit `-Apply`, and is idempotent.
It also starts with the shared guard (`tools/lib/Guard.psm1`): the script stops at once unless the computer it runs on is the one the command
block names (`-ExpectedHost`, never a host name in the repository), the session is the right kind, and, when it applies a change, the checkout is
the merged one. It says whether it changes anything, names Windows groups by their well-known SID and never by name (the systems may run in a localized Windows),
and parses in Windows PowerShell 5.1 before anyone is handed it. The standard is [docs/process/owner-scripts.md](https://github.com/StevenHrovat/cm/blob/main/docs/process/owner-scripts.md).

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-011

## Guarded writes

How a script keeps constitution C15. Every change sits in the `-Write { }` block of `Invoke-GuardedWrite` (`tools/lib/Guard.psm1`),
with a live `-Read` of the target, an `-Expect` naming the state that may be overwritten (absent for a create), an optional `-Done` for
"already as wanted", a `-Verify` read back, and `-DependentSteps` naming the later steps that need it (`@()` when none: then a mismatch is
skipped and reported, and the script ends with `exit (Write-RunSummary)`, exit code 3). `-Attempts` re-reads before each try. The
read also runs in a dry run, so a dry run shows every mismatch. A script that connects to Microsoft Graph or Exchange Online calls
`Assert-Tenant` before its first write; host, account and session are the guard's first act. A line that only touches the
script's own scratch file ends with `# lint-ok: unguarded <why>`. Writes older than this rule are counted per script in
`tools/lib/unguarded-writes.txt`, and those numbers only go down. Not checked by the machine: that the read and the write name the same
object and that `-Expect` is the right condition; review that. Details: [docs/process/owner-scripts.md](https://github.com/StevenHrovat/cm/blob/main/docs/process/owner-scripts.md).

Status: stable since DMP 0.3.0
Owner: CM coordinator
Enforced by: DMP-012

## Every change is a pull request

`main` is protected and needs the `verify` check. Nobody pushes to it directly.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet
