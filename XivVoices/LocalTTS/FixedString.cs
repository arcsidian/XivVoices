using System;
using System.Runtime.InteropServices;

namespace XivVoices.LocalTTS
{
    public class FixedString : IDisposable
    {
        public IntPtr Address { get; private set; }

        public FixedString(string text)
        {
            Address = Marshal.StringToHGlobalAnsi(text);
        }

        public void Dispose()
        {
            if (Address == IntPtr.Zero) return;
            Marshal.FreeHGlobal(Address);
            Address = IntPtr.Zero;
        }
    }
}