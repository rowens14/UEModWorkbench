using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace IrrModWorkbench.Services
{
    public static class ToolDiscovery
    {
        private static bool IsAutoDiscoveryAllowed()
        {
            return string.Equals(SettingsService.Get("ToolsManuallyConfigured"), "true", StringComparison.OrdinalIgnoreCase);
        }
        public static string? FindMerger()
        {
            var saved = SettingsService.Get("MergerExePath");
            if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
                return saved;

            if (!IsAutoDiscoveryAllowed()) return null;
            var fromPath = FindOnPath("IRR-mod-merger.exe") ?? FindOnPath("irr-mod-merger.exe");
            if (fromPath != null) return fromPath;

            var known = new[]
            {
                @"E:\My Modding Projects\IRR Mod Merger\IRR-mod-merger.exe",
                @"E:\My Modding Projects\IRR Mod Merger\irr-mod-merger.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "IRR Mod Merger", "IRR-mod-merger.exe"),
            };
            foreach (var k in known)
                if (File.Exists(k)) return k;

            foreach (var root in new[] { @"E:\", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                var found = ShallowFind(root, f => Path.GetFileName(f).Equals("IRR-mod-merger.exe", StringComparison.OrdinalIgnoreCase)
                                                || Path.GetFileName(f).Equals("irr-mod-merger.exe", StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
            }
            return null;
        }

        public static string? FindRepak()
        {
            var saved = SettingsService.Get("RepakExePath");
            if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
                return saved;

            // Prefer the copy bundled with the application.
            var bundled = Path.Combine(AppContext.BaseDirectory, "Tools", "repak.exe");
            if (File.Exists(bundled)) return bundled;

            var fromPath = FindOnPath("repak.exe");
            if (fromPath != null) return fromPath;
            var extracted = @"C:\Users\owens\AppData\Local\Temp\opencode\IRR-mod-merger.exe_extracted\repak_cli\repak.exe";
            if (File.Exists(extracted)) return extracted;
            var found = ShallowFind(@"C:\Users\owens", f => Path.GetFileName(f).Equals("repak.exe", StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
            return null;
        }

        public static string? FindJaq()
        {
            var saved = SettingsService.Get("JaqExePath");
            if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
                return saved;
            if (!IsAutoDiscoveryAllowed()) return null;
            var fromPath = FindOnPath("jaq.exe") ?? FindOnPath("jaq-x86_64-pc-windows-msvc.exe");
            if (fromPath != null) return fromPath;
            var extracted = @"C:\Users\owens\AppData\Local\Temp\opencode\IRR-mod-merger.exe_extracted\jaq\jaq-x86_64-pc-windows-msvc.exe";
            if (File.Exists(extracted)) return extracted;
            var found = ShallowFind(@"C:\Users\owens", f => Path.GetFileName(f).Equals("jaq-x86_64-pc-windows-msvc.exe", StringComparison.OrdinalIgnoreCase)
                                                          || Path.GetFileName(f).Equals("jaq.exe", StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
            return null;
        }

        public static string? FindPacker()
        {
            var saved = SettingsService.Get("PackerExePath");
            if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
                return saved;

            var repak = FindOnPath("repak.exe");
            if (repak != null) return repak;

            var knownRepak = new[]
            {
                @"C:\Users\owens\OneDrive\Documents\AriesLR\PakMaster\bin\repak\repak.exe",
            };
            foreach (var k in knownRepak)
                if (File.Exists(k)) return k;

            var repakFound = ShallowFind(@"C:\Users\owens", f => Path.GetFileName(f).Equals("repak.exe", StringComparison.OrdinalIgnoreCase));
            if (repakFound != null) return repakFound;

            return FindUnrealPak();
        }

        public static string? FindUnrealPak()
        {
            var saved = SettingsService.Get("UnrealPakPath");
            if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
                return saved;

            if (!IsAutoDiscoveryAllowed()) return null;
            var fromPath = FindOnPath("UnrealPak.exe");
            if (fromPath != null) return fromPath;

            var gameFolder = TryGetGameFolder();
            if (!string.IsNullOrWhiteSpace(gameFolder))
            {
                var gamePak = Path.Combine(gameFolder, "Engine", "Binaries", "Win64", "UnrealPak.exe");
                if (File.Exists(gamePak)) return gamePak;
            }

            var epicRoots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games"),
                @"D:\Epic Games",
                @"D:\UE",
            };
            foreach (var epicRoot in epicRoots)
            {
                if (!Directory.Exists(epicRoot)) continue;
                foreach (var ueDir in Directory.GetDirectories(epicRoot, "UE_*").OrderByDescending(d => d))
                {
                    var candidate = Path.Combine(ueDir, "Engine", "Binaries", "Win64", "UnrealPak.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                // Direct UE_5.6 style
                var direct = Path.Combine(epicRoot, "UE_5.6", "Engine", "Binaries", "Win64", "UnrealPak.exe");
                if (File.Exists(direct)) return direct;

                // Shallow search within this root
                var found = ShallowFind(epicRoot, f => Path.GetFileName(f).Equals("UnrealPak.exe", StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
            }

            // Fallback: shallow search in game folder
            if (!string.IsNullOrWhiteSpace(gameFolder) && Directory.Exists(gameFolder))
            {
                var found = ShallowFind(gameFolder, f => Path.GetFileName(f).Equals("UnrealPak.exe", StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
            }
            return null;
        }

        private static string? TryGetGameFolder()
        {
            var paks = SettingsService.Get("GamePaksFolder");
            if (!string.IsNullOrWhiteSpace(paks))
            {
                var content = Directory.GetParent(paks)?.Parent;
                var testC = content?.Parent;
                var game = testC?.Parent;
                if (game != null && Directory.Exists(game.FullName)) return game.FullName;
            }
            try
            {
                var mergerIni = @"E:\My Modding Projects\IRR Mod Merger\irr-mod-merger.ini";
                if (File.Exists(mergerIni))
                {
                    foreach (var line in File.ReadAllLines(mergerIni))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("game_folder", StringComparison.OrdinalIgnoreCase))
                        {
                            var idx = t.IndexOf('=');
                            if (idx >= 0) return t.Substring(idx + 1).Trim().Replace('/', Path.DirectorySeparatorChar);
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static string? FindOnPath(string fileName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = fileName,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                var first = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first) && File.Exists(first)) return first;
            }
            catch { }
            return null;
        }

        private static string? ShallowFind(string root, Func<string, bool> predicate)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
            try
            {
                var stack = new System.Collections.Generic.Stack<(string path, int depth)>();
                stack.Push((root, 0));
                while (stack.Count > 0)
                {
                    var (dir, depth) = stack.Pop();
                    if (depth > 3) continue;
                    try
                    {
                        foreach (var f in Directory.GetFiles(dir))
                        {
                            if (predicate(f)) return f;
                        }
                        foreach (var d in Directory.GetDirectories(dir))
                        {
                            stack.Push((d, depth + 1));
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }
    }
}