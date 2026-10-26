using System.Buffers.Binary;
using System.Text;

namespace CM.BlackHole.Doppler.Tpm;

/// <summary>One measured event of one PCR bank: its type, the digest that was extended, and the event data.</summary>
public sealed record PcrEvent(uint Type, byte[] Digest, byte[] Data);

/// <summary>TCG event types Doppler reads (TCG PC Client Platform Firmware Profile, 10.4.1).</summary>
public static class TcgEventType
{
    public const uint NoAction = 0x00000003;
    public const uint Separator = 0x00000004;
    public const uint EfiVariableDriverConfig = 0x80000001;
    public const uint EfiAction = 0x80000007;
    public const uint EfiVariableAuthority = 0x800000E0;
}

/// <summary>
/// Reads the firmware's TCG event log in the crypto-agile format (TCG PC Client Platform Firmware Profile, 10.2): a first legacy
/// event holding the "Spec ID Event03" that lists the digest sizes, then TCG_PCR_EVENT2 records, all little-endian. This runs on the
/// agent (the log is the device's own); Doppler never parses the full log, it receives the events of one PCR and replays them.
/// </summary>
public static class TcgLog
{
    const int MaxEventData = 1 << 20;

    /// <summary>The events extended into <paramref name="pcr"/> in bank <paramref name="bank"/>, in log order, without EV_NO_ACTION.</summary>
    public static IReadOnlyList<PcrEvent> EventsOf(ReadOnlySpan<byte> log, int pcr, ushort bank)
    {
        var position = 0;
        // The first event is in the legacy SHA-1 format: pcr, type, 20-byte digest, size, data (the Spec ID event).
        U32(log, ref position);
        var firstType = U32(log, ref position);
        position += 20;
        var specSize = (int)U32(log, ref position);
        var spec = Take(log, ref position, specSize);
        // Found by the Supernova fuzzer: a Spec ID event shorter than its signature must be a format error, not an out-of-range slice.
        if (firstType != TcgEventType.NoAction || spec.Length < 16 || !spec[..16].SequenceEqual("Spec ID Event03\0"u8)) throw new TpmFormatException("not a crypto-agile TCG log");
        var sizes = DigestSizes(spec);

        var events = new List<PcrEvent>();
        while (position < log.Length)
        {
            var index = U32(log, ref position);
            var type = U32(log, ref position);
            var count = U32(log, ref position);
            if (count is 0 or > 8) throw new TpmFormatException("bad digest count");
            byte[]? digest = null;
            for (var i = 0; i < count; i++)
            {
                var alg = U16(log, ref position);
                if (!sizes.TryGetValue(alg, out var size)) throw new TpmFormatException("a digest of an algorithm the log did not announce");
                var value = Take(log, ref position, size);
                if (alg == bank) digest = value.ToArray();
            }
            var dataSize = (int)U32(log, ref position);
            if (dataSize is < 0 or > MaxEventData) throw new TpmFormatException("event data too large");
            var data = Take(log, ref position, dataSize).ToArray();
            if (index != pcr || type == TcgEventType.NoAction) continue;
            if (digest is null) throw new TpmFormatException("an event without a digest of the quoted bank");
            events.Add(new PcrEvent(type, digest, data));
        }
        return events;
    }

    static Dictionary<ushort, int> DigestSizes(ReadOnlySpan<byte> spec)
    {
        // signature[16], platformClass u32, specVersionMinor u8, major u8, errata u8, uintnSize u8, numberOfAlgorithms u32, {algId u16, size u16}...
        var p = 16 + 4 + 4;
        var n = U32(spec, ref p);
        if (n is 0 or > 8) throw new TpmFormatException("bad Spec ID event");
        var sizes = new Dictionary<ushort, int>();
        for (var i = 0; i < n; i++)
        {
            var alg = U16(spec, ref p);
            sizes[alg] = U16(spec, ref p);
        }
        return sizes;
    }

    // ---- building logs (the software TPM and the tests) ---------------------------------------------------------------

