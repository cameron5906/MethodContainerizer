using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MethodContainerizer.Extensions
{
    internal static class MarshalExtensions
    {
        public static void Becomes(this MethodInfo origin, MethodInfo target)
        {
            IntPtr ori = GetMethodAddress(origin);
            IntPtr tar = GetMethodAddress(target);

            Marshal.Copy(new IntPtr[] { Marshal.ReadIntPtr(tar) }, 0, ori, 1);
        }

        private static IntPtr GetMethodAddress(MethodInfo mi)
        {
            const ushort SLOT_NUMBER_MASK = 0xfff; // 3 bytes
            const int MT_OFFSET_32BIT = 0x28;      // 40 bytes
            const int MT_OFFSET_64BIT = 0x40;      // 64 bytes

            IntPtr address;

            // JIT compilation of the method
            RuntimeHelpers.PrepareMethod(mi.MethodHandle);

            IntPtr md = mi.MethodHandle.Value;             // MethodDescriptor address
            IntPtr mt = mi.DeclaringType.TypeHandle.Value; // MethodTable address

            if (mi.IsVirtual)
            {
                // The fixed-size portion of the MethodTable structure depends on the process type
                int offset = IntPtr.Size == 4 ? MT_OFFSET_32BIT : MT_OFFSET_64BIT;

                // First method slot = MethodTable address + fixed-size offset
                // This is the address of the first method of any type (i.e. ToString)
                IntPtr ms = Marshal.ReadIntPtr(mt + offset);

                // Get the slot number of the virtual method entry from the MethodDesc data structure
                // Remark: the slot number is represented on 3 bytes
                long shift = Marshal.ReadInt64(md) >> 32;
                int slot = (int)(shift & SLOT_NUMBER_MASK);

                // Get the virtual method address relative to the first method slot
                address = ms + (slot * IntPtr.Size);
            }
            else
            {
                // Bypass default MethodDescriptor padding (8 bytes) 
                // Reach the CodeOrIL field which contains the address of the JIT-compiled code
                address = md + 8;
            }

            return address;
        }
    }
}
