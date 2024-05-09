#region Usings
using Dalamud.Game;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using XivVoices.Attributes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FFXIVClientStructs.FFXIV.Client.Game.Housing;
using Dalamud.Plugin.Services;
using System.Collections.Concurrent;
using XivVoices.Voice;
using System.Text.RegularExpressions;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.DragDrop;
using XivVoices.Services;
using Dalamud.Interface.Internal;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System.Threading.Tasks;
using XivVoices.Engine;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
#endregion

namespace XivVoices {
    public class Plugin : IDalamudPlugin {
        #region Fields
        private readonly DalamudPluginInterface pluginInterface;
        private readonly IChatGui _chat;
        private readonly IClientState _clientState;
        private IObjectTable _objectTable;
        private IToastGui _toast;
        private IGameConfig _gameConfig;
        private ISigScanner _sigScanner;
        private IGameInteropProvider _interopProvider;
        private IFramework _framework;
        private ITextureProvider _textureProvider;
        private bool texturesLoaded = false;

        private readonly PluginCommandManager<Plugin> commandManager;
        private readonly Configuration config;
        //public XIVVWebSocketServer webSocketServer;
        private readonly WindowSystem windowSystem;
        private PluginWindow _window { get; init; }

        private Filter _filter;

        private bool disposed;

        private Stopwatch _messageTimer = new Stopwatch();
        private Dictionary<string, string> _scdReplacements = new Dictionary<string, string>();
        private ConcurrentDictionary<string, List<KeyValuePair<string, bool>>> _papSorting = new ConcurrentDictionary<string, List<KeyValuePair<string, bool>>>();
        private ConcurrentDictionary<string, List<KeyValuePair<string, bool>>> _mdlSorting = new ConcurrentDictionary<string, List<KeyValuePair<string, bool>>>();

        private ConcurrentDictionary<string, string> _animationMods = new ConcurrentDictionary<string, string>();
        private Dictionary<string, List<string>> _modelMods = new Dictionary<string, List<string>>();

        private bool _hasBeenInitialized;
        uint LockCode = 0x6D617265;
        private AddonTalkManager _addonTalkManager;
        private AddonTalkHandler _addonTalkHandler;
        private IGameGui _gameGui;
        private int _recentCFPop;
        private unsafe FFXIVClientStructs.FFXIV.Client.Game.Camera* _camera;
        private MediaCameraObject _playerCamera;

        public IDalamudTextureWrap Logo;
        public IDalamudTextureWrap Icon;
        public IDalamudTextureWrap GeneralSettings;
        public IDalamudTextureWrap GeneralSettingsActive;
        public IDalamudTextureWrap DialogueSettings;
        public IDalamudTextureWrap DialogueSettingsActive;
        public IDalamudTextureWrap AudioSettings;
        public IDalamudTextureWrap AudioSettingsActive;
        public IDalamudTextureWrap Archive;
        public IDalamudTextureWrap ArchiveActive;
        public IDalamudTextureWrap Discord;
        public IDalamudTextureWrap KoFi;
        public IDalamudTextureWrap Changelog;
        public IDalamudTextureWrap ChangelogActive;
        public string Name => "XivVoices Plugin";

        public ISigScanner SigScanner { get => _sigScanner; set => _sigScanner = value; }

        internal Filter Filter
        {
            get
            {
                if (_filter == null)
                {
                    _filter = new Filter(this);
                    _filter.Enable();
                }
                return _filter;
            }
            set => _filter = value;
        }

        public IGameInteropProvider InteropProvider { get => _interopProvider; set => _interopProvider = value; }

        public Configuration Config => config;

        public PluginWindow Window => _window; 

        public MediaCameraObject PlayerCamera => _playerCamera;

        public IChatGui Chat => _chat;
        public IClientState ClientState => _clientState;
        public ITextureProvider TextureProvider => _textureProvider;

        public AddonTalkHandler AddonTalkHandler { get => _addonTalkHandler; set => _addonTalkHandler = value; }

