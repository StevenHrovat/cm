namespace CM.MilkyWay.Verify;

/// <summary>
/// Why Milky Way refused a frame. The names follow the verify rules and the handshake of DMP chapter 04. A refusal is counted by
/// this code and nothing else, and the sender is never told which code it was.
/// </summary>
public enum ImRefusal
{
    // verify rules 1 to 16, in order
    Oversize,
    Malformed,
    UnknownVersion,
    UntrustedTokenSigner,
    CertificateExpired,
    BadTokenSignature,
    TokenPolicy,
    TokenNotYetValid,
    TokenExpired,
    NotEnrolled,
    Revoked,
    EnrolmentMismatch,
    SessionMismatch,
    BadMessageSignature,
    Stale,
    UnknownKind,
    WrongPermission,
    Replayed,
    ReplayCacheFull,

    // the handshake
    KnockRefused,
    KnockStale,
    KnockReplayed,
    UntrustedDevice,
    DeviceKeyMismatch,
    BadResponseSignature,
    OutOfOrder,
    HandshakeTimeout,

    // the enrolment door (DMP chapter 04, "The enrolment door")
    /// <summary>A door frame reached a gate that has no door configured.</summary>
    DoorClosed,
    /// <summary>Doppler refused the door payload (Doppler counts its own reason).</summary>
    DoorRefused,
    /// <summary>More door frames in one session than <c>milkyway.im.maxDoorFrames</c>.</summary>
    DoorLimit,
    // the transport (DMP chapter 05, "Transport encryption")
    /// <summary>TLS did not come up: no device certificate, a certificate that is not a Device certificate of the new authority, an old protocol version, or garbage.</summary>
    TransportRefused,
    /// <summary>The device certificate in the IM response is not the one the device showed in TLS.</summary>
    TransportMismatch,
    /// <summary>The device's TLS certificate is expired or not yet valid.</summary>
    TransportCertificateExpired,
    /// <summary>More connections than <c>milkyway.im.maxConnections</c>; the new one is closed without a byte.</summary>
    ConnectionLimit,
    /// <summary>An established session sent nothing for <c>milkyway.im.idleTimeout</c>.</summary>
    IdleTimeout,
    /// <summary>A handshake response on a connection whose TLS showed no device certificate (such a connection may only knock and use the door).</summary>
    TransportNoCertificate,
}

/// <summary>The families the acceptance test of lane L2 names (expired, forged, unenrolled, replayed, wrong permission), plus the rest.</summary>
public enum ImRefusalFamily
{
    Malformed,
    Forged,
    Expired,
    Unenrolled,
    Replayed,
    Stale,
    WrongPermission,
    Unknown,
    Quiet,
    Protocol,
    Capacity,
}

public static class ImRefusals
{
    public static ImRefusalFamily Family(this ImRefusal refusal) => refusal switch
    {
        ImRefusal.Oversize or ImRefusal.Malformed or ImRefusal.UnknownVersion => ImRefusalFamily.Malformed,
        ImRefusal.UntrustedTokenSigner or ImRefusal.BadTokenSignature or ImRefusal.BadMessageSignature or ImRefusal.SessionMismatch
            or ImRefusal.UntrustedDevice or ImRefusal.DeviceKeyMismatch or ImRefusal.BadResponseSignature or ImRefusal.TokenPolicy
            or ImRefusal.TransportMismatch or ImRefusal.TransportNoCertificate => ImRefusalFamily.Forged,
        ImRefusal.CertificateExpired or ImRefusal.TransportCertificateExpired or ImRefusal.TokenNotYetValid or ImRefusal.TokenExpired => ImRefusalFamily.Expired,
        ImRefusal.NotEnrolled or ImRefusal.Revoked or ImRefusal.EnrolmentMismatch => ImRefusalFamily.Unenrolled,
        ImRefusal.Replayed or ImRefusal.KnockReplayed => ImRefusalFamily.Replayed,
        ImRefusal.Stale or ImRefusal.KnockStale => ImRefusalFamily.Stale,
        ImRefusal.WrongPermission => ImRefusalFamily.WrongPermission,
        ImRefusal.UnknownKind => ImRefusalFamily.Unknown,
        ImRefusal.KnockRefused or ImRefusal.TransportRefused => ImRefusalFamily.Quiet,
        ImRefusal.OutOfOrder or ImRefusal.HandshakeTimeout or ImRefusal.IdleTimeout => ImRefusalFamily.Protocol,
        ImRefusal.ReplayCacheFull or ImRefusal.DoorLimit or ImRefusal.ConnectionLimit => ImRefusalFamily.Capacity,
        ImRefusal.DoorClosed or ImRefusal.DoorRefused => ImRefusalFamily.Unenrolled,
        _ => ImRefusalFamily.Malformed,
    };
}
