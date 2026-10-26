using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Im;

/// <summary>Why bytes are not IM. A code only: the exception never carries the bytes it refused (DMP chapter 04, "The refusal log").</summary>
public enum ImFormatError
{
    /// <summary>Wrong magic, a short or overlong field, a character outside its alphabet, trailing bytes, a key or signature of the wrong shape.</summary>
    Malformed,
    /// <summary>The magic is right but the version byte is not one this code speaks (version 0 only).</summary>
    UnknownVersion,
}

/// <summary>Thrown by <see cref="ImReader"/> and the codecs when bytes are not canonical IM. Its message is the error code and nothing else.</summary>
public sealed class ImFormatException : Exception
{
    public ImFormatException(ImFormatError error) : base(error.ToString()) => Error = error;

    public ImFormatError Error { get; }
}

/// <summary>The alphabets of <c>id8</c> fields (DMP chapter 04, "Encoding"). ASCII only, so there are no look-alike spellings.</summary>
public enum ImAlphabet
{
    /// <summary><c>a-z 0-9 . -</c>: kinds, permissions, roles.</summary>
    Name,
    /// <summary><c>a-z 0-9 -</c>: the opaque subject identifier Doppler assigns.</summary>
    Subject,
    /// <summary>Bytes 0x21 to 0x7E: the agent version of a heartbeat.</summary>
    Printable,
}

/// <summary>The limits of IM version 0 (DMP chapter 04, "Encoding"). Limits an administrator tunes are settings in Milky Way; these are part of the format.</summary>
public static class ImLimits
{
    public const int SubjectMax = 64;
    public const int RoleMax = 32;
    public const int NameMax = 64;
    public const int PermissionsMax = 16;
    public const int CertificateMax = 4096;
    public const int ChainMax = 4;
    public const int TokenMax = 16 * 1024;
    public const int KeyMax = 256;
    public const int SignatureLength = 64;
    public const int AgentVersionMax = 32;
    /// <summary>The structural maximum of a door payload; the gate's setting <c>milkyway.im.maxDoorBytes</c> is at most this.</summary>
    public const int DoorMax = 60 * 1024;
}

/// <summary>
/// Writes canonical IM bytes. The writer refuses (with <see cref="ArgumentException"/>) anything the reader would refuse, so a
/// sender built on it cannot emit a non-canonical frame.
/// </summary>
public sealed class ImWriter
{
    readonly ArrayBufferWriter<byte> buffer = new(256);

    public ImWriter Magic(string magic, byte version)
    {
        Raw(Encoding.ASCII.GetBytes(magic));
        return U8(version);
    }

    public ImWriter U8(byte value)
    {
        buffer.GetSpan(1)[0] = value;
        buffer.Advance(1);
        return this;
    }

    public ImWriter U16(ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.GetSpan(2), value);
        buffer.Advance(2);
        return this;
    }

    public ImWriter U32(uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(buffer.GetSpan(4), value);
        buffer.Advance(4);
        return this;
    }

    public ImWriter U64(ulong value)
    {
        BinaryPrimitives.WriteUInt64BigEndian(buffer.GetSpan(8), value);
        buffer.Advance(8);
        return this;
    }

    public ImWriter I64(long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(buffer.GetSpan(8), value);
        buffer.Advance(8);
        return this;
    }

    public ImWriter Raw(ReadOnlySpan<byte> bytes)
    {
        buffer.Write(bytes);
        return this;
    }

    public ImWriter Fixed(ReadOnlySpan<byte> bytes, int length)
    {
        if (bytes.Length != length) throw new ArgumentException($"a fixed({length}) field got {bytes.Length} bytes", nameof(bytes));
        return Raw(bytes);
    }

    public ImWriter Bytes16(ReadOnlySpan<byte> bytes, int max)
    {
        if (bytes.Length > max || bytes.Length > ushort.MaxValue) throw new ArgumentException("a bytes16 field is over its limit", nameof(bytes));
        U16((ushort)bytes.Length);
        return Raw(bytes);
    }

    public ImWriter Bytes32(ReadOnlySpan<byte> bytes, int max)
    {
        if (bytes.Length > max) throw new ArgumentException("a bytes32 field is over its limit", nameof(bytes));
        U32((uint)bytes.Length);
        return Raw(bytes);
    }

    public ImWriter Id8(string value, ImAlphabet alphabet, int max)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!ImText.IsValid(value, alphabet, max)) throw new ArgumentException($"not a valid {alphabet} id of at most {max} characters", nameof(value));
        U8((byte)value.Length);
        return Raw(Encoding.ASCII.GetBytes(value));
    }

    /// <summary>A list of certificates (or other blobs) as <c>u8</c> count then <c>bytes16</c> items.</summary>
    public ImWriter List16(IReadOnlyList<byte[]> items, int maxCount, int maxItem)
    {
        if (items.Count is 0 || items.Count > maxCount) throw new ArgumentException("a list is empty or over its limit", nameof(items));
        U8((byte)items.Count);
        foreach (var item in items) Bytes16(item, maxItem);
        return this;
    }

    public byte[] ToArray() => buffer.WrittenSpan.ToArray();
}

