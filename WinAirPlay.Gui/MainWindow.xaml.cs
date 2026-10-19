using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinAirPlay.Gui
{
    public partial class MainWindow : Window
    {
        private EngineLauncher? _engine;
        private VideoRenderer? _renderer;
        private ReceiverSettings _settings = null!;
        private readonly object _engineLock = new object();
        private State _state = State.Initializing;

        // Must match AirPlay/Services/VideoOutputService.cs
        private const string PipeReadyMarker = "VIDEOPIPE_READY";
        private const string PipeClosedMarker = "VIDEOPIPE_CLOSED";

        private enum State { Initializing, Waiting, Mirroring, Stopped }

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _settings = Settings.Load();
            DeviceNameBox.Text = _settings.Instance;
            PortBox.Text = _settings.AirPlayPort.ToString();
            MacBox.Text = _settings.DeviceMacAddress;

            // Environment diagnostics: the first thing to look at when video never appears.
            AppendLog("引擎目录：" + Settings.EngineDir);
            AppendLog("ffmpeg.exe：" + (File.Exists(Path.Combine(Settings.EngineDir, "ffmpeg.exe")) ? "OK" : "缺失！"));

            // Open the app and it's immediately ready to receive — no Start button needed.
            StartReceiver();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopVideo();
            lock (_engineLock)
            {
                _engine?.Dispose();
                _engine = null;
            }
        }

        private void StartReceiver()
        {
            try
            {
                EngineBundle.EnsureExtracted();
            }
            catch (Exception ex)
            {
                SetStatus(State.Stopped, "引擎解压失败：" + ex.Message);
                AppendLog("ERROR: " + ex);
                return;
            }

            string engineExe = Path.Combine(Settings.EngineDir, "AirPlay.exe");
            if (!File.Exists(engineExe))
            {
                SetStatus(State.Stopped, "未找到接收引擎 AirPlay.exe（请先构建/部署，见 README）。");
                AppendLog("ERROR: AirPlay.exe not found at " + engineExe);
                return;
            }

            Settings.Save(_settings);

            lock (_engineLock)
            {
                _engine?.Dispose();
                _engine = new EngineLauncher();
                _engine.LogLine += OnLog;
                _engine.Exited += OnEngineExited;
            }

            StopVideo();
            SetStatus(State.Initializing, "正在启动接收器…");
            try
            {
                _engine.Start(engineExe, Settings.EngineDir);
            }
            catch (Exception ex)
            {
                SetStatus(State.Stopped, "启动失败：" + ex.Message);
                AppendLog("ERROR: " + ex);
            }
        }

        private void OnEngineExited(int code)
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.Invoke(() =>
            {
                if (_state != State.Stopped)
                    SetStatus(State.Stopped, "接收器已退出（代码 " + code + "）。");
                StopVideo();
                WaitingOverlay.Visibility = Visibility.Visible;
            });
        }

        private void OnLog(string line)
        {
            Dispatcher.Invoke(() =>
            {
                AppendLog(line);

                if (line.Contains("Video output service ready") || line.Contains("IP address"))
                    SetStatus(State.Waiting, "正在广播，等待 iPad 连接…");

                // The engine created a fresh video pipe: decode it ourselves, in-process.
                if (line.Contains(PipeReadyMarker))
                {
                    StartVideo();
                    return;
                }

                if (line.Contains(PipeClosedMarker) || line.Contains("Mirroring stopped"))
                {
                    StopVideo();
                    if (_state != State.Stopped) SetStatus(State.Waiting, "正在广播，等待 iPad 连接…");
                    WaitingOverlay.Visibility = Visibility.Visible;
                }
            });
        }

        /// <summary>Starts (or restarts) the in-process renderer for the current pipe.</summary>
        private void StartVideo()
        {
            StopVideo();

            string ffmpeg = Path.Combine(Settings.EngineDir, "ffmpeg.exe");
            if (!File.Exists(ffmpeg))
            {
                SetStatus(State.Mirroring, "缺少 ffmpeg.exe，无法显示画面");
                AppendLog("ERROR: 未找到 " + ffmpeg);
                return;
            }

            try
            {
                var renderer = new VideoRenderer(ffmpeg, @"\\.\pipe\AirPlayVideo");
                renderer.Log += msg =>
                {
                    if (Dispatcher.HasShutdownStarted) return;
                    Dispatcher.BeginInvoke(new Action(() => AppendLog("[ffmpeg] " + msg)));
                };
                renderer.Exited += () =>
                {
                    if (Dispatcher.HasShutdownStarted) return;
                    Dispatcher.BeginInvoke(new Action(() => AppendLog("[ffmpeg] 解码进程已退出")));
                };

                _renderer = renderer;
                VideoImage.Source = renderer.Bitmap;
                renderer.Start();

                WaitingOverlay.Visibility = Visibility.Collapsed;
                VideoImage.Visibility = Visibility.Visible;
                SetStatus(State.Mirroring, "已连接，正在投屏…");

                WatchFirstFrame(renderer);
            }
            catch (Exception ex)
            {
                SetStatus(State.Mirroring, "视频渲染启动失败：" + ex.Message);
                AppendLog("ERROR: " + ex);
            }
        }

        /// <summary>Warns if ffmpeg stays silent, instead of leaving the user staring at black.</summary>
        private void WatchFirstFrame(VideoRenderer renderer)
        {
            Task.Delay(6000).ContinueWith(_ =>
            {
                if (Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!ReferenceEquals(_renderer, renderer)) return;
                    if (renderer.FramesRendered > 0) return;
                    AppendLog("WARN: 6 秒内没有解出任何画面（ffmpeg 未收到可解码帧）。");
                    SetStatus(State.Mirroring, "已连接，但未收到可解码的视频帧（请看日志 [ffmpeg] 行）");
                }));
            });
        }

        private void StopVideo()
        {
            var r = _renderer;
            _renderer = null;
            VideoImage.Source = null;
            try { r?.Dispose(); } catch { }
        }

        private void SetStatus(State s, string text)
        {
            _state = s;
            StatusText.Text = text;
            StatusDot.Fill = s == State.Mirroring ? Brushes.LimeGreen
                           : s == State.Waiting ? Brushes.Goldenrod
                           : s == State.Stopped ? Brushes.Red
                           : Brushes.Gray;
            DeviceNameText.Text = "设备名：" + (_settings?.Instance ?? "");
        }

        private void AppendLog(string line)
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.IsExpanded = !SettingsPanel.IsExpanded;
        }

        private void SettingsApply_Click(object sender, RoutedEventArgs e)
        {
            _settings.Instance = (DeviceNameBox.Text ?? "").Trim();
            if (ushort.TryParse(PortBox.Text, out ushort p)) _settings.AirPlayPort = p;
            if (!string.IsNullOrWhiteSpace(MacBox.Text)) _settings.DeviceMacAddress = MacBox.Text.Trim();

            StartReceiver(); // restart with new settings
            SettingsPanel.IsExpanded = false;
        }
    }
}
