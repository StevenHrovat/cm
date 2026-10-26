using System.Security.Cryptography;
using Im.Trust;

namespace Im;

/// <summary>The first byte of every frame (DMP chapter 04, "The handshake").</summary>
public enum ImFrameType : byte
{
    Knock = 0x01,
    Accept = 0x02,
    Response = 0x03,
    Established = 0x04,
    Message = 0x10,
    Ack = 0x11,
    /// <summary>No content: the gate never says why.</summary>
    Refused = 0x7F,
    /// <summary>Device to gate, after the accept and before the response: an opaque payload for Doppler (DMP chapter 04, "The enrolment door").</summary>
    Door = 0x20,
    /// <summary>Gate to device: Doppler's opaque answer to a door frame.</summary>
    DoorReply = 0x21,
}

public sealed record ImKnock(byte[] ClientNonce, long SentAt, byte[] Mac);

public sealed record ImAccept(byte[] Challenge, byte[] SessionId, IReadOnlyList<byte[]> GateChain, byte[] Signature);

public sealed record ImResponse(IReadOnlyList<byte[]> DeviceChain, byte[] Token, byte[] Signature);

/// <summary>Builds and parses the frames. Every parser is strict and throws <see cref="ImFormatException"/>.</summary>
public static class ImFrames
{
    /// <summary>A knock is exactly this long: type, 32-byte nonce, 8-byte time, 32-byte MAC.</summary>
    public const int KnockLength = 1 + 32 + 8 + 32;

    public const int ChallengeLength = 32;
    public const int SessionIdLength = 16;

    public static ImFrameType TypeOf(ReadOnlySpan<byte> frame) =>
        frame.IsEmpty ? throw new ImFormatException(ImFormatError.Malformed) : (ImFrameType)frame[0];

    // ---- knock --------------------------------------------------------------------------------------------------

    public static byte[] Knock(ReadOnlySpan<byte> sharedSecret, DateTimeOffset sentAt)
    {
        var nonce = RandomNumberGenerator.GetBytes(32);
        var time = ImTime.ToWire(sentAt);
        return new ImWriter().U8((byte)ImFrameType.Knock).Fixed(nonce, 32).I64(time).Fixed(KnockMac(sharedSecret, nonce, time), 32).ToArray();
    }

    public static byte[] KnockMac(ReadOnlySpan<byte> sharedSecret, byte[] clientNonce, long sentAt) =>
        HMACSHA256.HashData(sharedSecret, ImTags.Input(ImTags.Knock, clientNonce, new ImWriter().I64(sentAt).ToArray()));

    public static ImKnock ParseKnock(ReadOnlyMemory<byte> frame)
    {
        if (frame.Length != KnockLength) throw new ImFormatException(ImFormatError.Malformed);
        var reader = new ImReader(frame);
        if (reader.U8() != (byte)ImFrameType.Knock) throw new ImFormatException(ImFormatError.Malformed);
        var knock = new ImKnock(reader.Fixed(32), reader.I64(), reader.Fixed(32));
        reader.End();
        return knock;
    }

    // ---- accept -------------------------------------------------------------------------------------------------

    public static byte[] Accept(byte[] knockFrame, byte[] challenge, byte[] sessionId, IImSigner gate)
    {
        var chain = ImChains.Of(gate);
        var signature = ImKeys.Sign(gate, AcceptInput(knockFrame, challenge, sessionId, chain));
        return new ImWriter()
            .U8((byte)ImFrameType.Accept)
            .Fixed(challenge, ChallengeLength)
            .Fixed(sessionId, SessionIdLength)
            .List16(chain, ImLimits.ChainMax, ImLimits.CertificateMax)
            .Fixed(signature, ImLimits.SignatureLength)
            .ToArray();
    }

    public static byte[] AcceptInput(byte[] knockFrame, byte[] challenge, byte[] sessionId, IReadOnlyList<byte[]> gateChain) =>
        ImTags.Input(ImTags.Accept, SHA256.HashData(knockFrame), challenge, sessionId,
            new ImWriter().List16(gateChain, ImLimits.ChainMax, ImLimits.CertificateMax).ToArray());

