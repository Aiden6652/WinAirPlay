using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinAirPlay.Gui
{
    /// <summary>
    /// Receiver configuration (maps to the AirPlay engine's appsettings_win.json).
    /// </summary>
    public class ReceiverSettings
    {
        public string Instance { get; set; } = "WinAirPlay";
        public ushort AirTunesPort { get; set; } = 5000;
        public ushort AirPlayPort { get; set; } = 7000;
        public string DeviceMacAddress { get; set; } = "11:22:33:44:55:66";
    }

    public static class Settings
    {
        /// <summary>The engine (AirPlay.exe + FFmpeg) is extracted from the embedded bundle to a temp folder at launch.</summary>
        public static string EngineDir => EngineBundle.EngineDir;

        public static string ConfigPath => Path.Combine(EngineDir, "appsettings_win.json");

        public static ReceiverSettings Load()
        {
            var s = new ReceiverSettings();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = JsonNode.Parse(File.ReadAllText(ConfigPath));
                    var ap = json?["AirPlayReceiver"];
                    if (ap != null)
                    {
                        s.Instance = ap["Instance"]?.GetValue<string>() ?? s.Instance;
                        if (ap["AirTunesPort"] != null) s.AirTunesPort = ap["AirTunesPort"]!.GetValue<ushort>();
                        if (ap["AirPlayPort"] != null) s.AirPlayPort = ap["AirPlayPort"]!.GetValue<ushort>();
                        s.DeviceMacAddress = ap["DeviceMacAddress"]?.GetValue<string>() ?? s.DeviceMacAddress;
                    }
                }
            }
            catch { /* fall back to defaults */ }
            return s;
        }

        /// <summary>Persist only the AirPlayReceiver section, leaving Logging/Dump intact.</summary>
        public static void Save(ReceiverSettings s)
        {
            JsonObject root = File.Exists(ConfigPath)
                ? (JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject ?? new JsonObject())
                : new JsonObject();

            var ap = root["AirPlayReceiver"] as JsonObject ?? new JsonObject();
            ap["Instance"] = s.Instance;
            ap["AirTunesPort"] = s.AirTunesPort;
            ap["AirPlayPort"] = s.AirPlayPort;
            ap["DeviceMacAddress"] = s.DeviceMacAddress;
            root["AirPlayReceiver"] = ap;

            if (!Directory.Exists(EngineDir)) Directory.CreateDirectory(EngineDir);
            File.WriteAllText(ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
