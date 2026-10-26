using System.Buffers.Binary;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Im.Trust;

namespace Im;

/// <summary>What reading one frame from a stream gave (DMP chapter 04, "Transport and ports").</summary>
public enum ImStreamOutcome
{
    /// <summary>A whole frame arrived.</summary>
    Frame,
    /// <summary>The other side closed cleanly between frames.</summary>
    Closed,
    /// <summary>The length prefix announced more than the limit; nothing after the prefix was read (verify rule 1).</summary>
    Oversize,
    /// <summary>The stream ended inside a prefix or a frame, or announced a frame of length zero.</summary>
    Truncated,
}

public readonly record struct ImStreamRead(ImStreamOutcome Outcome, byte[]? Frame);

/// <summary>
/// Frames on a stream transport: a 4-byte big-endian length, then the frame. The length is checked against the limit before a single
/// byte of the frame is read, so an oversize frame costs four bytes (verify rule 1).
/// </summary>
public static class ImStreamFraming
{
    public const int PrefixLength = 4;

    public static async Task<ImStreamRead> ReadAsync(Stream stream, int maxFrameBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var prefix = new byte[PrefixLength];
        var got = await stream.ReadAtLeastAsync(prefix, PrefixLength, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        if (got == 0) return new(ImStreamOutcome.Closed, null);
        if (got < PrefixLength) return new(ImStreamOutcome.Truncated, null);

        var length = BinaryPrimitives.ReadUInt32BigEndian(prefix);
        if (length > (uint)maxFrameBytes) return new(ImStreamOutcome.Oversize, null);
        if (length == 0) return new(ImStreamOutcome.Truncated, null);

        var frame = new byte[length];
        got = await stream.ReadAtLeastAsync(frame, frame.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        return got < frame.Length ? new(ImStreamOutcome.Truncated, null) : new(ImStreamOutcome.Frame, frame);
    }

    /// <summary>Writes the prefix and the frame in one write and flushes.</summary>
    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (frame.IsEmpty) throw new ArgumentException("an IM frame is never empty", nameof(frame));
        var buffer = new byte[PrefixLength + frame.Length];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)frame.Length);
        frame.CopyTo(buffer.AsMemory(PrefixLength));
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// A certificate with its private key and its intermediates, as TLS presents it: the gate's MilkyWayGate certificate on the gate side,
/// the device's Device certificate on the device side. It is the same key and certificate the party signs IM with, so the identity
/// TLS proves and the identity IM proves are one (DMP chapter 05, "Transport encryption").
/// </summary>
public sealed class ImTlsIdentity : IDisposable
{
    readonly bool owned;

    ImTlsIdentity(X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain, bool owned)
    {
        Certificate = certificate;
        Chain = chain;
        this.owned = owned;
        KeyId = ImKeys.KeyIdBytes(ImKeys.SubjectPublicKeyInfo(certificate));
    }

    /// <summary>The end-entity certificate, with its private key.</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>The intermediates (never the root), sent with the certificate so the other side needs no download.</summary>
    public IReadOnlyList<X509Certificate2> Chain { get; }

    /// <summary>SHA-256 of the SubjectPublicKeyInfo: the 32 bytes behind the KeyId.</summary>
    public byte[] KeyId { get; }

