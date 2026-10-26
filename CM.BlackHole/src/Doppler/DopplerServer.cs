using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using cm.os.Health;
using Im.Trust;
using CM.BlackHole.Doppler.Tpm;

namespace CM.BlackHole.Doppler;

/// <summary>
/// The Doppler server: enrols machines into a role after attestation, is the only requester at the issuing CA, signs
/// IM tokens with its authority key, keeps the register Milky Way's verifier reads, and watches Milky Way's health while Milky Way
/// watches its own. Every decision is counted by reason and written as an audit line without content. Time comes from the injected
/// <see cref="TimeProvider"/>.
/// </summary>
public sealed class DopplerServer : IHealthSource
{
    public const string ComponentName = "doppler";

    /// <summary>The permissions an enrolled role gets in IM version 0 (DMP chapter 04): open a session and send a heartbeat. Wave 2 widens this per role.</summary>
    public static IReadOnlyList<string> PermissionsFor(EnrolledRole role) => role switch
    {
        EnrolledRole.Laptop or EnrolledRole.Test or EnrolledRole.DevVm or EnrolledRole.Runner or EnrolledRole.Build => ["im.session.open", "im.heartbeat.send"],
        _ => [],
    };

    readonly DopplerSettings settings;
    readonly ICertificateIssuer issuer;
    readonly IImSigner authority;
    readonly IImSigner identity;
    readonly ITokenEncoder encoder;
    readonly IDopplerAudit audit;
    readonly TimeProvider clock;
    readonly IDopplerStore store;
    /// <summary>Open challenges: when each expires, and the order it was issued in (the clock can stand still, so the order says which is oldest).</summary>
    readonly ConcurrentDictionary<string, (DateTimeOffset Expires, long Order)> challenges = new(StringComparer.Ordinal);
    long challengeOrder;
    readonly ConcurrentDictionary<string, PendingEnrolment> pending = new(StringComparer.Ordinal);

    /// <summary>An enrolment between its two steps: the sealed credential and the attestation key it was sealed to. In memory only; a restart forgets it and the agent starts again.</summary>
    sealed record PendingEnrolment(DateTimeOffset Expires, string EndorsementKeyId, byte[] AttestationKeyPublic, byte[] Credential);
    readonly ConcurrentDictionary<string, long> counts = new(StringComparer.Ordinal);
    readonly PeerWatch milkyWay;
    long sequence;

    /// <param name="authority">The ImTokenSigner key (ca-plan.md profile ImTokenSigner).</param>
    /// <param name="identity">Doppler's own DopplerServer certificate; signs its health reports.</param>
    /// <param name="anchors">The new root, for Milky Way's health reports.</param>
    /// <param name="milkyWayComponent">The component name Milky Way reports under.</param>
    /// <param name="store">Where plans, the register and issued token ids live; memory when null (the sandbox), a file on the Doppler server in a deployment.</param>
    public DopplerServer(DopplerSettings settings, ICertificateIssuer issuer, IImSigner authority, IImSigner identity, ITokenEncoder encoder,
        ITrustAnchors anchors, IDopplerAudit audit, TimeProvider clock, string milkyWayComponent, IDopplerStore? store = null)
    {
        this.settings = settings;
        this.issuer = issuer;
        this.authority = authority;
        this.identity = identity;
        this.encoder = encoder;
        this.audit = audit;
        this.clock = clock;
        this.store = store ?? new InMemoryDopplerStore();
        Registry = new DopplerRegistry(this.store);
        milkyWay = new PeerWatch(milkyWayComponent, TrustRole.MilkyWayGate, anchors, clock, settings.PeerStaleAfter, settings.AllowedClockSkew);
    }

    /// <summary>The register Milky Way's verifier reads.</summary>
    public DopplerRegistry Registry { get; }

    /// <summary>The KeyIds of the token authority keys Doppler currently signs with; the verifier refuses a token from any other key at once (ca-plan.md section 5).</summary>
    public IReadOnlyList<string> AuthorityKeyIds => [authority.KeyId];

    // ---- plans ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Registers an approved enrolment plan. With four eyes on, it needs two distinct approvers. A plan is used up by the enrolment it
    /// allows, so a revoked machine cannot enrol again on its old plan; a new enrolment needs a new plan.
    /// </summary>
    public bool AddPlan(EnrolmentPlan plan)
    {
        var accepted = DopplerAdmin.AddPlan(store, settings, plan);
        Count(accepted ? "plan.accepted" : "plan.refused.approvers");
        Audit("plan.add", plan.PlanId, accepted ? "accepted" : "refused: approvers");
        return accepted;
    }

