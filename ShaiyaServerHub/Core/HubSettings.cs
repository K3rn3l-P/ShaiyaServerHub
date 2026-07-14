using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Config locale del Hub. Persistita accanto all'exe (hub.settings.xml).
    /// La password SQL è cifrata con DPAPI (scope LocalMachine, così anche un
    /// task pianificato sotto SYSTEM può decifrarla). Uso solo locale sul server.
    /// </summary>
    public class HubSettings
    {
        // ── SQL ──────────────────────────────────────────────
        public string SqlServer { get; set; } = "localhost";
        public string SqlAuthMode { get; set; } = "Windows";   // "Windows" | "Sql"
        public string SqlUser { get; set; } = "ShaiyaTaskAgent";
        public string SqlPasswordEnc { get; set; } = "";        // DPAPI base64
        public string DefaultDatabase { get; set; } = "PS_GameDefs";
        // DB del sito web (coda task Web_TaskJobs/Web_TaskQueue/Web_TaskJobRuns, Web_Settings).
        public string WebsiteDatabase { get; set; } = "PS_WebSite";

        // ── Percorsi server ──────────────────────────────────
        public string ServerBinPath { get; set; } = @"C:\ShaiyaServer\PSM_Client\Bin";
        public string StartServicesScript { get; set; } = "";   // es. AutoAvvioServiziShaiya.ps1

        // ── Connessione SQL del SITO (Web Jobs), separata da quella di gioco ──
        // Il sito usa un account dedicato (Ernoweb@) con accesso a PS_WebSite. Modi:
        //  "Env"     → legge DB_USER/DB_PASSWORD dalle variabili d'ambiente Machine (come il sito). DEFAULT.
        //  "Windows" → autenticazione Windows (-E), utile se l'Hub gira sotto un account con accesso.
        //  "Sql"     → login SQL esplicito (WebSqlUser/WebSqlPassword).
        public string WebSqlAuthMode { get; set; } = "Env";     // "Env" | "Windows" | "Sql"
        public string WebSqlUser { get; set; } = "";
        public string WebSqlPasswordEnc { get; set; } = "";     // DPAPI base64

        [XmlIgnore]
        public string WebSqlPassword
        {
            get { return Unprotect(WebSqlPasswordEnc); }
            set { WebSqlPasswordEnc = Protect(value); }
        }

        // ── Web server (XAMPP/Apache ora, IIS in futuro) ─────
        public string WebServerKind { get; set; } = "Xampp";    // "Xampp" | "Iis"
        public string XamppRoot { get; set; } = @"C:\xampp";
        public string WebRoot { get; set; } = @"C:\xampp\htdocs";
        // Endpoint di liveness applicativa (già servito dal sito: Updater/api/status.php → {web,server,ts}).
        public string WebHealthUrl { get; set; } = "https://127.0.0.1/Updater/api/status.php";

        // ── Firewall ─────────────────────────────────────────
        // Subnet della rete locale di gestione (SSH/RDP/SQL ristretti qui).
        public string FirewallLanCidr { get; set; } = "10.10.0.0/16";

        // ── UI ───────────────────────────────────────────────
        public string Language { get; set; } = "It";

        // ── AI (Hub Brain) ────────────────────────────────────
        // Tutti i segreti AI seguono lo stesso pattern DPAPI di SqlPasswordEnc: MAI in chiaro su disco.
        public bool AiMasterEnabled { get; set; } = false;    // kill-switch principale: AI spenta di default
        public string OpenRouterEndpoint { get; set; } = "https://openrouter.ai/api/v1/chat/completions";
        public string OpenRouterApiKeyEnc { get; set; } = "";  // DPAPI base64

        [XmlIgnore]
        public string OpenRouterApiKey
        {
            get { return Unprotect(OpenRouterApiKeyEnc); }
            set { OpenRouterApiKeyEnc = Protect(value); }
        }

        // Bot Telegram 2-vie (notifiche + approvazioni remote). Vedi mini-guida BotFather in AiControl.
        public string TelegramBotTokenEnc { get; set; } = "";  // DPAPI base64
        public string TelegramChatId { get; set; } = "";       // non segreto, ma privato: resta in chiaro

        [XmlIgnore]
        public string TelegramBotToken
        {
            get { return Unprotect(TelegramBotTokenEnc); }
            set { TelegramBotTokenEnc = Protect(value); }
        }

        // Login SQL dedicato, read-only (ShaiyaAiReader) — vedi script guida MSSQL.
        public string AiSqlReadUser { get; set; } = "ShaiyaAiReader";
        public string AiSqlReadPassEnc { get; set; } = "";     // DPAPI base64

        [XmlIgnore]
        public string AiSqlReadPass
        {
            get { return Unprotect(AiSqlReadPassEnc); }
            set { AiSqlReadPassEnc = Protect(value); }
        }

        // Login SQL dedicato per il tool T3 "ban_player" (ShaiyaAiBanExecutor) — SEPARATO sia da
        // ShaiyaAiReader (sola lettura) sia da SqlUser/ShaiyaTaskAgent (PSM_Cmd). Un solo permesso lato
        // server: EXECUTE su PS_UserData.dbo.sp_BanUser, nient'altro — vedi script
        // 3.1.Login-BanExecutor-AI.sql sulla guida NAS. Se non configurato, il tool ban_player rifiuta
        // esplicitamente invece di tentare con credenziali sbagliate.
        public string AiBanSqlUser { get; set; } = "ShaiyaAiBanExecutor";
        public string AiBanSqlPassEnc { get; set; } = "";     // DPAPI base64

        [XmlIgnore]
        public string AiBanSqlPass
        {
            get { return Unprotect(AiBanSqlPassEnc); }
            set { AiBanSqlPassEnc = Protect(value); }
        }

        // Ricerca web per l'AI (mitigazioni/CVE/best-practice) — non segreto. Due modalità:
        // "Mcp"  → server MCP (JSON-RPC su HTTP, Core\Ai\McpSearxngClient). Default: istanza nota di
        //          questo ambiente, verificata (initialize → notifications/initialized → tools/call).
        // "Rest" → endpoint SearXNG REST classico (/search?format=json) per chi ha/vorrà in futuro
        //          un'istanza SearXNG propria con l'API JSON abilitata in settings.yml.
        public string AiWebSearchMode { get; set; } = "Mcp";       // "Mcp" | "Rest"
        public string AiWebSearchUrl { get; set; } = DefaultAiWebSearchUrl;
        public const string DefaultAiWebSearchUrl = "http://10.10.19.4:3030/mcp";

        // Digest esteso (P4 — sorveglianza Windows/SQL/web completa, vedi SecurityDigestScheduler).
        // Soglia minima di gravità che genera un turno LLM + notifica Telegram — "Low"/"Medium"/"High".
        // Default Medium: i finding Low restano SOLO nell'audit log (hub.log), mai su Telegram (anti-spam).
        public string AiDigestTelegramMinSeverity { get; set; } = "Medium";
        // Spazio disco: sotto questa percentuale libera → finding High/Medium.
        public double AiDigestDiskFreePctHigh { get; set; } = 5;
        public double AiDigestDiskFreePctMedium { get; set; } = 10;
        // Login falliti OS (Security 4625/4740) con IP reale, conteggio per singolo IP nella finestra.
        public int AiDigestOsFailedLogonMedium { get; set; } = 20;
        public int AiDigestOsFailedLogonHigh { get; set; } = 100;
        // Blocco SQL sostenuto (secondi) oltre cui diventa un finding Medium.
        public int AiDigestSqlBlockingSecondsMedium { get; set; } = 30;

        // Audit persistenza/ASEP (autorunsc, vedi AutorunsAuditScheduler) — default 30min/24h/30min.
        public int AiAutorunsCheckIntervalMinutes { get; set; } = 30;
        public int AiAutorunsRunEveryHours { get; set; } = 24;
        public int AiAutorunsStartupGraceMinutes { get; set; } = 30;

        // Login SQL falliti a livello server (SqlAuditSensor) — conteggio per singolo IP che fa scattare Medium/High.
        public int AiDigestSqlFailedLoginMedium { get; set; } = 5;
        public int AiDigestSqlFailedLoginHigh { get; set; } = 20;

        // ── Password in chiaro (runtime, NON serializzata) ──
        [XmlIgnore]
        public string SqlPassword
        {
            get { return Unprotect(SqlPasswordEnc); }
            set { SqlPasswordEnc = Protect(value); }
        }

        // ── Persistenza ──────────────────────────────────────
        private static string SettingsPath =>
            Path.Combine(AppContext.BaseDirectory, "hub.settings.xml");

        public static HubSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    using (var fs = File.OpenRead(SettingsPath))
                    {
                        var ser = new XmlSerializer(typeof(HubSettings));
                        var s = ser.Deserialize(fs) as HubSettings;
                        if (s != null)
                        {
                            // Migrazione: un hub.settings.xml salvato PRIMA che questo default esistesse
                            // contiene <AiWebSearchUrl /> vuoto esplicito, che altrimenti vincerebbe sempre
                            // sul default della proprietà (la deserializzazione imposta ciò che TROVA nel
                            // file). Ripristina il default solo se il campo è vuoto, mai se l'utente ha
                            // scelto qualcos'altro.
                            if (string.IsNullOrWhiteSpace(s.AiWebSearchUrl)) s.AiWebSearchUrl = DefaultAiWebSearchUrl;
                            if (string.IsNullOrWhiteSpace(s.AiWebSearchMode)) s.AiWebSearchMode = "Mcp";
                            return s;
                        }
                    }
                }
            }
            catch { /* config corrotta → default */ }
            return new HubSettings();
        }

        public void Save()
        {
            try
            {
                using (var fs = File.Create(SettingsPath))
                {
                    var ser = new XmlSerializer(typeof(HubSettings));
                    ser.Serialize(fs, this);
                }
            }
            catch { }
        }

        // ── DPAPI ────────────────────────────────────────────
        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] enc = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.LocalMachine);
                return Convert.ToBase64String(enc);
            }
            catch { return ""; }
        }

        private static string Unprotect(string enc)
        {
            if (string.IsNullOrEmpty(enc)) return "";
            try
            {
                byte[] plain = ProtectedData.Unprotect(
                    Convert.FromBase64String(enc), null, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(plain);
            }
            catch { return ""; }
        }
    }
}
