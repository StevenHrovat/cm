using System.Security.Cryptography;
using Im;
using Im.Trust;
using CM.MilkyWay.Verify;

namespace CM.MilkyWay.Gate;

/// <summary>Where verified messages go. The heartbeat board is the first consumer; it takes only <see cref="VerifiedMessage"/>.</summary>
public interface IVerifiedMessageSink
{
    void Deliver(VerifiedMessage message);
}

/// <summary>A sink that also wants the token of every established session (Milky Way's health report lists accepted token ids for Doppler).</summary>
public interface ITokenAcceptanceSink
{
    /// <summary>Lower-case hex of an accepted token's id.</summary>
    void TokenAccepted(string tokenId);
}

/// <summary>
/// What a gate session answers to one frame: a frame to send (or none), and whether to close the connection. The quiet answer is no
/// frame and close; a refusal is the refused frame and close.
/// </summary>
public readonly record struct GateReply(byte[]? Frame, bool Close)
{
    public static GateReply Quiet { get; } = new(null, true);

    public static GateReply Refused { get; } = new(ImFrames.Refused(), true);

    public bool IsQuiet => Frame is null && Close;
}

/// <summary>
/// Milky Way's gate, written without I/O (DMP chapter 05, "The gate"): a listener (wave 2) or the simulator opens one
/// <see cref="GateSession"/> per connection and passes it whole frames. The gate holds what all sessions share: the settings, the
/// gate key, the trust anchors, the verifier with its replay memory, and the memory of knock nonces.
/// </summary>
public sealed class MilkyWayGate
{
    /// <param name="door">Doppler's enrolment door, or null for a gate without one (door frames are then refused as <see cref="ImRefusal.DoorClosed"/>).</param>
    public MilkyWayGate(MilkyWaySettings settings, IImSigner gateKey, ITrustAnchors trust, IEnrolmentRegistry enrolment, TimeProvider time, IImRefusalLog log, IVerifiedMessageSink sink, IImDoor? door = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        Settings = settings;
        GateKey = gateKey ?? throw new ArgumentNullException(nameof(gateKey));
        Trust = trust ?? throw new ArgumentNullException(nameof(trust));
        Time = time ?? throw new ArgumentNullException(nameof(time));
        Log = log ?? throw new ArgumentNullException(nameof(log));
        Sink = sink ?? throw new ArgumentNullException(nameof(sink));
        Verifier = new ImVerifier(settings, trust, enrolment, time, log);
        Knocks = new ReplayMemory(settings.ReplayCapacity);
        Door = door;
    }

    internal MilkyWaySettings Settings { get; }
    internal IImSigner GateKey { get; }
    internal ITrustAnchors Trust { get; }
    internal TimeProvider Time { get; }
    internal IImRefusalLog Log { get; }
    internal IVerifiedMessageSink Sink { get; }
    /// <summary>The verifier all sessions share (and the internal proxy will use directly, outside a session).</summary>
    public ImVerifier Verifier { get; }
    internal ReplayMemory Knocks { get; }
    internal IImDoor? Door { get; }

    /// <summary>
    /// A new session for a new connection; the handshake deadline starts now. <paramref name="transport"/> is what TLS proved about the
    /// device when the session runs over TLS (<see cref="Transport.GateTransport"/>): the device certificate in the response must be that one.
    /// The simulator's frame-only connections pass none.
    /// </summary>
    public GateSession Open(TransportPeer? transport = null) => new(this, Time.GetUtcNow() + Settings.HandshakeTimeout, transport);
}

/// <summary>
/// What the transport proved about the device before the first IM frame: the KeyId (32 bytes) of its TLS client certificate, or null
/// when it showed none (a machine before enrolment: it may knock and use Doppler's door, never respond).
/// </summary>
public sealed record TransportPeer(byte[]? DeviceKeyId);

/// <summary>
/// One connection's session: waiting for the knock, waiting for the response, established, closed (DMP chapter 04, "The handshake").
/// A frame that does not fit the state, and the first refusal, close the session.
/// </summary>
public sealed class GateSession
{
    enum State { AwaitKnock, AwaitResponse, Established, Closed }

    static readonly byte[] KnockScope = new byte[32];