    public static ImAccept ParseAccept(ReadOnlyMemory<byte> frame)
    {
        var reader = new ImReader(frame);
        if (reader.U8() != (byte)ImFrameType.Accept) throw new ImFormatException(ImFormatError.Malformed);
        var accept = new ImAccept(reader.Fixed(ChallengeLength), reader.Fixed(SessionIdLength), reader.List16(ImLimits.ChainMax, ImLimits.CertificateMax), reader.Fixed(ImLimits.SignatureLength));
        reader.End();
        return accept;
    }

    // ---- response -----------------------------------------------------------------------------------------------

    public static byte[] Response(byte[] knockFrame, byte[] acceptFrame, byte[] token, IImSigner device)
    {
        ArgumentNullException.ThrowIfNull(token);
        var chain = ImChains.Of(device);
        var signature = ImKeys.Sign(device, ResponseInput(knockFrame, acceptFrame, chain, token));
        return new ImWriter()
            .U8((byte)ImFrameType.Response)
            .List16(chain, ImLimits.ChainMax, ImLimits.CertificateMax)
            .Bytes32(token, ImLimits.TokenMax + 4 + ImLimits.SignatureLength)
            .Fixed(signature, ImLimits.SignatureLength)
            .ToArray();
    }

    public static byte[] ResponseInput(byte[] knockFrame, byte[] acceptFrame, IReadOnlyList<byte[]> deviceChain, byte[] token) =>
        ImTags.Input(ImTags.Response, SHA256.HashData(knockFrame), SHA256.HashData(acceptFrame),
            new ImWriter().List16(deviceChain, ImLimits.ChainMax, ImLimits.CertificateMax).ToArray(), token);

    public static ImResponse ParseResponse(ReadOnlyMemory<byte> frame)
    {
        var reader = new ImReader(frame);
        if (reader.U8() != (byte)ImFrameType.Response) throw new ImFormatException(ImFormatError.Malformed);
        var response = new ImResponse(reader.List16(ImLimits.ChainMax, ImLimits.CertificateMax), reader.Bytes32(ImLimits.TokenMax + 4 + ImLimits.SignatureLength), reader.Fixed(ImLimits.SignatureLength));
        reader.End();
        return response;
    }

    // ---- established, message, ack, refused ---------------------------------------------------------------------

    public static byte[] Established(byte[] sessionId) => new ImWriter().U8((byte)ImFrameType.Established).Fixed(sessionId, SessionIdLength).ToArray();

    public static byte[] ParseEstablished(ReadOnlyMemory<byte> frame)
    {
        var reader = new ImReader(frame);
        if (reader.U8() != (byte)ImFrameType.Established) throw new ImFormatException(ImFormatError.Malformed);
        var sessionId = reader.Fixed(SessionIdLength);
        reader.End();
        return sessionId;
    }

    public static byte[] Message(byte[] signedEnvelope) => new ImWriter().U8((byte)ImFrameType.Message).Raw(signedEnvelope).ToArray();

    /// <summary>The signed envelope inside a message frame.</summary>
    public static ReadOnlyMemory<byte> MessageBody(ReadOnlyMemory<byte> frame) =>
        frame.Length > 1 && frame.Span[0] == (byte)ImFrameType.Message ? frame[1..] : throw new ImFormatException(ImFormatError.Malformed);

    public static byte[] Ack(byte[] messageId) => new ImWriter().U8((byte)ImFrameType.Ack).Fixed(messageId, 16).ToArray();

    public static byte[] ParseAck(ReadOnlyMemory<byte> frame)
    {
        var reader = new ImReader(frame);
        if (reader.U8() != (byte)ImFrameType.Ack) throw new ImFormatException(ImFormatError.Malformed);
        var id = reader.Fixed(16);
        reader.End();
        return id;
    }

    public static byte[] Refused() => [(byte)ImFrameType.Refused];

    // ---- the enrolment door -------------------------------------------------------------------------------------

    /// <summary>A door frame: type, then <c>bytes32</c> payload. Milky Way never reads the payload; Doppler does.</summary>
    public static byte[] Door(ReadOnlySpan<byte> payload) =>
        new ImWriter().U8((byte)ImFrameType.Door).Bytes32(payload, ImLimits.DoorMax).ToArray();

    public static byte[] ParseDoor(ReadOnlyMemory<byte> frame, int maxPayload) => ParseDoorFrame(frame, ImFrameType.Door, maxPayload);

    public static byte[] DoorReply(ReadOnlySpan<byte> payload) =>
        new ImWriter().U8((byte)ImFrameType.DoorReply).Bytes32(payload, ImLimits.DoorMax).ToArray();

    public static byte[] ParseDoorReply(ReadOnlyMemory<byte> frame) => ParseDoorFrame(frame, ImFrameType.DoorReply, ImLimits.DoorMax);

    static byte[] ParseDoorFrame(ReadOnlyMemory<byte> frame, ImFrameType type, int maxPayload)
    {
        var reader = new ImReader(frame);
        if (reader.U8() != (byte)type) throw new ImFormatException(ImFormatError.Malformed);
        var payload = reader.Bytes32(Math.Min(maxPayload, ImLimits.DoorMax));
        reader.End();
        return payload;
    }
}

/// <summary>Why the client side gave up on a gate. A code only.</summary>
public enum ImClientError
{
    /// <summary>The accept frame did not parse.</summary>
    Malformed,
    /// <summary>The gate's certificate does not validate in the role <see cref="TrustRole.MilkyWayGate"/>.</summary>
    UntrustedGate,
    /// <summary>The gate's signature over the knock and the challenge is wrong.</summary>
    BadGateSignature,
    /// <summary>The established frame names another session.</summary>
    WrongSession,
    /// <summary>A frame arrived out of order.</summary>
    OutOfOrder,
    /// <summary>The gate answered a door frame with refused (it never says why) or closed the connection.</summary>
    DoorRefused,
    /// <summary>TLS did not come up: the gate's TLS certificate is not a MilkyWayGate certificate of the new authority, or the gate refused ours.</summary>
    TransportRefused,
    /// <summary>The gate certificate in the accept frame is not the one the gate showed in TLS.</summary>
    TransportMismatch,
    /// <summary>The gate answered with the refused frame (it never says why).</summary>
    Refused,
    /// <summary>The gate closed the connection without an answer (the quiet answer, or a deadline).</summary>
    Closed,
}

public sealed class ImClientException(ImClientError error) : Exception(error.ToString())
{
    public ImClientError Error { get; } = error;
}

/// <summary>
/// The client side of one IM session: knock, check the gate, respond, then send messages. Doppler's agent and the Pictor shell use
/// it; it lives in this library so they never reference Milky Way. Time comes from the injected <see cref="TimeProvider"/>.
/// <paramref name="transportGateKeyId"/> is the KeyId (32 bytes) of the certificate the gate showed in TLS, when the session runs over
/// TLS: the gate certificate in the accept frame must be that one (DMP chapter 05, "Transport encryption").
/// </summary>
public sealed class ImClientSession(ITrustAnchors trust, IImSigner? device, byte[]? token, TimeProvider time, int maxPayload = 4096, byte[]? transportGateKeyId = null)
{
    byte[]? knock;
    byte[]? accept;
    byte[]? sessionId;

    /// <summary>A session for a machine that has no device key or token yet: it may only use the enrolment door until <see cref="Respond(byte[], IImSigner)"/>.</summary>
    public ImClientSession(ITrustAnchors trust, TimeProvider time, int maxPayload = 4096) : this(trust, null, null, time, maxPayload)
    {
    }

    /// <summary>True once the gate's accept frame has been checked (<see cref="CheckGate"/>).</summary>
    public bool GateChecked => accept is not null;

    public bool IsEstablished { get; private set; }

    public byte[] Knock(ReadOnlySpan<byte> sharedSecret)
    {
        if (knock is not null) throw new ImClientException(ImClientError.OutOfOrder);
        knock = ImFrames.Knock(sharedSecret, time.GetUtcNow());
        return knock;
    }

    /// <summary>Checks the gate (the gate's half of the key signing) and answers the challenge with the key and token given at construction.</summary>
    public byte[] Respond(byte[] acceptFrame)
    {
        CheckGate(acceptFrame);
        return Response();
    }

    /// <summary>Answers the challenge with a key and token obtained after the accept (through the door). <see cref="CheckGate"/> must have passed.</summary>
    public byte[] Respond(byte[] newToken, IImSigner newDevice)
    {
        ArgumentNullException.ThrowIfNull(newToken);
        ArgumentNullException.ThrowIfNull(newDevice);
        token = newToken;
        device = newDevice;
        return Response();
    }

    /// <summary>A door frame carrying <paramref name="payload"/> to Doppler. Only after the gate was checked and before the response.</summary>
    public byte[] Door(ReadOnlySpan<byte> payload)
    {
        if (accept is null || IsEstablished || responded) throw new ImClientException(ImClientError.OutOfOrder);
        return ImFrames.Door(payload);
    }

    /// <summary>Doppler's answer inside a door reply frame; a refused frame (or anything else) is <see cref="ImClientError.DoorRefused"/>.</summary>
    public static byte[] ReadDoorReply(byte[]? frame)
    {
        if (frame is null || frame.Length == 0 || frame[0] != (byte)ImFrameType.DoorReply) throw new ImClientException(ImClientError.DoorRefused);
        try { return ImFrames.ParseDoorReply(frame); }
        catch (ImFormatException) { throw new ImClientException(ImClientError.Malformed); }
    }

    bool responded;

    byte[] Response()
    {
        if (accept is null || responded) throw new ImClientException(ImClientError.OutOfOrder);
        if (device is null || token is null) throw new ImClientException(ImClientError.OutOfOrder);
        responded = true;
        return ImFrames.Response(knock!, accept, token, device);
    }

    /// <summary>Checks the gate's accept frame: its chain in the role MilkyWayGate and its signature over our knock. Nothing is sent to a gate that fails.</summary>
    public void CheckGate(byte[] acceptFrame)
    {
        ArgumentNullException.ThrowIfNull(acceptFrame);
        if (knock is null || accept is not null) throw new ImClientException(ImClientError.OutOfOrder);
        ImAccept parsed;
        try { parsed = ImFrames.ParseAccept(acceptFrame); }
        catch (ImFormatException) { throw new ImClientException(ImClientError.Malformed); }

        using (var chain = LoadOrRefuse(parsed.GateChain))
        {
            if (!trust.Validate(chain.EndEntity, chain.Intermediates, TrustRole.MilkyWayGate, time.GetUtcNow()).Ok) throw new ImClientException(ImClientError.UntrustedGate);
            if (transportGateKeyId is not null && !CryptographicOperations.FixedTimeEquals(ImKeys.KeyIdBytes(ImKeys.SubjectPublicKeyInfo(chain.EndEntity)), transportGateKeyId))
                throw new ImClientException(ImClientError.TransportMismatch);
            var input = ImFrames.AcceptInput(knock, parsed.Challenge, parsed.SessionId, parsed.GateChain);
            if (!ImKeys.Verify(ImKeys.SubjectPublicKeyInfo(chain.EndEntity), input, parsed.Signature)) throw new ImClientException(ImClientError.BadGateSignature);
        }
        accept = acceptFrame;
        sessionId = parsed.SessionId;
    }

    public void Established(byte[] frame)
    {
        if (accept is null || IsEstablished) throw new ImClientException(ImClientError.OutOfOrder);
        byte[] id;
        try { id = ImFrames.ParseEstablished(frame); }
        catch (ImFormatException) { throw new ImClientException(ImClientError.Malformed); }
        if (!CryptographicOperations.FixedTimeEquals(id, sessionId)) throw new ImClientException(ImClientError.WrongSession);
        IsEstablished = true;
    }

    /// <summary>A message frame of any kind, sealed with the device key and the session's token.</summary>
    public byte[] Message(string kind, ReadOnlySpan<byte> payload)
    {
        if (!IsEstablished || device is null || token is null) throw new ImClientException(ImClientError.OutOfOrder);
        return ImFrames.Message(ImEnvelopeCodec.Seal(kind, payload, token, device, time.GetUtcNow(), maxPayload));
    }

    public byte[] Heartbeat(ImHeartbeat heartbeat)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        return Message(ImKinds.Heartbeat, heartbeat.Encode());
    }

    static LoadedChain LoadOrRefuse(IReadOnlyList<byte[]> chain)
    {
        try { return ImChains.Load(chain); }
        catch (ImFormatException) { throw new ImClientException(ImClientError.Malformed); }
    }
}
