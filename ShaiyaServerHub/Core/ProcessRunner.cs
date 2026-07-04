using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace ShaiyaServerHub.Core
{
    /// <summary>Esecuzione processi con output live (stdout+stderr) via callback.</summary>
    public static class ProcessRunner
    {
        /// <returns>exit code (o -999 se il processo non parte)</returns>
        public static int Run(string exe, string args, Action<string> onLine, string workingDir = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = string.IsNullOrEmpty(workingDir) ? AppContext.BaseDirectory : workingDir
            };

            try
            {
                using (var p = new Process { StartInfo = psi, EnableRaisingEvents = true })
                using (var outDone = new ManualResetEventSlim(false))
                using (var errDone = new ManualResetEventSlim(false))
                {
                    // e.Data == null segnala EOF dello stream: attendiamo entrambi
                    // così TUTTE le righe sono consegnate PRIMA di ritornare (niente
                    // output che arriva dopo il riassunto).
                    p.OutputDataReceived += (s, e) => { if (e.Data == null) outDone.Set(); else onLine?.Invoke(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data == null) errDone.Set(); else onLine?.Invoke(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    outDone.Wait();
                    errDone.Wait();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                onLine?.Invoke("PROCESS ERROR: " + ex.Message);
                return -999;
            }
        }

        /// <summary>
        /// Esegue uno script PowerShell via -EncodedCommand (Base64 UTF-16LE): l'unico modo
        /// robusto per passare comandi con virgolette annidate senza che Windows le strippi
        /// (ProcessStartInfo.Arguments è una stringa singola ri-parsata da CreateProcess).
        /// </summary>
        public static int RunPowerShellEncoded(string script, Action<string> onLine, string workingDir = null)
        {
            // ProgressPreference SilentlyContinue: evita che lo stream progress finisca come
            // CLIXML su stderr (che qui verrebbe catturato come output).
            string full = "$ProgressPreference='SilentlyContinue';" + script;
            string b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
            return Run("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + b64, onLine, workingDir);
        }

        /// <summary>Cattura tutto l'output in una stringa (per query brevi).</summary>
        public static int RunCapture(string exe, string args, out string output, string workingDir = null)
        {
            var sb = new StringBuilder();
            int rc = Run(exe, args, l => sb.AppendLine(l), workingDir);
            output = sb.ToString();
            return rc;
        }

        /// <summary>RunPowerShellEncoded catturando l'output (per query PS con virgolette interne).</summary>
        public static int RunPowerShellEncodedCapture(string script, out string output, string workingDir = null)
        {
            var sb = new StringBuilder();
            int rc = RunPowerShellEncoded(script, l => sb.AppendLine(l), workingDir);
            output = sb.ToString();
            return rc;
        }
    }
}
