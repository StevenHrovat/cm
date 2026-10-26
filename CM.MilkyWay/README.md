# CM.MilkyWay

The universal I/O layer of CM: the one door into CMOS, out of it and between its parts (constitution C1). It speaks IM and checks every
message (C2). Specification: [DMP 04, IM](../DMP/04-im.md) and [DMP 05, Milky Way](../DMP/05-milky-way.md), both drafts.

Security context: this part is defensive security engineering under the charter in `CLAUDE.md`. It is the operating organisation's own entrance and its
message check, tested on synthetic data in the sandbox.

## State: IM version 0 on the sandbox (lane L2, goal 1)

| Folder | What | DMP |
|---|---|---|
| `src/Verify` | `ImVerifier` (verify rules 1 to 16, in order, fail closed), `VerifiedMessage` (only the verifier creates one), `ReplayMemory`, the refusal log (`IImRefusalLog`, `ImCounters`: counts, never content) | 04 "The verify rules", "The refusal log" |
| `src/Gate` | `MilkyWayGate` and `GateSession`: the handshake and the message loop, written without I/O; the quiet answer | 04 "The handshake", 05 "The gate", "The quiet answer" |
| `src/Transport` | `GateTransport`: TLS 1.3 with mutual certificates, frames on a stream, the connection limit and the deadlines; it decides nothing about IM | 05 "Transport encryption", "Resource limits" |
| `src/Board` | `HeartbeatBoard`: the last verified heartbeat per device | 05 "The heartbeat board" |
| `src/Simulation` | `SandboxWorld`, `SimulatedClock` (with timers that fire when the clock moves), `SimulatedConnection`, `SimulatedDevice`, `InMemoryDuplex`: the in-memory world every test runs in | 05 "The simulator" |
| `src/Health` | `MilkyWayHealth`: the signed health report for Doppler (counts and accepted token ids, never content); the gate's sink in front of the board | 05 "The contract with Doppler" |
| `src/MilkyWaySettings.cs` | every tunable value with its default and description (`Describe()`) | 05 "Settings" |

The encoding and the client side are in `cm.os/src/Im` (`Im`), so senders in other constellations never reference Milky Way; the
certificate roles, chain validation, the signer and Doppler's enrolment contract are in `cm.os/src/Trust` (lane L4).

Tests: `tests/` (the `im` group of `tools/verify`, `Category=im`). Not built yet (wave 2): the listeners on TCP 443 and 138 (the TLS they run is built), token
renewal, the shield. The sandbox pair with Doppler is `tools/sandbox-pair` (lane L4), group `pair` of `tools/verify`.
