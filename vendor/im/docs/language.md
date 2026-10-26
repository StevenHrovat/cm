# The language: what an IM sentence is

IM, the Interstellar Medium, is the language of acts in a CMOS galaxy. DMP says what trust is: a witness trusts a part for an act at a
time (`vendor/dmp/01-constitution.md`, the words). IM is the closed set of acts a part can speak, and the one shape every act is spoken in,
so that every door decides every act the same way, with both witnesses' grants (Axiom 1, T1), and the record holds every one (Axiom 3).
The owner, 2026-10-26: "IM is defined by its ability to make DMP axiomatically sound" (`vendor/dmp/04-im.md`, "What IM is"), and "verbs
reduce to read, write and execute over nouns and adjectives".

IM carries the act and its record, never the bytes: bulk data flows on a stream the act opened, bound to the act's token, the way TLS is
bound to the gate today (DMP 04: "IM carries control data, never bulk data"; the owner approved this reading on 2026-10-26).

## One sentence

```
sentence := kind path adjectives
kind     := <layer> "." <verb>          the envelope's kind field, and the permission the token needs
verb     := read | write | execute
layer    := setup | board | file | record     (this list grows by addition at a stable promotion, C4)
path     := 1 to 1024 bytes of a-z 0-9 . - /  the item inside the layer
adjectives := mode expect
mode     := dry | apply
expect   := any | absent | present      a write says absent (create) or present (overwrite), C15; a read or an execute says any
```

| Verb | Information flows | Keeps |
|---|---|---|
| read | from the noun to the part | T1 (a read on an enrolled part is inside, T19) |
| write | from the part to the noun; the sentence says what it expects to find there | C15 (a write first reads what it changes), Axiom 3 |
| execute | nowhere; the noun changes state (a job, a promotion, a seat) and only the record flows | T28 (enforcement is an act), Axiom 3 |

The verbs people type (pause, resume, explain, dry-run) are spellings, not verbs: each is exactly one sentence, so "one name in three
spellings, never a fourth" holds and the permission and the proof see only read, write and execute. `pause` is `write job/x/control` with
the word in the stream it opened; `explain` is `read job/x/plan`; a dry run is the `dry` adjective of any sentence.

A token lists kinds, `<layer>.<verb>`: one row of Lampson's access matrix (1971). A sentence passes a door only when both tokens of the
sender carry its kind (IM version 1, two chains; T1: the intersection). The layer places the noun and never grants (C12): who may act on
it comes only from the grants.

## The layers

| Layer | Nouns | First sentences |
|---|---|---|
| `setup` | what a root repository needs to run itself (`vendor/dmp/SETUP.md`): `toolchain/<id>`, `host/<name>`, `pin/<repo>` | `setup.read toolchain/lean` (version, source, hash); `setup.write toolchain/lean apply absent` (install); `setup.execute check apply` (the check) |
| `board` | the heartbeat board of Milky Way (DMP 05) | `board.write heartbeat apply present` (what `im.heartbeat` is in version 0) |
| `file` | files (the files layer, C12); the bytes flow on the stream the sentence opened, never in the sentence | `file.read <path>`, `file.write <path> apply absent` |
| `record` | the record of acts (Axiom 3): grants, uses, losses; counts for everyone, content by a grant (T21) | `record.read <path>`; an admission or an export is `record.write` (Axiom 3: every grant is a record), not a fourth verb |

An interface of CM, `cm.ii.<layer>`, is one layer's noun family with its three verbs (the owner, 2026-10-26: cm.ii, Information
Interfaces; a decision on the DMP chapter 02 text, which names `cm.io`, is the owner's and not made here). The interface names the kind
of information, never the platform; where it runs (a file system, an architecture, an operating system) is a part of `cm.os` and a line of
the recipe (the owner, 2026-10-26: "we already have a solution, cm.os.x8664.…").

## Versions

