using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using GTAVMods.Agent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GTAVMods.Tests.Support
{
    internal static class AgentClient
    {
        internal static (int ExitCode, string Output, string Error) Run(string root, string arguments, Action tick)
        {
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Invoke-Gta.ps1");
            var info = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -DiscoveryDirectory \"" + root + "\" -TimeoutSeconds 10 " + arguments) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = Process.Start(info))
            {
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                var elapsed = Stopwatch.StartNew();
                try
                {
                    while (!process.HasExited && elapsed.ElapsedMilliseconds < 20000) { tick(); Thread.Sleep(1); }
                    Assert.IsTrue(process.HasExited, "External GTA utility did not finish within 20 seconds.");
                    return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
                }
                finally { if (!process.HasExited) process.Kill(); }
            }
        }
    }
}
