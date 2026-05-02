using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ConanMultiServerLauncher.Services
{
    public static class PathsService
    {
        private static string? _cachedSteamRoot;
        private static string? _cachedConanRoot;
        private static List<string>? _cachedSteamAppsRoots;

        // 1) Steam root from registry or default
        public static string? GetSteamRoot()
        {
            if (_cachedSteamRoot != null) return _cachedSteamRoot;

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var path = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                _cachedSteamRoot = path;
                return path;
            }

            var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            if (Directory.Exists(defaultPath))
            {
                _cachedSteamRoot = defaultPath;
                return defaultPath;
            }

            return null;
        }

        public static string? GetSteamExe()
        {
            var root = GetSteamRoot();
            if (root == null) return null;
            var exe = Path.Combine(root, "steam.exe");
            return File.Exists(exe) ? exe : null;
        }

        public static string? GetSteamCmdExe()
        {
            // 1. Check same directory as launcher
            var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steamcmd.exe");
            if (File.Exists(local)) return local;

            // 2. Check Steam root
            var steamRoot = GetSteamRoot();
            if (steamRoot != null)
            {
                var path = Path.Combine(steamRoot, "steamcmd.exe");
                if (File.Exists(path)) return path;
                
                // Also check a subdirectory "steamcmd" under steam root
                path = Path.Combine(steamRoot, "steamcmd", "steamcmd.exe");
                if (File.Exists(path)) return path;
            }

            // 3. Check common installation path C:\steamcmd
            var commonPath = @"C:\steamcmd\steamcmd.exe";
            if (File.Exists(commonPath)) return commonPath;

            // 4. Check LocalAppData (sometimes installed there)
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localAppPath = Path.Combine(localAppData, "steamcmd", "steamcmd.exe");
            if (File.Exists(localAppPath)) return localAppPath;

            return null;
        }

        // 2) Enumerate all Steam library roots (…\steamapps)
        private static IEnumerable<string> EnumerateSteamAppsRoots()
        {
            if (_cachedSteamAppsRoots != null) return _cachedSteamAppsRoots;

            var roots = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var steamRoot = GetSteamRoot();
            if (steamRoot != null)
            {
                var main = Path.Combine(steamRoot, "steamapps");
                if (Directory.Exists(main) && seen.Add(main))
                    roots.Add(main);

                var vdf = Path.Combine(main, "libraryfolders.vdf");
                foreach (var root in ParseLibraryFolders(vdf))
                {
                    var apps = Path.Combine(root, "steamapps");
                    if (Directory.Exists(apps) && seen.Add(apps))
                        roots.Add(apps);
                }
            }
            _cachedSteamAppsRoots = roots;
            return roots;
        }

        // Parses libraryfolders.vdf for library paths (lenient) using Regex for robustness
        private static readonly Regex VdfPathRegex = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static IEnumerable<string> ParseLibraryFolders(string vdfPath)
        {
            if (!File.Exists(vdfPath)) return Enumerable.Empty<string>();
            
            var results = new List<string>();
            try
            {
                var text = File.ReadAllText(vdfPath);
                var matches = VdfPathRegex.Matches(text);
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 2)
                    {
                        var path = match.Groups[1].Value.Replace("\\\\", "\\").Replace('/', '\\');
                        if (Directory.Exists(path))
                            results.Add(path);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PathsService] Error parsing VDF {vdfPath}: {ex.Message}");
            }
            return results;
        }

        public static string? GetConanRoot()
        {
            if (_cachedConanRoot != null) return _cachedConanRoot;

            // Override via settings
            var settings = SettingsService.Load();
            if (!string.IsNullOrWhiteSpace(settings.ConanModsFolderOverride))
            {
                var mods = settings.ConanModsFolderOverride;
                if (Directory.Exists(mods))
                {
                    var conan = Directory.GetParent(mods)?.Parent?.FullName;
                    if (!string.IsNullOrWhiteSpace(conan) && Directory.Exists(conan))
                    {
                        _cachedConanRoot = conan;
                        return conan;
                    }
                }
            }

            // Search all libraries
            foreach (var apps in EnumerateSteamAppsRoots())
            {
                var conan = Path.Combine(apps, "common", "Conan Exiles");
                if (Directory.Exists(conan))
                {
                    _cachedConanRoot = conan;
                    return conan;
                }
            }
            return null;
        }

        public static void ClearCache()
        {
            _cachedSteamRoot = null;
            _cachedConanRoot = null;
            _cachedSteamAppsRoots = null;
        }

        public static string? GetConanModsFolder()
        {
            var settings = SettingsService.Load();
            if (!string.IsNullOrWhiteSpace(settings.ConanModsFolderOverride) && Directory.Exists(settings.ConanModsFolderOverride))
                return settings.ConanModsFolderOverride;

            var conan = GetConanRoot();
            if (conan == null) return null;
            var mods = Path.Combine(conan, "ConanSandbox", "Mods");
            try { Directory.CreateDirectory(mods); } catch { }
            return Directory.Exists(mods) ? mods : null;
        }

        public static string? GetConanSandboxFolder()
        {
            // Prefer override if user already selected Mods folder override
            var settings = SettingsService.Load();
            if (!string.IsNullOrWhiteSpace(settings.ConanModsFolderOverride) && Directory.Exists(settings.ConanModsFolderOverride))
            {
                var mods = settings.ConanModsFolderOverride;
                var sandbox = Directory.GetParent(mods)?.FullName;
                if (!string.IsNullOrWhiteSpace(sandbox) && Directory.Exists(sandbox))
                    return sandbox;
            }

            var conan = GetConanRoot();
            if (conan == null) return null;
            var sandboxPath = Path.Combine(conan, "ConanSandbox");
            return Directory.Exists(sandboxPath) ? sandboxPath : null;
        }

        public static string? GetConanServerModListTxt()
        {
            var settings = SettingsService.Load();
            if (!string.IsNullOrWhiteSpace(settings.ModListTxtOverride))
            {
                // If user pointed to old Mods\\modlist.txt, map to ConanSandbox\\servermodlist.txt
                try
                {
                    var selected = settings.ModListTxtOverride;
                    if (selected.EndsWith("modlist.txt", StringComparison.OrdinalIgnoreCase) && !selected.EndsWith("servermodlist.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        var modsDir = Path.GetDirectoryName(selected);
                        var sandbox = modsDir != null ? Directory.GetParent(modsDir)?.FullName : null;
                        if (!string.IsNullOrWhiteSpace(sandbox))
                            return Path.Combine(sandbox!, "servermodlist.txt");
                    }
                    if (selected.EndsWith("servermodlist.txt", StringComparison.OrdinalIgnoreCase))
                        return selected;
                }
                catch { }
            }

            var sandboxFolder = GetConanSandboxFolder();
            if (sandboxFolder == null) return null;
            return Path.Combine(sandboxFolder, "servermodlist.txt");
        }

        // Back-compat shim. Existing callers now target servermodlist.txt
        public static string? GetConanModListTxt() => GetConanServerModListTxt();

        public static string? GetWorkshopContent440900()
        {
            var settings = SettingsService.Load();
            if (!string.IsNullOrWhiteSpace(settings.Workshop440900Override) && Directory.Exists(settings.Workshop440900Override))
                return settings.Workshop440900Override;

            foreach (var apps in EnumerateSteamAppsRoots())
            {
                var p = Path.Combine(apps, "workshop", "content", "440900");
                if (Directory.Exists(p))
                    return p;
            }
            return null;
        }

        // Resolve Funcom Launcher executable
        public static string? GetFuncomLauncherExe()
        {
            var root = GetConanRoot();
            if (string.IsNullOrWhiteSpace(root)) return null;
            var launcher = Path.Combine(root, "Launcher", "FuncomLauncher.exe");
            return File.Exists(launcher) ? launcher : null;
        }

        public static string GetLocalAppDataConanConfigDir()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(localAppData, "ConanSandbox", "Saved", "Config", "WindowsNoEditor");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetLocalAppDataModListTxt()
        {
            var dir = GetLocalAppDataConanConfigDir();
            return Path.Combine(dir, "modlist.txt");
        }

        public static string? GetGameIni(bool useLocalAppData = true)
        {
            if (useLocalAppData)
            {
                var dir = GetLocalAppDataConanConfigDir();
                return Path.Combine(dir, "Game.ini");
            }
            
            var root = GetConanRoot();
            if (root == null) return null;
            return Path.Combine(root, "ConanSandbox", "Saved", "Config", "WindowsNoEditor", "Game.ini");
        }

        public static string? GetGameUserSettingsIni(bool useLocalAppData = true)
        {
            if (useLocalAppData)
            {
                var dir = GetLocalAppDataConanConfigDir();
                return Path.Combine(dir, "GameUserSettings.ini");
            }

            var root = GetConanRoot();
            if (root == null) return null;
            return Path.Combine(root, "ConanSandbox", "Saved", "Config", "WindowsNoEditor", "GameUserSettings.ini");
        }
    }
}