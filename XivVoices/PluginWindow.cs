﻿using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Numerics;
using System.Diagnostics;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using System.Threading.Tasks;
using XivVoices.Engine;
using System.IO;
using System.Linq;
using Dalamud.Interface.Textures.TextureWraps;

namespace XivVoices {
    public class PluginWindow : Window {
        private Configuration configuration;
        private IClientState clientState;

        private string managerNullMessage = string.Empty;
        private bool SizeYChanged = false;
        private bool managerNull;
        private Vector2? initialSize;
        private Vector2? changedSize;

        private bool needSave = false;
        private string selectedDrive = string.Empty;
        private string reportInput = new string('\0', 250);
        private bool isFrameworkWindowOpen = false;
        private string currentTab = "General";

        private IDalamudTextureWrap changelogTexture;
        private IDalamudTextureWrap changelogActiveTexture;
        private IDalamudTextureWrap generalSettingsTexture;
        private IDalamudTextureWrap generalSettingsActiveTexture;
        private IDalamudTextureWrap dialogueSettingsTexture;
        private IDalamudTextureWrap dialogueSettingsActiveTexture;
        private IDalamudTextureWrap audioSettingsTexture;
        private IDalamudTextureWrap audioSettingsActiveTexture;
        private IDalamudTextureWrap archiveTexture;
        private IDalamudTextureWrap archiveActiveTexture;
        private IDalamudTextureWrap discordTexture;
        private IDalamudTextureWrap koFiTexture;
        private IDalamudTextureWrap iconTexture;
        private IDalamudTextureWrap logoTexture;

        private IntPtr changelogHandle;
        private IntPtr changelogActiveHandle;
        private IntPtr generalSettingsHandle;
        private IntPtr generalSettingsActiveHandle;
        private IntPtr dialogueSettingsHandle;
        private IntPtr dialogueSettingsActiveHandle;
        private IntPtr audioSettingsHandle;
        private IntPtr audioSettingsActiveHandle;
        private IntPtr archiveHandle;
        private IntPtr archiveActiveHandle;
        private IntPtr discordHandle;
        private IntPtr koFiHandle;
        private IntPtr iconHandle;
        private IntPtr logoHandle;


        public PluginWindow() : base("    XIVV") {
            Size = new Vector2(440, 650);
            initialSize = Size;
            SizeCondition = ImGuiCond.Always;
            Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize;
        }

        public async void InitializeImageHandles()
        {

            changelogTexture = await this.PluginReference.Changelog.RentAsync();
            changelogActiveTexture = await this.PluginReference.ChangelogActive.RentAsync();
            generalSettingsTexture = await this.PluginReference.GeneralSettings.RentAsync();
            generalSettingsActiveTexture = await this.PluginReference.GeneralSettingsActive.RentAsync();
            dialogueSettingsTexture = await this.PluginReference.DialogueSettings.RentAsync();
            dialogueSettingsActiveTexture = await this.PluginReference.DialogueSettingsActive.RentAsync();
            audioSettingsTexture = await this.PluginReference.AudioSettings.RentAsync();
            audioSettingsActiveTexture = await this.PluginReference.AudioSettingsActive.RentAsync();
            archiveTexture = await this.PluginReference.Archive.RentAsync();
            archiveActiveTexture = await this.PluginReference.ArchiveActive.RentAsync();
            discordTexture = await this.PluginReference.Discord.RentAsync();
            koFiTexture = await this.PluginReference.KoFi.RentAsync();
            iconTexture = await this.PluginReference.Icon.RentAsync();
            logoTexture = await this.PluginReference.Logo.RentAsync();

            changelogHandle = changelogTexture.ImGuiHandle;
            changelogActiveHandle = changelogActiveTexture.ImGuiHandle;
            generalSettingsHandle = generalSettingsTexture.ImGuiHandle;
            generalSettingsActiveHandle = generalSettingsActiveTexture.ImGuiHandle;
            dialogueSettingsHandle = dialogueSettingsTexture.ImGuiHandle;
            dialogueSettingsActiveHandle = dialogueSettingsActiveTexture.ImGuiHandle;
            audioSettingsHandle = audioSettingsTexture.ImGuiHandle;
            audioSettingsActiveHandle = audioSettingsActiveTexture.ImGuiHandle;
            archiveHandle = archiveTexture.ImGuiHandle;
            archiveActiveHandle = archiveActiveTexture.ImGuiHandle;
            discordHandle = discordTexture.ImGuiHandle;
            koFiHandle = koFiTexture.ImGuiHandle;
            iconHandle = iconTexture.ImGuiHandle;
            logoHandle = logoTexture.ImGuiHandle;
        }

        public Configuration Configuration {
            get => configuration;
            set {
                configuration = value;
            }
        }

        public IDalamudPluginInterface PluginInterface { get; internal set; }

        internal IClientState ClientState {
            get => clientState;
            set {
                clientState = value;
                clientState.Login += ClientState_Login;
                clientState.Logout += ClientState_Logout;
            }
        }

        public Plugin PluginReference { get; internal set; }
        public event EventHandler OnMoveFailed;

        private void ClientState_Logout() {
        }

        private void ClientState_Login() {
        }

        public override void Draw() {
            if (clientState.IsLoggedIn) {

                if(!configuration.Initialized)
                {
                    InitializationWindow();
                }
                else
                {
                    if (Updater.Instance.State.Count > 0)
                    {
                        if (ImGui.BeginTabBar("ConfigTabs"))
                        {
                            if (ImGui.BeginTabItem("  (           Update Process           )  "))
                            {
                                UpdateWindow();
                                ImGui.EndTabItem();
                            }

                            if (ImGui.BeginTabItem("Dialogue Settings"))
                            {
                                DrawSettings();
                                ImGui.EndTabItem();
                            }

                            if (ImGui.BeginTabItem("Audio Settings"))
                            {
                                AudioSettings();
                                ImGui.EndTabItem();
                            }

                            ImGui.EndTabBar();
                        }
                    }
                    else
                    {
                        var backupColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];
                        ImGui.GetStyle().Colors[(int)ImGuiCol.Button] = new Vector4(0, 0, 0, 0);

                        // Floating Button ----------------------------------
                        var originPos = ImGui.GetCursorPos();
                        ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMax().X + 8f);
                        ImGui.SetCursorPosY(ImGui.GetWindowContentRegionMax().Y - ImGui.GetFrameHeight() - 26f);
                        DrawImageButton("Changelog", currentTab == "Changelog" ? changelogActiveHandle : changelogHandle);
                        ImGui.SetCursorPos(originPos);

                        // The sidebar with the tab buttons
                        ImGui.BeginChild("Sidebar", new Vector2(50, 500), false);

                        DrawSidebarButton("General", generalSettingsHandle, generalSettingsActiveHandle);
                        DrawSidebarButton("Dialogue Settings", dialogueSettingsHandle, dialogueSettingsActiveHandle);
                        DrawSidebarButton("Audio Settings", audioSettingsHandle, audioSettingsActiveHandle);
                        DrawSidebarButton("Audio Logs", archiveHandle, archiveActiveHandle);

                        /*
                        if (ImGui.ImageButton(discordHandle, new Vector2(42, 42)))
                        {
                            Process process = new Process();
                            try
                            {
                                // true is the default, but it is important not to set it to false
                                process.StartInfo.UseShellExecute = true;
                                process.StartInfo.FileName = "https://discord.com";
                                process.Start();
                            }
                            catch (Exception e)
                            {

                            }
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Join Our Discord Community");
                        */


                        if (this.configuration.FrameworkActive) {
                            if (ImGui.ImageButton(iconHandle, new Vector2(42, 42), new Vector2(1, 1)))
                            {
                                isFrameworkWindowOpen = true;
                            }
                        }

                        ImGui.GetStyle().Colors[(int)ImGuiCol.Button] = backupColor;
                        Framework();
                        ImGui.EndChild();

                        // Draw a vertical line separator
                        ImGui.SameLine();
                        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
                        Vector2 lineStart = ImGui.GetCursorScreenPos() - new Vector2(0,10);
                        Vector2 lineEnd = new Vector2(lineStart.X, lineStart.Y + 630);
                        uint lineColor = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.15f, 0.15f, 0.15f, 1));
                        drawList.AddLine(lineStart, lineEnd, lineColor, 1f);
                        ImGui.SameLine(85);

