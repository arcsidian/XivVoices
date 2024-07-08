using Anamnesis;
using Anamnesis.Actor;
using Anamnesis.Core.Memory;
using Anamnesis.GameData.Excel;
using Anamnesis.Memory;
using Anamnesis.Services;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Logging;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using XivVoices.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using ICharacter = Dalamud.Game.ClientState.Objects.Types.ICharacter;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;
using System.Threading.Tasks;
using System.Globalization;

namespace XivVoices.Voice {
    public class AddonTalkHandler : IDisposable {
        private AddonTalkManager addonTalkManager;
        private IFramework framework;
        private IObjectTable _objectTable;
        private IClientState _clientState;
        private object subscription;
        private string _lastText;
        private string _currentText;
        private Plugin _plugin;
        private bool _blockAudioGeneration;
        private List<string> _currentDialoguePaths = new List<string>();
        private List<bool> _currentDialoguePathsCompleted = new List<bool>();
        private bool _startedNewDialogue;
        private bool _textIsPresent;
        private bool _alreadyAddedEvent;
        Stopwatch _passthroughTimer = new Stopwatch();
        List<string> _namesToRemove = new List<string>();
        private IChatGui _chatGui;
        private AddonTalkState _state;
        private Hook<NPCSpeechBubble> _openChatBubbleHook;
        private bool alreadyConfiguredBubbles;
        private ISigScanner _scanner;
        private bool disposed;
        Stopwatch bubbleCooldown = new Stopwatch();
        // private readonly Object _speechBubbleInfoLockObj = new();
        //private readonly Object mGameChatInfoLockObj = new();

        private MemoryService _memoryService;
        private AnimationService _animationService;
        private GameDataService _gameDataService;
        private List<Dalamud.Game.ClientState.Objects.Types.IGameObject> _threadSafeObjectTable;
        public List<ActionTimeline> LipSyncTypes { get; private set; }

        private readonly List<NPCBubbleInformation> _speechBubbleInfo = new();
        private readonly Queue<NPCBubbleInformation> _speechBubbleInfoQueue = new();
        private readonly List<NPCBubbleInformation> _gameChatInfo = new();

        public string lastNPCDialogue = "";
        public string lastBubbleDialogue = "";
        public string lastBattleDialogue = "";

        Dictionary<string, CancellationTokenSource> taskCancellations = new Dictionary<string, CancellationTokenSource>();

        public ConditionalWeakTable<ActorMemory, UserAnimationOverride> UserAnimationOverrides { get; private set; } = new();
        public bool TextIsPresent { get => _textIsPresent; set => _textIsPresent = value; }

        public AddonTalkHandler(AddonTalkManager addonTalkManager, IFramework framework, IObjectTable objects,
            IClientState clientState, Plugin plugin, IChatGui chatGui, ISigScanner sigScanner) {
            this.addonTalkManager = addonTalkManager;
            this.framework = framework;
            this._objectTable = objects;
            _clientState = clientState;
            framework.Update += Framework_Update;
            _plugin = plugin;
            _chatGui = chatGui;
            //_chatGui.ChatMessage += _chatGui_ChatMessage;
            _clientState.TerritoryChanged += _clientState_TerritoryChanged;
            _scanner = sigScanner;
            bubbleCooldown.Start();

            InitializeAsync().ContinueWith(t => {
                if (t.Exception != null)
                    Plugin.PluginLog.Error("Initialization failed: " + t.Exception);
            });
        }

        private async Task InitializeAsync()
        {
            Plugin.PluginLog.Information("InitializeAsync --> Waiting for Game Process Stability");
            await WaitForGameProcessStability();
            Plugin.PluginLog.Information("InitializeAsync --> Done waiting");
            InitializeServices();
        }

        private async Task WaitForGameProcessStability()
        {
            // Wait until the game process is stable
            while (Process.GetCurrentProcess() == null || !Process.GetCurrentProcess().Responding)
            {
                await Task.Delay(1000); // Check every second
            }
        }

        private void InitializeServices()
        {
            // Initialize all services that depend on the game process
            _memoryService = new MemoryService();
            _gameDataService = new GameDataService();
            _animationService = new AnimationService();
            StartServices();
        }

        private async void StartServices()
        {
            await _memoryService.Initialize();
            Plugin.PluginLog.Information("StartServices --> Waiting for Process Response");
            while (!Process.GetCurrentProcess().Responding)
                await Task.Delay(100);
            Plugin.PluginLog.Information("StartServices --> Done waiting");
            await _memoryService.OpenProcess(Process.GetCurrentProcess());
            await _gameDataService.Initialize();

            LipSyncTypes = GenerateLipList().ToList();
            await _animationService.Initialize();
            await _animationService.Start();
            await _memoryService.Start();
        }