    /// <summary>
    /// The TLS identity of a signer whose certificate carries its private key. The key is moved through a password-less PKCS#12 blob
    /// held in memory, because Windows' TLS stack cannot use the ephemeral keys the sandbox authority creates; nothing is written to disk
    /// beyond what the platform does for a loaded PKCS#12 key, and that is removed when this identity is disposed.
    /// </summary>
    public static ImTlsIdentity Of(IImSigner signer)
    {
        ArgumentNullException.ThrowIfNull(signer);
        if (!signer.Certificate.HasPrivateKey) throw new ArgumentException("the signer's certificate carries no private key; TLS needs it", nameof(signer));
        var blob = signer.Certificate.Export(X509ContentType.Pkcs12);
        try
        {
            var loaded = X509CertificateLoader.LoadPkcs12(blob, password: null);
            return new ImTlsIdentity(loaded, signer.Chain, owned: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    /// <summary>
    /// The TLS identity of a certificate already joined to a key the platform's TLS stack can sign with, used as it is: on a device, a
    /// certificate joined to the non-exportable TPM key (the Platform Crypto Provider), which <see cref="Of"/> cannot export. The caller
    /// keeps owning <paramref name="certificateWithKey"/>; disposing this identity leaves it alone.
    /// </summary>
    public static ImTlsIdentity Bound(X509Certificate2 certificateWithKey, IReadOnlyList<X509Certificate2> chain)
    {
        ArgumentNullException.ThrowIfNull(certificateWithKey);
        ArgumentNullException.ThrowIfNull(chain);
        if (!certificateWithKey.HasPrivateKey) throw new ArgumentException("the certificate is not joined to a private key; TLS needs it", nameof(certificateWithKey));
        return new ImTlsIdentity(certificateWithKey, chain, owned: false);
    }

    internal SslStreamCertificateContext Context() =>
        SslStreamCertificateContext.Create(Certificate, new X509Certificate2Collection(Chain.ToArray()), offline: true);

    public void Dispose()
    {
        if (owned) Certificate.Dispose();
    }
}

/// <summary>
/// TLS for IM (DMP chapter 05, "Transport encryption"): TLS 1.3 only, both sides show a certificate from the new authority, and each side
/// checks the other's certificate itself through <see cref="ITrustAnchors"/> in the role it must carry (the gate: MilkyWayGate; the
/// device: Device). The platform's own chain verdict is ignored, the machine's certificate store is never consulted (so the old CA is not
/// trusted here either), and no certificate or revocation list is downloaded. The time of the check comes from the injected clock.
/// </summary>
public static class ImTls
{
    /// <summary>The one protocol version. There is no fallback; a peer that cannot speak TLS 1.3 does not connect.</summary>
    public const SslProtocols Protocols = SslProtocols.Tls13;

    /// <summary>
    /// The name the device asks for in TLS. It is a role word, never a host name (constitution C3): the gate is recognised by the role
    /// on its certificate, not by a name, so the name check of TLS is the one platform verdict the role check replaces.
    /// </summary>
    public const string ServerName = "milky-way";

    /// <summary>
    /// The device side. <paramref name="device"/> is null for a machine that has no Device certificate yet (enrolment): it reaches the
    /// knock and Doppler's enrolment door only, and the gate refuses its handshake response (DMP chapter 05, "Transport encryption").
    /// </summary>
    public static SslClientAuthenticationOptions ClientOptions(ImTlsIdentity? device, ITrustAnchors trust, TimeProvider time, Action<ChainVerdict?>? observe = null)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(time);
        return new SslClientAuthenticationOptions
        {
            TargetHost = ServerName,
            EnabledSslProtocols = Protocols,
            AllowRenegotiation = false,
            AllowTlsResume = false,
            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            CertificateChainPolicy = NoNetworkPolicy(),
            ClientCertificateContext = device?.Context(),
            RemoteCertificateValidationCallback = (_, certificate, chain, _) => Observe(certificate, Check(trust, certificate, chain, TrustRole.MilkyWayGate, time.GetUtcNow()), observe),
        };
    }

    /// <summary>
    /// The gate side. It asks for a client certificate and refuses one that is shown and not a Device certificate of the new authority;
    /// a client that shows none is let through TLS (for enrolment) and the gate session then allows it the knock and the door only.
    /// <paramref name="observe"/> hears the verdict, or null when no certificate was shown.
    /// </summary>
    public static SslServerAuthenticationOptions ServerOptions(ImTlsIdentity gate, ITrustAnchors trust, TimeProvider time, Action<ChainVerdict?>? observe = null)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(time);
        return new SslServerAuthenticationOptions
        {
            EnabledSslProtocols = Protocols,
            ClientCertificateRequired = true,
            AllowRenegotiation = false,
            AllowTlsResume = false,
            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            CertificateChainPolicy = NoNetworkPolicy(),
            ServerCertificateContext = gate.Context(),
            RemoteCertificateValidationCallback = (_, certificate, chain, _) =>
                certificate is null ? ObserveNone(observe) : Observe(certificate, Check(trust, certificate, chain, TrustRole.Device, time.GetUtcNow()), observe),
        };
    }

    static bool ObserveNone(Action<ChainVerdict?>? observe)
    {
        observe?.Invoke(null);
        return true;
    }

    /// <summary>
    /// The peer's certificate in the role it must carry, at <paramref name="at"/>. The intermediates are the certificates the peer sent
    /// (the platform puts them in the chain's extra store); the platform's verdict is not consulted. No certificate is <see cref="ChainRefusal.Malformed"/>.
    /// </summary>
    public static ChainVerdict Check(ITrustAnchors trust, X509Certificate? certificate, X509Chain? chain, TrustRole role, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(trust);
        if (certificate is not X509Certificate2 leaf) return new(ChainRefusal.Malformed);
        var intermediates = chain is null
            ? []
            : chain.ChainPolicy.ExtraStore.Where(c => !c.RawDataMemory.Span.SequenceEqual(leaf.RawDataMemory.Span)).ToArray();
        try
        {
            return trust.Validate(leaf, intermediates, role, at);
        }
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException or NotSupportedException or ArgumentException)
        {
            return new(ChainRefusal.Malformed);
        }
    }

