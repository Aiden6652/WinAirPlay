using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace WinAirPlay.Gui
{
    /// <summary>
    /// The AirPlay receiver engine + FFmpeg are embedded as a zip resource inside this
    /// single-file EXE. On first launch we extract them to a per-user temp folder and
    /// launch AirPlay.exe from there. This keeps the delivered package to a single EXE
    /// (plus the unavoidable WPF native DLLs) with no scattered files for the user.
    /// </summary>
    public static class EngineBundle
    {
        private static readonly string ExtractDir =
            Path.Combine(Path.GetTempPath(), "WinAirPlay", "engine");

        public static string EngineDir => ExtractDir;

        public static void EnsureExtracted()
        {
            var marker = Path.Combine(ExtractDir, "AirPlay.exe");
            if (File.Exists(marker) && Directory.Exists(ExtractDir))
                return;

            Directory.CreateDirectory(ExtractDir);
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("WinAirPlay.Gui.Resources.engine.zip");
            if (stream == null)
                throw new FileNotFoundException("Embedded engine bundle (engine.zip) was not found in the assembly.");

            ZipFile.ExtractToDirectory(stream, ExtractDir, overwriteFiles: true);
        }
    }
}
