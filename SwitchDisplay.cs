using NAudio.CoreAudioApi;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Playnite.SDK.Events;

namespace SwitchDisplay
{
    public class SwitchDisplay : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private SwitchDisplaySettings settings { get; set; }
        private IPlayniteAPI api { get; set; }

        public override Guid Id { get; } = Guid.Parse("75b4d2cc-8308-4c34-8aeb-4dd9a012586d");

        private PolicyConfigClient _policyConfigClient;
        public MMDeviceEnumerator AudioEnumerator { get; set; }
        public DisplayHandler Handler { get; set; }

        private string initialAudioDevice;

        public SwitchDisplay(IPlayniteAPI api) : base(api)
        {
            settings = new SwitchDisplaySettings(this);
            this.api = api;
            Handler = new DisplayHandler();
            AudioEnumerator = new MMDeviceEnumerator();
            _policyConfigClient = new PolicyConfigClient();

            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (api.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                // Switch Display
                if (settings.SwitchDisplays && !String.IsNullOrEmpty(settings.FullscreenDisplay))
                {
                    if (!Handler.SwitchPrimaryDisplay(settings.FullscreenDisplay))
                    {
                        logger.Error(String.Format("Error setting primary display: {0}", settings.FullscreenDisplay));
                    }
                }

                // Switch Audio
                if (settings.SwitchAudio && settings.FullScreenAudioDeviceList.Count > 0)
                {
                    // Save current audio device ID before switching
                    if (settings.AutoDetectAudioDevice)
                    {
                        try
                        {
                            initialAudioDevice = AudioEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Failed to get current default audio endpoint.");
                        }
                    }

                    // Refresh enumerated devices so IDs are current
                    settings.RefreshAudioDevices();
                    var currentDevices = settings.EnumerateAudioDevices;

                    // Try each preferred device in priority order
                    bool audioSwitched = false;
                    foreach (KeyValuePair<string, string> device in settings.FullScreenAudioDeviceList)
                    {
                        string targetId = null;

                        // First try to match by ID (most reliable)
                        if (currentDevices.ContainsKey(device.Key))
                        {
                            targetId = device.Key;
                        }
                        // Fall back to matching by friendly name (handles device reconnection with new ID)
                        else
                        {
                            var byName = currentDevices.FirstOrDefault(p => p.Value.Equals(device.Value, StringComparison.OrdinalIgnoreCase));
                            if (!string.IsNullOrEmpty(byName.Key))
                            {
                                targetId = byName.Key;
                                logger.Warn(String.Format("Audio device ID changed, matched by name '{0}'. Old ID: {1}, New ID: {2}", device.Value, device.Key, targetId));
                            }
                        }

                        if (!string.IsNullOrEmpty(targetId))
                        {
                            try
                            {
                                _policyConfigClient.SetDefaultEndpoint(targetId, Role.Multimedia);
                                _policyConfigClient.SetDefaultEndpoint(targetId, Role.Console);
                                audioSwitched = true;
                                logger.Info(String.Format("Switched audio to: {0} ({1})", device.Value, targetId));
                            }
                            catch (Exception ex)
                            {
                                logger.Error(ex, String.Format("Failed to set audio endpoint: {0}", targetId));
                            }
                            break;
                        }
                    }

                    if (!audioSwitched)
                    {
                        logger.Warn("No matching audio device found for fullscreen mode.");
                    }
                }
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            if (api.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                // Restore Display
                if (settings.SwitchDisplays && !String.IsNullOrEmpty(settings.DefaultDisplay))
                {
                    if (!Handler.SwitchPrimaryDisplay(settings.DefaultDisplay))
                    {
                        logger.Error(String.Format("Error restoring primary display: {0}", settings.DefaultDisplay));
                    }
                }

                // Restore Audio
                if (settings.SwitchAudio)
                {
                    string restoreId = settings.AutoDetectAudioDevice ? initialAudioDevice : settings.DefaultAudioDevice;

                    if (!string.IsNullOrEmpty(restoreId))
                    {
                        try
                        {
                            _policyConfigClient.SetDefaultEndpoint(restoreId, Role.Multimedia);
                            _policyConfigClient.SetDefaultEndpoint(restoreId, Role.Console);
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, String.Format("Failed to restore audio endpoint: {0}", restoreId));
                        }
                    }
                }
            }
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new SwitchDisplaySettingsView(settings);
        }
    }
}
