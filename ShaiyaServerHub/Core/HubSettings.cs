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

        // ── Percorsi server ──────────────────────────────────
        public string ServerBinPath { get; set; } = @"C:\ShaiyaServer\PSM_Client\Bin";
        public string StartServicesScript { get; set; } = "";   // es. AutoAvvioServiziShaiya.ps1

        // ── UI ───────────────────────────────────────────────
        public string Language { get; set; } = "It";

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
                        if (s != null) return s;
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
