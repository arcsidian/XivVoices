using Dalamud.Game.ClientState.Objects.Types;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using WebSocketSharp;
using WebSocketSharp.Server;

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

        public XIVVWebSocketServer(Configuration configuration, Plugin plugin)
        {
            this.Configuration = configuration;
            this.Plugin = plugin;
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

        private void UpdateConfigStatus(string status)
        {
            this.Configuration.WebsocketStatus = status;
            this.Configuration.Save();
        }
    }

}
