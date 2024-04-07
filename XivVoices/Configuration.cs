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
        public string WebsocketStatus { get; set; } = "";
        public bool ReplaceVoicedARRCutscenes { get; set; } = true;
        public bool LipsyncEnabled { get; set; } = true;
        public bool SkipEnabled { get; set; } = true;

        // Chat Settings
        public bool SayEnabled { get; set; } = true;
        public bool TellEnabled { get; set; } = true;
        public bool ShoutEnabled { get; set; } = true;
        public bool PartyEnabled { get; set; } = true;
        public bool FreeCompanyEnabled { get; set; } = true;
        public bool BattleDialoguesEnabled { get; set; } = true;
        public bool BubblesEnabled { get; set; } = true;

        // the below exist just to make saving less cumbersome
        [NonSerialized]
        private DalamudPluginInterface? PluginInterface;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.PluginInterface!.SavePluginConfig(this);
        }
    }
}
