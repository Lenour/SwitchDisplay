using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SwitchDisplay
{
    public partial class SwitchDisplaySettingsView : UserControl
    {
        private SwitchDisplaySettings settings { get; set; }

        public SwitchDisplaySettingsView(SwitchDisplaySettings settings)
        {
            this.settings = settings;
            InitializeComponent();
        }

        // ---- Always-disable-in-TV list -------------------------------------------------------

        private void AddDisableInFullscreen_Click(object sender, RoutedEventArgs e)
        {
            var item = DisableInFullscreenCombo.SelectedItem;
            if (item == null) return;
            var kv = (KeyValuePair<string, string>)item;
            settings.AddDisableInFullscreen(kv.Key, kv.Value);
        }

        private void RemoveDisableInFullscreen_Click(object sender, RoutedEventArgs e)
        {
            settings.RemoveDisableInFullscreenAt(DisableInFullscreenListBox.SelectedIndex);
        }

        // ---- Preserve-state list -------------------------------------------------------------

        private void AddPreserveState_Click(object sender, RoutedEventArgs e)
        {
            var item = PreserveStateCombo.SelectedItem;
            if (item == null) return;
            var kv = (KeyValuePair<string, string>)item;
            settings.AddPreserveState(kv.Key, kv.Value);
        }

        private void RemovePreserveState_Click(object sender, RoutedEventArgs e)
        {
            settings.RemovePreserveStateAt(PreserveStateListBox.SelectedIndex);
        }

        // ---- Audio device list ---------------------------------------------------------------

        private void AddFullscreenAudioDevice_Click(object sender, RoutedEventArgs e)
        {
            var item = FullscreenAudioDeviceList.SelectedItem;
            if (item == null) return;
            var kv = (KeyValuePair<string, string>)item;
            settings.AddFullscreenDeviceById(kv.Key, kv.Value);
        }

        private void MoveAudioDeviceUp_Click(object sender, RoutedEventArgs e)
        {
            settings.MoveFullscreenDeviceUp(DevicesOrderList.SelectedIndex);
        }

        private void MoveAudioDeviceDown_Click(object sender, RoutedEventArgs e)
        {
            settings.MoveFullscreenDeviceDown(DevicesOrderList.SelectedIndex);
        }

        private void RemoveAudioDevice_Click(object sender, RoutedEventArgs e)
        {
            settings.RemoveFullscreenDeviceByIndex(DevicesOrderList.SelectedIndex);
        }
    }
}
