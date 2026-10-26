using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using System.IO.Compression;

namespace Silk
{
    public static class Updater
    {
        private static string LatestVersionUrl => Config.GetConfigValue<string>("updater.latestVersionUrl") ?? "https://raw.githubusercontent.com/SilkModding/Silk/master/version";
        private static string DownloadUrl => Config.GetConfigValue<string>("updater.downloadUrl") ?? "https://github.com/SilkModding/Silk/releases/download/v{0}/Silk-v{0}.zip";
        private const string TempDownloadPath = "SilkUpdate.zip";

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        // TODO: This does not work. winhttp.dll/doorstop does not cooperate.
        // It *does* restart the game, but Silk is not started along with it.
        public static async Task RestartAfterUpdate()
        {
            Logger.LogInfo("Restarting game after update...");
            await Task.Delay(5000);

            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Silk", "Updater.exe");
            var startInfo = new ProcessStartInfo(exePath, Process.GetCurrentProcess().Id.ToString())
            {
                UseShellExecute = true,
                CreateNoWindow = false
            };

            FreeConsole();
            Process.Start(startInfo);
            Application.Quit(0);
        }

        public static void RunUpdateExtractor()
        {
            var updateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Silk", "Updater");
            var updaterDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Updater");

            // Move the update extractor from /Silk/Updates/* to /Updater/
            foreach (var file in Directory.GetFiles(updateDir))
            {
                var newFile = Path.Combine(updaterDir, Path.GetFileName(file));
                File.Move(file, newFile);
            }
            Directory.Delete(updateDir);

            var path = Path.Combine(updaterDir, "Updater.exe");
            var currentProcessId = Process.GetCurrentProcess().Id;

            var startInfo = new ProcessStartInfo(path, currentProcessId.ToString())
            {
                UseShellExecute = true,
                CreateNoWindow = false
            };

            Process.Start(startInfo);
            Application.Quit(0);
        }

        // updater entrypoint
        public static async Task CheckForUpdates()
        {
            if (!Config.GetConfigValue<bool>("updater.checkForUpdates")) return;
            Logger.LogInfo("Checking for updates...");
            string latestVersion;
            try
            {
                latestVersion = await GetLatestVersion();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not check for Silk updates: {ex.Message}");
                return;
            }
            Logger.LogInfo($"Latest version: {latestVersion}");
            if (VersionHandler.IsOlderThan(latestVersion))
            {
                Logger.LogInfo("A new version of Silk is available!");
                var downloadUrl = string.Format(DownloadUrl, latestVersion);
                Logger.LogInfo($"Download: {downloadUrl}");

                Announcer.TwoOptionsPopup(
                    "A new Silk version is available! Do you want to update?\nNote: you will need to restart the game to apply the update",
                    "Yes", "No",
                    () =>
                    {
                        try
                        {
                            Announcer.InformationPopup($"Downloading and installing Silk version {latestVersion}...\nDo not close your game...");
                            DownloadAndInstallUpdate(downloadUrl);
                            Announcer.InformationPopup($"Download Finished! You must restart the game manually for this update to apply\n(We know it's not ideal, but we are working on something to do this automatically.)");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("An error occurred during the update process.");
                            Logger.LogError(ex.Message);
                            Announcer.InformationPopup("An error occurred while downloading the update. Please try again later.");
                        }
                    }, () => {
                        Logger.LogInfo("Update declined, continuing with current version.");
                    },
                null);
            }
            else
            {
                Logger.LogInfo("No updates available.");
            }
        }

        /// <summary>
        /// Downloads the latest version string from the web.
        /// </summary>
        /// <returns>A task that resolves to the latest version string.</returns>
        private static async Task<string> GetLatestVersion()
        {
            Logger.LogInfo("Checking for latest version...");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.GetStringAsync(LatestVersionUrl);
            return response.Trim();
        }

        /// <summary>
        /// Downloads and installs the given update.
        /// </summary>
        /// <param name="downloadUrl">The URL to download the update from.</param>
        /// <remarks>
        /// This method will download the update to the temporary download path and then run the update extractor.
        /// </remarks>
        private static void DownloadAndInstallUpdate(string downloadUrl)
        {
            Logger.LogInfo("Starting download...");

            // Path logging
            Logger.Log("Temp download path: " + TempDownloadPath);
            Logger.Log("Download URL: " + downloadUrl);

            // Download the update
            using (WebClient client = new())
            {
                client.DownloadFile(new Uri(downloadUrl), TempDownloadPath);
            }
            Logger.LogInfo($"Downloaded to {TempDownloadPath}");

            RunUpdateExtractor();
        }
    }
}

