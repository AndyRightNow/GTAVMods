using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GTAVMods.Agent
{
    public interface IAgentConnection : IDisposable
    {
        Task Connect();
        Task<int> Read(byte[] buffer, int offset, int count);
        Task Write(byte[] buffer);
    }

    public sealed class AgentPipeConnection : IAgentConnection
    {
        private readonly NamedPipeServerStream stream;
        public AgentPipeConnection(string name)
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            using (var identity = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
            stream = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 65536, 65536, security);
        }
        public Task Connect() => stream.WaitForConnectionAsync();
        public Task<int> Read(byte[] buffer, int offset, int count) => stream.ReadAsync(buffer, offset, count);
        public Task Write(byte[] buffer) => stream.WriteAsync(buffer, 0, buffer.Length);
        public void Dispose() => stream.Dispose();
    }

    /// <summary>Continuous asynchronous I/O, one bounded request awaiting a game tick. All game callbacks run from Tick.</summary>
    internal sealed class PipeServer : IDisposable
    {
        private readonly Func<IAgentConnection> open;
        private readonly Func<long> clock;
        private readonly Func<string, string> dispatch;
        private readonly Action<string> diagnostic;
        private readonly Func<string, bool> responseSent;
        private Exchange active;
        private long retryAt;
        private readonly object sync = new object();
        private bool disposed;

        private sealed class Exchange
        {
            internal IAgentConnection Connection;
            internal Task<string> Request;
            internal Task<Exchange> Next;
            internal string Response;
            internal bool Sent;
            internal long Deadline = long.MaxValue;
        }

        public PipeServer(Func<IAgentConnection> open, Func<long> clock, Func<string, string> dispatch, Action<string> diagnostic, Func<string, bool> responseSent)
        { this.open = open; this.clock = clock; this.dispatch = dispatch; this.diagnostic = diagnostic; this.responseSent = responseSent; }

        public void Tick()
        {
            if (clock() < retryAt) return;
            try
            {
                if (active == null) active = Open();
                if (active.Next != null && active.Next.IsCompleted)
                {
                    var previous = active;
                    bool reload = previous.Sent && responseSent(previous.Response);
                    active = previous.Next.GetAwaiter().GetResult();
                    if (reload) return; // Reload before dispatching any later action.
                }
                if (clock() >= Interlocked.Read(ref active.Deadline)) throw new TimeoutException("Pipe request timed out.");
                if (active.Next != null || !active.Request.IsCompleted) return;
                active.Response = dispatch(active.Request.GetAwaiter().GetResult());
                Interlocked.Exchange(ref active.Deadline, clock() + 10000);
                active.Next = SendAndAccept(active);
            }
            catch (Exception error)
            {
                Close(active); active = null; retryAt = clock() + 1000;
                diagnostic(error.ToString());
            }
        }

        private Exchange Open()
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(PipeServer));
                var exchange = new Exchange { Connection = open() };
                exchange.Request = Receive(exchange);
                return exchange;
            }
        }
        private async Task<string> Receive(Exchange exchange)
        {
            await exchange.Connection.Connect().ConfigureAwait(false);
            Interlocked.Exchange(ref exchange.Deadline, clock() + 10000);
            return await ReadRequest(exchange.Connection).ConfigureAwait(false);
        }
        private async Task<Exchange> SendAndAccept(Exchange exchange)
        {
            byte[] response = Encoding.UTF8.GetBytes(exchange.Response);
            byte[] frame = new byte[response.Length + 4];
            Buffer.BlockCopy(BitConverter.GetBytes(response.Length), 0, frame, 0, 4);
            Buffer.BlockCopy(response, 0, frame, 4, response.Length);
            await exchange.Connection.Write(frame).ConfigureAwait(false);
            exchange.Sent = true;
            exchange.Connection.Dispose();
            return Open();
        }
        private static async Task<string> ReadRequest(IAgentConnection connection)
        {
            byte[] header = await ReadExactly(connection, 4).ConfigureAwait(false);
            int length = BitConverter.ToInt32(header, 0);
            if (length <= 0 || length > Protocol.MaximumRequestBytes) throw new InvalidDataException("Invalid request length.");
            byte[] body = await ReadExactly(connection, length).ConfigureAwait(false);
            return new UTF8Encoding(false, true).GetString(body);
        }
        private static async Task<byte[]> ReadExactly(IAgentConnection connection, int length)
        {
            var bytes = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int count = await connection.Read(bytes, offset, length - offset).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("Client disconnected before completing request.");
                offset += count;
            }
            return bytes;
        }
        private static void Close(Exchange exchange)
        {
            if (exchange == null) return;
            exchange.Connection.Dispose();
            exchange.Request.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            exchange.Next?.ContinueWith(t => {
                if (t.Status == TaskStatus.RanToCompletion) Close(t.Result);
                else GC.KeepAlive(t.Exception);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
        public void Dispose()
        {
            lock (sync) disposed = true;
            Close(active); active = null;
        }
    }
}
