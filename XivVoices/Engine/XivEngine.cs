using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Concentus.Structs;
using Concentus.Oggfile;
using Xabe.FFmpeg;
using Newtonsoft.Json;
using Dalamud.Utility;
using NAudio.Wave;
using System.Threading;
using System.Net.Http;
using Dalamud.Game.ClientState.Objects.Types;
using System.Numerics;
using XivVoices.LocalTTS;
using System.Linq;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Security.Policy;

namespace XivVoices.Engine
{

    public class XivEngine
    {
        #region Private Parameters
        private string currentVersion { get; set; }

        bool CheckingForNewVersion { get; set; } = false;
        private SemaphoreSlim speakBlock { get; set; }
        private SemaphoreSlim reportBlock { get; set; }
        private Timer _updateTimer;
        private Timer _autoUpdateTimer;

        private Queue<XivMessage> ffxivMessages = new Queue<XivMessage>();
        private Queue<string> reportedLines = new Queue<string>();
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

            currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            speakBlock = new SemaphoreSlim(1, 1);
            reportBlock = new SemaphoreSlim(1, 1);
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
            Plugin.PluginLog.Information($"Version[{currentVersion}]");

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
            if (!Active || !this.Database.Plugin.Config.Active) return;

            if (ffxivMessages.Count > 0)
            {
                if (ffxivMessages.TryDequeue(out XivMessage msg))
                {
                    Plugin.PluginLog.Information($"Update ---> {msg.TtsData.Speaker}: {msg.TtsData.Message}");
                    if (msg.Network == "Online")
                    {
                        if (this.Database.Plugin.Config.LocalTTSEnabled && !this.Database.Plugin.Config.WebsocketRedirectionEnabled && (msg.Reported || msg.Ignored)) // !&& AudioIsMuted?
                            Task.Run(async () => await SpeakAI(msg));
                        else
                            Speak(msg);
                    }
                    else
                    {
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
            if (!Active || !this.Database.Plugin.Config.Active) return;
            if (!this.Database.Plugin.Config.Initialized) return;

            // Failed Reports Check
            // TODO: if this.Database.ReportsPath folder is empty, ignore, if it's not empty, for each file, call LateReportToArcJSON() in a Task.Run
            // LateReportToArcJSON should take two parameters, first parameter is the contents of the file, second parameter is the path of the file
            if (Directory.Exists(this.Database.ReportsPath))
            {
                string[] reportFiles = Directory.GetFiles(this.Database.ReportsPath);
                if (reportFiles.Length > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        foreach (string filePath in reportFiles)
                        {
                            try
                            {
                                string url = await File.ReadAllTextAsync(filePath);
                                await LateReportToArcJSON(url, filePath);

                            }
                            catch (Exception ex)
                            {
                                XivEngine.Instance.Database.Plugin.PrintError($"Failed to process report file {filePath}: {ex.Message}");
                            }
                        }
                    });
                }
            }

            


            // Version Update Check
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.Add("User-Agent", "MyGitHubApp");
                try
                {
                    HttpResponseMessage response = await client.GetAsync("https://api.github.com/repos/arcsidian/XivVoices/releases/latest");
                    response.EnsureSuccessStatusCode();
                    string responseBody = await response.Content.ReadAsStringAsync();

                    var releaseInfo = JsonConvert.DeserializeObject<GitHubRelease>(responseBody);

                    if (releaseInfo.TagName == currentVersion)
                        this.Database.Plugin.Log($"Latest Release Tag: {releaseInfo.TagName}, you're up to date!");
                    else
                    {
                        this.Database.Plugin.Print($"XIVV: Version {releaseInfo.TagName} is out, please update the plugin!");
                        Random random = new Random();
                        string filePath = Path.Combine(this.Database.Plugin.Interface.AssemblyLocation.Directory?.FullName!, "update_" + random.Next(1, 4) + ".ogg");
                        if (!Conditions.IsBoundByDuty && !Conditions.IsOccupiedInCutSceneEvent && !Conditions.IsOccupiedInEvent && !Conditions.IsOccupiedInQuestEvent && this.Database.Plugin.Config.UpdateAudioNotification)
                            Audio.PlaySystemAudio(DecodeOggOpusToPCM(filePath));
                    }
                }
                catch (HttpRequestException e)
                {
                    this.Database.Plugin.PrintError("Exception Caught!");
                    this.Database.Plugin.PrintError("Message: " + e.Message);
                }
                catch (TaskCanceledException e)
                {
                    this.Database.Plugin.PrintError("Request Timed Out!");
                    this.Database.Plugin.PrintError("Message: " + e.Message);
                }
                catch (Exception e)
                {
                    this.Database.Plugin.PrintError("Unexpected Exception Caught: " + e.Message);
                    this.Database.Plugin.LogError("AutoUpdate1 ---> Exception Stack Trace: " + e.StackTrace);
                }
                finally
                {
                    await Task.Delay(600000);
                    CheckingForNewVersion = false;
                }
            }

            if (!this.Database.Plugin.Config.AutoUpdate) return;
            if (Updater.Busy) return;

            // Voice Files Update Check
            string dateString = await this.Database.FetchDateFromServer("http://www.arcsidian.com/xivv.json");
            if (dateString == null) return;