    /// <summary>The 32 bytes behind the KeyId of the certificate the peer showed in TLS.</summary>
    public static byte[] KeyIdOf(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        using var copy = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        return ImKeys.KeyIdBytes(ImKeys.SubjectPublicKeyInfo(copy));
    }

    /// <summary>The platform builds a chain before it calls back; this policy keeps that build off the network and away from the machine's store.</summary>
    static X509ChainPolicy NoNetworkPolicy() => new()
    {
        TrustMode = X509ChainTrustMode.CustomRootTrust,
        DisableCertificateDownloads = true,
        RevocationMode = X509RevocationMode.NoCheck,
    };

    /// <summary>Tells the observer the verdict, or null when the peer showed no certificate at all, and answers the platform.</summary>
    static bool Observe(X509Certificate? certificate, ChainVerdict verdict, Action<ChainVerdict?>? observe)
    {
        observe?.Invoke(certificate is null ? null : verdict);
        return verdict.Ok;
    }
}

/// <summary>
/// The device side of an IM session over a stream: TLS 1.3 with the device certificate, then the IM handshake inside it, bound to the
/// TLS identities (the gate's certificate in the accept frame must be the one TLS showed). Doppler's agent uses it; it lives in
/// this library so the agent never references Milky Way. The stream comes from the caller (a socket in production, the simulator's
/// in-memory pipe in tests); this class opens nothing itself.
/// </summary>
public sealed class ImTlsClientConnection : IAsyncDisposable
{
    readonly SslStream tls;
    readonly ImTlsIdentity identity;
    readonly ImClientSession session;
    readonly int maxFrameBytes;

    ImTlsClientConnection(SslStream tls, ImTlsIdentity identity, ImClientSession session, int maxFrameBytes)
    {
        this.tls = tls;
        this.identity = identity;
        this.session = session;
        this.maxFrameBytes = maxFrameBytes;
    }

    public ImClientSession Session => session;

    /// <summary>What the TLS layer agreed (for tests and the health report: the version and the suite, never a key).</summary>
    public SslProtocols Protocol => tls.SslProtocol;

    public TlsCipherSuite CipherSuite => tls.NegotiatedCipherSuite;

