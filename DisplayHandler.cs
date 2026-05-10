using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SwitchDisplay
{
    /// <summary>
    /// Handles display operations using the modern CCD API (SetDisplayConfig / QueryDisplayConfig).
    /// Unlike the legacy EnumDisplayDevices + ChangeDisplaySettingsEx approach, the CCD API
    /// can enumerate and operate on inactive/disconnected monitors, which is essential for
    /// scenarios where a TV is powered off but physically connected via HDMI.
    /// </summary>
    public class DisplayHandler
    {
        // -----------------------------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Enumerates all displays currently active (attached to desktop).
        /// Used to populate the settings dropdowns.
        /// </summary>
        public List<MonitorInfo> EnumerateActive()
        {
            return EnumerateInternal(QueryDisplayFlags.OnlyActivePaths);
        }

        /// <summary>
        /// Enumerates ALL displays known to Windows, including inactive/disconnected ones.
        /// Used to match saved monitor IDs even when the TV is powered off.
        /// </summary>
        public List<MonitorInfo> EnumerateAll()
        {
            return EnumerateInternal(QueryDisplayFlags.AllPaths);
        }

        /// <summary>
        /// Returns whether a monitor (identified by DevicePath) is currently active.
        /// </summary>
        public bool IsMonitorActive(string devicePath)
        {
            var active = EnumerateActive();
            return active.Exists(m => m.DevicePath.Equals(devicePath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Activates a monitor that is currently inactive (e.g. a TV that was powered off).
        /// Equivalent to DisplaySwitch /extend — adds the monitor to the desktop without
        /// changing any existing layout.
        /// </summary>
        public bool ActivateMonitor(string devicePath)
        {
            return SetMonitorActive(devicePath, true);
        }

        /// <summary>
        /// Sets the specified monitor as the primary display.
        /// All other currently active monitors remain active but positions are adjusted.
        /// </summary>
        public bool SetPrimaryMonitor(string devicePath)
        {
            uint numPathElements, numModeElements;
            int result = QueryDisplayConfig(
                QueryDisplayFlags.OnlyActivePaths,
                out numPathElements, null,
                out numModeElements, null,
                IntPtr.Zero);

            if (result != ERROR_SUCCESS) return false;

            var paths = new DISPLAYCONFIG_PATH_INFO[numPathElements];
            var modes = new DISPLAYCONFIG_MODE_INFO[numModeElements];

            result = QueryDisplayConfig(
                QueryDisplayFlags.OnlyActivePaths,
                out numPathElements, paths,
                out numModeElements, modes,
                IntPtr.Zero);

            if (result != ERROR_SUCCESS) return false;

            // Find the target monitor and its current desktop position offset
            int targetModeIdx = -1;
            int offsetX = 0, offsetY = 0;

            for (int i = 0; i < numPathElements; i++)
            {
                string path = GetMonitorDevicePath(paths[i].targetInfo.adapterId, paths[i].targetInfo.id);
                if (!path.Equals(devicePath, StringComparison.OrdinalIgnoreCase)) continue;

                int modeIdx = (int)paths[i].sourceInfo.modeInfoIdx;
                if (modeIdx < 0 || modeIdx >= numModeElements) continue;
                if (modes[modeIdx].infoType != DISPLAYCONFIG_MODE_INFO_TYPE.Source) continue;

                offsetX = modes[modeIdx].sourceMode.position.x;
                offsetY = modes[modeIdx].sourceMode.position.y;
                targetModeIdx = modeIdx;
                break;
            }

            if (targetModeIdx < 0) return false;

            // Already primary
            if (offsetX == 0 && offsetY == 0) return true;

            // Shift all source modes so the target ends up at (0,0)
            for (int i = 0; i < numModeElements; i++)
            {
                if (modes[i].infoType != DISPLAYCONFIG_MODE_INFO_TYPE.Source) continue;
                modes[i].sourceMode.position.x -= offsetX;
                modes[i].sourceMode.position.y -= offsetY;
            }

            result = SetDisplayConfig(
                numPathElements, paths,
                numModeElements, modes,
                SdcFlags.Apply | SdcFlags.UseSuppliedDisplayConfig | SdcFlags.SaveToDatabase | SdcFlags.NoOptimization);

            return result == ERROR_SUCCESS;
        }

        /// <summary>
        /// Disables a monitor (removes it from the active desktop).
        /// </summary>
        public bool DisableMonitor(string devicePath)
        {
            return SetMonitorActive(devicePath, false);
        }

        /// <summary>
        /// Enables a monitor (adds it back to the active desktop).
        /// </summary>
        public bool EnableMonitor(string devicePath)
        {
            return SetMonitorActive(devicePath, true);
        }

        // -----------------------------------------------------------------------------------------
        // Internal helpers
        // -----------------------------------------------------------------------------------------

        private bool SetMonitorActive(string devicePath, bool active)
        {
            uint numPathElements, numModeElements;
            int result = QueryDisplayConfig(
                QueryDisplayFlags.AllPaths,
                out numPathElements, null,
                out numModeElements, null,
                IntPtr.Zero);

            if (result != ERROR_SUCCESS) return false;

            var paths = new DISPLAYCONFIG_PATH_INFO[numPathElements];
            var modes = new DISPLAYCONFIG_MODE_INFO[numModeElements];

            result = QueryDisplayConfig(
                QueryDisplayFlags.AllPaths,
                out numPathElements, paths,
                out numModeElements, modes,
                IntPtr.Zero);

            if (result != ERROR_SUCCESS) return false;

            bool found = false;
            for (int i = 0; i < numPathElements; i++)
            {
                string path = GetMonitorDevicePath(paths[i].targetInfo.adapterId, paths[i].targetInfo.id);
                if (!path.Equals(devicePath, StringComparison.OrdinalIgnoreCase)) continue;

                if (active)
                    paths[i].flags |= PathInfoFlags.Active;
                else
                    paths[i].flags &= ~PathInfoFlags.Active;

                found = true;
                break;
            }

            if (!found) return false;

            result = SetDisplayConfig(
                numPathElements, paths,
                numModeElements, modes,
                SdcFlags.Apply | SdcFlags.UseSuppliedDisplayConfig | SdcFlags.SaveToDatabase | SdcFlags.NoOptimization);

            return result == ERROR_SUCCESS;
        }

        private List<MonitorInfo> EnumerateInternal(QueryDisplayFlags flags)
        {
            var monitors = new List<MonitorInfo>();

            uint numPathElements, numModeElements;
            int result = QueryDisplayConfig(flags, out numPathElements, null, out numModeElements, null, IntPtr.Zero);
            if (result != ERROR_SUCCESS) return monitors;

            var paths = new DISPLAYCONFIG_PATH_INFO[numPathElements];
            var modes = new DISPLAYCONFIG_MODE_INFO[numModeElements];

            result = QueryDisplayConfig(flags, out numPathElements, paths, out numModeElements, modes, IntPtr.Zero);
            if (result != ERROR_SUCCESS) return monitors;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < numPathElements; i++)
            {
                bool isActive = (paths[i].flags & PathInfoFlags.Active) != 0;
                string devicePath = GetMonitorDevicePath(paths[i].targetInfo.adapterId, paths[i].targetInfo.id);
                string friendlyName = GetMonitorFriendlyName(paths[i].targetInfo.adapterId, paths[i].targetInfo.id);
                string adapterName = GetAdapterName(paths[i].targetInfo.adapterId);

                if (string.IsNullOrEmpty(devicePath)) continue;
                if (!seen.Add(devicePath)) continue; // deduplicate

                int width = 0, height = 0;
                int modeIdx = (int)paths[i].sourceInfo.modeInfoIdx;
                if (modeIdx >= 0 && modeIdx < numModeElements && modes[modeIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.Source)
                {
                    width = (int)modes[modeIdx].sourceMode.width;
                    height = (int)modes[modeIdx].sourceMode.height;
                }

                monitors.Add(new MonitorInfo
                {
                    DevicePath = devicePath,
                    FriendlyName = string.IsNullOrEmpty(friendlyName) ? adapterName : friendlyName,
                    AdapterName = adapterName,
                    IsActive = isActive,
                    Width = width,
                    Height = height
                });
            }

            return monitors;
        }

        private string GetMonitorDevicePath(LUID adapterId, uint targetId)
        {
            var info = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
            info.header.size = (uint)Marshal.SizeOf(info);
            info.header.adapterId = adapterId;
            info.header.id = targetId;
            info.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetTargetName;
            int result = DisplayConfigGetDeviceInfo(ref info);
            return result == ERROR_SUCCESS ? info.monitorDevicePath : string.Empty;
        }

        private string GetMonitorFriendlyName(LUID adapterId, uint targetId)
        {
            var info = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
            info.header.size = (uint)Marshal.SizeOf(info);
            info.header.adapterId = adapterId;
            info.header.id = targetId;
            info.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetTargetName;
            int result = DisplayConfigGetDeviceInfo(ref info);
            if (result != ERROR_SUCCESS) return string.Empty;
            bool hasFriendlyName = (info.flags & 0x2) != 0;
            return hasFriendlyName ? info.monitorFriendlyDeviceName : string.Empty;
        }

        private string GetAdapterName(LUID adapterId)
        {
            var info = new DISPLAYCONFIG_ADAPTER_NAME();
            info.header.size = (uint)Marshal.SizeOf(info);
            info.header.adapterId = adapterId;
            info.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetAdapterName;
            int result = DisplayConfigGetDeviceInfo(ref info);
            return result == ERROR_SUCCESS ? info.adapterDevicePath : string.Empty;
        }

        // -----------------------------------------------------------------------------------------
        // Win32 P/Invoke
        // -----------------------------------------------------------------------------------------

        private const int ERROR_SUCCESS = 0;

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            QueryDisplayFlags flags,
            out uint numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            out uint numModeInfoArrayElements,
            [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(
            uint numPathArrayElements,
            [In] DISPLAYCONFIG_PATH_INFO[] pathArray,
            uint numModeInfoArrayElements,
            [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            SdcFlags flags);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_ADAPTER_NAME requestPacket);

        // -----------------------------------------------------------------------------------------
        // Win32 structs and enums
        // -----------------------------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public int outputTechnology;
            public int rotation;
            public int scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public int scanLineOrdering;
            [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public PathInfoFlags flags;
        }

        [Flags]
        private enum PathInfoFlags : uint
        {
            Active = 0x00000001,
            Preferred = 0x00000002,
            SupportVirtualMode = 0x00000008
        }

        private enum DISPLAYCONFIG_MODE_INFO_TYPE : int { Source = 1, Target = 2, DesktopImage = 3 }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINTL { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_SOURCE_MODE
        {
            public uint width;
            public uint height;
            public int pixelFormat;
            public POINTL position;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_TARGET_MODE
        {
            // Simplified — full DISPLAYCONFIG_VIDEO_SIGNAL_INFO not needed for our operations
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
            public byte[] data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            [FieldOffset(0)] public DISPLAYCONFIG_MODE_INFO_TYPE infoType;
            [FieldOffset(4)] public uint id;
            [FieldOffset(8)] public LUID adapterId;
            [FieldOffset(16)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
            [FieldOffset(16)] public DISPLAYCONFIG_TARGET_MODE targetMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        private enum DISPLAYCONFIG_DEVICE_INFO_TYPE : int
        {
            GetSourceName = 1,
            GetTargetName = 2,
            GetTargetPreferredMode = 3,
            GetAdapterName = 4,
            SetTargetPersistence = 5
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags;
            public int outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_ADAPTER_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string adapterDevicePath;
        }

        [Flags]
        private enum QueryDisplayFlags : uint
        {
            AllPaths = 0x00000001,
            OnlyActivePaths = 0x00000002,
            DatabaseCurrent = 0x00000004
        }

        [Flags]
        private enum SdcFlags : uint
        {
            TopologyInternal = 0x00000001,
            TopologyClone = 0x00000002,
            TopologyExtend = 0x00000004,
            TopologyExternal = 0x00000008,
            TopologySupplied = 0x00000010,
            UseSuppliedDisplayConfig = 0x00000020,
            Validate = 0x00000040,
            Apply = 0x00000080,
            NoOptimization = 0x00000100,
            SaveToDatabase = 0x00000200,
            AllowChanges = 0x00000400,
            PathPersistIfRequired = 0x00000800,
            ForceModeEnumeration = 0x00001000,
            AllowPathOrderChanges = 0x00002000
        }
    }

    // -----------------------------------------------------------------------------------------
    // Public data model
    // -----------------------------------------------------------------------------------------

    public class MonitorInfo
    {
        /// <summary>
        /// Stable hardware device path (e.g. \\?\DISPLAY#SAM7558#...).
        /// Persists across reboots and power cycles — safe to save in settings.
        /// </summary>
        public string DevicePath { get; set; }

        /// <summary>Human-readable monitor name (e.g. "SAMSUNG" or "LG ULTRAGEAR").</summary>
        public string FriendlyName { get; set; }

        /// <summary>GPU adapter path.</summary>
        public string AdapterName { get; set; }

        /// <summary>Whether the monitor is currently part of the active desktop.</summary>
        public bool IsActive { get; set; }

        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>Display string shown in the settings UI.</summary>
        public string DisplayLabel =>
            Width > 0 ? $"{FriendlyName} ({Width}x{Height})" : FriendlyName;
    }
}