    // ---- enrolment -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A fresh single-use challenge for an enrolment or a token request. The door is reachable by anyone with the knock secret, so the
    /// table is bounded by <see cref="DopplerSettings.MaxOpenChallenges"/>; when it is full the oldest open challenge makes room
    /// (GRB-0003 F2: refusing instead let a holder of the shared secret fill the table and take token renewal away from every machine for a
    /// challenge lifetime). A challenge is used within one door connection, so only a caller who issues a whole table of challenges inside
    /// that round trip can push one out. Null only when pending enrolments, which need an approved plan, hold the whole table.
    /// </summary>
    public byte[]? Challenge()
    {
        var now = clock.GetUtcNow();
        if (NewChallenge(now, evict: true) is not { } nonce) return null;
        challenges[Convert.ToHexStringLower(nonce)] = (now + settings.ChallengeLifetime, Interlocked.Increment(ref challengeOrder));
        Count("challenge.issued");
        return nonce;
    }

    /// <summary>
    /// A random challenge when the open challenges and pending enrolments together are under the cap; expired ones are swept first. With
    /// <paramref name="evict"/> the oldest open challenge (never a pending enrolment) makes room when the table is still full.
    /// </summary>
    byte[]? NewChallenge(DateTimeOffset now, bool evict)
    {
        if (challenges.Count + pending.Count >= settings.MaxOpenChallenges)
        {
            foreach (var expired in challenges.Where(c => c.Value.Expires < now).Select(c => c.Key).ToList()) challenges.TryRemove(expired, out _);
            foreach (var expired in pending.Where(c => c.Value.Expires < now).Select(c => c.Key).ToList()) pending.TryRemove(expired, out _);
            while (evict && challenges.Count + pending.Count >= settings.MaxOpenChallenges && !challenges.IsEmpty)
            {
                var oldest = challenges.MinBy(c => c.Value.Order).Key;
                if (challenges.TryRemove(oldest, out _)) Count("challenge.evicted");
            }
            if (challenges.Count + pending.Count >= settings.MaxOpenChallenges)
            {
                Count("challenge.refused.full");
                return null;
            }
        }
        return RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>
    /// Enrolment step 1 (lane L4b goal 2): checks the challenge, that an approved plan names this endorsement key, and that the
    /// attestation key is a restricted, TPM-bound signing key; then seals a fresh credential to the endorsement key and the attestation
    /// key's name (TPM2_MakeCredential). Only the TPM that holds both can recover it, so step 2 proves where the attestation key lives.
    /// </summary>
    public BeginResult BeginEnrolment(EnrolBegin request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.GetUtcNow();
        var refusal = Consume(request.Challenge, now);
        if (refusal != DopplerRefusal.None) return RefuseBegin(refusal);

        // A plan names the SHA-256 of a real endorsement key's SPKI; an empty or missing SPKI hashes to no plan.
        var ekId = Convert.ToHexStringLower(SHA256.HashData(request.EndorsementKeySpki ?? []));
        if (!store.Read().Plans.ContainsKey(ekId)) return RefuseBegin(DopplerRefusal.NoApprovedPlan);
        if (TpmEvidence.CheckAttestationKey(request.AttestationKeyPublic, settings.AllowSha1Attestation) is not { } aik) return RefuseBegin(DopplerRefusal.BadAttestationKey);
        using var ek = TpmEvidence.ImportEndorsementKey(request.EndorsementKeySpki);
        if (ek is null) return RefuseBegin(DopplerRefusal.BadAttestationKey);

        var credential = RandomNumberGenerator.GetBytes(32);
        var sealedCredential = TpmCredential.Make(ek, aik.Name, credential);
        if (NewChallenge(now, evict: false) is not { } next) return RefuseBegin(DopplerRefusal.UnknownChallenge);
        pending[Convert.ToHexStringLower(next)] = new PendingEnrolment(now + settings.ChallengeLifetime, ekId, request.AttestationKeyPublic, credential);
        Count("enrol.begun");
        return new BeginResult(DopplerRefusal.None, new Activation(next, sealedCredential.CredentialBlob, sealedCredential.EncryptedSecret));
    }

    /// <summary>
    /// Enrolment step 2: the credential came back (the attestation key is in the planned TPM); the attestation key certified the device
    /// key (it lives in the same TPM, non-exportable, and the request is for that key) over Doppler's challenge, the CSR and the reported
    /// state; the attestation key quoted PCR 7 over the challenge, and PCR 7 shows Secure Boot on; the policy holds. Then Doppler issues the
    /// Device certificate and writes the register.
    /// </summary>
    public EnrolmentResult FinishEnrolment(EnrolFinish request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.GetUtcNow();
        if (request.Challenge is not { Length: 32 } || !pending.TryRemove(Convert.ToHexStringLower(request.Challenge), out var step)) return RefuseEnrolment(DopplerRefusal.UnknownChallenge);
        if (now > step.Expires) return RefuseEnrolment(DopplerRefusal.ExpiredChallenge);
        if (!store.Read().Plans.TryGetValue(step.EndorsementKeyId, out var plan)) return RefuseEnrolment(DopplerRefusal.NoApprovedPlan);
        if (request.Credential is null || !CryptographicOperations.FixedTimeEquals(request.Credential, step.Credential)) return RefuseEnrolment(DopplerRefusal.BadActivation);
        var aik = TpmEvidence.CheckAttestationKey(step.AttestationKeyPublic, settings.AllowSha1Attestation)!;
        // The in-process path can hand over nulls the wire format cannot carry; they are a malformed request, not an exception.
        if (request.CertificateSigningRequest is null || request.Reported?.AgentPackageSha256 is null || request.Key is null) return RefuseEnrolment(DopplerRefusal.BadRequest);

        CertificateRequest csr;
        try { csr = CertificateRequest.LoadSigningRequest(request.CertificateSigningRequest, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.Default); }
        // A CSR whose key names a curve the platform does not know throws PlatformNotSupportedException, one whose algorithm identifier is
        // unknown throws NotSupportedException (its base class; GRB-0003 F4), not CryptographicException.
        catch (Exception e) when (e is CryptographicException or NotSupportedException) { return RefuseEnrolment(DopplerRefusal.BadRequest); }

        var certifyData = EnrolmentStatements.CertifyData(request.Challenge, request.CertificateSigningRequest, request.Reported);
        byte[]? deviceSpki;
        // A device public area whose point is not on its curve throws from the curve routines (GRB-0003 F4): refused like any bad certification.
        try { deviceSpki = TpmEvidence.CheckKeyCertification(aik, request.Key, certifyData, settings.AllowSha1Attestation); }
        catch (Exception e) when (e is CryptographicException or NotSupportedException) { deviceSpki = null; }
        if (deviceSpki is null) return RefuseEnrolment(DopplerRefusal.BadCertify);
        if (!csr.PublicKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(deviceSpki)) return RefuseEnrolment(DopplerRefusal.KeyMismatch);

        SecureBootVerdict? secureBoot;
        try { secureBoot = TpmEvidence.CheckQuote(aik, request.Quote, EnrolmentStatements.QuoteData(request.Challenge), settings.AllowSha1Attestation); }
        catch (Exception e) when (e is CryptographicException or NotSupportedException) { secureBoot = null; }
        if (secureBoot is null) return RefuseEnrolment(DopplerRefusal.BadQuote);
        Count("enrol.secureboot." + secureBoot.Value.ToString().ToLowerInvariant());

        var reported = request.Reported;
        if (secureBoot != SecureBootVerdict.On || !reported.DiskEncrypted || !settings.AllowedAgentPackages.Contains(reported.AgentPackageSha256))
            return RefuseEnrolment(DopplerRefusal.PolicyFailed);
        var ekId = step.EndorsementKeyId;

        var subject = "dev-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var certificate = issuer.Issue(TrustRole.Device, subject, csr.PublicKey, now, now + settings.DeviceCertificateLifetime);
        var entry = new RegisterEntry(subject, plan.Role, ImSignature.KeyIdOf(certificate), ekId, certificate.RawData, now, Revoked: false, RevokedAt: null, OnCrl: false);
        var used = false;
        IReadOnlyList<string> replaced = [];
        store.Update(s =>
        {
            // One write (GRB-0003 F5): the plan is used up, a re-enrolled machine's old identity is revoked and the new one registered together, so
            // two racing enrolments cannot both use the plan, and a plan withdrawn meanwhile leaves the old identity as it was.
            used = false;
            if (!s.Plans.TryGetValue(ekId, out var current) || current.PlanId != plan.PlanId || current.Role != plan.Role) return s;
            used = true;
            var previous = s.Register.Values.Where(r => r.EndorsementKeyId == ekId && !r.Revoked).ToList();
            replaced = [.. previous.Select(r => r.Subject)];
            var register = s.Register.SetItems(previous.Select(r => KeyValuePair.Create(r.Subject, r with { Revoked = true, RevokedAt = now })));
            return s with { Plans = s.Plans.Remove(ekId), Register = register.SetItem(subject, entry) };
        });
        if (!used)
        {
            issuer.Revoke(certificate);
            return RefuseEnrolment(DopplerRefusal.NoApprovedPlan);
        }
        foreach (var previous in replaced)
        {
            Count("enrol.revoked");
            Audit("revoke", previous, "re-enrolment");
        }
        if (replaced.Count > 0) SyncRevocations();
        Count("enrol.accepted");
        Audit("enrol", subject, $"accepted: role {plan.Role}, plan {plan.PlanId}");
        return new EnrolmentResult(DopplerRefusal.None, subject, certificate, issuer.Chain);
    }

    // ---- tokens --------------------------------------------------------------------------------------------------------

    public TokenResult IssueToken(TokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.GetUtcNow();
        var refusal = Consume(request.Challenge, now);
        if (refusal != DopplerRefusal.None) return RefuseToken(refusal);

        var record = Registry.Find(request.Subject);
        if (record is null) return RefuseToken(DopplerRefusal.NotEnrolled);
        if (record.Revoked) return RefuseToken(DopplerRefusal.Revoked);
        var certificate = Registry.CertificateOf(request.Subject)!;
        if (!ImSignature.Verify(certificate, EnrolmentStatements.TokenProof(request.Subject, request.Challenge), request.Proof))
            return RefuseToken(DopplerRefusal.BadProof);

        var claims = new TokenClaims(record.Subject, record.Role, record.KeyId, certificate.PublicKey.ExportSubjectPublicKeyInfo(),
            PermissionsFor(record.Role), now, now + settings.TokenLifetime);
        var token = encoder.Encode(claims, authority);
        var keepUntil = token.Expires + settings.IssuedTokenMemory;
        store.Update(s => s with { IssuedTokens = s.IssuedTokens.RemoveRange(s.IssuedTokens.Where(t => t.Value < now).Select(t => t.Key)).SetItem(token.TokenId, keepUntil) });
        Count("token.issued");
        Audit("token.issue", record.Subject, "accepted");
        return new TokenResult(DopplerRefusal.None, token);
    }

    // ---- revocation ----------------------------------------------------------------------------------------------------

    /// <summary>Revokes a machine: the register says revoked at once (Milky Way refuses its next message), the certificate goes on the CRL.</summary>
    public bool Revoke(string subject) => RevokeSubject(subject, "revoked by plan");

    bool RevokeSubject(string subject, string why)
    {
        if (!DopplerAdmin.Revoke(store, subject, clock.GetUtcNow())) return false;
        Count("enrol.revoked");
        Audit("revoke", subject, why);
        SyncRevocations();
        return true;
    }

    /// <summary>
    /// Puts every revoked machine whose certificate is not on the issuing CA's list yet on it. A revocation written by the admin command
    /// while the server runs takes effect in the register at once (the verifier refuses the next message); the server calls this
    /// periodically so the CRL follows.
    /// </summary>
    public int SyncRevocations()
    {
        var pending = store.Read().Register.Values.Where(e => e.Revoked && !e.OnCrl).ToList();
        foreach (var entry in pending)
        {
            using var certificate = X509CertificateLoader.LoadCertificate(entry.Certificate);
            issuer.Revoke(certificate);
        }
        if (pending.Count > 0)
            store.Update(s => s with
            {
                Register = s.Register.SetItems(pending.Where(p => s.Register.ContainsKey(p.Subject)).Select(p => KeyValuePair.Create(p.Subject, s.Register[p.Subject] with { OnCrl = true }))),
            });
        return pending.Count;
    }

    // ---- health, both directions ---------------------------------------------------------------------------------------

    /// <summary>Doppler's own report for Milky Way: counts by reason and the state of its watch on Milky Way.</summary>
    public SignedHealthReport Report()
    {
        var peer = milkyWay.View();
        var state = peer.State switch
        {
            PeerState.Untrusted => HealthState.Unhealthy,
            PeerState.Stale or PeerState.Unknown => HealthState.Degraded,
            _ => counts.ContainsKey("alarm.milkyway.unknown_token") ? HealthState.Unhealthy : HealthState.Healthy,
        };
        var snapshot = new SortedDictionary<string, long>(counts, StringComparer.Ordinal) { ["enrolled"] = Registry.Count };
        var report = new HealthReport(ComponentName, state, clock.GetUtcNow(), Interlocked.Increment(ref sequence), snapshot, []);
        return SignedHealthReport.Sign(report, identity);
    }

    /// <summary>
    /// Takes Milky Way's signed report. Beyond the signature and chain check of <see cref="PeerWatch"/>, Doppler checks Milky Way's
    /// security: every token Milky Way says it accepted must be one Doppler issued. A token id Doppler never issued means a forged
    /// token got through the verifier, and raises an alarm that makes Doppler's own report Unhealthy.
    /// </summary>
    public ReportRefusal ObserveMilkyWay(SignedHealthReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var refusal = milkyWay.Observe(report);
        if (refusal != ReportRefusal.None)
        {
            Count("peer.report.refused." + refusal.ToString().ToLowerInvariant());
            return refusal;
        }
        var issued = store.Read().IssuedTokens;
        foreach (var id in report.Report.AcceptedTokenIds)
            if (!issued.ContainsKey(id))
            {
                Count("alarm.milkyway.unknown_token");
                Audit("alarm", ComponentName, "milky way accepted a token doppler never issued");
            }
        return ReportRefusal.None;
    }

    public PeerView MilkyWay => milkyWay.View();

    public IReadOnlyDictionary<string, long> Counts => new SortedDictionary<string, long>(counts, StringComparer.Ordinal);

    // ---- helpers -------------------------------------------------------------------------------------------------------

    DopplerRefusal Consume(byte[] challenge, DateTimeOffset now)
    {
        if (challenge is null || challenge.Length != 32) return DopplerRefusal.UnknownChallenge;
        if (!challenges.TryRemove(Convert.ToHexStringLower(challenge), out var open)) return DopplerRefusal.UnknownChallenge;
        return now > open.Expires ? DopplerRefusal.ExpiredChallenge : DopplerRefusal.None;
    }

    EnrolmentResult RefuseEnrolment(DopplerRefusal refusal)
    {
        Count("enrol.refused." + refusal.ToString().ToLowerInvariant());
        Audit("enrol", "-", "refused: " + refusal);
        return EnrolmentResult.Refused(refusal);
    }

    BeginResult RefuseBegin(DopplerRefusal refusal)
    {
        Count("enrol.refused." + refusal.ToString().ToLowerInvariant());
        Audit("enrol.begin", "-", "refused: " + refusal);
        return new BeginResult(refusal, null);
    }

    TokenResult RefuseToken(DopplerRefusal refusal)
    {
        Count("token.refused." + refusal.ToString().ToLowerInvariant());
        Audit("token.issue", "-", "refused: " + refusal);
        return new TokenResult(refusal, null);
    }

    void Count(string key) => counts.AddOrUpdate(key, 1, (_, v) => v + 1);

    /// <summary>The door's own counts (a payload that does not parse, a failure inside); a key, never content.</summary>
    internal void CountDoor(string key) => Count(key);

    void Audit(string action, string target, string outcome) => audit.Write(new AuditLine(clock.GetUtcNow(), action, target, outcome));

}

/// <summary>The statements the TPM's attestation key and the device key sign. One place, so agent and server cannot drift.</summary>
public static class EnrolmentStatements
{
    /// <summary>The qualifying data of the device key's certification: 32 bytes binding Doppler's challenge, the CSR and the reported state.</summary>
    public static byte[] CertifyData(byte[] challenge, byte[] certificateSigningRequest, ReportedState reported)
    {
        ArgumentNullException.ThrowIfNull(reported);
        return SHA256.HashData(Canonical.Of("CM0/doppler/certify", challenge, SHA256.HashData(certificateSigningRequest),
            [reported.DiskEncrypted ? (byte)1 : (byte)0], Canonical.Utf8(reported.AgentPackageSha256)));
    }

    /// <summary>The qualifying data of the PCR 7 quote: 32 bytes binding Doppler's challenge.</summary>
    public static byte[] QuoteData(byte[] challenge) => SHA256.HashData(Canonical.Of("CM0/doppler/quote", challenge));

    public static byte[] TokenProof(string subject, byte[] challenge) =>
        Canonical.Of("CM0/doppler/token-request", Canonical.Utf8(subject), challenge);
}