        private IEnumerable<ActionTimeline> GenerateLipList()
        {
            // Grab "no animation" and all "speak/" animations, which are the only ones valid in this slot
            IEnumerable<ActionTimeline> lips = GameDataService.ActionTimelines.Where(x => x.AnimationId == 0 || (x.Key?.StartsWith("speak/") ?? false));
            return lips;
        }

        private void _clientState_TerritoryChanged(ushort obj) {
            _speechBubbleInfo.Clear();
        }

        
        private void _chatGui_ChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled) {
            if (_plugin.Config.Active && _clientState.IsLoggedIn && bubbleCooldown.ElapsedMilliseconds >= 200 && Conditions.IsBoundByDuty) {
                if (_state == null) {
                    switch (type) {
                        case XivChatType.NPCDialogueAnnouncements:
                            if (message.TextValue != _lastText && !Conditions.IsWatchingCutscene && !_blockAudioGeneration) {
                                _lastText = message.TextValue;
                                NPCText(sender.TextValue, message.TextValue.TrimStart('.'), true, !Conditions.IsBoundByDuty);
#if DEBUG
                                _plugin.Chat.Print("Sent audio from NPC chat.");
#endif
                            }
                            _lastText = message.TextValue;
                            _blockAudioGeneration = false;
                            break;
                    }
                }
            }
        }

