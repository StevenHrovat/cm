# CM.Observatory

Observatory is the central observability and administration stack of CMOS: the link between the whole infrastructure that CM runs and the people
who administer it. It is a **constellation of its own**: `cm.observatory`. The surface through which The Administrator views and administers CMOS is
Observatory and its Administrator node.

Scope: the observe, control and contain surface of every component CM deploys.

## What is here today

| Part | State |
|---|---|
| `src/CM.Observatory`, `tests/CM.Observatory.Tests` | the tree node: empty but tested (rule DMP-004). The product code arrives with Observatory 1.0. |

The specification, the coverage register, the register check and the page template are not part of this seed: in the previous repository they described one
organisation's deployed stack. They return as generic parts in a later pull request.

## Rules that belong to this node

- A component that reaches a deployment declares what Observatory can observe, control and contain.
- Depends on `cm.io` and `cm.os` only, like every constellation; it reads what components publish (status records) and sends IM orders, it never logs in.
