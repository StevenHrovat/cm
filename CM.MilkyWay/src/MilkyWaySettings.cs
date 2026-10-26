using System.Globalization;

namespace CM.MilkyWay;

/// <summary>One setting as the settings page of the administration tool shows it (DMP chapter 10, "Settings are exposed").</summary>
public sealed record SettingDescription(string Key, string Default, string Description, bool IsSecret);

/// <summary>
/// The settings of Milky Way's gate and verifier (DMP chapter 05, "Settings"). A class, not a record, so that printing it never
/// prints the knock secrets.
/// </summary>
public sealed class MilkyWaySettings
{
    /// <summary>How far a sender's clock may be off; bounds message freshness and how long the replay memory keeps an id.</summary>
    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The longest token lifetime (expires at minus not before) the verifier accepts.</summary>
    public TimeSpan TokenMaxLifetime { get; init; } = TimeSpan.FromHours(24);

    /// <summary>How long a handshake may take from the connection to the established frame.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public int MaxFrameBytes { get; init; } = 65536;

    public int MaxPayloadBytes { get; init; } = 4096;

    /// <summary>How many message ids (and, separately, knock nonces) the replay memory holds before it fails closed.</summary>
    public int ReplayCapacity { get; init; } = 100_000;

    /// <summary>The current shared secret of the knock and, during a rotation, the previous one. One or two values of at least 32 bytes. Secret.</summary>
    public IReadOnlyList<byte[]> KnockSecrets { get; init; } = [];

    /// <summary>How many door frames one session may send before the response (DMP chapter 04, "The enrolment door").</summary>
    public int MaxDoorFrames { get; init; } = 8;

    /// <summary>The longest door payload; it carries the TPM evidence and the PCR 7 event log of an enrolment.</summary>
    public int MaxDoorBytes { get; init; } = 49152;
    /// <summary>How many connections the gate serves at once; one more is closed without a byte (DMP chapter 05, "Resource limits").</summary>
    public int MaxConnections { get; init; } = 1000;

    /// <summary>
    /// How many of those connections may have shown no device certificate (machines before enrolment, which reach the knock and the door
    /// only). Kept small so certificate-less traffic can never use up the slots of enrolled devices.
    /// </summary>
    public int MaxUncertifiedConnections { get; init; } = 50;

    /// <summary>How long an established session may stay silent before the gate closes it.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);

    public int ExternalPort { get; init; } = 443;

    public int InternalPort { get; init; } = 138;

    /// <summary>Throws when the settings cannot run a gate: no secret (there is no default secret), too many, too short, or a limit that is not positive.</summary>
    public void Validate()
    {
        if (KnockSecrets.Count is 0 or > 2) throw new InvalidOperationException("milkyway.im.knockSecrets needs the current secret and at most the previous one");
        if (KnockSecrets.Any(s => s.Length < 32)) throw new InvalidOperationException("a knock secret is shorter than 32 bytes");
        if (ClockSkew <= TimeSpan.Zero || TokenMaxLifetime <= TimeSpan.Zero || HandshakeTimeout <= TimeSpan.Zero || IdleTimeout <= TimeSpan.Zero) throw new InvalidOperationException("a Milky Way time setting is not positive");
        if (MaxFrameBytes <= 0 || MaxPayloadBytes <= 0 || ReplayCapacity <= 0 || MaxConnections <= 0) throw new InvalidOperationException("a Milky Way limit is not positive");
        if (MaxUncertifiedConnections <= 0 || MaxUncertifiedConnections > MaxConnections) throw new InvalidOperationException("milkyway.im.maxUncertifiedConnections must be positive and at most milkyway.im.maxConnections");
        if (MaxPayloadBytes >= MaxFrameBytes) throw new InvalidOperationException("milkyway.im.maxPayloadBytes must be smaller than milkyway.im.maxFrameBytes");
        if (MaxDoorFrames < 0 || MaxDoorBytes <= 0 || MaxDoorBytes > Im.ImLimits.DoorMax || MaxDoorBytes >= MaxFrameBytes)
            throw new InvalidOperationException("milkyway.im.maxDoorBytes must be positive, at most the structural door maximum and smaller than milkyway.im.maxFrameBytes");
    }

    /// <summary>Every setting with its default and a plain description, for the settings page.</summary>
    public static IReadOnlyList<SettingDescription> Describe()
    {
        var d = new MilkyWaySettings();
        return
        [
            new("milkyway.im.clockSkew", Minutes(d.ClockSkew), "How far a sender's clock may be off. Messages older or newer than this are refused as stale; the replay memory keeps each message id this long.", false),
            new("milkyway.im.tokenMaxLifetime", Hours(d.TokenMaxLifetime), "The longest token Doppler may issue and the verifier accepts.", false),
            new("milkyway.im.handshakeTimeout", Seconds(d.HandshakeTimeout), "How long a device may take for the handshake before the gate refuses it.", false),
            new("milkyway.im.maxFrameBytes", d.MaxFrameBytes.ToString(CultureInfo.InvariantCulture), "The longest frame the gate reads from a connection.", false),
            new("milkyway.im.maxPayloadBytes", d.MaxPayloadBytes.ToString(CultureInfo.InvariantCulture), "The longest message payload.", false),
            new("milkyway.im.replayCapacity", d.ReplayCapacity.ToString(CultureInfo.InvariantCulture), "How many message ids the replay memory holds. When it is full and nothing has expired, new messages are refused (fail closed).", false),
            new("milkyway.im.maxConnections", d.MaxConnections.ToString(CultureInfo.InvariantCulture), "How many connections the gate serves at once. One more is closed without a byte and counted.", false),
            new("milkyway.im.maxUncertifiedConnections", d.MaxUncertifiedConnections.ToString(CultureInfo.InvariantCulture), "How many of those connections may come without a device certificate (machines before enrolment; they reach only the knock and Doppler's door). One more is closed and counted.", false),
            new("milkyway.im.idleTimeout", Minutes(d.IdleTimeout), "How long an established session may send nothing before the gate closes it. Longer than the heartbeat interval.", false),
            new("milkyway.im.knockSecrets", "(none)", "The current shared secret of the knock and, while rotating, the previous one. From the settings store; there is no default.", true),
            new("milkyway.im.trustRoots", "(none)", "The root certificates of the new certificate authority the gate trusts. The old CA is never listed here.", false),
            new("milkyway.im.maxDoorFrames", d.MaxDoorFrames.ToString(CultureInfo.InvariantCulture), "How many enrolment-door frames a machine may send in one session before it must answer the challenge. 0 closes the door.", false),
            new("milkyway.im.maxDoorBytes", d.MaxDoorBytes.ToString(CultureInfo.InvariantCulture), "The longest enrolment-door payload (the TPM evidence and the Secure Boot event log of an enrolment).", false),
            new("milkyway.listen.external", d.ExternalPort.ToString(CultureInfo.InvariantCulture), "The external port the provider opens (wave 2).", false),
            new("milkyway.listen.internal", d.InternalPort.ToString(CultureInfo.InvariantCulture), "Milky Way's internal port, reached over the VPN (wave 2).", false),
        ];

        static string Minutes(TimeSpan t) => t.TotalMinutes.ToString(CultureInfo.InvariantCulture) + " min";
        static string Hours(TimeSpan t) => t.TotalHours.ToString(CultureInfo.InvariantCulture) + " h";
        static string Seconds(TimeSpan t) => t.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s";
    }
}
