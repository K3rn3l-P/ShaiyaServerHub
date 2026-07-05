using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaiyaServerHub.Core.WebServer
{
    /// <summary>Esito di una sonda al web server (liveness + metriche + scadenza cert TLS).</summary>
    public class WebProbeResult
    {
        public bool Reachable;          // health url ha risposto (qualsiasi status HTTP)
        public int HttpStatus;          // status code health url (0 = irraggiungibile)
        public bool AppWeb;             // status.php → "web": true
        public bool AppServer;          // status.php → "server": true (login server TCP up)
        public int? BusyWorkers;        // mod_status
        public int? IdleWorkers;        // mod_status
        public double? ReqPerSec;       // mod_status
        public DateTime? CertExpiry;    // scadenza cert TLS effettivamente servito
        public string Error;

        public string Summary()
        {
            if (!Reachable) return L.WebUnreachable + (string.IsNullOrEmpty(Error) ? "" : " — " + Error);
            string s = $"● HTTP {HttpStatus}";
            if (AppWeb) s += " · web OK";
            if (AppServer) s += " · game OK"; else s += " · game DOWN";
            if (BusyWorkers.HasValue) s += $" · worker {BusyWorkers}/{BusyWorkers + IdleWorkers}";
            if (ReqPerSec.HasValue) s += $" · {ReqPerSec.Value:0.##} req/s";
            return s;
        }
    }

    /// <summary>
    /// Sonda il web server via PowerShell (Invoke-WebRequest + TLS handshake), saltando la
    /// validazione del certificato (self-signed/Tailscale su 127.0.0.1). Nessuna dipendenza:
    /// riusa ProcessRunner. Una sola chiamata restituisce health, metriche mod_status e cert.
    /// </summary>
    public static class WebProbe
    {
        public static WebProbeResult Probe(IWebServer ws)
        {
            var r = new WebProbeResult();
            if (ws == null) { r.Error = "web server non configurato"; return r; }

            string script = BuildScript(ws.HealthUrl, ws.StatusUrl);
            int rc = ProcessRunner.RunPowerShellEncodedCapture(script, out string outp);
            if (rc != 0 && string.IsNullOrWhiteSpace(outp)) { r.Error = "probe fallita (exit " + rc + ")"; return r; }

            foreach (var raw in outp.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("HTTP=")) { r.Reachable = true; int.TryParse(line.Substring(5).Trim(), out r.HttpStatus); }
                else if (line.StartsWith("BODY=")) ParseHealthBody(line.Substring(5), r);
                else if (line.StartsWith("STATUS=")) ParseStatusLine(line.Substring(7), r);
                else if (line.StartsWith("CERTEXP="))
                {
                    if (DateTime.TryParse(line.Substring(8).Trim(), CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var dt)) r.CertExpiry = dt;
                }
                else if (line.StartsWith("ERR=") && string.IsNullOrEmpty(r.Error)) r.Error = line.Substring(4).Trim();
            }
            return r;
        }

        private static void ParseHealthBody(string body, WebProbeResult r)
        {
            // status.php → JSON {"web":true,"server":true,"ts":...}. Parse leggero (zero deps).
            var mw = Regex.Match(body, "\"web\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            if (mw.Success) r.AppWeb = mw.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            var ms = Regex.Match(body, "\"server\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            if (ms.Success) r.AppServer = ms.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static void ParseStatusLine(string line, WebProbeResult r)
        {
            // mod_status ?auto: righe "BusyWorkers: N", "IdleWorkers: N", "ReqPerSec: X".
            int idx = line.IndexOf(':');
            if (idx <= 0) return;
            string key = line.Substring(0, idx).Trim();
            string val = line.Substring(idx + 1).Trim();
            if (key.Equals("BusyWorkers", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out int b)) r.BusyWorkers = b;
            else if (key.Equals("IdleWorkers", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out int i)) r.IdleWorkers = i;
            else if (key.Equals("ReqPerSec", StringComparison.OrdinalIgnoreCase) &&
                     double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double q)) r.ReqPerSec = q;
        }

        private static string BuildScript(string healthUrl, string statusUrl)
        {
            string h = Ps(healthUrl);
            var sb = new StringBuilder();
            // NON usare uno scriptblock come ServerCertificateValidationCallback: Invoke-WebRequest
            // invoca la callback su un thread del threadpool SENZA runspace → "There is no Runspace
            // available to run scripts in this thread". Serve un tipo COMPILATO (ICertificatePolicy)
            // che non richiede runspace. (Il cert probe con SslStream sotto funziona perché è sincrono
            // sullo stesso thread col runspace.)
            sb.AppendLine("add-type 'using System.Net; using System.Security.Cryptography.X509Certificates; public class SshHubTrustAll : ICertificatePolicy { public bool CheckValidationResult(ServicePoint s, X509Certificate c, WebRequest r, int p){ return true; } }'");
            sb.AppendLine("[System.Net.ServicePointManager]::CertificatePolicy = New-Object SshHubTrustAll");
            // OR-in TLS 1.2 (3072) + TLS 1.3 (12288) senza declassare né toccare enum mancanti:
            // forzare solo Tls12 rompe l'handshake se Apache/Tailscale offre TLS 1.3.
            sb.AppendLine("try { [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072 -bor 12288 } catch {}");
            // Health
            sb.AppendLine("try {");
            sb.AppendLine($"  $r = Invoke-WebRequest -Uri {h} -UseBasicParsing -TimeoutSec 8");
            sb.AppendLine("  Write-Output ('HTTP=' + [int]$r.StatusCode)");
            sb.AppendLine("  Write-Output ('BODY=' + ($r.Content -replace '\\r?\\n',' '))");
            sb.AppendLine("} catch { Write-Output ('ERR=' + $_.Exception.Message) }");
            // mod_status (opzionale)
            if (!string.IsNullOrWhiteSpace(statusUrl))
            {
                string st = Ps(statusUrl);
                sb.AppendLine("try {");
                sb.AppendLine($"  $s = Invoke-WebRequest -Uri {st} -UseBasicParsing -TimeoutSec 8");
                sb.AppendLine("  foreach ($ln in ($s.Content -split \"`n\")) { Write-Output ('STATUS=' + $ln.Trim()) }");
                sb.AppendLine("} catch {}");
            }
            // Certificato TLS realmente servito (handshake diretto su 443)
            sb.AppendLine("try {");
            sb.AppendLine("  $tc = New-Object System.Net.Sockets.TcpClient('127.0.0.1',443)");
            sb.AppendLine("  $cb = [System.Net.Security.RemoteCertificateValidationCallback]{ param($a,$b,$c,$d) $true }");
            sb.AppendLine("  $ss = New-Object System.Net.Security.SslStream($tc.GetStream(),$false,$cb)");
            sb.AppendLine("  $ss.AuthenticateAsClient('127.0.0.1')");
            sb.AppendLine("  $x = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($ss.RemoteCertificate)");
            sb.AppendLine("  Write-Output ('CERTEXP=' + $x.NotAfter.ToString('yyyy-MM-dd'))");
            sb.AppendLine("  $ss.Close(); $tc.Close()");
            sb.AppendLine("} catch {}");
            return sb.ToString();
        }

        // Stringa PowerShell single-quoted sicura (raddoppia gli apici).
        private static string Ps(string s) => "'" + (s ?? "").Replace("'", "''") + "'";
    }
}
