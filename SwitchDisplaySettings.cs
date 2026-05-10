using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NAudio.CoreAudioApi;

namespace SwitchDisplay
{
    public class SwitchDisplaySettings : ObservableObject, ISettings
    {
        private SwitchDisplay plugin;

        // ---- Display settings ----------------------------------------------------------------

        /// <summary>Device path of the monitor to set as primary in fullscreen (TV) mode.</summary>
        public string FullscreenDisplay { get; set; } = string.Empty;

        /// <summary>Device path of the monitor to set as primary when returning to desktop.</summary>
        public string DefaultDisplay { get; set; } = string.Empty;

        /// <summary>
        /// Monitors that should always be disabled in fullscreen (TV) mode.
        /// Key = DevicePath, Value = FriendlyName.
        /// </summary>
        public ObservableCollection<KeyValuePair<string, string>> DisableInFullscreenList { get; set; }
            = new ObservableCollection<KeyValuePair<string, string>>();

        /// <summary>
        /// Monitors whose state should be preserved: if active before entering fullscreen,
        /// they will be re-enabled on return; if already inactive, they remain off.
        /// Key = DevicePath, Value = FriendlyName.
        /// </summary>
        public ObservableCollection<KeyValuePair<string, string>> PreserveStateList { get; set; }
            = new ObservableCollection<KeyValuePair<string, string>>();

        public bool SwitchDisplays { get; set; } = true;

        /// <summary>
        /// Seconds to wait after activating the TV before applying further display changes.
        /// Allows the TV time to be detected by Windows after HDMI signal appears.
        /// </summary>
        public int DisplayActivationDelaySecs { get; set; } = 3;

        // ---- Audio settings ------------------------------------------------------------------

        /// <summary>
        /// Ordered list of preferred audio devices for fullscreen (TV) mode.
        /// The first active device in the list will be used.
        /// Key = device ID, Value = friendly name.
        /// </summary>
        public ObservableCollection<KeyValuePair<string, string>> FullScreenAudioDeviceList { get; set; }
            = new ObservableCollection<KeyValuePair<string, string>>();

        /// <summary>Device ID to restore when returning to desktop (if AutoDetect is off).</summary>
        public string DefaultAudioDevice { get; set; } = string.Empty;

        public bool SwitchAudio { get; set; } = true;

        /// <summary>
        /// If true, the audio device active at the moment of entering fullscreen is saved
        /// and automatically restored on exit, ignoring DefaultAudioDevice.
        /// </summary>
        public bool AutoDetectAudioDevice { get; set; } = false;

        /// <summary>
        /// Seconds to keep retrying the audio switch after entering fullscreen.
        /// The TV audio endpoint may not be ACTIVE immediately after display activation.
        /// </summary>
        public int AudioRetryTimeoutSecs { get; set; } = 15;

        // ---- Runtime state (not serialized) --------------------------------------------------

