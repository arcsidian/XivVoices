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
        List<Character> characterList;
        private static DateTime lastBubblePlayback = DateTime.MinValue;
        private static float lastBubbleLength = 0;
        private static readonly object lockObject = new object();

        public LinkedList<AudioInfo> AudioInfoState { get; set; } = new LinkedList<AudioInfo>();

        public Audio(Plugin plugin)
        {
            this.Plugin = plugin;
            Plugin.Chat.Print("Audio: I am awake");
            characterList = new List<Character>();
            AudioInfoState.Clear();
        }

        public void Dispose()
        {
            if (characterList != null)
                characterList.Clear();
            characterList = null;
        }

        public async Task PlayAudio(XivMessage xivMessage, WaveStream waveStream)
        {
            
            while (AudioIsPlaying)
                await Task.Delay(50);

            AudioIsPlaying = true;
            audioIsStopped = false;

            var audioInfo = GetAudioInfo(xivMessage);

            Plugin.TriggerLipSync(xivMessage.TtsData.Character, waveStream.TotalTime.TotalSeconds.ToString());
            using (var audioOutput = new WaveOut())
            {
                audioOutput.Init(waveStream);
                audioOutput.Play();
                audioInfo.state = "playing";
                audioOutput.Volume = (float)Plugin.Config.Volume/100f;
                var totalDuration = waveStream.TotalTime.TotalMilliseconds;
                while (audioOutput.PlaybackState == PlaybackState.Playing)
                {
                    var currentPosition = waveStream.CurrentTime.TotalMilliseconds;
                    audioInfo.percentage = (float)(currentPosition / totalDuration);
                    audioOutput.Volume = (float)Plugin.Config.Volume / 100f;
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
            //if (characterList.Contains(xivMessage.TtsData.Character))
            //    return;
            // Plugin.Chat.Print(waveStream.TotalTime.Milliseconds.ToString());
            while (true)
            {
                lock (lockObject)
                {
                    if ((DateTime.Now - lastBubblePlayback).TotalMilliseconds >= lastBubbleLength)
                    {
                        lastBubblePlayback = DateTime.Now;
                        lastBubbleLength = waveStream.TotalTime.Milliseconds+500;
                        break;
                    }
                }
                await Task.Delay(10);
            }

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
                        
                        Plugin.Chat.Print("distance:" + distance);
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

            //if (characterList.Contains(xivMessage.TtsData.Character))
            //    characterList.Remove(xivMessage.TtsData.Character);

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
