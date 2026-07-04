using System.Windows;
using System.Windows.Input;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub
{
    /// <summary>Modale per definire un trigger (aggiuntivo o in modifica). Restituisce un ScheduleSpec.</summary>
    public partial class TriggerDialog : Window
    {
        public ScheduleSpec ResultSpec { get; private set; }

        public TriggerDialog(ScheduleSpec initial = null, bool editing = false)
        {
            InitializeComponent();
            if (initial != null) Picker.SetSpec(initial);
            if (editing) TbHeader.Text = L.TrigDlgEditTitle;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (!Picker.TryBuild(out var spec, out string error))
            {
                TbError.Text = error;
                TbError.Visibility = Visibility.Visible;
                return;
            }
            ResultSpec = spec;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
        private void BtnClose_Click(object sender, MouseButtonEventArgs e) { DialogResult = false; Close(); }
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    }
}
