using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShaiyaServerHub.Core
{
    /// <summary>
    /// Scopre gli script nelle cartelle sql\ e ps1\ accanto all'exe.
    /// Le cartelle vengono create all'avvio se mancanti: l'utente ci mette
    /// dentro gli script vecchi e nuovi, ricaricati ad ogni apertura / Reload.
    /// </summary>
    public static class ScriptRepository
    {
        public static string Root => AppContext.BaseDirectory;
        public static string SqlDir => Path.Combine(Root, "sql");
        public static string Ps1Dir => Path.Combine(Root, "ps1");

        public static void EnsureFolders()
        {
            Directory.CreateDirectory(SqlDir);
            Directory.CreateDirectory(Ps1Dir);
        }

        public static List<ScriptItem> Discover()
        {
            EnsureFolders();
            var list = new List<ScriptItem>();

            // sql\ : *.sql
            foreach (var f in SafeEnum(SqlDir, "*.sql"))
                list.Add(new ScriptItem { FullPath = f, Kind = ScriptKind.Sql });

            // ps1\ : *.ps1, *.bat, *.cmd (script "di sistema" li teniamo qui)
            foreach (var f in SafeEnum(Ps1Dir, "*.ps1")
                              .Concat(SafeEnum(Ps1Dir, "*.bat"))
                              .Concat(SafeEnum(Ps1Dir, "*.cmd")))
                list.Add(new ScriptItem { FullPath = f, Kind = ScriptItem.KindFromExtension(f) });

            return list.OrderBy(s => s.Kind).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<string> SafeEnum(string dir, string pattern)
        {
            try
            {
                return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories);
            }
            catch { return Enumerable.Empty<string>(); }
        }
    }
}
