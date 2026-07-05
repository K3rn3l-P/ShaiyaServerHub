using System;
using System.IO;

namespace ShaiyaServerHub.Core.WebServer
{
    /// <summary>
    /// Astrazione del web server sotto il sito Shaiya. Oggi XAMPP/Apache; domani IIS.
    /// Espone nomi servizi, percorsi (webroot, log, conf), lo script di rigenerazione
    /// certificato SSL e gli URL di health-check. L'implementazione concreta si sceglie
    /// da HubSettings.WebServerKind via <see cref="WebServerFactory"/>.
    /// </summary>
    public interface IWebServer
    {
        string Key { get; }                       // "Xampp" | "Iis"
        string DisplayName { get; }
        (string svc, string disp)[] Services { get; }
        string PrimaryService { get; }            // servizio principale (Apache2.4 / W3SVC)
        string WebRoot { get; }
        string[] LogPaths { get; }
        string[] ConfPaths { get; }
        string SslCertScript { get; }             // nome file in scripts\ps1 (o null)
        string HealthUrl { get; }                 // liveness applicativa (status.php)
        string StatusUrl { get; }                 // metriche server (mod_status ?auto) o null
    }

    /// <summary>XAMPP / Apache httpd (setup attuale).</summary>
    public sealed class XamppApache : IWebServer
    {
        private readonly HubSettings _s;
        public XamppApache(HubSettings s) { _s = s; }

        private string Root => string.IsNullOrWhiteSpace(_s.XamppRoot) ? @"C:\xampp" : _s.XamppRoot;

        public string Key => "Xampp";
        public string DisplayName => "XAMPP / Apache";
        // Solo Apache: il DB è MSSQL (MSSQLSERVER, già nella vista Servizi di gioco),
        // non MySQL/MariaDB. Se un giorno servisse MySQL, aggiungerlo qui.
        public (string svc, string disp)[] Services => new[]
        {
            ("Apache2.4", "Apache (httpd)"),
        };
        public string PrimaryService => "Apache2.4";
        public string WebRoot => string.IsNullOrWhiteSpace(_s.WebRoot)
            ? Path.Combine(Root, "htdocs") : _s.WebRoot;
        public string[] LogPaths => new[]
        {
            Path.Combine(Root, @"apache\logs\error.log"),
            Path.Combine(Root, @"apache\logs\access.log"),
        };
        public string[] ConfPaths => new[]
        {
            Path.Combine(Root, @"apache\conf\httpd.conf"),
            Path.Combine(Root, @"apache\conf\extra\httpd-ssl.conf"),
        };
        public string SslCertScript => "genera_chiavi_ssl-XAMPP.ps1";
        public string HealthUrl => _s.WebHealthUrl;
        // mod_status: va abilitato SOLO su 127.0.0.1 in una conf Apache dedicata (non nel webroot).
        public string StatusUrl => "https://127.0.0.1/server-status?auto";
    }

    /// <summary>
    /// IIS (stub, non ancora in uso). Nomi servizi/percorsi standard; niente mod_status
    /// (le metriche IIS si leggono dai contatori perf, non da un endpoint HTTP).
    /// </summary>
    public sealed class Iis : IWebServer
    {
        private readonly HubSettings _s;
        public Iis(HubSettings s) { _s = s; }

        public string Key => "Iis";
        public string DisplayName => "IIS";
        public (string svc, string disp)[] Services => new[]
        {
            ("W3SVC", "World Wide Web Publishing"),
            ("WAS",   "Windows Process Activation"),
        };
        public string PrimaryService => "W3SVC";
        public string WebRoot =>
            (_s.WebRoot ?? "").IndexOf("inetpub", StringComparison.OrdinalIgnoreCase) >= 0
                ? _s.WebRoot : @"C:\inetpub\wwwroot";
        public string[] LogPaths => new[] { @"C:\inetpub\logs\LogFiles" };
        public string[] ConfPaths => new[] { @"C:\Windows\System32\inetsrv\config\applicationHost.config" };
        public string SslCertScript => "genera_chiavi_ssl-IIS.ps1";
        public string HealthUrl => _s.WebHealthUrl;
        public string StatusUrl => null;
    }

    public static class WebServerFactory
    {
        public static IWebServer Create(HubSettings s)
        {
            if (s != null && string.Equals(s.WebServerKind, "Iis", StringComparison.OrdinalIgnoreCase))
                return new Iis(s);
            return new XamppApache(s);
        }
    }
}