        unsafe private IntPtr NPCBubbleTextDetour(IntPtr pThis, FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* pActor, IntPtr pString, bool param3) {
            if (_plugin.Config.Active)
            try {
                if (_clientState.IsLoggedIn && !Conditions.IsWatchingCutscene && !Conditions.IsWatchingCutscene78) {
                    if (pString != IntPtr.Zero &&
                    !Service.ClientState.IsPvPExcludingDen) {
                        if (pActor == null || (ObjectKind)pActor->GetObjectKind() != ObjectKind.Player) {
                            Plugin.PluginLog.Information("NPCBubbleTextDetour is running.");
                            long currentTime_mSec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                            SeString speakerName = SeString.Empty;
                            if (pActor != null && pActor->Name != null) {
                                speakerName = (pActor)->NameString;
                            }

                            var npcBubbleInformaton = new NPCBubbleInformation(MemoryHelper.ReadSeStringNullTerminated(pString), currentTime_mSec, speakerName);
                            var extantMatch = _speechBubbleInfo.Find((x) => { return x.IsSameMessageAs(npcBubbleInformaton); });
                            if (extantMatch != null) {
                                extantMatch.TimeLastSeen_mSec = currentTime_mSec;
                            } else {
                                _speechBubbleInfo.Add(npcBubbleInformaton);
                                try {
                                    //_plugin.webSocketServer.SendMessage($"_blockAudioGeneration:[{_blockAudioGeneration}] bubbleCooldown.ElapsedMilliseconds:[{bubbleCooldown.ElapsedMilliseconds}]");
                                    if (!_blockAudioGeneration && bubbleCooldown.ElapsedMilliseconds >= 0 ) {
                                        FFXIVClientStructs.FFXIV.Client.Game.Character.Character* character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)pActor;
                                        if ((ObjectKind)character->GameObject.ObjectKind == ObjectKind.EventNpc || (ObjectKind)character->GameObject.ObjectKind == ObjectKind.BattleNpc) {
                                            string nameID =
                                                    character->DrawData.Equipment(FFXIVClientStructs.FFXIV.Client.Game.Character.DrawDataContainer.EquipmentSlot.Body).Value.ToString() +
                                                    character->DrawData.Equipment(FFXIVClientStructs.FFXIV.Client.Game.Character.DrawDataContainer.EquipmentSlot.Head).Value.ToString() +
                                                    character->DrawData.Equipment(FFXIVClientStructs.FFXIV.Client.Game.Character.DrawDataContainer.EquipmentSlot.Feet).Value.ToString() +
                                                    character->DrawData.Equipment(FFXIVClientStructs.FFXIV.Client.Game.Character.DrawDataContainer.EquipmentSlot.Ears).Value.ToString() +
                                                    speakerName.TextValue +
                                                    character->BaseId;
                                            ICharacter characterObject = GetCharacterFromId(character->GameObject.EntityId);
                                            string finalName = characterObject != null && !string.IsNullOrEmpty(characterObject.Name.TextValue) && Conditions.IsBoundByDuty ? characterObject.Name.TextValue : nameID;
                                            if (npcBubbleInformaton.MessageText.TextValue != _lastText) {

                                                string id = character->BaseId.ToString();
                                                string skeleton = character->CharacterData.ModelSkeletonId.ToString();

                                                if (_plugin.Config.BubblesEverywhere && !Conditions.IsOccupiedInCutSceneEvent && !Conditions.IsOccupiedInEvent && !Conditions.IsOccupiedInQuestEvent)
                                                {
                                                    NPCText(finalName, id, skeleton, npcBubbleInformaton.MessageText.TextValue, character->DrawData.CustomizeData.Sex == 1,
                                                        character->DrawData.CustomizeData.Race, character->DrawData.CustomizeData.BodyType, character->DrawData.CustomizeData.Tribe, character->DrawData.CustomizeData.EyeShape, character->GameObject.Position);
                                                }
                                                else if (Conditions.IsBoundByDuty && _plugin.Config.BubblesInBattleZones && !Conditions.IsOccupiedInCutSceneEvent && !Conditions.IsOccupiedInEvent && !Conditions.IsOccupiedInQuestEvent)
                                                {
                                                    NPCText(finalName, id, skeleton, npcBubbleInformaton.MessageText.TextValue, character->DrawData.CustomizeData.Sex == 1,
                                                        character->DrawData.CustomizeData.Race, character->DrawData.CustomizeData.BodyType, character->DrawData.CustomizeData.Tribe, character->DrawData.CustomizeData.EyeShape, character->GameObject.Position);
                                                }
                                                else if (!Conditions.IsBoundByDuty && _plugin.Config.BubblesInSafeZones && !Conditions.IsOccupiedInCutSceneEvent && !Conditions.IsOccupiedInEvent && !Conditions.IsOccupiedInQuestEvent)
                                                {
                                                    NPCText(finalName, id, skeleton, npcBubbleInformaton.MessageText.TextValue, character->DrawData.CustomizeData.Sex == 1,
                                                        character->DrawData.CustomizeData.Race, character->DrawData.CustomizeData.BodyType, character->DrawData.CustomizeData.Tribe, character->DrawData.CustomizeData.EyeShape, character->GameObject.Position);
                                                }
                                            }
                                        }
                                    }
                                    _lastText = npcBubbleInformaton.MessageText.TextValue;
                                    bubbleCooldown.Restart();
                                    _blockAudioGeneration = false;
                                } catch {
                                    NPCText((pActor)->NameString, npcBubbleInformaton.MessageText.TextValue, true);
                                }
                            }
                        }
                    }
                }
            } catch (Exception e) {
                Plugin.PluginLog.Error(e, e.Message);
            }
            return _openChatBubbleHook.Original(pThis, pActor, pString, param3);
        }
        private ICharacter GetCharacterFromId(uint id) {
            foreach (Dalamud.Game.ClientState.Objects.Types.IGameObject gameObject in _threadSafeObjectTable)
            {
                if (gameObject.DataId == id
                    && (gameObject.ObjectKind == ObjectKind.EventNpc || gameObject.ObjectKind == ObjectKind.BattleNpc))
                {
                    return gameObject as ICharacter;
                }
            }
            return null;
        }
        private void Framework_Update(IFramework framework) {
            if (!disposed)
                try {
                    if (_clientState != null) {
                        if (_clientState.IsLoggedIn && _plugin.Config.Active) {
                            // Filter ------------------------------------------
                            if (_plugin.Filter.IsCutsceneDetectionNull())
                            {
                                if (!_alreadyAddedEvent)
                                {
                                    _plugin.Filter.OnCutsceneAudioDetected += Filter_OnCutsceneAudioDetected;
                                    _alreadyAddedEvent = true;
                                }
                            }
                            // End of Filter -----------------------------------
                            if (!alreadyConfiguredBubbles) {
                                //	Hook
                                unsafe {
                                    IntPtr fpOpenChatBubble = _scanner.ScanText("E8 ?? ?? ?? ?? F6 86 ?? ?? ?? ?? ?? C7 46 ?? ?? ?? ?? ??");
                                    if (fpOpenChatBubble != IntPtr.Zero) {
                                        Plugin.PluginLog.Information($"OpenChatBubble function signature found at 0x{fpOpenChatBubble:X}.");
                                        _openChatBubbleHook = Service.GameInteropProvider.HookFromAddress<NPCSpeechBubble>(fpOpenChatBubble, NPCBubbleTextDetour);
                                        _openChatBubbleHook?.Enable();
                                    } else {
                                        throw new Exception("Unable to find the specified function signature for OpenChatBubble.");
                                    }
                                }
                                alreadyConfiguredBubbles = true;
                            }
                            _state = GetTalkAddonState();
                            if (_state == null) {
                                _state = GetBattleTalkAddonState();
                            }
                            if (_state != null && !string.IsNullOrEmpty(_state.Text) && _state.Speaker != "All") {
                                _textIsPresent = true;
                                if (_state.Text != _currentText) {
                                    _lastText = _currentText;
                                    _currentText = _state.Text;
                                    if (!_blockAudioGeneration) {
                                        //_plugin.webSocketServer.SendMessage($"----------------> [1] {_state.Speaker}: {_state.Text.TrimStart('.')}");
                                        NPCText(_state.Speaker, _state.Text.TrimStart('.'), false);
                                        _startedNewDialogue = true;
                                        _passthroughTimer.Reset();
                                    }
                                    if (_currentDialoguePaths.Count > 0) {
                                        _currentDialoguePathsCompleted[_currentDialoguePathsCompleted.Count - 1] = true;
                                    }
                                    _blockAudioGeneration = false;
                                }
                            } else {
                                if (_currentDialoguePaths.Count > 0) {
                                    if (!_currentDialoguePathsCompleted[_currentDialoguePathsCompleted.Count - 1] && !_blockAudioGeneration) {
                                        try {
                                            //_plugin.webSocketServer.SendMessage($"----------------> [2] {_state.Speaker}: {_state.Text.TrimStart('.')}");
                                        } catch (Exception e) {
                                            Plugin.PluginLog.Error(e, e.Message);
                                        }
                                    }
                                    if (_currentDialoguePaths.Count > 0) {
                                        _currentDialoguePathsCompleted[_currentDialoguePathsCompleted.Count - 1] = true;
                                    }
                                }
                                if (_startedNewDialogue) {
                                    var otherData = _clientState.LocalPlayer.OnlineStatus;
                                    if (otherData.Id != 15) {
                                        _namesToRemove.Clear();
                                        _currentText = "";
                                        //_plugin.webSocketServer.SendMessage($"----------------> [3] {_state.Speaker}: {_state.Text.TrimStart('.')}");
                                        _currentDialoguePaths.Clear();
                                        _currentDialoguePathsCompleted.Clear();
                                    }
                                    _startedNewDialogue = false;
                                }
                                _threadSafeObjectTable = _objectTable.ToList();
                                _blockAudioGeneration = false;
                                _textIsPresent = false;
                            }
                        }
                    }
                } catch (Exception e) {
                    Plugin.PluginLog.Information(e, e.Message);
                }
        }

        private void Filter_OnCutsceneAudioDetected(object sender, InterceptedSound e)
        {
            if (_clientState != null)
            {
                if (_clientState.IsLoggedIn)
                {
                    if (!_currentDialoguePaths.Contains(e.SoundPath))
                    {
                        _blockAudioGeneration = e.isBlocking;
                        _currentDialoguePaths.Add(e.SoundPath);
                        _currentDialoguePathsCompleted.Add(false);
#if DEBUG
                        _plugin.Chat.Print("Block Next Line Of Dialogue Is " + e.isBlocking);
#endif
                    }
                }
            }
        }

        public string ConvertRomanNumberals(string text) {
            string value = text;
            for (int i = 25; i > 5; i--) {
                string numeral = Numerals.Roman.To(i);
                if (numeral.Length > 1) {
                    value = value.Replace(numeral, i.ToString());
                }
            }
            return value;
        }

        private static int GetSimpleHash(string s) {
            return s.Select(a => (int)a).Sum();
        }

        public string CleanSentence(string sentence)
        {
            //string correctedMessage = StripPlayerNameFromNPCDialogue(ConvertRomanNumberals(sentence));
            string correctedMessage = ConvertRomanNumberals(sentence);
            correctedMessage = Regex.Replace(correctedMessage, @"\s+", " ");
            correctedMessage = correctedMessage
                .Replace("─", " - ")
                .Replace("—", " - ")
                .Replace("–", "-")
                .Replace("\n", " ");

            // Remove leading "..." if present
            if (correctedMessage.StartsWith("..."))
            {
                correctedMessage = correctedMessage[3..];
            }

            // Trim leading and trailing spaces
            correctedMessage = correctedMessage.Trim();

            return correctedMessage ?? "";
        }

        public string CleanSender(string sender)
        {
            var suffixes = new string[] { "'s Voice", "'s Avatar" };

            foreach (var suffix in suffixes)
            {
                if (sender.EndsWith(suffix))
                {
                    sender = sender.Substring(0, sender.Length - suffix.Length);
                    break;
                }
            }

            return sender;
        }

        public async void TriggerLipSync(ICharacter character, string length)
        {
            
            if (Conditions.IsBoundByDuty && !Conditions.IsWatchingCutscene) return;
            if (!_plugin.Config.Active) return;

            ActorMemory actorMemory = null;
            AnimationMemory animationMemory = null;
            if (character != null)
            {
                actorMemory = new ActorMemory();
                actorMemory.SetAddress(character.Address);
                animationMemory = actorMemory.Animation;

                // Determine the duration based on the message size
                float duration = float.Parse(length, CultureInfo.InvariantCulture);

                Dictionary<int,int> mouthMovement = new Dictionary<int, int>();

                if (duration < 0.2f)
                    return;

                int durationMs = (int)(duration * 1000);


                // Decide on the lengths
                int durationRounded = (int)Math.Floor(duration);
                int remaining = durationRounded;
                mouthMovement[6] = remaining / 4;
                remaining = remaining % 4;
                mouthMovement[5] = remaining / 2;
                remaining = remaining % 2;
                mouthMovement[4] = remaining / 1;
                remaining = remaining % 1;
#if DEBUG
                _chatGui.Print($"durationMs[{durationMs}] durationRounded[{durationRounded}] fours[{mouthMovement[6]}] twos[{mouthMovement[5]}] ones[{mouthMovement[4]}]");
#endif
                _plugin.Log($"durationMs[{durationMs}] durationRounded[{durationRounded}] fours[{mouthMovement[6]}] twos[{mouthMovement[5]}] ones[{mouthMovement[4]}]");

                // Decide on the Mode
                ActorMemory.CharacterModes intialState = actorMemory.CharacterMode;
                ActorMemory.CharacterModes mode = ActorMemory.CharacterModes.EmoteLoop;

                if (!taskCancellations.ContainsKey(character.ToString()))
                {
                    var cts = new CancellationTokenSource();
                    taskCancellations.Add(character.ToString(), cts);
                    var token = cts.Token;

                    Task task = Task.Run(async () => {
                        try
                        {
                            await Task.Delay(100, token);

                            if(!token.IsCancellationRequested && mouthMovement[6] > 0 && character != null && actorMemory != null && actorMemory != null)
                            {
                                animationMemory.LipsOverride = LipSyncTypes[6].Timeline.AnimationId;
                                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), mode, "Animation Mode Override");
                                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), LipSyncTypes[6].Timeline.AnimationId, "Lipsync");

                                int adjustedDelay = CalculateAdjustedDelay(mouthMovement[6] * 4000, 6);
#if DEBUG
                                _chatGui.Print($"Task was started mouthMovement[6] durationMs[{mouthMovement[6]*4}] delay [{adjustedDelay}]");
#endif
                                _plugin.Log($"Task was started mouthMovement[6] durationMs[{mouthMovement[6] * 4}] delay [{adjustedDelay}]");

                                await Task.Delay(adjustedDelay, token);

                                if (!token.IsCancellationRequested && character != null && actorMemory != null)
                                {
#if DEBUG
                                    _chatGui.Print($"Task mouthMovement[6] was finished");
#endif
                                    _plugin.Log($"Task mouthMovement[6] was finished");

                                    animationMemory.LipsOverride = 0;
                                    MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                                    MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                                }

                            }

                            if (!token.IsCancellationRequested && mouthMovement[5] > 0 && character != null && actorMemory != null)
                            {
                                animationMemory.LipsOverride = LipSyncTypes[5].Timeline.AnimationId;
                                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), mode, "Animation Mode Override");
                                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), LipSyncTypes[5].Timeline.AnimationId, "Lipsync");
                                int adjustedDelay = CalculateAdjustedDelay(mouthMovement[5] * 2000, 5);
