﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Dalamud.Plugin;
using System.Threading.Tasks;
using System.Reflection;
using System.Net.Http;
using Dalamud.Utility;

namespace XivVoices.Engine
{
    public class Database
    {

        #region Private Parameters
        private IDalamudPluginInterface _pluginInterface;
        #endregion

        #region Public Parameters
        public string RootPath { get; set; }
        public string DirectoryPath { get; set; }
        public string VoiceFilesPath { get { return Path.Combine(DirectoryPath, "Data"); } }
        public string ReportsPath { get { return Path.Combine(DirectoryPath, "Reports"); } }
        public string ToolsPath { get { return "C:/XIV_Voices/Tools";  } }
        public string Firstname { get; } = "_FIRSTNAME_";
        public string Lastname { get; } = "_LASTNAME_";
        public Dictionary<string, XivNPC> NpcData { get; set; }
        public Dictionary<string, PlayerCharacter> PlayerData { get; set; }
        public List<string> NpcsWithRetainerLines { get; set; } = new List<string>()
            {
                "Alphinaud",
                "Alisaie",
                "Y'shtola",
                "Thancred",
                "Yda",
                "Lyse",
                "Urianger",
                "Cid",
                "Merlwyb",
                "Ryne",
                "G'raha Tia",
                "Estinien",
                "Krile",
                "Tataru",
                "Minfilia",
                "Riol",
                "Unukalhai"
            };
        public List<string> NpcWithVariedLooks { get; set; } = new List<string>()
            {
                "Abducted Ala Mhigan",
                "Adventurers' Guild Investigator",
                "Airship Ticketer",
                "Ala Mhigan Resistance Gate Guard",
                "Alehouse Wench",
                "Alisaie's Assistant",
                "All Saints' Wake Crier",
                "Amarokeep",
                "Apartment Caretaker",
                "Apartment Merchant",
                "Armed Imperial Citizen",
                "Arms Supplier",
                "Arrivals Attendant",
                "Attentive Radiant",
                "Auri Boy",
                "Alexandrian Citizen",
                "Brass Blade",
                "Calamity Salvager",
                "Campaign Attendant",
                "Captive Soul",
                "Celebration Guide",
                "Chocobokeep",
                "Collectable Appraiser",
                "Concerned Mother",
                "Contemplative Citizen",
                "Commanding Officer",
                "Despondent Refugee",
                "Dreamer",
                "Dapper Zombie",
                "Despondent Refugee",
                "Doman Liberator",
                "Doman Miner",
                "Eggsaminer",
                "Ethelia",
                "Expedition Artisan",
                "Expedition Birdwatcher",
                "Expedition Provisioner",
                "Expedition Scholar",
                "Faire Adventurer",
                "Faire Crier",
                "Falcon Porter",
                "Ferry Skipper",
                "Firmament Resident",
                "Flame Veteran",
                "Flame Officer",
                "Flame Private",
                "Flame Recruit",
                "Flame Scout",
                "Flame Sergeant",
                "Flame Soldier",
                "Flustered Fisher",
                "Firmament Boy",
                "Firmament Girl",
                "GATE Keeper",
                "Genial Guiser",
                "Gold Saucer Attendant",
                "Gridanian Merchant",
                "Harlequin Guide",
                "House Durendaire Knight",
                "House Fortemps Knight",
                "House Fortemps Banneret",
                "House Valentione Butler",
                "House Valentione Maid",
                "House Valentione Servant",
                "Housing Enthusiast",
                "Housing Merchant",
                "Hunt Billmaster",
                "Hunter-scholar",
                "Imperial Centurion",
                "Independent Armorer",
                "Independent Armorfitter",
                "Independent Arms Mender",
                "Independent Mender",
                "Independent Merchant",
                "Independent Sutler",
                "Inu Doshin",
                "Ishgardian Merchant",
                "Ironworks Engineer",
                "Ironworks Hand",
                "Junkmonger",
                "Keeper of the Entwined Serpents",
                "Koshu Onmyoji",
                "Lente's Tear",
                "Local Merchant",
                "Loitering Lad",
                "Lonesome Lass",
                "Long-haired Pirate",
                "Maelstrom Veteran",
                "Maelstrom Officer",
                "Maelstrom Private",
                "Maelstrom Recruit",
                "Maelstrom Scout",
                "Maelstrom Sergeant",
                "Maelstrom Soldier",
                "Maelstrom Gladiator",
                "Malevolent Mummer",
                "Materia Melder",
                "Material Supplier",
                "Meghaduta Attendant",
                "Mender",
                "Merchant & Mender",
                "Minion Enthusiast",
                "Moonfire Faire Chaperone",
                "Moonfire Faire Vendor",
                "Moonfire Marine",
                "Meghaduta Attendant",
                "OIC Administrator",
                "OIC Officer of Arms",
                "OIC Quartermaster",
                "Oroniri Merchant",
                "Oroniri Warrior",
                "Picker of Locks",
                "Provisions Crate",
                "Ragged Refugee",
                "Recompense Officer",
                "Resident Caretaker",
                "Resistance Commander",
                "Resistance Fighter",
                "Resistance Soldier",
                "Resistance Guard",
                "Resistance Supplier",
                "Rising Attendant",
                "Rising Vendor",
                "Rowena's Representative",
                "Royal Handmaiden",
                "Royal Herald",
                "Royal Seneschal",
                "Royal Servant",
                "Saint's Little Helper",
                "Saucer Attendant",
                "Scrip Exchange",
                "Scion of the Seventh Dawn",
                "Scion Lancer",
                "Scion Conjurer",
                "Scion Marauder",
                "Scion Thaumaturge",
                "Seasoned Adventurer",
                "Serpent Conjurer",
                "Serpent Lancer",
                "Serpent Lieutenant",
                "Serpent Officer",
                "Serpent Private",
                "Serpent Recruit",
                "Serpent Scout",
                "Serpent Gladiator",
                "Serpent Veteran",
                "Skysteel Engineer",
                "Skywatcher",
                "Soldier's Corpse",
                "Spirited Citizen",
                "Splendors Vendor",
                "Spoils Collector",
                "Spoils Trader",
                "Starlight Celebrant",
                "Starlight Celebration Crier",
                "Starlight Supplier",
                "Steersman",
                "Stocky Student",
                "Stone Torch",
                "Straggler",
                "Storm Armorer",
                "Storm Blacksmith",
                "Storm Captain",
                "Storm Marauder",
                "Storm Officer",
                "Storm Private",
                "Storm Recruit",
                "Storm Sergeant",
                "Storm Soldier",
                "Storm Gladiator",
                "Storm Veteran",
                "Stone Torch",
                "Yellowjacket Armorer",
                "Yellowjacket Blacksmith",
                "Yellowjacket Captain",
                "Yellowjacket Marauder",
                "Yellowjacket Officer",
                "Yellowjacket Private",
                "Yellowjacket Recruit",
                "Yellowjacket Sergeant",
                "Yellowjacket Soldier",
                "Yellowjacket Gladiator",
                "Yellowjacket Veteran",
                "Stranded Miner",
                "Studium Staff",
                "Sultansworn Elite",
                "Suspicious Coerthan",
                "Tailfeather Hunter",
                "Temple Knight",
                "Tournament Registrar",
                "Traveling Merchant",
                "Traveling Trader",
                "Triple Triad Trader",
                "Troubled Coachman",
                "Uncanny Illusionist",
                "Untrustworthy Illusionist",
                "Usagi Doshin",
                "Unnerved Knight",
                "Vexed Villager",
                "Vault Friar",
                "Well-informed Adventurer",
                "Wood Wailer Lance",
                "Wood Wailer Sentry",
                "Wounded Imperial",
                "Wounded Resistance Fighter",
                "Yellow Moon Admirer",
                "Yellowjacket Captain",
            };
        public Dictionary<string, string> Data { get; set; }
        public Dictionary<string, string> AccessData { get; set; }
        public Dictionary<string, string> VoiceNames { get; set; }
        public Dictionary<string, string> Lexicon { get; set; }
        public Dictionary<string, string> Nameless { get; set; }
        public Dictionary<string, string> Retainers { get; set; }
        public List<string> Ignored { get; set; }
        public Framework Framework { get; set; }
        #endregion

