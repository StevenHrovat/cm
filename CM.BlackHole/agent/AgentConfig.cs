using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CM.BlackHole.Doppler;

namespace CM.BlackHole.Agent;

/// <summary>
/// The agent's configuration, <c>agent.json</c> in its data folder (<c>%ProgramData%\CM\DopplerAgent</c>, readable by LocalSystem and
/// administrators only, set by install.ps1). The packaging script writes it from the settings store; the repository holds no value of
/// it (roles, not values). It holds the knock secret, so it is never logged or printed.
/// </summary>
public sealed record AgentConfig(string GateHost, int GatePort, byte[] KnockSecret, IReadOnlyList<X509Certificate2> TrustRoots, AgentOptions Options)
{
    sealed record Dto(int Version, string GateHost, int GatePort, string KnockSecret, string[] TrustRoots,
        string? HeartbeatInterval, string? RetryInterval, string? MaxRetryInterval);

    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CM", "DopplerAgent");

    public static AgentConfig Load(string path)
    {
        var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllBytes(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("agent.json is empty");
        if (dto.Version != 1) throw new InvalidDataException("agent.json has an unknown version");
        if (string.IsNullOrWhiteSpace(dto.GateHost) || dto.GatePort is < 1 or > 65535) throw new InvalidDataException("agent.json: gateHost and gatePort are required");
        var secret = Convert.FromBase64String(dto.KnockSecret);
        if (secret.Length < 32) throw new InvalidDataException("agent.json: the knock secret is shorter than 32 bytes");
        if (dto.TrustRoots is not { Length: > 0 }) throw new InvalidDataException("agent.json: at least one trust root is required");
        var defaults = new AgentOptions();
        return new AgentConfig(dto.GateHost, dto.GatePort, secret,
            [.. dto.TrustRoots.Select(r => X509CertificateLoader.LoadCertificate(Convert.FromBase64String(r)))],
            defaults with
            {
                HeartbeatInterval = Span(dto.HeartbeatInterval) ?? defaults.HeartbeatInterval,
                RetryInterval = Span(dto.RetryInterval) ?? defaults.RetryInterval,
                MaxRetryInterval = Span(dto.MaxRetryInterval) ?? defaults.MaxRetryInterval,
                AgentVersion = typeof(AgentConfig).Assembly.GetName().Version?.ToString(3) ?? defaults.AgentVersion,
            });
    }

    static TimeSpan? Span(string? value) => value is null ? null : TimeSpan.Parse(value, CultureInfo.InvariantCulture);
}
