using System;
using System.Runtime.InteropServices;

namespace XivVoices.LocalTTS
{
    public static class TTSNative
    {
        private const string localTTSLib = "C:\\XIV_Voices\\Tools\\localtts";
        
        [StructLayout(LayoutKind.Sequential)]
        public struct LocalTTSResult
        {
            public uint Channels;
            public uint SampleRate;
            public uint LengthSamples;
            public IntPtr Samples; 
        }

        [DllImport(localTTSLib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "localtts_start")]
        public static extern IntPtr LocalTTSStart();

        [DllImport(localTTSLib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "localtts_text_2_audio")]
        public static extern LocalTTSResult LocalTTSText2Audio(IntPtr ctx, IntPtr text, IntPtr voice);

        [DllImport(localTTSLib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "localtts_load_voice")]
        public static extern IntPtr LocalTTSLoadVoice(IntPtr configBuffer, uint configBufferSize, IntPtr modelBuffer, uint modelBufferSize);

        [DllImport(localTTSLib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "localtts_set_speaker_id")]
        public static extern void LocalTTSSetSpeakerId(IntPtr voice, long speakerId);
    
        [DllImport(localTTSLib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "localtts_free_voice")]
        public static extern void LocalTTSFreeVoice(IntPtr voice);
    
        [DllImport(localTTSLib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "localtts_free_result")]
        public static extern void LocalTTSFreeResult(LocalTTSResult result);

        [DllImport(localTTSLib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "localtts_free")]
        public static extern void LocalTTSFree(IntPtr ctx);
    }
}
