using System;
using System.Diagnostics;
using System.IO;

namespace IrrModWorkbench.Services
{
    public static class MergeService
    {
        public static void RunMerger(string mergerPath, Action<string>? log = null)
        {
            if (string.IsNullOrWhiteSpace(mergerPath) || !File.Exists(mergerPath))
                throw new FileNotFoundException("Merger executable not found.", mergerPath);

            var workingDir = Path.GetDirectoryName(mergerPath) ?? string.Empty;
            // Best-effort .NET 8 runtime check.
            if (!HasDotNet8Runtime())
                throw new Exception(".NET 8.0 Runtime is not installed. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 then retry.");

            // Set the .NET 8 runtime location explicitly (elevated processes may not inherit
            // the user's PATH) and invoke the merger directly to avoid shell injection.
            var psi = new ProcessStartInfo
            {
                FileName = mergerPath,
                Arguments = "/merge",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDir
            };
            psi.EnvironmentVariables["DOTNET_ROOT"] = @"C:\Program Files\dotnet";
            var currentPath = psi.EnvironmentVariables["PATH"] ?? string.Empty;
            psi.EnvironmentVariables["PATH"] = @"C:\Program Files\dotnet;" + currentPath;
            psi.EnvironmentVariables["DOTNET_ROLL_FORWARD"] = "Disable";

            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) log?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) log?.Invoke("STDERR: " + e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new Exception($"IRR Mod Merger failed (exit code {process.ExitCode}).");
        }

        private static bool HasDotNet8Runtime()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "--list-runtimes",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return true;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return output.Contains("Microsoft.NETCore.App 8.0.0", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
    }
}