                        // The content area where the selected tab's contents will be displayed
                        ImGui.BeginGroup();

                        if (currentTab == "General")
                        {
                            DrawGeneral();
                        }
                        else if (currentTab == "Dialogue Settings")
                        {
                            DrawSettings();
                        }
                        else if (currentTab == "Audio Settings")
                        {
                            AudioSettings();
                        }
                        else if (currentTab == "Audio Logs")
                        {
                            LogsSettings();
                        }
                        else if (currentTab == "Changelog")
                        {
                            Changelog();
                        }

                        ImGui.EndGroup();
                    }
                }
                
                DrawErrors();
                //Close();
            } else {
                ImGui.TextUnformatted("Please login to access and configure settings.");
            }
        }

        private void DrawImageButton(string tabName, IntPtr imageHandle)
        {
            if (ImGui.ImageButton(imageHandle, new Vector2(42, 42)))
            {
                currentTab = tabName;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(tabName);
            }
        }

        private void DrawSidebarButton(string tabName, IntPtr normalHandle, IntPtr activeHandle)
        {
            DrawImageButton(tabName, currentTab == tabName ? activeHandle : normalHandle);
        }

        private static readonly List<string> ValidTextureExtensions = new List<string>(){
          ".png",
        };

        private void Framework() {
            if (isFrameworkWindowOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(430, 650));
                ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;
                if (ImGui.Begin("Framework", ref isFrameworkWindowOpen, windowFlags))
                {
                    if (ImGui.BeginTabBar("FrameworkTabs"))
                    {
                        if (ImGui.BeginTabItem("General Framework"))
                        {
                            Framework_General();
                            ImGui.EndTabItem();
                        }

                        if (ImGui.BeginTabItem("Unknown Dialogues"))
                        {
                            Framework_Unknown();
                            ImGui.EndTabItem();
                        }

                        if (ImGui.BeginTabItem("   Audio Monitoring   "))
                        {
                            Framework_Audio();
                            ImGui.EndTabItem();
                        }
                        ImGui.EndTabBar();
                    }
                    ImGui.End();
                }
            }
        }

        private void RequestSave()
        {
            Plugin.PluginLog.Error("RequestSave");
            this.configuration.Save();
        }

        private void DrawErrors() { 
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10f);
            ImGui.BeginChild("ErrorRegion", new Vector2(
            ImGui.GetContentRegionAvail().X,
            ImGui.GetContentRegionAvail().Y - 40f), false);
            if (managerNull) {
                ErrorMessage(managerNullMessage);
            }
            ImGui.EndChild();
        }


        private Vector2? GetSizeChange(float requiredY, float availableY, int Lines, Vector2? initial) {
            // Height
            if (availableY - requiredY * Lines < 1) {
                Vector2? newHeight = new Vector2(initial.Value.X, initial.Value.Y + requiredY * Lines);
                return newHeight;
            }
            return initial;
        }

        private void ErrorMessage(string message) {
            var requiredY = ImGui.CalcTextSize(message).Y + 1f;
            var availableY = ImGui.GetContentRegionAvail().Y;
            var initialH = ImGui.GetCursorPos().Y;
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextColored(new Vector4(1f, 0f, 0f, 1f), message);
            ImGui.PopTextWrapPos();
            var changedH = ImGui.GetCursorPos().Y;
            float textHeight = changedH - initialH;
            int textLines = (int)(textHeight / ImGui.GetTextLineHeight());

            // Check height and increase if necessarry
            if (availableY - requiredY * textLines < 1 && !SizeYChanged) {
                SizeYChanged = true;
                changedSize = GetSizeChange(requiredY, availableY, textLines, initialSize);
                Size = changedSize;
            }
        }

        internal class BetterComboBox {
            string _label = "";
            int _width = 0;
            int index = -1;
            int _lastIndex = 0;
            bool _enabled = true;
            string[] _contents = new string[1] { "" };
            public event EventHandler OnSelectedIndexChanged;
            public string Text { get { return index > -1 ? _contents[index] : ""; } }
            public BetterComboBox(string _label, string[] contents, int index, int width = 100) {
                if (Label != null) {
                    this._label = _label;
                }
                this._width = width;
                this.index = index;
                if (contents != null) {
                    this._contents = contents;
                }
            }

            public string[] Contents { get => _contents; set => _contents = value; }
            public int SelectedIndex { get => index; set => index = value; }
            public int Width { get => (_enabled ? _width : 0); set => _width = value; }
            public string Label { get => _label; set => _label = value; }
            public bool Enabled { get => _enabled; set => _enabled = value; }

            public void Draw() {
                if (_enabled) {
                    ImGui.SetNextItemWidth(_width);
                    if (_label != null && _contents != null) {
                        if (_contents.Length > 0) {
                            ImGui.Combo("##" + _label, ref index, _contents, _contents.Length);
                        }
                    }
                }
                if (index != _lastIndex) {
                    if (OnSelectedIndexChanged != null) {
                        OnSelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
                _lastIndex = index;
            }
        }


        private void InitializationWindow()
        {
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.Indent(150);
            ImGui.TextWrapped("Xiv Voices Initialization");
            ImGui.Unindent(140);
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.TextWrapped("Choose a working directory that will hold all the voice files in your computer, afterwards press \"Start\" to begin downloading Xiv Voices into your computer.");

            ImGui.Indent(35);
            ImGui.Dummy(new Vector2(0, 20));
            ImGui.Indent(65);

            //if (logoHandle != null)
                ImGui.Image(logoHandle, new Vector2(200, 200));
            //else
            //    ImGui.Dummy(new Vector2(200, 200));

            ImGui.TextWrapped("Working Directory is " + this.configuration.WorkingDirectory);
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.Unindent(30);

            // Get available drives
            DriveInfo[] allDrives = DriveInfo.GetDrives();
            List<string> drives = allDrives.Where(drive => drive.IsReady).Select(drive => drive.Name.Trim('\\')).ToList();
            string[] driveNames = drives.ToArray();
            int driveIndex = drives.IndexOf(selectedDrive);

            ImGui.Text("Select Drive:");
            if (ImGui.Combo("##Drives", ref driveIndex, driveNames, driveNames.Length))
            {
                selectedDrive = drives[driveIndex];
                this.configuration.WorkingDirectory = $"{selectedDrive}/XIV_Voices";
            }

            ImGui.Dummy(new Vector2(0, 50));

            if (ImGui.Button("Start Downloading Xiv Voices", new Vector2(260, 50)))
            {
                if(selectedDrive != string.Empty)
                    Updater.Instance.Check();
            }
            ImGui.Unindent(45);

        }

        private void UpdateWindow()
        {
            if (Updater.Instance.State.Contains(1))
            {
                ImGui.Dummy(new Vector2(0, 10));
                ImGui.TextWrapped("Checking Server Manifest...");
            }

            if (Updater.Instance.State.Contains(2))
            {
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.TextWrapped("Checking Local Manifest...");
            }

            if (Updater.Instance.State.Contains(3))
            {
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.TextWrapped("Checking Xiv Voices Tools...");
            }

            if (Updater.Instance.State.Contains(-1))
            {
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.TextWrapped("Error: Unable to load Manifests");
            }

            if (Updater.Instance.State.Contains(4))
            {
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.TextWrapped("Xiv Voices Tools are Ready.");
            }

            if (Updater.Instance.State.Contains(5))
            {
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.TextWrapped("Xiv Voices Tools Missing, Downloading..");
            }

            if (Updater.Instance.State.Contains(6))
            {
                ImGui.Dummy(new Vector2(0, 5));
                float progress = Updater.Instance.ToolsDownloadState / 100.0f;
                ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{Updater.Instance.ToolsDownloadState}% Complete");
                ImGui.Dummy(new Vector2(0, 5));
            }

            if (Updater.Instance.State.Contains(7))
            {
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.TextWrapped("All Voice Files are Up to Date");
            }

            if (Updater.Instance.State.Contains(8))
            {
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.TextWrapped("There is a new update, downloading...");
                if (Updater.Instance.State.Contains(9))
                {
                    ImGui.SameLine();
                    ImGui.Text(" " +Updater.Instance.DataDownloadCount + " files left");
                }
                ImGui.Dummy(new Vector2(0, 5));
            }

            if (Updater.Instance.State.Contains(9))
            {
                var downloadInfoSnapshot = Updater.Instance.DownloadInfoState.ToList();
                foreach (var item in downloadInfoSnapshot)
                {
                    ImGui.ProgressBar(item.percentage, new Vector2(-1, 0), $"{item.file} {item.status}");
                }
            }

            if (Updater.Instance.State.Contains(10))
            {
                ImGui.TextWrapped("Done Updating.");
            }
        }

        private void DrawGeneral() {
            ImGui.Unindent(8);
            if (ImGui.BeginChild("ScrollingRegion", new Vector2(360, -1), false, ImGuiWindowFlags.NoScrollbar))
            {
                ImGui.Columns(2, "ScrollingRegionColumns", false);
                ImGui.SetColumnWidth(0, 350);

                // START

                ImGui.Dummy(new Vector2(0, 10));
                ImGui.Indent(60);

                ImGui.Image(logoHandle, new Vector2(200, 200));

                // Working Directory
                ImGui.TextWrapped("Working Directory is " + this.configuration.WorkingDirectory);
                ImGui.Dummy(new Vector2(0, 10));

                // Data
                ImGui.Indent(10);
                ImGui.TextWrapped("NPCs:");
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.0f, 1.0f, 0.0f, 1.0f)); // Green color
                ImGui.TextWrapped(XivEngine.Instance.Database.Data["npcs"]);
                ImGui.PopStyleColor();
                ImGui.SameLine();

                ImGui.TextWrapped(" Voices:");
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.0f, 1.0f, 0.0f, 1.0f)); // Green color
                ImGui.TextWrapped(XivEngine.Instance.Database.Data["voices"]);
                ImGui.PopStyleColor();

                // Update Button
                ImGui.Unindent(70);
                ImGui.Dummy(new Vector2(0, 10));
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.25f, 1.0f)); // Gray color
                if (ImGui.Button("Click here to download the latest Voice Files", new Vector2(336, 60)))
                {
                    Updater.Instance.Check();
                }
                ImGui.PopStyleColor();

                // Xiv Voices Enabled
                ImGui.Dummy(new Vector2(0, 15));
                var activeValue = this.Configuration.Active;
                if (ImGui.Checkbox("##xivVoicesActive", ref activeValue))
                {
                    this.configuration.Active = activeValue;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Xiv Voices Enabled");

                /*

                // OnlineRequests
                ImGui.Dummy(new Vector2(0, 8));
                var onlineRequests = this.Configuration.OnlineRequests;
                if (ImGui.Checkbox("##onlineRequests", ref onlineRequests))
                {
                    this.configuration.OnlineRequests = onlineRequests;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Download missing lines individually if they exist");

                // Reports Enabled
                ImGui.Dummy(new Vector2(0, 8));
                var reports = this.Configuration.Reports;
                if (ImGui.Checkbox("##reports", ref reports))
                {
                    this.configuration.Reports = reports;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Report Missing Dialogues Automatically");
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.25f, 0.25f, 1.0f));
                ImGui.Text("( English lines only, do not enable for other languages )");
                ImGui.PopStyleColor();

                // AnnounceReports
                var announceReports = this.Configuration.AnnounceReports;
                if (ImGui.Checkbox("##announceReports", ref announceReports))
                {
                    this.configuration.AnnounceReports = announceReports;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Announce Reported Lines");

                */

                // END

                ImGui.Columns(1);
            }
            ImGui.Indent(8);

            // Saving Process
            if (needSave)
            {
                needSave = false;
                RequestSave();
            }
        }

        private void DrawSettings() {

            ImGui.Unindent(8);
            if (ImGui.BeginChild("ScrollingRegion", new Vector2(360, -1), false, ImGuiWindowFlags.NoScrollbar))
            {
                ImGui.Columns(2, "ScrollingRegionColumns", false);
                ImGui.SetColumnWidth(0, 350);

                // START

                // Chat Settings ----------------------------------------------
                ImGui.Dummy(new Vector2(0, 10));
                ImGui.TextWrapped("Chat Settings");
                ImGui.Dummy(new Vector2(0, 10));


                // SayEnabled
                var sayEnabled = this.Configuration.SayEnabled;
                if (ImGui.Checkbox("##sayEnabled", ref sayEnabled))
                {
                    this.configuration.SayEnabled = sayEnabled;
                    needSave = true;

                };
                ImGui.SameLine();
                ImGui.Text("Say Enabled");

                // TellEnabled
                var tellEnabled = this.Configuration.TellEnabled;
                if (ImGui.Checkbox("##tellEnabled", ref tellEnabled))
                {
                    this.configuration.TellEnabled = tellEnabled;
                    needSave = true;

                };
                ImGui.SameLine();
                ImGui.Text("Tell Enabled");

                // ShoutEnabled
                var shoutEnabled = this.Configuration.ShoutEnabled;
                if (ImGui.Checkbox("##shoutEnabled", ref shoutEnabled))
                {
                    this.configuration.ShoutEnabled = shoutEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Shout/Yell Enabled");

                // PartyEnabled
                var partyEnabled = this.Configuration.PartyEnabled;
                if (ImGui.Checkbox("##partyEnabled", ref partyEnabled))
                {
                    this.configuration.PartyEnabled = partyEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Party Enabled");

                // AllianceEnabled
                var allianceEnabled = this.Configuration.AllianceEnabled;
                if (ImGui.Checkbox("##allianceEnabled", ref allianceEnabled))
                {
                    this.configuration.AllianceEnabled = allianceEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Alliance Enabled");

                // FreeCompanyEnabled
                var freeCompanyEnabled = this.Configuration.FreeCompanyEnabled;
                if (ImGui.Checkbox("##freeCompanyEnabled", ref freeCompanyEnabled))
                {
                    this.configuration.FreeCompanyEnabled = freeCompanyEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Free Company Enabled");

                // LinkshellEnabled
                var linkshellEnabled = this.Configuration.LinkshellEnabled;
                if (ImGui.Checkbox("##linkshellEnabled", ref linkshellEnabled))
                {
                    this.configuration.LinkshellEnabled = linkshellEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Linkshell Enabled");

                // BattleDialoguesEnabled
                var battleDialoguesEnabled = this.Configuration.BattleDialoguesEnabled;
                if (ImGui.Checkbox("##battleDialoguesEnabled", ref battleDialoguesEnabled))
                {
                    this.configuration.BattleDialoguesEnabled = battleDialoguesEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Battle Dialogues Enabled");

                // RetainersEnabled
                var retainersEnabled = this.Configuration.RetainersEnabled;
                if (ImGui.Checkbox("##retainersEnabled", ref retainersEnabled))
                {
                    this.configuration.RetainersEnabled = retainersEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Retainers Enabled");

                // Bubble Settings ----------------------------------------------
                ImGui.Dummy(new Vector2(0, 10));
                ImGui.TextWrapped("Bubble Settings");
                ImGui.Dummy(new Vector2(0, 10));

                // BubblesEnabled
                var bubblesEnabled = this.Configuration.BubblesEnabled;
                if (ImGui.Checkbox("##bubblesEnabled", ref bubblesEnabled))
                {
                    this.configuration.BubblesEnabled = bubblesEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Chat Bubbles Enabled");

                ImGui.Indent(28);
                var nullcheck = false;
                // BubblesEverywhere
                var bubblesEverywhere = this.Configuration.BubblesEverywhere;
                if (this.Configuration.BubblesEnabled)
                {
                    if (ImGui.Checkbox("##bubblesEverywhere", ref bubblesEverywhere))
                    {
                        if (bubblesEverywhere)
                        {
                            this.configuration.BubblesEverywhere = bubblesEverywhere;
                            this.configuration.BubblesInSafeZones = !bubblesEverywhere;
                            this.configuration.BubblesInBattleZones = !bubblesEverywhere;
                            needSave = true;
                        }
                    };
                }
                else
                    ImGui.Checkbox("##null", ref nullcheck);
                ImGui.SameLine();
                ImGui.Text("Enable Bubbles Everywhere");

                // BubblesInSafeZones
                var bubblesOutOfBattlesOnly = this.Configuration.BubblesInSafeZones;
                if (this.Configuration.BubblesEnabled)
                {
                    if (ImGui.Checkbox("##bubblesOutOfBattlesOnly", ref bubblesOutOfBattlesOnly))
                    {
                        if (bubblesOutOfBattlesOnly)
                        {
                            this.configuration.BubblesEverywhere = !bubblesOutOfBattlesOnly;
                            this.configuration.BubblesInSafeZones = bubblesOutOfBattlesOnly;
                            this.configuration.BubblesInBattleZones = !bubblesOutOfBattlesOnly;
                            needSave = true;
                        }
                    };
                }
                else
                    ImGui.Checkbox("##null", ref nullcheck);
                ImGui.SameLine();
                ImGui.Text("Only Enable Chat Bubbles In Safe Zones");

                // BubblesInBattleZones
                var bubblesInBattlesOnly = this.Configuration.BubblesInBattleZones;
                if (this.Configuration.BubblesEnabled)
                {
                    if (ImGui.Checkbox("##bubblesInBattlesOnly", ref bubblesInBattlesOnly))
                    {
                        if (bubblesInBattlesOnly)
                        {
                            this.configuration.BubblesEverywhere = !bubblesInBattlesOnly;
                            this.configuration.BubblesInSafeZones = !bubblesInBattlesOnly;
                            this.configuration.BubblesInBattleZones = bubblesInBattlesOnly;
                            needSave = true;
                        }
                    };
                }
                else
                    ImGui.Checkbox("##null", ref nullcheck);
                ImGui.SameLine();
                ImGui.Text("Only Enable Chat Bubbles in Battle Zones");

                // BubbleChatEnabled
                var bubbleChatEnabled = this.Configuration.BubbleChatEnabled;
                if (ImGui.Checkbox("##bubbleChatEnabled", ref bubbleChatEnabled))
                {
                    this.configuration.BubbleChatEnabled = bubbleChatEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Print Bubbles in Chat");


                ImGui.Unindent(28);

                // Other Settings ----------------------------------------------

                ImGui.Dummy(new Vector2(0, 10));
                ImGui.TextWrapped("Other Settings");
                ImGui.Dummy(new Vector2(0, 10));

                // ReplaceVoicedARRCutscenes
                var replaceVoicedARRCutscenes = this.Configuration.ReplaceVoicedARRCutscenes;
                if (ImGui.Checkbox("##replaceVoicedARRCutscenes", ref replaceVoicedARRCutscenes))
                {
                    this.configuration.ReplaceVoicedARRCutscenes = replaceVoicedARRCutscenes;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Replace ARR Cutscenes");

                // SkipEnabled
                var skipEnabled = this.Configuration.SkipEnabled;
                if (ImGui.Checkbox("##interruptEnabled", ref skipEnabled))
                {
                    this.configuration.SkipEnabled = skipEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Dialogue Skip Enabled");



                // END

                ImGui.Columns(1);
            }
            ImGui.Indent(8);

            

            // Saving Process
            if (needSave)
            {
                needSave = false;
                RequestSave();
            }

        }

        private void AudioSettings()
        {
            ImGui.Unindent(8);
            if (ImGui.BeginChild("ScrollingRegion", new Vector2(360, -1), false, ImGuiWindowFlags.NoScrollbar))
            {
                ImGui.Columns(2, "ScrollingRegionColumns", false);
                ImGui.SetColumnWidth(0, 350);

                // START

                // Mute Button -----------------------------------------------

                ImGui.Dummy(new Vector2(0, 20));
                var mute = this.Configuration.Mute;
                if (ImGui.Checkbox("##mute", ref mute))
                {
                    this.configuration.Mute = mute;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Mute Enabled");

                // Lipsync Enabled -----------------------------------------------
                ImGui.Dummy(new Vector2(0, 10));
                var lipsyncEnabled = this.Configuration.LipsyncEnabled;
                if (ImGui.Checkbox("##lipsyncEnabled", ref lipsyncEnabled))
                {
                    this.configuration.LipsyncEnabled = lipsyncEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Lipsync Enabled");

                // Volume Slider ---------------------------------------------

                ImGui.Dummy(new Vector2(0, 20));
                ImGui.TextWrapped("Volume Control");
                int volume = this.Configuration.Volume;
                if (ImGui.SliderInt("##volumeSlider", ref volume, 0, 100, volume.ToString()))
                {
                    this.Configuration.Volume = volume;
                    needSave = true;
                }
                ImGui.SameLine();
                ImGui.Text("Volume");

                // Speed Slider ---------------------------------------------

                ImGui.Dummy(new Vector2(0, 20));
                ImGui.TextWrapped("Speed Control");
                int speed = this.Configuration.Speed;
                if (ImGui.SliderInt("##speedSlider", ref speed, 75, 150, speed.ToString()))
                {
                    this.Configuration.Speed = speed;
                    needSave = true;
                }
                ImGui.SameLine();
                ImGui.Text("Speed");

                // Playback Engine  ---------------------------------------------
                ImGui.Dummy(new Vector2(0, 20));
                ImGui.TextWrapped("Playback Engine");
                string[] audioEngines = new string[] { "DirectSound", "Wasapi", "WaveOut" };
                int currentEngine = this.Configuration.AudioEngine - 1;

                if (ImGui.Combo("##audioEngine", ref currentEngine, audioEngines, audioEngines.Length))
                {
                    this.Configuration.AudioEngine = currentEngine + 1;
                    needSave = true;
                }
                ImGui.SameLine();
                ImGui.Text("Engine");


                // Speed Slider ---------------------------------------------

                ImGui.Dummy(new Vector2(0, 30));
                ImGui.Unindent(15);
                ImGui.Separator();
                ImGui.Indent(15);

                // Local AI Settings Settings ----------------------------------------------

                ImGui.Dummy(new Vector2(0, 20));
                var localTTSEnabled = this.Configuration.LocalTTSEnabled;
                if (ImGui.Checkbox("##localTTSEnabled", ref localTTSEnabled))
                {
                    this.configuration.LocalTTSEnabled = localTTSEnabled;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Local TTS Enabled");

                ImGui.Indent(20);
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.Text("Local TTS Ungendered Voice:");
                ImGui.SameLine();
                var localTTSUngendered = this.Configuration.LocalTTSUngendered;
                string[] genders = { "Male", "Female" };
                ImGui.SetNextItemWidth(129);
                if (ImGui.Combo("##localTTSUngendered", ref localTTSUngendered, genders, genders.Length))
                {
                    this.Configuration.LocalTTSUngendered = localTTSUngendered;
                    needSave = true;
                } 


                // LocalTTS Volume Slider
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.Text("Volume:");
                ImGui.SameLine();
                int localTTSVolume = this.Configuration.LocalTTSVolume;
                if (ImGui.SliderInt("##localTTSVolumeSlider", ref localTTSVolume, 0, 100, localTTSVolume.ToString()))
                {
                    this.Configuration.LocalTTSVolume = localTTSVolume;
                    needSave = true;
                }

                ImGui.Dummy(new Vector2(0, 5));
                var localTTSPlayerSays = this.Configuration.LocalTTSPlayerSays;
                if (ImGui.Checkbox("##localTTSPlayerSays", ref localTTSPlayerSays))
                {
                    this.configuration.LocalTTSPlayerSays = localTTSPlayerSays;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Say Speaker Name in Chat");
                

                ImGui.Dummy(new Vector2(0, 5));
                var ignoreNarratorLines = this.Configuration.IgnoreNarratorLines;
                if (ImGui.Checkbox("##ignoreNarratorLines", ref ignoreNarratorLines))
                {
                    this.configuration.IgnoreNarratorLines = ignoreNarratorLines;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Ignore Narrator Lines");
                ImGui.Unindent(20);


                // END

                ImGui.Columns(1);
            }
            ImGui.Indent(8);


            // Saving Process
            if (needSave)
            {
                needSave = false;
                RequestSave();
            }

        }

        private void LogsSettings()
        {
            if (!configuration.Active)
            {
                ImGui.Dummy(new Vector2(0, 20));
                ImGui.TextWrapped("Xiv Voices is Disabled");
                ImGui.Dummy(new Vector2(0, 10));
            }
            else
            {
                ImGui.Unindent(8);
                // Begin a scrollable region
                if (ImGui.BeginChild("ScrollingRegion", new Vector2(360, -1), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
                {
                    ImGui.Columns(2, "ScrollingRegionColumns", false);
                    ImGui.SetColumnWidth(0, 350);

                    var audioInfoStateCopy = PluginReference.audio.AudioInfoState.ToList();
                    foreach (var item in audioInfoStateCopy)
                    {
                        // Show Dialogue Details (Name: Sentence)
                        ImGui.TextWrapped($"{item.data.Speaker}: {item.data.TtsData.Message}");

                        // Show Player Progress Bar
                        int progressSize = 253;
                        if (item.type == "xivv")
                            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.0f, 0.7f, 0.0f, 1.0f)); // RGBA: Full green
                        else if (item.type == "empty")
                        {
                            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.2f, 0.2f, 0.2f, 1.0f)); // RGBA: Full green
                            progressSize = 310;
                        }

                        if (XivEngine.Instance.Database.Access) progressSize -= 100;

                        ImGui.ProgressBar(item.percentage, new Vector2(progressSize, 24), $"{item.state}");
                        if (item.type == "xivv" || item.type == "empty")
                            ImGui.PopStyleColor();


                        if (item.type != "empty")
                        {
                            // Show Play and Stop Buttons
                            ImGui.SameLine();
                            if (item.state == "playing")
                            {
                                if (ImGui.Button("Stop", new Vector2(50, 24)))
                                    PluginReference.audio.StopAudio();
                            }
                            else
                            {
                                if (ImGui.Button($"Play##{item.id}", new Vector2(50, 24)))
                                {
                                    PluginReference.audio.StopAudio();
                                    item.data.Network = "Local";
                                    PluginReference.xivEngine.AddToQueue(item.data);
                                }
                            }
                        }
                        ImGui.Dummy(new Vector2(0, 10));

                    }

                    ImGui.Columns(1);
                }
                ImGui.Indent(8);
            }
        }

        private void Framework_General()
        {
            ImGui.Dummy(new Vector2(0, 10));
            var frameworkOnline = this.Configuration.FrameworkOnline;
            if (ImGui.Checkbox("##frameworkOnline", ref frameworkOnline))
            {
                this.configuration.FrameworkOnline = frameworkOnline;
                needSave = true;
            };
            ImGui.SameLine();
            ImGui.Text("Framework Enabled");

            // Saving Process
            if (needSave)
            {
                needSave = false;
                RequestSave();
            }
        }

        private void Framework_Unknown()
        {
            ImGui.Dummy(new Vector2(0, 10));
            if (ImGui.Button($"Load Unknown List##loadUnknownList", new Vector2(385, 25)))
            {
                XivEngine.Instance.UnknownList_Load();
            }

            ImGui.Dummy(new Vector2(0, 10));

            var unknownQueueSnapshot = XivEngine.Instance.Audio.unknownQueue.ToList();
            foreach (string item in unknownQueueSnapshot)
            {
                if (ImGui.BeginChild("unknownList"+item, new Vector2(275, 50), true))
                {
                    ImGui.Dummy(new Vector2(0, 5));

                    ImGui.Text(item);
                    ImGui.EndChild();
                }
                ImGui.SameLine();
                ImGui.SameLine();
                if (ImGui.Button("Run##unknowButton" + item, new Vector2(45, 50)))
                {
                    XivEngine.Instance.Database.Framework.Run(item);
                }
                ImGui.SameLine();
                if (ImGui.Button("Once##unknowButton" + item, new Vector2(45, 50)))
                {
                    XivEngine.Instance.Database.Framework.Run(item, true);
                }
            }

            // Saving Process
            if (needSave)
            {
                needSave = false;
                RequestSave();
            }
        }

        private void Changelog()
        {
            ImGui.Unindent(8);
            if (ImGui.BeginChild("ChangelogScrollingRegion", new Vector2(360, 592), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                ImGui.Columns(2, "ChangelogColumns", false);
                ImGui.SetColumnWidth(0, 350);

                if (ImGui.CollapsingHeader("Version 0.3.0.1", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed a major issue where some users crash due to Settings Window's saving functionality.");
                    ImGui.Bullet(); ImGui.TextWrapped("A few changes under the hood in preparation for moving the project to a new home.");
                }

                if (ImGui.CollapsingHeader("Version 0.3.0.0 (final)", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("XIVV will no longer sent reports for missing dialogues.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database with more Dawntrail NPCs.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.9.9"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Optimized UI.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database with more Dawntrail NPCs.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.9.8"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Added the option to Ignore Narrator Lines, it can be found under Local TTS in the Audio Settings.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added alternative names for Otis and Koana.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database with more Dawntrail NPCs.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.9.7"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Improved UI and made it more efficient, it should no longer cause a crash upon version update.");
                    ImGui.Bullet(); ImGui.TextWrapped("Bugfix: Late reports will no longer print the response.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database with more Dawntrail NPCs.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.9.6"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Hotfix: Late reports will no longer loop infinitely.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.9.5"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Added Nu Mou beast tribe to the Data Mapper.");
                    ImGui.Bullet(); ImGui.TextWrapped("Improved dialogue reports.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database with more Dawntrail NPCs.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.9.4"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database with more Dawntrail NPCs.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.9.3"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("More code optimization to never allow those crashing bugs inside our house again!");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database with more Dawntrail NPCs.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.9.2"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Another attempt to fix the lipsync crashing error.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.9.1"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Better optimization and error catching for future crashes.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.9.0"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed a bug in lipsync that causes some users to crash upon starting or ending some dialogues.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed a bug in the volume of bubble chats inside duties.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.8.9"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Users will no longer report a line they reported before.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed a bug where a line from Riol is seen as a retainer line.");
                    ImGui.Bullet(); ImGui.TextWrapped("Empty and non-verbal lines will not longer be processed.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated VoiceNames Databse for new NPCs with unique voices: Alayla, Hunmurruk, Koana, Otis.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database with more Dawntrail NPCs.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.8.8"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("To ensure that users get the latest Dawntrail voice lines as early as possible without waiting for the next update, an option has been added to allow users to automatically download and play any missing line they encounter that exists in the server but does not exist in their computer yet. You can enable this feature by checking \"Download missing lines individually\" in the General Settings.");
                    ImGui.Bullet(); ImGui.TextWrapped("Reports have been restructured in order to ensure a better and faster experience.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed an issue in the UI where buttons may not be as clickable as they used to be pre-Dawntrail.");
                    ImGui.Bullet(); ImGui.TextWrapped("Bugfix: reports and http requests will no longer cause users to crash randomly in rare instances.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database with more Dawntrail NPCs.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.8.7"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Data with more Dawntrail NPCs.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.8.6"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Data with more Dawntrail NPCs.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated Lexicon.");
                    ImGui.Bullet(); ImGui.TextWrapped("Minor Bugfixes.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.8.5"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Further Dawntrail Optimizations.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.8.4"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Lipsync is back!");
                    ImGui.Bullet(); ImGui.TextWrapped("Improved Lipsync animations to fit more variaties of dialogues long and short.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.8.3"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database for new Dawntrail NPCs!");
                }

                if (ImGui.CollapsingHeader("Version 0.2.8.2"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Chat Bubbles are back for DT!");
                }

                if (ImGui.CollapsingHeader("Version 0.2.8.1"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Updated XivVoices to work on the latest version of Dalamud for Dawntrail.");
                    ImGui.Bullet(); ImGui.TextWrapped("The following features have been disabled temporarily: Bubbles chat, Lipsync.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.8.0"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed a problem with retainers not being recognized when they have similar names to NPCs in the database.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated Player Chat Processing and added more shortcuts.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database for the upcoming Voice Files Update.");

                }

                if (ImGui.CollapsingHeader("Version 0.2.7.0"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Enhanced Redo and Mute Reports menu.");
                    ImGui.Bullet(); ImGui.TextWrapped("Improved Player Chat Processing with added shortcuts.");
                    ImGui.Bullet(); ImGui.TextWrapped("Enhanced dialogue processing for names like \"Light\" and \"Darkness\".");
                    ImGui.Bullet(); ImGui.TextWrapped("Improved handling of sentences with numbers (tickets, winners, etc.).");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database for upcoming Voice Files Update.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed issues with bubble volumes and report announcements.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated Lexicon and Player Chat Processing.");
                    ImGui.Bullet(); ImGui.TextWrapped("Voice Files Updater improved to avoid running during duties or events.");
                    ImGui.Bullet(); ImGui.TextWrapped("New option to disable announcing reported lines in chat.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.6.0"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Enhanced bubble dialogue volumes and improved beast tribe NPC recognition.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added NPCs and voices (Cait Sith, Gilgamesh, Genbu, Yozakura, etc.).");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC, Nameless Dialogues, and Retainers Databases.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed issues with sound effects and bubble speech volumes.");
                    ImGui.Bullet(); ImGui.TextWrapped("Improved logging and stability of the Audio Playing Engine.");
                    ImGui.Bullet(); ImGui.TextWrapped("Performance optimizations and utility updates.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.5.0"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Hotfix: Fixed a bug in the sound effects that stopped playing dialogues for Dragon.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database.");
                    ImGui.Bullet(); ImGui.TextWrapped("System audio player added.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated player chat processing.");
                    ImGui.Bullet(); ImGui.TextWrapped("Version check with audio notification.");
                    ImGui.Bullet(); ImGui.TextWrapped("More logging for debugging.");
                    ImGui.Bullet(); ImGui.TextWrapped("Hotfix: Linked Xiv Voices Enabled with New Update Notification.");
                    ImGui.Bullet(); ImGui.TextWrapped("New commands: /xivv volup, /xivv voldown.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added Wood Wailer Lance NPC.");
                    ImGui.Bullet(); ImGui.TextWrapped("Bugfix: Auto update crashing game on unstable connection.");
                    ImGui.Bullet(); ImGui.TextWrapped("Settings: Turn off New Update Audio Notification.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated player chat processing with party roles.");
                    ImGui.Bullet(); ImGui.TextWrapped("More logging for debugging.");
                    ImGui.Bullet(); ImGui.TextWrapped("Hotfix: More logging and crash handling.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.4.0"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Linked Bubble Dialogues to Volume Slider.");
                    ImGui.Bullet(); ImGui.TextWrapped("Dynamic dialogue processing for Feo Ul.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added Matanga, Giants, Skeletons to beast tribe mapper.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC Database for May's voice data.");
                    ImGui.Bullet(); ImGui.TextWrapped("Player LocalTTS handles emotions and case sensitivity.");
                    ImGui.Bullet(); ImGui.TextWrapped("Bugfix: Stopping lipsync crash.");
                    ImGui.Bullet(); ImGui.TextWrapped("New volume slider for Local TTS.");
                    ImGui.Bullet(); ImGui.TextWrapped("Improved Dialogue Skip functionality.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added Mamool Ja to Beast Tribe Mapping.");
                    ImGui.Bullet(); ImGui.TextWrapped("Bugfix: Anamnesis crash during loading.");
                    ImGui.Bullet(); ImGui.TextWrapped("New Audio Playback Engine option.");
                    ImGui.Bullet(); ImGui.TextWrapped("Dragon beast tribe voices added.");
                    ImGui.Bullet(); ImGui.TextWrapped("Player chat processing for Local TTS.");
                    ImGui.Bullet(); ImGui.TextWrapped("Hotfix: Bubble chat processing initialization.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fix for bubble chats initialization.");
                    ImGui.Bullet(); ImGui.TextWrapped("Player chat processing updates.");
                    ImGui.Bullet(); ImGui.TextWrapped("Potential fix for plugin-related crashes.");
                    ImGui.Bullet(); ImGui.TextWrapped("Performance Improvements.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated player chat processing.");
                    ImGui.Bullet(); ImGui.TextWrapped("Bubble message in chat shows NPC name.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed player name in party chat.");
                    ImGui.Bullet(); ImGui.TextWrapped("Option to say speaker's name for Local TTS.");
                    ImGui.Bullet(); ImGui.TextWrapped("Command /xivv skip added.");
                    ImGui.Bullet(); ImGui.TextWrapped("Alliance Chat added to Dialogue Settings.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed error caused by SayWhat Plugin.");
                    ImGui.Bullet(); ImGui.TextWrapped("Bubble chat printed as green NPC Dialogue.");
                    ImGui.Bullet(); ImGui.TextWrapped("Option to enable/disable Bubble chat printing.");
                    ImGui.Bullet(); ImGui.TextWrapped("Improved player chat processing for emoticons.");
                    ImGui.Bullet(); ImGui.TextWrapped("Job abbreviations read by Local TTS.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.3.0"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Linked Bubble Dialogues to the main Volume Slider.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added dynamic dialogue processing for Feo Ul.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added Matanga, Giants and Skeletons to the beast tribe mapper.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated the NPC Database to work with May's voice data update.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated Player LocalTTS to handle emotions such as \":)\"  \":(\"  \":D\"  \":C\"  \"XD\"  \">_<\"  \"^_^\" ...etc.");
                    ImGui.Bullet(); ImGui.TextWrapped("Bugfix: Fixed a crash related to stopping lipsync when it has already stopped.");
                    ImGui.Bullet(); ImGui.TextWrapped("Quick Bug Fix, don't worry about it :)");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated Player LocalTTS to handle case sensitivity.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated Player LocalTTS to ignore web links.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated Player LocalTTS to ignore coordinates in <pos>.");
                    ImGui.Bullet(); ImGui.TextWrapped("Player chat no longer skips NPC dialogues.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated retainers list to cover a mismatched dialogue for Male Viera Retainer.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated dialogue processing to handle names that end with a symbol.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated dialogue processing for players with their first name identical to their last name.");
                    ImGui.Bullet(); ImGui.TextWrapped("Implemented Player Chat Processing to Local TTS so messages like o/ are expressed better.");
                    ImGui.Bullet(); ImGui.TextWrapped("Implemented Lexicon Processing to Local TTS.");
                    ImGui.Bullet(); ImGui.TextWrapped("Hotfix: Fixed a crash related to the process being unavailable during initialization.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added a separate volume slider for Local TTS.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed concurrent modification issue in LogsSettings.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed a bug where the stop button doesn't work if you don't have Skip enabled.");
                    ImGui.Bullet(); ImGui.TextWrapped("Audio Logs now have the unfiltered version of the dialogue with the user's name in it.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed a bug related to the Nameless Database causing a crash.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added commands /xivv mute and /xivv unmute.");
                    ImGui.Bullet(); ImGui.TextWrapped("Slight UI Improvements for better visuals.");
                    ImGui.Bullet(); ImGui.TextWrapped("Improved the Dialogue Skip so skipping quickly works every time.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added Mamool Ja to the Beast Tribe Mapping Process.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed an issue related to Anamnesis that causes a crash during loading to some users.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added Linkshell to the list of chat dialogues.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added the option to enable and disable Linkshell messages in the Dialogue Settings.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.2.0"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Implemented a new initialization routine that waits for the game process to be fully responsive before starting operations.");
                    ImGui.Bullet(); ImGui.TextWrapped("Modified the service initialization to verify the game's stability.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added error handling around memory read operations.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed and improved Retainers List recognition.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated NPC, Nameless, and Beast Tribe data.");
                    ImGui.Bullet(); ImGui.TextWrapped("Implemented a system to process dialogues from similar-named NPCs with different looks.");
                    ImGui.Bullet(); ImGui.TextWrapped("Redesigned the UI to use vertical tabs with icons and hover text.");
                    ImGui.Bullet(); ImGui.TextWrapped("Audio Logs now show up to 100 dialogues in a scrollable window.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed bubbles' positional audio skipping the first word of each sentence.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed volume slider issues affecting game audio.");
                    ImGui.Bullet(); ImGui.TextWrapped("Hotfix: Fixed a bug where 'Retainer Enabled' doesn't block retainers' dialogues.");
                    ImGui.Bullet(); ImGui.TextWrapped("Hotfix: Fixed repeated dialogues playing simultaneously.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added new menu window 'Changelog' to keep users informed about updates.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added a Cactpot variant sentence for prize-winning.");
                    ImGui.Bullet(); ImGui.TextWrapped("Changed filename processing to remove accented letters.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.1.0"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("Improved quality of reports.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added an option to choose the playback engine for users having trouble with the audio player.");
                    ImGui.Bullet(); ImGui.TextWrapped("Improved data fetching and added skeleton mapper for beast tribes.");
                    ImGui.Bullet(); ImGui.TextWrapped("Report Button in the Audio Logs changes to Mute Button for Local TTS.");
                    ImGui.Bullet(); ImGui.TextWrapped("Fixed an issue with reports being sent with the player's name incorrectly.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added a dialogue queue for correct order of dialogues.");
                    ImGui.Bullet(); ImGui.TextWrapped("Updated how retainers' dialogues are processed.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added 'Report Missing Dialogues Automatically' option.");
                }

                if (ImGui.CollapsingHeader("Version 0.2.0.0"))
                {
                    ImGui.Bullet(); ImGui.TextWrapped("The plugin no longer needs the app to work; it can now run the entire process of Xiv Voices alone.");
                    ImGui.Bullet(); ImGui.TextWrapped("Feel free to delete everything in the Xiv Voices folder except for the Data folder.");
                    ImGui.Bullet(); ImGui.TextWrapped("The plugin can now play dialogue audio files directly.");
                    ImGui.Bullet(); ImGui.TextWrapped("The plugin can now report missing and unknown dialogues.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added new tabs to the plugin config window: Dialogue Settings, Audio Settings, Audio Logs.");
                    ImGui.Bullet(); ImGui.TextWrapped("Users can change the volume and playback speed from the Audio Settings.");
                    ImGui.Bullet(); ImGui.TextWrapped("Local TTS can be enabled or disabled from the Audio Settings.");
                    ImGui.Bullet(); ImGui.TextWrapped("Introduced two Local TTS voices, one for each gender.");
                    ImGui.Bullet(); ImGui.TextWrapped("Dialogue history is now available in the Audio Logs, with options to replay or report dialogues.");
                    ImGui.Bullet(); ImGui.TextWrapped("Websocket connection has been disabled.");
                    ImGui.Bullet(); ImGui.TextWrapped("Bubbles now have position audio that changes based on the user's position relative to the NPC.");
                    ImGui.Bullet(); ImGui.TextWrapped("General Tab in the config window includes an option to download the latest tools and audio files.");
                    ImGui.Bullet(); ImGui.TextWrapped("The process of downloading audio files has been made faster and more efficient.");
                    ImGui.Bullet(); ImGui.TextWrapped("Added an initialization process for first-time users to select the drive for installing audio files.");
                }

                ImGui.Columns(1);
                ImGui.EndChild();
            }
            ImGui.Indent(8);
        }

        private void Framework_Audio()
        {
            ImGui.Dummy(new Vector2(0, 10));
            if (ImGui.BeginChild("frameworkAudio", new Vector2(385, 90), true))
            {
                // Player Name
                ImGui.Dummy(new Vector2(130, 0));
                ImGui.SameLine();
                string playerName = XivEngine.Instance.Database.PlayerName;
                ImGui.SetNextItemWidth(112);
                if (ImGui.InputText("##playerName", ref playerName, 100))
                {
                    XivEngine.Instance.Database.PlayerName = playerName;
                    needSave = true;
                }
                ImGui.SameLine();
                var forcePlayerName = XivEngine.Instance.Database.ForcePlayerName;
                if (ImGui.Checkbox("##forcePlayerName", ref forcePlayerName))
                {
                    XivEngine.Instance.Database.ForcePlayerName = forcePlayerName;
                    needSave = true;
                };
                ImGui.SameLine();
                ImGui.Text("Force Name");

                // Full Sentence
                ImGui.Dummy(new Vector2(0, 3));
                ImGui.Dummy(new Vector2(3, 0));
                ImGui.SameLine();
                string wholeSentence = XivEngine.Instance.Database.WholeSentence;
                ImGui.SetNextItemWidth(240);
                if (ImGui.InputText("##wholeSentence", ref wholeSentence, 200))
                {
                    XivEngine.Instance.Database.WholeSentence = wholeSentence;
                }
                ImGui.SameLine();
                var forceWholeSentence = XivEngine.Instance.Database.ForceWholeSentence;
                if (ImGui.Checkbox("##forceWholeSentence", ref forceWholeSentence))
                {
                    XivEngine.Instance.Database.ForceWholeSentence = forceWholeSentence;
                };
                ImGui.SameLine();
                ImGui.Text("Sentence");

                ImGui.EndChild();
            }

            foreach (var item in PluginReference.audio.AudioInfoState.Take(6))
            {
                // Show Dialogue Details (Name: Sentence)
                if (ImGui.BeginChild(item.id, new Vector2(385, 43), false))
                {
                    float textHeight = ImGui.CalcTextSize($"{item.data.Speaker}: {item.data.Sentence}", 340.0f).Y;
                    float paddingHeight = Math.Max(35 - textHeight, 0);
                    ImGui.Dummy(new Vector2(1, 3));
                    if (paddingHeight > 3)
                        ImGui.Dummy(new Vector2(1, paddingHeight - 3));

                    ImGui.TextWrapped($"{item.data.Speaker}: {item.data.Sentence}");
                    ImGui.EndChild();
                }

                // Show Player Progress Bar
                int progressSize = 265;
                if (item.type == "xivv")
                    ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.0f, 0.7f, 0.0f, 1.0f)); // RGBA: Full green
                else if (item.type == "empty")
                {
                    ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.2f, 0.2f, 0.2f, 1.0f)); // RGBA: Full green
                    progressSize = 380;
                }
                ImGui.ProgressBar(item.percentage, new Vector2(progressSize, 24), $"{item.state}");
                if (item.type == "xivv" || item.type == "empty")
                    ImGui.PopStyleColor();


                if (item.type != "empty")
                {

                    // Show Play and Stop Buttons
                    ImGui.SameLine();
                    if (item.state == "playing")
                    {
                        if (ImGui.Button("Stop", new Vector2(50, 24)))
                            PluginReference.audio.StopAudio();
                    }
                    else
                    {
                        if (ImGui.Button($"Play##{item.id}", new Vector2(50, 24)))
                        {
                            PluginReference.audio.StopAudio();
                            item.data.Network = "Local";
                            PluginReference.xivEngine.AddToQueue(item.data);
                        }
                    }
                }

            }

            ImGui.Dummy(new Vector2(1, 1));
            ImGui.TextWrapped($"Files: {XivEngine.Instance.Database.Framework.Queue.Count}");

            // Saving Process
            if (needSave)
            {
                needSave = false;
                RequestSave();
            }
        }

        public void Dispose()
        {
            changelogTexture?.Dispose();
            changelogActiveTexture?.Dispose();
            generalSettingsTexture?.Dispose();
            generalSettingsActiveTexture?.Dispose();
            dialogueSettingsTexture?.Dispose();
            dialogueSettingsActiveTexture?.Dispose();
            audioSettingsTexture?.Dispose();
            audioSettingsActiveTexture?.Dispose();
            archiveTexture?.Dispose();
            archiveActiveTexture?.Dispose();
            discordTexture?.Dispose();
            koFiTexture?.Dispose();
            iconTexture?.Dispose();
            logoTexture?.Dispose();

            clientState.Login -= ClientState_Login;
            clientState.Logout -= ClientState_Logout;
        }

        public class MessageEventArgs : EventArgs {
            string message;

            public string Message { get => message; set => message = value; }
        }
    }
}
