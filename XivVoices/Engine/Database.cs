using Newtonsoft.Json;
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
using Dalamud.Logging;
using System.Threading.Tasks;
using System.Reflection;
using System.Net.Http;
using Xabe.FFmpeg;
using Dalamud.Utility;

namespace XivVoices.Engine
{
    public class Database
    {

        #region Private Parameters
        private DalamudPluginInterface _pluginInterface;
        #endregion

        #region Public Parameters
        public string RootPath { get; set; }
        public string DirectoryPath { get; set; }
        public string VoiceFilesPath { get { return Path.Combine(DirectoryPath, "Data"); } }
        public string ToolsPath { get { return "C:/XIV_Voices/Tools";  } }
        public string Firstname { get; } = "_FIRSTNAME_";
        public string Lastname { get; } = "_LASTNAME_";
        public Dictionary<string, XivNPC> NpcData { get; set; }
        public Dictionary<string, PlayerCharacter> PlayerData { get; set; }
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
                "OIC Administrator",
                "OIC Officer of Arms",
                "OIC Quartermaster",
                "Oroniri Merchant",
                "Oroniri Warrior",
                "Picker of Locks",
                "Provisions Crate",
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
                "Vexed Villager",
                "Vault Friar",
                "Well-informed Adventurer",
                "Wood Wailer Lance",
                "Wounded Imperial",
                "Wounded Resistance Fighter",
                "Yellow Moon Admirer",
                "Yellowjacket Captain",
            };
        public Dictionary<string, string> Data { get; set; }
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
        public string AccessToken { get; set; }
        public bool RequestBusy { get; set; } = false;
        public bool RequestMuteBusy { get; set; } = false;
        public bool RequestActive { get; set; } = false;
        #endregion

        public Plugin Plugin { get; set; }

        #region Unity Methods
        public Database(DalamudPluginInterface pluginInterface, Plugin plugin)
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

            // Check for Data folder
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

            

            PluginLog.Information("Working directory is: " + DirectoryPath);

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
                PluginLog.Information("Loading Data...");
                await LoadDataAsync();
                await LoadLexiconsAsync();

                Plugin.Chat.Print("Loading Nameless Data...");
                PluginLog.Information("Loading Nameless Data...");
                await LoadNamelessAsync();

                Plugin.Chat.Print("Loading Retainers Data...");
                PluginLog.Information("Loading Nameless Data...");
                await LoadRetainersAsync();

                Plugin.Chat.Print("Loading Ignored Data...");
                PluginLog.Information("Loading Ignored Data...");
                await LoadIgnoredAsync();

                Plugin.Chat.Print("Loading NPC Data...");
                PluginLog.Information("Loading NPC Data...");
                await LoadNPCsAsync();

                Plugin.Chat.Print("Loading Player Data...");
                PluginLog.Information("Loading Player Data...");
                await LoadPlayersAsync();

                Plugin.Chat.Print("Loading Voice Names...");
                PluginLog.Information("Loading Voice Names...");

                await LoadVoiceNamesAsync();

                Framework = new Framework();

                Plugin.Chat.Print("Done.");
                PluginLog.Information("Done.");
                await Task.Delay(200);

                DeleteLeftoverZipFiles();

