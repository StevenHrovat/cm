using System.Net.Security;
using System.Security.Authentication;
using Im;
using Im.Trust;
using CM.MilkyWay.Gate;
using CM.MilkyWay.Verify;

namespace CM.MilkyWay.Transport;

/// <summary>
/// The gate on a stream (DMP chapter 05, "Transport encryption" and "Resource limits"): one call to <see cref="ServeAsync"/> per
/// connection. It admits the connection under <c>milkyway.im.maxConnections</c>, runs TLS 1.3 with the gate's MilkyWayGate certificate
/// and asks for the device's Device certificate (refusing a bad one; a connection without one counts against
/// <c>milkyway.im.maxUncertifiedConnections</c> and may only knock and use the door), then moves length-prefixed frames between the TLS stream and one
/// <see cref="GateSession"/>, bound to the device certificate TLS proved. It decides nothing about IM itself: every IM decision is the
/// session's and the verifier's, so the code that decides is the code the simulator tests. The stream comes from the caller (a socket
/// listener in wave 2, the simulator's in-memory pipe now); this class opens no port.
/// </summary>
public sealed class GateTransport : IDisposable
{
    readonly MilkyWayGate gate;
    readonly ImTlsIdentity identity;
    int active;
    int uncertified;

    public GateTransport(MilkyWayGate gate)
    {
        this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
        identity = ImTlsIdentity.Of(gate.GateKey);
    }

    /// <summary>Connections being served now.</summary>
    public int Active => Volatile.Read(ref active);

    /// <summary>Of those, connections whose TLS showed no device certificate.</summary>
    public int Uncertified => Volatile.Read(ref uncertified);

    /// <summary>
    /// Serves one connection until either side closes, a deadline passes or the session refuses. Never throws for anything the peer
    /// does; the outcome is in the refusal log. The handshake deadline (<c>milkyway.im.handshakeTimeout</c>, covering TLS and the IM
    /// handshake) is armed before this method first yields, and after each answered frame the idle deadline is armed before the answer
    /// is written, so a test that has seen an answer can move the clock without a race.
    /// </summary>
    public Task ServeAsync(Stream connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (Interlocked.Increment(ref active) > gate.Settings.MaxConnections)
        {
            Interlocked.Decrement(ref active);
            gate.Log.Refused(ImRefusal.ConnectionLimit);
            connection.Dispose();
            return Task.CompletedTask;
        }
        var deadline = new CancellationTokenSource(gate.Settings.HandshakeTimeout, gate.Time);
        return ServeAdmittedAsync(connection, deadline, cancellationToken);
    }

    async Task ServeAdmittedAsync(Stream connection, CancellationTokenSource deadline, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var tls = new SslStream(connection, leaveInnerStreamOpen: false);
        GateSession? session = null;
        var countedUncertified = false;
        try
        {
            ChainVerdict? peer = null;
            try
            {
                await tls.AuthenticateAsServerAsync(ImTls.ServerOptions(identity, gate.Trust, gate.Time, v => peer = v), linked.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                gate.Log.Refused(deadline.IsCancellationRequested ? ImRefusal.HandshakeTimeout : TransportReason(peer));
                return;
            }
            byte[]? deviceKeyId = null;
            if (tls.RemoteCertificate is { } deviceCertificate) deviceKeyId = ImTls.KeyIdOf(deviceCertificate);
            else
            {
                countedUncertified = true;
                if (Interlocked.Increment(ref uncertified) > gate.Settings.MaxUncertifiedConnections)
                {
                    gate.Log.Refused(ImRefusal.ConnectionLimit);
                    return;
                }
            }

            session = gate.Open(new TransportPeer(deviceKeyId));
            while (true)
            {
                ImStreamRead read;
                try
                {
                    read = await ImStreamFraming.ReadAsync(tls, gate.Settings.MaxFrameBytes, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    await AnswerAsync(tls, session.Abort(session.IsEstablished ? ImRefusal.IdleTimeout : ImRefusal.HandshakeTimeout)).ConfigureAwait(false);
                    return;
                }

                GateReply reply;
                switch (read.Outcome)
                {
                    case ImStreamOutcome.Closed:
                        return;
                    case ImStreamOutcome.Oversize:
                        reply = session.Abort(ImRefusal.Oversize);
                        break;
                    case ImStreamOutcome.Truncated:
                        reply = session.Abort(ImRefusal.Malformed);
                        break;
                    default:
                        reply = session.Receive(read.Frame);
                        break;
                }

                if (!reply.Close && session.IsEstablished) deadline.CancelAfter(gate.Settings.IdleTimeout);
                await AnswerAsync(tls, reply).ConfigureAwait(false);
                if (reply.Close) return;
            }
        }
        catch (Exception e) when (e is IOException or AuthenticationException or OperationCanceledException or ObjectDisposedException)
        {
            // The peer went away or the caller stopped the gate. A session that was still open is closed without a further count.
        }
        finally
        {
            await tls.DisposeAsync().ConfigureAwait(false);
            deadline.Dispose();
            if (countedUncertified) Interlocked.Decrement(ref uncertified);
            Interlocked.Decrement(ref active);
        }
    }

    static async Task AnswerAsync(SslStream tls, GateReply reply)
    {
        if (reply.Frame is { } frame) await ImStreamFraming.WriteAsync(tls, frame, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>The reason TLS failed, from what the certificate check saw: an expired certificate, a foreign or wrong-role one, none at all.</summary>
    static ImRefusal TransportReason(ChainVerdict? peer) => peer?.Refusal switch
    {
        null or ChainRefusal.None => ImRefusal.TransportRefused,
        ChainRefusal.Expired or ChainRefusal.NotYetValid => ImRefusal.TransportCertificateExpired,
        _ => ImRefusal.UntrustedDevice,
    };

    public void Dispose() => identity.Dispose();
}