    readonly MilkyWayGate gate;
    readonly DateTimeOffset deadline;
    readonly TransportPeer? transport;
    State state = State.AwaitKnock;
    byte[]? knockFrame;
    byte[]? acceptFrame;
    int doorFrames;

    internal GateSession(MilkyWayGate gate, DateTimeOffset deadline, TransportPeer? transport)
    {
        this.gate = gate;
        this.deadline = deadline;
        this.transport = transport;
    }

    public bool IsEstablished => state == State.Established;

    public bool IsClosed => state == State.Closed;

    /// <summary>The opaque subject of an established session.</summary>
    public string? Subject { get; private set; }

    public GateReply Receive(ReadOnlyMemory<byte> frame)
    {
        if (state == State.Closed) return GateReply.Quiet;
        try
        {
            var before = state;
            var reply = Handle(frame);
            if (reply.Close) state = State.Closed;
            return before == State.AwaitKnock && reply.Close ? GateReply.Quiet : reply;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Fail closed; the exception is dropped, never logged.
            var quiet = state == State.AwaitKnock;
            state = State.Closed;
            gate.Log.Refused(ImRefusal.Malformed);
            return quiet ? GateReply.Quiet : GateReply.Refused;
        }
    }

    /// <summary>
    /// Ends the session for a reason the transport found before a frame could be handed over (an oversize prefix, a truncated frame, a
    /// deadline). Counted once; the answer follows the same rule as <see cref="Receive"/>: nothing before a valid knock, refused after.
    /// </summary>
    public GateReply Abort(ImRefusal reason)
    {
        if (state == State.Closed) return GateReply.Quiet;
        var quiet = state == State.AwaitKnock;
        state = State.Closed;
        gate.Log.Refused(reason);
        return quiet ? GateReply.Quiet : GateReply.Refused;
    }

    GateReply Handle(ReadOnlyMemory<byte> frame)
    {
        var now = gate.Time.GetUtcNow();
        if (frame.Length > gate.Settings.MaxFrameBytes) return Refuse(ImRefusal.Oversize);
        if (state != State.Established && now > deadline) return Refuse(ImRefusal.HandshakeTimeout);
        return state switch
        {
            State.AwaitKnock => OnKnock(frame, now),
            State.AwaitResponse => OnResponse(frame, now),
            _ => OnMessage(frame),
        };
    }

    GateReply OnKnock(ReadOnlyMemory<byte> frame, DateTimeOffset now)
    {
        // Every failure here becomes the quiet answer in Receive.
        if (frame.Length != ImFrames.KnockLength || frame.Span[0] != (byte)ImFrameType.Knock) return Refuse(ImRefusal.KnockRefused);
        var knock = ImFrames.ParseKnock(frame);
        var nowMs = ImTime.ToWire(now);
        var skew = (long)gate.Settings.ClockSkew.TotalMilliseconds;

        var macOk = false;
        foreach (var secret in gate.Settings.KnockSecrets)
            macOk |= CryptographicOperations.FixedTimeEquals(ImFrames.KnockMac(secret, knock.ClientNonce, knock.SentAt), knock.Mac);
        if (!macOk) return Refuse(ImRefusal.KnockRefused);
        if (knock.SentAt < nowMs - skew || knock.SentAt > nowMs + skew) return Refuse(ImRefusal.KnockStale);

        // After the MAC, so nobody without the secret can fill the knock memory.
        switch (gate.Knocks.TryRemember(KnockScope, knock.ClientNonce, knock.SentAt + skew, nowMs))
        {
            case ReplayOutcome.Seen: return Refuse(ImRefusal.KnockReplayed);
            case ReplayOutcome.Full: return Refuse(ImRefusal.ReplayCacheFull);
        }

        knockFrame = frame.ToArray();
        acceptFrame = ImFrames.Accept(knockFrame, RandomNumberGenerator.GetBytes(ImFrames.ChallengeLength), RandomNumberGenerator.GetBytes(ImFrames.SessionIdLength), gate.GateKey);
        state = State.AwaitResponse;
        return new GateReply(acceptFrame, false);
    }

