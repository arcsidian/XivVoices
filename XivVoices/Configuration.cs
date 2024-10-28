using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.IO;
using System.Text.Json;

namespace XivVoices
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;
        public bool Active { get; set; } = true;
        public bool Initialized { get; set; } = false;
        public string WorkingDirectory { get; set; } = "C:/XIV_Voices";
        public bool Reports { get; set; } = false;
        public bool AnnounceReports { get; set; } = true;
        public bool OnlineRequests { get; set; } = false;
        public bool ReplaceVoicedARRCutscenes { get; set; } = true;
        public bool LipsyncEnabled { get; set; } = true;
        public bool SkipEnabled { get; set; } = true;

        // Chat Settings
        public bool SayEnabled { get; set; } = true;
        public bool TellEnabled { get; set; } = true;
        public bool ShoutEnabled { get; set; } = true;
        public bool PartyEnabled { get; set; } = true;
        public bool AllianceEnabled { get; set; } = true;
        public bool FreeCompanyEnabled { get; set; } = true;
        public bool LinkshellEnabled { get; set; } = true;
        public bool BattleDialoguesEnabled { get; set; } = true;
        public bool RetainersEnabled { get; set; } = true;
        public bool BubblesEnabled { get; set; } = true;
        public bool BubblesEverywhere { get; set; } = true;
        public bool BubblesInSafeZones { get; set; } = false;
        public bool BubblesInBattleZones { get; set; } = false;
        public bool BubbleChatEnabled { get; set; } = true;

        // Engine Settings
        public bool Mute { get; set; } = false;
        public int Volume { get; set; } = 100;
        public int Speed { get; set; } = 100;
        public int AudioEngine { get; set; } = 1;
        public bool PollyEnabled { get; set; } = false;
        public bool LocalTTSEnabled { get; set; } = false;
        public string LocalTTSMale { get; set; } = "en-gb-northern_english_male-medium";
        public string LocalTTSFemale { get; set; } = "en-gb-jenny_dioco-medium";
        public int LocalTTSUngendered { get; set; } = 1;
        public int LocalTTSVolume { get; set; } = 100;
        public bool LocalTTSPlayerSays { get; set; } = false;
        public bool IgnoreNarratorLines { get; set; } = false;

        // Framework Settings
        public bool FrameworkActive { get; set; } = false;
        public bool FrameworkOnline { get; set; } = false;

        // the below exist just to make saving less cumbersome
        [NonSerialized]
        private IDalamudPluginInterface? PluginInterface;
        private const string ConfigPath = "C:/XIV_Voices/Tools/config.json";

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
            Load();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    // Read and deserialize the JSON file
                    string jsonString = File.ReadAllText(ConfigPath);
                    var loadedConfig = JsonSerializer.Deserialize<Configuration>(jsonString);

                    if (loadedConfig != null)
                    {
                        // Update the current instance with loaded data
                        this.Active = loadedConfig.Active;
                        this.Initialized = loadedConfig.Initialized;
                        this.WorkingDirectory = loadedConfig.WorkingDirectory;
                        this.Reports = loadedConfig.Reports? loadedConfig.Reports : false;
                        this.AnnounceReports = loadedConfig.AnnounceReports? loadedConfig.AnnounceReports : true;
                        this.OnlineRequests = loadedConfig.OnlineRequests ? loadedConfig.OnlineRequests : false;
                        this.ReplaceVoicedARRCutscenes = loadedConfig.ReplaceVoicedARRCutscenes;
                        this.LipsyncEnabled = loadedConfig.LipsyncEnabled;
                        this.SkipEnabled = loadedConfig.SkipEnabled;
                        this.SayEnabled = loadedConfig.SayEnabled;
                        this.TellEnabled = loadedConfig.TellEnabled;
                        this.ShoutEnabled = loadedConfig.ShoutEnabled;
                        this.PartyEnabled = loadedConfig.PartyEnabled;
                        this.AllianceEnabled = loadedConfig.AllianceEnabled;
                        this.FreeCompanyEnabled = loadedConfig.FreeCompanyEnabled;
                        this.LinkshellEnabled = loadedConfig.LinkshellEnabled;
                        this.BattleDialoguesEnabled = loadedConfig.BattleDialoguesEnabled;
                        this.RetainersEnabled = loadedConfig.RetainersEnabled;
                        this.BubblesEnabled = loadedConfig.BubblesEnabled;
                        this.BubblesEverywhere = loadedConfig.BubblesEverywhere;
                        this.BubblesInSafeZones = loadedConfig.BubblesInSafeZones;
                        this.BubblesInBattleZones = loadedConfig.BubblesInBattleZones;
                        this.BubbleChatEnabled = loadedConfig.BubbleChatEnabled;
                        this.Mute = loadedConfig.Mute;
                        this.Volume = loadedConfig.Volume;
                        this.Speed = loadedConfig.Speed;
                        this.AudioEngine = loadedConfig.AudioEngine;
                        this.PollyEnabled = loadedConfig.PollyEnabled;
                        this.LocalTTSEnabled = loadedConfig.LocalTTSEnabled;
                        this.LocalTTSMale = loadedConfig.LocalTTSMale;
                        this.LocalTTSFemale = loadedConfig.LocalTTSFemale;
                        this.LocalTTSUngendered = loadedConfig.LocalTTSUngendered;
                        this.LocalTTSVolume = loadedConfig.LocalTTSVolume;
                        this.LocalTTSPlayerSays = loadedConfig.LocalTTSPlayerSays;
                        this.IgnoreNarratorLines = loadedConfig.IgnoreNarratorLines;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
            }
        }

        public void Save()
        {
            try
            {
                // Ensure the directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));

                // Serialize the current configuration to JSON
                string jsonString = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }
    }
}