        public XivVoices.Engine.Updater updater;
        public XivVoices.Engine.Database database;
        public XivVoices.Engine.Audio audio;
        public XivVoices.Engine.XivEngine xivEngine;
        

        #endregion
        #region Plugin Initiialization
        public unsafe Plugin(
            DalamudPluginInterface pi,
            ICommandManager commands,
            IChatGui chat,
            IClientState clientState,
            ISigScanner scanner,
            IObjectTable objectTable,
            IToastGui toast,
            IDataManager dataManager,
            IGameConfig gameConfig,
            IFramework framework,
            IGameInteropProvider interopProvider,
            ICondition condition,
            IGameGui gameGui,
            IDragDropManager dragDrop,
            ITextureProvider textureProvider) {
            #region Constructor
            try {
                Service.DataManager = dataManager;
                Service.SigScanner = scanner;
                Service.GameInteropProvider = interopProvider;
                Service.ChatGui = chat;
                Service.ClientState = clientState;
                Service.ObjectTable = objectTable;
                this.pluginInterface = pi;
                this._chat = chat;
                this._clientState = clientState; 
                // Get or create a configuration object
                this.config = this.pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
                this.config.Initialize(this.pluginInterface);
                //webSocketServer = new XIVVWebSocketServer(this.config, this);
                // Initialize the UI
                this.windowSystem = new WindowSystem(typeof(Plugin).AssemblyQualifiedName);
                _window = this.pluginInterface.Create<PluginWindow>();
                pluginInterface.UiBuilder.DisableAutomaticUiHide = true;
                pluginInterface.UiBuilder.DisableGposeUiHide = true;
                _window.ClientState = this._clientState;
                _window.Configuration = this.config;
                _window.PluginInterface = this.pluginInterface;
                _window.PluginReference = this;
                if (_window is not null)
                {
                    this.windowSystem.AddWindow(_window);
                }
                this.pluginInterface.UiBuilder.Draw += UiBuilder_Draw;
                this.pluginInterface.UiBuilder.OpenConfigUi += UiBuilder_OpenConfigUi;

                // Load all of our commands
                this.commandManager = new PluginCommandManager<Plugin>(this, commands);
                _toast = toast;
                _gameConfig = gameConfig;
                _sigScanner = scanner;
                _textureProvider = textureProvider;
                _interopProvider = interopProvider;
                _objectTable = objectTable;
                _framework = framework;
                _framework.Update += framework_Update;
                _addonTalkManager = new AddonTalkManager(_framework, _clientState, condition, gameGui);
                _addonTalkHandler = new AddonTalkHandler(_addonTalkManager, _framework, _objectTable, clientState, this, chat, scanner);
                _gameGui = gameGui;
                _camera = CameraManager.Instance()->GetActiveCamera();
                _playerCamera = new MediaCameraObject(_camera);

            } catch (Exception e) {
                Dalamud.Logging.PluginLog.LogWarning(e, e.Message);
                _chat.PrintError("[XivVoices] Fatal Error, the plugin did not initialize correctly!\n" + e.Message);
            }
            #endregion
        }

        private void InitializeEverything() {
            try { 
                _chat.ChatMessage += Chat_ChatMessage;
                _clientState.Login += _clientState_Login;
                _clientState.Logout += _clientState_Logout;
                _clientState.TerritoryChanged += _clientState_TerritoryChanged;
                Filter = new Filter(this);
                Filter.Enable();
                Filter.OnSoundIntercepted += _filter_OnSoundIntercepted;
                if (_clientState.IsLoggedIn) {
                    _chat.Print("XivVoices is live.");
                }

                
                database = new Database(this.pluginInterface, this);
                updater = new Updater();
                audio = new Audio(this);
                xivEngine = new XivEngine(this.database, this.audio, this.updater);

            } catch (Exception e) {
                Dalamud.Logging.PluginLog.LogWarning(e, e.Message);
                _chat.PrintError("[XivVoicesInitializer] Fatal Error, the plugin did not initialize correctly!\n" + e.Message);
            }
        }

