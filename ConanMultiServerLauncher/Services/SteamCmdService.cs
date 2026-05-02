using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConanMultiServerLauncher.Services
{
    public static class SteamCmdService
    {
        public static string? GetSteamCmdPath()
        {
            var settings = SettingsService.Load();
            if (!string.IsNullOrWhiteSpace(settings.SteamCmdPath) && File.Exists(settings.SteamCmdPath))
                return settings.SteamCmdPath;

            return PathsService.GetSteamCmdExe();
        }

        public static async Task DownloadModsAsync(IEnumerable<long> modIds, Action<string>? logger = null, CancellationToken ct = default)
        {
            var steamCmd = GetSteamCmdPath();
            if (string.IsNullOrEmpty(steamCmd))
            {
                throw new InvalidOperationException("steamcmd.exe not found. Please set the path in settings.");
            }

            var workshopRoot = PathsService.GetWorkshopContent440900();
            if (string.IsNullOrEmpty(workshopRoot))
            {
                 // We need a workshop root. Usually it's under steamapps/workshop/content/440900.
                 // If we can't find it, we might have to guess or ask the user.
                 // PathsService.GetWorkshopContent440900() tries to find it.
                 // If it returns null, it means we don't know where Conan's workshop folder is.
                 throw new InvalidOperationException("Conan Workshop folder not found. Cannot determine where to install mods.");
            }

            // steamcmd wants the root steamapps folder for +force_install_dir
            // workshopRoot is .../steamapps/workshop/content/440900
            // We want .../
            var steamAppsRoot = Directory.GetParent(workshopRoot)?.Parent?.Parent?.FullName;
            if (string.IsNullOrEmpty(steamAppsRoot))
            {
                throw new InvalidOperationException("Could not determine SteamApps root directory.");
            }

            var args = new List<string>
            {
                "+login anonymous",
                $"+force_install_dir \"{steamAppsRoot}\""
            };

            foreach (var id in modIds)
            {
                args.Add($"+workshop_download_item 440900 {id} validate");
            }
            args.Add("+quit");

            var psi = new ProcessStartInfo
            {
                FileName = steamCmd,
                Arguments = string.Join(" ", args),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            logger?.Invoke($"Starting SteamCMD to download {modIds.Count()} mods...");
            
            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (s, e) => { if (e.Data != null) logger?.Invoke(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) logger?.Invoke($"ERROR: {e.Data}"); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    logger?.Invoke("SteamCMD download cancelled.");
                }
                throw;
            }
            
            logger?.Invoke($"SteamCMD exited with code {process.ExitCode}");
        }
    }
}
