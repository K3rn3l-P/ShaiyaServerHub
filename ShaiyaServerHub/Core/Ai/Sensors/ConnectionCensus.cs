using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ShaiyaServerHub.Core.Ai.Sensors
{
    /// <summary>Un IP remoto con connessioni TCP stabilite verso le porte osservate.</summary>
    public class IpConnectionCount
    {
        public string Ip;
        public int Connections;
    }

    /// <summary>
    /// Censimento connessioni per-IP sulle porte di gioco (30800/30810 di default, vedi PLAN §Realtà del
    /// server) — primo sensore T0 di P3. Legge SOLO connessioni TCP realmente <c>Established</c> (stato
    /// Windows reale via <c>Get-NetTCPConnection</c>, stesso approccio già usato da
    /// <see cref="FirewallManager.NetworkAudit"/> per le porte in ascolto), niente stima o simulazione:
    /// un flood/abuso in-game si manifesta come molte connessioni stabilite dallo stesso IP remoto.
    /// </summary>
    public static class ConnectionCensus
    {
        public static readonly int[] GamePorts = { 30800, 30810 };

        public static List<IpConnectionCount> Query(int[] ports)
        {
            string portsCsv = string.Join(",", (ports != null && ports.Length > 0 ? ports : GamePorts));
            string ps = $@"
$ErrorActionPreference='SilentlyContinue'
Get-NetTCPConnection -State Established | Where-Object {{ $_.LocalPort -in @({portsCsv}) }} |
  Group-Object RemoteAddress | Sort-Object Count -Descending |
  ForEach-Object {{ 'IP'+[char]9+$_.Name+[char]9+$_.Count }}
";
            ProcessRunner.RunPowerShellEncodedCapture(ps, out string outp);
            var list = new List<IpConnectionCount>();
            foreach (var line in (outp ?? "").Replace("\r", "").Split('\n'))
            {
                var f = line.Split('\t');
                if (f.Length >= 3 && f[0] == "IP")
                {
                    int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
                    list.Add(new IpConnectionCount { Ip = f[1], Connections = n });
                }
            }
            return list.OrderByDescending(x => x.Connections).ToList();
        }
    }
}
