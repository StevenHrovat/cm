using System.IO.Pipelines;

namespace CM.MilkyWay.Simulation;

/// <summary>
/// A connection in memory (DMP chapter 05, "The simulator"): two streams, each reading what the other writes. TLS and the frame
/// transport run over it exactly as they would over a socket, and nothing touches a network or a port.
/// </summary>
public static class InMemoryDuplex
{
    /// <summary>The device's end and the gate's end of one connection.</summary>
    public static (Stream DeviceEnd, Stream GateEnd) Pair()
    {
        var toGate = new Pipe();
        var toDevice = new Pipe();
        return (new End(toDevice.Reader, toGate.Writer), new End(toGate.Reader, toDevice.Writer));
    }

    /// <summary>One end: reads from one pipe, writes to the other. Disposing it ends both directions, as closing a socket does.</summary>
    sealed class End(PipeReader reader, PipeWriter writer) : Stream
    {
        readonly Stream input = reader.AsStream();
        readonly Stream output = writer.AsStream();
        int disposed;

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => input.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            input.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count)
        {
            output.Write(buffer, offset, count);
            output.Flush();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await output.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => output.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref disposed, 1) == 0)
            {
                writer.Complete();
                reader.Complete();
            }
            base.Dispose(disposing);
        }
    }
}