    /// <summary>A crypto-agile log with a SHA-256 bank only, for the software TPM.</summary>
    public static byte[] Build(IEnumerable<(int Pcr, uint Type, byte[] Data)> events)
    {
        using var s = new MemoryStream();
        var spec = new MemoryStream();
        spec.Write("Spec ID Event03\0"u8);
        W32(spec, 0); // platform class
        spec.Write([0, 2, 0, 2]); // version 2.0, errata 0, uintn size 2 (64-bit)
        W32(spec, 1);
        W16(spec, TpmAlg.Sha256);
        W16(spec, 32);
        spec.WriteByte(0); // vendor info size
        W32(s, 0);
        W32(s, TcgEventType.NoAction);
        s.Write(new byte[20]);
        W32(s, (uint)spec.Length);
        s.Write(spec.ToArray());
        foreach (var (pcr, type, data) in events)
        {
            W32(s, (uint)pcr);
            W32(s, type);
            W32(s, 1);
            W16(s, TpmAlg.Sha256);
            s.Write(TpmAlg.Hash(TpmAlg.Sha256, data));
            W32(s, (uint)data.Length);
            s.Write(data);
        }
        return s.ToArray();
    }

    /// <summary>A UEFI_VARIABLE_DATA as firmware measures it: GUID, name length (characters), data length, UTF-16LE name, data.</summary>
    public static byte[] UefiVariable(Guid vendor, string name, byte[] data)
    {
        using var s = new MemoryStream();
        s.Write(vendor.ToByteArray());
        W64(s, (ulong)name.Length);
        W64(s, (ulong)data.Length);
        s.Write(Encoding.Unicode.GetBytes(name));
        s.Write(data);
        return s.ToArray();
    }

    static uint U32(ReadOnlySpan<byte> b, ref int p) => BinaryPrimitives.ReadUInt32LittleEndian(Take(b, ref p, 4));

    static ushort U16(ReadOnlySpan<byte> b, ref int p) => BinaryPrimitives.ReadUInt16LittleEndian(Take(b, ref p, 2));

    static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> b, ref int p, int n)
    {
        if (n < 0 || n > b.Length - p) throw new TpmFormatException("truncated TCG log");
        var slice = b.Slice(p, n);
        p += n;
        return slice;
    }

    static void W16(Stream s, ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        s.Write(b);
    }

    static void W32(Stream s, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        s.Write(b);
    }

    static void W64(Stream s, ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, v);
        s.Write(b);
    }
}

/// <summary>Why PCR 7 does not show Secure Boot on. Codes only.</summary>
public enum SecureBootVerdict
{
    On,
    /// <summary>Replaying the events does not give the quoted PCR 7.</summary>
    ReplayMismatch,
    /// <summary>An event's digest is not the hash of its data, so its meaning cannot be checked.</summary>
    EventDigestMismatch,
    /// <summary>No SecureBoot variable was measured before the separator.</summary>
    NotMeasured,
    /// <summary>The SecureBoot variable was measured as off.</summary>
    Off,
    /// <summary>The firmware measured an error separator.</summary>
    ErrorSeparator,
    /// <summary>PCR 7 does not hold exactly one separator, so the firmware's hand-over cannot be placed. A separator is recognised by its type or by
    /// its data (the type is not measured, only the data's digest is), so a relabelled separator still counts.</summary>
    NoSeparator,
}

