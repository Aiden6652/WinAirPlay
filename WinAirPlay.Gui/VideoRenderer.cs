using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinAirPlay.Gui
{
    /// <summary>
    /// Decodes the AirPlay H.264 stream (from the engine's named pipe) with the bundled
    /// ffmpeg.exe and paints the frames straight into a WPF Image.
    ///
    /// This replaces the old "launch ffplay and re-parent its SDL window" approach, which
    /// could not be made reliable: the SDL window is invisible/0x0 while initialising,
    /// breaks when re-parented, and leaves orphaned processes plus a stray console window.
    /// Here the video never leaves our own process, so there is nothing to find, embed or
    /// rescue: if ffmpeg delivers frames, they are shown.
    /// </summary>
    public sealed class VideoRenderer : IDisposable
    {
        // Fixed render canvas. ffmpeg letterboxes the source into it, so any AirPlay
        // resolution (1566x1080, 1920x1080, portrait, ...) works without extra plumbing,
        // and the raw frame size is known in advance (no need to parse ffmpeg stderr).
        public const int FrameWidth = 1280;
        public const int FrameHeight = 720;
        private const int Stride = FrameWidth * 3;
        private const int FrameBytes = Stride * FrameHeight;

        private readonly string _ffmpegPath;
        private readonly string _pipePath;
        private readonly Dispatcher _dispatcher;
        private readonly WriteableBitmap _bitmap;

        // Reader-owned buffer (filled from ffmpeg stdout) and UI-owned buffer (painted).
        private readonly byte[] _readBuffer = new byte[FrameBytes];
        private readonly byte[] _displayBuffer = new byte[FrameBytes];

        private Process? _proc;
        private Thread? _reader;
        private volatile bool _running;
        private int _uiPending;          // 1 while a frame is queued/painting on the UI thread
        private long _framesRendered;
        private string? _lastError;

        public event Action<string>? Log;
        public event Action? Exited;

        public ImageSource Bitmap => _bitmap;

        public VideoRenderer(string ffmpegPath, string pipePath)
        {
            _ffmpegPath = ffmpegPath;
            _pipePath = pipePath;
            _dispatcher = Dispatcher.CurrentDispatcher;
            // Must be created (and only ever written) on the UI thread.
            _bitmap = new WriteableBitmap(FrameWidth, FrameHeight, 96, 96, PixelFormats.Bgr24, null);
        }

        public void Start()
        {
            if (!File.Exists(_ffmpegPath))
                throw new FileNotFoundException("未找到 ffmpeg.exe", _ffmpegPath);

            // -f h264 ... : raw Annex-B H.264 from the pipe (same thing ffplay used to read)
            // scale+pad     : fit any source into the fixed 1280x720 canvas, letterboxed
            // bgr24 rawvideo: WPF can blit this directly with no pixel conversion
            string args =
                "-hide_banner -loglevel warning -nostdin " +
                "-f h264 -probesize 65536 -analyzeduration 0 -fflags nobuffer+discardcorrupt -flags low_delay " +
                $"-i \"{_pipePath}\" " +
                $"-an -vf \"scale={FrameWidth}:{FrameHeight}:force_original_aspect_ratio=decrease," +
                $"pad={FrameWidth}:{FrameHeight}:(ow-iw)/2:(oh-ih)/2\" " +
                "-pix_fmt bgr24 -f rawvideo pipe:1";

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                // ffmpeg.exe needs its sibling av*.dll from the engine folder.
                WorkingDirectory = Path.GetDirectoryName(_ffmpegPath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8
            };

            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                if (e.Data == _lastError) return;      // ffmpeg repeats the same warning per frame
                _lastError = e.Data;
                Log?.Invoke(e.Data);
            };
            _proc.Exited += (_, _) =>
            {
                if (!_running) return;
                _running = false;
                Exited?.Invoke();
            };

            _proc.Start();
            _proc.BeginErrorReadLine();

            _running = true;
            _reader = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "WinAirPlay.VideoReader",
                Priority = ThreadPriority.AboveNormal
            };
            _reader.Start();
        }

        private void ReadLoop()
        {
            var stdout = _proc!.StandardOutput.BaseStream;
            try
            {
                while (_running)
                {
                    int read = 0;
                    while (read < FrameBytes)
                    {
                        int n = stdout.Read(_readBuffer, read, FrameBytes - read);
                        if (n <= 0) return; // pipe closed / ffmpeg gone
                        read += n;
                    }

                    _framesRendered++;

                    // Never overwrite the display buffer while the UI is still painting it,
                    // but don't stall the decode pipeline either: drop the frame instead.
                    int waited = 0;
                    while (Volatile.Read(ref _uiPending) == 1 && waited < 60)
                    {
                        Thread.Sleep(1);
                        waited++;
                    }
                    if (Volatile.Read(ref _uiPending) == 1) continue;

                    Buffer.BlockCopy(_readBuffer, 0, _displayBuffer, 0, FrameBytes);
                    Interlocked.Exchange(ref _uiPending, 1);

                    _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                    {
                        try
                        {
                            _bitmap.WritePixels(
                                new System.Windows.Int32Rect(0, 0, FrameWidth, FrameHeight),
                                _displayBuffer, Stride, 0);
                        }
                        catch { /* window closing */ }
                        finally { Interlocked.Exchange(ref _uiPending, 0); }
                    }));
                }
            }
            catch (Exception ex)
            {
                if (_running) Log?.Invoke("读帧线程异常：" + ex.Message);
            }
        }

        public long FramesRendered => _framesRendered;

        public void Dispose()
        {
            _running = false;
            try
            {
                if (_proc != null && !_proc.HasExited) _proc.Kill();
            }
            catch { }
            try { _reader?.Join(300); } catch { }
            try { _proc?.Dispose(); } catch { }
            _proc = null;
            _reader = null;
        }
    }
}
