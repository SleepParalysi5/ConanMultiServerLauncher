using System;
using System.Diagnostics;
using System.IO;

namespace ConanMultiServerLauncher.Services
{
    public static class LauncherService
    {
        // Launch Conan directly, choosing BattlEye or non-BattlEye executable, continue last session
        public static void LaunchConan(bool battlEyeEnabled, string? serverAddress = null, string? password = null)
        {
            // serverAddress/password not passed as args; we set last-connected server in GameUserSettings.ini separately
            var conanRoot = PathsService.GetConanRoot();
            if (string.IsNullOrWhiteSpace(conanRoot) || !Directory.Exists(conanRoot))
                throw new InvalidOperationException("Conan Exiles root folder not found. Make sure Conan Exiles is installed.");

            var bin64 = Path.Combine(conanRoot!, "ConanSandbox", "Binaries", "Win64");
            var exeName = battlEyeEnabled ? "ConanSandbox_BE.exe" : "ConanSandbox.exe";
            var exePath = Path.Combine(bin64, exeName);
            if (!File.Exists(exePath))
                throw new InvalidOperationException($"{exeName} not found under ConanSandbox\\Binaries\\Win64. Expected at: {exePath}");

            // If BattlEye is enabled, start the BattlEye service first, if present
            if (battlEyeEnabled)
            {
                try
                {
                    var beService = Path.Combine(bin64, "BattlEye", "BEService_x64.exe");
                    if (File.Exists(beService))
                    {
                        var bePsi = new ProcessStartInfo
                        {
                            FileName = beService,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = Path.GetDirectoryName(beService)!
                        };
                        Process.Start(bePsi);
                    }
                    else
                    {
                        // Do not block launching the game; just inform via exception message path if later fails
                        // Alternatively, could log somewhere; keeping silent to avoid UX noise.
                    }
                }
                catch
                {
                    // Swallow errors from BE service start to not prevent game launch.
                }
            }

            var settings = SettingsService.Load();

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
                Arguments = settings.TextureStreamingEnabled ? string.Empty : "-notexturestreaming"
            };
            Process.Start(psi);
        }
    }
}