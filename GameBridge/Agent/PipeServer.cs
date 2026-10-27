using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
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

    /// <summary>Length-prefixed UTF-8 JSON, one request per connection. All callbacks run from Tick.</summary>
    internal sealed class PipeServer : IDisposable
    {
        private readonly Func<IAgentConnection> open;
        private readonly Func<long> clock;
        private readonly Func<string, string> dispatch;
        private readonly Action<string> diagnostic;
        private readonly Action<string> responseSent;
        private string responseJson;
        private IAgentConnection connection;
        private Task pending;
        private long deadline, retryAt;
        private Phase phase;
        private enum Phase { Connecting, Reading, Writing }

        public PipeServer(Func<IAgentConnection> open, Func<long> clock, Func<string, string> dispatch, Action<string> diagnostic, Action<string> responseSent)
        { this.open = open; this.clock = clock; this.dispatch = dispatch; this.diagnostic = diagnostic; this.responseSent = responseSent; }

        public void Tick()
        {
            if (clock() < retryAt) return;
            try
            {
                if (connection == null) Open();
                if (phase != Phase.Connecting && clock() >= deadline) throw new TimeoutException("Pipe request timed out.");
                if (!pending.IsCompleted) return;
                pending.GetAwaiter().GetResult();
                switch (phase)
                {
                    case Phase.Connecting:
                        // Framing is transport work, never game work. Windows asynchronous
                        // reads usually complete after Tick returns; read the whole bounded
                        // frame on a worker instead of adding a game frame per read stage.
                        var active = connection;
                        phase = Phase.Reading; deadline = clock() + 10000;
                        pending = Task.Run(() => ReadRequest(active));
                        break;
                    case Phase.Reading:
                        responseJson = dispatch(((Task<string>)pending).Result);
                        byte[] response = Encoding.UTF8.GetBytes(responseJson);
                        byte[] frame = new byte[response.Length + 4];
                        Buffer.BlockCopy(BitConverter.GetBytes(response.Length), 0, frame, 0, 4);
                        Buffer.BlockCopy(response, 0, frame, 4, response.Length);
                        phase = Phase.Writing; deadline = clock() + 10000; pending = connection.Write(frame);
                        break;
                    case Phase.Writing:
                        Close(); responseSent(responseJson);
                        // Arm the next listener now; dispatch still occurs only in a
                        // later Tick, including when this receipt requests script reload.
                        Open();
                        break;
                }
            }
            catch (Exception error)
            {
                Close(); retryAt = clock() + 1000;
                diagnostic(error.ToString());
            }
        }

        private void Open()
        {
            connection = open(); phase = Phase.Connecting; pending = connection.Connect();
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
        private void Close()
        {
            connection?.Dispose(); connection = null;
            // Disposing a pipe completes outstanding I/O; observe faults without running game work there.
            pending?.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            pending = null;
        }
        public void Dispose() => Close();
    }
}
