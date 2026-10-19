using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AirPlay.Models;

namespace AirPlay.Services
{
    /// <summary>
    /// Video output service: writes H.264 Annex B data to a named pipe ("AirPlayVideo").
    ///
    /// Rendering is NOT done here any more. The GUI (WinAirPlay.exe) connects to this pipe
    /// with its own bundled ffmpeg.exe, decodes to raw frames and paints them with WPF.
    /// That removes every failure mode of embedding an ffplay/SDL window into a WPF window
    /// (window discovery races, 0x0 clipping, black video after re-parenting, orphaned
    /// ffplay processes and stray console windows).
    /// </summary>
    public class VideoOutputService : IDisposable
    {
        private const string PIPE_NAME = "AirPlayVideo";
        private const string UNIX_PIPE_PATH = "/tmp/airplay_video";

        /// <summary>Stable machine-readable markers consumed by the GUI. Do not change without updating it.</summary>
        public const string ReadyMarker = "VIDEOPIPE_READY";
        public const string ClosedMarker = "VIDEOPIPE_CLOSED";

        private NamedPipeServerStream _pipeServer;
        private FileStream _unixPipeStream;
        private readonly object _lock = new object();
        private bool _disposed = false;
        private bool _connected = false;
        private bool _sessionActive = false;
        private long _frameCount = 0;
        private CancellationTokenSource _cts;
        private Task _acceptTask;
        private List<byte[]> _pendingFrames = new List<byte[]>();
        private bool _hasReceivedKeyFrame = false;

        public event EventHandler<string> OnStatusChanged;

        /// <summary>
        /// Start a new mirroring session: create the pipe and wait for the GUI renderer to connect.
        /// Can be called multiple times across mirroring sessions.
        /// </summary>
        public void StartMirroring()
        {
            lock (_lock)
            {
                if (_disposed) return;

                CleanupSession();
                _sessionActive = true;

                _cts = new CancellationTokenSource();
                _frameCount = 0;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    StartWindowsSession();
                }
                else
                {
                    StartUnixSession();
                }
            }
        }

        /// <summary>
        /// Stop the current mirroring session: tear down the pipe so the renderer sees EOF.
        /// </summary>
        public void StopMirroring()
        {
            lock (_lock)
            {
                Console.WriteLine($"Mirroring stopped ({_frameCount} frames written)");
                CleanupSession();
            }
        }

        private void StartWindowsSession()
        {
            try
            {
                _pipeServer = new NamedPipeServerStream(
                    PIPE_NAME,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                Console.WriteLine($"Video pipe created: \\\\.\\pipe\\{PIPE_NAME}");
                // Tell the GUI: a session is live, start your decoder now.
                Console.WriteLine(ReadyMarker);

                _acceptTask = Task.Run(() => WaitForPipeConnection(_cts.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start video session: {ex.Message}");
            }
        }

        private void StartUnixSession()
        {
            try
            {
                if (File.Exists(UNIX_PIPE_PATH))
                {
                    File.Delete(UNIX_PIPE_PATH);
                }

                var mkfifo = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "mkfifo",
                    Arguments = UNIX_PIPE_PATH,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                mkfifo?.WaitForExit();

                Console.WriteLine($"Video FIFO created: {UNIX_PIPE_PATH}");
                Console.WriteLine(ReadyMarker);

                // Open the FIFO for writing (blocks until reader connects)
                _acceptTask = Task.Run(() => WaitForUnixPipeConnection(_cts.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start video session: {ex.Message}");
            }
        }

        private void CleanupSession()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _connected = false;
            _pendingFrames.Clear();
            _hasReceivedKeyFrame = false;

            try { _pipeServer?.Dispose(); } catch { }
            _pipeServer = null;

            try { _unixPipeStream?.Dispose(); } catch { }
            _unixPipeStream = null;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(UNIX_PIPE_PATH))
            {
                try { File.Delete(UNIX_PIPE_PATH); } catch { }
            }

            if (_sessionActive)
            {
                _sessionActive = false;
                Console.WriteLine(ClosedMarker);
            }
        }

        private async Task WaitForPipeConnection(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Console.WriteLine("Waiting for video renderer to connect...");
                    await _pipeServer.WaitForConnectionAsync(token);

                    lock (_lock)
                    {
                        _connected = true;
                        FlushPendingFrames();
                    }
                    Console.WriteLine("Video renderer connected!");
                    OnStatusChanged?.Invoke(this, "connected");
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Pipe connection error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }

        private void WaitForUnixPipeConnection(CancellationToken token)
        {
            try
            {
                _unixPipeStream = new FileStream(UNIX_PIPE_PATH, FileMode.Open, FileAccess.Write);
                lock (_lock)
                {
                    _connected = true;
                    FlushPendingFrames();
                }
                Console.WriteLine("Video renderer connected to FIFO!");
                OnStatusChanged?.Invoke(this, "connected");
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Console.WriteLine($"FIFO connection error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Flush buffered frames to the pipe. Must be called under _lock.
        /// </summary>
        private void FlushPendingFrames()
        {
            if (_pendingFrames.Count == 0) return;

            Console.WriteLine($"Flushing {_pendingFrames.Count} buffered frames to video renderer...");
            try
            {
                foreach (var frameData in _pendingFrames)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _pipeServer?.Write(frameData, 0, frameData.Length);
                    }
                    else
                    {
                        _unixPipeStream?.Write(frameData, 0, frameData.Length);
                    }
                    _frameCount++;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _pipeServer?.Flush();
                }
                else
                {
                    _unixPipeStream?.Flush();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error flushing buffered frames: {ex.Message}");
            }
            _pendingFrames.Clear();
        }

        /// <summary>
        /// Write H.264 frame data to the pipe.
        /// Buffers frames before the renderer connects and replays them on connection.
        /// </summary>
        public void WriteFrame(H264Data data)
        {
            lock (_lock)
            {
                if (_disposed) return;
                if (data.Data == null || data.Length <= 0) return;

                bool isKeyFrame = data.FrameType == 5;

                if (!_connected)
                {
                    // Buffer frames until the renderer connects.
                    // On a new keyframe, discard older buffered frames (start fresh from latest keyframe).
                    if (isKeyFrame)
                    {
                        _pendingFrames.Clear();
                        _hasReceivedKeyFrame = true;
                    }
                    if (_hasReceivedKeyFrame)
                    {
                        var copy = new byte[data.Length];
                        Array.Copy(data.Data, 0, copy, 0, data.Length);
                        _pendingFrames.Add(copy);
                    }
                    return;
                }

                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        if (_pipeServer != null && _pipeServer.IsConnected)
                        {
                            _pipeServer.Write(data.Data, 0, data.Length);
                            if (isKeyFrame) _pipeServer.Flush();
                        }
                    }
                    else
                    {
                        if (_unixPipeStream != null && _unixPipeStream.CanWrite)
                        {
                            _unixPipeStream.Write(data.Data, 0, data.Length);
                            if (isKeyFrame) _unixPipeStream.Flush();
                        }
                    }

                    _frameCount++;

                    if (_frameCount % 300 == 0)
                    {
                        Console.WriteLine($"Video: {_frameCount} frames written | Type: {(data.FrameType == 5 ? "IDR" : "P")} | {data.Width}x{data.Height}");
                    }
                }
                catch (IOException)
                {
                    Console.WriteLine("Video renderer disconnected");
                    _connected = false;
                    OnStatusChanged?.Invoke(this, "disconnected");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Video write error: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                CleanupSession();

                Console.WriteLine("Video output service disposed");
            }
        }
    }
}
