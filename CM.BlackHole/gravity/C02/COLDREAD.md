# Cold read of C2's laws (2026-10-25)

Before C2's laws were locked (DMP-018), a fresh session that had never seen the sentences read `LAWS.bend` with every comment
stripped (so the quoted sentences were gone) and wrote what each law guarantees. Its report follows, lightly shortened. It is the
back-translation the owner compares with C2's `Law:` block; `CM.BlackHole/docs/gravity.md` "Slice G1" lists what it found missing.

## verified_means_authorised (C2.L1)

For every facts f, time now, configuration and memory: if verify returns verified, then the token signer's chain is trusted and the
token signature holds, the subject is enrolled (not revoked, not mismatched), the message signature holds, `nbf <= now + skew`,
`now < exp`, and the kind is Heartbeat with HeartbeatSend among the token's permissions. An unlisted kind can never be verified.

Not covered: size, parsing, session, staleness of `sent`, the token lifetime policy and the memory (those are L2's rows). The clock
skew applies to `nbf` only; expiry has none. One way only: it does not say an authorised message gets verified.

## no_replay (C2.L3)

If verifying f at now against a memory succeeds, verifying the same f against the memory that first answer returned fails at every
time now2.

Not covered: the reason of the second refusal (at a late now2, expiry alone satisfies the law); a copy with another `sent`, payload
or session; a retry after other messages changed the memory; what verify writes into the memory.

## table_heartbeat, table_nothing_else (C2.L4)

The permission a heartbeat needs is HeartbeatSend; an unlisted kind needs none (it is not a kind). Neither law links the table to
verify; `Permitted` and the `lacks` row repeat the same mapping. SessionOpen and OtherPerm are needed by no kind.

## first_failing_rule (C2.L2)

For every input, take the first row of the table whose test is true. If there is one, with reason w, the verdict is exactly w and the
memory comes back unchanged. If no row fails, the verdict is pass, Replayed or ReplayCacheFull; earlier rows win.

Not covered: when every row passes, what the memory becomes, and when pass happens rather than one of the replay reasons. Malformed
and UnknownVersion are each shared by envelope, token and payload. The payload is checked last, after permissions. The lifetime row
uses truncating subtraction, which the `exp < nbf` disjunct covers. Session SNone passes; only SOther fails.

Rows in order: 1 oversize, Oversize; 2 envelope malformed, Malformed; 3 envelope unknown version, UnknownVersion; 4 token malformed,
Malformed; 5 token unknown version, UnknownVersion; 6 chain untrusted, UntrustedTokenSigner; 7 chain expired, CertificateExpired;
8 token signature bad, BadTokenSignature; 9 `exp < nbf` or `exp - nbf > max_life`, TokenPolicy; 10 `now + skew < nbf`,
TokenNotYetValid; 11 `exp <= now`, TokenExpired; 12 not enrolled, NotEnrolled; 13 revoked, Revoked; 14 enrolment mismatch,
EnrolmentMismatch; 15 other session, SessionMismatch; 16 message signature bad, BadMessageSignature; 17 `sent + skew < now` or
`now + skew < sent`, Stale; 18 unlisted kind, UnknownKind; 19 heartbeat without HeartbeatSend, WrongPermission; 20 payload malformed,
Malformed; 21 payload unknown version, UnknownVersion.

## full_memory_refuses (C2.L3b)

If the memory holds at least `cap` entries whose expiry is not before now, verify does not verify. Not covered: the reason
(ReplayCacheFull is not forced); entries are counted without regard to key and id; with cap 0 nothing ever verifies; room in the
memory does not mean acceptance; expired entries need not be evicted.

## General

The meaning of verify, verdict, memory and the entries is taken from the model the reader could not see. No law states liveness:
that some valid message is ever accepted.
