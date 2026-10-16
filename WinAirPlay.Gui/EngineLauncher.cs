using System;
using System.Diagnostics;
using System.Text;

namespace WinAirPlay.Gui
{
    /// <summary>
    /// Launches the AirPlay receiver engine (AirPlay.exe) as a child process,
    /// captures its stdout/stderr for the log panel, and surfaces exit events.
    /// </summary>
    public class EngineLauncher : IDisposable
    {
        private Process _proc;

        public event Action<string> LogLine;
        public event Action<int> Exited;

        public bool IsRunning => _proc != null && !_proc.HasExited;

        public void Start(string engineExePath, string workingDir)
        {
            var psi = new ProcessStartInfo
            {
                FileName = engineExePath,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (s, e) => { if (e.Data != null) LogLine?.Invoke(e.Data); };
            _proc.ErrorDataReceived += (s, e) => { if (e.Data != null) LogLine?.Invoke(e.Data); };
            _proc.Exited += (s, e) => Exited?.Invoke(_proc.ExitCode);

            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
        }

        public void Stop()
        {
            try
            {
                if (_proc != null && !_proc.HasExited)
                {
                    _proc.Kill();
                }
            }
            catch { /* already gone */ }
        }

        public void Dispose()
        {
            Stop();
            try { _proc?.Dispose(); } catch { }
            _proc = null;
        }
    }
}
