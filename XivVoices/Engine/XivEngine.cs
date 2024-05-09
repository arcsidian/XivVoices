using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Concentus.Structs;
using Concentus.Oggfile;
using Xabe.FFmpeg;
using Dalamud.Logging;
using Newtonsoft.Json;
using Dalamud.Utility;
using NAudio.Wave;
using System.Threading;
using System.Net.Http;
using Dalamud.Game.ClientState.Objects.Types;
using System.Numerics;
using XivVoices.LocalTTS;
//using Amazon.Polly;
//using Amazon.Polly.Model;

namespace XivVoices.Engine
{

    public class XivEngine
    {
        #region Private Parameters
        private SemaphoreSlim speakBlock { get; set; }
        private Timer _updateTimer;
        private Timer _autoUpdateTimer;

        private Queue<XivMessage> ffxivMessages = new Queue<XivMessage>();
        private TTSEngine ttsEngine;
        TTSVoiceNative[] localTTS = new TTSVoiceNative[2];
        #endregion


        #region Public Parameters
        private bool Active { get; set; } = false;
        public Database Database { get; set; }
        public Audio Audio { get; set; }
        public DataMapper Mapper { get; set; }
        public Updater Updater { get; set; }
        public bool OnlineTTS { get; set; } = false;

        public List<string> IgnoredDialogues = new List<string>();

        #endregion


        #region Core Methods
        public static XivEngine Instance;

        public XivEngine(Database _database, Audio _audio, Updater _updater)
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
                return;

            speakBlock = new SemaphoreSlim(1, 1);
            this.Database = _database;
            this.Audio = _audio;
            this.Updater = _updater;
            this.ttsEngine = null;
            Mapper = new DataMapper();
            _updateTimer = new Timer(Update, null, 0, 50);
            _autoUpdateTimer = new Timer(AutoUpdate, null, 10000, 600000);
            localTTS[0] = null;
            localTTS[1] = null;
            Active = true;
            this.Database.Plugin.Chat.Print("Engine: I am awake");
        }


        public void StopTTS()
        {
            if (this.ttsEngine != null)
            {
                this.ttsEngine.Dispose();
                this.ttsEngine = null;
            }
            if (localTTS[0] != null)
            {
                localTTS[0].Dispose();
                localTTS[0] = null;
            }
            if (localTTS[1] != null)
            {
                localTTS[1].Dispose();
                localTTS[1] = null;
            }
        }

        private void Update(object state)
        {
            if (!Active) return;

            if (ffxivMessages.Count > 0)
            {
                if (ffxivMessages.TryDequeue(out XivMessage msg))
                {
                    PluginLog.Information($"Update ---> {msg.TtsData.Speaker}: {msg.TtsData.Message}");
                    if (msg.Network == "Online")
                    {
                        //this.Database.Plugin.Chat.Print("CheckMessages: Online");
                        //if (Configuration.PollyEnabled && !Configuration.WebsocketRedirectionEnabled && (msg.Reported || msg.Ignored)) // && !AudioIsMuted?
                        //    Task.Run(async () => await SpeakPollyAsync(msg));
                        if (this.Database.Plugin.Config.LocalTTSEnabled && !this.Database.Plugin.Config.WebsocketRedirectionEnabled && (msg.Reported || msg.Ignored)) // !&& AudioIsMuted?
                            Task.Run(async () => await SpeakAI(msg));
                        else
                            Speak(msg);
                    }
                    else
                    {
                        //this.Database.Plugin.Chat.Print("CheckMessages: Offline");
                        Task.Run(async () => await SpeakLocallyAsync(msg));
                    }
                }
            }

            if (reports.Count > 0)
            {
                ReportXivMessage report = reports.Dequeue();
                if (!this.Database.Plugin.Config.FrameworkActive)
                    Task.Run(async () => await ReportToArcJSON(report.message, report.folder, report.comment));
            }
        }

        private async void AutoUpdate(object state)
        {
            if (!Active) return;
            if (!this.Database.Plugin.Config.AutoUpdate) return;
            if (Updater.Busy) return;
            if (!this.Database.Plugin.Config.Initialized) return;
            string dateString = await this.Database.FetchDateFromServer("http://www.arcsidian.com/xivv.json");
            if (dateString == null) return;

            DateTime serverDateTime = DateTime.Parse(dateString, null, DateTimeStyles.RoundtripKind);
            int comparisonResult = DateTime.Compare(this.Database.Plugin.Config.LastUpdate, serverDateTime);
            if (comparisonResult < 0)
            {
                this.Database.Plugin.Chat.Print("Xiv Voices: Checking for new Voice Files... There is a new update!");
                this.Updater.ServerLastUpdate = serverDateTime;
                await this.Updater.Check(true, this.Database.Plugin.Window.IsOpen);
            }
            else
            {
                //this.Database.Plugin.Chat.Print("Xiv Voices: Checking for new Voice Files... You're up to date!");
            }
        }

        public void Dispose()
        {
            speakBlock.Dispose();
            StopTTS();
            _autoUpdateTimer?.Dispose();
            _autoUpdateTimer = null;
            _updateTimer?.Dispose();
            _updateTimer = null;
            Active = false;
        }
        #endregion


        #region Processing Methods
        public void Process(string type, string speaker, string npcID, string skeletonID, string message, string body, string gender, string race, string tribe, string eyes, string language, Vector3 position, Character character, string user)
        {

            TTSData ttsData = new TTSData(type, speaker, npcID, skeletonID, message, body, gender, race, tribe, eyes, language, position, character, user);
            XivMessage msg = new XivMessage(ttsData);

            if (ttsData.Type != "Cancel" && ttsData.Language != "English")
                return;

            if (ttsData.Speaker == "NPC")
                return;

            //PluginLog.Information($"------> Icoming: [Type]: {type}, [Gender]:{msg.TtsData.Gender}, [Body]:{msg.TtsData.Body}, [Race]:{msg.TtsData.Race}, [Tribe]:{msg.TtsData.Tribe}, [Eyes]:{msg.TtsData.Eyes} [Reported]:{msg.Reported} [Ignored]:{msg.Ignored}, [Speaker]:{speaker}, [Message]:{msg.TtsData.Message},");

            if (this.Database.Plugin.Config.SkipEnabled && (ttsData.Type == "Dialogue" || ttsData.Type == "Cancel") )
                Audio.StopAudio();

            if (ttsData.Type == "Cancel")
                return;

            PluginLog.Information($"New Dialogue: [Gender]:{msg.TtsData.Gender}, [Body]:{msg.TtsData.Body}, [Race]:{msg.TtsData.Race}, [Tribe]:{msg.TtsData.Tribe}, [Eyes]:{msg.TtsData.Eyes} [Reported]:{msg.Reported} [Ignored]:{msg.Ignored}, [Message]:{msg.TtsData.Message},");

            if (ttsData.Type != "Dialogue" && ttsData.Type != "Bubble" && ttsData.Type != "NPCDialogueAnnouncements")
            {
                PluginLog.Information("[Ignored] " + ttsData.Speaker + ":" + ttsData.Message);
                msg.Ignored = true;
                msg.VoiceName = "Unknown";
                msg.Network = "Online";
                AddToQueue(msg);
                return;
            }

            msg.TtsData.Body = Mapper.GetBody(int.Parse(msg.TtsData.Body));
            msg.TtsData.Race = Mapper.GetRace(int.Parse(msg.TtsData.Race));
            msg.TtsData.Tribe = Mapper.GetTribe(int.Parse(msg.TtsData.Tribe));
            msg.TtsData.Eyes = Mapper.GetEyes(int.Parse(msg.TtsData.Eyes));
            if (msg.TtsData.Body == "Beastman")
            {
                PluginLog.Information("Race before Mapper: " + msg.TtsData.Race);
                msg.TtsData.Race = Mapper.GetSkeleton(int.Parse(msg.TtsData.SkeletonID), Database.Plugin.ClientState.TerritoryType);
                PluginLog.Information("Race after Mapper: " + msg.TtsData.Race);
            }

            string[] fullname = Database.Plugin.ClientState.LocalPlayer.Name.TextValue.Split(" ");

            if (this.Database.Plugin.Config.FrameworkActive)
            {
                msg.Sentence = msg.Sentence.Replace(fullname[0], "_FIRSTNAME_");

                if (fullname.Length > 1)
                {
                    msg.Sentence = msg.Sentence.Replace(fullname[1], "_LASTNAME_");
                }

                msg = CleanXivMessage(msg);
                if (msg.Speaker == "???")
                    msg = this.Database.GetNameless(msg);
                msg = UpdateXivMessage(msg);

                if (msg.FilePath == null)
                    msg.Reported = true;
            }
            else
            {
                var results = GetPossibleSentences(message, fullname[0], fullname[1]);
                bool sentenceFound = false;
                foreach (var result in results)
                {
                    msg.Sentence = result;
                    msg = CleanXivMessage(msg);
                    if (msg.Speaker == "???")
                        msg = this.Database.GetNameless(msg);
                    msg = UpdateXivMessage(msg);
                    if (msg.FilePath != null)
                    {
                        sentenceFound = true;
                        break;
                    }
                }

                if (!sentenceFound)
                {
                    msg.Sentence = msg.TtsData.Message;
                    if (!msg.Reported)
                    {
                        ReportToArc(msg);
                        msg.Reported = true;
                    }
                }
            }



            if (msg.isRetainer && !this.Database.Plugin.Config.RetainersEnabled) return;
            if (IgnoredDialogues.Contains(msg.Speaker + msg.Sentence) || this.Database.Ignored.Contains(msg.Speaker)) return;

            PluginLog.Information($"After processing: [Gender]:{msg.TtsData.Gender}, [Body]:{msg.TtsData.Body}, [Race]:{msg.TtsData.Race}, [Tribe]:{msg.TtsData.Tribe}, [Eyes]:{msg.TtsData.Eyes} [Reported]:{msg.Reported} [Ignored]:{msg.Ignored}, [Message]:{msg.TtsData.Message},");
            if (msg.NPC == null)
                PluginLog.Information("npc is null, voice name is " + msg.VoiceName);

            this.Database.Plugin.PrintLog($"Data: [Gender]:{msg.TtsData.Gender}, [Body]:{msg.TtsData.Body}, [Race]:{msg.TtsData.Race}, [Tribe]:{msg.TtsData.Tribe}, [Eyes]:{msg.TtsData.Eyes} [Reported]:{msg.Reported} [Ignored]:{msg.Ignored}\n{msg.TtsData.Speaker}:{msg.TtsData.Message}\n");

            AddToQueue(msg);

        }