        private void _clientState_CfPop(Lumina.Excel.GeneratedSheets.ContentFinderCondition obj) {
            _recentCFPop = 1;
        }
        #endregion Plugin Initiialization
        #region Debugging
        public void Print(string text)
        {
            this.Chat.Print(text);
        }
        public void PrintLog(string text)
        {
            if (this.Config.FrameworkActive)
                this.Chat.Print(text);
        }
        #endregion
        #region Sound Management
        private void framework_Update(IFramework framework) {
            if (!disposed) {
                if (!_hasBeenInitialized && _clientState.LocalPlayer != null) {
                    InitializeEverything();
                    _hasBeenInitialized = true;
                }
            }
        }

        private void Chat_ChatMessage(XivChatType type, uint senderId,
        ref SeString sender, ref SeString message, ref bool isHandled) {
            if (!disposed) {
                if (!config.Active || !config.Initialized) return;

                string playerName = "";
                try {
                    foreach (var item in sender.Payloads) {
                        PlayerPayload player = item as PlayerPayload;
                        TextPayload text = item as TextPayload;
                        if (player != null) {
                            playerName = player.PlayerName;
                            break;
                        }
                        if (text != null) {
                            playerName = text.Text;
                            break;
                        }
                    }
                } catch {

                }

                if (type == XivChatType.NPCDialogue){
                    //string correctedMessage = _addonTalkHandler.StripPlayerNameFromNPCDialogue(_addonTalkHandler.ConvertRomanNumberals(message.TextValue.TrimStart('.')));
                    //webSocketServer.BroadcastMessage($"=====> lastNPCDialogue [{_addonTalkHandler.lastNPCDialogue}]\n=========> current [{playerName + cleanedMessage}]");
                    // Check for Cancel
                    string cleanedMessage = _addonTalkHandler.CleanSentence(message.TextValue);
                    if (_addonTalkHandler.lastNPCDialogue == playerName + cleanedMessage)
                    {
                        if(config.SkipEnabled)
                            ChatText(playerName, cleanedMessage, type, senderId, true);
                    }
                    return;
                }

                if (type == XivChatType.NPCDialogueAnnouncements)
                {
                    HandleNPCDialogueAnnouncements(playerName,type,senderId,message.TextValue);
                    return;
                }

                switch (type) {
                    case XivChatType.Say:
                        if (config.SayEnabled)
                        {
                            ChatText(playerName, message.TextValue, type, senderId);
                        }
                        break;
                    case XivChatType.TellIncoming:
                        if (config.TellEnabled)
                            ChatText(playerName, message.TextValue, type, senderId);
                        break;
                    case XivChatType.TellOutgoing:
                        break;
                    case XivChatType.Shout:
                    case XivChatType.Yell:
                        if (config.ShoutEnabled)
                            ChatText(playerName, message.TextValue, type, senderId);
                        break;
                    case XivChatType.CustomEmote:
                        break;
                    case XivChatType.Party:
                    case XivChatType.CrossParty:
                        if (config.PartyEnabled)
                            ChatText(playerName, message.TextValue, type, senderId);
                        break;
                    case XivChatType.Alliance:
                        if (config.AllianceEnabled)
                            ChatText(playerName, message.TextValue, type, senderId);
                        break;
                    case XivChatType.FreeCompany:
                        if (config.FreeCompanyEnabled)
                            ChatText(playerName, message.TextValue, type, senderId);
                        break;
                    case XivChatType.NPCDialogue:
                        break;
                    case XivChatType.NPCDialogueAnnouncements:
                        break;
                    case XivChatType.CrossLinkShell1:
                    case XivChatType.CrossLinkShell2:
                    case XivChatType.CrossLinkShell3:
                    case XivChatType.CrossLinkShell4:
                    case XivChatType.CrossLinkShell5:
                    case XivChatType.CrossLinkShell6:
                    case XivChatType.CrossLinkShell7:
                    case XivChatType.CrossLinkShell8:
                    case XivChatType.Ls1:
                    case XivChatType.Ls2:
                    case XivChatType.Ls3:
                    case XivChatType.Ls4:
                    case XivChatType.Ls5:
                    case XivChatType.Ls6:
                    case XivChatType.Ls7:
                    case XivChatType.Ls8:
                        if (config.LinkshellEnabled)
                            ChatText(playerName, message.TextValue, type, senderId);
                        break;
                    case (XivChatType)2729:
                    case (XivChatType)2091:
                    case (XivChatType)2234:
                    case (XivChatType)2730:
                    case (XivChatType)2219:
                    case (XivChatType)2859:
                    case (XivChatType)2731:
                    case (XivChatType)2106:
                    case (XivChatType)10409:
                    case (XivChatType)8235:
                    case (XivChatType)9001:
                    case (XivChatType)4139:
                        break;
                }

                
            }
        }

