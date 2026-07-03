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

        /// <summary>Cattura tutto l'output in una stringa (per query brevi).</summary>
        public static int RunCapture(string exe, string args, out string output, string workingDir = null)
        {
            var sb = new StringBuilder();
            int rc = Run(exe, args, l => sb.AppendLine(l), workingDir);
            output = sb.ToString();
            return rc;
        }
    }
}
