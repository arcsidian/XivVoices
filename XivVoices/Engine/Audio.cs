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
            try
            {
                this.Plugin = plugin;
                Plugin.Chat.Print("Audio: I am awake");
                playAudioLock = new AsyncLock();
                playBubbleLock = new AsyncLock();
                audioIsStopped = false;
                unknownQueue = new Queue<string>();
                AudioInfoState = new LinkedList<AudioInfo>();
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"Error initializing Audio: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                playAudioLock.Dispose();
                playBubbleLock.Dispose();
                unknownQueue.Clear();
                unknownQueue = null;
                AudioInfoState.Clear();
                AudioInfoState = null;
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"Error disposing Audio: {ex.Message}");
            }
        }

        public async Task PlayAudio(XivMessage xivMessage, WaveStream waveStream, string type)
        {
            if (!Plugin.Config.Active) return;

            using (await playAudioLock.LockAsync())
            {
                try
                {
                    Plugin.PluginLog.Information($"PlayAudio ---> start");

                    if (waveStream == null)
                    {
                        Plugin.PluginLog.Error("PlayAudio ---> waveStream is null");
                        return;
                    }

                    var volumeProvider = new VolumeSampleProvider(waveStream.ToSampleProvider());
                    var audioInfo = GetAudioInfo(xivMessage, type);
                    Plugin.PluginLog.Information($"PlayAudio ---> audioinfo receieved");

                    audioIsStopped = false;
                    if (!this.Plugin.Config.Mute)
                    {
                        if (!xivMessage.Ignored && xivMessage.TtsData != null)
                            Plugin.TriggerLipSync(xivMessage.TtsData.Character, waveStream.TotalTime.TotalSeconds.ToString());

                        using (var audioOutput = GetAudioEngine())
                        {
                            Plugin.PluginLog.Information($"PlayAudio ---> audioengine chosen");
                            audioOutput.Init(volumeProvider);

                            if (type == "ai")
                                volumeProvider.Volume = (float)Plugin.Config.LocalTTSVolume / 100f;
                            else
                                volumeProvider.Volume = (float)Plugin.Config.Volume / 100f;

                            audioOutput.Play();
                            Plugin.PluginLog.Information($"PlayAudio ---> playing");
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
                    Plugin.PluginLog.Information($"PlayAudio ---> stopped");
                    audioInfo.percentage = 1f;
                    Plugin.ClickTalk();
                }
                catch (Exception ex)
                {
                    Plugin.PrintError("Error during audio playback. " + ex);
                    Plugin.PluginLog.Error($"PlayAudio ---> Exception: {ex}");
                }
                finally
                {
                    try
                    {
                        waveStream?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Plugin.PluginLog.Error("An error occurred while disposing of waveStream resources: " + ex);
                    }
                }
            }
        }

        public async Task PlayBubble(XivMessage xivMessage, WaveStream waveStream, string type)
        {
            if (!Plugin.Config.Active) return;

            using (await playBubbleLock.LockAsync())
            {
                Plugin.PluginLog.Information($"PlayBubble ---> start");

                try
                {
                    if (waveStream == null)
                    {
                        Plugin.PluginLog.Error("PlayBubble ---> waveStream is null");
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
                    try
                    {
                        waveStream?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Plugin.PluginLog.Error("An error occurred while disposing of bubble waveStream resources: " + ex);
                    }
                }
            }
        }

        public async Task PlaySystemAudio(WaveStream waveStream)
        {
            if (!Plugin.Config.Active) return;
            Plugin.PluginLog.Information($"PlaySystemAudio ---> start");

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
                try
                {
                    waveStream?.Dispose();
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Error("An error occurred while disposing of system waveStream resources: " + ex);
                }
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
            try
            {
                var audioInfo = GetAudioInfo(xivMessage, "empty");
                audioInfo.percentage = 1f;
                if (xivMessage.Reported)
                    audioInfo.state = "not voiced";
                else
                    audioInfo.state = "";
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"Error in PlayEmptyAudio method: {ex.Message}");
            }
        }

        float AdjustVolume(float distance)
        {
            try
            {
                float volume = (float)Plugin.Config.Volume / 100f;
                (float distanceStart, float distanceEnd, float volumeStart, float volumeEnd)[] volumeRanges =
                {
                    (0f, 3f, volume*1f, volume*0.85f),   // 0 to 3 units: 100% to 85%
                    (3f, 5f, volume*0.85f, volume*0.3f),   // 3 to 5 units: 85% to 30%
                    (5f, 20f, volume*0.3f, volume*0.05f)     // 5 to 20 units: 30% to 5%
                };

                if (Conditions.IsBoundByDuty)
                {
                    volumeRanges[0].volumeStart = 0.65f;
                    volumeRanges[0].volumeEnd = 0.63f;  // 0 to 3 units: 65% to 63%
                    volumeRanges[1].volumeStart = 0.63f;
                    volumeRanges[1].volumeEnd = 0.60f;   // 3 to 5 units: 63% to 60%
                    volumeRanges[2].volumeStart = 0.60f;
                    volumeRanges[2].volumeEnd = 0.55f;   // 5 to 20 units: 60% to 55%
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
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"Error adjusting volume: {ex.Message}");
                return 0.05f; // return a default value in case of error
            }
        }

        AudioInfo GetAudioInfo(XivMessage xivMessage, string type)
        {
            try
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
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"Error getting audio info: {ex.Message}");
                return new AudioInfo("error", "error", 0f, "error", xivMessage);
            }
        }

        (float Distance,float Balance) GetDistanceAndBalance(Vector3 speakerPosition)
        {
            try
            {
                // Update camera vectors
                Vector3 cameraForward = Vector3.Normalize(Plugin.PlayerCamera.Forward);
                Vector3 cameraUp = Vector3.Normalize(Plugin.PlayerCamera.Top);
                Vector3 cameraRight = Vector3.Normalize(Vector3.Cross(cameraUp, cameraForward));

                // Calculate relative position from player to speaker
                Vector3 relativePosition = speakerPosition - Plugin.ClientState.LocalPlayer.Position;

                // Distance for volume adjustment
                float distance = relativePosition.Length();

                // Direction for stereo balance
                float dotProduct = Vector3.Dot(relativePosition, cameraRight);
                float balance = Math.Clamp(dotProduct / 20, -1, 1); // Normalize and clamp the value for balance

                return (Distance: distance, Balance: balance);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error($"Error calculating distance and balance: {ex.Message}");
                return (Distance: 0, Balance: 0);
            }
        }

        public void StopAudio()
        {
            audioIsStopped = true;
        }

    }
}
