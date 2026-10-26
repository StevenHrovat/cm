# The data gate: the document pair

Constitution C16, rule DMP-013. A feature that directly touches company data moves from **IT and trusted advisors** to **general testing**
only when two documents exist, both built from the current artifact template (see [templates/map](../map/README.md): a page keeps the
provenance line of the template it was copied from):

1. **How the data is handled.** Exactly which data the feature reads and writes, from which system and to which, who and what can see it,
   the class of every value (C3) and where it is masked, how long it is kept and where, which gate, plan and audit entry stand in front of every
   write (C15), and what happens when a protection fails. The drawing shows the path of the data; every protection is a part with its status
   and its evidence (a test, a rule, a commit).
2. **The GRB report that proves it.** A GRB run under a signed scope file (DMP chapter 06, [templates/grb](../grb/README.md)) that tries to
   break each protection the first document claims, with the result per claim. The report names its scope file
   (`CM.BlackHole/docs/grb/GRB-<nnnn>-<target>.md`).

Where they live: next to the feature, `<node>/docs/data/<feature>/handling.html` and `<node>/docs/data/<feature>/grb-report.html`. Roles, not values
(C9): no person, host, address or real record in either page; real examples stay out, synthetic ones are fine.

How a feature moves: one pull request that adds the pair and changes the feature's stage in `DMP/data-features.txt` from `it` to `general`.
DMP-013 refuses `general` without both pages; the review of that pull request checks that they are true.

Why (the owner, 2026-10-25): it is good practice and compliant, it is the right thing to do, and it raises the trust of the users, and with it
the chance and the speed of CMOS's adoption.