/// <summary>
/// Reads canonical IM bytes strictly: every read is bounds-checked, every length is checked against its limit before anything is
/// allocated, and <see cref="End"/> refuses trailing bytes. Any failure is an <see cref="ImFormatException"/>.
/// </summary>
public sealed class ImReader(ReadOnlyMemory<byte> data)
{
    int position;

    public int Position => position;

    public void Magic(string magic, byte version)
    {
        var expected = Encoding.ASCII.GetBytes(magic);
        if (!Take(expected.Length).Span.SequenceEqual(expected)) throw new ImFormatException(ImFormatError.Malformed);
        if (U8() != version) throw new ImFormatException(ImFormatError.UnknownVersion);
    }

    public byte U8() => Take(1).Span[0];

    public ushort U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2).Span);

    public uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4).Span);

    public ulong U64() => BinaryPrimitives.ReadUInt64BigEndian(Take(8).Span);

    public long I64() => BinaryPrimitives.ReadInt64BigEndian(Take(8).Span);

    public byte[] Fixed(int length) => Take(length).ToArray();

    public byte[] Bytes16(int max)
    {
        var length = U16();
        if (length > max) throw new ImFormatException(ImFormatError.Malformed);
        return Take(length).ToArray();
    }

    public byte[] Bytes32(int max)
    {
        var length = U32();
        if (length > (uint)max) throw new ImFormatException(ImFormatError.Malformed);
        return Take((int)length).ToArray();
    }

    public string Id8(ImAlphabet alphabet, int max)
    {
        var length = U8();
        if (length == 0 || length > max) throw new ImFormatException(ImFormatError.Malformed);
        var raw = Take(length).Span;
        // Check the bytes before decoding: the ASCII decoder turns every byte above 0x7F into '?', which is printable, so a check on the
        // decoded text alone gave such bytes a second spelling (found by the fuzzer, invariant I6).
        foreach (var b in raw) if (b > 0x7F) throw new ImFormatException(ImFormatError.Malformed);
        var text = Encoding.ASCII.GetString(raw);
        if (!ImText.IsValid(text, alphabet, max)) throw new ImFormatException(ImFormatError.Malformed);
        return text;
    }

    public IReadOnlyList<byte[]> List16(int maxCount, int maxItem)
    {
        var count = U8();
        if (count == 0 || count > maxCount) throw new ImFormatException(ImFormatError.Malformed);
        var items = new byte[count][];
        for (var i = 0; i < count; i++) items[i] = Bytes16(maxItem);
        return items;
    }

    public void End()
    {
        if (position != data.Length) throw new ImFormatException(ImFormatError.Malformed);
    }

    ReadOnlyMemory<byte> Take(int length)
    {
        if (length < 0 || length > data.Length - position) throw new ImFormatException(ImFormatError.Malformed);
        var slice = data.Slice(position, length);
        position += length;
        return slice;
    }
}

/// <summary>The alphabet checks shared by the reader and the writer.</summary>
public static class ImText
{
    public static bool IsValid(string value, ImAlphabet alphabet, int max)
    {
        if (value.Length == 0 || value.Length > max) return false;
        foreach (var c in value)
        {
            var ok = alphabet switch
            {
                ImAlphabet.Name => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-',
                ImAlphabet.Subject => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-',
                ImAlphabet.Printable => c is >= '!' and <= '~',
                _ => false,
            };
            if (!ok) return false;
        }
        return true;
    }
}

/// <summary>Times on the wire are Unix milliseconds, UTC (DMP chapter 04, "Encoding").</summary>
public static class ImTime
{
    public static long ToWire(DateTimeOffset time) => time.ToUnixTimeMilliseconds();

    public static DateTimeOffset FromWire(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
}