IM version 0 (`vendor/dmp/04-im.md`) stays as it is: one kind, `im.heartbeat`, and C2.L4 ("no other kind exists in IM version 0") is a
locked law. Sentences are IM version 1 payloads: the envelope of version 1 (two tokens, one signature, DMP 04 "IM version 1") carries the
kind `<layer>.<verb>` and the payload below. Version 1 only adds (C18); nothing is locked yet (`DMP/im-lock.txt` in CM is empty).

## On the wire

The payload of a sentence (the envelope's `kind` field already names the kind):

| Field | Encoding |
|---|---|
| magic, version | `"IMS"`, `u8` = 1 |
| path | `bytes16`: `u16` length, then the bytes; 1 to 1024 bytes of `a-z 0-9 . - /` |
| mode | `u8`: 0 dry, 1 apply |
| expect | `u8`: 0 any, 1 absent, 2 present |

Nothing else, nothing trailing; an ill-formed sentence (a write that expects `any`, a read that expects something, an illegal path) is
malformed (`ImFormatError.Malformed`), a wrong version byte is `UnknownVersion`. Code: `src/Im/Language/ImSentence.cs`
(`ImSentence`, `ImSentenceCodec`, `ImLanguage`, the table of kinds).

## The proof (`proof/`)

The grammar is closed Lean types in Gravity's shape (`vendor/gravity/evidence/Dmp/Recipe.lean`); `Shape.all` lists every kind, mode and
expectation and is proven complete (`Shape.all_complete`: no sentence has a shape outside the list). The laws, each a DMP sentence at the
level of one IM sentence:

| Law | DMP | Theorem |
|---|---|---|
| L1 both grants or refuse | Axiom 1, T1 | `pass_needs_both_grants`, `one_grant_never_passes` |
| L2 fail closed | Axiom 1, C2.L2 | `nothing_granted_nothing_passes` |
| L3 one permission per kind, no two kinds share one | DMP 04, kinds and permissions | `permissions_distinct` |
| L4 legal names | DMP 04, encoding | `kind_names_ok` |
| L5 a write says what it expects | C15 | `write_says_expect`, `read_execute_expect_any` |
| I6 one canonical spelling | DMP 04 | `decode_encode` (for every well-formed sentence with any legal path), `decode_wf`, `decode_kind`, `trailing_byte_malformed`, `wrong_version_malformed`, `ill_formed_never_decodes` |
| the door makes the composition sound | DMP 04, "What IM is" | `sound`, `galaxy_robust`, `galaxy_is_a_node`, `order_free`, `echo_alone_not_robust`, `im_makes_echo_sound` (Compose.lean, carried from the cm-work draft, pull request 137) |
| the closure | the owner, 2026-10-26 | `closure`: every sentence keeps the decidable laws through its shape; `space_size` 72, `wf_space_size` 32, `kinds_size` 12, computed by the kernel |

`proof/tools/check.sh` checks the pins and the setup file, refuses escape hatches (`sorry`, `axiom`, `native_decide`, …), builds, asks the
kernel which axioms every theorem rests on (`propext`, `Quot.sound`, `Classical.choice` only, Gravity's audit) and regenerates
`proof/table/shapes.tsv`, the table of every well-formed shape with its payload bytes. `tests/Language/ShapeTableTests.cs` replays that
table through the C#: every row reproduced, no C# shape the proof does not have. That is the exhaustive check DMP chapter 06 asks for
("IM: checked, exhaustively") in the shape Cedar uses (a Lean model, an engine tested against it; `docs/acknowledgements.md`).

Each proven sentence is a lemma in the ledger of DMP chapter 06. Both chains (C22): this proof is stated from DMP 04 and this grammar;
the evidence chain, written from the C#, lives in Gravity and never names these files.

## What is not here yet

The reference verifier (rules 1 to 16, today in CM's Milky Way) next to the codec, so a clone of DMP, Gravity and IM verifies a sentence on
its own; the version 1 envelope with two tokens in this library; the class adjective (C3); the depth field (T32); the MCP facade of the
cm.ii interfaces; the lemma ledger file. Findings the proof produced: `docs/findings.md`.
