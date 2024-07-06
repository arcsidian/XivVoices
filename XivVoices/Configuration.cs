using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace XivVoices
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;
        public string Port { get; set; } = "16969";
        public bool Active { get; set; } = true;
        public bool Initialized { get; set; } = false;
        public string WorkingDirectory { get; set; } = "C:/XIV_Voices";
        public bool Reports { get; set; } = false;
        public bool AutoUpdate { get; set; } = true;
        public bool UpdateAudioNotification { get; set; } = true;
        public DateTime LastUpdate { get; set; } = new DateTime(1969, 7, 20);
        public string WebsocketStatus { get; set; } = "";
        public bool ReplaceVoicedARRCutscenes { get; set; } = true;
        public bool LipsyncEnabled { get; set; } = true;
        public bool SkipEnabled { get; set; } = true;
        public bool AnnounceReports { get; set; } = true;
        public bool OnlineRequests { get; set; } = false;

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
        public bool WebsocketRedirectionEnabled { get; set; } = false;

        // Framework Settings
        public bool FrameworkActive { get; set; } = false;
        public bool FrameworkOnline { get; set; } = false;

        // the below exist just to make saving less cumbersome
        [NonSerialized]
        private IDalamudPluginInterface? PluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.PluginInterface!.SavePluginConfig(this);
        }
    }
}
