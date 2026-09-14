using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MSFS_AutoFPS
{
    public static class MemoryInterface
    {
        public const int PROCESS_VM_OPERATION = 0x0008;
        public const int PROCESS_VM_READ = 0x0010;
        public const int PROCESS_VM_WRITE = 0x0020;

        private static Process proc;
        private static IntPtr procHandle;

        public static long GetModuleAddress(string Name)
        {
            try
            {
                if (proc != null)
                {
                    foreach (ProcessModule ProcMod in proc.Modules)
                    {
                        if (Name == ProcMod.ModuleName)
                            return (long)ProcMod.BaseAddress;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, "MemoryInterface:GetModuleAddress", $"Exception {ex}: {ex.Message}");
            }

            return -1;
        }

        public static bool Attach(string name)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    proc = Process.GetProcessesByName(name)[0];
                    procHandle = NativeMethods.OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, proc.Id);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, "MemoryInterface:Attach", $"Exception {ex}: {ex.Message}");
            }

            return false;
        }

        // Was previously WriteMemory<T>(long, object): T was never actually used to determine how many
        // bytes got written - Marshal.SizeOf(obj) sized the buffer off the boxed argument's runtime type
        // instead. That silently wrote 4 bytes (the default marshaled size of a boxed bool) for the two
        // call sites passing a bool into WriteMemory<byte>, clobbering 3 bytes past the intended 1-byte
        // MSFS field. Making T the actual parameter type fixes that and lets the size be cached per T
        // instead of resolved via reflection on every single call.
        public static void WriteMemory<T>(long Address, T Value) where T : struct
        {
            try
            {
                int size = SizeCache<T>.Size;
                IntPtr pointer = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(Value, pointer, false);
                    var buffer = new byte[size];
                    Marshal.Copy(pointer, buffer, 0, size);
                    NativeMethods.NtWriteVirtualMemory(procHandle, Address, buffer, buffer.Length, out _);
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, "MemoryInterface:WriteMemory", $"Exception {ex}: {ex.Message}");
            }
        }

        public static T ReadMemory<T>(long address) where T : struct
        {
            try
            {
                var buffer = new byte[SizeCache<T>.Size];

                NativeMethods.NtReadVirtualMemory(procHandle, address, buffer, buffer.Length, out _);

                return ByteArrayToStructure<T>(buffer);
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, "MemoryInterface:ReadMemory", $"Exception {ex}: {ex.Message}");
            }

            return default;
        }

        private static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }

        // Marshal.SizeOf(typeof(T)) involves a reflection lookup; caching it in a generic static field
        // resolves it exactly once per closed type (float/int/byte/long) instead of on every read/write -
        // and this class does a dozen+ reads per LODController tick.
        private static class SizeCache<T> where T : struct
        {
            public static readonly int Size = Marshal.SizeOf<T>();
        }
    }

    internal static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        internal static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        // ProcessHandle is a Windows HANDLE (pointer-sized) - was previously declared as `int` here and
        // truncated into one via a `checked` cast at the call site, which would throw if a handle value
        // ever exceeded Int32 range instead of just working like a real HANDLE parameter should.
        [DllImport("ntdll.dll")]
        internal static extern IntPtr NtWriteVirtualMemory(IntPtr ProcessHandle, long BaseAddress, byte[] Buffer, int NumberOfBytesToWrite, out int NumberOfBytesWritten);
        [DllImport("ntdll.dll")]
        internal static extern bool NtReadVirtualMemory(IntPtr ProcessHandle, long BaseAddress, byte[] Buffer, int NumberOfBytesToRead, out int NumberOfBytesRead);
    }
}
