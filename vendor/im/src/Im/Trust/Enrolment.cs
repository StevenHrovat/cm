namespace Im.Trust;

/// <summary>The roles a machine can be enrolled into. A machine outside an enrolled role runs no CMOS component and its IM messages are refused.</summary>
public enum EnrolledRole
{
    Runner,
    DevVm,
    Test,
    Build,
    Laptop,
}

/// <summary>
/// One enrolled machine as Doppler knows it. <paramref name="Subject"/> is the opaque id Doppler assigned at enrolment (never a
/// host name or a person); <paramref name="KeyId"/> is <see cref="ImSignature.KeyIdOf"/> of the device certificate, so a
/// message signed by any other key is refused even when the subject matches.
/// </summary>
public sealed record EnrolmentRecord(string Subject, EnrolledRole Role, string KeyId, bool Revoked);

/// <summary>
/// Doppler's register of enrolled machines, consulted by Milky Way's verifier on every message (a missing, revoked or
/// key-mismatched record means "unenrolled"). Doppler implements it; the verifier only reads.
/// </summary>
public interface IEnrolmentRegistry
{
    EnrolmentRecord? Find(string subject);
}