        private async void HandleNPCDialogueAnnouncements(string playerName, XivChatType type, uint senderId, string message){
            if (!config.Active || !config.Initialized) return;

            await Task.Delay(250);
            string cleanedMessage = _addonTalkHandler.CleanSentence(message);

            if (_addonTalkHandler.lastBubbleDialogue == cleanedMessage)
            {
                //webSocketServer.SendMessage($"NPCDialogueAnnouncement blocked: {cleanedMessage}");
                return;
            }

            if (config.BattleDialoguesEnabled)
            {
                ChatText(playerName, cleanedMessage, type, senderId);
                _addonTalkHandler.lastBattleDialogue = cleanedMessage;
            }

        }

        private void ChatText(string sender, SeString message, XivChatType type, uint senderId, bool cancel = false) {
            if (!config.Active || !config.Initialized) return;

            if (sender.Length == 1)
                sender = _clientState.LocalPlayer.Name.TextValue;

            string stringtype = type.ToString();
            string correctSender = _addonTalkHandler.CleanSender(sender);
            string user = $"{ClientState.LocalPlayer.Name}@{ClientState.LocalPlayer.HomeWorld.GameData.Name}";

            if (cancel)
            {
                stringtype = "Cancel";
                XivEngine.Instance.Process(stringtype, correctSender, "-1", "-1", message.ToString(), "-1", "-1", "-1", "-1", "-1", _clientState.ClientLanguage.ToString(), new Vector3(-99), null, user);
                return;
            }

            // Default Paramters
            Character character = null;
            string id = "-1";
            string skeleton = "-1";
            string body = "-1";
            string gender = "default";
            string race = "-1";
            string tribe = "-1";
            string eyes = "-1";

            
            // Get Character Data
            if (sender.Contains(_clientState.LocalPlayer.Name.TextValue)){
                character = _clientState.LocalPlayer;
            }
            else {
                character = _addonTalkHandler.GetCharacterFromName(sender);
            }

            // Fill the Parameters
            if (character == null)
            {
                if(this.database.PlayerData.ContainsKey(sender))
                {
                    body = this.database.PlayerData[sender].Body.ToString();
                    gender = this.database.PlayerData[sender].Gender.ToString();
                    race = this.database.PlayerData[sender].Race.ToString();
                    tribe = this.database.PlayerData[sender].Tribe.ToString();
                    eyes = this.database.PlayerData[sender].EyeShape.ToString();
                }
            }
            else
            {
                id = character.DataId.ToString();
                body = character.Customize[(int)CustomizeIndex.ModelType].ToString();
                gender = Convert.ToBoolean(character.Customize[(int)CustomizeIndex.Gender]) ? "Female" : "Male";
                race = character.Customize[(int)CustomizeIndex.Race].ToString();
                tribe = character.Customize[(int)CustomizeIndex.Tribe].ToString();
                eyes = character.Customize[(int)CustomizeIndex.EyeShape].ToString();

                if (type == XivChatType.TellIncoming || type == XivChatType.Party || type == XivChatType.Alliance || type == XivChatType.FreeCompany)
                {
                    var playerCharacter = new PlayerCharacter
                    {
                        Body = character.Customize[(int)CustomizeIndex.ModelType].ToString(),
                        Gender = Convert.ToBoolean(character.Customize[(int)CustomizeIndex.Gender]) ? "Female" : "Male",
                        Race = character.Customize[(int)CustomizeIndex.Race].ToString(),
                        Tribe = character.Customize[(int)CustomizeIndex.Tribe].ToString(),
                        EyeShape = character.Customize[(int)CustomizeIndex.EyeShape].ToString()
                    };
                    this.database.UpdateAndSavePlayerData(sender, playerCharacter);
                }
                
            }

            //Chat.Print($"{correctSender}: id[{id}] skeleton[{skeleton}] body[{body}] gender[{gender}] race[{race}] tribe[{tribe}] eyes[{eyes}]");
            XivEngine.Instance.Process(stringtype, correctSender, id, skeleton, message.ToString(), body, gender, race, tribe, eyes, _clientState.ClientLanguage.ToString(), new Vector3(-99), character, user);
        }

