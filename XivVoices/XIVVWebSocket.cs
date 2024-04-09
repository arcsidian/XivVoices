using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Security;
using WebSocketSharp;
using WebSocketSharp.Server;
using XivCommon.Functions;
using XivVoices.Voice;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace XivVoices
{
    public class XivVoicesWebSocketService : WebSocketBehavior
    {
        public static event Action<string> MessageReceived;

        protected override void OnMessage(MessageEventArgs e)
        {
            MessageReceived?.Invoke(e.Data);
        }
    }

    public class XIVVWebSocketServer
    {
        private Plugin Plugin;
        private Configuration Configuration;
        private WebSocketServer _wss;

        Dictionary<string, Character> characterList = new Dictionary<string, Character>();

        public XIVVWebSocketServer(Configuration configuration, Plugin plugin)
        {
            this.Configuration = configuration;
            this.Plugin = plugin;
            XivVoicesWebSocketService.MessageReceived += OnMessageReceived;
            Connect();
        }

        public void Connect(bool reconnect = false)
        {
            if (reconnect && _wss.IsListening)
            {
                _wss.Stop();
            }

            _wss = new WebSocketServer(System.Net.IPAddress.Any, int.Parse(this.Configuration.Port));
            _wss.AddWebSocketService<XivVoicesWebSocketService>("/XivVoices");
            _wss.Start();

            UpdateConfigStatus($"Listening on ws://localhost:{this.Configuration.Port}/XivVoices");
        }

        public void Stop()
        {
            _wss.Stop();
            Console.WriteLine("WebSocket Server stopped.");
            UpdateConfigStatus("WebSocket Server stopped.");
        }

        public void SendMessage(string message)
        {
            _wss.WebSocketServices["/XivVoices"].Sessions.Broadcast(message);
        }

        public void BroadcastMessage(string type, string speaker, string npcID, string message, string body, string gender, string race, string tribe, string eyes, string language, string position, Character character)
        {
            string index = "none";
            if (character != null)
            {
                Random rnd = new Random();
                int randomNumber = rnd.Next(0, 10000);
                string formattedNumber = randomNumber.ToString("D4");

                index = speaker + formattedNumber;
                if (!characterList.ContainsKey(index))
                    characterList.Add(index, character);
            }
            string user = $"{this.Plugin.ClientState.LocalPlayer.Name}@{this.Plugin.ClientState.LocalPlayer.HomeWorld.GameData.Name}";

            // Remove known suffixes from speaker name
            var suffixes = new string[] { "'s Voice", "'s Avatar" };
            foreach (var suffix in suffixes)
            {
                if (speaker.EndsWith(suffix))
                {
                    speaker = speaker.Substring(0, speaker.Length - suffix.Length);
                    break;
                }
            }


            var dataToSend = $"{{\"Type\":\"{type}\",\"Speaker\":\"{speaker}\",\"NpcID\":\"{npcID}\",\"Message\":\"{message}\",\"Body\":\"{body}\",\"Gender\":\"{gender}\",\"Race\":\"{race}\",\"Tribe\":\"{tribe}\",\"Eyes\":\"{eyes}\",\"Language\":\"{language}\",\"Position\":\"{position}\",\"Character\":\"{index}\",\"User\":\"{user}\"}}";
            //this.Plugin.Chat.Print("Websocket Sent: " + dataToSend);
            _wss.WebSocketServices["/XivVoices"].Sessions.Broadcast(dataToSend);
        }

        private void OnMessageReceived(string data)
        {
            XivvData ttsData = JsonConvert.DeserializeObject<XivvData>(data);
#if DEBUG
            this.Plugin.Chat.Print("Websocket Received: " + ttsData.Type + " " + ttsData.Character + " " + ttsData.Data);
#endif


            if (ttsData.Type == "Start")
            {
#if DEBUG
                this.Plugin.Chat.Print($"characterList has {characterList.Count}");

                if (characterList.ContainsKey(ttsData.Character))
                    this.Plugin.Chat.Print("found the character");
                else
                    this.Plugin.Chat.Print("did not find it");
#endif

                this.Plugin.TriggerLipSync(characterList[ttsData.Character], ttsData.Data);
            }
            else if (ttsData.Type == "Stop")
            {
                if (characterList.ContainsKey(ttsData.Character))
                {
                    this.Plugin.StopLipSync(characterList[ttsData.Character]);
                    characterList.Remove(ttsData.Character);
                }
            }
            
        }

        private void UpdateConfigStatus(string status)
        {
            this.Configuration.WebsocketStatus = status;
            this.Configuration.Save();
        }
    }

    [System.Serializable]
    public class XivvData
    {
        public XivvData() { }

        public string Type;
        public string Character;
        public string Data;
    }
}