    /// <summary>
    /// The enrolment door (DMP chapter 04): after the accept and before the response, a door payload goes to Doppler unread, and
    /// Doppler's answer comes back. Nothing from the door is verified, delivered or put on the board; the session stays waiting for
    /// the response, and the handshake deadline keeps running.
    /// </summary>
    GateReply OnDoor(ReadOnlyMemory<byte> frame)
    {
        if (gate.Door is not { } door) return Refuse(ImRefusal.DoorClosed);
        if (++doorFrames > gate.Settings.MaxDoorFrames) return Refuse(ImRefusal.DoorLimit);
        byte[] payload;
        try { payload = ImFrames.ParseDoor(frame, gate.Settings.MaxDoorBytes); }
        catch (ImFormatException) { return Refuse(ImRefusal.Malformed); }
        var answer = door.Answer(payload);
        if (answer is null || answer.Length > gate.Settings.MaxDoorBytes) return Refuse(ImRefusal.DoorRefused);
        gate.Log.Accepted(ImAccepted.DoorFrame);
        return new GateReply(ImFrames.DoorReply(answer), false);
    }

    GateReply OnResponse(ReadOnlyMemory<byte> frame, DateTimeOffset now)
    {
        if (!frame.IsEmpty && frame.Span[0] == (byte)ImFrameType.Door) return OnDoor(frame);
        if (frame.IsEmpty || frame.Span[0] != (byte)ImFrameType.Response) return Refuse(ImRefusal.OutOfOrder);
        if (transport is { DeviceKeyId: null }) return Refuse(ImRefusal.TransportNoCertificate);
        ImResponse response;
        ImToken token;
        try
        {
            response = ImFrames.ParseResponse(frame);
            token = ImTokenCodec.Parse(response.Token);
        }
        catch (ImFormatException e)
        {
            return Refuse(e.Error == ImFormatError.UnknownVersion ? ImRefusal.UnknownVersion : ImRefusal.Malformed);
        }

        using var device = ImChains.Load(response.DeviceChain);
        var verdict = gate.Trust.Validate(device.EndEntity, device.Intermediates, TrustRole.Device, now);
        if (!verdict.Ok) return Refuse(verdict.Refusal is ChainRefusal.Expired or ChainRefusal.NotYetValid ? ImRefusal.CertificateExpired : ImRefusal.UntrustedDevice);

        if (gate.Verifier.CheckToken(token, now) is { } tokenRefusal) return Refuse(tokenRefusal);

        var deviceKey = ImKeys.SubjectPublicKeyInfo(device.EndEntity);
        if (!CryptographicOperations.FixedTimeEquals(ImKeys.KeyIdBytes(deviceKey), token.SubjectKeyId)) return Refuse(ImRefusal.DeviceKeyMismatch);
        if (transport is { DeviceKeyId: { } shown } && !CryptographicOperations.FixedTimeEquals(ImKeys.KeyIdBytes(deviceKey), shown)) return Refuse(ImRefusal.TransportMismatch);

        var input = ImFrames.ResponseInput(knockFrame!, acceptFrame!, response.DeviceChain, response.Token);
        if (!ImKeys.Verify(deviceKey, input, response.Signature)) return Refuse(ImRefusal.BadResponseSignature);

        if (!token.HasPermission(ImKinds.SessionPermission)) return Refuse(ImRefusal.WrongPermission);

        Subject = token.Subject;
        state = State.Established;
        gate.Log.Accepted(ImAccepted.Session);
        if (gate.Sink is ITokenAcceptanceSink tokens) tokens.TokenAccepted(Convert.ToHexStringLower(token.TokenId));
        return new GateReply(ImFrames.Established(ImFrames.ParseAccept(acceptFrame!).SessionId), false);
    }

    GateReply OnMessage(ReadOnlyMemory<byte> frame)
    {
        if (frame.IsEmpty || frame.Span[0] != (byte)ImFrameType.Message) return Refuse(ImRefusal.OutOfOrder);
        var result = gate.Verifier.Verify(ImFrames.MessageBody(frame), Subject); // the verifier counts its own outcome
        if (result.Message is not { } message) return GateReply.Refused;
        gate.Sink.Deliver(message);
        return new GateReply(ImFrames.Ack(message.MessageId), false);
    }

    GateReply Refuse(ImRefusal reason)
    {
        gate.Log.Refused(reason);
        return GateReply.Refused;
    }
}
