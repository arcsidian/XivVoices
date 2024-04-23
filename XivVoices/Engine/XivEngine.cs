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
using Dalamud.Plugin.Services;
//using Amazon.Polly;
//using Amazon.Polly.Model;

namespace XivVoices.Engine
{

    public class XivEngine
    {
        #region Private Parameters
        private Timer _timer;
        private bool speakLocallyIsBusy = false;
        private DataMapper mapper;
        private Queue<XivMessage> ffxivMessages = new Queue<XivMessage>();
        private TTSEngine ttsEngine;
        TTSVoiceNative[] localTTS = new TTSVoiceNative[2];
        #endregion


        #region Public Parameters
        private bool Active { get; set; } = false;
        public Configuration Configuration { get; set; }
        public Database Database { get; set; }
        public Audio Audio { get; set; }
        public bool OnlineTTS { get; set; } = false;

        public List<string> IgnoredDialogues = new List<string>();
        public bool UnknownProcessRunning { get; set; } = false;
        //public Queue<NotificationData> notifications = new Queue<NotificationData>();
        #endregion


        #region Core Methods
        public static XivEngine Instance;

        public XivEngine(Configuration _configuration, Database _database, Audio _audio)
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
                return;

            this.Configuration = _configuration;
            this.Database = _database;
            this.Audio = _audio;
            this.ttsEngine = null;
            //AiVoicesEnabled = PlayerPrefs.GetInt("aiVoicesEnabled", 1) == 1;
            //RetainersEnabled = PlayerPrefs.GetInt("retainersEnabled", 1) == 1;
            mapper = new DataMapper();
            _timer = new Timer(Update, null, 0, 50);
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
                    if (msg.Network == "Online")
                    {
                        //this.Database.Plugin.Chat.Print("CheckMessages: Online");
                        //if (Configuration.PollyEnabled && !Configuration.WebsocketRedirectionEnabled && (msg.Reported || msg.Ignored)) // && !AudioIsMuted?
                        //    Task.Run(async () => await SpeakPollyAsync(msg));
                        if (Configuration.LocalTTSEnabled && !Configuration.WebsocketRedirectionEnabled && (msg.Reported || msg.Ignored)) // !&& AudioIsMuted?
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
                if (report.message.TtsData.User != "Arc Xiv@Twintania")
                    Task.Run(async () => await ReportToArcJSON(report.message, report.folder, report.comment));
            }
        }

        public void Dispose()
        {
            StopTTS();
            _timer?.Dispose();
            _timer = null;
            Active = false;
        }
        #endregion


