using System.Windows;
using System.Windows.Input;

namespace ShaiyaServerHub
{
    /// <summary>Mini-guida modale: ruoli/tier, allowlist, profili chiamante, audit di PSM_Cmd.</summary>
    public partial class PsmCmdGuideWindow : Window
    {
        public PsmCmdGuideWindow() { InitializeComponent(); }
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
