# GRB scope file template

Security context: defensive security engineering under the charter in `CLAUDE.md`. GRB runs are authorised tests of our own controls.

A GRB run (DMP chapter 06, "GRB runs and the scope file") happens only under a scope file copied from [scope-file.md](scope-file.md).
Copy it to `CM.BlackHole/docs/grb/GRB-<nnnn>-<target role>.md`, fill every field, and get it signed before the run starts. A field left
empty means the run does not happen. Roles, not values: the scope file in the repository names roles and sandbox targets; when a run
needs a real address or a person's name, that goes into the signed copy kept outside the repository, and the file here says where.

The first one: [CM.BlackHole/docs/grb/GRB-0001-sandbox-gate.md](../../CM.BlackHole/docs/grb/GRB-0001-sandbox-gate.md).
