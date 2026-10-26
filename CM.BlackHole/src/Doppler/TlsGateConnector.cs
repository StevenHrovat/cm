using System.Net.Security;
using Im;
using Im.Trust;

namespace CM.BlackHole.Doppler;

/// <summary>
/// The agent's connections to Milky Way's gate over TLS 1.3 (DMP chapter 05, "Transport encryption"; cm.os <see cref="ImTls"/>). The
/// door connection shows no certificate (a machine that is not enrolled has none; the gate allows it the knock and the door only), and
/// the accept frame must name the gate certificate TLS showed. The message session shows the Device certificate through
/// <paramref name="tlsIdentity"/>: on Windows the certificate joined to the TPM key (<c>PcpTpm.TlsCertificate</c>), so the key never
/// leaves the TPM. <paramref name="connect"/> opens the byte stream: a TCP connection to the gate in the service, an in-memory pipe to
/// the gate's transport in tests.
/// </summary>
public sealed class TlsGateConnector(Func<Stream> connect, ITrustAnchors trust, byte[] knockSecret, TimeProvider time,
    Func<IImSigner, ImTlsIdentity?> tlsIdentity, int maxFrameBytes = 65536, int maxPayloadBytes = 4096) : IGateConnector
{
    /// <summary>How long one connection may take to answer; the gate's own handshake deadline is shorter.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public DoorChannel OpenDoor()
    {
        var stream = connect();
        SslStream? tls = null;
        try
        {
            tls = new SslStream(stream, leaveInnerStreamOpen: false);
            using (var cancel = Deadline())
            {
                try { tls.AuthenticateAsClientAsync(ImTls.ClientOptions(null, trust, time), cancel.Token).GetAwaiter().GetResult(); }
                catch (Exception e) when (e is System.Security.Authentication.AuthenticationException or IOException or OperationCanceledException)
                {
                    throw new ImClientException(ImClientError.TransportRefused);
                }
            }
            var gateKeyId = tls.RemoteCertificate is { } gate ? ImTls.KeyIdOf(gate) : throw new ImClientException(ImClientError.TransportRefused);
            var session = new ImClientSession(trust, null, null, time, maxPayloadBytes, gateKeyId);
            return new DoorChannel(new FramedLink(tls, maxFrameBytes, Timeout, time), session, knockSecret);
        }
        catch
        {
            if (tls is not null) tls.Dispose();
            else stream.Dispose();
            throw;
        }
    }

    public IGateSession OpenSession(IImSigner device, byte[] token)
    {
        var stream = connect();
        using var cancel = Deadline();
        var connection = ImTlsClientConnection.ConnectAsync(stream, device, trust, token, knockSecret, time, maxFrameBytes, maxPayloadBytes,
            cancel.Token, tlsIdentity(device)).GetAwaiter().GetResult();
        return new Session(connection, Timeout, time);
    }

    CancellationTokenSource Deadline() => new(Timeout, time);

    /// <summary>One frame out, one frame back, length-prefixed (<see cref="ImStreamFraming"/>); null when the gate closed without a frame.</summary>
    sealed class FramedLink(Stream stream, int maxFrameBytes, TimeSpan timeout, TimeProvider time) : IGateLink
    {
        public byte[]? Exchange(byte[] frame)
        {
            using var cancel = new CancellationTokenSource(timeout, time);
            try
            {
                ImStreamFraming.WriteAsync(stream, frame, cancel.Token).GetAwaiter().GetResult();
                var read = ImStreamFraming.ReadAsync(stream, maxFrameBytes, cancel.Token).GetAwaiter().GetResult();
                return read.Outcome == ImStreamOutcome.Frame ? read.Frame : null;
            }
            catch (Exception e) when (e is IOException or OperationCanceledException) { return null; }
        }

        public void Dispose() => stream.Dispose();
    }

    sealed class Session(ImTlsClientConnection connection, TimeSpan timeout, TimeProvider time) : IGateSession
    {
        public void Heartbeat(ImHeartbeat heartbeat)
        {
            using var cancel = new CancellationTokenSource(timeout, time);
            try { connection.HeartbeatAsync(heartbeat, cancel.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { throw new ImClientException(ImClientError.Closed); }
        }

        public void Dispose() => connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