        public void TriggerLipSync(Character character, string length)
        {
            if (config.LipsyncEnabled && character != null)
                _addonTalkHandler.TriggerLipSync(character, length);
        }

        public void StopLipSync(Character character)
        {
            if (config.LipsyncEnabled && character != null)
                _addonTalkHandler.StopLipSync(character);
        }


        public int GetNumberFromString(string value) {
            try {
                return int.Parse(value.Split('.')[1]) - 1;
            } catch {
                return -1;
            }
        }

        
        private void _filter_OnSoundIntercepted(object sender, InterceptedSound e)
        {
            
                if (_scdReplacements.ContainsKey(e.SoundPath))
                {
                    if (!e.SoundPath.Contains("se_vfx_monster"))
                    {
                        Dalamud.Logging.PluginLog.Log("Sound Mod Intercepted");
#if DEBUG
                        _chat.Print("Sound Mod Intercepted");
#endif
                    }
                }
        }

        

        private unsafe void _clientState_TerritoryChanged(ushort e) {
#if DEBUG
            _chat.Print("Territory is " + e);
#endif
        }
        private unsafe bool IsResidential() {
            return HousingManager.Instance()->IsInside() || HousingManager.Instance()->OutdoorTerritory != null;
        }

        private void _clientState_Logout() {
        }

