using NAudio.CoreAudioApi;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

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

        /// <summary>Audio device that was active before entering fullscreen (for AutoDetect restore).</summary>
        private string _initialAudioDeviceId;

        /// <summary>CancellationToken for the audio retry background task.</summary>
        private CancellationTokenSource _audioRetryCts;

        public SwitchDisplay(IPlayniteAPI api) : base(api)
        {
            settings = new SwitchDisplaySettings(this);
            this.api = api;
            Handler = new DisplayHandler();
            AudioEnumerator = new MMDeviceEnumerator();
            _policyConfigClient = new PolicyConfigClient();

            Properties = new GenericPluginProperties { HasSettings = true };
        }

        // -----------------------------------------------------------------------------------------
        // Playnite events
        // -----------------------------------------------------------------------------------------

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (api.ApplicationInfo.Mode != ApplicationMode.Fullscreen) return;

            Task.Run(() =>
            {
                try
                {
                    if (settings.SwitchDisplays) ApplyTVDisplayLayout();
                    if (settings.SwitchAudio)    ApplyTVAudio();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Unhandled error during TV mode activation.");
                }
            });
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            if (api.ApplicationInfo.Mode != ApplicationMode.Fullscreen) return;

            // Cancel any pending audio retry
            _audioRetryCts?.Cancel();

            Task.Run(() =>
            {
                try
                {
                    if (settings.SwitchDisplays) RestoreDesktopDisplayLayout();
                    if (settings.SwitchAudio)    RestoreDesktopAudio();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Unhandled error during desktop mode restoration.");
                }
            });
        }

        // -----------------------------------------------------------------------------------------
        // Display — enter TV mode
        // -----------------------------------------------------------------------------------------

        private void ApplyTVDisplayLayout()
        {
            // 1. Save state of "preserve" monitors before touching anything
            settings.PreserveStateWasActive.Clear();
            foreach (var entry in settings.PreserveStateList)
            {
                if (Handler.IsMonitorActive(entry.Key))
                    settings.PreserveStateWasActive.Add(entry.Key);
            }

            // 2. Activate the TV (may be inactive if it was powered off)
            if (!string.IsNullOrEmpty(settings.FullscreenDisplay))
            {
                if (!Handler.IsMonitorActive(settings.FullscreenDisplay))
                {
                    logger.Info($"TV not active — activating: {settings.FullscreenDisplay}");
                    bool activated = Handler.ActivateMonitor(settings.FullscreenDisplay);
                    if (!activated)
                        logger.Error($"Failed to activate TV: {settings.FullscreenDisplay}");

                    // Wait for Windows to process the display change (same as timeout in your script)
                    int delaySecs = Math.Max(1, settings.DisplayActivationDelaySecs);
                    logger.Info($"Waiting {delaySecs}s for TV to be detected...");
                    Thread.Sleep(delaySecs * 1000);
                }

                // 3. Set TV as primary
                bool primary = Handler.SetPrimaryMonitor(settings.FullscreenDisplay);
                if (!primary)
                    logger.Error($"Failed to set TV as primary: {settings.FullscreenDisplay}");
                else
                    logger.Info($"TV set as primary: {settings.FullscreenDisplay}");
            }

            // 4. Disable monitors that must always be off in TV mode
            foreach (var entry in settings.DisableInFullscreenList)
            {
                if (Handler.IsMonitorActive(entry.Key))
                {
                    bool ok = Handler.DisableMonitor(entry.Key);
                    logger.Info(ok
                        ? $"Disabled monitor: {entry.Value}"
                        : $"Failed to disable monitor: {entry.Value}");
                }
            }

            // 5. Disable preserve-state monitors (they are always off in TV mode)
            foreach (var entry in settings.PreserveStateList)
            {
                if (Handler.IsMonitorActive(entry.Key))
                {
                    bool ok = Handler.DisableMonitor(entry.Key);
                    logger.Info(ok
                        ? $"Disabled preserve-state monitor: {entry.Value}"
                        : $"Failed to disable preserve-state monitor: {entry.Value}");
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // Display — restore desktop mode
        // -----------------------------------------------------------------------------------------

        private void RestoreDesktopDisplayLayout()
        {
            // 1. Set desktop primary
            if (!string.IsNullOrEmpty(settings.DefaultDisplay))
            {
                if (!Handler.IsMonitorActive(settings.DefaultDisplay))
                {
                    bool activated = Handler.ActivateMonitor(settings.DefaultDisplay);
                    if (!activated)
                        logger.Error($"Failed to activate desktop primary: {settings.DefaultDisplay}");
                    else
                        Thread.Sleep(Math.Max(1, settings.DisplayActivationDelaySecs) * 1000);
                }

                bool primary = Handler.SetPrimaryMonitor(settings.DefaultDisplay);
                logger.Info(primary
                    ? $"Desktop monitor set as primary: {settings.DefaultDisplay}"
                    : $"Failed to set desktop primary: {settings.DefaultDisplay}");
            }

            // 2. Disable TV
            if (!string.IsNullOrEmpty(settings.FullscreenDisplay) && Handler.IsMonitorActive(settings.FullscreenDisplay))
            {
                bool ok = Handler.DisableMonitor(settings.FullscreenDisplay);
                logger.Info(ok
                    ? $"TV disabled: {settings.FullscreenDisplay}"
                    : $"Failed to disable TV: {settings.FullscreenDisplay}");
            }

            // 3. Re-enable "always disable in TV" monitors
            foreach (var entry in settings.DisableInFullscreenList)
            {
                if (!Handler.IsMonitorActive(entry.Key))
                {
                    bool ok = Handler.EnableMonitor(entry.Key);
                    logger.Info(ok
                        ? $"Re-enabled monitor: {entry.Value}"
                        : $"Failed to re-enable monitor: {entry.Value}");
                }
            }

            // 4. Restore preserve-state monitors only if they were active before entering TV mode
            foreach (var entry in settings.PreserveStateList)
            {
                bool wasActive = settings.PreserveStateWasActive.Contains(entry.Key);
                bool isActive = Handler.IsMonitorActive(entry.Key);

                if (wasActive && !isActive)
                {
                    bool ok = Handler.EnableMonitor(entry.Key);
                    logger.Info(ok
                        ? $"Restored preserve-state monitor: {entry.Value}"
                        : $"Failed to restore preserve-state monitor: {entry.Value}");
                }
                else if (!wasActive)
                {
                    logger.Info($"Preserve-state monitor was off before, leaving off: {entry.Value}");
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // Audio — enter TV mode (with retry loop)
        // -----------------------------------------------------------------------------------------

        private void ApplyTVAudio()
        {
            if (settings.FullScreenAudioDeviceList.Count == 0) return;

            // Save current device for auto-restore
            if (settings.AutoDetectAudioDevice)
            {
                try { _initialAudioDeviceId = AudioEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
                catch (Exception ex) { logger.Error(ex, "Could not read current audio endpoint."); }
            }

            _audioRetryCts?.Cancel();
            _audioRetryCts = new CancellationTokenSource();
            var token = _audioRetryCts.Token;

            int timeoutMs = Math.Max(5, settings.AudioRetryTimeoutSecs) * 1000;
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
            {
                settings.RefreshAudioDevices();
                var currentDevices = settings.EnumerateAudioDevices;

                foreach (var preferred in settings.FullScreenAudioDeviceList)
                {
                    string targetId = ResolveAudioDeviceId(preferred, currentDevices);
                    if (string.IsNullOrEmpty(targetId)) continue;

                    try
                    {
                        _policyConfigClient.SetDefaultEndpoint(targetId, Role.Multimedia);
                        _policyConfigClient.SetDefaultEndpoint(targetId, Role.Console);
                        _policyConfigClient.SetDefaultEndpoint(targetId, Role.Communications);
                        logger.Info($"Audio switched to: {preferred.Value} ({targetId})");
                        return; // success
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Failed to set audio endpoint: {targetId}");
                    }
                }

                // Device not active yet — wait and retry
                logger.Info("TV audio device not active yet, retrying in 1s...");
                Thread.Sleep(1000);
            }

            if (!token.IsCancellationRequested)
                logger.Warn($"TV audio device not found after {settings.AudioRetryTimeoutSecs}s timeout.");
        }

        // -----------------------------------------------------------------------------------------
        // Audio — restore desktop mode
        // -----------------------------------------------------------------------------------------

        private void RestoreDesktopAudio()
        {
            string restoreId = settings.AutoDetectAudioDevice
                ? _initialAudioDeviceId
                : settings.DefaultAudioDevice;

            if (string.IsNullOrEmpty(restoreId)) return;

            try
            {
                _policyConfigClient.SetDefaultEndpoint(restoreId, Role.Multimedia);
                _policyConfigClient.SetDefaultEndpoint(restoreId, Role.Console);
                _policyConfigClient.SetDefaultEndpoint(restoreId, Role.Communications);
                logger.Info($"Audio restored to: {restoreId}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to restore audio endpoint: {restoreId}");
            }
        }

        // -----------------------------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Resolves a preferred audio device to its current active ID.
        /// First tries exact ID match, then falls back to friendly name match
        /// (handles cases where Windows reassigns IDs after reconnection).
        /// </summary>
        private string ResolveAudioDeviceId(KeyValuePair<string, string> preferred, Dictionary<string, string> activeDevices)
        {
            // Exact ID match
            if (activeDevices.ContainsKey(preferred.Key))
                return preferred.Key;

            // Name fallback
            var byName = activeDevices.FirstOrDefault(p =>
                p.Value.Equals(preferred.Value, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(byName.Key))
            {
                logger.Warn($"Audio device ID changed for '{preferred.Value}'. Old: {preferred.Key} New: {byName.Key}");
                return byName.Key;
            }

            return null;
        }

        // -----------------------------------------------------------------------------------------
        // Playnite plugin boilerplate
        // -----------------------------------------------------------------------------------------

        public override ISettings GetSettings(bool firstRunSettings) => settings;

        public override UserControl GetSettingsView(bool firstRunSettings)
            => new SwitchDisplaySettingsView(settings);
    }
}
