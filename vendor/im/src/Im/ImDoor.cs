namespace Im;

/// <summary>
/// The enrolment door (DMP chapter 04, "The enrolment door"): the one way a machine without a device certificate or a token can
/// speak to CMOS. Milky Way's gate passes the opaque payload of a door frame to this interface and sends back what it returns;
/// Doppler implements it. Milky Way never reads a door payload and nothing from the door reaches a consumer or the board.
/// It lives in this library so that Milky Way and Doppler never reference each other (C1); the composition root wires the two.
/// </summary>
public interface IImDoor
{
    /// <summary>Doppler's answer to one door payload, or null to refuse (the gate then sends refused and closes, without a reason).</summary>
    byte[]? Answer(ReadOnlyMemory<byte> payload);
}
