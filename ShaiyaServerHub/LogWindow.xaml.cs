using System.Windows;
using System.Windows.Input;

namespace ShaiyaServerHub
{
    /// <summary>Log modale dettagliato (snapshot del log di un'area).</summary>
    public partial class LogWindow : Window
    {
        public LogWindow(string title, string logText)
        {
            InitializeComponent();
            TbTitle.Text = title;
            TbLog.Text = string.IsNullOrEmpty(logText) ? "(log vuoto)" : logText;
            TbLog.ScrollToEnd();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(TbLog.Text); } catch { }
        }

        private void BtnClose_Click(object sender, MouseButtonEventArgs e) => Close();
        private void BtnClose2_Click(object sender, RoutedEventArgs e) => Close();
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    }
}