#if DEBUG
                                _chatGui.Print($"Task was started mouthMovement[5] durationMs[{mouthMovement[5] * 2}] delay [{adjustedDelay}]");
#endif
                                _plugin.Log($"Task was started mouthMovement[5] durationMs[{mouthMovement[5] * 2}] delay [{adjustedDelay}]");

                                await Task.Delay(adjustedDelay, token);
                                if (!token.IsCancellationRequested && character != null && actorMemory != null)
                                {
#if DEBUG
                                    _chatGui.Print($"Task mouthMovement[5] was finished");
#endif
                                    _plugin.Log($"Task mouthMovement[5] was finished");

                                    animationMemory.LipsOverride = 0;
                                    MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                                    MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                                }

                            }

                            if (!token.IsCancellationRequested && mouthMovement[4] > 0 && character != null && actorMemory != null)
                            {
                                animationMemory.LipsOverride = LipSyncTypes[4].Timeline.AnimationId;
                                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), mode, "Animation Mode Override");
                                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), LipSyncTypes[2].Timeline.AnimationId, "Lipsync");
                                int adjustedDelay = CalculateAdjustedDelay(mouthMovement[4]*1000, 4);
#if DEBUG
                                _chatGui.Print($"Task was started mouthMovement[4] durationMs[{mouthMovement[4]}] delay [{adjustedDelay}]");