        public static List<string> GetPossibleSentences(string sentence, string firstName, string lastName)
        {
            var replacements = new Dictionary<string, string>();
            var names = new List<string>();

            // Special case where firstname == lastname and the full name is mentioned
            if (firstName == lastName)
            {
                string pattern = "\\b" + firstName + " " + lastName + "\\b";
                sentence = Regex.Replace(sentence, pattern, firstName);
            }

            // Add unique names to replacements and names list
            if (!replacements.ContainsKey(firstName))
            {
                replacements.Add(firstName, "_FIRSTNAME_");
                names.Add(firstName);
            }

            if (!firstName.Equals(lastName) && !replacements.ContainsKey(lastName))
            {
                replacements.Add(lastName, "_LASTNAME_");
                names.Add(lastName);
            }

            List<string> results = new List<string>();
            RecurseCombinations(sentence, replacements, names, 0, results);
            return results;
        }

        private static void RecurseCombinations(string currentSentence, Dictionary<string, string> replacements, List<string> names, int index, List<string> results)
        {
            if (index == names.Count)
            {
                results.Add(currentSentence);
                return;
            }

            string name = names[index];
            string pattern = $@"(?<=^|\W){Regex.Escape(name)}(?=\W|$)";
            Regex regex = new Regex(pattern);

            // Get all distinct positions where the name occurs
            var matches = regex.Matches(currentSentence);
            int matchCount = matches.Count;

            // Generate all combinations of replacements for this name
            for (int i = 0; i < (1 << matchCount); i++) // 2^matchCount combinations
            {
                List<(int Start, int Length, string Replacement)> replacementList = new List<(int, int, string)>();
                for (int j = 0; j < matchCount; j++)
                {
                    if ((i & (1 << j)) != 0)
                    {
                        var match = matches[j];
                        replacementList.Add((match.Index, match.Length, replacements[name]));
                    }
                }

                // Replace using the replacement list to avoid index shifts
                string modifiedSentence = ReplaceUsingList(currentSentence, replacementList);
                RecurseCombinations(modifiedSentence, replacements, names, index + 1, results);
            }
        }

        private static string ReplaceUsingList(string sentence, List<(int Start, int Length, string Replacement)> replacements)
        {
            // Apply replacements from last to first to avoid index shift issues
            replacements.Sort((a, b) => b.Start.CompareTo(a.Start));
            foreach (var (Start, Length, Replacement) in replacements)
            {
                sentence = sentence.Substring(0, Start) + Replacement + sentence.Substring(Start + Length);
            }
            return sentence;
        }


        public XivMessage CleanXivMessage(XivMessage xivMessage)
        {
            // Replace 'full name' with 'firstName'
            string pattern = "\\b" + this.Database.Firstname + " " + this.Database.Lastname + "\\b";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, this.Database.Firstname);