    /// <summary>
    /// TLS, then knock, accept, response, established. Throws <see cref="ImClientException"/> with a code when the gate is not genuine,
    /// refuses or stays quiet; the transport is closed then. <paramref name="tlsIdentity"/> is the device's TLS identity when its key
    /// cannot be exported (<see cref="ImTlsIdentity.Bound"/>, a TPM key); it must hold the device key, and the caller keeps owning it.
    /// Without it the identity is made from <paramref name="device"/> (<see cref="ImTlsIdentity.Of"/>).
    /// </summary>
    public static async Task<ImTlsClientConnection> ConnectAsync(Stream transport, IImSigner device, ITrustAnchors trust, byte[] token, ReadOnlyMemory<byte> knockSecret,
        TimeProvider time, int maxFrameBytes, int maxPayload, CancellationToken cancellationToken, ImTlsIdentity? tlsIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(device);
        // The gate refuses a TLS certificate that is not the IM device certificate (TransportMismatch); say so here, before a byte is sent.
        if (tlsIdentity is not null && !CryptographicOperations.FixedTimeEquals(tlsIdentity.KeyId, ImKeys.KeyIdBytes(ImKeys.SubjectPublicKeyInfo(device.Certificate))))
            throw new ArgumentException("the TLS identity's key is not the device key", nameof(tlsIdentity));
        var identity = tlsIdentity ?? ImTlsIdentity.Of(device);
        var tls = new SslStream(transport, leaveInnerStreamOpen: false);
        try
        {
            try
            {
                await tls.AuthenticateAsClientAsync(ImTls.ClientOptions(identity, trust, time), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is AuthenticationException or IOException)
            {
                throw new ImClientException(ImClientError.TransportRefused);
            }

            var gateKeyId = tls.RemoteCertificate is { } gateCertificate ? ImTls.KeyIdOf(gateCertificate) : throw new ImClientException(ImClientError.TransportRefused);
            var session = new ImClientSession(trust, device, token, time, maxPayload, gateKeyId);
            var connection = new ImTlsClientConnection(tls, identity, session, maxFrameBytes);

            await connection.WriteAsync(session.Knock(knockSecret.Span), cancellationToken).ConfigureAwait(false);
            var accept = await connection.ReadOrThrowAsync(cancellationToken).ConfigureAwait(false);
            await connection.WriteAsync(session.Respond(accept), cancellationToken).ConfigureAwait(false);
            session.Established(await connection.ReadOrThrowAsync(cancellationToken).ConfigureAwait(false));
            return connection;
        }
        catch
        {
            await tls.DisposeAsync().ConfigureAwait(false);
            identity.Dispose();
            throw;
        }
    }

    /// <summary>Sends a heartbeat and waits for the ack. Throws <see cref="ImClientException"/> (<see cref="ImClientError.Refused"/>) when the gate refuses it.</summary>
    public async Task HeartbeatAsync(ImHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        await WriteAsync(session.Heartbeat(heartbeat), cancellationToken).ConfigureAwait(false);
        var reply = await ReadOrThrowAsync(cancellationToken).ConfigureAwait(false);
        if (ImFrames.TypeOf(reply) != ImFrameType.Ack) throw new ImClientException(ImClientError.OutOfOrder);
    }

    Task WriteAsync(byte[] frame, CancellationToken cancellationToken) => ImStreamFraming.WriteAsync(tls, frame, cancellationToken);

    async Task<byte[]> ReadOrThrowAsync(CancellationToken cancellationToken)
    {
        ImStreamRead read;
        try
        {
            read = await ImStreamFraming.ReadAsync(tls, maxFrameBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            throw new ImClientException(ImClientError.Closed);
        }
        if (read.Outcome != ImStreamOutcome.Frame) throw new ImClientException(ImClientError.Closed);
        if (ImFrames.TypeOf(read.Frame) == ImFrameType.Refused) throw new ImClientException(ImClientError.Refused);
        return read.Frame!;
    }

    public async ValueTask DisposeAsync()
    {
        await tls.DisposeAsync().ConfigureAwait(false);
        identity.Dispose();
    }
}
