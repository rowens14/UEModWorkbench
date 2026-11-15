using System.Diagnostics;
using System.IO;

namespace IrrModWorkbench.Services
{
    public static class PackingService
    {
        public static void RunUnrealPak(string unrealPakPath, string responseFile, string outputPak)
        {
            var psi = new ProcessStartInfo
            {
                FileName = unrealPakPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add(outputPak);
            psi.ArgumentList.Add($"-Create={responseFile}");
            psi.ArgumentList.Add("-compress");
            var process = new Process { StartInfo = psi };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new System.Exception("UnrealPak failed.");
        }

        public static void RunRepak(string repakPath, string inputDir, string outputPak, string mountPoint = "../../../Test_C/Content/")
        {
            if (File.Exists(outputPak)) { try { File.Delete(outputPak); } catch { } }

            var psi = new ProcessStartInfo
            {
                FileName = repakPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("pack");
            psi.ArgumentList.Add(inputDir);
            psi.ArgumentList.Add(outputPak);
            psi.ArgumentList.Add("--mount-point");
            psi.ArgumentList.Add(mountPoint);
            psi.ArgumentList.Add("--compression");
            psi.ArgumentList.Add("Zlib");

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new System.Exception($"repak failed (exit {process.ExitCode}): {stderr}".Trim());
        }
    }
}
