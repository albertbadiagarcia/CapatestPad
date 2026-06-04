using System;
using System.Diagnostics;

namespace Capatest.Pad
{
    /// <summary>
    /// Creates a BeforeConnect callback that starts a remote socat bridge over SSH
    /// before each TCP connection attempt.
    ///
    /// Usage:
    ///   var pad = PadRtx2.CreateEthernetWithBridge("192.168.1.73", 5555);
    ///   // or with custom target/script:
    ///   var pad = PadRtx2.CreateEthernet("192.168.1.73", 5555,
    ///       SshBridgeLauncher.Create("root@192.168.1.73", "/home/root/padrtx-serial-bridge.sh"));
    ///
    /// Requirements: ssh must be available (Windows 10+ ships OpenSSH in System32\OpenSSH).
    /// Key-based auth must be configured (no interactive password prompt).
    /// </summary>
    public static class SshBridgeLauncher
    {
        /// <param name="sshTarget">SSH destination, e.g. "root@192.168.1.73"</param>
        /// <param name="scriptPath">Absolute path to the bridge script on the remote host</param>
        public static Func<bool> Create(string sshTarget, string scriptPath)
        {
            if (string.IsNullOrWhiteSpace(sshTarget))
            {
                throw new ArgumentException("sshTarget must not be empty", nameof(sshTarget));
            }
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                throw new ArgumentException("scriptPath must not be empty", nameof(scriptPath));
            }

            return () =>
            {
                try
                {
                    // Start socat only if not already running — avoids "Address already in use"
                    // that occurs when pkill kills an existing socat but its socket is still in
                    // TIME_WAIT when a new instance tries to bind.
                    // "sleep 1" keeps the SSH session alive long enough for socat to reach listen().
                    string remoteCmd =
                        $"pidof socat > /dev/null 2>&1 || " +
                        $"(sh {scriptPath} > /tmp/bridge.log 2>&1 & sleep 1)";

                    // On 32-bit host processes, System32 is redirected to SysWOW64 via WOW64 and
                    // ssh.exe is absent there. "Sysnative" is a virtual alias available to 32-bit
                    // processes that points to the real System32 on 64-bit Windows.
                    string windir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
                    string sshExe = System.IO.Path.Combine(windir, "Sysnative", "OpenSSH", "ssh.exe");

                    var psi = new ProcessStartInfo
                    {
                        FileName = sshExe,
                        Arguments = $"-o StrictHostKeyChecking=no -o BatchMode=yes {sshTarget} \"{remoteCmd}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    using (Process proc = Process.Start(psi))
                    {
                        bool exited = proc?.WaitForExit(5000) ?? true;
                        if (!exited)
                        {
                            proc?.Kill();
                            return false;
                        }
                        return (proc?.ExitCode ?? 0) == 0;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            };
        }
    }
}
