using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GTAVMods.Agent;

namespace GTAVMods.Tests.Support
{
    // Only transport observations: bytes, partial reads, EOF, and asynchronous completions.
    internal sealed class AgentTransport : IAgentConnection
    {
        internal readonly TaskCompletionSource<bool> Connected = new TaskCompletionSource<bool>();
        internal readonly TaskCompletionSource<bool> WriteCompleted = new TaskCompletionSource<bool>();
        internal byte[] Incoming = Array.Empty<byte>(), Outgoing;
        internal int Chunk = int.MaxValue, Consumed;
        internal bool ReadStalls, Disposed;
        private readonly TaskCompletionSource<int> stalled = new TaskCompletionSource<int>();
        internal void Request(string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            Incoming = new byte[body.Length + 4];
            Buffer.BlockCopy(BitConverter.GetBytes(body.Length), 0, Incoming, 0, 4);
            Buffer.BlockCopy(body, 0, Incoming, 4, body.Length);
            Connected.SetResult(true);
        }
        public Task Connect() => Connected.Task;
        public Task<int> Read(byte[] buffer, int offset, int count)
        {
            if (ReadStalls) return stalled.Task;
            int length = Math.Min(Math.Min(count, Chunk), Incoming.Length - Consumed);
            Buffer.BlockCopy(Incoming, Consumed, buffer, offset, length); Consumed += length;
            return Task.FromResult(length);
        }
        public Task Write(byte[] buffer) { Outgoing = buffer; return WriteCompleted.Task; }
        internal string Response => Encoding.UTF8.GetString(Outgoing, 4, BitConverter.ToInt32(Outgoing, 0));
        public void Dispose()
        {
            Disposed = true;
            stalled.TrySetException(new IOException("Connection closed."));
            Connected.TrySetCanceled(); WriteCompleted.TrySetCanceled();
        }
    }
}