        #region Framework Parameters
        public bool ForcePlayerName { get; set; } = false;
        public string PlayerName { get; set; } = "our friend";
        public bool ForceWholeSentence { get; set; } = false;
        public string WholeSentence { get; set; } = "";
        public bool Access { get; set; }
        public bool RequestBusy { get; set; } = false;

        public bool RequestMuteBusy { get; set; } = false;
        public bool RequestActive { get; set; } = false;
        #endregion

        public Plugin Plugin { get; set; }

        #region Unity Methods
        public Database(IDalamudPluginInterface pluginInterface, Plugin plugin)
        {
            this._pluginInterface = pluginInterface;
            this.Plugin = plugin;
            

            string pathWith_Data = _pluginInterface.AssemblyLocation.DirectoryName;
            RootPath = Path.GetDirectoryName(pathWith_Data);
            
            string directory = this.Plugin.Config.WorkingDirectory;
            DirectoryPath = directory;

            this.Plugin.Config.Initialized = false;
            bool dataAndToolsExist = true;

            // Check for Data folder
            if (!Directory.Exists(DirectoryPath))
            {
                Directory.CreateDirectory(DirectoryPath);
                dataAndToolsExist = false;
            }

            // Check for Voice Data folder
            if (!Directory.Exists(VoiceFilesPath))
            {
                Directory.CreateDirectory(VoiceFilesPath);
                dataAndToolsExist = false;
            }

            // Check for Tools folder
            if (!Directory.Exists(ToolsPath))
            {
                Directory.CreateDirectory(ToolsPath);
                dataAndToolsExist = false;
            }
            else
            {
                
                bool ffmpegExists = File.Exists(Path.Combine(ToolsPath, "ffmpeg.exe"));
                bool ffprobeExists = File.Exists(Path.Combine(ToolsPath, "ffprobe.exe"));

                if (!ffmpegExists || !ffprobeExists)
                    dataAndToolsExist = false;
            }


            if (dataAndToolsExist)
                this.Plugin.Config.Initialized = true;

            

            Plugin.PluginLog.Information("Working directory is: " + DirectoryPath);

            Task.Run(async () => await LoadDatabaseAsync());
            Plugin.Chat.Print("Database: I am awake");
        }

