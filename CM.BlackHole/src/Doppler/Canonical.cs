using System.Buffers.Binary;
using System.Text;

namespace CM.BlackHole.Doppler;

/// <summary>Length-prefixed concatenation under a fixed tag: the bytes Doppler's attestation signatures cover. One spelling, used by agent and server.</summary>
internal static class Canonical
{
    public static byte[] Of(string tag, params byte[][] parts)
    {
        using var stream = new MemoryStream();
        Put(stream, Encoding.UTF8.GetBytes(tag));
        foreach (var part in parts) Put(stream, part);
        return stream.ToArray();
    }

    public static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    static void Put(Stream stream, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }
}
