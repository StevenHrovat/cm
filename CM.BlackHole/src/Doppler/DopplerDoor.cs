using Im;

namespace CM.BlackHole.Doppler;

/// <summary>
/// Doppler's side of the enrolment door (DMP chapter 04): Milky Way's gate hands it the opaque payload of a door frame, and it answers
/// with Doppler's reply or with nothing (refused, without a reason on the wire). Every outcome is counted by the server; a payload
/// that does not parse is counted as <c>door.malformed</c> and never logged.
/// </summary>
public sealed class DopplerDoor(DopplerServer server) : IImDoor
{
    public byte[]? Answer(ReadOnlyMemory<byte> payload)
    {
        DoorRequest request;
        try { request = DopplerWire.ParseRequest(payload); }
        catch (ImFormatException)
        {
            server.CountDoor("door.malformed");
            return null;
        }

        try
        {
            switch (request.Op)
            {
                case DoorOp.Challenge:
                    return server.Challenge() is { } challenge ? DopplerWire.ChallengeAnswer(challenge) : null;
                case DoorOp.EnrolBegin:
                    var begun = server.BeginEnrolment(request.Begin!);
                    return begun.Ok ? DopplerWire.ActivationAnswer(begun.Activation!) : null;
                case DoorOp.EnrolFinish:
                    var enrolled = server.FinishEnrolment(request.Finish!);
                    try { return enrolled.Ok ? DopplerWire.EnrolAnswer(enrolled) : null; }
                    finally { enrolled.Certificate?.Dispose(); }
                case DoorOp.Token:
                    var token = server.IssueToken(request.Token!);
                    return token.Ok ? DopplerWire.TokenAnswer(token.Token!) : null;
                default:
                    return null;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Fail closed. The exception may hold bytes of the request, so it is dropped, never logged.
            server.CountDoor("door.failed");
            return null;
        }
    }
}

/// <summary>One connection from the agent to Milky Way's gate: send a frame, get the gate's answer (null when the gate closed without a frame).</summary>
public interface IGateLink : IDisposable
{
    byte[]? Exchange(byte[] frame);
}

/// <summary>
/// The agent's channel to Doppler through Milky Way's enrolment door (lane L4b goal 1): one gate session, opened with a knock and
/// the gate check, then one door frame per call. It replaces <see cref="DirectChannel"/> outside the sandbox. After the door the same
/// session can answer the challenge (<see cref="Respond"/>), so a token fetch and the session that uses it are one connection.
/// </summary>
public sealed class DoorChannel : IDopplerChannel, IDisposable
{
    readonly IGateLink link;
    readonly ImClientSession client;
    bool closed;

    /// <summary>Knocks and checks the gate. Throws <see cref="ImClientException"/> when the gate is not genuine or does not answer.</summary>
    public DoorChannel(IGateLink link, ImClientSession client, ReadOnlySpan<byte> knockSecret)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(client);
        this.link = link;
        this.client = client;
        var accept = link.Exchange(client.Knock(knockSecret)) ?? throw new ImClientException(ImClientError.DoorRefused);
        client.CheckGate(accept);
    }

    public ImClientSession Client => client;

    public IGateLink Link => link;

    public byte[]? Challenge() => TryAsk(DopplerWire.ChallengeRequest(), p => DopplerWire.ParseChallengeAnswer(p), out var challenge) ? challenge : null;

    public BeginResult BeginEnrolment(EnrolBegin request) =>
        TryAsk(DopplerWire.BeginRequest(request), p => DopplerWire.ParseActivationAnswer(p), out var activation)
            ? new BeginResult(DopplerRefusal.None, activation)
            : new BeginResult(DopplerRefusal.Unanswered, null);

    public EnrolmentResult FinishEnrolment(EnrolFinish request) =>
        TryAsk(DopplerWire.FinishRequest(request), p => DopplerWire.ParseEnrolAnswer(p), out var a)
            ? new EnrolmentResult(DopplerRefusal.None, a.Subject, a.Certificate, a.Chain)
            : EnrolmentResult.Refused(DopplerRefusal.Unanswered);

    public TokenResult IssueToken(TokenRequest request)
    {
        if (!TryAsk(DopplerWire.TokenRequest(request), p => DopplerWire.ParseTokenAnswer(p), out var a)) return new TokenResult(DopplerRefusal.Unanswered, null);
        var id = Convert.ToHexStringLower(ImTokenCodec.Parse(a.Token).TokenId);
        return new TokenResult(DopplerRefusal.None, new IssuedToken(id, a.Token, a.Expires));
    }

    /// <summary>Answers the gate's challenge on this session with the token and key just obtained; true when the session is established.</summary>
    public bool Respond(byte[] token, Im.Trust.IImSigner device)
    {
        if (closed) return false;
        var reply = link.Exchange(client.Respond(token, device));
        if (reply is null || reply.Length == 0 || reply[0] != (byte)ImFrameType.Established)
        {
            closed = true;
            return false;
        }
        client.Established(reply);
        return true;
    }

    bool TryAsk<T>(byte[] payload, Func<byte[], T> parse, out T value)
    {
        value = default!;
        if (closed) return false;
        try
        {
            value = parse(ImClientSession.ReadDoorReply(link.Exchange(client.Door(payload))));
            return true;
        }
        catch (Exception e) when (e is ImClientException or ImFormatException)
        {
            // A refusal closes the gate session; any later call on this channel answers nothing.
            closed = true;
            return false;
        }
    }

    public void Dispose() => link.Dispose();
}
