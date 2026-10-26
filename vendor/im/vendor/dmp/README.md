# DMP, the Dark Matter Protocol

*General Trustability.*

The single specification every pull request in [CM, Canis Major](https://github.com/StevenHrovat/cm) is judged against. A pull
request is refused when it breaks the constitution or a section marked stable; draft sections are guidance. The check is the `dmp`
group of CM's `tools/verify` (`tools/dmp`), which is also the `verify` check on CM's `main`. Paths such as `cm.io`, `cm.os`,
`CM.MilkyWay` or `tools/verify` in these chapters are paths in the CM repository. [Gravity](https://github.com/StevenHrovat/gravity)
proves DMP; [IM](https://github.com/StevenHrovat/im) is its medium.

**Three names, one flow (names ruled 2026-10-26).** DMP, the **Dark Matter Protocol** (*General Trustability*), translates intent
through IM, the machine- and audit-readable **Interstellar Medium**, directly into CM, **Canis Major** (alternative backronym, offered
explicitly: Company Management). Gravity, the DMP prover, carries the tagline *Bending Trustspace*. The names of 2026-10-25 (Direct
Meaning Protocol, Intermediary Medium) are retired. DMP and IM are independent of Milky Way in name and identity: Milky Way
carries IM and checks every message, it does not define the language. DMP, Gravity, IM and CM are released as four repositories.

| Chapter | File | Status of the file's sections |
|---|---|---|
| 00 Preamble | [00-preamble.md](00-preamble.md) | stable |
| 01 Constitution | [01-constitution.md](01-constitution.md) | stable |
| 02 Names and the hierarchy | [02-names-and-hierarchy.md](02-names-and-hierarchy.md) | stable |
| 04 IM, the Interstellar Medium (IM version 0; version 1 proposed) | [04-im.md](04-im.md) | draft |
| 05 Milky Way, the universal I/O layer (what IM version 0 needs) | [05-milky-way.md](05-milky-way.md) | draft |
| 06 Black Hole | [06-black-hole.md](06-black-hole.md) | draft (the parts, Supernova classes, GRB scope files) |
| 10 Code quality | [10-code-quality.md](10-code-quality.md) | stable |
| 11 Test design (first part: the cold-start criteria for a stable rollout) | [11-test-design.md](11-test-design.md) | draft |
| 14 Process | [14-process.md](14-process.md) | stable, one section draft |

The other chapters (03, 07 to 09, 12, 13, 15, 16) are written in the order of the owner's outline
(wave 2 starts when wave 1 is stable) and are not in the tree until they exist. Their numbers are reserved.

[SETUP.md](SETUP.md) is the one setup file of the four root repositories: every toolchain with its version, source and hash, every host a
setup or a check reaches, the install steps, and `tools/check-setup.sh`, which fails a repository that needs something the file does not list.
It is born here and travels down the root chain with the vendored copy of DMP, so all four show the same text.

`VERSION` is the DMP version. It moves only with a stable promotion. The machine-readable registers the gate reads (`tree.txt`,
`routes.txt`, `im-lock.txt` and the others) live in CM's `DMP/` folder next to the code they describe.

Every security-shaped chapter is written under the defensive charter in [CHARTER.md](CHARTER.md). Licensed under the Apache License 2.0
([LICENSE](LICENSE)).