            // Replace 'lastName' with 'firstName'
            pattern = "\\b" + this.Database.Lastname + "\\b";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, this.Database.Firstname);

            // Replace 'firstName' with 'Arc'
            pattern = "\\b" + this.Database.Firstname + "\\b";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "_NAME_");
            // OTHER FUNDAMENTAL CHANGES ===========================

            // 1-  Cactpot Broker Drawing numbers removal
            pattern = @"Come one, come all - drawing number \d{4}";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "Come one, come all - drawing number");

            // 2-  Cactpot Winning Prize
            if (xivMessage.Speaker == "Cactpot Cashier" && xivMessage.Sentence.StartsWith("Congratulations! You have won"))
                xivMessage.Sentence = "Congratulations! You have won!";

            // 3- Delivery Moogle carrier level removal
            pattern = @"Your postal prowess has earned you carrier level \d{2}";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "Your postal prowess has earned you this carrier level");

            // 4- Chocobo Eligible to Participate In Races
            pattern = @"^Congratulations.*eligible to participate in sanctioned chocobo races\.*";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "Congratulations! Your chocobo is now eligible to participate in sanctioned chocobo races.");

            // 5- Chocobo Training
            pattern = @"^What sort of training did you have in mind for .*, (madam|sir)\?$";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "What sort of training did you have in mind for your chocobo?");

            // 6- Teaching Chocobo an Ability
            pattern = @"^You wish to teach .*, (madam|sir)\? Then, if you would be so kind as to provide the requisite manual\.$";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "You wish to teach your chocobo an ability? Then, if you would be so kind as to provide the requisite manual.");

            // 7- Removing Chocobo Ability
            pattern = @"^You wish for .+ to unlearn an ability\? Very well, if you would be so kind as to specify the undesired ability\.\.\.$";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "You wish for your chocobo to unlearn an ability? Very well, if you would be so kind as to specify the undesired ability...");

            // 8- Feoul Lines
            if(xivMessage.Speaker == "Feo Ul")
            {
                if (xivMessage.Sentence.StartsWith("A whispered word, and off"))
                    xivMessage.Sentence = "A whispered word, and off goes yours on a grand adventure! What wonders await at journey's end?";
                else if (xivMessage.Sentence.StartsWith("Carried by the wind, the leaf flutters to the ground"))
                    xivMessage.Sentence = "Carried by the wind, the leaf flutters to the ground - and so does yours return to your side. Was the journey a fruitful one?";
                else if (xivMessage.Sentence.StartsWith("From verdant green to glittering gold"))
                    xivMessage.Sentence = "From verdant green to glittering gold, so does the leaf take on delightful hues with each new season. If you would see yours dressed in new colors, your beautiful branch will attend to the task.";
                else if (xivMessage.Sentence.StartsWith("Oh, my adorable sapling! You have need"))
                    xivMessage.Sentence = "Oh, my adorable sapling! You have need of yours, yes? But sing the word, and let your beautiful branch do what only they can.";
                else if (xivMessage.Sentence.StartsWith("Very well. I shall slip quietly from"))
                    xivMessage.Sentence = "Very well. I shall slip quietly from your servant's dreams. May your leaf flutter, float, and find a way back to you.";
                else if (xivMessage.Sentence.StartsWith("You have no more need of"))
                    xivMessage.Sentence = "You have no more need of yours? So be it! I shall steal quietly from your loyal servant's dreams.";
            }

            // Send help
            xivMessage.Sentence = xivMessage.Sentence
                .Replace("\\u00e1", "á")
                .Replace("\\u00e9", "é")
                .Replace("\\u00ed", "í")
                .Replace("\\u00f3", "ó")
                .Replace("\\u00fa", "ú")
                .Replace("\\u00f1", "ñ")
                .Replace("\\u00e0", "à")
                .Replace("\\u00e8", "è")
                .Replace("\\u00ec", "ì")
                .Replace("\\u00f2", "ò")
                .Replace("\\u00f9", "ù");

            // Fix Three Dots between letters that have no spaces like "hi...there" to "hi... there"
            string result = Regex.Replace(xivMessage.Sentence, @"(\.{3})(\w)", "$1 $2");

            //this.Database.Plugin.Chat.Print(result);
            // Replace normal quotes with quote symbols "" -> “”
            result = Regex.Replace(result, "[“”]", "\"");
            bool isOpeningQuote = true;
            result = Regex.Replace(result, "\"", match =>
            {
                if (isOpeningQuote)
                {
                    isOpeningQuote = false;
                    return "“";
                }
                else
                {
                    isOpeningQuote = true;
                    return "”";
                }
            });
            //this.Database.Plugin.Chat.Print(result);

            // Remove double spaces if any were created
            //result = Regex.Replace(result, "  ", " ");

            // Save As Sentence after using Lexicons
            xivMessage.Sentence = result;

            return xivMessage;
        }

        public XivMessage UpdateXivMessage(XivMessage xivMessage)
        {
            if (xivMessage.Ignored)
            {
                xivMessage.VoiceName = "Unknown";
                xivMessage.Network = "Online";
                return xivMessage;
            }

            // Check if it belongs to a retainer
            xivMessage = this.Database.GetRetainer(xivMessage);


            // Changing 2-letter names because fuck windows defender
            if (xivMessage.Speaker.Length == 2)
                xivMessage.Speaker = "FUDefender_" + xivMessage.Speaker;

            bool fetchedByID = false;
            xivMessage.NPC = this.Database.GetNPC(xivMessage.Speaker, xivMessage.NpcId, xivMessage.TtsData, ref fetchedByID);
            if (xivMessage.NPC != null)
            {
                //this.Database.Plugin.Chat.Print(xivMessage.Speaker + " has been found in the DB");
                xivMessage.VoiceName = GetVoiceName(xivMessage, fetchedByID);
            }
            else
            {
                PluginLog.Information(xivMessage.Speaker + " does not exist in the DB");
                xivMessage.VoiceName = "Unknown";
            }

            if (xivMessage.VoiceName == "Unknown" && xivMessage.Speaker != "???")
            {
                if (!xivMessage.Reported)
                {
                    ReportUnknown(xivMessage);
                    xivMessage.Reported = true;
                }
            }


            xivMessage.FilePath = this.Database.VoiceDataExists(xivMessage.VoiceName.ToString(), xivMessage.Speaker, xivMessage.Sentence);
            if (xivMessage.FilePath.IsNullOrEmpty())
            {
                xivMessage.Network = "Online";
            }
            else if (xivMessage.FilePath == "report")
            {
                xivMessage.Network = "Online";
                if (!Database.Plugin.Config.FrameworkActive)
                {
                    ReportDifferent(xivMessage);
                    xivMessage.Reported = true;
                }

            }
            else
                xivMessage.Network = "Local";

            return xivMessage;
        }

        string GetVoiceName(XivMessage message, bool fetchedByID)
        {
            bool npcWithVariedLooksFound = false;

            if (this.Database.NpcWithVariedLooks.Contains(message.Speaker))
            {
                this.Database.Plugin.PrintLog(message.Speaker + " --> npcWithVariedLooks ");
                message.NPC.BodyType = message.TtsData.Body;
                message.NPC.Gender = message.TtsData.Gender;
                message.NPC.Race = message.TtsData.Race;
                message.NPC.Clan = message.TtsData.Tribe;
                message.NPC.EyeShape = message.TtsData.Eyes;
                npcWithVariedLooksFound = true;
            }

            if (!fetchedByID && this.Database.VoiceNames.TryGetValue(message.Speaker, out string voiceName) && !npcWithVariedLooksFound)
            {
                PluginLog.Information("GetVoiceName: fetchedByID is " + fetchedByID);
                return voiceName;
            }

            // Voice By Age -> Race -> Clan -> Gender -> Face
            else
                return GetOtherVoiceNames(message);
        }

        string GetOtherVoiceNames(XivMessage message)
        {
            if (message.NPC.BodyType == "Adult")
            {
                if (message.NPC.Race == "Au Ra")
                {
                    if (message.NPC.Clan == "Raen" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Au_Ra_Raen_Female_01";
                    if (message.NPC.Clan == "Raen" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Au_Ra_Raen_Female_02";
                    if (message.NPC.Clan == "Raen" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Au_Ra_Raen_Female_03";
                    if (message.NPC.Clan == "Raen" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Au_Ra_Raen_Female_04";
                    if (message.NPC.Clan == "Raen" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Au_Ra_Raen_Female_05";

                    if (message.NPC.Clan == "Raen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Au_Ra_Raen_Male_01";
                    if (message.NPC.Clan == "Raen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Au_Ra_Raen_Male_02";
                    if (message.NPC.Clan == "Raen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Au_Ra_Raen_Male_03";
                    if (message.NPC.Clan == "Raen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Au_Ra_Raen_Male_04";
                    if (message.NPC.Clan == "Raen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Au_Ra_Raen_Male_05";
                    if (message.NPC.Clan == "Raen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                        return "Au_Ra_Raen_Male_06";

                    if (message.NPC.Clan == "Xaela" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Au_Ra_Xaela_Female_01";
                    if (message.NPC.Clan == "Xaela" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Au_Ra_Xaela_Female_02";
                    if (message.NPC.Clan == "Xaela" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Au_Ra_Xaela_Female_03";
                    if (message.NPC.Clan == "Xaela" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Au_Ra_Xaela_Female_04";
                    if (message.NPC.Clan == "Xaela" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Au_Ra_Xaela_Female_05";

                    if (message.NPC.Clan == "Xaela" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Au_Ra_Xaela_Male_01";
                    if (message.NPC.Clan == "Xaela" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Au_Ra_Xaela_Male_02";
                    if (message.NPC.Clan == "Xaela" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Au_Ra_Xaela_Male_03";
                    if (message.NPC.Clan == "Xaela" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Au_Ra_Xaela_Male_04";
                    if (message.NPC.Clan == "Xaela" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Au_Ra_Xaela_Male_05";
                    if (message.NPC.Clan == "Xaela" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                        return "Au_Ra_Xaela_Male_06";
                }

                if (message.NPC.Race == "Elezen")
                {
                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Elezen_Duskwight_Female_01";
                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Elezen_Duskwight_Female_02";
                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Elezen_Duskwight_Female_03";
                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Elezen_Duskwight_Female_04";
                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Elezen_Duskwight_Female_05_06";
                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 6")
                        return "Elezen_Duskwight_Female_05_06";

                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Elezen_Duskwight_Male_01";
                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Elezen_Duskwight_Male_02";
                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Elezen_Duskwight_Male_03";
                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Elezen_Duskwight_Male_04";
                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Elezen_Duskwight_Male_05";
                    if (message.NPC.Clan == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                        return "Elezen_Duskwight_Male_06";

                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Elezen_Wildwood_Female_01";
                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Elezen_Wildwood_Female_02";
                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Elezen_Wildwood_Female_03";
                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Elezen_Wildwood_Female_04";
                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Elezen_Wildwood_Female_05";
                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 6")
                        return "Elezen_Wildwood_Female_06";

                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Elezen_Wildwood_Male_01";
                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Elezen_Wildwood_Male_02";
                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Elezen_Wildwood_Male_03";
                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Elezen_Wildwood_Male_04";
                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Elezen_Wildwood_Male_05";
                    if (message.NPC.Clan == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                        return "Elezen_Wildwood_Male_06";
                }

                if (message.NPC.Race == "Hrothgar")
                {
                    if (message.NPC.Clan == "Helions" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Hrothgar_Helion_01_05";
                    if (message.NPC.Clan == "Helions" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Hrothgar_Helion_02";
                    if (message.NPC.Clan == "Helions" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Hrothgar_Helion_03";
                    if (message.NPC.Clan == "Helions" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Hrothgar_Helion_04";
                    if (message.NPC.Clan == "Helions" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Hrothgar_Helion_01_05";

                    if (message.NPC.Clan == "The Lost" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Hrothgar_The_Lost_01";
                    if (message.NPC.Clan == "The Lost" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Hrothgar_The_Lost_02";
                    if (message.NPC.Clan == "The Lost" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Hrothgar_The_Lost_03";
                    if (message.NPC.Clan == "The Lost" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Hrothgar_The_Lost_04_05";
                    if (message.NPC.Clan == "The Lost" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Hrothgar_The_Lost_04_05";
                }

                if (message.NPC.Race == "Hyur")
                {
                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Hyur_Highlander_Female_01";
                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Hyur_Highlander_Female_02";
                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Hyur_Highlander_Female_03";
                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Hyur_Highlander_Female_04";
                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Hyur_Highlander_Female_05";
                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 6")
                        return "Hyur_Highlander_Female_06";

                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Hyur_Highlander_Male_01";
                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Hyur_Highlander_Male_02";
                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Hyur_Highlander_Male_03";
                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Hyur_Highlander_Male_04";
                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Hyur_Highlander_Male_05";
                    if (message.NPC.Clan == "Highlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                        return "Hyur_Highlander_Male_06";

                    if (message.NPC.Clan == "Midlander" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Hyur_Midlander_Female_01";
                    if (message.NPC.Clan == "Midlander" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Hyur_Midlander_Female_02";
                    if (message.NPC.Clan == "Midlander" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Hyur_Midlander_Female_03";
                    if (message.NPC.Clan == "Midlander" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Hyur_Midlander_Female_04";
                    if (message.NPC.Clan == "Midlander" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Hyur_Midlander_Female_05";

                    if (message.NPC.Clan == "Midlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Hyur_Midlander_Male_01";
                    if (message.NPC.Clan == "Midlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Hyur_Midlander_Male_02";
                    if (message.NPC.Clan == "Midlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Hyur_Midlander_Male_03";
                    if (message.NPC.Clan == "Midlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Hyur_Midlander_Male_04";
                    if (message.NPC.Clan == "Midlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Hyur_Midlander_Male_05";
                    if (message.NPC.Clan == "Midlander" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                        return "Hyur_Midlander_Male_06";
                }

                if (message.NPC.Race == "Lalafell")
                {
                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Lalafell_Dunesfolk_Female_01";
                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Lalafell_Dunesfolk_Female_02";
                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Lalafell_Dunesfolk_Female_03";
                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Lalafell_Dunesfolk_Female_04";
                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Lalafell_Dunesfolk_Female_05";
                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 6")
                        return "Lalafell_Dunesfolk_Female_06";

                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Lalafell_Dunesfolk_Male_01";
                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Lalafell_Dunesfolk_Male_02";
                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Lalafell_Dunesfolk_Male_03";
                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Lalafell_Dunesfolk_Male_04";
                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Lalafell_Dunesfolk_Male_05";
                    if (message.NPC.Clan == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                        return "Lalafell_Dunesfolk_Male_06";

                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Lalafell_Plainsfolk_Female_01";
                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Lalafell_Plainsfolk_Female_02";
                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Lalafell_Plainsfolk_Female_03";
                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Lalafell_Plainsfolk_Female_04";
                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Lalafell_Plainsfolk_Female_05";
                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 6")
                        return "Lalafell_Plainsfolk_Female_06";

                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Lalafell_Plainsfolk_Male_01";
                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Lalafell_Plainsfolk_Male_02";
                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Lalafell_Plainsfolk_Male_03";
                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Lalafell_Plainsfolk_Male_04";
                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Lalafell_Plainsfolk_Male_05";
                    if (message.NPC.Clan == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                        return "Lalafell_Plainsfolk_Male_06";
                }

                if (message.NPC.Race == "Miqo'te")
                {
                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Miqote_Keeper_of_the_Moon_Female_01";
                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Miqote_Keeper_of_the_Moon_Female_02";
                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Miqote_Keeper_of_the_Moon_Female_03";
                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Miqote_Keeper_of_the_Moon_Female_04";
                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Miqote_Keeper_of_the_Moon_Female_05";
                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 6")
                        return "Miqote_Keeper_of_the_Moon_Female_06";

                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Miqote_Keeper_of_the_Moon_Male_01";
                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Miqote_Keeper_of_the_Moon_Male_02_06";
                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Miqote_Keeper_of_the_Moon_Male_03";
                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Miqote_Keeper_of_the_Moon_Male_04";
                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Miqote_Keeper_of_the_Moon_Male_05";
                    if (message.NPC.Clan == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                        return "Miqote_Keeper_of_the_Moon_Male_02_06";

                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Miqote_Seeker_of_the_Sun_Female_01";
                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Miqote_Seeker_of_the_Sun_Female_02";
                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Miqote_Seeker_of_the_Sun_Female_03";
                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Miqote_Seeker_of_the_Sun_Female_04";
                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Miqote_Seeker_of_the_Sun_Female_05";
                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 6")
                        return "Miqote_Seeker_of_the_Sun_Female_06";

                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Miqote_Seeker_of_the_Sun_Male_01";
                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Miqote_Seeker_of_the_Sun_Male_02";
                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Miqote_Seeker_of_the_Sun_Male_03";
                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Miqote_Seeker_of_the_Sun_Male_04";
                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Miqote_Seeker_of_the_Sun_Male_05";
                    if (message.NPC.Clan == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                        return "Miqote_Seeker_of_the_Sun_Male_06";

                    //if (message.NPC.Clan == "Fat Cat")
                    //    return "Miqote_Fat";
                }

                if (message.NPC.Race == "Roegadyn")
                {
                    if (message.NPC.Clan == "Hellsguard" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Roegadyn_Hellsguard_Female_01";
                    if (message.NPC.Clan == "Hellsguard" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Roegadyn_Hellsguard_Female_02";
                    if (message.NPC.Clan == "Hellsguard" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Roegadyn_Hellsguard_Female_03";
                    if (message.NPC.Clan == "Hellsguard" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Roegadyn_Hellsguard_Female_04";
                    if (message.NPC.Clan == "Hellsguard" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Roegadyn_Hellsguard_Female_05";

                    if (message.NPC.Clan == "Hellsguard" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Roegadyn_Hellsguard_Male_01";
                    if (message.NPC.Clan == "Hellsguard" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Roegadyn_Hellsguard_Male_02";
                    if (message.NPC.Clan == "Hellsguard" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Roegadyn_Hellsguard_Male_03";
                    if (message.NPC.Clan == "Hellsguard" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Roegadyn_Hellsguard_Male_04";
                    if (message.NPC.Clan == "Hellsguard" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Roegadyn_Hellsguard_Male_05";

                    if (message.NPC.Clan == "Sea Wolf" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Roegadyn_Sea_Wolves_Female_01";
                    if (message.NPC.Clan == "Sea Wolf" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Roegadyn_Sea_Wolves_Female_02";
                    if (message.NPC.Clan == "Sea Wolf" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Roegadyn_Sea_Wolves_Female_03";
                    if (message.NPC.Clan == "Sea Wolf" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Roegadyn_Sea_Wolves_Female_04";
                    if (message.NPC.Clan == "Sea Wolf" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Roegadyn_Sea_Wolves_Female_05";

                    if (message.NPC.Clan == "Sea Wolf" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Roegadyn_Sea_Wolves_Male_01";
                    if (message.NPC.Clan == "Sea Wolf" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Roegadyn_Sea_Wolves_Male_02";
                    if (message.NPC.Clan == "Sea Wolf" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Roegadyn_Sea_Wolves_Male_03";
                    if (message.NPC.Clan == "Sea Wolf" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Roegadyn_Sea_Wolves_Male_04";
                    if (message.NPC.Clan == "Sea Wolf" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                        return "Roegadyn_Sea_Wolves_Male_05";
                }

                if (message.NPC.Race == "Viera")
                {
                    if (message.NPC.Clan == "Rava" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Viera_Rava_Female_01_05";
                    if (message.NPC.Clan == "Rava" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Viera_Rava_Female_02";
                    if (message.NPC.Clan == "Rava" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Viera_Rava_Female_03";
                    if (message.NPC.Clan == "Rava" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Viera_Rava_Female_04";
                    if (message.NPC.Clan == "Rava" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Viera_Rava_Female_01_05";

                    if (message.NPC.Clan == "Rava" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                        return "Viera_Rava_Male_01";
                    if (message.NPC.Clan == "Rava" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Viera_Rava_Male_03";
                    if (message.NPC.Clan == "Rava" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                        return "Viera_Rava_Male_04";

                    if (message.NPC.Clan == "Veena" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                        return "Viera_Veena_Female_01_05";
                    if (message.NPC.Clan == "Veena" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                        return "Viera_Veena_Female_02";
                    if (message.NPC.Clan == "Veena" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                        return "Viera_Veena_Female_03";
                    if (message.NPC.Clan == "Veena" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                        return "Viera_Veena_Female_04";
                    if (message.NPC.Clan == "Veena" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                        return "Viera_Veena_Female_01_05";

                    if (message.NPC.Clan == "Veena" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                        return "Viera_Veena_Male_02";
                    if (message.NPC.Clan == "Veena" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                        return "Viera_Veena_Male_03";
                }
            }

            if (message.NPC.BodyType == "Elderly")
            {
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male")
                    return "Elderly_Male_Hyur";

                if (message.NPC.Gender == "Male")
                    return "Elderly_Male";

                if (message.NPC.Gender == "Female")
                    return "Elderly_Female";
            }

            if (message.NPC.BodyType == "Child")
            {
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                    return "Child_Hyur_Female_1";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                    return "Child_Hyur_Female_2";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                    return "Child_Hyur_Female_3_5";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                    return "Child_Hyur_Female_4";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                    return "Child_Hyur_Female_3_5";

                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                    return "Child_Hyur_Male_1";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                    return "Child_Hyur_Male_2";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                    return "Child_Hyur_Male_3_6";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                    return "Child_Hyur_Male_4";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                    return "Child_Hyur_Male_5";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                    return "Child_Hyur_Male_3_6";

                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                    return "Child_Elezen_Female_1_3";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                    return "Child_Elezen_Female_2";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                    return "Child_Elezen_Female_1_3";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                    return "Child_Elezen_Female_4";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                    return "Child_Elezen_Female_5_6";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 6")
                    return "Child_Elezen_Female_5_6";

                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                    return "Child_Elezen_Male_1";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                    return "Child_Elezen_Male_2";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                    return "Child_Elezen_Male_3";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                    return "Child_Elezen_Male_4";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                    return "Child_Elezen_Male_5_6";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                    return "Child_Elezen_Male_5_6";

                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 1")
                    return "Child_Aura_Female_1_5";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                    return "Child_Aura_Female_2";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                    return "Child_Aura_Female_4";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 5")
                    return "Child_Aura_Female_1_5";

                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 1")
                    return "Child_Aura_Male_1";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 2")
                    return "Child_Aura_Male_2";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 3")
                    return "Child_Aura_Male_3";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 4")
                    return "Child_Aura_Male_4";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 5")
                    return "Child_Aura_Male_5_6";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.EyeShape == "Option 6")
                    return "Child_Aura_Male_5_6";

                if (message.NPC.Race == "Miqo'te" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 2")
                    return "Child_Miqote_Female_2";
                if (message.NPC.Race == "Miqo'te" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 3")
                    return "Child_Miqote_Female_3_4";
                if (message.NPC.Race == "Miqo'te" && message.NPC.Gender == "Female" && message.NPC.EyeShape == "Option 4")
                    return "Child_Miqote_Female_3_4";


            }







            // ARR Beast Tribes
            if (message.NPC.Race == "Amalj'aa")
                return "Amaljaa";

            if (message.NPC.Race == "Sylph")
                return "Sylph";

            if (message.NPC.Race == "Kobold")
                return "Kobold";

            if (message.NPC.Race == "Sahagin")
                return "Sahagin";

            if (message.NPC.Race == "Ixal")
                return "Ixal";

            if (message.NPC.Race == "Qiqirn")
                return "Qiqirn";

            // HW Beast Tribes
            if (message.NPC.Race.StartsWith("Dragon"))
            {
                if (this.Database.VoiceNames.TryGetValue(message.Speaker, out string voiceName))
                    return voiceName;
                else
                    return message.NPC.Race;
            }

            if (message.NPC.Race == "Goblin")
            {
                if (message.NPC.Gender == "Female")
                    return "Goblin_Female";
                else
                    return "Goblin_Male";
            }

            if (message.NPC.Race == "Vanu Vanu")
            {
                if (message.NPC.Gender == "Female")
                    return "Vanu_Female";
                else
                    return "Vanu_Male";
            }

            if (message.NPC.Race == "Vath")
                return "Vath";

            if (message.NPC.Race == "Moogle")
                return "Moogle";

            if (message.NPC.Race == "Node")
                return "Node";

            // SB Beast Tribes
            if (message.NPC.Race == "Kojin")
                return "Kojin";

            if (message.NPC.Race == "Ananta")
                return "Ananta";

            if (message.NPC.Race == "Namazu")
                return "Namazu";

            if (message.NPC.Race == "Lupin")
                return "Lupin";

            // Shb Beast Tribes
            if (message.NPC.Race == "Pixie")
                return "Pixie";

            // EW Beast Tribes
            if (message.NPC.Race == "Matanga")
            {
                if (message.NPC.Gender == "Female")
                    return "Matanga_Female";
                else
                    return "Matanga_Male";
            }

            if (message.NPC.Race == "Loporrit")
                return "Loporrit";

            if (message.NPC.Race == "Omicron")
                return "Omicron";

            // Bosses
            if (message.NPC.Race.StartsWith("Boss"))
                return message.NPC.Race;

            PluginLog.Information("Cannot find a voice for " + message.Speaker);
            return "Unknown";
        }

        public void AddToQueue(XivMessage msg)
        {
            PluginLog.Information($"AddToQueue ---> {msg.TtsData.Speaker}: {msg.TtsData.Message}");
            ffxivMessages.Enqueue(msg);
        }
        #endregion


        #region Audio Methods
        public void Speak(XivMessage _msg)
        {
            if (Database.Plugin.Config.FrameworkOnline && Database.Plugin.Config.FrameworkActive)
                this.Database.Framework.Process(_msg);
            else
                Audio.PlayEmptyAudio(_msg);
        }

        /*
        public async Task SpeakPollyAsync(XivMessage msg)
        {
            this.Database.Plugin.Chat.Print("starting polly tts");
            var credentials = new Amazon.Runtime.BasicAWSCredentials(XivPolly.Instance.AccessKey, XivPolly.Instance.SecretKey);
            yield return Polly(msg, credentials);
        }

        public async Task Polly(XivMessage msg, Amazon.Runtime.AWSCredentials credentials)
        {
            // Decide the Gender
            VoiceId voiceId;
            if (msg.TtsData.Gender == "Male")
                voiceId = XivPolly.Instance.PolyMale;
            else if (msg.TtsData.Gender == "Female")
                voiceId = XivPolly.Instance.PolyFemale;
            else
                voiceId = XivPolly.Instance.PolyUngendered;

            this.Database.Plugin.Chat.Print($"Polly: {msg.Speaker}'s Gender from FFXIV is {msg.TtsData.Gender}");
            if (msg.VoiceName != "Unknown" && msg.VoiceName != "Retainer")
                this.Database.Plugin.Chat.Print($"Polly: {msg.Speaker}'s Gender from XIVV is {msg.NPC.Gender}");

            // Fix the Name
            string pattern = "\\b" + "_NAME_" + "\\b";
            string sentence = Regex.Replace(msg.Sentence, pattern, msg.TtsData.User.Split(' ')[0]);

            // Use Lexicon
            foreach (KeyValuePair<string, string> entry in this.Database.Lexicon)
            {
                pattern = "\\b" + entry.Key + "\\b";
                sentence = Regex.Replace(sentence, pattern, entry.Value, RegexOptions.IgnoreCase);
            }

            // Start the Process
            using (var client = new AmazonPollyClient(credentials, XivPolly.Instance.Region))
            {
                var request = new SynthesizeSpeechRequest
                {
                    VoiceId = voiceId,
                    Engine = "neural",
                    OutputFormat = OutputFormat.Ogg_vorbis,
                    SampleRate = "24000",
                    Text = sentence
                };

                SynthesizeSpeechResponse res;
                try
                {
                    res = await client.SynthesizeSpeechAsync(request);
                }
                catch (Exception e)
                {
                    Debug.LogError("Synthesis request failed: " + e.Message);
                    return;
                }

                var memoryStream = new MemoryStream();
                await res.AudioStream.CopyToAsync(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);
                var audioData = memoryStream.ToArray();

                string outputPollyPath = System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "polly.ogg");
                string outputFilePath = System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "current" + this.Database.GenerateRandomSuffix() + ".ogg");
                await File.WriteAllBytesAsync(outputPollyPath, audioData);

                // Effects
                string filterArgs = SoundEffects(msg, true);
                string arguments = $"-i \"{outputPollyPath}\" -filter:a \"{filterArgs}\" -c:a libopus \"{outputFilePath}\"";
                string ffmpegDirectoryPath = Path.Combine(Engine.Instance.Database.DirectoryPath, "Tools");
                FFmpeg.SetExecutablesPath(ffmpegDirectoryPath);

                IConversion conversion = FFmpeg.Conversions.New().AddParameter(arguments);
                await conversion.Start();


                // Read the Opus file
                using (FileStream fileStream = new FileStream(outputFilePath, FileMode.Open, FileAccess.Read))
                {
                    // Initialize the decoder
                    OpusDecoder decoder = new OpusDecoder(48000, 1);
                    OpusOggReadStream oggStream = new OpusOggReadStream(decoder, fileStream);
                    List<float> pcmSamples = new List<float>();
                    while (oggStream.HasNextPacket)
                    {
                        short[] packet = oggStream.DecodeNextPacket();
                        if (packet != null)
                        {
                            foreach (var sample in packet)
                            {
                                pcmSamples.Add(sample / 32768f); // Convert to float and normalize
                            }
                        }
                    }
                    float[] pcmData = pcmSamples.ToArray();

                    AudioClip audioClip = AudioClip.Create("Decoded Opus Clip", pcmData.Length, 1, 48000, false);
                    audioClip.SetData(pcmData, 0);
                    Audio.PlayAudioFromClip(msg, audioClip, messagesContainer);
                    fileStream.Close();

                }

                // Delete the ogg file after reading and decoding it
                try
                {
                    File.Delete(outputPollyPath);
                    File.Delete(outputFilePath);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error deleting temporary files: {ex.Message}");
                }
                //------------------------------------------------------------------------------------


            }
        }
        */

        public async Task SpeakAI(XivMessage msg)
        {
            if (this.ttsEngine == null)
                this.ttsEngine = new TTSEngine(Database.Plugin);

            if (localTTS[0] == null)
                localTTS[0] = TTSVoiceNative.LoadVoiceFromDisk(this.Database.Plugin.Config.LocalTTSMale);
            if (localTTS[1] == null)
                localTTS[1] = TTSVoiceNative.LoadVoiceFromDisk(this.Database.Plugin.Config.LocalTTSFemale);

            try
            {
                int speaker = this.Database.Plugin.Config.LocalTTSUngendered;
                if (msg.TtsData.Gender == "Male")
                    speaker = 0;
                if (msg.TtsData.Gender == "Female")
                    speaker = 1;

                string sentence = Regex.Replace(msg.TtsData.Message, "[“”]", "\"");
                if (msg.Ignored)
                    sentence = ProcessPlayerChat(sentence, msg.Speaker);
                
                sentence = ApplyLexicon(sentence, msg.Speaker);

                var pcmData = await ttsEngine.SpeakTTS(sentence, localTTS[speaker]);
                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(22050, 1);
                var stream = new MemoryStream();
                var writer = new BinaryWriter(stream);

                foreach (var sample in pcmData)
                {
                    writer.Write(sample);
                }

                stream.Position = 0;
                WaveStream waveStream = new RawSourceWaveStream(stream, waveFormat);
                PlayAudio(msg, waveStream, "ai");
            }
            catch (Exception ex)
            {
                PluginLog.LogError($"Error processing audio file: {ex}");
            }
        }

        public static string ProcessPlayerChat(string sentence, string speaker)
        {
            XivEngine.Instance.Database.Plugin.PrintLog(sentence);
            sentence = sentence.Trim();
            string playerName = speaker.Split(" ")[0];
            bool iAmSpeaking = XivEngine.Instance.Database.Plugin.ClientState.LocalPlayer.Name.TextValue == speaker;
            var options = RegexOptions.IgnoreCase;
            var emoticons = new Dictionary<string, string>
            {
                { @"(^|\s)o/($|\s)", "waves and says " },
                { @"(^|\s)(:\)|\^\^|\^[^\s]\^)($|\s)", "smiles and says " },
                { @"(^|\s)(:D|:>)($|\s)", "looks happy and says " },
                { @"(^|\s)(:O|:0)($|\s)", "looks surprised and says " },
                { @"(^|\s)(:\(|:<|:C|>([^\s]+)<)($|\s)", "looks sad and says " },
                { @"\bxD\b", "laughs and says " },
                { @"(^|\s)(:3)($|\s)", "gives a playful smile and says " },
                { @"\bT[^\s]T\b", "cries and says " },
                { @"(^|\s);\)($|\s)", "winks and says " },
            };

            if (iAmSpeaking)
            {
                playerName = "You";
                var keys = new List<string>(emoticons.Keys);
                foreach (var key in keys)
                    emoticons[key] = Regex.Replace(emoticons[key], "s ", " ");
            }

            // Regex: remove links
            sentence = Regex.Replace(sentence, @"https?\S*", "", options);

            // Regex: remove coordinates
            sentence = Regex.Replace(sentence, @"(\ue0bb[^\(]*?)\([^\)]*\)", "$1", options);

            // Check if the player is waving
            if (sentence.Equals("o/"))
            {
                if(iAmSpeaking)
                    return playerName + " wave.";
                else
                    return playerName + " is waving.";
            }

            // Check other emotions
            foreach (var emoticon in emoticons)
            {
                if (Regex.IsMatch(sentence, emoticon.Key, options))
                {
                    sentence = Regex.Replace(sentence, emoticon.Key, " ", options).Trim();
                    sentence = playerName + " " + emoticon.Value + sentence;
                    break;
                }
            }
                


            // Replace "min" following numbers with "minutes", ensuring proper pluralization
            sentence = Regex.Replace(sentence, @"(\b\d+)\s*min\b", m =>
            {
                return int.Parse(m.Groups[1].Value) == 1 ? $"{m.Groups[1].Value} minute" : $"{m.Groups[1].Value} minutes";
            }, options);

            // Clean "and says" at the end of the sentence
            string pattern = @"\s*and says\s*$";
            if (iAmSpeaking)
                pattern = @"\s*and say\s*$";
            sentence = Regex.Replace(sentence, pattern, "", options);

            // Regex: replacements
            sentence = Regex.Replace(sentence, @"\bggty\b", "good game, thank you", options);
            sentence = Regex.Replace(sentence, @"\btyfp\b", "thank you for the party!", options);
            sentence = Regex.Replace(sentence, @"\btyvm\b", "thank you very much", options);
            sentence = Regex.Replace(sentence, @"\bty\b", "thank you", options);
            sentence = Regex.Replace(sentence, @"\brp\b", "role play", options);
            sentence = Regex.Replace(sentence, @"\bo7\b", "salute", options);
            sentence = Regex.Replace(sentence, @"\bafk\b", "away from keyboard", options);
            sentence = Regex.Replace(sentence, @"\bbrb\b", "be right back", options);
            sentence = Regex.Replace(sentence, @"\bprog\b", "progress", options);
            sentence = Regex.Replace(sentence, @"\bcomms\b", "commendations", options);
            sentence = Regex.Replace(sentence, @"\bcomm\b", "commendation", options);
            sentence = Regex.Replace(sentence, @"\blq\b", "low quality", options);
            sentence = Regex.Replace(sentence, @"\bhq\b", "high quality", options);
            sentence = Regex.Replace(sentence, @"\bfl\b", "friend list", options);
            sentence = Regex.Replace(sentence, @"\bfc\b", "free company", options);
            sentence = Regex.Replace(sentence, @"\bdot\b", "damage over time", options);
            sentence = Regex.Replace(sentence, @"\bcrit\b", "critical hit", options);
            sentence = Regex.Replace(sentence, @"\blol\b", "\"L-O-L\"", options);
            sentence = Regex.Replace(sentence, @"\blmao\b", "\"Lah-mao\"", options);
            sentence = Regex.Replace(sentence, @"\bgg\b", "good game", options);
            sentence = Regex.Replace(sentence, @"\bgl\b", "good luck", options);
            sentence = Regex.Replace(sentence, @"\bsry\b", "sorry", options);
            sentence = Regex.Replace(sentence, @"\bsrry\b", "sorry", options);
            sentence = Regex.Replace(sentence, @"\bcs\b", "cutscene", options);
            sentence = Regex.Replace(sentence, @"\bttyl\b", "talk to you later", options);
            sentence = Regex.Replace(sentence, @"\boki\b", "okay", options);
            sentence = Regex.Replace(sentence, @"\bggs\b", "good game", options);
            sentence = Regex.Replace(sentence, @"\bgn\b", "good night", options);
            sentence = Regex.Replace(sentence, @"\bnn\b", "ight night", options);
            sentence = Regex.Replace(sentence, @"\bdd\b", "damage dealer", options);
            sentence = Regex.Replace(sentence, @"\bbis\b", "best in slot", options);
            sentence = Regex.Replace(sentence, @"(?<=\s|^):\)(?=\s|$)", "smile", options);
            sentence = Regex.Replace(sentence, @"(?<=\s|^):\((?=\s|$)", "sadge", options);
            sentence = Regex.Replace(sentence, @"\b<3\b", "heart", options);
            sentence = Regex.Replace(sentence, @"\bucob\b", "ultimate coils of bahamut", options);
            sentence = Regex.Replace(sentence, @"\bIT\b", "it");

            XivEngine.Instance.Database.Plugin.PrintLog(sentence);
            // Regex: Job Abbreviations
            sentence = JobReplacement(sentence);
            return sentence;
        }

        public static string JobReplacement(string sentence)
        {
            var jobReplacementsCaseSensitive = new Dictionary<string, string>
            {
                { "WAR", "Warrior" },
                { "ARC", "Archer" },
                { "SAM", "Samurai" }
            };

            var jobReplacementsCaseInsensitive = new Dictionary<string, string>
            {
                { "CRP", "Carpenter" },
                { "BSM", "Blacksmith" },
                { "ARM", "Armorer" },
                { "GSM", "Goldsmith" },
                { "LTW", "Leatherworker" },
                { "WVR", "Weaver" },
                { "ALC", "Alchemist" },
                { "CUL", "Culinarian" },
                { "MIN", "Miner" },
                { "BTN", "Botanist" },
                { "FSH", "Fisher" },
                { "GLA", "Gladiator" },
                { "PGL", "Pugilist" },
                { "MRD", "Marauder" },
                { "LNC", "Lancer" },
                { "ROG", "Rogue" },
                { "CNJ", "Conjurer" },
                { "THM", "Thaumaturge" },
                { "ACN", "Arcanist" },
                { "PLD", "Paladin" },
                { "DRK", "Dark Knight" },
                { "GNB", "Gunbreaker" },
                { "RPR", "Reaper" },
                { "MNK", "Monk" },
                { "DRG", "Dragoon" },
                { "NIN", "Ninja" },
                { "WHM", "White Mage" },
                { "SCH", "Scholar" },
                { "AST", "Astrologian" },
                { "SGE", "Sage" },
                { "BRD", "Bard" },
                { "MCH", "Machinist" },
                { "DNC", "Dancer" },
                { "BLM", "Black Mage" },
                { "SMN", "Summoner" },
                { "RDM", "Red Mage" },
                { "BLU", "Blue Mage" }
            };

            // Apply case-insensitive replacements for most job abbreviations
            foreach (var job in jobReplacementsCaseInsensitive)
            {
                sentence = Regex.Replace(sentence, $@"\b{job.Key}\b", job.Value, RegexOptions.IgnoreCase);
            }

            // Apply case-sensitive replacements for "WAR," "ARC," and "SAM"
            foreach (var job in jobReplacementsCaseSensitive)
            {
                sentence = Regex.Replace(sentence, $@"\b{job.Key}\b", job.Value);
            }

            return sentence;
        }

        public static string ApplyLexicon(string sentence, string speaker)
        {
            // Use Lexicon
            string cleanedMessage = sentence;
            foreach (KeyValuePair<string, string> entry in XivEngine.Instance.Database.Lexicon)
            {
                string pattern = "\\b" + entry.Key + "\\b";
                cleanedMessage = Regex.Replace(cleanedMessage, pattern, entry.Value, RegexOptions.IgnoreCase);
            }
            return cleanedMessage;
        }

        public async Task SpeakLocallyAsync(XivMessage msg, bool isMp3 = false)
        {
            await speakBlock.WaitAsync();
            PluginLog.Information($"SpeakLocallyAsync ---> {msg.TtsData.Speaker}: {msg.TtsData.Message}");

            if (isMp3)
            {
                File.Delete(msg.FilePath + ".ogg");
                string arguments = $"-i \"{msg.FilePath+".mp3"}\" -c:a libopus \"{msg.FilePath +".ogg"}\"";
                string ffmpegDirectoryPath = Path.Combine(XivEngine.Instance.Database.ToolsPath); ;
                FFmpeg.SetExecutablesPath(ffmpegDirectoryPath);
                IConversion conversion = FFmpeg.Conversions.New().AddParameter(arguments);
                await conversion.Start();
                File.Delete(msg.FilePath + ".mp3");
                msg.FilePath = msg.FilePath + ".ogg";
            }

            if (msg.FilePath.EndsWith(".ogg"))
            {
                PluginLog.Information($"SpeakLocallyAsync: found ogg path: {msg.FilePath}");
                WaveStream waveStream = null;

                // Check for audio speed adjustment or special effects
                bool changeSpeed = this.Database.Plugin.Config.Speed != 100;
                bool applyEffects = SoundEffects(msg) != "";


                try
                {
                    // Load and possibly modify the OGG file
                    if (changeSpeed || applyEffects)
                        waveStream = await FFmpegFileToWaveStream(msg);
                    else
                        waveStream = DecodeOggOpusToPCM(msg.FilePath);
                    PlayAudio(msg,waveStream, "xivv");
                }
                catch (Exception ex)
                {
                    PluginLog.LogError($"Error processing audio file: {ex.Message}");
                }
            }
            else
            {
                // Handling for other audio formats like WAV
                try
                {
                    using (var audioFile = new AudioFileReader("file:" + msg.FilePath))
                        PlayAudio(msg,audioFile, "xivv");
                }
                catch (Exception ex)
                {
                    PluginLog.LogError($"Error loading audio file: {ex.Message}");
                }
            }

            speakBlock.Release();
        }

        public static WaveStream DecodeOggOpusToPCM(string filePath)
        {
            // Read the Opus file
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                // Initialize the decoder
                OpusDecoder decoder = new OpusDecoder(48000, 1); // Assuming a sample rate of 48000 Hz and mono audio
                OpusOggReadStream oggStream = new OpusOggReadStream(decoder, fileStream);

                // Buffer for storing the decoded samples
                List<float> pcmSamples = new List<float>();

                // Read and decode the entire file
                while (oggStream.HasNextPacket)
                {
                    short[] packet = oggStream.DecodeNextPacket();
                    if (packet != null)
                    {
                        foreach (var sample in packet)
                        {
                            pcmSamples.Add(sample / 32768f); // Convert to float and normalize
                        }
                    }
                }

                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
                var stream = new MemoryStream();
                var writer = new BinaryWriter(stream);
                foreach (var sample in pcmSamples.ToArray())
                {
                    writer.Write(sample);
                }
                stream.Position = 0;
                return new RawSourceWaveStream(stream, waveFormat);
            }
        }

        static bool changeTimeBusy = false;
        public static async Task<WaveStream> FFmpegFileToWaveStream(XivMessage msg)
        {
            changeTimeBusy = true;
            string outputFilePath = System.IO.Path.Combine(XivEngine.Instance.Database.DirectoryPath, "current" + XivEngine.Instance.Database.GenerateRandomSuffix() + ".ogg");

            string filterArgs = SoundEffects(msg);
            string arguments = $"-i \"{msg.FilePath}\" -filter:a \"{filterArgs}\" -c:a libopus \"{outputFilePath}\"";

            string ffmpegDirectoryPath = Path.Combine(XivEngine.Instance.Database.ToolsPath); ;
            FFmpeg.SetExecutablesPath(ffmpegDirectoryPath);

            IConversion conversion = FFmpeg.Conversions.New().AddParameter(arguments);
            await conversion.Start();

            // Read the Opus file
            using (FileStream fileStream = new FileStream(outputFilePath, FileMode.Open, FileAccess.Read))
            {
                // Initialize the decoder
                OpusDecoder decoder = new OpusDecoder(48000, 1); // Assuming a sample rate of 48000 Hz and mono audio
                OpusOggReadStream oggStream = new OpusOggReadStream(decoder, fileStream);

                // Buffer for storing the decoded samples
                List<float> pcmSamples = new List<float>();

                // Read and decode the entire file
                while (oggStream.HasNextPacket)
                {
                    short[] packet = oggStream.DecodeNextPacket();
                    if (packet != null)
                    {
                        foreach (var sample in packet)
                        {
                            pcmSamples.Add(sample / 32768f); // Convert to float and normalize
                        }
                    }
                }

                fileStream.Close();

                // Delete the ogg file after reading and decoding it
                try
                {
                    File.Delete(outputFilePath);
                }
                catch (Exception ex)
                {
                    PluginLog.LogError($"Error deleting temporary file: {ex.Message}");
                    // Handle the error, log it, or inform the user as necessary
                }

                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
                var stream = new MemoryStream();
                var writer = new BinaryWriter(stream);
                foreach (var sample in pcmSamples.ToArray())
                {
                    writer.Write(sample);
                }
                stream.Position = 0;

                changeTimeBusy = false;
                return new RawSourceWaveStream(stream, waveFormat);
            }
        }

        static string SoundEffects(XivMessage msg, bool polly = false)
        {
            bool changeSpeed = false;
            string additionalChanges = "";
            if (XivEngine.Instance.Database.Plugin.Config.Speed != 100) changeSpeed = true;
            if (msg.VoiceName == "Omicron" || msg.VoiceName == "Node") additionalChanges = "robot";

            string filterArgs = "";

            if (polly)
                filterArgs = "\"volume=6dB\"";

            /* determine a pitch based on string msg.Speaker
            {
                int hash = msg.Speaker == "Bubble" ? msg.Sentence.GetHashCode() : msg.Speaker.GetHashCode();
                float normalized = (hash & 0x7FFFFFFF) / (float)Int32.MaxValue;
                float pitch = (normalized - 0.5f) * 0.5f;
                pitch = (float)Math.Round(pitch * 10) / 50;
                float setRate = 44100 * (1 + pitch);
                float tempo = 1.0f / (1 + pitch);
                XivEngine.Instance.Database.Plugin.Chat.Print($"Pitch for {msg.Speaker} is {pitch}");
                XivEngine.Instance.Database.Plugin.Chat.Print($"\"atempo={tempo},asetrate={setRate}\"");
                if (pitch != 0)
                {
                    if (filterArgs != "") filterArgs += ",";
                    filterArgs += $"\"atempo={tempo},asetrate={setRate}\"";
                }
            }
            //*/

            if (changeSpeed)
            {
                if (filterArgs != "") filterArgs += ",";
                filterArgs += $"\"atempo={(XivEngine.Instance.Database.Plugin.Config.Speed/100f).ToString(CultureInfo.InvariantCulture)}\"";
            }

            if (additionalChanges == "robot")
            {
                if (filterArgs != "") filterArgs += ",";
                filterArgs += $"\"flanger=depth=10:delay=15,volume=15dB,aphaser=in_gain=0.4\"";
            }

            return filterArgs;
        }

        private void PlayAudio(XivMessage xivMessage, WaveStream waveStream, string type)
        {
            if (xivMessage.TtsData != null && xivMessage.TtsData.Position != new Vector3(-99))
            {
                Audio.PlayBubble(xivMessage, waveStream, type);
            }
            else
            {
                Audio.PlayAudio(xivMessage, waveStream, type);
            }
        }
        #endregion

        #region Framework
        public void RedoAudio(XivMessage xivMessage)
        {
            XivEngine.Instance.Database.Plugin.PrintLog("ArcFramework: RedoAudio");
            if (xivMessage.VoiceName == "Unknown")
            {
                xivMessage = XivEngine.Instance.CleanXivMessage(xivMessage);
                if (xivMessage.Speaker == "???")
                    xivMessage = XivEngine.Instance.Database.GetNameless(xivMessage);
                xivMessage = XivEngine.Instance.UpdateXivMessage(xivMessage);
                XivEngine.Instance.Speak(xivMessage);
            }
            else
                this.Database.Framework.Process(xivMessage);
        }

        public void UnknownList_Load()
        {
            string directoryPath = Database.DirectoryPath + "/Unknown";
            Audio.unknownQueue.Clear();
            foreach (var directory in System.IO.Directory.GetDirectories(directoryPath))
            {
                // Get all files in the directory
                string[] files = System.IO.Directory.GetFiles(directory);

                if (files.Length == 0)
                    continue;

                Audio.unknownQueue.Enqueue(directory);
            }
        }
        #endregion


        #region Reports
        public void ReportUnprocessable(XivMessage msg)
        {
            if (!this.Database.Plugin.Config.Reports) return;
            PluginLog.Information("ReportUnprocessable");
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;
            reports.Enqueue(new ReportXivMessage(msg, "/Unprocessable/", ""));
        }

        public void ReportError(XivMessage msg)
        {
            if (!this.Database.Plugin.Config.Reports) return;
            PluginLog.Information("ReportError");
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;
            reports.Enqueue(new ReportXivMessage(msg, "/Error/", ""));
        }

        Queue<ReportXivMessage> reports = new Queue<ReportXivMessage>();
        public void ReportUnknown(XivMessage msg)
        {
            if (!this.Database.Plugin.Config.Reports) return;
            PluginLog.Information("ReportUnknown");
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;
            reports.Enqueue(new ReportXivMessage(msg, "unknown", ""));
        }

        public void ReportDifferent(XivMessage msg)
        {
            if (!this.Database.Plugin.Config.Reports) return;
            PluginLog.Information("ReportDifferent");
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;
            reports.Enqueue(new ReportXivMessage(msg, "different", ""));
        }

        public void ReportMuteToArc(XivMessage msg, string input)
        {
            if (!this.Database.Plugin.Config.Reports) return;
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;
            PluginLog.Information($"Reporting line: \"{msg.Sentence}\"");
            this.Database.Plugin.Print($"Reporting line: \"{msg.Sentence}\"");
            reports.Enqueue(new ReportXivMessage(msg, "mute", input));
        }

        public void ReportRedoToArc(XivMessage msg, string input)
        {
            if (!this.Database.Plugin.Config.Reports) return;
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;
            PluginLog.Information($"Reporting line: \"{msg.Sentence}\"");
            this.Database.Plugin.Print($"Reporting line: \"{msg.Sentence}\"");
            reports.Enqueue(new ReportXivMessage(msg, "redo", input));
        }


        public void ReportToArc(XivMessage msg)
        {
            if (!this.Database.Plugin.Config.Reports) return;
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;
            PluginLog.Information($"Reporting line: \"{msg.Sentence}\"");
            this.Database.Plugin.Print($"Reporting line: \"{msg.Sentence}\"");
            reports.Enqueue(new ReportXivMessage(msg, "missing", ""));
        }

        bool reportToArcJSONBusy = false;
        public async Task ReportToArcJSON(XivMessage xivMessage, string folder, string comment)
        {
            if (!this.Database.Plugin.Config.Reports) return;
            string[] fullname = Database.Plugin.ClientState.LocalPlayer.Name.TextValue.Split(" ");
            xivMessage.Sentence = xivMessage.TtsData.Message;
            xivMessage.Sentence = xivMessage.Sentence.Replace(fullname[0], "_FIRSTNAME_");
            if (fullname.Length > 1)
            {
                xivMessage.Sentence = xivMessage.Sentence.Replace(fullname[1], "_LASTNAME_");
            }


            while (reportToArcJSONBusy && Database.Data["voices"] != "0")
                await Task.Delay(500);

            reportToArcJSONBusy = true;
            ReportData reportData = new ReportData
            {
                speaker = xivMessage.Speaker,
                sentence = xivMessage.Sentence,
                npcid = xivMessage.NpcId,
                skeletonid = xivMessage.TtsData.SkeletonID,
                body = xivMessage.TtsData.Body,
                gender = xivMessage.TtsData.Gender,
                race = xivMessage.TtsData.Race,
                tribe = xivMessage.TtsData.Tribe,
                eyes = xivMessage.TtsData.Eyes,
                folder = folder,
                user = xivMessage.TtsData.User,
                comment = comment
            };

            string jsonContent = JsonConvert.SerializeObject(reportData);
            string url = "https://arcsidian.com/report_to_arc.php";

            using (HttpClient client = new HttpClient())
            {
                HttpContent content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                try
                {
                    HttpResponseMessage response = await client.PostAsync(url, content);

                    if (response.IsSuccessStatusCode)
                        PluginLog.Information("Report uploaded successfully.");
                    else
                        PluginLog.Error($"Error uploading Report: {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Exception when uploading Report: {ex.Message}");
                }
            }

            reportToArcJSONBusy = false;
            return;
        }
        #endregion

    }


}