                string tokenFilePath = Path.Combine(DirectoryPath, "access.txt");
                if (File.Exists(tokenFilePath))
                {
                    Access = true;
                    AccessToken = await File.ReadAllTextAsync(tokenFilePath);
                }
            }
            catch (Exception ex)
            {
                PluginLog.LogError(ex, "Error during database load.");
            }
        }

        private async Task LoadDataAsync()
        {
            string filePath = DirectoryPath + "/data.json";
            Data = ReadFile(filePath);
            if (Data == null)
            {
                Data = new Dictionary<string, string>();
                Data["voices"] = "0";
                Data["npcs"] = "0";
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
                PluginLog.LogError("Failed to load Lexicon.json from Resources.");
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
                PluginLog.LogError("Failed to load Retainers.json from Resources.");
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
                PluginLog.LogError("Something is wrong with the NPC database");
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
                PluginLog.LogError("Failed to load voiceNames.json from embedded resources.");
                return;
            }

            List<VoiceMapping> voiceMappings = JsonConvert.DeserializeObject<List<VoiceMapping>>(json);
            if (voiceMappings == null)
            {
                VoiceNames = new Dictionary<string, string>();
                PluginLog.LogError("Failed to deserialize voiceNames.json.");
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
            //if (XivVoices.Instance.ArcFramework) XivVoices.Instance.ArcFramework.LoadDatabase();
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
                PluginLog.LogError($"Failed to find embedded resource: {resourceName}");
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
                PluginLog.LogError($"Failed to find embedded resource: {resourceName}");
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
                    PluginLog.LogError($"Failed to load resource: {resourceName}");
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
                PluginLog.LogError($"Failed to find embedded resource: {resourceName}");
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
                PluginLog.Information(jsonDictionary.Count + " NPCs have been loaded.");
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

            PluginLog.Information("JSON file written successfully to path: " + filePath);
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
                Data["actors"] = (int.Parse(Data["actors"]) + 1).ToString();
            }

            if (!Directory.Exists(speakerDirectory))
            {
                Directory.CreateDirectory(speakerDirectory);
                Data["npcs"] = (int.Parse(Data["npcs"]) + 1).ToString();
            }

            try
            {
                await File.WriteAllBytesAsync(xivMessage.FilePath + ".mp3", audioData);

                PluginLog.Information("MP3 file written successfully to path: " + filePath);
                if (!fileExistedBefore)
                {
                    Data["voices"] = (int.Parse(Data["voices"]) + 1).ToString();
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
                PluginLog.LogError($"Failed to write MP3 file to path: {filePath}. Error: {ex.Message}");
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
            Data["voices"] = _voices.ToString();
            Data["npcs"] = _npcs.ToString();
            Data["actors"] = _actors.ToString();
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
            PluginLog.Information($"looking for path [{filePath + ".ogg"}]");
            if (File.Exists(filePath + ".ogg"))
            {
                string jsonContent = File.ReadAllText(filePath + ".json");
                var json = JsonConvert.DeserializeObject<DialogueData>(jsonContent);

                //PluginLog.Information(json.sentence);
                var sentence_no_spaces = Regex.Replace(sentence, @"\s+", "").Trim();
                var pattern = "\\b" + "Arc" + "\\b";
                var json_no_space = Regex.Replace(json.sentence, pattern, "_NAME_");
                //PluginLog.Information(json_no_space);
                json_no_space = Regex.Replace(json_no_space, @"\s+", "").Trim();
                if (json_no_space.StartsWith("..."))
                    json_no_space = json_no_space[3..];
                //PluginLog.Information(json_no_space);

                if (sentence_no_spaces != json_no_space && !sentence.Contains("<"))
                {
                    PluginLog.Information("Sentence from XIVV:" + sentence_no_spaces);
                    PluginLog.Information("Sentence from JSON:" + json_no_space);
                    return "report";
                }
                else
                    return filePath + ".ogg";
            }
            else if (File.Exists(filePath + ".wav"))
                return filePath + ".wav";
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

        string RemoveSymbolsAndLowercase(string input)
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
                PluginLog.Information("GetNPC: " + npcName + " - " + npcId + " --> Grabbed Bubble");
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
                PluginLog.Information("GetNPC: " + npcName + ", ID: " + npcId + " --> Grabbed ID");
                fetchedByID = true;
                return NpcData[npcId];
            }

            // Handle NPCs Recognized by Name
            else if (NpcData.ContainsKey(npcName))
            {
                PluginLog.Information("GetNPC: " + npcName + " --> Grabbed NpcData");
                return NpcData[npcName];
            }

            // Handle Beast Tribes
            else if (ttsData != null && ttsData.Body == "Beastman")
            {
                PluginLog.Information("GetNPC: " + npcName + ", Beast Tribe: " + ttsData.Race + " --> Grabbed NpcData");
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
                PluginLog.Information("GetNPC: " + npcName + ", ID: " + npcId + " --> NULL");
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
                        PluginLog.LogError("Failed to retrieve data: " + response.StatusCode);
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.LogError("Failed to load the data from the server: " + ex.Message);
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
                    PluginLog.Information($"Deleted zip file: {zipFile}");
                }
                catch (Exception ex)
                {
                    PluginLog.LogError($"Failed to delete zip file: {zipFile}. Error: {ex.Message}");
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
        private readonly HttpClient client = new HttpClient();
        private string previousPath = "";
        public async Task Request(XivMessage xivMessage, string id)
        {
            RequestBusy = true;
            RequestActive = false;

            XivEngine.Instance.Database.Plugin.Print("ForceWholeSentence -->" + ForceWholeSentence);

            // Use Lexicon
            string cleanedMessage = xivMessage.Sentence;
            foreach (KeyValuePair<string, string> entry in XivEngine.Instance.Database.Lexicon)
            {
                string pattern = "\\b" + entry.Key + "\\b";
                cleanedMessage = Regex.Replace(cleanedMessage, pattern, entry.Value, RegexOptions.IgnoreCase);
            }
            cleanedMessage = ProcessSentence(cleanedMessage);
            cleanedMessage = Regex.Replace(cleanedMessage, "  ", " ");

            string fileName = Path.GetFileName(xivMessage.FilePath);
            string url = $"http://arcsidian.com/xivv/request.php?token={AccessToken}&speaker={Uri.EscapeDataString(xivMessage.VoiceName)}&npc={Uri.EscapeDataString(xivMessage.Speaker)}&sentence={Uri.EscapeDataString(cleanedMessage)}&type=redo&filename={Uri.EscapeDataString(fileName)}";

            try
            {
                // Send the request
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                // Check the response content type
                if (response.Content.Headers.ContentType.MediaType == "audio/ogg")
                {
                    // Save the received file (assuming it could be mp3 or ogg)
                    byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();
                    string tempFilePath = DirectoryPath +"/temp.ogg"; // Temporary file path
                    await File.WriteAllBytesAsync(tempFilePath, fileBytes);

                    // Convert to OGG Opus using FFmpeg
                    string outputFilePath = DirectoryPath + "/" + fileName;
                    string ffmpegDirectoryPath = Path.Combine(XivEngine.Instance.Database.ToolsPath);
                    FFmpeg.SetExecutablesPath(ffmpegDirectoryPath);

                    string arguments = $"-i \"{tempFilePath}\" -c:a libopus \"{outputFilePath}\"";
                    IConversion conversion = FFmpeg.Conversions.New().AddParameter(arguments);
                    await conversion.Start();

                    // Clean up the temporary file
                    File.Delete(tempFilePath);

                    XivEngine.Instance.Database.Plugin.Print("New dialogue has been generated, choose YES/NO to keep or delete it.");
                    previousPath = xivMessage.FilePath;
                    xivMessage.FilePath = outputFilePath;
                    XivEngine.Instance.SpeakLocallyAsync(xivMessage);
                    RequestBusy = false;
                    RequestActive = true;
                }
                else
                {
                    // Read the response as a string
                    string responseBody = await response.Content.ReadAsStringAsync();
                    if (responseBody == "EXISTS")
                    {
                        XivEngine.Instance.Database.Plugin.Print("Someone already updated this voice file, retrieving the latest version..");
                        url = $"http://arcsidian.com/xivv/request.php?token={AccessToken}&speaker={Uri.EscapeDataString(xivMessage.VoiceName)}&npc={Uri.EscapeDataString(xivMessage.Speaker)}&sentence={Uri.EscapeDataString(xivMessage.Sentence)}&type=latest&filename={Uri.EscapeDataString(fileName)}";
                        
                        // Send the second request
                        HttpResponseMessage latestResponse = await client.GetAsync(url);
                        latestResponse.EnsureSuccessStatusCode();

                        // Check the response content type for the second request
                        if (latestResponse.Content.Headers.ContentType.MediaType == "audio/ogg")
                        {

                            //-------------------------------
                            byte[] fileBytes = await latestResponse.Content.ReadAsByteArrayAsync();
                            string tempFilePath = DirectoryPath + "/" + fileName;
                            File.Delete(xivMessage.FilePath);
                            await File.WriteAllBytesAsync(tempFilePath, fileBytes);

                            // Convert to OGG Opus using FFmpeg
                            string outputFilePath = xivMessage.FilePath;
                            string ffmpegDirectoryPath = Path.Combine(XivEngine.Instance.Database.ToolsPath);
                            FFmpeg.SetExecutablesPath(ffmpegDirectoryPath);

                            string arguments = $"-i \"{tempFilePath}\" -c:a libopus \"{outputFilePath}\"";
                            IConversion conversion = FFmpeg.Conversions.New().AddParameter(arguments);
                            await conversion.Start();

                            // Clean up the temporary file
                            File.Delete(tempFilePath);
                            XivEngine.Instance.SpeakLocallyAsync(xivMessage);
                        }
                        else
                        {
                            // Read the response as a string
                            responseBody = await latestResponse.Content.ReadAsStringAsync();
                            XivEngine.Instance.Database.Plugin.Print("-->" + responseBody);
                        }
                    }
                    else
                    {
                        XivEngine.Instance.Database.Plugin.Print(responseBody);
                    }

                    RequestBusy = false;
                    RequestActive = false;

                }
            }
            catch (HttpRequestException e)
            {
                XivEngine.Instance.Database.Plugin.Print(e.Message);
                RequestBusy = false;
                RequestActive = false;
            }
        }

        public async Task Confirm(XivMessage xivMessage)
        {
            RequestActive = false;

            string fileName = Path.GetFileName(xivMessage.FilePath);
            string url = $"http://arcsidian.com/xivv/request.php?token={AccessToken}&speaker={Uri.EscapeDataString(xivMessage.VoiceName)}&npc={Uri.EscapeDataString(xivMessage.Speaker)}&type=confirm&filename={Uri.EscapeDataString(fileName)}";

            try
            {
                // Send the request
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                
                if(responseBody == "CONFIRMED")
                {
                    XivEngine.Instance.Database.Plugin.Print("New dialogue has been confirmed by the server.");
                    string fromFilePath = xivMessage.FilePath;
                    string toFilePath = previousPath;

                    try
                    {
                        File.Copy(fromFilePath, toFilePath, true);
                        XivEngine.Instance.Database.Plugin.Print("Replacement completed.");
                        File.Delete(fromFilePath);
                        xivMessage.FilePath = toFilePath;
                        previousPath = "";
                    }
                    catch (Exception ex)
                    {
                        XivEngine.Instance.Database.Plugin.Print($"Replacement failed: {ex.Message}");
                    }
                }
            }
            catch (HttpRequestException e)
            {
                XivEngine.Instance.Database.Plugin.Print(e.Message);
            }
        }

        public async Task Cancel(XivMessage xivMessage, bool deleteOnly = false)
        {
            RequestActive = false;

            string fileName = Path.GetFileName(xivMessage.FilePath);
            string url = $"http://arcsidian.com/xivv/request.php?token={AccessToken}&speaker={Uri.EscapeDataString(xivMessage.VoiceName)}&npc={Uri.EscapeDataString(xivMessage.Speaker)}&type=delete&filename={Uri.EscapeDataString(fileName)}";

            try
            {
                // Send the request
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                if (responseBody == "FILE DELETED")
                {
                    XivEngine.Instance.Database.Plugin.Print("NO --> deleted by the server.");

                    try
                    {
                        File.Delete(xivMessage.FilePath);
                        xivMessage.FilePath = previousPath;
                        previousPath = "";
                    }
                    catch (Exception ex)
                    {
                        XivEngine.Instance.Database.Plugin.Print($"Deletion failed: {ex.Message}");
                    }
                }
            }
            catch (HttpRequestException e)
            {
                XivEngine.Instance.Database.Plugin.Print(e.Message);
            }
        }

        public async Task Mute(XivMessage xivMessage)
        {
            RequestMuteBusy = true;

            string fileName = Path.GetFileName(xivMessage.FilePath);
            string url = $"http://arcsidian.com/xivv/request.php?token={AccessToken}&speaker={Uri.EscapeDataString(xivMessage.VoiceName)}&npc={Uri.EscapeDataString(xivMessage.Speaker)}&type=mute&filename={Uri.EscapeDataString(fileName)}";

            try
            {
                // Send the request
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                // Check the response content type
                if (response.Content.Headers.ContentType.MediaType == "audio/ogg")
                {
                    // Save the received file (assuming it could be mp3 or ogg)
                    byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(xivMessage.FilePath, fileBytes);
                    XivEngine.Instance.SpeakLocallyAsync(xivMessage);
                    RequestMuteBusy = false;
                }
                else
                {
                    // Read the response as a string
                    string responseBody = await response.Content.ReadAsStringAsync();
                    XivEngine.Instance.Database.Plugin.Print(responseBody);
                    RequestMuteBusy = false;
                }
            }
            catch (HttpRequestException e)
            {
                XivEngine.Instance.Database.Plugin.Print(e.Message);
                RequestMuteBusy = false;
            }
        }

        public async Task Run(bool once = false)
        {
            XivEngine.Instance.Database.Plugin.Chat.Print("Run: Start");
            string directoryPath = DirectoryPath + "/redo";

            // Get all json files in directory
            string[] filePaths = System.IO.Directory.GetFiles(directoryPath, "*.json");
            int i = 0;
            foreach (string filePath in filePaths)
            {
                if (once)
                {
                    if (i > 0)
                    {
                        return;
                    }
                    i++;
                }

                await Task.Delay(100);
                // Read one file json and process it
                Dictionary<string, string> wavdata = XivEngine.Instance.Database.ReadFile(filePath);

                if (wavdata.Count > 3)
                {
                    string[] fullname = wavdata["user"].Split("@")[0].Split(" ");
                    wavdata["sentence"] = wavdata["sentence"].Replace(fullname[0], "_FIRSTNAME_");
                    if (fullname.Length > 1)
                    {
                        wavdata["sentence"] = wavdata["sentence"].Replace(fullname[1], "_LASTNAME_");
                    }
                }
                else
                {
                    wavdata["sentence"] = wavdata["sentence"].Replace("Arc", "_FIRSTNAME_");
                }

                TTSData TtsData = new TTSData();
                TtsData.Type = "test";
                TtsData.NpcID = "-1";
                TtsData.Speaker = wavdata["speaker"];
                TtsData.Message = wavdata["sentence"];
                TtsData.Race = "idk";

                if (!wavdata.ContainsKey("gender") || !wavdata.ContainsKey("body") ||
                    !wavdata.ContainsKey("race") || !wavdata.ContainsKey("tribe") ||
                    !wavdata.ContainsKey("eyes"))
                {
                    ;
                }
                else
                {
                    TtsData.Gender = wavdata["gender"];
                    TtsData.Body = wavdata["body"];
                    TtsData.Race = wavdata["race"];
                    if (TtsData.Race.StartsWith("Unknown combination"))
                    {
                        // Extract the ID and Region using regex
                        var match = Regex.Match(TtsData.Race, @"ID (\d+), Region (\d+)");
                        if (match.Success)
                        {
                            TtsData.SkeletonID = match.Groups[1].Value;
                            TtsData.Region = ushort.Parse(match.Groups[2].Value);
                            TtsData.Race = XivEngine.Instance.Mapper.GetSkeleton(int.Parse(TtsData.SkeletonID), TtsData.Region);
                        }
                    }
                    XivEngine.Instance.Database.Plugin.Log("---> " + TtsData.Race);
                    TtsData.Tribe = wavdata["tribe"];
                    TtsData.Eyes = wavdata["eyes"];
                }

                XivMessage xivMessage = new XivMessage(TtsData);

                xivMessage = XivEngine.Instance.CleanXivMessage(xivMessage);
                if (xivMessage.Speaker == "???")
                    xivMessage = XivEngine.Instance.Database.GetNameless(xivMessage);
                xivMessage = XivEngine.Instance.UpdateXivMessage(xivMessage);
                if (xivMessage.VoiceName == "Retainer" && !XivEngine.Instance.Database.Plugin.Config.RetainersEnabled) continue;

                XivEngine.Instance.Database.Plugin.Chat.Print("Run: Speaker: " + xivMessage.Speaker);
                XivEngine.Instance.Database.Plugin.Chat.Print("Run: Sentence: " + xivMessage.Sentence);

                if (xivMessage.Speaker == "Unknown" || xivMessage.NPC == null || xivMessage.VoiceName == "Unknown")
                {
                    XivEngine.Instance.Database.Plugin.Chat.Print("Skipping");
                    continue;
                }

                if (xivMessage.Network != "Online")
                {
                    XivEngine.Instance.Database.Plugin.Chat.Print("Run: Playing and deleting json file");
                    try
                    {
                        System.IO.File.Delete(filePath);
                        PluginLog.Information($"Deleted file: {filePath}");
                    }
                    catch (System.Exception ex)
                    {
                        PluginLog.LogError($"Failed to delete file: {filePath}. Error: {ex.Message}");
                    }
                }

                XivEngine.Instance.AddToQueue(xivMessage);
            }
        }

        #endregion
    }

}