            try
            {
                DateTime serverDateTime = DateTime.Parse(dateString, null, DateTimeStyles.RoundtripKind);
                int comparisonResult = DateTime.Compare(this.Database.Plugin.Config.LastUpdate, serverDateTime);
                if (comparisonResult < 0)
                {
                    this.Database.Plugin.Chat.Print("Xiv Voices: Checking for new Voice Files... There is a new update!");
                    if (!Conditions.IsBoundByDuty && !Conditions.IsOccupiedInCutSceneEvent && !Conditions.IsOccupiedInEvent && !Conditions.IsOccupiedInQuestEvent)
                    {
                        this.Updater.ServerLastUpdate = serverDateTime;
                        await this.Updater.Check(true, this.Database.Plugin.Window.IsOpen);
                    }    
                }
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"AutoUpdate2 ---> Exception: {ex}");
            }
            
        }

        public void Dispose()
        {
            speakBlock.Dispose();
            reportBlock.Dispose();
            StopTTS();
            _autoUpdateTimer?.Dispose();
            _autoUpdateTimer = null;
            _updateTimer?.Dispose();
            _updateTimer = null;
            Active = false;
        }
        #endregion


        #region Processing Methods
        public void Process(string type, string speaker, string npcID, string skeletonID, string message, string body, string gender, string race, string tribe, string eyes, string language, Vector3 position, ICharacter character, string user)
        {
            string possibleFileName = Regex.Replace(message, "<[^<]*>", "");
            possibleFileName = this.Database.RemoveSymbolsAndLowercase(possibleFileName);
            if(possibleFileName.IsNullOrEmpty()) return;


            TTSData ttsData = new TTSData(type, speaker, npcID, skeletonID, message, body, gender, race, tribe, eyes, language, position, character, user);
            XivMessage msg = new XivMessage(ttsData);

            if (ttsData.Type != "Cancel" && ttsData.Language != "English")
                return;

            if (ttsData.Speaker == "NPC")
                return;

            if (this.Database.Plugin.Config.SkipEnabled && (ttsData.Type == "Dialogue" || ttsData.Type == "Cancel") )
                Audio.StopAudio();

            if (ttsData.Type == "Cancel")
                return;

            Plugin.PluginLog.Information($"New Dialogue: [Gender]:{msg.TtsData.Gender}, [Body]:{msg.TtsData.Body}, [Race]:{msg.TtsData.Race}, [Tribe]:{msg.TtsData.Tribe}, [Eyes]:{msg.TtsData.Eyes} [Reported]:{msg.Reported} [Ignored]:{msg.Ignored}, [Message]:{msg.TtsData.Message},");

            if (ttsData.Type != "Dialogue" && ttsData.Type != "Bubble" && ttsData.Type != "NPCDialogueAnnouncements")
            {
                Plugin.PluginLog.Information("[Ignored] " + ttsData.Speaker + ":" + ttsData.Message);
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
                Plugin.PluginLog.Information("Race before Mapper: " + msg.TtsData.Race);
                msg.TtsData.Race = Mapper.GetSkeleton(int.Parse(msg.TtsData.SkeletonID), msg.TtsData.Region);
                if (msg.TtsData.Speaker.Contains("Moogle"))
                    msg.TtsData.Race = "Moogle";
                Plugin.PluginLog.Information("Race after Mapper: " + msg.TtsData.Race);
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
                    //msg.Sentence = msg.TtsData.Message;
                    if (!msg.Reported)
                    {
                        ReportToArc(msg);
                        msg.Reported = true;
                    }
                }
            }

            if (msg.isRetainer && !this.Database.Plugin.Config.RetainersEnabled) return;
            if (IgnoredDialogues.Contains(msg.Speaker + msg.Sentence) || this.Database.Ignored.Contains(msg.Speaker)) return;

            Plugin.PluginLog.Information($"After processing: [Gender]:{msg.TtsData.Gender}, [Body]:{msg.TtsData.Body}, [Race]:{msg.TtsData.Race}, [Tribe]:{msg.TtsData.Tribe}, [Eyes]:{msg.TtsData.Eyes} [Reported]:{msg.Reported} [Ignored]:{msg.Ignored}, [Message]:{msg.TtsData.Message},");
            if (msg.NPC == null)
                Plugin.PluginLog.Information("npc is null, voice name is " + msg.VoiceName);

            this.Database.Plugin.Log($"Data: [Gender]:{msg.TtsData.Gender}, [Body]:{msg.TtsData.Body}, [Race]:{msg.TtsData.Race}, [Tribe]:{msg.TtsData.Tribe}, [Eyes]:{msg.TtsData.Eyes} [Reported]:{msg.Reported} [Ignored]:{msg.Ignored}\n{msg.TtsData.Speaker}:{msg.TtsData.Message}\n");

            if (msg.AccessRequested != "")
                _ = Database.AccessRequest(msg);
            else if (msg.GetRequested != "")
                _ = Database.GetRequest(msg);
            else
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
            // Remove '!' and '?' from xivMessage.Speaker
            if(xivMessage.Speaker != "???")
                xivMessage.Speaker = xivMessage.Speaker.Replace("!", "").Replace("?", "");

            // Replace 'full name' with 'firstName'
            string pattern = "\\b" + this.Database.Firstname + " " + this.Database.Lastname + "\\b";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, this.Database.Firstname);

            // Replace 'lastName' with 'firstName'
            pattern = "\\b" + this.Database.Lastname + "\\b";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, this.Database.Firstname);

            // Replace 'firstName' with '_NAME_'
            pattern = "(?<!the )\\b" + this.Database.Firstname + "\\b(?! of the)";
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

            // 9 - Lady Luck
            pattern = @"And the winning number for draw \d+ is... \d+!";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "And here is the winning number!");
            pattern = @"And the Early Bird Bonus grants everyone an extra \d+%! Make sure you lucky folk claim your winnings promptly!";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "And the Early Bird Bonus grants everyone an extra! Make sure you lucky folk claim your winnings promptly!");

            // 10- Jumbo Cactpot Broker
            pattern = @"Welcome to drawing number \d+ of the Jumbo Cactpot! Can I interest you in a ticket to fame and fortune?";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "Welcome to drawing number of the Jumbo Cactpot! Can I interest you in a ticket to fame and fortune?");

            // 11- Gold Saucer Attendant
            pattern = @"Tickets for drawing number \d+ of the Mini Cactpot are now on sale. To test your fortunes, make your way to Entrance Square!";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "Tickets for this drawing number of the Mini Cactpot are now on sale. To test your fortunes, make your way to Entrance Square!");
            pattern = @"Entries are now being accepted for drawing number \d+ of the Mini Cactpot! Venture to Entrance Square to test your luck!";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "Entries are now being accepted for this drawing number of the Mini Cactpot! Venture to Entrance Square to test your luck!");
            pattern = @"Entries for drawing number \d+ of the Mini Cactpot will close momentarily. Those still wishing to purchase a ticket are encouraged to act quickly!";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "Entries for the drawing number of the Mini Cactpot will close momentarily. Those still wishing to purchase a ticket are encouraged to act quickly!");


            // 12- Delivery Moogle
            pattern = @"Your mailbox is a complete and utter mess! There wasn't any room left, so I had to send back \d+ letters, kupo!";
            xivMessage.Sentence = Regex.Replace(xivMessage.Sentence, pattern, "Your mailbox is a complete and utter mess! There wasn't any room left, so I had to send back some letters, kupo!");

            // 13- Mini Cactpot Broker
            if (xivMessage.Speaker == "Mini Cactpot Broker")
            {
                if (xivMessage.Sentence.StartsWith("We have a winner! Please accept my congratulations"))
                    xivMessage.Sentence = "We have a winner! Please accept my congratulations!";
                if (xivMessage.Sentence.StartsWith("Congratulations! Here is your prize"))
                    xivMessage.Sentence = "Congratulations! Here is your prize. Would you like to purchase another ticket?";
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
            if(!this.Database.NpcsWithRetainerLines.Contains(xivMessage.Speaker))
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
                Plugin.PluginLog.Information(xivMessage.Speaker + " does not exist in the DB");
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
                this.Database.Plugin.Log(message.Speaker + " --> npcWithVariedLooks ");
                message.NPC.Body = message.TtsData.Body;
                message.NPC.Gender = message.TtsData.Gender;
                message.NPC.Race = message.TtsData.Race;
                message.NPC.Tribe = message.TtsData.Tribe;
                message.NPC.Eyes = message.TtsData.Eyes;
                npcWithVariedLooksFound = true;
            }

            if (!fetchedByID && this.Database.VoiceNames.TryGetValue(message.Speaker, out string voiceName) && !npcWithVariedLooksFound)
            {
                Plugin.PluginLog.Information("GetVoiceName: fetchedByID is " + fetchedByID);
                return voiceName;
            }

            // Voice By Age -> Race -> Clan -> Gender -> Face
            else
                return GetOtherVoiceNames(message);
        }

        string GetOtherVoiceNames(XivMessage message)
        {
            if (message.NPC.Body == "Adult")
            {
                if (message.NPC.Race == "Au Ra")
                {
                    if (message.NPC.Tribe == "Raen" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Au_Ra_Raen_Female_01";
                    if (message.NPC.Tribe == "Raen" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Au_Ra_Raen_Female_02";
                    if (message.NPC.Tribe == "Raen" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Au_Ra_Raen_Female_03";
                    if (message.NPC.Tribe == "Raen" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Au_Ra_Raen_Female_04";
                    if (message.NPC.Tribe == "Raen" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Au_Ra_Raen_Female_05";

                    if (message.NPC.Tribe == "Raen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Au_Ra_Raen_Male_01";
                    if (message.NPC.Tribe == "Raen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Au_Ra_Raen_Male_02";
                    if (message.NPC.Tribe == "Raen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Au_Ra_Raen_Male_03";
                    if (message.NPC.Tribe == "Raen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Au_Ra_Raen_Male_04";
                    if (message.NPC.Tribe == "Raen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Au_Ra_Raen_Male_05";
                    if (message.NPC.Tribe == "Raen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                        return "Au_Ra_Raen_Male_06";

                    if (message.NPC.Tribe == "Xaela" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Au_Ra_Xaela_Female_01";
                    if (message.NPC.Tribe == "Xaela" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Au_Ra_Xaela_Female_02";
                    if (message.NPC.Tribe == "Xaela" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Au_Ra_Xaela_Female_03";
                    if (message.NPC.Tribe == "Xaela" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Au_Ra_Xaela_Female_04";
                    if (message.NPC.Tribe == "Xaela" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Au_Ra_Xaela_Female_05";

                    if (message.NPC.Tribe == "Xaela" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Au_Ra_Xaela_Male_01";
                    if (message.NPC.Tribe == "Xaela" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Au_Ra_Xaela_Male_02";
                    if (message.NPC.Tribe == "Xaela" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Au_Ra_Xaela_Male_03";
                    if (message.NPC.Tribe == "Xaela" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Au_Ra_Xaela_Male_04";
                    if (message.NPC.Tribe == "Xaela" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Au_Ra_Xaela_Male_05";
                    if (message.NPC.Tribe == "Xaela" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                        return "Au_Ra_Xaela_Male_06";
                }

                if (message.NPC.Race == "Elezen")
                {
                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Elezen_Duskwight_Female_01";
                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Elezen_Duskwight_Female_02";
                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Elezen_Duskwight_Female_03";
                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Elezen_Duskwight_Female_04";
                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Elezen_Duskwight_Female_05_06";
                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 6")
                        return "Elezen_Duskwight_Female_05_06";

                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Elezen_Duskwight_Male_01";
                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Elezen_Duskwight_Male_02";
                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Elezen_Duskwight_Male_03";
                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Elezen_Duskwight_Male_04";
                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Elezen_Duskwight_Male_05";
                    if (message.NPC.Tribe == "Duskwight" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                        return "Elezen_Duskwight_Male_06";

                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Elezen_Wildwood_Female_01";
                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Elezen_Wildwood_Female_02";
                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Elezen_Wildwood_Female_03";
                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Elezen_Wildwood_Female_04";
                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Elezen_Wildwood_Female_05";
                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 6")
                        return "Elezen_Wildwood_Female_06";

                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Elezen_Wildwood_Male_01";
                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Elezen_Wildwood_Male_02";
                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Elezen_Wildwood_Male_03";
                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Elezen_Wildwood_Male_04";
                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Elezen_Wildwood_Male_05";
                    if (message.NPC.Tribe == "Wildwood" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                        return "Elezen_Wildwood_Male_06";
                }

                if (message.NPC.Race == "Hrothgar")
                {
                    if (message.NPC.Tribe == "Helions" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Hrothgar_Helion_01_05";
                    if (message.NPC.Tribe == "Helions" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Hrothgar_Helion_02";
                    if (message.NPC.Tribe == "Helions" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Hrothgar_Helion_03";
                    if (message.NPC.Tribe == "Helions" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Hrothgar_Helion_04";
                    if (message.NPC.Tribe == "Helions" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Hrothgar_Helion_01_05";

                    if (message.NPC.Tribe == "The Lost" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Hrothgar_The_Lost_01";
                    if (message.NPC.Tribe == "The Lost" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Hrothgar_The_Lost_02";
                    if (message.NPC.Tribe == "The Lost" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Hrothgar_The_Lost_03";
                    if (message.NPC.Tribe == "The Lost" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Hrothgar_The_Lost_04_05";
                    if (message.NPC.Tribe == "The Lost" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Hrothgar_The_Lost_04_05";
                }

                if (message.NPC.Race == "Hyur")
                {
                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Hyur_Highlander_Female_01";
                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Hyur_Highlander_Female_02";
                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Hyur_Highlander_Female_03";
                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Hyur_Highlander_Female_04";
                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Hyur_Highlander_Female_05";
                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 6")
                        return "Hyur_Highlander_Female_06";

                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Hyur_Highlander_Male_01";
                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Hyur_Highlander_Male_02";
                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Hyur_Highlander_Male_03";
                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Hyur_Highlander_Male_04";
                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Hyur_Highlander_Male_05";
                    if (message.NPC.Tribe == "Highlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                        return "Hyur_Highlander_Male_06";

                    if (message.NPC.Tribe == "Midlander" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Hyur_Midlander_Female_01";
                    if (message.NPC.Tribe == "Midlander" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Hyur_Midlander_Female_02";
                    if (message.NPC.Tribe == "Midlander" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Hyur_Midlander_Female_03";
                    if (message.NPC.Tribe == "Midlander" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Hyur_Midlander_Female_04";
                    if (message.NPC.Tribe == "Midlander" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Hyur_Midlander_Female_05";

                    if (message.NPC.Tribe == "Midlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Hyur_Midlander_Male_01";
                    if (message.NPC.Tribe == "Midlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Hyur_Midlander_Male_02";
                    if (message.NPC.Tribe == "Midlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Hyur_Midlander_Male_03";
                    if (message.NPC.Tribe == "Midlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Hyur_Midlander_Male_04";
                    if (message.NPC.Tribe == "Midlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Hyur_Midlander_Male_05";
                    if (message.NPC.Tribe == "Midlander" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                        return "Hyur_Midlander_Male_06";
                }

                if (message.NPC.Race == "Lalafell")
                {
                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Lalafell_Dunesfolk_Female_01";
                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Lalafell_Dunesfolk_Female_02";
                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Lalafell_Dunesfolk_Female_03";
                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Lalafell_Dunesfolk_Female_04";
                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Lalafell_Dunesfolk_Female_05";
                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 6")
                        return "Lalafell_Dunesfolk_Female_06";

                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Lalafell_Dunesfolk_Male_01";
                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Lalafell_Dunesfolk_Male_02";
                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Lalafell_Dunesfolk_Male_03";
                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Lalafell_Dunesfolk_Male_04";
                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Lalafell_Dunesfolk_Male_05";
                    if (message.NPC.Tribe == "Dunesfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                        return "Lalafell_Dunesfolk_Male_06";

                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Lalafell_Plainsfolk_Female_01";
                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Lalafell_Plainsfolk_Female_02";
                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Lalafell_Plainsfolk_Female_03";
                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Lalafell_Plainsfolk_Female_04";
                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Lalafell_Plainsfolk_Female_05";
                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 6")
                        return "Lalafell_Plainsfolk_Female_06";

                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Lalafell_Plainsfolk_Male_01";
                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Lalafell_Plainsfolk_Male_02";
                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Lalafell_Plainsfolk_Male_03";
                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Lalafell_Plainsfolk_Male_04";
                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Lalafell_Plainsfolk_Male_05";
                    if (message.NPC.Tribe == "Plainsfolk" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                        return "Lalafell_Plainsfolk_Male_06";
                }

                if (message.NPC.Race == "Miqo'te")
                {
                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Miqote_Keeper_of_the_Moon_Female_01";
                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Miqote_Keeper_of_the_Moon_Female_02";
                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Miqote_Keeper_of_the_Moon_Female_03";
                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Miqote_Keeper_of_the_Moon_Female_04";
                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Miqote_Keeper_of_the_Moon_Female_05";
                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 6")
                        return "Miqote_Keeper_of_the_Moon_Female_06";

                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Miqote_Keeper_of_the_Moon_Male_01";
                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Miqote_Keeper_of_the_Moon_Male_02_06";
                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Miqote_Keeper_of_the_Moon_Male_03";
                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Miqote_Keeper_of_the_Moon_Male_04";
                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Miqote_Keeper_of_the_Moon_Male_05";
                    if (message.NPC.Tribe == "Keeper of the Moon" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                        return "Miqote_Keeper_of_the_Moon_Male_02_06";

                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Miqote_Seeker_of_the_Sun_Female_01";
                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Miqote_Seeker_of_the_Sun_Female_02";
                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Miqote_Seeker_of_the_Sun_Female_03";
                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Miqote_Seeker_of_the_Sun_Female_04";
                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Miqote_Seeker_of_the_Sun_Female_05";
                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 6")
                        return "Miqote_Seeker_of_the_Sun_Female_06";

                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Miqote_Seeker_of_the_Sun_Male_01";
                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Miqote_Seeker_of_the_Sun_Male_02";
                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Miqote_Seeker_of_the_Sun_Male_03";
                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Miqote_Seeker_of_the_Sun_Male_04";
                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Miqote_Seeker_of_the_Sun_Male_05";
                    if (message.NPC.Tribe == "Seeker of the Sun" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                        return "Miqote_Seeker_of_the_Sun_Male_06";

                    //if (message.NPC.Tribe == "Fat Cat")
                    //    return "Miqote_Fat";
                }

                if (message.NPC.Race == "Roegadyn")
                {
                    if (message.NPC.Tribe == "Hellsguard" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Roegadyn_Hellsguard_Female_01";
                    if (message.NPC.Tribe == "Hellsguard" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Roegadyn_Hellsguard_Female_02";
                    if (message.NPC.Tribe == "Hellsguard" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Roegadyn_Hellsguard_Female_03";
                    if (message.NPC.Tribe == "Hellsguard" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Roegadyn_Hellsguard_Female_04";
                    if (message.NPC.Tribe == "Hellsguard" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Roegadyn_Hellsguard_Female_05";

                    if (message.NPC.Tribe == "Hellsguard" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Roegadyn_Hellsguard_Male_01";
                    if (message.NPC.Tribe == "Hellsguard" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Roegadyn_Hellsguard_Male_02";
                    if (message.NPC.Tribe == "Hellsguard" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Roegadyn_Hellsguard_Male_03";
                    if (message.NPC.Tribe == "Hellsguard" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Roegadyn_Hellsguard_Male_04";
                    if (message.NPC.Tribe == "Hellsguard" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Roegadyn_Hellsguard_Male_05";

                    if (message.NPC.Tribe == "Sea Wolf" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Roegadyn_Sea_Wolves_Female_01";
                    if (message.NPC.Tribe == "Sea Wolf" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Roegadyn_Sea_Wolves_Female_02";
                    if (message.NPC.Tribe == "Sea Wolf" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Roegadyn_Sea_Wolves_Female_03";
                    if (message.NPC.Tribe == "Sea Wolf" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Roegadyn_Sea_Wolves_Female_04";
                    if (message.NPC.Tribe == "Sea Wolf" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Roegadyn_Sea_Wolves_Female_05";

                    if (message.NPC.Tribe == "Sea Wolf" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Roegadyn_Sea_Wolves_Male_01";
                    if (message.NPC.Tribe == "Sea Wolf" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Roegadyn_Sea_Wolves_Male_02";
                    if (message.NPC.Tribe == "Sea Wolf" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Roegadyn_Sea_Wolves_Male_03";
                    if (message.NPC.Tribe == "Sea Wolf" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Roegadyn_Sea_Wolves_Male_04";
                    if (message.NPC.Tribe == "Sea Wolf" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                        return "Roegadyn_Sea_Wolves_Male_05";
                }

                if (message.NPC.Race == "Viera")
                {
                    if (message.NPC.Tribe == "Rava" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Viera_Rava_Female_01_05";
                    if (message.NPC.Tribe == "Rava" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Viera_Rava_Female_02";
                    if (message.NPC.Tribe == "Rava" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Viera_Rava_Female_03";
                    if (message.NPC.Tribe == "Rava" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Viera_Rava_Female_04";
                    if (message.NPC.Tribe == "Rava" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Viera_Rava_Female_01_05";

                    if (message.NPC.Tribe == "Rava" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                        return "Viera_Rava_Male_01";
                    if (message.NPC.Tribe == "Rava" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Viera_Rava_Male_03";
                    if (message.NPC.Tribe == "Rava" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                        return "Viera_Rava_Male_04";

                    if (message.NPC.Tribe == "Veena" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                        return "Viera_Veena_Female_01_05";
                    if (message.NPC.Tribe == "Veena" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                        return "Viera_Veena_Female_02";
                    if (message.NPC.Tribe == "Veena" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                        return "Viera_Veena_Female_03";
                    if (message.NPC.Tribe == "Veena" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                        return "Viera_Veena_Female_04";
                    if (message.NPC.Tribe == "Veena" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                        return "Viera_Veena_Female_01_05";

                    if (message.NPC.Tribe == "Veena" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                        return "Viera_Veena_Male_02";
                    if (message.NPC.Tribe == "Veena" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                        return "Viera_Veena_Male_03";
                }
            }

            if (message.NPC.Body == "Elderly")
            {
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male")
                    return "Elderly_Male_Hyur";

                if (message.NPC.Gender == "Male")
                    return "Elderly_Male";

                if (message.NPC.Gender == "Female")
                    return "Elderly_Female";
            }

            if (message.NPC.Body == "Child")
            {
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                    return "Child_Hyur_Female_1";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                    return "Child_Hyur_Female_2";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                    return "Child_Hyur_Female_3_5";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                    return "Child_Hyur_Female_4";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                    return "Child_Hyur_Female_3_5";

                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                    return "Child_Hyur_Male_1";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                    return "Child_Hyur_Male_2";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                    return "Child_Hyur_Male_3_6";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                    return "Child_Hyur_Male_4";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                    return "Child_Hyur_Male_5";
                if (message.NPC.Race == "Hyur" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                    return "Child_Hyur_Male_3_6";

                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                    return "Child_Elezen_Female_1_3";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                    return "Child_Elezen_Female_2";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                    return "Child_Elezen_Female_1_3";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                    return "Child_Elezen_Female_4";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                    return "Child_Elezen_Female_5_6";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 6")
                    return "Child_Elezen_Female_5_6";

                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                    return "Child_Elezen_Male_1";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                    return "Child_Elezen_Male_2";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                    return "Child_Elezen_Male_3";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                    return "Child_Elezen_Male_4";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                    return "Child_Elezen_Male_5_6";
                if (message.NPC.Race == "Elezen" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                    return "Child_Elezen_Male_5_6";

                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 1")
                    return "Child_Aura_Female_1_5";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                    return "Child_Aura_Female_2";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                    return "Child_Aura_Female_4";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 5")
                    return "Child_Aura_Female_1_5";

                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 1")
                    return "Child_Aura_Male_1";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 2")
                    return "Child_Aura_Male_2";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 3")
                    return "Child_Aura_Male_3";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 4")
                    return "Child_Aura_Male_4";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 5")
                    return "Child_Aura_Male_5_6";
                if (message.NPC.Race == "Au Ra" && message.NPC.Gender == "Male" && message.NPC.Eyes == "Option 6")
                    return "Child_Aura_Male_5_6";

                if (message.NPC.Race == "Miqo'te" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 2")
                    return "Child_Miqote_Female_2";
                if (message.NPC.Race == "Miqo'te" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 3")
                    return "Child_Miqote_Female_3_4";
                if (message.NPC.Race == "Miqo'te" && message.NPC.Gender == "Female" && message.NPC.Eyes == "Option 4")
                    return "Child_Miqote_Female_3_4";


            }


            this.Database.Plugin.Log("message.NPC.Race: " + message.NPC.Race);

            string race;
            for (int i = 0; i < 2; i++)
            {
                race = i == 0 ? message.NPC.Race : message.TtsData.Race;

                // ARR Beast Tribes
                if (race == "Amalj'aa")
                    return "Amaljaa";

                if (race == "Sylph")
                    return "Sylph";

                if (race == "Kobold")
                    return "Kobold";

                if (race == "Sahagin")
                    return "Sahagin";

                if (race == "Ixal")
                    return "Ixal";

                if (race == "Qiqirn")
                    return "Qiqirn";

                // HW Beast Tribes
                if (message.TtsData.Race.StartsWith("Dragon"))
                {
                    if (this.Database.VoiceNames.TryGetValue(message.Speaker, out string voiceName))
                        return voiceName;
                    else
                        return message.TtsData.Race;
                }

                if (race == "Goblin")
                {
                    if (message.NPC.Gender == "Female" || message.TtsData.Gender == "Female")
                        return "Goblin_Female";
                    else
                        return "Goblin_Male";
                }

                if (race == "Vanu Vanu")
                {
                    if (message.NPC.Gender == "Female" || message.TtsData.Gender == "Female")
                        return "Vanu_Female";
                    else
                        return "Vanu_Male";
                }

                if (race == "Vath")
                    return "Vath";

                if (race == "Moogle")
                    return "Moogle";

                if (race == "Node")
                    return "Node";

                // SB Beast Tribes
                if (race == "Kojin")
                    return "Kojin";

                if (race == "Ananta")
                    return "Ananta";

                if (race == "Namazu")
                    return "Namazu";

                if (race == "Lupin")
                {
                    if (message.Speaker == "Hakuro" || message.Speaker == "Hakuro Gunji" || message.Speaker == "Hakuro Whitefang")
                        return "Ranjit";

                    int hashValue = message.Speaker.GetHashCode();
                    int result = Math.Abs(hashValue) % 10 + 1;

                    switch(result)
                    {
                        case 1: return "Hrothgar_Helion_03";
                        case 2: return "Hrothgar_Helion_04";
                        case 3: return "Hrothgar_The_Lost_02";
                        case 4: return "Hrothgar_The_Lost_03";
                        case 5: return "Lalafell_Dunesfolk_Male_06";
                        case 6: return "Roegadyn_Hellsguard_Male_04";
                        case 7: return "Others_Widargelt";
                        case 8: return "Hyur_Highlander_Male_04";
                        case 9: return "Hrothgar_Helion_02";
                        case 10: return "Hyur_Highlander_Male_05";
                    }
                    return "Lupin";
                }

                // Shb Beast Tribes
                if (race == "Pixie")
                    return "Pixie";

                // EW Beast Tribes
                if (race == "Matanga")
                {
                    if (message.NPC.Gender == "Female" || message.TtsData.Gender == "Female")
                        return "Matanga_Female";
                    else
                        return "Matanga_Male";
                }

                if (race == "Loporrit")
                    return "Loporrit";

                if (race == "Omicron")
                    return "Omicron";

                if (race == "Ea")
                    return "Ea";

                // Bosses
                if (message.NPC.Race.StartsWith("Boss"))
                    return message.NPC.Race;
            }

            Plugin.PluginLog.Information("Cannot find a voice for " + message.Speaker);
            return "Unknown";
        }

        public void AddToQueue(XivMessage msg)
        {
            Plugin.PluginLog.Information($"AddToQueue ---> {msg.TtsData.Speaker}: {msg.TtsData.Message}");
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

        public async Task SpeakAI(XivMessage msg)
        {
            // Start Local TTS Engine
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

                // Remove anything that's not a letter, number, space, ',' or '.'
                string cleanedMessage = new string(sentence.Where(c => char.IsLetterOrDigit(c) || c == ',' || c == '.' || c == ' ').ToArray());
                if (!cleanedMessage.Any(char.IsLetter))
                    return;
                sentence = cleanedMessage;

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
                Plugin.PluginLog.Error($"Error processing audio file: {ex}");
            }
        }

        public static string ProcessPlayerChat(string sentence, string speaker)
        {
            sentence = sentence.Trim();
            string playerName = speaker.Split(" ")[0];
            bool iAmSpeaking = XivEngine.Instance.Database.Plugin.ClientState.LocalPlayer.Name.TextValue == speaker;
            var options = RegexOptions.IgnoreCase;
            var emoticons = new Dictionary<string, string>
            {
                { @"(^|\s)o/($|\s)", "waves and says " },
                { @"(^|\s)\\o($|\s)", "waves and says " },
                { @"(^|\s)(:\)|\^\^|\^[^\s]\^)($|\s)", "smiles and says " },
                { @"(^|\s)(:D|:>)($|\s)", "looks happy and says " },
                { @"(^|\s)(:O|:0)($|\s)", "looks surprised and says " },
                { @"(^|\s)(:\(|:<|:C|>([^\s]+)<)($|\s)", "looks sad and says " },
                { @"\bxD\b", "laughs and says " },
                { @"(^|\s)(:3)($|\s)", "gives a playful smile and says " },
                { @"(^|\s)(:P)($|\s)", "sticks a tongue out and says " },
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
            bool saysAdded = false;
            foreach (var emoticon in emoticons)
            {
                if (Regex.IsMatch(sentence, emoticon.Key, options))
                {
                    saysAdded = true;
                    sentence = Regex.Replace(sentence, emoticon.Key, " ", options).Trim();
                    sentence = playerName + " " + emoticon.Value + sentence;
                    break;
                }
            }

            if (!saysAdded && XivEngine.Instance.Database.Plugin.Config.LocalTTSPlayerSays)
            {
                string says = iAmSpeaking ? " say " : " says ";
                sentence = playerName + says + sentence;
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
            sentence = Regex.Replace(sentence, @"\bty4p\b", "thank you for the party!", options);
            sentence = Regex.Replace(sentence, @"\btyvm\b", "thank you very much", options);
            sentence = Regex.Replace(sentence, @"\btyft\b", "thank you for the train", options);
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
            sentence = Regex.Replace(sentence, @"\bglhf\b", "good luck, have fun", options);
            sentence = Regex.Replace(sentence, @"\bgl\b", "good luck", options);
            sentence = Regex.Replace(sentence, @"\bsry\b", "sorry", options);
            sentence = Regex.Replace(sentence, @"\bsrry\b", "sorry", options);
            sentence = Regex.Replace(sentence, @"\bcs\b", "cutscene", options);
            sentence = Regex.Replace(sentence, @"\bttyl\b", "talk to you later", options);
            sentence = Regex.Replace(sentence, @"\boki\b", "okay", options);
            sentence = Regex.Replace(sentence, @"\bkk\b", "okay", options);
            sentence = Regex.Replace(sentence, @"\bffs\b", "for fuck's sake", options);
            sentence = Regex.Replace(sentence, @"\baight\b", "ight", options);
            sentence = Regex.Replace(sentence, @"\bggs\b", "good game", options);
            sentence = Regex.Replace(sentence, @"\bwp\b", "well played", options);
            sentence = Regex.Replace(sentence, @"\bgn\b", "good night", options);
            sentence = Regex.Replace(sentence, @"\bnn\b", "ight night", options);
            sentence = Regex.Replace(sentence, @"\bdd\b", "damage dealer", options);
            sentence = Regex.Replace(sentence, @"\bbis\b", "best in slot", options);
            sentence = Regex.Replace(sentence, @"(?<=\s|^):\)(?=\s|$)", "smile", options);
            sentence = Regex.Replace(sentence, @"(?<=\s|^):\((?=\s|$)", "sadge", options);
            sentence = Regex.Replace(sentence, @"\b<3\b", "heart", options);
            sentence = Regex.Replace(sentence, @"\bARR\b", "A Realm Reborn", options);
            sentence = Regex.Replace(sentence, @"\bHW\b", "Heavensward");
            sentence = Regex.Replace(sentence, @"\bSB\b", "Storm Blood");
            sentence = Regex.Replace(sentence, @"\bSHB\b", "Shadow Bangers", options);
            sentence = Regex.Replace(sentence, @"\bEW\b", "End Walker");
            sentence = Regex.Replace(sentence, @"\bucob\b", "ultimate coils of bahamut", options);
            sentence = Regex.Replace(sentence, @"\bIT\b", "it");
            sentence = Regex.Replace(sentence, @"r says", "rr says");
            sentence = Regex.Replace(sentence, @"Eleanorr says", "el-uh-ner says");
            sentence = Regex.Replace(sentence, @"\bm1\b", "\"Melee one\"", options);
            sentence = Regex.Replace(sentence, @"\bm2\b", "\"Melee two\"", options);
            sentence = Regex.Replace(sentence, @"\bot\b", "\"Off-Tank\"", options);
            sentence = Regex.Replace(sentence, @"\bMt\b", "\"Main-Tank\"");
            sentence = Regex.Replace(sentence, @"\bMT\b", "\"Main-Tank\"");
            sentence = Regex.Replace(sentence, @"\bmt\b", "\"mistake\"");
            sentence = Regex.Replace(sentence, @"\br1\b", "\"Ranged One\"", options);
            sentence = Regex.Replace(sentence, @"\br2\b", "\"Ranged Two\"", options);
            sentence = Regex.Replace(sentence, @"\bh1\b", "\"Healer One\"", options);
            sentence = Regex.Replace(sentence, @"\bh2\b", "\"Healer Two\"", options);
            sentence = Regex.Replace(sentence, @"\brn\b", "\"right now\"", options);

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
            try
            {
                Plugin.PluginLog.Information($"SpeakLocallyAsync ---> {msg.TtsData.Speaker}: {msg.TtsData.Message}");

                if (isMp3)
                {
                    File.Delete(msg.FilePath + ".ogg");
                    string arguments = $"-i \"{msg.FilePath + ".mp3"}\" -c:a libopus \"{msg.FilePath + ".ogg"}\"";
                    string ffmpegDirectoryPath = Path.Combine(XivEngine.Instance.Database.ToolsPath); ;
                    FFmpeg.SetExecutablesPath(ffmpegDirectoryPath);
                    IConversion conversion = FFmpeg.Conversions.New().AddParameter(arguments);
                    await conversion.Start();
                    File.Delete(msg.FilePath + ".mp3");
                    msg.FilePath = msg.FilePath + ".ogg";
                }

                if (msg.FilePath.EndsWith(".ogg"))
                {
                    Plugin.PluginLog.Information($"SpeakLocallyAsync: found ogg path: {msg.FilePath}");
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
                        PlayAudio(msg, waveStream, "xivv");
                    }
                    catch (Exception ex)
                    {
                        Plugin.PluginLog.Error($"Error processing audio file: {ex.Message}");
                    }
                }
                else
                {
                    // Handling for other audio formats like WAV
                    try
                    {
                        using (var audioFile = new AudioFileReader("file:" + msg.FilePath))
                            PlayAudio(msg, audioFile, "xivv");
                    }
                    catch (Exception ex)
                    {
                        Plugin.PluginLog.Error($"Error loading audio file: {ex.Message}");
                    }
                }
            }
            finally
            {
                speakBlock.Release();
            }
        }

        public static WaveStream DecodeOggOpusToPCM(string filePath)
        {
            Plugin.PluginLog.Information($"DecodeOggOpusToPCM ---> start");
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
            Plugin.PluginLog.Information($"FFmpegFileToWaveStream ---> start");
            changeTimeBusy = true;
            string outputFilePath = System.IO.Path.Combine(XivEngine.Instance.Database.DirectoryPath, "current" + XivEngine.Instance.Database.GenerateRandomSuffix() + ".ogg");

            string filterArgs = SoundEffects(msg);
            string arguments = $"-i \"{msg.FilePath}\" -filter_complex \"{filterArgs}\" -c:a libopus \"{outputFilePath}\"";

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
                    Plugin.PluginLog.Error($"Error deleting temporary file: {ex.Message}");
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

        static string SoundEffects(XivMessage msg)
        {
            bool changeSpeed = false;
            string additionalChanges = "";
            if (XivEngine.Instance.Database.Plugin.Config.Speed != 100) changeSpeed = true;
            if (msg.VoiceName == "Omicron" || msg.VoiceName == "Node" || msg.NPC.Type.Contains("Robot")) additionalChanges = "robot";

            string filterArgs = "";
            bool addEcho = false;

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

            float setRate = 48000;
            float tempo = 1.0f;

            // Sounds Effects for Age
            if (msg.NPC.Type == "Old")
            {
                setRate *= (1 - 0.1f);
                tempo /= (1 - 0.1f);
                if (filterArgs != "") filterArgs += ",";
                filterArgs += $"\"atempo={tempo},asetrate={setRate}\"";
            }

            // Sound Effects for Dragons
            if (msg.TtsData.Race.StartsWith("Dragon"))
            {
                if(msg.NPC.Type == "Female")
                {
                    setRate *= (1 - 0.1f);
                    tempo /= (1 + 0.1f);
                }
                else
                    switch(msg.TtsData.Race)
                    {
                        case "Dragon_Medium":
                            setRate *= (1 - 0.1f);
                            tempo /= (1 + 0.1f);
                            break;
                        case "Dragon_Small": 
                            setRate *= (1 - 0.03f);
                            tempo /= (1 + 0.06f);
                            break;
                        default:
                            setRate *= (1 - 0.05f);
                            tempo /= (1 + 0.05f); 
                            break;
                    }

                if (tempo != 1)
                {
                    if (filterArgs != "") filterArgs += ",";
                    filterArgs += $"\"atempo={tempo},asetrate={setRate}\"";
                }
                    
                addEcho = true;
            }

            // Sound Effects for Ea
            if (msg.VoiceName == "Ea")
            {
                filterArgs += "\"[0:a]asplit=2[sc][oc];[sc]rubberband=pitch=0.90[sc];[oc]rubberband=pitch=1.02[oc];[sc][oc]amix=inputs=2:duration=longest,volume=2\"";
                filterArgs += ",\"aecho=0.8:0.88:120:0.4\"";
            }

            // Sound Effects for Golems
            else if (msg.TtsData.Race.StartsWith("Golem"))
            {
                setRate *= (1 - 0.15f);
                tempo /= (1 - 0.15f);
                if (filterArgs != "") filterArgs += ",";
                filterArgs += $"\"atempo={tempo},asetrate={setRate}\"";
            }

            // Sound Effects for Giants
            else if (msg.TtsData.Race.StartsWith("Giant"))
            {
                setRate *= (1 - 0.25f);
                tempo /= (1 - 0.15f);
                if (filterArgs != "") filterArgs += ",";
                filterArgs += $"\"atempo={tempo},asetrate={setRate}\"";
            }

            // Sound Effects for Primals
            if (msg.NPC.Type.StartsWith("Primal"))
            addEcho = true; 

            if (msg.NPC.Type == "Primal M1")
            { 
                setRate *= (1 - 0.15f);
                tempo /= (1 - 0.1f);
                if (filterArgs != "") filterArgs += ",";
                filterArgs += $"\"atempo={tempo},asetrate={setRate}\"";
            }

            else if (msg.NPC.Type == "Primal Dual")
            {
                if (msg.Speaker == "Thal" || msg.Sentence.StartsWith("Nald"))
                    filterArgs += "\"rubberband=pitch=0.92\"";
                else if (msg.Speaker == "Nald" || msg.Sentence.StartsWith("Thal"))
                    filterArgs += "\"rubberband=pitch=1.03\"";
                else
                    filterArgs += "\"[0:a]asplit=2[sc][oc];[sc]rubberband=pitch=0.93[sc];[oc]rubberband=pitch=1.04[oc];[sc][oc]amix=inputs=2:duration=longest,volume=2\"";
            }

            // Sound Effects for Bosses
            if (msg.NPC.Type.StartsWith("Boss"))
                addEcho = true;

            if (msg.NPC.Type == "Boss F1")
            {
                if (filterArgs != "") filterArgs += ",";
                filterArgs += "\"[0:a]asplit=2[sc][oc];[sc]rubberband=pitch=0.8[sc];[oc]rubberband=pitch=1.0[oc];[sc][oc]amix=inputs=2:duration=longest,volume=2\"";
            }

            /*
            if (msg.TtsData.Race == "Pixie")
            {
                setRate *= (1 + 0.15f);
                tempo /= (1 + 0.1f);
                if (filterArgs != "") filterArgs += ",";
                filterArgs += $"\"atempo={tempo},asetrate={setRate}\"";
            }
            */

            if (addEcho)
            {
                if (filterArgs != "") filterArgs += ",";
                filterArgs += "\"aecho=0.8:0.9:500:0.1\"";
            }

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

            Plugin.PluginLog.Information($"SoundEffects ---> done");
            return filterArgs;
        }

        private void PlayAudio(XivMessage xivMessage, WaveStream waveStream, string type)
        {
            Plugin.PluginLog.Information($"PlayAudio ---> type: " + type);
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
            XivEngine.Instance.Database.Plugin.Log("ArcFramework: RedoAudio");
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

        Queue<ReportXivMessage> reports = new Queue<ReportXivMessage>();
        public void ReportUnknown(XivMessage msg)
        {
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;

            if (!this.Database.Plugin.Config.Reports) return;
            Plugin.PluginLog.Information("ReportUnknown");
            
            reports.Enqueue(new ReportXivMessage(msg, "unknown", ""));
        }

        public void ReportDifferent(XivMessage msg)
        {
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;

            if (Database.Access)
            {
                msg.AccessRequested = "different";
                return;
            }
            else if (XivEngine.Instance.Database.Plugin.Config.OnlineRequests)
            {
                msg.GetRequested = "different";
                return;
            }

            if (!this.Database.Plugin.Config.Reports) return;
            Plugin.PluginLog.Information("ReportDifferent");
            reports.Enqueue(new ReportXivMessage(msg, "different", ""));
        }

        public void ReportMuteToArc(XivMessage msg, string input)
        {
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;

            if (input.IsNullOrEmpty())
            {
                this.Database.Plugin.PrintError("Report failed, you did not provide any context.");
                return;
            }

            reports.Enqueue(new ReportXivMessage(msg, "mute", input));
        }

        public void ReportRedoToArc(XivMessage msg, string input)
        {
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;

            if(input.IsNullOrEmpty())
            {
                this.Database.Plugin.PrintError("Report failed, you did not provide any context.");
                return;
            }

            reports.Enqueue(new ReportXivMessage(msg, "redo", input));
        }


        public void ReportToArc(XivMessage msg)
        {
            if (Database.Ignored.Contains(msg.Speaker) || Database.Plugin.Config.FrameworkActive) return;

            if (Database.Access)
            {
                msg.AccessRequested = "missing";
                return;
            }
            else if (XivEngine.Instance.Database.Plugin.Config.OnlineRequests)
            {
                msg.GetRequested = "missing";
                return;
            }

            if (!this.Database.Plugin.Config.Reports) return;

            reports.Enqueue(new ReportXivMessage(msg, "missing", ""));
        }

        private readonly HttpClient client = new HttpClient();
        public async Task ReportToArcJSON(XivMessage xivMessage, string folder, string comment)
        {
            if (!this.Database.Plugin.Config.Reports) return;

            await reportBlock.WaitAsync();
            try
            {
                Plugin.PluginLog.Information($"Reporting line: \"{xivMessage.Sentence}\"");

                string[] fullname = Database.Plugin.ClientState.LocalPlayer.Name.TextValue.Split(" ");
                xivMessage.Sentence = xivMessage.TtsData.Message;
                xivMessage.Sentence = xivMessage.Sentence.Replace(fullname[0], "_FIRSTNAME_");
                if (fullname.Length > 1)
                {
                    xivMessage.Sentence = xivMessage.Sentence.Replace(fullname[1], "_LASTNAME_");
                }

                string url = $"?user={xivMessage.TtsData.User}&speaker={xivMessage.Speaker}&sentence={xivMessage.Sentence}&npcid={xivMessage.NpcId}&skeletonid={xivMessage.TtsData.SkeletonID}&body={xivMessage.TtsData.Body}&gender={xivMessage.TtsData.Gender}&race={xivMessage.TtsData.Race}&tribe={xivMessage.TtsData.Tribe}&eyes={xivMessage.TtsData.Eyes}&folder={folder}";
                if (!reportedLines.Contains(url))
                {
                    reportedLines.Enqueue(url);
                    if (reportedLines.Count > 100)
                        reportedLines.Dequeue();

                    
                    if (Database.Plugin.Config.AnnounceReports) this.Database.Plugin.Print($"Reporting line: \"{xivMessage.Sentence}\"");

                    try
                    {
                        HttpResponseMessage response = await client.GetAsync(this.Database.GetReportSource() + url);
                        response.EnsureSuccessStatusCode();
                        string responseBody = await response.Content.ReadAsStringAsync();
                    }
                    catch (HttpRequestException e)
                    {
                        XivEngine.Instance.Database.Plugin.PrintError("Report failed, saving it Reports folder to be automatically sent later.");
                        Directory.CreateDirectory(this.Database.ReportsPath);
                        string fileName = Path.Combine(this.Database.ReportsPath, $"{xivMessage.Speaker}_{new Random().Next(10000, 99999)}.txt");
                        await File.WriteAllTextAsync(fileName, url);
                    }

                }
                
            }
            finally
            {
                reportBlock.Release();
            }
            
        }

        public async Task LateReportToArcJSON(string url, string path)
        {
            if (!this.Database.Plugin.Config.Reports) return;
            try
            {
                HttpResponseMessage response = await client.GetAsync(this.Database.GetReportSource() + url);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                XivEngine.Instance.Database.Plugin.Print(responseBody);
                File.Delete(path);
                return;
            }
            catch (HttpRequestException e)
            {
                return;
            }
        }
        #endregion

    }


}