        /// <summary>
        /// Snapshot of which PreserveState monitors were active just before entering fullscreen.
        /// Used to decide which ones to re-enable on exit.
        /// </summary>
        [DontSerialize]
        public HashSet<string> PreserveStateWasActive { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [DontSerialize]
        private Dictionary<string, string> _audioDevicesCache;

        // ---- Constructors --------------------------------------------------------------------

        public SwitchDisplaySettings() { }

        public SwitchDisplaySettings(SwitchDisplay plugin)
        {
            this.plugin = plugin;

            var saved = plugin.LoadPluginSettings<SwitchDisplaySettings>();
            if (saved != null)
            {
                FullscreenDisplay = saved.FullscreenDisplay;
                DefaultDisplay = saved.DefaultDisplay;
                DisableInFullscreenList = saved.DisableInFullscreenList;
                PreserveStateList = saved.PreserveStateList;
                SwitchDisplays = saved.SwitchDisplays;
                DisplayActivationDelaySecs = saved.DisplayActivationDelaySecs;
                FullScreenAudioDeviceList = saved.FullScreenAudioDeviceList;
                DefaultAudioDevice = saved.DefaultAudioDevice;
                SwitchAudio = saved.SwitchAudio;
                AutoDetectAudioDevice = saved.AutoDetectAudioDevice;
                AudioRetryTimeoutSecs = saved.AudioRetryTimeoutSecs;
            }
        }

        // ---- ISettings -----------------------------------------------------------------------

        public void BeginEdit()
        {
            RefreshAudioDevices();
        }

        public void CancelEdit() { }

        public void EndEdit()
        {
            plugin.SavePluginSettings(this);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }

        // ---- Enumeration helpers (used by settings UI) ---------------------------------------

        /// <summary>Active monitors — used to populate primary display dropdowns.</summary>
        [JsonIgnore]
        public Dictionary<string, string> EnumerateDisplays
        {
            get => plugin.Handler.EnumerateActive()
                .ToDictionary(m => m.DevicePath, m => m.DisplayLabel);
        }

        /// <summary>
        /// All known monitors (active + inactive) — used to populate the disable/preserve lists.
        /// Shows "(offline)" suffix for inactive monitors.
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, string> EnumerateAllDisplays
        {
            get => plugin.Handler.EnumerateAll()
                .ToDictionary(
                    m => m.DevicePath,
                    m => m.IsActive ? m.DisplayLabel : $"{m.DisplayLabel} (offline)");
        }

        [JsonIgnore]
        public Dictionary<string, string> EnumerateAudioDevices
        {
            get
            {
                if (_audioDevicesCache == null)
                {
                    try
                    {
                        _audioDevicesCache = plugin.AudioEnumerator
                            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                            .ToDictionary(d => d.ID, d => d.FriendlyName);
                    }
                    catch
                    {
                        _audioDevicesCache = new Dictionary<string, string>();
                    }
                }
                return _audioDevicesCache;
            }
        }

        public void RefreshAudioDevices() => _audioDevicesCache = null;

        // ---- List mutation helpers (called from View code-behind) ----------------------------

        public void AddDisableInFullscreen(string id, string name)
        {
            if (!string.IsNullOrEmpty(id) && !DisableInFullscreenList.Any(p => p.Key == id))
                DisableInFullscreenList.Add(new KeyValuePair<string, string>(id, name));
        }

        public void RemoveDisableInFullscreenAt(int index)
        {
            if (index >= 0 && index < DisableInFullscreenList.Count)
                DisableInFullscreenList.RemoveAt(index);
        }

        public void AddPreserveState(string id, string name)
        {
            if (!string.IsNullOrEmpty(id) && !PreserveStateList.Any(p => p.Key == id))
                PreserveStateList.Add(new KeyValuePair<string, string>(id, name));
        }

        public void RemovePreserveStateAt(int index)
        {
            if (index >= 0 && index < PreserveStateList.Count)
                PreserveStateList.RemoveAt(index);
        }

        public void AddFullscreenDeviceById(string id, string value)
        {
            if (!string.IsNullOrEmpty(id) && !FullScreenAudioDeviceList.Any(p => p.Key == id))
                FullScreenAudioDeviceList.Add(new KeyValuePair<string, string>(id, value));
        }

        public void RemoveFullscreenDeviceByIndex(int index)
        {
            if (index >= 0 && index < FullScreenAudioDeviceList.Count)
                FullScreenAudioDeviceList.RemoveAt(index);
        }

        public void MoveFullscreenDeviceUp(int index)
        {
            if (index > 0 && index < FullScreenAudioDeviceList.Count)
                FullScreenAudioDeviceList.Move(index, index - 1);
        }

        public void MoveFullscreenDeviceDown(int index)
        {
            if (index >= 0 && index < FullScreenAudioDeviceList.Count - 1)
                FullScreenAudioDeviceList.Move(index + 1, index);
        }
    }
}
