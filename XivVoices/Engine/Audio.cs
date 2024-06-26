﻿using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
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
        private Plugin Plugin;
        private AsyncLock playAudioLock { get; set; }
        private AsyncLock playBubbleLock { get; set; }
        private bool audioIsStopped { get; set; }
        public Queue<string> unknownQueue { get; set; }
        public LinkedList<AudioInfo> AudioInfoState { get; set; }

        public Audio(Plugin plugin)
        {
            this.Plugin = plugin;
            Plugin.Chat.Print("Audio: I am awake");
            playAudioLock = new AsyncLock();
            playBubbleLock = new AsyncLock();
            audioIsStopped = false;
            unknownQueue = new Queue<string>();
            AudioInfoState = new LinkedList<AudioInfo>();
        }

        public void Dispose()
        {
            playAudioLock.Dispose();
            playBubbleLock.Dispose();
            unknownQueue.Clear();
            unknownQueue = null;
            AudioInfoState.Clear();
            AudioInfoState = null;
        }

        public async Task PlayAudio(XivMessage xivMessage, WaveStream waveStream, string type)
        {
            if (!Plugin.Config.Active) return;

            using (await playAudioLock.LockAsync())
            {
                try
                {
                    PluginLog.Information($"PlayAudio ---> start");

                    if (waveStream == null)
                    {
                        PluginLog.Error("PlayAudio ---> waveStream is null");
                        return;
                    }

                    var volumeProvider = new VolumeSampleProvider(waveStream.ToSampleProvider());
                    var audioInfo = GetAudioInfo(xivMessage, type);
                    PluginLog.Information($"PlayAudio ---> audioinfo receieved");

                    audioIsStopped = false;
                    if (!this.Plugin.Config.Mute)
                    {
                        if (!xivMessage.Ignored && xivMessage.TtsData != null)
                            Plugin.TriggerLipSync(xivMessage.TtsData.Character, waveStream.TotalTime.TotalSeconds.ToString());

                        using (var audioOutput = GetAudioEngine())
                        {
                            PluginLog.Information($"PlayAudio ---> audioengine chosen");
                            audioOutput.Init(volumeProvider);

                            if (type == "ai")
                                volumeProvider.Volume = (float)Plugin.Config.LocalTTSVolume / 100f;
                            else
                                volumeProvider.Volume = (float)Plugin.Config.Volume / 100f;

                            audioOutput.Play();
                            PluginLog.Information($"PlayAudio ---> playing");
                            audioInfo.state = "playing";

                            var totalDuration = waveStream.TotalTime.TotalMilliseconds;
                            while (audioOutput.PlaybackState == PlaybackState.Playing)
                            {
                                var currentPosition = waveStream.CurrentTime.TotalMilliseconds;
                                audioInfo.percentage = (float)(currentPosition / totalDuration);

                                if (type == "ai")
                                    volumeProvider.Volume = (float)Plugin.Config.LocalTTSVolume / 100f;
                                else
                                    volumeProvider.Volume = (float)Plugin.Config.Volume / 100f;

                                if (audioIsStopped)
                                {
                                    audioOutput.Stop();
                                    audioIsStopped = false;
                                    if (!xivMessage.Ignored && xivMessage.TtsData != null)
                                        Plugin.StopLipSync(xivMessage.TtsData.Character);
                                    break;
                                }
                                await Task.Delay(50);
                            }
                        }
                    }

                    audioInfo.state = "stopped";
                    PluginLog.Information($"PlayAudio ---> stopped");
                    audioInfo.percentage = 1f;
                }
                catch (Exception ex)
                {
                    Plugin.PrintError("Error during audio playback. " + ex);
                    PluginLog.Error($"PlayAudio ---> Exception: {ex}");
                }
                finally
                {
                    waveStream?.Dispose();
                    PluginLog.Information($"PlayAudio ---> playAudioLock released");
                }
            }
        }

        public async Task PlayBubble(XivMessage xivMessage, WaveStream waveStream, string type)
        {
            if (!Plugin.Config.Active) return;

            using (await playBubbleLock.LockAsync())
            {
                PluginLog.Information($"PlayBubble ---> start");

                try
                {
                    if (waveStream == null)
                    {
                        PluginLog.Error("PlayBubble ---> waveStream is null");
                        return;
                    }

                    var volumeProvider = new VolumeSampleProvider(waveStream.ToSampleProvider());
                    PanningSampleProvider panningProvider = new PanningSampleProvider(volumeProvider);

                    var audioInfo = GetAudioInfo(xivMessage, type);
                    ushort initialRegion = this.Plugin.ClientState.TerritoryType;

                    if (!this.Plugin.Config.Mute)
                    {
                        using (var audioOutput = GetAudioEngine())
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
                                if (initialRegion != this.Plugin.ClientState.TerritoryType)
                                {
                                    audioOutput.Stop();
                                    break;
                                }

                                data = GetDistanceAndBalance(xivMessage.TtsData.Position);
                                volumeProvider.Volume = AdjustVolume(data.Distance);
                                panningProvider.Pan = data.Balance;

                                /* Testing In a Loop
                                Plugin.Chat.Print("distance:" + data.Distance);
                                Plugin.Chat.Print("volumeProvider.Volume:" + volumeProvider.Volume);

                                if (audioIsStopped)
                                {
                                    audioOutput.Stop();
                                    audioIsStopped = false;
                                    break;
                                }
                                if (waveStream.Position > waveStream.Length - 10000)
                                    waveStream.Position = 0;
                                */

                                await Task.Delay(100);
                            }
                        }
                    }

                    audioInfo.state = "stopped";
                    audioInfo.percentage = 1f;
                }
                catch (Exception ex)
                {
                    Plugin.PrintError("Error during bubble playback. " + ex);
                }
                finally
                {
                    waveStream?.Dispose();
                }
            }
        }

        public async Task PlaySystemAudio(WaveStream waveStream)
        {
            if (!Plugin.Config.Active) return;
            PluginLog.Information($"PlaySystemAudio ---> start");

            try
            {
                var volumeProvider = new VolumeSampleProvider(waveStream.ToSampleProvider());
                if (!this.Plugin.Config.Mute)
                {
                    using (var audioOutput = GetAudioEngine())
                    {
                        audioOutput.Init(volumeProvider);
                        volumeProvider.Volume = 1f;
                        audioOutput.Play();
                        while (audioOutput.PlaybackState == PlaybackState.Playing)
                            await Task.Delay(50);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.PrintError("Error during system audio playback. " + ex);
            }
            finally
            {
                waveStream?.Dispose();
            }

        }

        IWavePlayer GetAudioEngine()
        {
            switch (this.Plugin.Config.AudioEngine)
            {
                case 1:
                    return new DirectSoundOut();
                case 2:
                    return new WasapiOut();
                default:
                    return new WaveOut();
            }
        }

        public async Task PlayEmptyAudio(XivMessage xivMessage)
        {
            var audioInfo = GetAudioInfo(xivMessage, "empty");
            audioInfo.percentage = 1f;
            if(xivMessage.Reported)
                audioInfo.state = "Reported";
            else
                audioInfo.state = "";
        }

        float AdjustVolume(float distance)
        {
            float volume = (float)Plugin.Config.Volume / 100f;
            (float distanceStart, float distanceEnd, float volumeStart, float volumeEnd)[] volumeRanges =
            {
                (0f, 3f, volume*1f, volume*0.85f),   // 0 to 3 units: 100% to 85%
                (3f, 5f, volume*0.85f, volume*0.3f),   // 3 to 5 units: 85% to 30% 
                (5f, 20f, volume*0.3f, volume*0.05f)     // 5 to 20 units: 30% to 5%
            };

            if(Conditions.IsBoundByDuty)
            {
                volumeRanges[0].volumeStart = 0.75f;
                volumeRanges[0].volumeEnd = 0.73f;  // 0 to 3 units: 75% to 73%
                volumeRanges[0].volumeStart = 0.73f;
                volumeRanges[1].volumeEnd = 0.70f;   // 3 to 5 units: 73% to 70% 
                volumeRanges[2].volumeStart = 0.70f;
                volumeRanges[2].volumeEnd = 0.60f;   // 5 to 20 units: 70% to 60%
            }

            foreach (var range in volumeRanges)
            {
                if (distance >= range.distanceStart && distance <= range.distanceEnd)
                {
                    float slope = (range.volumeEnd - range.volumeStart) / (range.distanceEnd - range.distanceStart);
                    float yIntercept = range.volumeStart - slope * range.distanceStart;
                    float _volume = slope * distance + yIntercept;
                    return Math.Clamp(_volume, Math.Min(range.volumeStart, range.volumeEnd), Math.Max(range.volumeStart, range.volumeEnd));
                }
            }
            return volumeRanges[^1].volumeEnd;
        }

        AudioInfo GetAudioInfo(XivMessage xivMessage, string type)
        {
            string id = $"{xivMessage.Speaker}_{xivMessage.Sentence}";
            id = Regex.Replace(id, @"[^a-zA-Z0-9 _-]", "").Replace(" ", "_").Replace("-", "_");
            var audioInfo = AudioInfoState.FirstOrDefault(ai => ai.id == id);
            if (audioInfo == null)
            {
                audioInfo = new AudioInfo(id, "new", 0f, type, xivMessage);
                AudioInfoState.AddFirst(audioInfo);
            }
            else
            {
                AudioInfoState.Remove(audioInfo);
                audioInfo = new AudioInfo(id, "new", 0f, type, xivMessage);
                AudioInfoState.AddFirst(audioInfo);
                audioInfo.state = "new";
            }
            if (AudioInfoState.Count > 100)
            {
                var oldestFinished = AudioInfoState.LastOrDefault(ddi => ddi.state == "stopped" || ddi.state == "Reported");
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