/// <summary>
/// Doppler's reading of PCR 7 (the Secure Boot policy PCR, TCG PC Client Platform Firmware Profile, section "PCR[7] Secure Boot policy"). Every event must carry
/// its data with digest = hash(data), so Doppler knows exactly what each event says; replaying them must give the quoted PCR 7; the
/// SecureBoot variable (EFI global variable GUID) must be measured as 1 before the separator, which firmware measures before it hands over
/// to the operating system, so nothing extended later by software can stand in for it; no SecureBoot event may say 0; PCR 7 must hold
/// exactly one separator.
/// <para>
/// The event type is the agent's word: the TPM measures only the digest of the data. So no decision here rests on a type (GRB-0003 F1): an
/// event is a separator when its type says so or when its data is a separator's (four bytes, all zero or all one), and an event is the
/// SecureBoot variable when its data parses as that variable, whatever its type. Relabelling the firmware's separator or its SecureBoot=0
/// event therefore changes nothing. What PCR 7 alone cannot show is a firmware that measured nothing into it at all: software can then
/// extend a complete, honest-looking Secure Boot sequence. That case is the documented limit in <c>docs/doppler.md</c> ("What PCR 7 cannot
/// prove"); the enrolment plan, which names the machine's own endorsement key under four eyes, is the control that covers it.
/// </para>
/// </summary>
public static class SecureBootPolicy
{
    public static readonly Guid EfiGlobalVariable = new("8be4df61-93ca-11d2-aa0d-00e098032b8c");

    static readonly byte[] SeparatorData = [0, 0, 0, 0];
    static readonly byte[] ErrorSeparatorData = [0xFF, 0xFF, 0xFF, 0xFF];

    public static SecureBootVerdict Evaluate(ushort bank, byte[] quotedPcr7, IReadOnlyList<PcrEvent> events)
    {
        ArgumentNullException.ThrowIfNull(quotedPcr7);
        ArgumentNullException.ThrowIfNull(events);
        var size = TpmAlg.DigestSize(bank);
        var pcr = new byte[size];
        foreach (var e in events)
        {
            if (e.Digest.Length != size || !e.Digest.AsSpan().SequenceEqual(TpmAlg.Hash(bank, e.Data))) return SecureBootVerdict.EventDigestMismatch;
            pcr = TpmAlg.Hash(bank, [.. pcr, .. e.Digest]);
        }
        if (!pcr.AsSpan().SequenceEqual(quotedPcr7)) return SecureBootVerdict.ReplayMismatch;
        // The firmware measures one separator into PCR 7 when it hands over (TCG PC Client PFP, "Measuring Boot Events").
        if (events.Count(IsSeparator) != 1) return SecureBootVerdict.NoSeparator;

        var beforeSeparator = true;
        var on = false;
        foreach (var e in events)
        {
            if (IsSeparator(e))
            {
                if (!e.Data.AsSpan().SequenceEqual(SeparatorData)) return SecureBootVerdict.ErrorSeparator;
                beforeSeparator = false;
                continue;
            }
            // Two lines on purpose: with both tests in one condition, Stryker's mutants of the || leave the out variables unassigned and do not compile.
            if (!TryReadVariable(e.Data, out var vendor, out var name, out var value)) continue;
            if (vendor != EfiGlobalVariable || name != "SecureBoot") continue;
            if (value.Length != 1 || value[0] != 1) return SecureBootVerdict.Off;
            if (beforeSeparator) on = true;
        }
        return on ? SecureBootVerdict.On : SecureBootVerdict.NotMeasured;
    }

    /// <summary>A separator by its type or by its data: the type is not measured, so the data decides as much as the type does.</summary>
    static bool IsSeparator(PcrEvent e) =>
        e.Type == TcgEventType.Separator || e.Data.AsSpan().SequenceEqual(SeparatorData) || e.Data.AsSpan().SequenceEqual(ErrorSeparatorData);

    static bool TryReadVariable(byte[] data, out Guid vendor, out string name, out byte[] value)
    {
        vendor = Guid.Empty;
        name = "";
        value = [];
        if (data.Length < 32) return false;
        vendor = new Guid(data.AsSpan(0, 16));
        var nameChars = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(16, 8));
        var dataLength = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(24, 8));
        if (nameChars > 256 || dataLength > (ulong)data.Length || 32 + (long)nameChars * 2 + (long)dataLength != data.Length) return false;
        name = Encoding.Unicode.GetString(data, 32, (int)nameChars * 2);
        value = data[(32 + (int)nameChars * 2)..];
        return true;
    }
}