        public void UpdateDirectory()
        {
            DirectoryPath = this.Plugin.Config.WorkingDirectory;

            if (!Directory.Exists(DirectoryPath))
            {
                Directory.CreateDirectory(DirectoryPath);
            }

            if (!Directory.Exists(VoiceFilesPath))
            {
                Directory.CreateDirectory(VoiceFilesPath);
            }

            if (!Directory.Exists(ToolsPath))
            {
                Directory.CreateDirectory(ToolsPath);
            }
        }

        public void Dispose()
        {
            Framework.Dispose();
        }
        #endregion


        #region Loaders
        public async Task LoadDatabaseAsync()
        {
            try
            {
                // Assume loadingScreen and other UI components are handled differently
                Plugin.Chat.Print("Loading Data...");
                Plugin.PluginLog.Information("Loading Data...");
                await LoadDataAsync();
                await LoadLexiconsAsync();

                Plugin.Chat.Print("Loading Nameless Data...");
                Plugin.PluginLog.Information("Loading Nameless Data...");
                await LoadNamelessAsync();

                Plugin.Chat.Print("Loading Retainers Data...");
                Plugin.PluginLog.Information("Loading Nameless Data...");
                await LoadRetainersAsync();

                Plugin.Chat.Print("Loading Ignored Data...");
                Plugin.PluginLog.Information("Loading Ignored Data...");
                await LoadIgnoredAsync();

                Plugin.Chat.Print("Loading NPC Data...");
                Plugin.PluginLog.Information("Loading NPC Data...");
                await LoadNPCsAsync();

                Plugin.Chat.Print("Loading Player Data...");
                Plugin.PluginLog.Information("Loading Player Data...");
                await LoadPlayersAsync();

                Plugin.Chat.Print("Loading Voice Names...");
                Plugin.PluginLog.Information("Loading Voice Names...");

                await LoadVoiceNamesAsync();

                Plugin.Config.FrameworkActive = false;
                Framework = new Framework();

                Plugin.Chat.Print("Done.");
                Plugin.PluginLog.Information("Done.");
                await Task.Delay(200);

                DeleteLeftoverZipFiles();

                string accessFilePath = Path.Combine(DirectoryPath, "access.json");
                if (File.Exists(accessFilePath))
                {
                    Access = true;
                    AccessData = ReadFile(accessFilePath);
                    Plugin.Print("You are an Access User.");
                }
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error(ex, "Error during database load.");
            }
        }

        private async Task LoadDataAsync()
        {
            string filePath = DirectoryPath + "/data.json";
            Data = ReadFile(filePath);
            if (Data == null)
            {
                Data = new Dictionary<string, string>();
                Data["voices"] = "000000";
                Data["npcs"] = "0000";
                Data["actors"] = "0";
                await ReloadAndUpdateData();
            }
        }

        public async Task LoadLexiconsAsync(bool announce = false)
        {
            string resourceName = "XivVoices.Data.lexicon.json";
            Lexicon = ReadResourceFile(resourceName);
            if (Lexicon == null)
            {
                Plugin.PluginLog.Error("Failed to load Lexicon.json from Resources.");
                Lexicon = new Dictionary<string, string>();
            }
        }

        public async Task LoadNamelessAsync(bool announce = false)
        {
            string resourceName = "XivVoices.Data.nameless.json";
            Nameless = ReadResourceFile(resourceName);
            if (Nameless == null)
            {
                Nameless = new Dictionary<string, string>();
            }
        }

        public async Task LoadRetainersAsync()
        {
            string resourceName = "XivVoices.Data.retainers.json";
            Retainers = ReadResourceFile(resourceName);
            if (Retainers == null)
            {
                Plugin.PluginLog.Error("Failed to load Retainers.json from Resources.");
                Retainers = new Dictionary<string, string>();
            }
        }

        public async Task LoadIgnoredAsync(bool announce = false)
        {
            string resourceName = "XivVoices.Data.ignored.json";
            Ignored = ReadResourceList(resourceName);
            if (Ignored == null)
            {
                Ignored = new List<string>();
            }
        }

        public async Task LoadNPCsAsync()
        {
            string resourceName = "XivVoices.Data.npcData.json";
            string filePath = DirectoryPath + "/npcData.json";
            NpcData = ReadResourceNPCs(resourceName);
            if (NpcData == null)
            {
                NpcData = new Dictionary<string, XivNPC>();
                Plugin.PluginLog.Error("Something is wrong with the NPC database");
            }
        }

