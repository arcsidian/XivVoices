using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Shader;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace XivVoices.Engine
{
    public class Audio
    {
        private bool audioIsStopped = false;
        public bool AudioIsPlaying { get; set; } = false;

        private Plugin Plugin;
        public Queue<string> bubbleQueue { get; set; }
        public LinkedList<AudioInfo> AudioInfoState { get; set; }

        public Audio(Plugin plugin)
        {
            this.Plugin = plugin;
            Plugin.Chat.Print("Audio: I am awake");
            AudioInfoState = new LinkedList<AudioInfo>();
            bubbleQueue = new Queue<string>();

        }

        public void Dispose()
        {
            AudioInfoState.Clear();
            AudioInfoState = null;
            bubbleQueue.Clear();
            bubbleQueue = null;
        }

        public async Task PlayAudio(XivMessage xivMessage, WaveStream waveStream)
        {
            
            while (AudioIsPlaying)
                await Task.Delay(50);

            AudioIsPlaying = true;
            audioIsStopped = false;

            var volumeProvider = new VolumeSampleProvider(waveStream.ToSampleProvider());
            var audioInfo = GetAudioInfo(xivMessage);

            Plugin.TriggerLipSync(xivMessage.TtsData.Character, waveStream.TotalTime.TotalSeconds.ToString());
            using (var audioOutput = new WaveOut())
            {
                audioOutput.Init(volumeProvider);
                audioOutput.Play();
                audioInfo.state = "playing";
                volumeProvider.Volume = (float)Plugin.Config.Volume/100f;
                var totalDuration = waveStream.TotalTime.TotalMilliseconds;
                while (audioOutput.PlaybackState == PlaybackState.Playing)
                {
                    var currentPosition = waveStream.CurrentTime.TotalMilliseconds;
                    audioInfo.percentage = (float)(currentPosition / totalDuration);
                    volumeProvider.Volume = (float)Plugin.Config.Volume / 100f;
                    if (audioIsStopped)
                    {
                        audioOutput.Stop();
                        Plugin.StopLipSync(xivMessage.TtsData.Character);
                        break;
                    }
                    await Task.Delay(50);
                }
                audioInfo.state = "stopped";
                audioInfo.percentage = 1f;
            }
            waveStream?.Dispose();
            audioIsStopped = false;
            AudioIsPlaying = false;
        }

        public async Task PlayBubble(XivMessage xivMessage, WaveStream waveStream)
        {
            bool initialization = false;

            while (true)
            {
                if (!initialization)
                {
                    bubbleQueue.Enqueue(xivMessage.NpcId+xivMessage.Sentence);
                    initialization = true;
                    Plugin.Chat.Print("Waiting:" + xivMessage.NpcId + xivMessage.Sentence);
                }

                if (bubbleQueue.Peek() == xivMessage.NpcId + xivMessage.Sentence)
                    break;

                await Task.Delay(30);
            }

            Plugin.Chat.Print("Free:" + xivMessage.NpcId + xivMessage.Sentence);

            try
            {
                var volumeProvider = new VolumeSampleProvider(waveStream.ToSampleProvider());
                PanningSampleProvider panningProvider = new PanningSampleProvider(volumeProvider);

                var audioInfo = GetAudioInfo(xivMessage);

                using (var audioOutput = new WaveOut())
                {
                    audioOutput.Init(panningProvider);
                    var data = GetDistanceAndBalance(xivMessage.TtsData.Position);
                    volumeProvider.Volume = AdjustVolume(data.Distance);
                    panningProvider.Pan = data.Balance;
                    audioOutput.Play();
                    audioInfo.state = "playing";
                    var totalDuration = waveStream.TotalTime.TotalMilliseconds;
                    while (audioOutput.PlaybackState == PlaybackState.Playing)
                    {
                        var currentPosition = waveStream.CurrentTime.TotalMilliseconds;
                        audioInfo.percentage = (float)(currentPosition / totalDuration);
                        data = GetDistanceAndBalance(xivMessage.TtsData.Position);
                        volumeProvider.Volume = AdjustVolume(data.Distance);
                        panningProvider.Pan = data.Balance;

                        /* Testing In a Loop
                        Plugin.Chat.Print("distance:" + data.Distance);
                        Plugin.Chat.Print("audioOutput.Volume:" + audioOutput.Volume);

                        if (audioIsStopped)
                        {
                            audioOutput.Stop();
                            break;
                        }
                        if (waveStream.Position > waveStream.Length - 100)
                            waveStream.Position = 0;
                        */


                        await Task.Delay(50);
                    }
                    audioInfo.state = "stopped";
                    audioInfo.percentage = 1f;
                    bubbleQueue.Dequeue();
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions that may occur during playback
                waveStream?.Dispose();
                Plugin.Chat.PrintError("Error during audio playback. " + ex);
            }
            finally
            {
                waveStream?.Dispose();
            }
        }


        float AdjustVolume(float distance)
        {
            (float distanceStart, float distanceEnd, float volumeStart, float volumeEnd)[] volumeRanges =
            {
                (0f, 3f, 1f, 0.85f),   // 0 to 3 units: 100% to 85%
                (3f, 5f, 0.85f, 0.3f),   // 3 to 5 units: 85% to 30% 
                (5f, 20f, 0.3f, 0.05f)     // 5 to 20 units: 30% to 5%
            };

            foreach (var range in volumeRanges)
            {
                if (distance >= range.distanceStart && distance <= range.distanceEnd)
                {
                    float slope = (range.volumeEnd - range.volumeStart) / (range.distanceEnd - range.distanceStart);
                    float yIntercept = range.volumeStart - slope * range.distanceStart;
                    float volume = slope * distance + yIntercept;
                    return Math.Clamp(volume, Math.Min(range.volumeStart, range.volumeEnd), Math.Max(range.volumeStart, range.volumeEnd));
                }
            }
            return volumeRanges[^1].volumeEnd;
        }

        AudioInfo GetAudioInfo(XivMessage xivMessage)
        {
            string id = $"{xivMessage.Speaker}_{xivMessage.Sentence}";
            id = Regex.Replace(id, @"[^a-zA-Z0-9 _-]", "").Replace(" ", "_").Replace("-", "_");
            var audioInfo = AudioInfoState.FirstOrDefault(ai => ai.id == id);
            if (audioInfo == null)
            {
                audioInfo = new AudioInfo(id, "new", 0f, xivMessage);
                AudioInfoState.AddFirst(audioInfo);
            }
            else
            {
                AudioInfoState.Remove(audioInfo);
                AudioInfoState.AddFirst(audioInfo);
                audioInfo.state = "new";
            }
            if (AudioInfoState.Count > 7)
            {
                var oldestFinished = AudioInfoState.LastOrDefault(ddi => ddi.state == "stopped");
                if (oldestFinished != null)
                    AudioInfoState.Remove(oldestFinished);
            }
            return audioInfo;
        }

        (float Distance,float Balance) GetDistanceAndBalance(Vector3 speakerPosition)
        {
            // Update camera vectors
            Vector3 cameraForward = Vector3.Normalize(Plugin.PlayerCamera.Forward);
            Vector3 cameraUp = Vector3.Normalize(Plugin.PlayerCamera.Top);
            Vector3 cameraRight = Vector3.Normalize(Vector3.Cross(cameraUp, cameraForward));

            // Calculate relative position from player to speaker
            Vector3 relativePosition = speakerPosition - Plugin.ClientState.LocalPlayer.Position;

            // Distance for volume adjustment
            float distance = relativePosition.Length();
            //volumeProvider.Volume = AdjustVolume(distance);


            // Direction for stereo balance
            float dotProduct = Vector3.Dot(relativePosition, cameraRight);
            float balance = Math.Clamp(dotProduct / 20, -1, 1); // Normalize and clamp the value for balance
            //panningProvider.Pan = balance; // Set stereo balance based on direction

            return (Distance: distance, Balance: balance);
        }

        public void StopAudio()
        {
            audioIsStopped = true;
        }

    }
}
