using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConanMultiServerLauncher.Services
{
    public static class GameConfigService
    {
        // Updates LastConnected, LastPassword and StartedListenServerSession in Game.ini
        public static async Task UpdateLastConnectedServerAsync(string? serverAddress, string? password)
        {
            await UpdateGameIniAsync(serverAddress, password, false);
        }

        public static async Task UpdateSingleplayerModeAsync()
        {
            await UpdateGameIniAsync(null, null, true);
        }

        private static async Task UpdateGameIniAsync(string? serverAddress, string? password, bool isSingleplayer)
        {
            // Update BOTH LocalAppData and Game-folder config files
            await UpdateGameIniAtPathAsync(PathsService.GetGameIni(true), serverAddress, password, isSingleplayer);
            
            var gameFolderIni = PathsService.GetGameIni(false);
            if (!string.IsNullOrEmpty(gameFolderIni))
            {
                await UpdateGameIniAtPathAsync(gameFolderIni, serverAddress, password, isSingleplayer);
            }

            // To be absolutely sure, we also update GameUserSettings.ini for StartedListenServerSession
            await UpdateGameUserSettingsIniAsync(isSingleplayer);
        }

        private static async Task UpdateGameIniAtPathAsync(string iniPath, string? serverAddress, string? password, bool isSingleplayer)
        {
            if (string.IsNullOrEmpty(iniPath)) return;
            
            try
            {
                var directoryName = Path.GetDirectoryName(iniPath);
                if (directoryName != null) Directory.CreateDirectory(directoryName);
                
                var lines = File.Exists(iniPath) ? (await File.ReadAllLinesAsync(iniPath)).ToList() : new List<string>();

                // Sections we need to update
                EnsureSection(lines, "SavedServers");
                var (ssStart, ssEnd) = GetSectionRange(lines, "SavedServers");
                if (serverAddress != null)
                    UpsertKey(lines, ssStart, ssEnd, "LastConnected", serverAddress);
                if (password != null)
                    UpsertKey(lines, ssStart, ssEnd, "LastPassword", password);

                EnsureSection(lines, "SavedCoopData");
                var (scStart, scEnd) = GetSectionRange(lines, "SavedCoopData");
                UpsertKey(lines, scStart, scEnd, "StartedListenServerSession", isSingleplayer ? "True" : "False");

                // Redundantly update [ServerSettings] as some game versions use it
                EnsureSection(lines, "ServerSettings");
                var (serStart, serEnd) = GetSectionRange(lines, "ServerSettings");
                if (serverAddress != null)
                    UpsertKey(lines, serStart, serEnd, "LastConnected", serverAddress);
                if (password != null)
                    UpsertKey(lines, serStart, serEnd, "LastPassword", password);

                await WriteAtomicAsync(iniPath, lines);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameConfigService] Failed to update Game.ini at {iniPath}: {ex.Message}");
            }
        }

        private static async Task UpdateGameUserSettingsIniAsync(bool isSingleplayer)
        {
            await UpdateGameUserSettingsIniAtPathAsync(PathsService.GetGameUserSettingsIni(true), isSingleplayer);
            
            var gameFolderIni = PathsService.GetGameUserSettingsIni(false);
            if (!string.IsNullOrEmpty(gameFolderIni))
            {
                await UpdateGameUserSettingsIniAtPathAsync(gameFolderIni, isSingleplayer);
            }
        }

        private static async Task UpdateGameUserSettingsIniAtPathAsync(string iniPath, bool isSingleplayer)
        {
            if (string.IsNullOrEmpty(iniPath)) return;
            
            try
            {
                var directoryName = Path.GetDirectoryName(iniPath);
                if (directoryName != null) Directory.CreateDirectory(directoryName);
                
                var lines = File.Exists(iniPath) ? (await File.ReadAllLinesAsync(iniPath)).ToList() : new List<string>();

                EnsureSection(lines, "SavedCoopData");
                var (start, end) = GetSectionRange(lines, "SavedCoopData");
                UpsertKey(lines, start, end, "StartedListenServerSession", isSingleplayer ? "True" : "False");

                await WriteAtomicAsync(iniPath, lines);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameConfigService] Failed to update GameUserSettings.ini at {iniPath}: {ex.Message}");
            }
        }

        private static async Task WriteAtomicAsync(string path, List<string> lines)
        {
            var tempPath = path + ".tmp";
            await File.WriteAllLinesAsync(tempPath, lines, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tempPath, path);
        }

        private static (string? ip, int port) ParseServerAddress(string? serverAddress)
        {
            const int defaultPort = 27015;
            if (string.IsNullOrWhiteSpace(serverAddress))
                return (null, defaultPort);

            var s = serverAddress.Trim();
            // If IPv6 in brackets [::1]:port not expected here, but handle simple cases
            var parts = s.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts.Last(), out var parsedPort))
            {
                var host = string.Join(":", parts.Take(parts.Length - 1));
                return (host, parsedPort);
            }
            return (s, defaultPort);
        }

        private static void EnsureSection(List<string> lines, string sectionName)
        {
            if (lines.Any(l => IsSectionLine(l, sectionName))) return;
            if (lines.Count > 0 && lines[^1].Length != 0)
                lines.Add(string.Empty);
            lines.Add($"[{sectionName}]");
            // Add an empty line after for readability
            lines.Add(string.Empty);
        }

        private static (int start, int end) GetSectionRange(List<string> lines, string sectionName)
        {
            int start = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (IsSectionLine(lines[i], sectionName)) { start = i; break; }
            }
            if (start == -1)
            {
                // Should not happen if EnsureSection was called, but guard anyway
                lines.Add($"[{sectionName}]");
                start = lines.Count - 1;
                lines.Add(string.Empty);
            }

            int end = lines.Count;
            for (int i = start + 1; i < lines.Count; i++)
            {
                if (IsAnySectionLine(lines[i])) { end = i; break; }
            }
            return (start, end);
        }

        private static bool IsSectionLine(string line, string sectionName)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            line = line.Trim();
            return line.StartsWith("[") && line.EndsWith("]") &&
                   string.Equals(line.Trim('[', ']'), sectionName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAnySectionLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            line = line.Trim();
            return line.StartsWith("[") && line.EndsWith("]");
        }

        private static void UpsertKey(List<string> lines, int start, int end, string key, string value)
        {
            // Search within section range (exclusive of section header at index 'start')
            for (int i = start + 1; i < end; i++)
            {
                var raw = lines[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var idx = raw.IndexOf('=');
                if (idx <= 0) continue;
                var k = raw.Substring(0, idx).Trim();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = key + "=" + value;
                    return;
                }
            }
            // Not found: insert before end
            lines.Insert(end, key + "=" + value);
        }
    }
}
