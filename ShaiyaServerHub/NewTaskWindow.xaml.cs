using System;
using System.Windows;
using System.Windows.Input;

namespace ShaiyaServerHub
{
    /// <summary>Modale "Nuovo task pianificato" — ospita il NewTaskControl riutilizzabile.</summary>
    public partial class NewTaskWindow : Window
    {
        public NewTaskWindow(string scriptPath, Action<string> logSink)
        {
            InitializeComponent();
            Form.LogSink = logSink;
            Form.SetScript(scriptPath);
            Form.Created += () => { DialogResult = true; Close(); };
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
        private void BtnClose_Click(object sender, MouseButtonEventArgs e) { DialogResult = false; Close(); }
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    }
}
