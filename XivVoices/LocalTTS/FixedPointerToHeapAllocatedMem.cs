using System;
using System.Runtime.InteropServices;

namespace XivVoices.LocalTTS
{
    public class FixedPointerToHeapAllocatedMem : IDisposable
    {
        private GCHandle _handle;
        public IntPtr Address { get; private set; }

        public void Free()
        {
            _handle.Free();
            Address = IntPtr.Zero;
        }
        public static FixedPointerToHeapAllocatedMem Create<T>(T Object, uint SizeInBytes)
        {
            var pointer = new FixedPointerToHeapAllocatedMem
            {
                _handle = GCHandle.Alloc(Object, GCHandleType.Pinned),
                SizeInBytes = SizeInBytes
            };
            pointer.Address = pointer._handle.AddrOfPinnedObject();
            return pointer;
        }

        public void Dispose()
        {
            if (_handle.IsAllocated)
            {
                _handle.Free();
                Address = IntPtr.Zero;
            }
        }

        public uint SizeInBytes { get; private set; }
    }
}