        public async Task LoadPlayersAsync() //= new Dictionary<string, PlayerCharacter>();
        {
            string filePath = DirectoryPath + "/playerData.json";

            if (File.Exists(filePath))
            {
                string jsonContent = File.ReadAllText(filePath);
                PlayerData = JsonConvert.DeserializeObject<Dictionary<string, PlayerCharacter>>(jsonContent);
            }
            else
            {
                PlayerData = new Dictionary<string, PlayerCharacter>();
                await WriteJSON(filePath, PlayerData);
            }
        }

        public void UpdateAndSavePlayerData(string name, PlayerCharacter playerCharacter) //= new Dictionary<string, PlayerCharacter>();
        {
            string filePath = DirectoryPath + "/playerData.json";
            if (!PlayerData.ContainsKey(name) || !IsEqual(PlayerData[name], playerCharacter))
            {
                PlayerData[name] = playerCharacter;
                string jsonContent = JsonConvert.SerializeObject(PlayerData, Formatting.Indented);
                // Asynchronously write text to the file
                File.WriteAllTextAsync(filePath, jsonContent);
            }
            
        }

        private bool IsEqual(PlayerCharacter pc1, PlayerCharacter pc2)
        {
            return pc1.Body == pc2.Body &&
                   pc1.Gender == pc2.Gender &&
                   pc1.Race == pc2.Race &&
                   pc1.Tribe == pc2.Tribe &&
                   pc1.EyeShape == pc2.EyeShape;
        }

        public async Task LoadVoiceNamesAsync()
        {
            string resourceName = "XivVoices.Data.voiceNames.json";
            string json = ReadResourceEmbedded(resourceName);
            if (json == null)
            {
                VoiceNames = new Dictionary<string, string>();
                Plugin.PluginLog.Error("Failed to load voiceNames.json from embedded resources.");
                return;
            }

            List<VoiceMapping> voiceMappings = JsonConvert.DeserializeObject<List<VoiceMapping>>(json);
            if (voiceMappings == null)
            {
                VoiceNames = new Dictionary<string, string>();
                Plugin.PluginLog.Error("Failed to deserialize voiceNames.json.");
                return;

            }

            VoiceNames = new Dictionary<string, string>();

            foreach (VoiceMapping mapping in voiceMappings)
            {
                foreach (string speaker in mapping.speakers)
                {
                    VoiceNames[speaker] = mapping.voiceName;
                }
            }
        }
         
        public void OnClick_Reload()
        {
            Task.Run(async () => await ReloadAsync());
        }

        private async Task ReloadAsync()
        {
            await LoadLexiconsAsync(true);
            await LoadNamelessAsync(true);
            await LoadIgnoredAsync(true);
            await LoadNPCsAsync();
            await LoadPlayersAsync();
            await LoadVoiceNamesAsync();
        }
        #endregion


        #region Readers

        public Dictionary<string, string> ReadResourceFile(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
            {
                Plugin.PluginLog.Error($"Failed to find embedded resource: {resourceName}");
                return null;
            }

            using (var reader = new StreamReader(resourceStream))
            {
                string jsonContent = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonContent);
            }
        }

        public List<string> ReadResourceList(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
            {
                Plugin.PluginLog.Error($"Failed to find embedded resource: {resourceName}");
                return null;
            }

            using (var reader = new StreamReader(resourceStream))
            {
                string jsonContent = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<List<string>>(jsonContent);
            }
        }

