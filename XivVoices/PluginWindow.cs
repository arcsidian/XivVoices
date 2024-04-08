using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Numerics;
using System.Diagnostics;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace XivVoices {
    public class PluginWindow : Window {
        private Configuration configuration;
        private IClientState clientState;

        private string _port;

        private string managerNullMessage = string.Empty;
        private bool SizeYChanged = false;
        private bool managerNull;
        private Vector2? initialSize;
        private Vector2? changedSize;

        public PluginWindow() : base("Xiv Voices by Arcsidian") {
            //IsOpen = true;
            Size = new Vector2(350, 600);
            initialSize = Size;
            SizeCondition = ImGuiCond.Always;
            Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize;
        }

        public Configuration Configuration {
            get => configuration;
            set {
                configuration = value;
                if (configuration != null) {
                    _port = configuration.Port;
                }
            }
        }

        public DalamudPluginInterface PluginInterface { get; internal set; }

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
                //DrawGeneral();
                
                if (ImGui.BeginTabBar("ConfigTabs")) {
                    if (ImGui.BeginTabItem("General")) {
                        DrawGeneral();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Settings")) {
                        DrawSettings();
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
                DrawErrors();
                SaveAndClose();
            } else {
                ImGui.TextUnformatted("Please login to access and configure settings.");
            }
        }

        private static readonly List<string> ValidTextureExtensions = new List<string>(){
          ".png",
        };

        private void SaveAndClose() {
            var originPos = ImGui.GetCursorPos();
            
            // Place save button in bottom left + some padding / extra space
            ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMax().X + 10f);
            ImGui.SetCursorPosY(ImGui.GetWindowContentRegionMax().Y - ImGui.GetFrameHeight() - 10f);
            if (ImGui.Button("Ko-Fi")) {
                //Save();
                Process process = new Process();
                try
                {
                    // true is the default, but it is important not to set it to false
                    process.StartInfo.UseShellExecute = true;
                    process.StartInfo.FileName = "https://ko-fi.com/arcsidian";
                    process.Start();
                }
                catch (Exception e)
                {

                }
            }
            ImGui.SetCursorPos(originPos);
            // Place close button in bottom right + some padding / extra space
            ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize("Close").X - 20f);
            ImGui.SetCursorPosY(ImGui.GetWindowContentRegionMax().Y - ImGui.GetFrameHeight() - 10f);
            if (ImGui.Button("Close")) {
                SizeYChanged = false;
                changedSize = null;
                Size = initialSize;
                IsOpen = false;
            }
            ImGui.SetCursorPos(originPos);
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

        private void DrawGeneral() {
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.Indent(65);
            if (this.PluginReference?.Logo?.ImGuiHandle != IntPtr.Zero)
                ImGui.Image(this.PluginReference.Logo.ImGuiHandle, new Vector2(this.PluginReference.Logo.Width, this.PluginReference.Logo.Height));
            ImGui.Unindent(65);
            ImGui.Dummy(new Vector2(0, 10));

            if (ImGui.Button(" remember to download Xiv Voices\nsoftware and connect it to this plugin", new Vector2(ImGui.GetWindowSize().X - 10, 60)))
            {
                Process process = new Process();
                try
                {
                    // true is the default, but it is important not to set it to false
                    process.StartInfo.UseShellExecute = true;
                    process.StartInfo.FileName = "https://arcsidian.com/xivv";
                    process.Start();
                }
                catch (Exception e)
                {

                }
            }
            
            ImGui.Dummy(new Vector2(0, 10));
            var activeValue = this.Configuration.Active;
            if (ImGui.Checkbox("##xivVoicesActive", ref activeValue)){
                this.configuration.Active = activeValue;
                this.configuration.Save();
            };
            ImGui.SameLine();
            ImGui.Text("Xiv Voices Enabled");

            var lipsyncEnabled = this.Configuration.LipsyncEnabled;
            if (ImGui.Checkbox("##lipsyncEnabled", ref lipsyncEnabled))
            {
                this.configuration.LipsyncEnabled = lipsyncEnabled;
                this.configuration.Save();
            };
            ImGui.SameLine();
            ImGui.Text("Lipsync Enabled");

            ImGui.Dummy(new Vector2(0, 10));
            ImGui.LabelText("##Label", "Websocket Settings");
            ImGui.InputText("##port", ref _port, 5);
            ImGui.SameLine();
            if (ImGui.Button("Restart"))
            {
                this.configuration.Port = _port;
                this.configuration.Save();
                PluginReference.webSocketServer.Stop();
                PluginReference.webSocketServer.Connect();
            }
            ImGui.TextWrapped(this.configuration.WebsocketStatus);
            ImGui.Dummy(new Vector2(0, 10));
        }

        private void DrawSettings() {
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.TextWrapped("Chat Settings");
            ImGui.Dummy(new Vector2(0, 10));

            // SayEnabled
            var sayEnabled = this.Configuration.SayEnabled;
            if (ImGui.Checkbox("##sayEnabled", ref sayEnabled))
            {
                this.configuration.SayEnabled = sayEnabled;
                this.configuration.Save();
            };
            ImGui.SameLine();
            ImGui.Text("Say Enabled");

            // TellEnabled
            var tellEnabled = this.Configuration.TellEnabled;
            if (ImGui.Checkbox("##tellEnabled", ref tellEnabled))
            {
                this.configuration.TellEnabled = tellEnabled;
                this.configuration.Save();
            };
            ImGui.SameLine();
            ImGui.Text("Tell Enabled");

            // ShoutEnabled
            var shoutEnabled = this.Configuration.ShoutEnabled;
            if (ImGui.Checkbox("##shoutEnabled", ref shoutEnabled))
            {
                this.configuration.ShoutEnabled = shoutEnabled;
                this.configuration.Save();
            };
            ImGui.SameLine();
            ImGui.Text("Shout/Yell Enabled");

            // PartyEnabled
            var partyEnabled = this.Configuration.PartyEnabled;
            if (ImGui.Checkbox("##partyEnabled", ref partyEnabled))
            {
                this.configuration.PartyEnabled = partyEnabled;
                this.configuration.Save();
            };
            ImGui.SameLine();
            ImGui.Text("Party Enabled");

            // FreeCompanyEnabled
            var freeCompanyEnabled = this.Configuration.FreeCompanyEnabled;
            if (ImGui.Checkbox("##freeCompanyEnabled", ref freeCompanyEnabled))
            {
                this.configuration.FreeCompanyEnabled = freeCompanyEnabled;
                this.configuration.Save();
            };
            ImGui.SameLine();
            ImGui.Text("Free Company Enabled");

            // BattleDialoguesEnabled
            var battleDialoguesEnabled = this.Configuration.BattleDialoguesEnabled;
            if (ImGui.Checkbox("##battleDialoguesEnabled", ref battleDialoguesEnabled))
            {
                this.configuration.BattleDialoguesEnabled = battleDialoguesEnabled;
                this.configuration.Save();
            };
            ImGui.SameLine();
            ImGui.Text("Battle Dialogues Enabled");

            // BubblesEnabled
            var bubblesEnabled = this.Configuration.BubblesEnabled;
            if (ImGui.Checkbox("##bubblesEnabled", ref bubblesEnabled))
            {
                this.configuration.BubblesEnabled = bubblesEnabled;
                this.configuration.Save();
            };
            ImGui.SameLine();
            ImGui.Text("Chat Bubbles Enabled");

            ImGui.Dummy(new Vector2(0, 10));
            ImGui.TextWrapped("Other Settings");
            ImGui.Dummy(new Vector2(0, 10));

            // ReplaceVoicedARRCutscenes
            var replaceVoicedARRCutscenes = this.Configuration.ReplaceVoicedARRCutscenes;
            if (ImGui.Checkbox("##replaceVoicedARRCutscenes", ref replaceVoicedARRCutscenes))
            {
                this.configuration.ReplaceVoicedARRCutscenes = replaceVoicedARRCutscenes;
                this.configuration.Save();
            };
            ImGui.SameLine();
            ImGui.Text("Replace ARR Cutscenes");

            // SkipEnabled
            var skipEnabled = this.Configuration.SkipEnabled;
            if (ImGui.Checkbox("##interruptEnabled", ref skipEnabled))
            {
                this.configuration.SkipEnabled = skipEnabled;
                this.configuration.Save();
            };
            ImGui.SameLine();
            ImGui.Text("Dialogue Skip Enabled");


            //ImGui.SetNextItemWidth(ImGui.GetContentRegionMax().X);
        }


        public class MessageEventArgs : EventArgs {
            string message;

            public string Message { get => message; set => message = value; }
        }
    }
}
