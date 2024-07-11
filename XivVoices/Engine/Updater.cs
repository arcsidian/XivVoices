using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace XivVoices.Engine
{
    public class Updater
    {
        #region Private Parameters
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private bool Active { get; set; } = false;
        private bool serverManifestLoaded = false;
        private bool localManifestLoaded = false;
        private bool toolsExist = false;
        private Dictionary<string, long> serverManifest;
        private Dictionary<string, long> localManifest;
        HashSet<string> splitFolders = new HashSet<string>()
        {
            "Alisaie/Alisaie",
              "Alphinaud/Alphinaud",
              "Aymeric/Aymeric",
              "Cid/Cid",
              "Estinien/Estinien",
              "Graha_Tia/Crystal_Exarch",
              "Graha_Tia/Graha_Tia",
              "Kan_E_Senna/Kan_E_Senna",
              "Krile/Krile",
              "Lyse/Lyse",
              "Merlwyb/Merlwyb",
              "Nanamo_Ul_Namo/Nanamo_Ul_Namo",
              "Tataru/Tataru",
              "Thancred/Thancred",
              "Urianger/Urianger",
              "Yshtola/Yshtola",
              "Yugiri/Yugiri",
              "Hien/Hien",
              "Minfilia/Minfilia",
              "Raubahn/Raubahn"
        };
        private string ServerManifestURL { get; } = "https://xivvoices.com/manifest.json";
        private string ServerFilesURL { get; } = "https://xivvoices.com/data/";
        #endregion


        #region Public Parameters
        public bool Busy { get; set; } = false;
        public DateTime ServerLastUpdate { get; set; } = new DateTime(1969, 7, 20);
        public List<int> State { get; set; } = new List<int>();
        public float ToolsDownloadState { get; set; } = 0;
        public LinkedList<DownloadInfo> DownloadInfoState { get; set; } = new LinkedList<DownloadInfo>();
        public int DataDownloadCount { get; set; } = 0;
        #endregion


        #region Core Methods
        public static Updater Instance;
        public Updater()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
                return;

            Active = true;
            Busy = false;
        }

        public void Dispose()
        {
            Active = false;
        }
        #endregion


        public async Task Check(bool calledByAutoUpdate = false, bool intialWindowState = true)
        {
            if (calledByAutoUpdate)
            {
                if (!intialWindowState && !XivEngine.Instance.Database.Plugin.Window.IsOpen)
                    XivEngine.Instance.Database.Plugin.Window.Toggle();
            }

            Busy = true;
            State.Clear();
            XivEngine.Instance.Database.UpdateDirectory();

            XivEngine.Instance.Database.Plugin.Config.Initialized = true;
            State.Add(1); // Checking Server Manifest State
            Plugin.PluginLog.Information("Updater: Check GetServerManifest");
            await GetServerManifest();

            State.Add(2); // Checking Local Manifest State
            Plugin.PluginLog.Information("Updater: Check GetLocalManifest");
            await GetLocalManifest();

            State.Add(3); // Checking Xiv Voices Tools State
            Plugin.PluginLog.Information("Updater: Check GetTools");
            await GetTools();

            // Manifest Check
            if (!serverManifestLoaded || !localManifestLoaded)
            {
                if (!serverManifestLoaded) XivEngine.Instance.Database.Plugin.PrintError("Server is updating right now!");
                if (!localManifestLoaded) XivEngine.Instance.Database.Plugin.PrintError("Local Manifest check failed!");
                State.Add(-1); // Error 1: Unable to load Manifests
                await Task.Delay(1000);
                State.Clear();
                return;
            }

            // Tools Check
            if (toolsExist)
            {
                State.Add(4); // All Tools Exist State 
                Plugin.PluginLog.Information("Updater: Tools exist!");
            }
            else
            {
                State.Add(5); // Tools Missing, Downloading...
                Plugin.PluginLog.Information("Updater: Tools are missing!");
                await DownloadAndExtractTools();
            }

            // Compare counts first for a quick mismatch check for MANIFEST
            bool manifestsMatch = true;
            if (serverManifest.Count != localManifest.Count)
            {
                manifestsMatch = false;
            }
            else
            {
                // Compare each entry.
                foreach (var kvp in serverManifest)
                {
                    if (!localManifest.TryGetValue(kvp.Key, out long localValue) || localValue != kvp.Value)
                    {
                        manifestsMatch = false;
                        break;
                    }
                }
            }

            if (manifestsMatch)
            {
                State.Add(7); // Up To Date State
                Plugin.PluginLog.Information("Updater: Manifests match!");
                await Task.Delay(2000);
            }
            else
            {
                State.Add(8); // Downloading Files...
                Plugin.PluginLog.Information("Updater: Manifests are different!");
                Plugin.PluginLog.Information($"Updater: serverManifest is {serverManifest.Count} while localManifest is {localManifest.Count}");
                await DownloadAndExtractData();
            }

            await Task.Delay(2000);
            State.Clear();
            Plugin.PluginLog.Information("Updater: State Cleared!");
            Busy = false;

            if (calledByAutoUpdate)
            {
                Plugin.PluginLog.Information("Updater: calledByAutoUpdate Process");
                if (!intialWindowState && XivEngine.Instance.Database.Plugin.Window.IsOpen)
                    XivEngine.Instance.Database.Plugin.Window.Toggle();
            }
            Plugin.PluginLog.Information("Updater: Done");
        }

        public async Task GetServerManifest()
        {
            if (!Active) return;

            serverManifestLoaded = false;
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    string response = await client.GetStringAsync(ServerManifestURL);
                    serverManifest = JsonConvert.DeserializeObject<Dictionary<string, long>>(response);
                    serverManifestLoaded = true;
                }
                catch (HttpRequestException e)
                {
                    Plugin.PluginLog.Error($"Updater: Error fetching server manifest: {e.Message}");
                }
            }
        }

        public async Task GetLocalManifest()
        {
            if (!Active) return;

            localManifestLoaded = false;
            localManifest = new Dictionary<string, long>();

            string[] folders = Directory.GetDirectories(XivEngine.Instance.Database.VoiceFilesPath);
            foreach (string folder in folders)
            {
                string[] subFolders = Directory.GetDirectories(folder);
                foreach (string subFolder in subFolders)
                {
                    string relativePath = subFolder.Replace(XivEngine.Instance.Database.VoiceFilesPath, "").Replace("\\", "/").TrimStart('/');
                    long size = await CalculateFolderSizeAsync(subFolder);

                    if (splitFolders.Contains(relativePath))
                    {
                        // If specific processing is required, adapt this method to be async as well
                        var splitResults = await SplitFolderByCharacterAsync(subFolder);
                        foreach (var entry in splitResults)
                        {
                            localManifest[entry.Key] = entry.Value;
                        }
                    }
                    else
                    {
                        localManifest[relativePath] = size;
                    }
                }
            }

            // Convert the manifest to JSON and save it
            string manifestJson = JsonConvert.SerializeObject(localManifest, Formatting.Indented);
            await File.WriteAllTextAsync(XivEngine.Instance.Database.DirectoryPath + "/manifest.json", manifestJson);

            Plugin.PluginLog.Information("Updater:Manifest created successfully.");
            localManifestLoaded = true;
        }

        public async Task GetTools()
        {
            if (!Active) return;

            await Task.Delay(100);
            toolsExist = false;

            // Check if the directory exists 
            if (!Directory.Exists(XivEngine.Instance.Database.ToolsPath))
            {
                Directory.CreateDirectory(XivEngine.Instance.Database.ToolsPath);
                return;
            }

            // Check for the existence of the tools
            bool ffmpegExists = File.Exists(Path.Combine(XivEngine.Instance.Database.ToolsPath, "ffmpeg.exe"));
            bool ffprobeExists = File.Exists(Path.Combine(XivEngine.Instance.Database.ToolsPath, "ffprobe.exe"));

            // If both files exist, set toolsExist to true
            if (ffmpegExists && ffprobeExists)
            {
                toolsExist = true;
            }
        }

        public async Task<long> CalculateFolderSizeAsync(string folderPath)
        {
            long totalSize = 0;
            await Task.Run(() =>
            {
                foreach (string file in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
                {
                    FileInfo fileInfo = new FileInfo(file);
                    totalSize += fileInfo.Length;
                }
            });
            return totalSize;
        }

        public async Task<Dictionary<string, long>> SplitFolderByCharacterAsync(string folderPath)
        {
            Dictionary<string, long> splitSizes = new Dictionary<string, long>();
            string baseFolderRelativePath = folderPath.Replace(XivEngine.Instance.Database.VoiceFilesPath, "").Replace("\\", "/").TrimStart('/');

            await Task.Run(() =>
            {
                foreach (string file in Directory.GetFiles(folderPath))
                {
                    string firstChar = Path.GetFileName(file)[0].ToString().ToLower();
                    if (!char.IsLetter(firstChar, 0))
                    {
                        firstChar = "z";
                    }

                    string key = $"{baseFolderRelativePath}__{firstChar}";

                    if (!splitSizes.ContainsKey(key))
                    {
                        splitSizes[key] = 0;
                    }

                    FileInfo fileInfo = new FileInfo(file);
                    splitSizes[key] += fileInfo.Length;
                }
            });
            return splitSizes;
        }

        private async Task DownloadAndExtractTools()
        {
            //XivEngine.Instance.Database.Plugin.Chat.Print("Download Start...");
            string toolsUrl = "https://github.com/arcsidian/XivVoices/releases/download/0.2.0.0/Tools.zip";
            string savePath = Path.Combine(XivEngine.Instance.Database.ToolsPath, "Tools.zip");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    using (HttpResponseMessage response = await client.GetAsync(toolsUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;
                        using (Stream contentStream = await response.Content.ReadAsStreamAsync(), fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            // Copy the content stream to the file stream, reporting progress
                            byte[] buffer = new byte[8192];
                            int bytesRead;
                            long totalRead = 0;
                            ToolsDownloadState = 0;
                            State.Add(6); // Downloading Tools State
                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalRead += bytesRead;
                                if (totalBytes.HasValue)
                                {
                                    ToolsDownloadState = (float)totalRead / totalBytes.Value * 100;
                                    //Engine.Instance.Database.Plugin.Chat.Print($"Downloaded {totalRead / 1024} KB of {totalBytes / 1024} KB ({ToolsDownloadState:F2}% complete)");
                                }
                            }
                        }

                        //XivEngine.Instance.Database.Plugin.Chat.Print("Download complete. Extracting...");

                        // Extract the zip file
                        string extractPath = XivEngine.Instance.Database.ToolsPath;
                        ZipFile.ExtractToDirectory(savePath, extractPath, true);
                        //XivEngine.Instance.Database.Plugin.Chat.Print("Extraction complete.");

                        // Delete the zip file after extracting
                        File.Delete(savePath);
                        //XivEngine.Instance.Database.Plugin.Chat.Print("Cleanup complete.");
                    }
                }
            }
            catch (Exception ex)
            {
                XivEngine.Instance.Database.Plugin.Chat.Print($"An error occurred: {ex.Message}");
            }
        }

        private async Task DownloadAndExtractData()
        {
            if (!Active) return;

            XivEngine.Instance.Database.Plugin.Chat.Print("Updating the software... Please wait...");
            List<string> deletedFiles = new List<string>();
            List<string> changedFiles = new List<string>();
            DownloadInfoState.Clear();
            DataDownloadCount = 0;

            // Determine which files need to be deleted or downloaded
            foreach (var pair in serverManifest)
            {
                if (!localManifest.ContainsKey(pair.Key))
                {
                    Plugin.PluginLog.Information($"Updater-DownloadAndExtractData: adding missing {pair.Key} to changedFiles");
                    changedFiles.Add(pair.Key);
                }
                else if (localManifest[pair.Key] != pair.Value)
                {
                    Plugin.PluginLog.Information($"Updater-DownloadAndExtractData: adding different {pair.Key} to changedFiles");
                    changedFiles.Add(pair.Key);
                }
            }

            foreach (var pair in localManifest)
            {
                if (!serverManifest.ContainsKey(pair.Key))
                {
                    Plugin.PluginLog.Information($"Updater-DownloadAndExtractData: adding {pair.Key} to deletedFiles");
                    deletedFiles.Add(pair.Key);
                }
            }

            DataDownloadCount = changedFiles.Count + 1;

            // Delete files
            foreach (var file in deletedFiles)
            {
                string fullPath = Path.Combine(XivEngine.Instance.Database.VoiceFilesPath, file);
                Plugin.PluginLog.Information($"Updater-DownloadAndExtractData: Deleting: {fullPath}");
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, recursive: true);
                }

                if (localManifest.ContainsKey(file))
                {
                    localManifest.Remove(file);
                }
            }
            await SaveLocalManifestAsync();


            // Erase existing .zip files
            var directoryInfo = new DirectoryInfo(XivEngine.Instance.Database.RootPath);
            foreach (var file in directoryInfo.GetFiles("*.zip"))
            {
                try
                {
                    file.Delete();
                    Plugin.PluginLog.Information($"Updater-DownloadAndExtractData: Successfully deleted: {file.FullName}");
                }
                catch (Exception e)
                {
                    Plugin.PluginLog.Error($"Updater-DownloadAndExtractData: Failed to delete {file.FullName}. Exception: {e.Message}");
                }
            }

            // Download changed files concurrently
            int maxConcurrentDownloads = 4;
            List<Task> downloadTasks = new List<Task>();
            foreach (var file in changedFiles)
            {
                if (!Active) return;

                downloadTasks.Add(ProcessFileAsync(file));
                if (downloadTasks.Count >= maxConcurrentDownloads)
                {
                    await Task.WhenAny(downloadTasks);
                    downloadTasks.RemoveAll(task => task.IsCompleted);
                }
            }

            // Wait for all remaining downloads to complete
            await Task.WhenAll(downloadTasks);

            State.Add(10); // Downloading Data State
            await XivEngine.Instance.Database.ReloadAndUpdateData();
            Plugin.PluginLog.Information("Updater: Update complete.");
        }

        private async Task ProcessFileAsync(string file)
        {
            if (!Active) return;

            DataDownloadCount--;
            // Handle file deletions based on naming conventions
            string baseDirectory = Path.Combine(XivEngine.Instance.Database.VoiceFilesPath, file);
            if (file.Contains("__"))
            {
                var parts = file.Split("__");
                baseDirectory = Path.Combine(XivEngine.Instance.Database.VoiceFilesPath, parts[0]);
                string specificFilePattern = parts[1] + "*";  // assuming naming like "subfolder__a" to delete all 'a*' files

                if (Directory.Exists(baseDirectory))
                {
                    var filesToDelete = Directory.GetFiles(baseDirectory, specificFilePattern);
                    foreach (var filePath in filesToDelete)
                        File.Delete(filePath);
                }
            }
            else
            {
                if (Directory.Exists(baseDirectory))
                    Directory.Delete(baseDirectory, true);
            }

            // Initiate file download and processing
            State.Add(9); // Downloading Data...
            string fileUrl = $"{ServerFilesURL}{file}.zip";
            string savePath = Path.Combine(XivEngine.Instance.Database.DirectoryPath, $"{Path.GetFileName(file)}_{XivEngine.Instance.Database.GenerateRandomSuffix()}.zip");
            file = file.Split('/')[1];
            if(DownloadInfoState.Count > 12)
            {
                var oldestFinished = DownloadInfoState.FirstOrDefault(ddi => ddi.status == "Finished");
                if (oldestFinished != null)
                {
                    DownloadInfoState.Remove(oldestFinished);
                }
            }
            string id = $"{file}_{XivEngine.Instance.Database.GenerateRandomSuffix()}";
            var downloadInfo = new DownloadInfo(id, file, "Downloading", 0f);
            DownloadInfoState.AddLast(downloadInfo);
            try
            {
                using (HttpClient client = new HttpClient())
                using (HttpResponseMessage response = await client.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead))
                using (Stream stream = await response.Content.ReadAsStreamAsync())
                using (FileStream fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var totalBytes = response.Content.Headers.ContentLength;
                    var totalRead = 0L;
                    var buffer = new byte[4096];
                    int bytesRead;

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        if (totalBytes.HasValue)
                        {
                            float progressPercentage = (float)totalRead / totalBytes.Value;
                            downloadInfo.percentage = progressPercentage;
                        }
                    }
                }

                downloadInfo.percentage = 1f;
                downloadInfo.status = "Processing";
                Plugin.PluginLog.Information($"Downloaded {file}. Extracting...");
                await UnzipAndDeleteFileAsync(savePath, XivEngine.Instance.Database.VoiceFilesPath);
                Plugin.PluginLog.Information($"Extracted {file}.");
                downloadInfo.status = "Finished";
            }
            catch (Exception ex)
            {
                downloadInfo.percentage = 0f;
                downloadInfo.status = "Failed";
                Plugin.PluginLog.Error($"Failed to process {file}. Exception: {ex.Message}");
            }
        }

        private async Task UnzipAndDeleteFileAsync(string zipPath, string extractPath)
        {
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractPath, true);
                File.Delete(zipPath);
                Plugin.PluginLog.Information($"Unzipped and deleted {zipPath}");
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Error($"Failed to unzip/delete {zipPath}. Exception: {e.Message}");
            }
        }

        private async Task SaveLocalManifestAsync()
        {
            string manifestJson = JsonConvert.SerializeObject(localManifest, Formatting.Indented);
            await File.WriteAllTextAsync(XivEngine.Instance.Database.DirectoryPath + "/manifest.json", manifestJson);
        }



    }
}
