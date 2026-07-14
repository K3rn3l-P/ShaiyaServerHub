using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ShaiyaServerHub.Core;
using ShaiyaServerHub.Core.Ai;

namespace ShaiyaServerHub.Controls
{
    /// <summary>
    /// Vista "AI" (Hub Brain): chat con l'AgentBrain + feed dei tool letti IN TEMPO REALE (callback di
    /// progresso, non solo al termine) + pulsante Stop per annullare una richiesta in corso. Dalla P2
    /// Blocco 2 esistono anche tool T2+ (es. kick_player) che possono finire in coda per approvazione
    /// Telegram (vedi <see cref="ApprovalWatchdog"/>, avviato qui) invece di eseguire subito. Dal
    /// Blocco 3, <see cref="WatchLoop"/> (avviato qui) fa sweep proattivi anche senza chat esplicita.
    /// Le Impostazioni AI stanno nel dialog Impostazioni generale del Hub (icona ⚙ qui apre lo stesso dialog).
    /// </summary>
    public partial class AiControl : UserControl
    {
        private HubSettings _settings;
        private ScriptRunner _runner;
        private AgentBrain _brain;
        // Storico CONDIVISO con la chat Telegram (vedi SharedChatSession) — una domanda fatta dall'Hub
        // è visibile anche da Telegram e viceversa, richiesta esplicita dell'utente.
        private AiTaskKind _mode = AiTaskKind.Default;
        private CancellationTokenSource _cts;
        private ApprovalWatchdog _approvalWatchdog;
        private WatchLoop _watchLoop;
        private TempBlockScheduler _tempBlockScheduler;
        private SecurityDigestScheduler _securityDigest;
        private AutorunsAuditScheduler _autorunsAudit;

        // Streaming della risposta AI corrente: testo grezzo mostrato subito (Run vivo), poi sostituito
        // con il markdown renderizzato a fine turno (vedi FinishAiStream) — così l'utente vede la
        // risposta man mano invece di aspettare l'intero riepilogo prima di vedere qualcosa.
        private Paragraph _streamParagraph;
        private Run _streamRun;
        private StringBuilder _streamBuffer;

        public AiControl()
        {
            InitializeComponent();
            RtbChat.Document = new FlowDocument { PagePadding = new Thickness(0) };
        }

        public void Init(HubSettings settings)
        {
            _settings = settings;
            _runner = new ScriptRunner(settings);
            AiPaths.EnsureFolders();
            _brain = new AgentBrain(settings, AiPaths.Root);
            CbAiMode.SelectedIndex = 0;   // Default: uso ordinario/routine (di norma openrouter/free)
            RefreshStatus();

            // Watchdog approvazioni T2+ (Telegram Approva/Nega/Prendo io + auto-esecuzione a grace
            // scaduta): vive per tutta la vita del processo Hub, indipendente dalla vista aperta —
            // stesso pattern degli altri timer dell'app (es. MainWindow._headerStatusTimer). Si
            // auto-disattiva nel proprio tick se Telegram non è configurato, quindi sicuro costruirlo sempre.
            // Brain: anche la chat testo libero da Telegram passa da qui (vedi ApprovalWatchdog).
            if (_approvalWatchdog == null) _approvalWatchdog = new ApprovalWatchdog(settings);
            _approvalWatchdog.Brain = _brain;

            // Sweep proattivo (Blocco 3): l'AI agisce anche senza che qualcuno le scriva in chat — vedi
            // WatchLoop per il perché è nel processo Hub invece di un CLI headless separato.
            if (_watchLoop == null) _watchLoop = new WatchLoop(settings, _brain);
            else _watchLoop.Brain = _brain;

            // Scadenza blocchi IP temporanei (P3): SEMPRE attivo, indipendente da AiMasterEnabled — vedi
            // TempBlockScheduler per il perché (rimuovere un blocco è sempre l'azione sicura).
            if (_tempBlockScheduler == null) _tempBlockScheduler = new TempBlockScheduler(settings);

            // Digest sicurezza/test/task ogni 15 min (finestra di grazia 30 min all'avvio) — vedi
            // SecurityDigestScheduler: zero costo LLM/Telegram se tutto è a posto, alert solo su anomalia.
            if (_securityDigest == null) _securityDigest = new SecurityDigestScheduler(settings, _brain);
            else _securityDigest.Brain = _brain;

            // Audit persistenza/ASEP 1x/giorno (autorunsc) — vedi AutorunsAuditScheduler: zero costo se
            // il tool non è scaricato, notifica Telegram solo su drift NUOVO rispetto all'ultimo avviso.
            if (_autorunsAudit == null) _autorunsAudit = new AutorunsAuditScheduler(settings);
        }

