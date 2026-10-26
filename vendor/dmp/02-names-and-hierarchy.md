# 02 · Names and the hierarchy

The names are the composition tree, and the composition tree is the folder layout.

## The three levels of the name

**Canis Major** is the project. **CM** is the framework: this repository, and what everything is called in speech and docs.
**CMOS** is what a company runs when CM runs its whole infrastructure (Canis Major Operating System).

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## The tree

The names form one composition tree (what contains what), and the tree is the layout of this repository (`DMP/tree.txt` lists the nodes).
The "May reference" column is a different structure, the dependency graph (what uses what):

| Folder | What it holds | May reference |
|---|---|---|
| `cm.io` | Bricks: each protocol or format once (`cm.io.sql`, `cm.io.ssh`, ...) | nothing else in CM |
| `cm.os` | Operating-system level and glue code, one level below CM | `cm.io` |
| `CM.MilkyWay` | The universal I/O layer and internal proxy; speaks IM | `cm.io`, `cm.os` |
| `CM.BlackHole` | The security suite (Event Horizon, CMB, Doppler, Supernova, GRB, Singularity) | `cm.io`, `cm.os` |
| `CM.Sagittarius` | The relational data warehouse | `cm.io`, `cm.os` |
| `CM.Pictor` | The clients and their design language | `cm.io`, `cm.os` |
| `CM.Wezen` | Monitoring | `cm.io`, `cm.os` |
| `CM.Observatory` | The central observability and administration stack | `cm.io`, `cm.os` |

Each node is `<Folder>/src/<Folder>.csproj` with `<Folder>/tests/<Folder>.Tests.csproj`: a node is never without a test project
("empty but tested"). The namespace of every file is the folder name or starts with it followed by a dot: the namespace is
the place in the tree. A constellation never references another constellation; they meet through Milky Way. A folder at the
root named like a node (`cm.` or `CM.` prefix) that `tree.txt` does not list is refused.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: DMP-004, DMP-008

## Adding a node

A new child of CM (level 1: a constellation, or a sibling of `cm.io` and `cm.os`) is a pull request that adds one line to `DMP/tree.txt`,
one line to `DMP/tree-approvals.txt` naming where and when The Administrator and LHC approved it in words (or `LHC pending` until the first LHC is minted), and the node's two projects (constitution C17).
A new node at level 2 or below is a folder inside its parent and needs no line; inside `cm.io` and `cm.os` it carries a README with its
scope (next section). A constellation that is not named yet is not added under a working name.

Status: stable since DMP 0.3.0
Owner: CM coordinator
Enforced by: DMP-004, DMP-014

## Foundation bricks are complete

Level 2 of the foundation (the bricks in `cm.io`, the parts of `cm.os`) is laid out once and is as complete as possible within its scope.
If CM speaks a protocol or a format, it speaks all of it, in one brick: everything any part ever needs of SQL goes into the SQL brick of
`cm.io`, and nothing outside `cm.io` references a protocol or file-format library (older uses are listed in `DMP/protocol-exceptions.txt`,
and that list only goes down). Each brick and part writes down its scope in a README (`Scope:`), what it covers today and what it does
not cover yet. Once a brick is built, tested and stable it is done: it grows only by addition at a stable promotion (C4), and a part that
uses it never works around it. The parts of a constellation at level 2 (Doppler, Event Horizon, ...) are not bound by this section;
they grow with their constellation (the owner, 2026-10-25).

Status: stable since DMP 0.3.0
Owner: CM coordinator
Enforced by: DMP-014

## Level 3 and below (proposal)

Open; the owner has not decided yet (2026-10-25). Two options:

1. **Free inside the parent.** Below level 2 the coordinator of the parent decides alone, with no further rule: the protocol rule above
   already keeps every level honest, and a deep folder costs nothing.
2. **Flat.** There is no level 3: a level-2 node holds files, and a part that wants its own folder becomes a level-2 node of its own.

Recommended: option 1.

Status: draft

## Names in use

Security names are primordial forces
under Black Hole: Event Horizon, CMB, Doppler, Supernova, GRB (Gamma Ray Burst), Singularity. Doppler's
reconnaissance part is its Scout. Federated Milky Ways form a galaxy group (idea, not built).

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet

## Sealed candidates

Name candidates that are sealed stay sealed: no agent proposes a new name for a part, and no working name enters the tree,
a namespace or a document in this repository.

Status: stable since DMP 0.1.0
Owner: CM coordinator
Enforced by: none yet
