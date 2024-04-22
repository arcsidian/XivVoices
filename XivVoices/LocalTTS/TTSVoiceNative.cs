using Dalamud.Logging;
using System;
using System.IO;
using System.Threading;
using XivVoices.Engine;
namespace XivVoices.LocalTTS
{
    public class TTSVoiceNative
    {
        public const int Timeout = 8000;
        public IntPtr Pointer { get; set; }
        public FixedPointerToHeapAllocatedMem ConfigPointer { get; set; }
        public FixedPointerToHeapAllocatedMem ModelPointer { get; set; }
        public bool Disposed { get; private set; }
        private readonly ReaderWriterLock _lock = new ReaderWriterLock();

        public static TTSVoiceNative LoadVoiceFromDisk(string voiceName)
        {
            var toolsPath = XivEngine.Instance.Database.ToolsPath;
            var modelPath = Path.Combine(toolsPath, $"{voiceName}.bytes");
            var configPath = Path.Combine(toolsPath, $"{voiceName}.config.json"); // Ensure the file extension is correct

            // Check if the files exist before trying to read them
            if (!File.Exists(modelPath))
            {
                PluginLog.LogError($"Failed to find voice model {voiceName}.bytes in {toolsPath}");
                return null;
            }
            if (!File.Exists(configPath))
            {
                PluginLog.LogError($"Failed to find voice model {voiceName}.config.json in {toolsPath}");
                return null;
            }

            // Read the binary data from files
            byte[] modelBytes = File.ReadAllBytes(modelPath);
            byte[] configBytes = File.ReadAllBytes(configPath);

            // Create memory pointers for the model and config
            var configPtr = FixedPointerToHeapAllocatedMem.Create(configBytes, (uint)configBytes.Length);
            var modelPtr = FixedPointerToHeapAllocatedMem.Create(modelBytes, (uint)modelBytes.Length);

            // Call native function to load the voice
            var ptr = TTSNative.LocalTTSLoadVoice(
                configPtr.Address,
                configPtr.SizeInBytes,
                modelPtr.Address,
                modelPtr.SizeInBytes
            );

            TTSNative.LocalTTSSetSpeakerId(ptr, 0);

            // Return a new instance of TTSVoiceNative with the loaded data
            return new TTSVoiceNative
            {
                Pointer = ptr,
                ConfigPointer = configPtr,
                ModelPointer = modelPtr,
            };
        }

        public void SetSpeakerId(int speakerId)
        {
            TTSNative.LocalTTSSetSpeakerId(Pointer, speakerId);
        }

        public void AcquireReaderLock()
        {
            try
            {
                _lock.AcquireReaderLock(Timeout);
            }
            catch
            {
                // Handle exception or rethrow
                throw;
            }
        }
        
        public void ReleaseReaderLock()
        {
            _lock.ReleaseReaderLock();
        }


        public void Dispose()
        {
            _lock.AcquireWriterLock(Timeout);
            Disposed = true;
            ConfigPointer.Free();
            ModelPointer.Free();
            TTSNative.LocalTTSFreeVoice(Pointer);
            _lock.ReleaseWriterLock();
        }
    }
}