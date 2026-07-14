namespace ShaiyaServerHub.Core.Ai.Sensors
{
    /// <summary>
    /// Spazio disco sui volumi fissi — verificato sul server reale (`Win32_LogicalDisk -Filter
    /// "DriveType=3"`, es. C: 27.4GB/99.1GB liberi). Deliberatamente SOLO disco tra le risorse: CPU/RAM
    /// sono rumorose per un check puntuale senza baseline storico (un picco normale è indistinguibile da
    /// un problema reale in uno snapshot ogni 15 min) — il disco pieno è invece un segnale oggettivo,
    /// vedi PLAN §P4.
    /// </summary>
    public static class DiskSpaceSensor
    {
        public static int Query(out string output)
        {
            string ps = @"
$ErrorActionPreference='SilentlyContinue'
Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object {
  $freeGb = [math]::Round($_.FreeSpace/1GB,1)
  $sizeGb = [math]::Round($_.Size/1GB,1)
  $pct = if ($_.Size -gt 0) { [math]::Round(($_.FreeSpace/$_.Size)*100,1) } else { 0 }
  'DISK'+[char]9+$_.DeviceID+[char]9+$freeGb+[char]9+$sizeGb+[char]9+$pct
}";
            return ProcessRunner.RunPowerShellEncodedCapture(ps, out output);
        }
    }
}
