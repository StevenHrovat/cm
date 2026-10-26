using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Im.Trust;

namespace cm.os.Health;

/// <summary>The state a component reports about itself.</summary>
public enum HealthState
{
    Healthy,
    Degraded,
    Unhealthy,
}

/// <summary>
/// What one component tells its peer about itself (Doppler and Milky Way watch each other). Counts, never content
/// (CMB rule): <see cref="Counts"/> holds named counters, <see cref="AcceptedTokenIds"/> the opaque ids of the IM tokens the
/// reporter accepted since its last report, so the token issuer can see a token it never issued.
/// </summary>
public sealed record HealthReport(
    string Component,
    HealthState State,
    DateTimeOffset At,
    long Sequence,
    IReadOnlyDictionary<string, long> Counts,
    IReadOnlyList<string> AcceptedTokenIds)
{
    /// <summary>The bytes that are signed: a fixed tag, then every field length-prefixed, counters in ordinal key order. Stable across runs and machines.</summary>
    public byte[] CanonicalBytes()
    {
        using var stream = new MemoryStream();
        Put(stream, "CM0/health");
        Put(stream, Component);
        Put(stream, State.ToString());
        Put(stream, At.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        Put(stream, Sequence.ToString(CultureInfo.InvariantCulture));
        Put(stream, Counts.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (key, value) in Counts.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            Put(stream, key);
            Put(stream, value.ToString(CultureInfo.InvariantCulture));
        }
        Put(stream, AcceptedTokenIds.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var id in AcceptedTokenIds) Put(stream, id);
        return stream.ToArray();
    }

    static void Put(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }
}

/// <summary>A report with the reporter's signature and certificate chain, so the peer can check who sent it.</summary>
public sealed record SignedHealthReport(HealthReport Report, byte[] Signature, X509Certificate2 Signer, IReadOnlyList<X509Certificate2> Chain)
{
    public static SignedHealthReport Sign(HealthReport report, IImSigner signer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(signer);
        return new(report, signer.Sign(report.CanonicalBytes()), signer.Certificate, signer.Chain);
    }
}

/// <summary>A component that can report its health on request (the peer pulls; in production over IM).</summary>
public interface IHealthSource
{
    SignedHealthReport Report();
}
