# 00 · Preamble

What DMP is, how a pull request is judged against it, and who keeps it.

## What DMP is

**The mission of CM** (the owner, 2026-10-25, verbatim): "It turns Information into Insight through provably safe and compliant, near-infinite composability, while maintaining absolute Observability." DMP is how CM keeps that promise: every rule below serves it.

DMP is the rule book of CM. It has two parts that age differently. The **constitution** ([01](01-constitution.md)) is one
page of rules that hold from the first day and do not bend. The **chapters** describe how each part is built; they are living
text that the CM coordinator keeps current while CMOS is being built, each section marked stable or draft.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-007

## The marks

Every `##` section of every chapter carries a `Status:` line: `Status: draft`, or `Status: stable since DMP x.y.z`. A stable
section also carries an `Owner:` line (a role, never a name) and an `Enforced by:` line that lists the rule ids of the `dmp`
group that check it, or the words `none yet` when no check exists. A section that says `none yet` is printed by the
`dmp` runner on every run, so nobody mistakes prose for a gate. A stable section without a check is a defect in DMP, not a
rule; the fix is a check, or the mark goes back to draft.

A section may also carry a `Law:` block: numbered sentences (`- C2.L1 ...`) that Gravity proves, followed by a line
`Laws file: <path> sha256 <hex>` that locks the formal laws (`CM.BlackHole/docs/gravity.md`, "How a section gets laws"). The
laws file quotes every sentence word for word, and changes only together with the section.

The constitution has no draft sections. The CM coordinator changes a mark only in a pull request whose title says so.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-007, DMP-018

## How a pull request is judged

A pull request is judged against the constitution and the stable sections, and only those. It is refused when the `dmp`
group of `tools/verify` fails, or when a reviewer finds that it breaks a stable section no check covers yet (and the review
then opens the missing check as an issue). Draft sections are guidance and never a reason to refuse. Every pull request goes
through the CM coordinator, who runs the pipeline, tests it and merges it (chapter 14).

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-007

## Who maintains it

The CM coordinator owns DMP and its marks. A constellation coordinator proposes changes to its own chapter. The owner of the
framework (the creator of CMOS, an IT-Systemadministrator and one of The Administrator) decides anything in the constitution; rule DMP-009 makes the authority grant
tamper-evident, and every other constitution change is a pull request that only the owner can approve.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-009

## The version

`DMP/VERSION` holds the DMP version, `MAJOR.MINOR.PATCH`. It moves only in a pull request that promotes a section to stable
or changes the constitution, so a frozen component can say which DMP it was built against. A section cannot be stable
since a version newer than `VERSION`.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-007

## How DMP hardens

Until CMOS runs an organisation in production, DMP is best-effort and always changing. When it does, the chapters harden: many
hands from that organisation go over them with great effort, and once final they are set in stone. Until then "written once"
is true of the constitution only.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet
