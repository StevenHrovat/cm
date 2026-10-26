using System.Security.Cryptography;
using Im.Trust;

namespace Im;

/// <summary>An envelope as parsed from the wire (DMP chapter 04, "The envelope"). Parsing checks the shape only.</summary>
public sealed class ImEnvelope
{
    internal ImEnvelope(byte[] body, byte[] signature, string kind, byte[] messageId, long sentAt, byte[] token, byte[] payload)
    {
        Body = body;
        Signature = signature;
        Kind = kind;
        MessageId = messageId;
        SentAt = sentAt;
        Token = token;
        Payload = payload;
    }

    /// <summary>The signed body; the envelope signature covers <see cref="ImTags.Message"/> and these bytes.</summary>
    public byte[] Body { get; }

    public byte[] Signature { get; }

    public string Kind { get; }

    public byte[] MessageId { get; }

    /// <summary>Unix milliseconds, the sender's clock.</summary>
    public long SentAt { get; }

    /// <summary>The sender's token, unparsed.</summary>
    public byte[] Token { get; }

    public byte[] Payload { get; }
}

/// <summary>Seals and parses envelopes.</summary>
public static class ImEnvelopeCodec
{
    const string Magic = "IMM";
    const byte Version = 0;

    /// <summary>
    /// Builds and signs an envelope. <paramref name="device"/> must hold the key the token names; <paramref name="sentAt"/> is the
    /// sender's time, passed in.
    /// </summary>
    public static byte[] Seal(string kind, ReadOnlySpan<byte> payload, byte[] token, IImSigner device, DateTimeOffset sentAt, int maxPayload)
    {
        ArgumentNullException.ThrowIfNull(token);
        var body = new ImWriter()
            .Magic(Magic, Version)
            .Id8(kind, ImAlphabet.Name, ImLimits.NameMax)
            .Fixed(RandomNumberGenerator.GetBytes(16), 16)
            .I64(ImTime.ToWire(sentAt))
            .Bytes32(token, ImLimits.TokenMax + 4 + ImLimits.SignatureLength)
            .Bytes32(payload, maxPayload)
            .ToArray();
        var signature = ImKeys.Sign(device, ImTags.Input(ImTags.Message, body));
        return new ImWriter().Bytes32(body, int.MaxValue).Fixed(signature, ImLimits.SignatureLength).ToArray();
    }

    /// <summary>Parses a signed envelope strictly (verify rule 2). Throws <see cref="ImFormatException"/>; never checks a signature.</summary>
    public static ImEnvelope Parse(ReadOnlyMemory<byte> signed, int maxPayload)
    {
        var outer = new ImReader(signed);
        var body = outer.Bytes32(signed.Length);
        var signature = outer.Fixed(ImLimits.SignatureLength);
        outer.End();

        var reader = new ImReader(body);
        reader.Magic(Magic, Version);
        var kind = reader.Id8(ImAlphabet.Name, ImLimits.NameMax);
        var messageId = reader.Fixed(16);
        var sentAt = reader.I64();
        var token = reader.Bytes32(ImLimits.TokenMax + 4 + ImLimits.SignatureLength);
        var payload = reader.Bytes32(maxPayload);
        reader.End();
        return new ImEnvelope(body, signature, kind, messageId, sentAt, token, payload);
    }
}

/// <summary>A device's state in a heartbeat. Any other byte on the wire is malformed.</summary>
public enum ImHeartbeatStatus : byte
{
    Healthy = 0,
    Degraded = 1,
    Failing = 2,
}

/// <summary>The heartbeat payload (DMP chapter 04, "The heartbeat message"). No host name, no user, no free text.</summary>
public sealed record ImHeartbeat(ulong Sequence, ImHeartbeatStatus Status, string AgentVersion)
{
    const byte Version = 0;

    public byte[] Encode() => new ImWriter()
        .U8(Version)
        .U64(Sequence)
        .U8((byte)Status)
        .Id8(AgentVersion, ImAlphabet.Printable, ImLimits.AgentVersionMax)
        .ToArray();

    /// <summary>Parses strictly (verify rule 15). Throws <see cref="ImFormatException"/>.</summary>
    public static ImHeartbeat Parse(ReadOnlyMemory<byte> payload)
    {
        var reader = new ImReader(payload);
        if (reader.U8() != Version) throw new ImFormatException(ImFormatError.UnknownVersion);
        var sequence = reader.U64();
        var status = reader.U8();
        if (status > (byte)ImHeartbeatStatus.Failing) throw new ImFormatException(ImFormatError.Malformed);
        var agentVersion = reader.Id8(ImAlphabet.Printable, ImLimits.AgentVersionMax);
        reader.End();
        return new ImHeartbeat(sequence, (ImHeartbeatStatus)status, agentVersion);
    }
}
