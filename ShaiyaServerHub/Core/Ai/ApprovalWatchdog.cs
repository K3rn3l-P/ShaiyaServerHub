using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Poller periodico per il ciclo di approvazione T2+ — vedi PLAN §Componenti codice. Ogni ~7s:
    /// (1) legge i click sui bottoni Telegram (Approva/Nega/Prendo io) e risolve la
    /// <see cref="ActionQueue"/> corrispondente, eseguendo il tool SOLO se approvato; (2) esegue le
    /// azioni la cui finestra di grace è scaduta senza risposta (auto-eseguite, notifica dopo);
    /// (3) legge i messaggi di TESTO LIBERO Telegram e li inoltra ad <see cref="AgentBrain.Chat"/> con la
    /// stessa <see cref="SharedChatSession"/> usata dalla chat del Hub UI — chat 2 vie completa dal
    /// telefono, non solo Approva/Nega. Nessuna azione parte MAI da qui senza essere già passata da
    /// <see cref="PolicyGate.Evaluate"/> in precedenza (questa classe esegue solo ciò che è già in coda,
    /// o inoltra un prompt a Chat che applica le stesse regole di qualunque altro turno).
    /// Vive per tutta la durata del processo Hub (avviato da <c>AiControl.Init</c>, come gli altri timer
    /// dell'app — es. <c>MainWindow._headerStatusTimer</c>): copre "l'admin è al Hub aperto", non ancora
    /// il caso pienamente non presidiato H24 (quello resta <c>--ai-watch</c>, feature futura separata).
    /// </summary>
    public class ApprovalWatchdog : IDisposable
    {
        private readonly HubSettings _settings;
        private readonly ToolRegistry _tools;
        private readonly Timer _timer;
        private volatile bool _busy;

        /// <summary>Non readonly: se le Impostazioni cambiano, AiControl ricrea AgentBrain e aggiorna questo riferimento.</summary>
        public AgentBrain Brain;

        // Registrazione menu comandi (setMyCommands) — un tentativo per tick finché non riesce (es. se
        // Telegram non era ancora configurato all'avvio Hub), poi mai più: idempotente ma non serve
        // richiamarla ad ogni giro.
        private bool _commandsRegistered;

        public ApprovalWatchdog(HubSettings settings)
        {
            _settings = settings;
            _tools = new ToolRegistry(new AiToolContext { Settings = settings });
            _timer = new Timer(Tick, null, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7));
        }

        private void Tick(object state)
        {
            if (_busy) return; // un giro lento (rete) non deve sovrapporsi al successivo
            _busy = true;
            try
            {
                if (!_settings.AiMasterEnabled || !Notifier.IsConfigured(_settings)) return;

                if (!_commandsRegistered)
                    _commandsRegistered = TelegramClient.SetMyCommands(_settings.TelegramBotToken, TelegramCommands.All, out _);

                ProcessTelegramCallbacks();
                ProcessGraceExpired();
            }
            catch (Exception ex) { App.WriteLog("AI-APPROVAL-WATCHDOG", "errore tick: " + ex.Message); }
            finally { _busy = false; }
        }

        private void ProcessTelegramCallbacks()
        {
            var callbacks = TelegramClient.PollCallbacks(_settings.TelegramBotToken, out var messages, out var error);
            if (error != null) { App.WriteLog("AI-APPROVAL-WATCHDOG", "poll Telegram: " + error); return; }

            ProcessTelegramMessages(messages);

            foreach (var cb in callbacks)
            {
                if (!ActionQueue.TryGet(cb.ActionId, out var action) || action.Status != PendingActionStatus.Pending)
                {
                    TelegramClient.AnswerCallback(_settings.TelegramBotToken, cb.CallbackQueryId, "Azione già risolta o non trovata.");
                    continue;
                }

                switch (cb.Decision?.ToLowerInvariant())
                {
                    case "approve":
                        if (ActionQueue.TryResolve(action.Id, PendingActionStatus.Approved))
                        {
                            string result = ExecuteQueuedAction(action);
                            ActionQueue.MarkResult(action.Id, PendingActionStatus.Approved, result);
                            TelegramClient.AnswerCallback(_settings.TelegramBotToken, cb.CallbackQueryId, "✅ Approvata ed eseguita.");
                            Notifier.NotifyInfo(_settings, $"✅ [{action.Id}] approvata da {cb.FromUser ?? "?"} ed eseguita.\n{result}");
                            App.WriteLog("AI-APPROVAL-WATCHDOG", $"APPROVATA {action.Id} ({action.ToolName}) da {cb.FromUser} → {result}");
                            OutcomeTracker.Record(action.ToolName, "Approved", $"da {cb.FromUser}: {result}");
                        }
                        break;

                    case "deny":
                        if (ActionQueue.TryResolve(action.Id, PendingActionStatus.Denied))
                        {
                            TelegramClient.AnswerCallback(_settings.TelegramBotToken, cb.CallbackQueryId, "❌ Negata.");
                            Notifier.NotifyInfo(_settings, $"❌ [{action.Id}] negata da {cb.FromUser ?? "?"} — nessuna azione eseguita.");
                            App.WriteLog("AI-APPROVAL-WATCHDOG", $"NEGATA {action.Id} ({action.ToolName}) da {cb.FromUser}");
                            OutcomeTracker.Record(action.ToolName, "Denied", $"da {cb.FromUser}: {action.Reasoning}");
                        }
                        break;

                    case "takeover":
                        if (ActionQueue.TryResolve(action.Id, PendingActionStatus.TakenOver))
                        {
                            TelegramClient.AnswerCallback(_settings.TelegramBotToken, cb.CallbackQueryId, "🙋 Presa in carico.");
                            Notifier.NotifyInfo(_settings, $"🙋 [{action.Id}] presa in carico da {cb.FromUser ?? "?"} — l'AI non interviene più su questa.");
                            App.WriteLog("AI-APPROVAL-WATCHDOG", $"PRESA IN CARICO {action.Id} ({action.ToolName}) da {cb.FromUser}");
                            OutcomeTracker.Record(action.ToolName, "TakenOver", $"da {cb.FromUser}");
                        }
                        break;

                    default:
                        TelegramClient.AnswerCallback(_settings.TelegramBotToken, cb.CallbackQueryId, "Decisione non riconosciuta.");
                        break;
                }
            }
        }

        private void ProcessGraceExpired()
        {
            foreach (var action in ActionQueue.GetDueForAutoExecution())
            {
                // "Claim" atomico: se un'altra risoluzione (Telegram) è arrivata nello stesso istante,
                // TryResolve fallisce e questa azione viene semplicemente saltata qui — nessuna doppia esecuzione.
                if (!ActionQueue.TryResolve(action.Id, PendingActionStatus.AutoExecuted)) continue;

                string result = ExecuteQueuedAction(action);
                ActionQueue.MarkResult(action.Id, PendingActionStatus.AutoExecuted, result);
                Notifier.NotifyInfo(_settings, $"⏱ [{action.Id}] eseguita automaticamente (nessuna risposta entro la scadenza).\n{result}");
                App.WriteLog("AI-APPROVAL-WATCHDOG", $"AUTO-ESEGUITA {action.Id} ({action.ToolName}) → {result}");
                OutcomeTracker.Record(action.ToolName, "AutoExecuted", $"grace scaduta: {result}");
            }
        }

        /// <summary>
        /// Messaggi di testo libero (non bottoni) — solo dalla chat configurata. Instrada tre casi:
        /// (1) <c>/help</c> → risposta statica, MAI un turno LLM (sempre disponibile anche a OpenRouter giù);
        /// (2) comando noto (<see cref="TelegramCommands"/>) → espanso nel prompt precompilato, il testo
        ///     ORIGINALE (es. "/db_audit") resta comunque quello salvato nello storico condiviso, così la
        ///     conversazione si legge naturalmente se richiamata da un altro canale;
        /// (3) comando sconosciuto (inizia per "/" ma non in elenco) → avviso statico verso /help, MAI un
        ///     turno LLM sprecato su un probabile refuso;
        /// (4) testo libero → invariato, passa così com'è.
        /// </summary>
        private void ProcessTelegramMessages(List<TelegramTextMessage> messages)
        {
            if (messages == null || messages.Count == 0 || Brain == null) return;
            foreach (var m in messages)
            {
                if (string.IsNullOrEmpty(m.ChatId) || !string.Equals(m.ChatId, _settings.TelegramChatId, StringComparison.Ordinal))
                {
                    App.WriteLog("AI-APPROVAL-WATCHDOG", $"messaggio Telegram da chat non configurata ({m.ChatId}) ignorato.");
                    continue;
                }

                string text = m.Text?.Trim() ?? "";
                if (text.StartsWith("/"))
                {
                    string cmd = text.Split(' ')[0].TrimStart('/').ToLowerInvariant();
                    if (cmd == "help")
                    {
                        Notifier.NotifyInfo(_settings, TelegramCommands.HelpText());
                        continue;
                    }
                    if (TelegramCommands.TryGet(cmd, out var entry))
                    {
                        HandleTelegramChatMessage(m, effectivePrompt: entry.Prompt);
                        continue;
                    }
                    Notifier.NotifyInfo(_settings, $"Comando /{cmd} non riconosciuto. Usa /help per l'elenco.");
                    continue;
                }

                HandleTelegramChatMessage(m, effectivePrompt: text);
            }
        }

        /// <summary>
        /// Inoltra <paramref name="effectivePrompt"/> ad <see cref="AgentBrain.Chat"/> con la history
        /// condivisa (<see cref="SharedChatSession"/>) — su un Task separato: un turno di chat può durare
        /// decine di secondi (LLM+tool), non deve bloccare il tick da 7s che processa
        /// approvazioni/scadenze. Un solo turno alla volta su TUTTO il Hub (Hub UI + Telegram) —
        /// se un altro turno è già in corso, l'utente riceve un avviso invece di un turno perso nel vuoto.
        /// Nello storico condiviso viene salvato <c>m.Text</c> (quello che l'utente ha DIGITATO, es.
        /// "/db_audit"), non il prompt espanso — più leggibile se richiamato da un altro canale.
        /// </summary>
        private void HandleTelegramChatMessage(TelegramTextMessage m, string effectivePrompt)
        {
            if (!SharedChatSession.TryBeginTurn())
            {
                Notifier.NotifyInfo(_settings, "⏳ Sto già elaborando un'altra richiesta (Hub o Telegram) — riprova tra poco.");
                return;
            }
            var brain = Brain; // snapshot: se le Impostazioni cambiano Brain durante il turno, non deve cambiare a metà
            string token = _settings.TelegramBotToken, chatId = _settings.TelegramChatId;
            Task.Run(() =>
            {
                try
                {
                    TelegramClient.SendTyping(token, chatId); // subito, prima ancora del primo tool call
                    var history = SharedChatSession.Snapshot();
                    var result = brain.Chat(effectivePrompt, history, AiTaskKind.Default,
                        msg => { App.WriteLog("AI-TELEGRAM-CHAT", msg); TelegramClient.SendTyping(token, chatId); },
                        CancellationToken.None);
                    string answer = result.Cancelled ? "(annullato)" : result.Ok ? result.Answer : ("ERRORE: " + result.Error);
                    SharedChatSession.Append("user", m.Text);
                    SharedChatSession.Append("assistant", answer ?? "");
                    Notifier.NotifyInfo(_settings, answer);
                }
                catch (Exception ex) { App.WriteLog("AI-TELEGRAM-CHAT", "errore: " + ex.Message); }
                finally { SharedChatSession.EndTurn(); }
            });
        }

        private string ExecuteQueuedAction(PendingAction action)
        {
            if (!_tools.TryGet(action.ToolName, out var tool))
                return $"ERRORE: tool '{action.ToolName}' non trovato nel registro.";
            try { return tool.Execute(action.ArgumentsJson) ?? ""; }
            catch (Exception ex) { return "ERRORE esecuzione: " + ex.Message; }
        }

        public void Dispose() => _timer?.Dispose();
    }
}
