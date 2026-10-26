using System.Security.Cryptography.X509Certificates;
using Im.Trust;

namespace CM.BlackHole.Doppler;

/// <summary>
/// Doppler's register of enrolled machines, a view over <see cref="IDopplerStore"/>. Milky Way's verifier reads it through
/// <see cref="IEnrolmentRegistry"/> on every message; only Doppler (the server and its admin command) writes it, so a revocation is
/// seen by the verifier at the next message.
/// </summary>
public sealed class DopplerRegistry(IDopplerStore store) : IEnrolmentRegistry
{
    public EnrolmentRecord? Find(string subject) => store.Read().Register.TryGetValue(subject, out var e) ? e.Record : null;

    public int Count => store.Read().Register.Values.Count(e => !e.Revoked);

    /// <summary>Every machine in the register, enrolled or revoked, for the admin command and the board.</summary>
    public IReadOnlyList<RegisterEntry> Entries => [.. store.Read().Register.Values.OrderBy(e => e.EnrolledAt).ThenBy(e => e.Subject, StringComparer.Ordinal)];

    internal X509Certificate2? CertificateOf(string subject) =>
        store.Read().Register.TryGetValue(subject, out var e) ? X509CertificateLoader.LoadCertificate(e.Certificate) : null;
}
