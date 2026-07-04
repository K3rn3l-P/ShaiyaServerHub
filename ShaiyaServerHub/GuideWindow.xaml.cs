using System.Windows;
using System.Windows.Input;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub
{
    /// <summary>
    /// Guida (solo spiegazione) alle funzioni speciali dei task: riavvio server e trigger
    /// su evento. Spiega COME funziona il generatore (finestra separata EventGenWindow) con
    /// snippet copiabili. Nessun input: la generazione avviene nel generatore dedicato.
    /// </summary>
    public partial class GuideWindow : Window
    {
        public GuideWindow()
        {
            InitializeComponent();
            TbEventExample.Text = EventTriggerBuilder.BuildQueryListXml("System",
                EventTriggerBuilder.BuildXPath(7036, EventTriggerBuilder.ScmProvider, "Shaiya Game Server", null));
        }

        private void Copy(string text)
        {
            try { Clipboard.SetText(text ?? ""); TbStatus.Text = L.CopiedToClipboard; } catch { }
        }
        private void CopyRestart_Click(object sender, RoutedEventArgs e) => Copy("/r /f /t 0");
        private void CopyStop_Click(object sender, RoutedEventArgs e) => Copy("/s /f /t 0");
        private void CopyFindCmd_Click(object sender, RoutedEventArgs e) => Copy(TbFindCmd.Text);

        private void BtnClose_Click(object sender, MouseButtonEventArgs e) => Close();
        private void BtnClose2_Click(object sender, RoutedEventArgs e) => Close();
        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    }
}
