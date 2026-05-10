using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using System.Collections.Specialized;
using System.Windows;
using System.Collections.ObjectModel;

namespace SwitchDisplay
{
    public class SwitchDisplaySettings : ObservableObject, ISettings
    {
        private SwitchDisplay plugin;

        public string FullscreenDisplay { get; set; } = string.Empty;
        public string DefaultDisplay { get; set; } = string.Empty;
        public string DefaultAudioDevice { get; set; } = string.Empty;
        public ObservableCollection<KeyValuePair<string, string>> FullScreenAudioDeviceList { get; set; } = new ObservableCollection<KeyValuePair<string, string>>();
        public bool SwitchDisplays { get; set; } = true;
        public bool SwitchAudio { get; set; } = true;
        public bool AutoDetectAudioDevice { get; set; } = false;

        [DontSerialize]
        private Dictionary<string, string> _audioDevices;

        // Parameterless constructor required for LoadPluginSettings
        public SwitchDisplaySettings()
        {
        }

        public SwitchDisplaySettings(SwitchDisplay plugin)
        {
            this.plugin = plugin;

            var savedSettings = plugin.LoadPluginSettings<SwitchDisplaySettings>();

            if (savedSettings != null)
            {
                FullscreenDisplay = savedSettings.FullscreenDisplay;
                DefaultDisplay = savedSettings.DefaultDisplay;
                DefaultAudioDevice = savedSettings.DefaultAudioDevice;
                FullScreenAudioDeviceList = savedSettings.FullScreenAudioDeviceList;
                SwitchDisplays = savedSettings.SwitchDisplays;
                SwitchAudio = savedSettings.SwitchAudio;
                AutoDetectAudioDevice = savedSettings.AutoDetectAudioDevice;
            }
        }

        public void BeginEdit()
        {
            // Force refresh of audio devices when settings are opened
            RefreshAudioDevices();
        }

        public void CancelEdit()
        {
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(this);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }

        /// <summary>
        /// Clears the audio device cache so the next access re-enumerates.
        /// Call this before switching audio to ensure IDs are current.
        /// </summary>
        public void RefreshAudioDevices()
        {
            _audioDevices = null;
        }

        [JsonIgnore]
        public Dictionary<string, string> EnumerateDisplays
        {
            get => plugin.Handler.Enumerate().ToDictionary(
                display => display.DeviceName,
                display => String.Format(
                    ResourceProvider.GetString("LOCSwitchDisplayDisplayString"),
                    display.MonitorString,
                    display.DeviceString));
        }

        [JsonIgnore]
        public Dictionary<string, string> EnumerateAudioDevices
        {
            get
            {
                if (_audioDevices == null)
                {
                    try
                    {
                        _audioDevices = plugin.AudioEnumerator
                            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                            .ToDictionary(audio => audio.ID, audio => audio.FriendlyName);
                    }
                    catch (Exception)
                    {
                        _audioDevices = new Dictionary<string, string>();
                    }
                }
                return _audioDevices;
            }
        }

        public void AddFullscreenDeviceById(string id, string value)
        {
            if (id.Length > 0 && !FullScreenAudioDeviceList.Any(p => p.Key == id))
            {
                FullScreenAudioDeviceList.Add(new KeyValuePair<string, string>(id, value));
            }
        }

        public void RemoveFullscreenDeviceByIndex(int index)
        {
            if (index > -1 && FullScreenAudioDeviceList.Count > index)
            {
                FullScreenAudioDeviceList.RemoveAt(index);
            }
        }

        public void MoveFullscreenDeviceUp(int index)
        {
            if (index > 0 && FullScreenAudioDeviceList.Count > index)
            {
                FullScreenAudioDeviceList.Move(index, index - 1);
            }
        }

        public void MoveFullscreenDeviceDown(int index)
        {
            if (index > -1 && FullScreenAudioDeviceList.Count - 1 > index)
            {
                FullScreenAudioDeviceList.Move(index + 1, index);
            }
        }
    }
}