#endif
                                _plugin.Log($"Task was started mouthMovement[4] durationMs[{mouthMovement[4]}] delay [{adjustedDelay}]");


                                await Task.Delay(adjustedDelay, token);
                                if (!token.IsCancellationRequested && character != null && actorMemory != null)
                                {
#if DEBUG
                                    _chatGui.Print($"Task mouthMovement[4] was finished");
#endif
                                    _plugin.Log($"Task mouthMovement[4] was finished");

                                    animationMemory.LipsOverride = 0;
                                    MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                                    MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                                }
                            }

                            if (!token.IsCancellationRequested)
                            {
#if DEBUG
                                _chatGui.Print($"Task was Completed");
#endif
                                _plugin.Log($"Task was Completed");

                                cts.Dispose();
                                if (taskCancellations.ContainsKey(character.ToString()))
                                    taskCancellations.Remove(character.ToString());
                            }
                        }
                        catch (TaskCanceledException)
                        {

#if DEBUG
                            _chatGui.Print($"Task was canceled.");
#endif
                            _plugin.Log($"Task was canceled.");

                            animationMemory.LipsOverride = 0;
                            MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                            MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                            cts.Dispose();
                            if (taskCancellations.ContainsKey(character.ToString()))
                                taskCancellations.Remove(character.ToString());
                        }
                        catch (Exception ex)
                        {
                            Plugin.PluginLog.Error($"Unhandled exception in TriggerLipSync task: {ex}");

                            animationMemory.LipsOverride = 0;
                            MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                            MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                            cts.Dispose();
                            if(taskCancellations.ContainsKey(character.ToString()))
                                taskCancellations.Remove(character.ToString());
                        }
                    }, token);
                }
                
                


            }
            
        }

        int CalculateAdjustedDelay(int durationMs, int lipSyncType)
        {
            int delay = 0;
            int animationLoop;
            if (lipSyncType == 4)
                animationLoop = 1000;
            else if (lipSyncType == 5)
                animationLoop = 2000;
            else
                animationLoop = 4000;
            int halfStep = animationLoop/2;

            if (durationMs <= (1* animationLoop) + halfStep)
            {
                return (1 * animationLoop) - 50;
            }
            else 
                for(int i = 2; delay < durationMs; i++)
                    if (durationMs > (i * animationLoop) - halfStep && durationMs  <= (i * animationLoop) + halfStep)
                    {
                        delay = (i * animationLoop) - 50;
                        return delay;
                    }

            return 404;
        }

        public async void StopLipSync(ICharacter character)
        {
            
            if (Conditions.IsBoundByDuty && !Conditions.IsWatchingCutscene) return;
            if (!_plugin.Config.Active) return;
            if (character == null) return;

            if (taskCancellations.TryGetValue(character.ToString(), out var cts))
            {
                //_chatGui.Print("Cancellation " + character.Name);
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    Plugin.PluginLog.Error($"CTS for {character.Name} was called to be disposed even though it was disposed already, tell Arc!");
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Error($"CTS for {character.Name} was called to be disposed but caught an exception, tell Arc!");
                }
                return;
            }

            try
            {
                //_chatGui.Print("StopLipSync " + character.Name);
                var actorMemory = new ActorMemory();
                actorMemory.SetAddress(character.Address);
                var animationMemory = actorMemory.Animation;
                animationMemory.LipsOverride = LipSyncTypes[5].Timeline.AnimationId;
                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
            }
            catch
            {

            }
            
        }

        public int EstimateDurationFromMessage(string message)
        {
            int words = message.Split(new char[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            double wordsPerSecond = 150.0 / 60; // 150 words per minute converted to words per second

            return (int)(words / wordsPerSecond * 1000); // duration in milliseconds
        }


        private async void NPCText(string npcName, string message, bool ignoreAutoProgress, bool lowLatencyMode = false) {
            if (!_plugin.Config.Active) return;
            try {
                uint id = 0;
                int skeleton = 0;
                byte body = 0; // 1 = adult, 3 = old,  4 = child
                bool gender = false;
                byte race = 0;
                byte tribe = 0;
                byte eyes = 0;

                Dalamud.Game.ClientState.Objects.Types.IGameObject npcObject = DiscoverNpc(npcName, ref id, ref skeleton, ref body, ref gender, ref tribe, ref race, ref eyes);

                // Gender Adjustment
                if (npcName.ToLower().Contains("gaia"))
                {
                    gender = true;
                }

                string nameToUse = npcObject != null ? npcObject.Name.ToString() : npcName;
                string correctedMessage = CleanSentence(message);
                string correctSender = CleanSender(nameToUse);
                
                string genderType = gender ? "Female":"Male";
                string user = $"{_plugin.ClientState.LocalPlayer.Name}@{_plugin.ClientState.LocalPlayer.HomeWorld.GameData.Name}";

                Engine.XivEngine.Instance.Process("Dialogue", correctSender, id.ToString(), skeleton.ToString(), correctedMessage, body.ToString(), genderType, race.ToString(), tribe.ToString(), eyes.ToString(), _clientState.ClientLanguage.ToString(), new Vector3(-99), npcObject as ICharacter, user);

                lastNPCDialogue = npcName + correctedMessage;
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"NPCText1 ---> Exception: {ex}");
            }
        }
        
        private async void NPCText(string name, string id, string skeleton, string message, bool gender, byte race, byte body, byte tribe, byte eyes, Vector3 position) {
            if (!_plugin.Config.Active || !_plugin.Config.BubblesEnabled) return;
            try {
                
                if (_plugin.PlayerCamera.Forward == null)
                {
                    Plugin.PluginLog.Information($"NPCText ---> Forward Camera is null...");
                    _plugin.InitializeCamera();
                }

                ICharacter npcObject = null;

                string nameToUse = name;
                string correctedMessage = CleanSentence(message);
                string genderType = gender ? "Female" : "Male";
                if (!string.IsNullOrEmpty(nameToUse) && char.IsDigit(nameToUse[0]))
                    nameToUse = "Bubble";
                string correctSender = CleanSender(nameToUse);

                // Player and NPC positions
                Vector3 playerPosition = _clientState.LocalPlayer.Position;
                Vector3 npcPosition = position;
                // Camera orientation vectors
                Vector3 cameraForward = Vector3.Normalize(_plugin.PlayerCamera.Forward);
                Vector3 cameraUp = Vector3.Normalize(_plugin.PlayerCamera.Top);
                Vector3 cameraRight = Vector3.Normalize(Vector3.Cross(cameraUp, cameraForward));

                // Construct the camera rotation matrix (as an inverse)
                Matrix4x4 cameraRotationMatrix = new Matrix4x4(
                    cameraRight.X, cameraRight.Y, cameraRight.Z, 0,
                    cameraUp.X, cameraUp.Y, cameraUp.Z, 0,
                    cameraForward.X, cameraForward.Y, cameraForward.Z, 0,
                    0, 0, 0, 1);

                // Compute vector from player to NPC
                Vector3 toNPC = npcPosition - playerPosition;

                // Transform the vector to camera space (using the transpose, which is the inverse for rotation matrices)
                Vector3 relativePosition = Vector3.Transform(toNPC, Matrix4x4.Transpose(cameraRotationMatrix));
                string positionString = relativePosition.ToString("#.###", CultureInfo.InvariantCulture);

                if (lastBattleDialogue != correctedMessage)
                {
                    string user = $"{_plugin.ClientState.LocalPlayer.Name}@{_plugin.ClientState.LocalPlayer.HomeWorld.GameData.Name}";

                    if(_plugin.Config.BubbleChatEnabled)
                        _plugin.Chat.Print(new XivChatEntry
                        {
                            Name = correctSender,
                            Message = correctedMessage,
                            Type = XivChatType.NPCDialogue
                        });
                    Engine.XivEngine.Instance.Process("Bubble", correctSender, id, skeleton, correctedMessage, body.ToString(), genderType, race.ToString(), tribe.ToString(), eyes.ToString(), _clientState.ClientLanguage.ToString(), position, npcObject, user);

                    lastBubbleDialogue = correctedMessage;
                }
                else
                {
#if DEBUG
                    _plugin.Chat.Print("bubble blocked");
#endif
                }

            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"NPCText2 ---> Exception: {ex}");
            }
        }

        private Dalamud.Game.ClientState.Objects.Types.IGameObject DiscoverNpc(string npcName, ref uint id, ref int skeleton, ref byte body, ref bool gender, ref byte tribe, ref byte race, ref byte eyes) {
            if (npcName == "???") {
                /*
                foreach (var item in _objectTable) {

                    if (item as Character == null || item as Character == _clientState.LocalPlayer || item.Name.TextValue == "") continue;
                    
                    if (true) {
                        Character character = item as Character;
                        if (character != null && character != _clientState.LocalPlayer) {
                            gender = Convert.ToBoolean(character.Customize[(int)CustomizeIndex.Gender]);
                            race = character.Customize[(int)CustomizeIndex.Race];
                            body = character.Customize[(int)CustomizeIndex.ModelType];
                            return character;
                        }
                        return item;
                    }
                }*/
            }
            else {
                foreach (Dalamud.Game.ClientState.Objects.Types.IGameObject item in _objectTable) {
                    if (item as ICharacter == null || item as ICharacter == _clientState.LocalPlayer || item.Name.TextValue == "") continue;
                    //_plugin.webSocketServer.SendMessage($"DiscoverNpc: LOOKING AT {item.Name.TextValue} WITH {item.DataId}");
                    if (item.Name.TextValue == npcName) {
                        _namesToRemove.Add(npcName);
                        //_plugin.webSocketServer.SendMessage($"DiscoverNpc: FOUND {item.Name.TextValue} WITH {item.DataId}");
                        return GetCharacterData(item, ref id, ref skeleton, ref body, ref gender, ref tribe, ref race, ref eyes);
                    }
                }
            }
            return null;
        }

        private unsafe Dalamud.Game.ClientState.Objects.Types.IGameObject GetCharacterData(Dalamud.Game.ClientState.Objects.Types.IGameObject gameObject, ref uint id, ref int skeleton, ref byte body, ref bool gender, ref byte tribe, ref byte race, ref byte eyes) {
            ICharacter character = gameObject as ICharacter;
            if (character != null) {
                id = gameObject.DataId;
                skeleton = ((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)character.Address)->CharacterData.ModelSkeletonId;
                body = character.Customize[(int)CustomizeIndex.ModelType];
                gender = Convert.ToBoolean(character.Customize[(int)CustomizeIndex.Gender]);
                race = character.Customize[(int)CustomizeIndex.Race];
                tribe = character.Customize[(int)CustomizeIndex.Tribe];
                eyes = character.Customize[(int)CustomizeIndex.EyeShape];
                //_plugin.Log($"{character.Name.TextValue}: id[{id}] skeleton[{skeleton}] body[{body}] gender[{gender}] race[{race}] tribe[{tribe}] eyes[{eyes}] ---> area[{_plugin.ClientState.TerritoryType}]");
            }
            return (Dalamud.Game.ClientState.Objects.Types.IGameObject)character;
        }

        public ICharacter GetCharacterFromName(string name)
        {
            try
            {
                foreach (var item in _objectTable)
                {

                    if (item as ICharacter == null || item.Name.TextValue == "") continue;

                    ICharacter character = item as ICharacter;
                    if (name == character.Name.TextValue)
                    {
                        return character;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"GetCharacterFromName ---> Exception: {ex}");
                return null;
            }
        }

        private AddonTalkState GetTalkAddonState() {
            if (!this.addonTalkManager.IsVisible()) {
                return default; 
            }

            var addonTalkText = this.addonTalkManager.ReadText();
            return addonTalkText != null
                ? new AddonTalkState(addonTalkText.Speaker, addonTalkText.Text)
                : default;
        }
        private AddonTalkState GetBattleTalkAddonState() {
            if (!this.addonTalkManager.IsVisible()) {
                return default;
            }

            var addonTalkText = this.addonTalkManager.ReadTextBattle();
            return addonTalkText != null
                ? new AddonTalkState(addonTalkText.Speaker, addonTalkText.Text)
                : default;
        }


        public void Dispose() {
            framework.Update -= Framework_Update;
            //_chatGui.ChatMessage -= _chatGui_ChatMessage;
            _clientState.TerritoryChanged -= _clientState_TerritoryChanged;
            disposed = true;

            _memoryService?.Shutdown();
            _gameDataService?.Shutdown();
            _animationService?.Shutdown();
            _openChatBubbleHook?.Dispose();
        }

        public class UserAnimationOverride
        {
            public ushort BaseAnimationId { get; set; } = 0;
            public ushort BlendAnimationId { get; set; } = 0;
            public bool Interrupt { get; set; } = true;
        }

        private unsafe delegate IntPtr NPCSpeechBubble(IntPtr pThis, FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* pActor, IntPtr pString, bool param3);
    }
}
