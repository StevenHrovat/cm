namespace CM.BlackHole.Doppler;

/// <summary>
/// Every value an admin could tune (DMP chapter 10, "Settings are exposed"). Defaults and descriptions are here; in a deployment the
/// values come from the settings store of the administration tool.
/// </summary>
public sealed record DopplerSettings
{
    /// <summary>How long an enrolment or token challenge stays valid. Default 5 minutes.</summary>
    public TimeSpan ChallengeLifetime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Lifetime of a device certificate (ca-plan.md section 5, profile Device). Default 365 days.</summary>
    public TimeSpan DeviceCertificateLifetime { get; init; } = TimeSpan.FromDays(365);

    /// <summary>Lifetime of an IM token Doppler issues. Default 1 hour; the agent renews before it runs out.</summary>
    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromHours(1);

    /// <summary>An enrolment plan needs two distinct approvers (four eyes) while this is on. Default on; the owner may switch it off only while there is one admin.</summary>
    public bool FourEyes { get; init; } = true;

    /// <summary>The peer (Milky Way) counts as stale when its last valid health report is older than this. Default 2 minutes.</summary>
    public TimeSpan PeerStaleAfter { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How far a peer's report may be ahead of Doppler's clock. Default 30 seconds.</summary>
    public TimeSpan AllowedClockSkew { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How many challenges may be open at once. The door is reachable by anyone who knows the knock secret, so the table is bounded; when it is full and nothing has expired, new challenges are refused. Default 10000.</summary>
    public int MaxOpenChallenges { get; init; } = 10_000;

    /// <summary>How long Doppler remembers an issued token id after the token expired, so Milky Way's late health reports still find it. Default 1 day.</summary>
    public TimeSpan IssuedTokenMemory { get; init; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Accept TPM evidence signed with SHA-1 and keys named with SHA-1. Windows' platform crypto provider makes attestation keys that sign
    /// RSASSA with SHA-1, so with this off no Windows machine can enrol. The risk it carries: with a SHA-1 chosen-prefix collision (costly,
    /// tens of thousands of euros of computing) the owner of a planned machine could make its own TPM sign a forged quote. Default on;
    /// switch it off when every enrolled platform signs with SHA-256.
    /// </summary>
    public bool AllowSha1Attestation { get; init; } = true;

    /// <summary>Lower-case hex SHA-256 of the agent packages Doppler accepts in an attestation. Empty refuses every agent.</summary>
    public IReadOnlySet<string> AllowedAgentPackages { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}
