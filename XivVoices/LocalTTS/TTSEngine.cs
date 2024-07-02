using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Dalamud.Logging;
using XivVoices.Engine;

namespace XivVoices.LocalTTS
{
    public class TTSEngine : IDisposable
    {
        public bool Loaded { get; private set; }
        private IntPtr _context;
        private readonly object _lock = new object();
        public bool Disposed { get; private set; }
        public Plugin Plugin;
        public TTSEngine(Plugin _Plugin)
        {
            lock (_lock)
            {
                _context = TTSNative.LocalTTSStart();
                Plugin.PluginLog.Information("Loaded speech model successfully");
                this.Plugin = _Plugin;
                Loaded = true;
            }
        }
        
        public async Task<float[]> SpeakTTS(string text, TTSVoiceNative voice)
        {

            Plugin.PluginLog.Information($"TTS for '{text ?? string.Empty}'");
            var units = SSMLPreprocessor.Preprocess(text );
            var samples = new List<float>();
            TTSResult result = null;
            foreach (var unit in units)
            {
                result = await SpeakSamples(unit, voice);
                samples.AddRange(result.Samples);
            }

            Plugin.PluginLog.Information($"Done. Returned '{samples.Count}' samples .. result.SampleRate {result.SampleRate}");
            return samples.ToArray();
        }

        public async Task<TTSResult> SpeakSamples(SpeechUnit unit, TTSVoiceNative voice)
        {
            var tcs = new TaskCompletionSource<TTSResult>();
        
            float[] samples = null;
            using var textPtr = new FixedString(unit.Text);
            var result = new TTSNative.LocalTTSResult
            {
                Channels = 0
            };

            await Task.Run(() =>
            {
                lock (_lock)
                {
                    try
                    {
                        voice.AcquireReaderLock();
                        if (Disposed || voice.Disposed)
                        { 
                            samples = Array.Empty<float>();
                            Plugin.PluginLog.Warning("Couldn't process TTS. TTSEngine or TTSVoiceNative has been disposed.");
                            return;
                        }
                        ValidatePointer(voice.Pointer, "Voice pointer is null.");
                        ValidatePointer(voice.ConfigPointer.Address, "Config pointer is null.");
                        ValidatePointer(voice.ModelPointer.Address, "Model pointer is null.");
                        ValidatePointer(_context, "Context pointer is null.");
                        ValidatePointer(textPtr.Address, "Text pointer is null.");
                        result = TTSNative.LocalTTSText2Audio(_context, textPtr.Address, voice.Pointer);
                        samples = PtrToSamples(result.Samples, result.LengthSamples);
                        voice.ReleaseReaderLock();
                    }
                    catch (Exception e)
                    {
                        Plugin.PluginLog.Error($"Error while processing TTS: {e.Message}");
                        tcs.SetException(e);
                    }
                }
            });

            

            tcs.SetResult(new TTSResult
            {
                Channels = result.Channels,
                SampleRate = result.SampleRate,
                Samples = samples
            });

            TTSNative.LocalTTSFreeResult(result);
            textPtr.Dispose();
            return await tcs.Task;
        }

        private void ValidatePointer(IntPtr pointer, string errorMessage)
        {
            if (pointer == IntPtr.Zero)
                throw new InvalidOperationException(errorMessage);
        }

        private float[] PtrToSamples(IntPtr int16Buffer, uint samplesLength)
        {
            var floatSamples = new float[samplesLength];
            var int16Samples = new short[samplesLength];

            Marshal.Copy(int16Buffer, int16Samples, 0, (int)samplesLength);

            for (int i = 0; i < samplesLength; i++)
            {
                floatSamples[i] = int16Samples[i] / (float)short.MaxValue;
            }
        
            return floatSamples;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                Disposed = true;
                if (_context != IntPtr.Zero)
                    TTSNative.LocalTTSFree(_context);
                Plugin.PluginLog.Information("Successfully cleaned up TTS Engine");
                
            }
        }
    }

    public class TTSResult
    {
        public float[] Samples;
        public uint Channels;
        public uint SampleRate;
    }
}