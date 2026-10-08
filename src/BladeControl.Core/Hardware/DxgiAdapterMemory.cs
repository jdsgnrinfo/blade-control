using System.Runtime.InteropServices;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Hardware;

/// <summary>
/// Each graphics adapter's dedicated memory as DirectX reports it, the figure
/// Task Manager shows: 128 MB for Intel's built-in graphics where the driver's
/// registry key says 1 GB, which is only the most it may borrow.
/// </summary>
internal static class DxgiAdapterMemory
{
    private const uint SoftwareAdapter = 2;

    /// <summary>Dedicated memory in bytes by adapter name ("Intel(R) UHD Graphics"); empty when DirectX cannot say.</summary>
    public static Dictionary<string, long> Read()
    {
        var memory = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        IDXGIFactory1? factory = null;

        try
        {
            var factoryId = typeof(IDXGIFactory1).GUID;

            if (CreateDXGIFactory1(ref factoryId, out factory) < 0)
                return memory;

            for (uint index = 0; factory.EnumAdapters1(index, out var adapter) >= 0; index++)
            {
                try
                {
                    if (adapter.GetDesc1(out var description) >= 0 &&
                        (description.Flags & SoftwareAdapter) == 0 &&
                        description.DedicatedVideoMemory > 0)
                    {
                        var name = description.Description.Trim();
                        memory[name] = Math.Max((long)description.DedicatedVideoMemory, memory.GetValueOrDefault(name));
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        catch (Exception exception) when (exception is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            AppLog.Error("Could not read the graphics adapters' memory from DirectX.", exception);
        }
        finally
        {
            if (factory is not null)
                Marshal.ReleaseComObject(factory);
        }

        return memory;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid id, out IDXGIFactory1 factory);

    // Only the methods called are spelled out; the others hold their places in the table.
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();

        [PreserveSig]
        int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();

        [PreserveSig]
        int GetDesc1(out AdapterDescription description);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }
}