        #region Processing Methods
        public void Process(string type, string speaker, string npcID, string message, string body, string gender, string race, string tribe, string eyes, string language, Vector3 position, Character character, string user)
        {
            TTSData ttsData = new TTSData(type, speaker, npcID, message, body, gender, race, tribe, eyes, language, position, character, user);
            XivMessage msg = new XivMessage(ttsData);
            msg.Ignored = false;

            if (ttsData.Type != "Cancel" && ttsData.Language != "English")
                return;

            if (ttsData.Type == "Cancel")
            {
                Audio.StopAudio();
                return;
            }

            if (ttsData.Type != "Dialogue" && ttsData.Type != "Bubble" && ttsData.Type != "NPCDialogueAnnouncements")
            {
                PluginLog.Information("[Ignored] " + ttsData.Speaker + ":" + ttsData.Message);
                msg.Ignored = true;
            }

            msg.TtsData.Body = mapper.GetBody(int.Parse(msg.TtsData.Body));
            msg.TtsData.Race = mapper.GetRace(int.Parse(msg.TtsData.Race));
            msg.TtsData.Tribe = mapper.GetTribe(int.Parse(msg.TtsData.Tribe));
            msg.TtsData.Eyes = mapper.GetEyes(int.Parse(msg.TtsData.Eyes));
            if (msg.TtsData.Body == "Beastman")
            {
                PluginLog.Information("Race before Mapper: " + msg.TtsData.Race);
                msg.TtsData.Race = mapper.GetSkeleton(int.Parse(msg.TtsData.NpcID));
                PluginLog.Information("Race after Mapper: " + msg.TtsData.Race);
            }


            string[] fullname = Database.Plugin.ClientState.LocalPlayer.Name.TextValue.Split(" ");
            var results = GetPossibleSentences(message, fullname[0], fullname[1]);
            bool sentenceFound = false;
            foreach (var result in results)
            {
                msg.Sentence = result;
                msg = CleanXivMessage(msg);
                if (msg.Speaker == "???")
                    msg = this.Database.GetNameless(msg);
                msg = UpdateXivMessage(msg);
                if(msg.FilePath != null)
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


            if (msg.VoiceName == "Retainer" && !Configuration.RetainersEnabled) return;
            if (IgnoredDialogues.Contains(msg.Speaker + msg.Sentence) || this.Database.Ignored.Contains(msg.Speaker)) return;

            PluginLog.Information($"NPC Data From FFXIV: [Gender]:{msg.TtsData.Gender}, [Body]:{msg.TtsData.Body}, [Race]:{msg.TtsData.Race}, [Tribe]:{msg.TtsData.Tribe}, [Eyes]:{msg.TtsData.Eyes}");
            if (msg.NPC == null)
                PluginLog.Information("npc is null, voice name is " + msg.VoiceName);

            AddToQueue(msg);

        }

        public static List<string> GetPossibleSentences(string sentence, string firstName, string lastName)
        {
            var replacements = new Dictionary<string, string>
            {
                { firstName, "_FIRSTNAME_" },
                { lastName, "_LASTNAME_" }
            };

            List<string> results = new List<string>();
            RecurseCombinations(sentence, replacements, new List<string> { firstName, lastName }, 0, results);
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
            string pattern = $@"\b{Regex.Escape(name)}\b";
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

        public void EnableOnlineTTS()
        {
            OnlineTTS = true;
        }

        public void DisableOnlineTTS()
        {
            OnlineTTS = false;
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

            // 2- Delivery Moogle carrier level removal
            pattern = @"Your postal prowess has earned you carrier level \d{2}";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "Your postal prowess has earned you this carrier level");

            // =====================================================

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

        public XivMessage UpdateXivMessage(XivMessage xivMessage, bool retainerRun = false)
        {
            if (xivMessage.Ignored)
            {
                xivMessage.VoiceName = "Unknown";
                xivMessage.Network = "Online";
                return xivMessage;
            }

            // Changing 2-letter names because fuck windows defender
            if (xivMessage.Speaker.Length == 2)
                xivMessage.Speaker = "FUDefender_" + xivMessage.Speaker;

            bool fetchedByID = false;
            xivMessage.NPC = this.Database.GetNPC(xivMessage.Speaker, xivMessage.NpcId, xivMessage.TtsData, ref fetchedByID);
            if (xivMessage.NPC != null)
            {
                //this.Database.Plugin.Chat.Print(xivMessage.Speaker + " has been found in the DB");
                //notifications.Enqueue(new NotificationData(xivMessage.Speaker + " has been found in the DB", false));
                xivMessage.VoiceName = GetVoiceName(xivMessage, fetchedByID);
            }
            else
            {
                PluginLog.Information(xivMessage.Speaker + " does not exist in the DB");
                xivMessage.VoiceName = "Unknown";
            }

            if (xivMessage.VoiceName == "Unknown" && xivMessage.Speaker != "???")
            {
                // Check if it belongs to a retainer
                if (!retainerRun)
                {
                    xivMessage = this.Database.GetRetainer(xivMessage);
                    if (xivMessage.VoiceName == "Retainer")
                    {
                        if (Configuration.RetainersEnabled)
                            return UpdateXivMessage(xivMessage, true);
                        else
                            return xivMessage;
                    }
                }

                // If not then report it as unknown
                if (!xivMessage.Reported)
                {
                    ReportUnknown(xivMessage);
                    xivMessage.Reported = true;
                }
            }


            xivMessage.FilePath = this.Database.VoiceDataExists(xivMessage.VoiceName.ToString(), xivMessage.Speaker, xivMessage.Sentence);
            if (xivMessage.FilePath.IsNullOrEmpty())
            {
                //notifications.Enqueue(new NotificationData("Voice file does not exist.", false));
                xivMessage.Network = "Online";
            }
            else if (xivMessage.FilePath == "report")
            {
                xivMessage.Network = "Online";
                if (!this.Database.Framework.Active)
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
            if (!fetchedByID && this.Database.VoiceNames.TryGetValue(message.Speaker, out string voiceName))
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
            if (message.NPC.Race == "Dragon")
            {
                if (this.Database.VoiceNames.TryGetValue(message.Speaker, out string voiceName))
                    return voiceName;
                else
                    return "Dragon";
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
            ffxivMessages.Enqueue(msg);
        }
        #endregion


        #region Audio Methods
        public void Speak(XivMessage _msg)
        {
            if (OnlineTTS && this.Database.Framework.Active)
                this.Database.Framework.Process(_msg);
            else
                Audio.PlayEmptyAudio(_msg, "empty");
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
                localTTS[0] = TTSVoiceNative.LoadVoiceFromDisk("en-gb-northern_english_male-medium");
            if (localTTS[1] == null)
                localTTS[1] = TTSVoiceNative.LoadVoiceFromDisk("en-gb-jenny_dioco-medium");

            try
            {
                int speaker = 0;
                if (msg.TtsData.Gender == "Female")
                    speaker = 1;

                var pcmData = await ttsEngine.SpeakTTS(msg.TtsData.Message, localTTS[speaker]);
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



        public async Task SpeakLocallyAsync(XivMessage msg)
        {
            while (speakLocallyIsBusy)
                await Task.Delay(50);
            speakLocallyIsBusy = true;

            if (msg.FilePath.EndsWith(".ogg"))
            {
                PluginLog.Information($"SpeakLocallyAsync: found ogg path: {msg.FilePath}");
                WaveStream waveStream = null;

                // Check for audio speed adjustment or special effects
                bool changeSpeed = Configuration.Speed != 100;
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

            speakLocallyIsBusy = false;
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
            if (XivEngine.Instance.Configuration.Speed != 100) changeSpeed = true;
            if (msg.VoiceName == "Omicron" || msg.VoiceName == "Node") additionalChanges = "robot";

            string filterArgs = "";

            if (polly)
                filterArgs = "\"volume=6dB\"";

            if (changeSpeed)
            {
                if (filterArgs != "") filterArgs += ",";
                filterArgs += $"\"atempo={(XivEngine.Instance.Configuration.Speed/100f).ToString(CultureInfo.InvariantCulture)}\"";
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


        #region Reports
        public void ReportUnprocessable(XivMessage msg)
        {
            PluginLog.Information("ReportUnprocessable");
            if (Database.Ignored.Contains(msg.Speaker) || Database.Framework.Active) return;
            reports.Enqueue(new ReportXivMessage(msg, "/Unprocessable/", ""));
        }

        public void ReportError(XivMessage msg)
        {
            PluginLog.Information("ReportError");
            if (Database.Ignored.Contains(msg.Speaker) || Database.Framework.Active) return;
            reports.Enqueue(new ReportXivMessage(msg, "/Error/", ""));
        }

        Queue<ReportXivMessage> reports = new Queue<ReportXivMessage>();
        public void ReportUnknown(XivMessage msg)
        {
            PluginLog.Information("ReportUnknown");
            if (Database.Ignored.Contains(msg.Speaker) || Database.Framework.Active) return;
            reports.Enqueue(new ReportXivMessage(msg, "unknown", ""));
        }

        public void ReportDifferent(XivMessage msg)
        {
            PluginLog.Information("ReportDifferent");
            if (Database.Ignored.Contains(msg.Speaker) || Database.Framework.Active) return;
            reports.Enqueue(new ReportXivMessage(msg, "different", ""));
        }

        public void ReportMuteToArc(XivMessage msg, string input)
        {
            if (Database.Ignored.Contains(msg.Speaker) || Database.Framework.Active) return;
            PluginLog.Information($"Reporting line: \"{msg.Sentence}\"");
            this.Database.Plugin.Chat.Print($"Reporting line: \"{msg.Sentence}\"");
            reports.Enqueue(new ReportXivMessage(msg, "mute", input));
        }

        public void ReportRedoToArc(XivMessage msg, string input)
        {
            if (Database.Ignored.Contains(msg.Speaker) || Database.Framework.Active) return;
            PluginLog.Information($"Reporting line: \"{msg.Sentence}\"");
            this.Database.Plugin.Chat.Print($"Reporting line: \"{msg.Sentence}\"");
            reports.Enqueue(new ReportXivMessage(msg, "redo", input));
        }


        public void ReportToArc(XivMessage msg)
        {
            if (Database.Ignored.Contains(msg.Speaker) || Database.Framework.Active) return;
            PluginLog.Information($"Reporting line: \"{msg.Sentence}\"");
            this.Database.Plugin.Chat.Print($"Reporting line: \"{msg.Sentence}\"");
            reports.Enqueue(new ReportXivMessage(msg, "missing", ""));
        }

        bool reportToArcJSONBusy = false;
        public async Task ReportToArcJSON(XivMessage xivMessage, string folder, string comment)
        {
            while (reportToArcJSONBusy && Database.Data["voices"] != "0")
                await Task.Delay(500);

            reportToArcJSONBusy = true;
            ReportData reportData = new ReportData
            {
                speaker = xivMessage.Speaker,
                sentence = xivMessage.TtsData.Message,
                npcid = xivMessage.NpcId,
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
            //Notification(xivMessage.Speaker + "'s missing dialogue reported to Arc", true, "[Reported] " + xivMessage.Speaker + ": " + xivMessage.Sentence);
            return;
        }
        #endregion

    }


}
