using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace WinAirPlay.Gui
{
    /// <summary>
    /// The AirPlay receiver engine + FFmpeg are embedded as a zip resource inside this
    /// single-file EXE. On launch we extract them to a per-user temp folder and run
    /// AirPlay.exe from there. This keeps the delivered package to a single EXE with no
    /// scattered files for the user.
    ///
    /// The extract folder is keyed by a SHA-256 of the embedded bundle, so a newly built
    /// EXE (with an updated engine) always re-extracts into a fresh folder. Without this,
    /// an older engine left over from a previous version would be silently reused (because
    /// AirPlay.exe already "exists"), which is exactly how a stale ffplay-based engine can
    /// survive an upgrade.
    /// </summary>
    public static class EngineBundle
    {
        private const string ZipResource = "WinAirPlay.Gui.Resources.engine.zip";
        private static readonly string BaseDir = Path.Combine(Path.GetTempPath(), "WinAirPlay");
        private static readonly string CurrentMarker = Path.Combine(BaseDir, ".current");

        public static string EngineDir { get; } = ResolveExtractDir();

        private static string ResolveExtractDir()
        {
            var asm = Assembly.GetExecutingAssembly();
            string hash;
            try
            {
                using var stream = asm.GetManifestResourceStream(ZipResource);
                if (stream == null)
                    throw new FileNotFoundException("Embedded engine bundle (engine.zip) was not found in the assembly.");
                using var sha = SHA256.Create();
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").Substring(0, 16);
            }
            catch (Exception ex)
            {
                // Fallback: a fixed dir name (extraction will be attempted and may fail loudly).
                throw new InvalidOperationException("无法读取内置引擎包：" + ex.Message, ex);
            }

            string dir = Path.Combine(BaseDir, hash);

            bool upToDate = File.Exists(CurrentMarker)
                            && File.ReadAllText(CurrentMarker) == hash
                            && File.Exists(Path.Combine(dir, "AirPlay.exe"));

            if (upToDate) return dir;

            // New or changed bundle: extract fresh (clean any previous copy of this hash first).
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);

            using (var stream = asm.GetManifestResourceStream(ZipResource))
            {
                if (stream == null)
                    throw new FileNotFoundException("Embedded engine bundle (engine.zip) was not found in the assembly.");
                ZipFile.ExtractToDirectory(stream, dir, overwriteFiles: true);
            }

            try { File.WriteAllText(CurrentMarker, hash); } catch { }
            return dir;
        }

        public static void EnsureExtracted()
        {
            // Resolution happens in the static initializer; this just forces it early / throws on failure.
            _ = EngineDir;
        }
    }
}