        private void CbAiMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = CbAiMode.SelectedItem as ComboBoxItem;
            _mode = (item?.Tag as string) == "Critical" ? AiTaskKind.Critical : AiTaskKind.Default;
        }

        private void RefreshStatus()
        {
            var rep = AiReport.Load(_settings);
            Color dot;
            string text;
            if (!rep.MasterEnabled) { dot = Color.FromRgb(0x66, 0x70, 0x7A); text = L.AiStatusOff; }
            else if (!rep.ApiKeyConfigured) { dot = Color.FromRgb(0xE8, 0xB0, 0x40); text = L.AiStatusNotConfigured; }
            else { dot = Color.FromRgb(0x4D, 0xC8, 0x70); text = L.AiStatusReady(rep.ToolCount); }
            AiStatusDot.Fill = new SolidColorBrush(dot);
            TbAiStatus.Text = text;
        }

        // ── Invio messaggio ──────────────────────────────────
        private async void BtnSend_Click(object sender, RoutedEventArgs e) => await SendAsync();

        private void TbInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                _ = SendAsync();
            }
        }

        private async Task SendAsync()
        {
            string msg = TbInput.Text?.Trim();
            if (string.IsNullOrEmpty(msg)) return;
            if (_brain == null) { RefreshStatus(); return; }

            // Storico condiviso con Telegram (SharedChatSession): un solo turno alla volta su TUTTO il
            // Hub. Se un turno da Telegram è già in corso, avvisa invece di sovrapporre due chiamate che
            // muterebbero la stessa history.
            if (!SharedChatSession.TryBeginTurn())
            {
                AppendChat("Tu", msg);
                MarkdownLite.AppendTurn(RtbChat.Document, "AI", "⏳ Sto già elaborando un'altra richiesta (probabilmente da Telegram) — riprova tra poco.", isAi: true);
                RtbChat.ScrollToEnd();
                return;
            }

            TbInput.Clear();
            AppendChat("Tu", msg);
            BeginAiStream();
            SetBusy(true);

            var history = SharedChatSession.Snapshot();
            var mode = _mode;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            AiChatTurnResult result = null;
            try
            {
                await Task.Run(() => result = _brain.Chat(msg, history, mode, OnProgress, ct, OnTextDelta));
            }
            finally { SharedChatSession.EndTurn(); }

            SetBusy(false);
            _cts = null;
            RefreshStatus();

            if (result == null) { FinishAiStream(L.Err("nessuna risposta")); return; }
            if (!string.IsNullOrWhiteSpace(result.ModelUsed)) TbAiModelInfo.Text = L.AiModelInfo(result.ModelUsed);

            if (result.Cancelled)
            {
                string partial = _streamBuffer?.ToString();
                FinishAiStream(string.IsNullOrWhiteSpace(partial) ? L.AiCancelled : partial + "\n\n*" + L.AiCancelled + "*");
                return;
            }
            if (!result.Ok)
            {
                FinishAiStream(L.Err(result.Error));
                return;
            }

            // Il feed è già stato popolato IN TEMPO REALE da OnProgress durante l'esecuzione; il testo
            // della risposta è già visibile grazie allo streaming — qui lo sostituiamo con la versione
            // markdown renderizzata (grassetto/titoli/tabelle) invece del testo grezzo accumulato.
            FinishAiStream(result.Answer);

            // Aggiorna lo storico condiviso (limitato per non far crescere il contesto all'infinito) —
            // visibile anche dalla chat Telegram da questo momento in poi.
            SharedChatSession.Append("user", msg);
            SharedChatSession.Append("assistant", result.Answer ?? "");
        }

        /// <summary>Apre un nuovo turno "AI" in chat: intestazione + paragrafo vuoto che riempiremo live.</summary>
        private void BeginAiStream()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(BeginAiStream)); return; }
            var doc = RtbChat.Document;
            var header = new Paragraph(new Run($"AI — {DateTime.Now:HH:mm:ss}"))
            {
                FontWeight = FontWeights.Bold, FontSize = 11, Margin = new Thickness(0, 10, 0, 2),
                Foreground = new SolidColorBrush(Color.FromRgb(0x7B, 0xE8, 0xC8))
            };
            doc.Blocks.Add(header);
            _streamRun = new Run("");
            _streamParagraph = new Paragraph(_streamRun) { Margin = new Thickness(0, 2, 0, 2) };
            doc.Blocks.Add(_streamParagraph);
            _streamBuffer = new StringBuilder();
            RtbChat.ScrollToEnd();
        }

        /// <summary>Chiamata da AgentBrain ad ogni frammento di testo generato (streaming SSE).</summary>
        private void OnTextDelta(string chunk)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnTextDelta(chunk))); return; }
            if (_streamRun == null || string.IsNullOrEmpty(chunk)) return;
            _streamBuffer.Append(chunk);
            _streamRun.Text += chunk;
            RtbChat.ScrollToEnd();
        }

        /// <summary>Chiude il turno "AI": rimuove il testo grezzo accumulato e lo sostituisce col markdown renderizzato.</summary>
        private void FinishAiStream(string finalText)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => FinishAiStream(finalText))); return; }
            var doc = RtbChat.Document;
            if (_streamParagraph != null) { doc.Blocks.Remove(_streamParagraph); _streamParagraph = null; _streamRun = null; }
            MarkdownLite.AppendBlocks(doc, string.IsNullOrWhiteSpace(finalText) ? "(risposta vuota)" : finalText);
            RtbChat.ScrollToEnd();
        }

        /// <summary>
        /// Chiamata da AgentBrain sul thread di background ad ogni passo (chiamata modello, tool in
        /// corso/completato): aggiorna lo stato live in toolbar E il feed permanente. Marshala da sé
        /// verso il thread UI.
        /// </summary>
        private void OnProgress(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnProgress(text))); return; }
            TbAiStatus.Text = text;
            AppendFeed(text);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void SetBusy(bool busy)
        {
            BtnSend.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            BtnStop.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            TbInput.IsEnabled = !busy;
        }

        private void AppendChat(string who, string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => AppendChat(who, text))); return; }
            MarkdownLite.AppendTurn(RtbChat.Document, who, text, isAi: who == "AI");
            RtbChat.ScrollToEnd();
        }

        private void AppendFeed(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => AppendFeed(text))); return; }
            TbFeed.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
            TbFeed.ScrollToEnd();
        }

        private void BtnCopyChat_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var range = new TextRange(RtbChat.Document.ContentStart, RtbChat.Document.ContentEnd);
                if (!string.IsNullOrEmpty(range.Text)) Clipboard.SetText(range.Text);
            }
            catch { }
        }

        private void BtnCopyFeed_Click(object sender, RoutedEventArgs e)
        {
            try { if (!string.IsNullOrEmpty(TbFeed.Text)) Clipboard.SetText(TbFeed.Text); } catch { }
        }

        private void BtnClearChat_Click(object sender, RoutedEventArgs e)
        {
            RtbChat.Document = new FlowDocument { PagePadding = new Thickness(0) };
            TbFeed.Clear();
            SharedChatSession.Clear(); // storico condiviso anche con Telegram — "pulisci" pulisce entrambi
        }

        // ── Impostazioni AI: stesso dialog Impostazioni generale del Hub ──
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(_settings, _runner, s => AppendFeed(s)) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                _settings.Save();
                _runner = new ScriptRunner(_settings);
                _brain = new AgentBrain(_settings, AiPaths.Root);   // ricrea con eventuali nuove credenziali
                if (_watchLoop != null) _watchLoop.Brain = _brain;  // WatchLoop non deve restare sull'AgentBrain vecchio
                if (_securityDigest != null) _securityDigest.Brain = _brain;
                if (_approvalWatchdog != null) _approvalWatchdog.Brain = _brain;
                RefreshStatus();
            }
        }
    }
}