        private void _clientState_Login() {
        }

#endregion
        #region String Sanitization
        public string RemoveActionPhrases(string value) {
            return value.Replace("Direct hit ", null)
                    .Replace("Critical direct hit ", null)
                    .Replace("Critical ", null)
                    .Replace("Direct ", null)
                    .Replace("direct ", null);
        }
        public static string CleanSenderName(string senderName) {
            string[] senderStrings = SplitCamelCase(RemoveSpecialSymbols(senderName)).Split(" ");
            string playerSender = senderStrings.Length == 1 ? senderStrings[0] : senderStrings.Length == 2 ?
                (senderStrings[0] + " " + senderStrings[1]) :
                (senderStrings[0] + " " + senderStrings[2]);
            return playerSender;
        }
        public static string SplitCamelCase(string input) {
            return Regex.Replace(input, "([A-Z])", " $1",
                RegexOptions.Compiled).Trim();
        }
        public static string RemoveSpecialSymbols(string value) {
            Regex rgx = new Regex(@"[^a-zA-Z:/._\ -]");
            return rgx.Replace(value, "");
        }
        #endregion
        #region UI Management
        private void UiBuilder_Draw() {
            if (!texturesLoaded)
            {
                try
                {
                    var imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "logo.png"));
                    Logo = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "icon.png"));
                    Icon = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "generalSettings.png"));
                    GeneralSettings = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "generalSettingsActive.png"));
                    GeneralSettingsActive = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "dialogueSettings.png"));
                    DialogueSettings = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "dialogueSettingsActive.png"));
                    DialogueSettingsActive = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "audioSettings.png"));
                    AudioSettings = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "audioSettingsActive.png"));
                    AudioSettingsActive = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "archive.png"));
                    Archive = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "archiveActive.png"));
                    ArchiveActive = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "discord.png"));
                    Discord = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "ko-fi.png"));
                    KoFi = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "changelog.png"));
                    Changelog = _textureProvider.GetTextureFromFile(imagePath);
                    imagePath = new FileInfo(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "changelogActive.png"));
                    ChangelogActive = _textureProvider.GetTextureFromFile(imagePath);
                    if (Logo != null)
                    {
                        texturesLoaded = true; // Set the flag if the logo is successfully loaded
                    }
                }
                catch (Exception e)
                {
                    _chat.PrintError("Failed to load textures: " + e.Message);
                }
            }
            this.windowSystem.Draw();
        }
        private void UiBuilder_OpenConfigUi() {
            _window.Toggle();
        }
        #endregion
        #region Chat Commands
        [Command("/xivv")]
        [HelpMessage("OpenConfig")]
        public void ExecuteCommandA(string command, string args) {
            OpenConfig(command, args);
        }
        public void OpenConfig(string command, string args) {
            if (!disposed) {
                string[] splitArgs = args.Split(' ');
                if (splitArgs.Length > 0) {
                    switch (splitArgs[0].ToLower()) {
                        case "help":
                            _chat.Print("Xiv Voices Commands:\r\n" +
                             "on (Enable Xiv Voices)\r\n" +
                             "off (Disable Xiv Voices)");
                            break;
                        case "on":
                            config.Active = true;
                            _window.Configuration = config;
                            this.pluginInterface.SavePluginConfig(config);
                            config.Active = true;
                            break;
                        case "off":
                            config.Active = false;
                            _window.Configuration = config;
                            this.pluginInterface.SavePluginConfig(config);
                            config.Active = false;
                            break;
                        case "mute":
                            config.Mute = true;
                            _window.Configuration = config;
                            this.pluginInterface.SavePluginConfig(config);
                            config.Mute = true;
                            break;
                        case "unmute":
                            config.Mute = false;
                            _window.Configuration = config;
                            this.pluginInterface.SavePluginConfig(config);
                            config.Mute = false;
                            break;
                        case "skip":
                            this.audio.StopAudio();
                            break;
                        default:
                            _window.Toggle();
                            break;
                    }
                }
            }
        }
        #endregion
        #region IDisposable Support
        protected virtual void Dispose(bool disposing) {
            if (!disposing) return;
            try { 
                disposed = true;
                config.Save();
                
                _chat.ChatMessage -= Chat_ChatMessage;
                this.pluginInterface.UiBuilder.Draw -= UiBuilder_Draw;
                this.pluginInterface.UiBuilder.OpenConfigUi -= UiBuilder_OpenConfigUi;
                this.windowSystem.RemoveAllWindows();
                this.commandManager?.Dispose();
                if (_filter != null)
                {
                    _filter.OnSoundIntercepted -= _filter_OnSoundIntercepted;
                }
                try {
                    _clientState.Login -= _clientState_Login;
                    _clientState.Logout -= _clientState_Logout;
                    _clientState.TerritoryChanged -= _clientState_TerritoryChanged;
                } catch (Exception e) {
                    Dalamud.Logging.PluginLog.LogWarning(e, e.Message);
                }
                try {
                    _framework.Update -= framework_Update;
                } catch (Exception e) {
                    Dalamud.Logging.PluginLog.LogWarning(e, e.Message);
                }
                Filter?.Dispose();
                _addonTalkHandler?.Dispose();

                updater?.Dispose();
                database?.Dispose();
                audio?.Dispose();
                xivEngine?.Dispose();
                //webSocketServer.Stop();
            } catch (Exception e) {
                Dalamud.Logging.PluginLog.LogWarning(e, e.Message);
            }
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}