        public static string ReadResourceEmbedded(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Plugin.PluginLog.Error($"Failed to load resource: {resourceName}");
                    return null;
                }
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public Dictionary<string, string> ReadFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                string jsonContent = File.ReadAllText(filePath);
                Dictionary<string, string> jsonDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonContent);
                return jsonDictionary;
            }
            else
            {
                return null;
            }
        }


        public Dictionary<string, XivNPC> ReadResourceNPCs(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
            {
                Plugin.PluginLog.Error($"Failed to find embedded resource: {resourceName}");
                return null;
            }

            using (var reader = new StreamReader(resourceStream))
            {
                string jsonContent = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<Dictionary<string, XivNPC>>(jsonContent);
            }
        }


        Dictionary<string, XivNPC> ReadNPCs(string filePath)
        {
            if (File.Exists(filePath))
            {
                string jsonContent = File.ReadAllText(filePath);
                Dictionary<string, XivNPC> jsonDictionary = JsonConvert.DeserializeObject<Dictionary<string, XivNPC>>(jsonContent);
                Plugin.PluginLog.Information(jsonDictionary.Count + " NPCs have been loaded.");
                return jsonDictionary;
            }
            else
            {
                return null;
            }
        }
        #endregion


        #region Writers
        public async Task WriteJSON<T>(string filePath, T dataToSave)
        {
            string jsonContent = JsonConvert.SerializeObject(dataToSave, Formatting.Indented);

            // Asynchronously write text to the file
            await File.WriteAllTextAsync(filePath, jsonContent);

            Plugin.PluginLog.Information("JSON file written successfully to path: " + filePath);
        }

        

        bool writeVoiceDataBusy = false;
        public async Task WriteVoiceData(XivMessage xivMessage, byte[] audioData)
        {
            string voiceName = xivMessage.VoiceName;
            string speaker = xivMessage.Speaker;
            string sentence = xivMessage.Sentence;

            while (writeVoiceDataBusy) await Task.Delay(50);

            writeVoiceDataBusy = true;

            string originalSpeaker = speaker;
            speaker = Regex.Replace(speaker, @"[^a-zA-Z0-9 _-]", "").Replace(" ", "_").Replace("-", "_");

            // Create a Path 
            string cleanedSentence = RemoveSymbolsAndLowercase(sentence);
            if (cleanedSentence.IsNullOrEmpty())
            {
                writeVoiceDataBusy = false;
                return;
            }
            string actorDirectory = VoiceFilesPath + "/" + voiceName;
            string speakerDirectory = actorDirectory + "/" + speaker;

            string filePath = speakerDirectory + "/" + cleanedSentence;
            int missingFromDirectoryPath = 0;
            if (DirectoryPath.Length < 13)
                missingFromDirectoryPath = 13 - DirectoryPath.Length;
            int maxLength = 200 - ((DirectoryPath + "/" + voiceName + "/" + speaker).Length);
            maxLength -= missingFromDirectoryPath;
            if (cleanedSentence.Length > maxLength)
                cleanedSentence = cleanedSentence.Substring(0, maxLength);

            cleanedSentence = Regex.Replace(cleanedSentence, @"[^a-zA-Z0-9 _-]", "").Replace(" ", "_").Replace("-", "_");
            filePath = speakerDirectory + "/" + cleanedSentence;
            xivMessage.FilePath = filePath;

            // Check if file exists
            bool fileExistedBefore;
            if (File.Exists(filePath + ".ogg"))
                fileExistedBefore = true;
            else
                fileExistedBefore = false;

            // Create the directory if it doesn't exist
            if (!Directory.Exists(actorDirectory))
            {
                Directory.CreateDirectory(actorDirectory);
                Data["actors"] = (int.Parse(Data["actors"]) + 1).ToString("000");
            }

            if (!Directory.Exists(speakerDirectory))
            {
                Directory.CreateDirectory(speakerDirectory);
                Data["npcs"] = (int.Parse(Data["npcs"]) + 1).ToString("0000");
            }

            try
            {
                await File.WriteAllBytesAsync(xivMessage.FilePath + ".mp3", audioData);

                Plugin.PluginLog.Information("MP3 file written successfully to path: " + filePath);
                if (!fileExistedBefore)
                {
                    Data["voices"] = (int.Parse(Data["voices"]) + 1).ToString("000000");
                }

                // Save JSON Data for this MP3
                var mp3data = new Dictionary<string, string>
                {
                    ["speaker"] = originalSpeaker,
                    ["sentence"] = sentence,
                    ["lastSave"] = DateTime.UtcNow.ToString("o")
                };
                await WriteJSON(filePath + ".json", mp3data);

                XivEngine.Instance.SpeakLocallyAsync(xivMessage,true);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"Failed to write MP3 file to path: {filePath}. Error: {ex.Message}");
            }

            await WriteJSON(DirectoryPath + "/Data.json", Data);
            writeVoiceDataBusy = false;
        }
        
        #endregion


        #region OnClick Methods
        public void OnClick_OpenFile(string fileName)
        {
            string filePath = "file:" + DirectoryPath + "/" + fileName;
            Process.Start(filePath);
        }

        public void OnClick_ReloadAndUpdateData()
        {
            Task.Run(async () => await ReloadAndUpdateData());
        }

        public async Task ReloadAndUpdateData()
        {
            int _actors = Directory.GetDirectories(VoiceFilesPath).Count();
            int _voices = 0;
            int _npcs = 0;
            foreach (var directory in Directory.GetDirectories(VoiceFilesPath))
            {
                foreach (var voiceDirectory in Directory.GetDirectories(directory))
                {
                    _npcs++;
                    _voices += Directory.GetFiles(voiceDirectory, "*.ogg").Length;
                }
            }
            Data["voices"] = _voices.ToString("000000");
            Data["npcs"] = _npcs.ToString("0000");
            Data["actors"] = _actors.ToString("000");
            await WriteJSON(DirectoryPath + "/Data.json", Data);
        }

        public void SetIgnored(string speaker)
        {
            Task.Run(async () => await SetIgnoredCoroutine(speaker));
        }

        async Task SetIgnoredCoroutine(string speaker)
        {
            Ignored.Add(speaker);
        }
        #endregion


        #region Utility

        public string GetFilePath(XivMessage xivMessage)
        {
            string voiceName = xivMessage.VoiceName.ToString();
            string speaker = xivMessage.Speaker;
            string sentence = xivMessage.Sentence;

            speaker = Regex.Replace(speaker, @"[^a-zA-Z0-9 _-]", "").Replace(" ", "_").Replace("-", "_");

            string cleanedSentence = Regex.Replace(sentence, "<[^<]*>", "");
            cleanedSentence = RemoveSymbolsAndLowercase(cleanedSentence);
            string actorDirectory = VoiceFilesPath + "/" + voiceName;
            string speakerDirectory = actorDirectory + "/" + speaker;

            string filePath = speakerDirectory + "/" + cleanedSentence;
            int missingFromDirectoryPath = 0;
            if (DirectoryPath.Length < 13)
                missingFromDirectoryPath = 13 - DirectoryPath.Length;
            int maxLength = 200 - ((DirectoryPath + "/" + voiceName + "/" + speaker).Length);
            maxLength -= missingFromDirectoryPath;
            if (cleanedSentence.Length > maxLength)
                cleanedSentence = cleanedSentence.Substring(0, maxLength);

            cleanedSentence = Regex.Replace(cleanedSentence, @"[^a-zA-Z0-9 _-]", "").Replace(" ", "_").Replace("-", "_");
            filePath = speakerDirectory + "/" + cleanedSentence;

            // Get Wav from filePath if it exists and return it as AudioClip, if not, return Null
            Plugin.PluginLog.Information($"looking for path [{filePath + ".ogg"}]");
            return filePath + ".ogg";
        }

        public string VoiceDataExists(string voiceName, string speaker, string sentence)
        {
            speaker = Regex.Replace(speaker, @"[^a-zA-Z0-9 _-]", "").Replace(" ", "_").Replace("-", "_");
            
            // Create a Path
            string cleanedSentence = Regex.Replace(sentence, "<[^<]*>", "");
            cleanedSentence = RemoveSymbolsAndLowercase(cleanedSentence);
            string actorDirectory = VoiceFilesPath + "/" + voiceName;
            string speakerDirectory = actorDirectory + "/" + speaker;

            string filePath = speakerDirectory + "/" + cleanedSentence;
            int missingFromDirectoryPath = 0;
            if (DirectoryPath.Length < 13)
                missingFromDirectoryPath = 13 - DirectoryPath.Length;
            int maxLength = 200 - ((DirectoryPath + "/" + voiceName + "/" + speaker).Length);
            maxLength -= missingFromDirectoryPath;
            if (cleanedSentence.Length > maxLength)
                cleanedSentence = cleanedSentence.Substring(0, maxLength);

            cleanedSentence = Regex.Replace(cleanedSentence, @"[^a-zA-Z0-9 _-]", "").Replace(" ", "_").Replace("-", "_");
            filePath = speakerDirectory + "/" + cleanedSentence;

            // Get Wav from filePath if it exists and return it as AudioClip, if not, return Null
            Plugin.PluginLog.Information($"looking for path [{filePath + ".ogg"}]");
            if (File.Exists(filePath + ".ogg"))
            {
                /*
                string jsonContent = File.ReadAllText(filePath + ".json");
                var json = JsonConvert.DeserializeObject<DialogueData>(jsonContent);

                //Plugin.PluginLog.Information(json.sentence);
                var sentence_no_spaces = Regex.Replace(sentence, @"\s+", "").Trim();
                var pattern = "(?<!the )\\b" + "Arc" + "\\b(?! of the)";
                var json_no_space = Regex.Replace(json.sentence, pattern, "_NAME_");

                //Plugin.PluginLog.Information(json_no_space);
                json_no_space = Regex.Replace(json_no_space, @"\s+", "").Trim();
                if (json_no_space.StartsWith("..."))
                    json_no_space = json_no_space[3..];
                //Plugin.PluginLog.Information(json_no_space);

                if (sentence_no_spaces != json_no_space && !sentence.Contains("<"))
                {
                    Plugin.PluginLog.Information("Sentence from XIVV:" + sentence_no_spaces);
                    Plugin.PluginLog.Information("Sentence from JSON:" + json_no_space);
                    return "report";
                }
                else
                */
                    return filePath + ".ogg";
            }
            else
                return null;
        }
        [Serializable]
        public class DialogueData
        {
            public string speaker;
            public string sentence;
            public string lastSave;
        }

        public string RemoveSymbolsAndLowercase(string input)
        {
            // Replace player name with Adventurer before loading or saving
            string pattern = "\\b" + "_NAME_" + "\\b";
            string result = Regex.Replace(input, pattern, "Adventurer");

            StringBuilder stringBuilder = new StringBuilder();
            foreach (char c in result)
            {
                if (char.IsLetter(c))
                {
                    stringBuilder.Append(char.ToLower(c));
                }
            }
            return stringBuilder.ToString();
        }

        public XivNPC GetNPC(string npcName, string npcId, TTSData ttsData, ref bool fetchedByID)
        {
            // Handle Bubbles
            if (ttsData != null && npcName == "Bubble")
            {
                // TODO: Check if the bubble belongs to one of the main characters by making a list

                Plugin.PluginLog.Information("GetNPC: " + npcName + " - " + npcId + " --> Grabbed Bubble");
                XivNPC npc = new XivNPC();
                npc.Gender = ttsData.Gender;
                npc.Race = ttsData.Race;
                npc.Tribe = ttsData.Tribe;
                npc.Body = ttsData.Body;
                npc.Eyes = ttsData.Eyes;
                npc.Type = "Default";
                return npc;
            }

            // Handle NPCs Recognized by ID Rather Than Name
            else if(!npcId.IsNullOrEmpty() && NpcData.ContainsKey(npcId))
            {
                Plugin.PluginLog.Information("GetNPC: " + npcName + ", ID: " + npcId + " --> Grabbed ID");
                fetchedByID = true;
                return NpcData[npcId];
            }

            // Handle NPCs Recognized by Name
            else if (NpcData.ContainsKey(npcName))
            {
                Plugin.PluginLog.Information("GetNPC: " + npcName + " --> Grabbed NpcData");
                return NpcData[npcName];
            }

            // Handle Beast Tribes
            else if (ttsData != null && ttsData.Body == "Beastman")
            {
                Plugin.PluginLog.Information("GetNPC: " + npcName + ", Beast Tribe: " + ttsData.Race + " --> Grabbed NpcData");
                XivNPC npc = new XivNPC();
                npc.Gender = ttsData.Gender;
                npc.Race = ttsData.Race;
                npc.Tribe = ttsData.Tribe;
                npc.Body = ttsData.Body;
                npc.Eyes = ttsData.Eyes;
                npc.Type = "Default";
                return npc;
            }

            // When NPC Does Not Exist in NPC Database
            else
            {
                Plugin.PluginLog.Information("GetNPC: " + npcName + ", ID: " + npcId + " --> NULL");
                return null;
            }
        }

        public XivMessage GetNameless(XivMessage msg)
        {
            XivEngine.Instance.Database.Plugin.Log("GetNameless");
            if (Nameless.ContainsKey(msg.Sentence))
            {
                msg.Speaker = Nameless[msg.Sentence];
            }
            else if (this.Plugin.Config.FrameworkActive)
            {
                string filePath = DirectoryPath + "/nameless.json";
                Nameless[msg.Sentence] = msg.Speaker;
                Task.Run(async () => await WriteJSON(filePath, Nameless));
            }
            return msg;
        }

        public XivMessage GetRetainer(XivMessage msg)
        {
            string sanitizedMessage = Regex.Replace(msg.Sentence, @"[\s,.]", "");
            foreach (var key in Retainers.Keys)
            {
                string sanitizedKey = Regex.Replace(key, @"[\s,.]", "");
                if (sanitizedMessage.Equals(sanitizedKey))
                {
                    msg.Speaker = Retainers[key];
                    msg.isRetainer = true;
                    break;
                }
            }
            return msg;
        }

        public string GenerateRandomSuffix(int x = 4)
        {
            System.Random random = new System.Random();
            int randomNumber = random.Next(0, 10 * x);
            return randomNumber.ToString("D" + x.ToString());
        }

        public async Task<string> FetchDateFromServer(string url)
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        return content;
                    }
                    else
                    {
                        Plugin.PluginLog.Error("Failed to retrieve data: " + response.StatusCode);
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Error("Failed to load the data from the server: " + ex.Message);
                    return null;
                }
            }
        }

        public void DeleteLeftoverZipFiles()
        {
            var zipFiles = Directory.GetFiles(DirectoryPath, "*.zip", SearchOption.TopDirectoryOnly);
            foreach (var zipFile in zipFiles)
            {
                try
                {
                    File.Delete(zipFile);
                    Plugin.PluginLog.Information($"Deleted zip file: {zipFile}");
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Error($"Failed to delete zip file: {zipFile}. Error: {ex.Message}");
                }
            }

            var oggFiles = Directory.GetFiles(DirectoryPath, "*.ogg", SearchOption.TopDirectoryOnly);
            foreach (var oggFile in oggFiles)
            {
                try
                {
                    File.Delete(oggFile);
                    Plugin.PluginLog.Information($"Deleted ogg file: {oggFile}");
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Error($"Failed to delete ogg file: {oggFile}. Error: {ex.Message}");
                }
            }
        }

        public string ProcessSentence(string sentence)
        {
            // Handle cases where the entire sentence is just "Arc" with various punctuations
            if (Regex.IsMatch(sentence, @"^_NAME_([.!?]{1,3})?$"))
            {
                return string.Empty;
            }

            if (!XivEngine.Instance.Database.ForcePlayerName)
            {

                // Handle the specific case where the sentence starts with "Arc, "
                sentence = Regex.Replace(sentence, @"^_NAME_,\s+", "");

                // Handle the new case where ", Arc - " exists and change it to ", "
                sentence = Regex.Replace(sentence, @",\s?_NAME_\s?-\s?", ", ");

                // Handle cases where ", Arc" is followed by specific punctuation anywhere in the sentence
                sentence = Regex.Replace(sentence, @"\s?,\s?_NAME_([,.!?]+)", "$1");

                // Adjusted pattern to handle "Arc" before or after the titles
                string titlesPattern = @"(?:\b_NAME_\s+)?(Mister|Miss|Master|Mistress|Sir|Lady|Private|Corporal|Sergeant|Lieutenant|Captain|Commander|Scion|Adventurer|Hero)(?:\s+_NAME_)?\b";
                sentence = Regex.Replace(sentence, titlesPattern, "$1", RegexOptions.IgnoreCase);

            }

            // General replacement for "Arc" not at the start of a sentence or not followed by a comma or period
            sentence = Regex.Replace(sentence, @"\b_NAME_\b", XivEngine.Instance.Database.PlayerName, RegexOptions.IgnoreCase);

            // Special case: "Arc" at the beginning of the sentence not followed by a punctuation to be removed
            sentence = Regex.Replace(sentence, @"^_NAME_\s+", XivEngine.Instance.Database.PlayerName, RegexOptions.IgnoreCase);

            // Clean up potential double spaces or incorrect punctuation spacing
            sentence = Regex.Replace(sentence, @"\s+", " ").Trim();
            sentence = Regex.Replace(sentence, @"\s([,\.!?])", "$1");

            if (ForceWholeSentence)
                return WholeSentence;
            else
                return sentence;
        }

        #endregion


        #region Access

        public string GetDataSource() => "http://arcsidian.net/access.php";
        public string GetReportSource() => "http://arcsidian.net/report.php";
        private readonly HttpClient client = new HttpClient();

        public async Task GetRequest(XivMessage xivMessage)
        {
            XivEngine.Instance.Database.Plugin.Print("Connecting to the server to get the line if it has already been created...");
            Plugin.PluginLog.Info("Starting GET");
            // Use Lexicon
            string cleanedMessage = xivMessage.Sentence;
            cleanedMessage = ProcessSentence(cleanedMessage);
            foreach (KeyValuePair<string, string> entry in XivEngine.Instance.Database.Lexicon)
            {
                string pattern = "\\b" + entry.Key + "\\b";
                cleanedMessage = Regex.Replace(cleanedMessage, pattern, entry.Value, RegexOptions.IgnoreCase);
            }
            cleanedMessage = Regex.Replace(cleanedMessage, "  ", " ");
            xivMessage.FilePath = GetFilePath(xivMessage);
            string fileName = Path.GetFileName(xivMessage.FilePath);
            string url = $"{GetDataSource()}?user={xivMessage.TtsData.User}&speaker={Uri.EscapeDataString(xivMessage.VoiceName)}&npc={Uri.EscapeDataString(xivMessage.Speaker)}&type=Get&filename={Uri.EscapeDataString(fileName)}";
            try
            {
                // Send the request
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                // Check the response content type for the second request
                if (response.Content.Headers.ContentType.MediaType == "audio/ogg")
                {
                    byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();
                    string directoryPath = Path.GetDirectoryName(xivMessage.FilePath);
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                        Data["npcs"] = (int.Parse(Data["npcs"]) + 1).ToString("0000");
                    }
                    await File.WriteAllBytesAsync(xivMessage.FilePath, fileBytes);
                    var jsondata = new Dictionary<string, string>
                    {
                        ["speaker"] = xivMessage.Speaker,
                        ["sentence"] = xivMessage.Sentence,
                        ["lastSave"] = DateTime.UtcNow.ToString("o")
                    };
                    string directory = Path.Combine(Path.GetDirectoryName(xivMessage.FilePath), Path.GetFileNameWithoutExtension(xivMessage.FilePath));
                    await WriteJSON(directory + ".json", jsondata);
                    XivEngine.Instance.Database.Plugin.Print("Line downloaded.");
                    Data["voices"] = (int.Parse(Data["voices"]) + 1).ToString("000000");
                    XivEngine.Instance.SpeakLocallyAsync(xivMessage);
                    await WriteJSON(DirectoryPath + "/Data.json", Data);
                }
                else
                {
                    // Read the response as a string
                    string responseBody = await response.Content.ReadAsStringAsync();
                    XivEngine.Instance.Database.Plugin.Print("-->" + responseBody);
                    _ = Task.Run(async () =>
                    {
                        if (Plugin.Config.Reports)
                            await XivEngine.Instance.ReportToArcJSON(xivMessage, xivMessage.GetRequested, "");
                        if (Plugin.Config.LocalTTSEnabled)
                            await XivEngine.Instance.SpeakAI(xivMessage);
                    });
                }
            }
            catch (HttpRequestException e)
            {
                XivEngine.Instance.Database.Plugin.Print(e.Message);
                _ = Task.Run(async () =>
                {
                    if (Plugin.Config.Reports)
                        await XivEngine.Instance.ReportToArcJSON(xivMessage, xivMessage.GetRequested, "");
                    if (Plugin.Config.LocalTTSEnabled)
                        await XivEngine.Instance.SpeakAI(xivMessage);
                });
            }
        }

        #endregion

    }

}