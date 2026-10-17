using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinAirPlay.Gui
{
    public partial class MainWindow : Window
    {
        private EngineLauncher? _engine;
        private ReceiverSettings _settings = null!;
        private readonly object _engineLock = new object();
        private State _state = State.Initializing;

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

            // Open the app and it's immediately ready to receive — no Start button needed.
            StartReceiver();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
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

            string engineExe = System.IO.Path.Combine(Settings.EngineDir, "AirPlay.exe");
            if (!System.IO.File.Exists(engineExe))
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

            ClearVideo();
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
                ClearVideo();
            });
        }

        private void OnLog(string line)
        {
            Dispatcher.Invoke(() =>
            {
                AppendLog(line);

                if (line.Contains("Video output service ready") || line.Contains("IP address"))
                    SetStatus(State.Waiting, "正在广播，等待 iPad 连接…");

                var m = Regex.Match(line, @"ffplay launched \(PID: (\d+)\)");
                if (m.Success)
                {
                    int pid = int.Parse(m.Groups[1].Value);
                    SetStatus(State.Mirroring, "已连接，正在投屏…");
                    ShowVideo(pid);
                }

                if (line.Contains("Mirroring stopped"))
                {
                    SetStatus(State.Waiting, "正在广播，等待 iPad 连接…");
                    ClearVideo();
                }
            });
        }

        private void ShowVideo(int pid)
        {
            WaitingOverlay.Visibility = Visibility.Collapsed;

            // Poll off the UI thread, then embed on the UI thread so BuildWindowCore is instant.
            Task.Run(() =>
            {
                IntPtr hwnd = FfplayHost.FindFfplayWindow(pid, 8000);
                if (hwnd != IntPtr.Zero)
                {
                    Dispatcher.Invoke(() =>
                    {
                        try { VideoPresenter.Content = new FfplayHost(pid); }
                        catch { /* ffplay keeps its own window as a fallback */ }
                    });
                }
            });
        }

        private void ClearVideo()
        {
            WaitingOverlay.Visibility = Visibility.Visible;
            VideoPresenter.Content = null;
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
