using System;
using System.Collections.Generic;

namespace ShaiyaServerHub.Core
{
    /// <summary>Un servizio scoperto negli eventi 7036 (nome + n. occorrenze).</summary>
    public class DiscoveredService
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public override string ToString() => $"{Name}  ({Count})";
    }

    /// <summary>Un servizio Windows (per il generatore: si sceglie dalla lista, niente da digitare).</summary>
    public class ServiceEntry
    {
        public string Name { get; set; }         // es. ps_game
        public string DisplayName { get; set; }  // es. Shaiya Game Server (= param1 nell'evento 7036)
        public string Status { get; set; }       // Running / Stopped (enum .NET, non localizzato)
        public string Display => $"{DisplayName}   ({Name})   ·   {Status}";
    }

    /// <summary>
    /// Costruisce filtri XPath per trigger "su evento" del Task Scheduler e scopre i
    /// servizi reali dagli eventi 7036 (Service Control Manager) del log System LOCALE.
    /// L'app gira sul server → locale = server. param1 = nome servizio, param2 = stato
    /// (LOCALIZZATO dal sistema, es. IT "in esecuzione"/"arrestato").
    /// </summary>
    public static class EventTriggerBuilder
    {
        public const string ScmProvider = "Service Control Manager";
        // Filtro del preset SHUTDOWN (spegnimento/riavvio: evento 1074 di User32).
        public const string ShutdownXPath = "*[System[Provider[@Name='User32'] and (EventID=1074)]]";

        /// <summary>
        /// XPath per /MO: filtra per EventID e (opz.) nome servizio (param1) e stato (param2).
        /// </summary>
        public static string BuildXPath(int eventId, string provider, string serviceName, string stateText)
        {
            string sys = string.IsNullOrWhiteSpace(provider)
                ? $"*[System[(EventID={eventId})]]"
                : $"*[System[Provider[@Name='{Esc(provider)}'] and (EventID={eventId})]]";

            string res = sys;
            if (!string.IsNullOrWhiteSpace(serviceName))
                res += $" and *[EventData[Data[@Name='param1'] and (Data='{Esc(serviceName)}')]]";
            if (!string.IsNullOrWhiteSpace(stateText))
                res += $" and *[EventData[Data[@Name='param2'] and (Data='{Esc(stateText)}')]]";
            return res;
        }

        /// <summary>QueryList XML completo (equivalente al filtro, utile per copia/incolla).</summary>
        public static string BuildQueryListXml(string channel, string xpath)
        {
            channel = string.IsNullOrWhiteSpace(channel) ? "System" : channel;
            return
                "<QueryList>\r\n" +
                $"  <Query Id=\"0\" Path=\"{Esc(channel)}\">\r\n" +
                $"    <Select Path=\"{Esc(channel)}\">\r\n" +
                $"      {xpath}\r\n" +
                "    </Select>\r\n" +
                "  </Query>\r\n" +
                "</QueryList>";
        }

        /// <summary>
        /// Scopre i servizi dagli eventi 7036 del log System locale (via Get-WinEvent).
        /// Ritorna lista ordinata per frequenza. Vuota se non ci sono eventi (es. su dev box).
        /// </summary>
        public static List<DiscoveredService> ScanServices(int maxEvents = 500)
        {
            var list = new List<DiscoveredService>();
            // Filtro ProviderName='Service Control Manager': l'ID 7036 è usato anche da altri
            // provider (es. driver) con schema EventData diverso — SOLO l'SCM ha Data Name='param1'.
            string ps =
                "$ErrorActionPreference='SilentlyContinue';" +
                "$e = Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Service Control Manager';Id=7036} -MaxEvents " + maxEvents + ";" +
                "$e | ForEach-Object { ([xml]$_.ToXml()).Event.EventData.Data } |" +
                " Where-Object { $_.Name -eq 'param1' } | ForEach-Object { $_.'#text' } |" +
                " Group-Object | Sort-Object Count -Descending | Select-Object -First 80 |" +
                " ForEach-Object { \"$($_.Count)`t$($_.Name)\" }";

            // -EncodedCommand: il format string contiene virgolette che -Command strippa.
            string output;
            ProcessRunner.RunPowerShellEncodedCapture(ps, out output);
            if (string.IsNullOrWhiteSpace(output)) return list;

            foreach (var raw in output.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int tab = line.IndexOf('\t');
                if (tab < 0) continue;
                int count;
                if (!int.TryParse(line.Substring(0, tab).Trim(), out count)) continue;
                string name = line.Substring(tab + 1).Trim();
                if (name.Length == 0) continue;
                list.Add(new DiscoveredService { Name = name, Count = count });
            }
            return list;
        }

        /// <summary>Elenco servizi Windows (Get-Service) per il picker del generatore.</summary>
        public static List<ServiceEntry> ListServices()
        {
            var list = new List<ServiceEntry>();
            string ps =
                "Get-Service | Sort-Object DisplayName | ForEach-Object {" +
                " \"$($_.Status)`t$($_.Name)`t$($_.DisplayName)\" }";
            string output;
            ProcessRunner.RunPowerShellEncodedCapture(ps, out output);
            if (string.IsNullOrWhiteSpace(output)) return list;
            foreach (var raw in output.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;
                var parts = line.Split(new[] { '\t' }, 3);
                if (parts.Length < 3) continue;
                list.Add(new ServiceEntry { Status = parts[0].Trim(), Name = parts[1].Trim(), DisplayName = parts[2].Trim() });
            }
            return list;
        }

        /// <summary>
        /// Testi di stato REALI (param2, localizzati) usati negli eventi 7036 del server.
        /// Ritorna (avviato, arrestato) inferiti per parola chiave; null se non trovati.
        /// </summary>
        public static void ScanStateTexts(out string runningText, out string stoppedText)
        {
            runningText = null; stoppedText = null;
            string ps =
                "$ErrorActionPreference='SilentlyContinue';" +
                "Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Service Control Manager';Id=7036} -MaxEvents 800 |" +
                " ForEach-Object { ([xml]$_.ToXml()).Event.EventData.Data } |" +
                " Where-Object { $_.Name -eq 'param2' } | ForEach-Object { $_.'#text' } | Sort-Object -Unique";
            string output;
            ProcessRunner.RunPowerShellEncodedCapture(ps, out output);
            if (string.IsNullOrWhiteSpace(output)) return;
            foreach (var raw in output.Replace("\r", "").Split('\n'))
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;
                string low = s.ToLowerInvariant();
                if (runningText == null && (low.Contains("esec") || low.Contains("run"))) runningText = s;
                else if (stoppedText == null && (low.Contains("arrest") || low.Contains("stop"))) stoppedText = s;
            }
        }

        private static string Esc(string s) => (s ?? "").Replace("'", "&apos;").Replace("\"", "&quot;");
    }
}
