using System;
using System.IO;

namespace ShaiyaServerHub.Core
{
    public enum ScriptKind { Sql, PowerShell, Batch, Unknown }

    /// <summary>Uno script scoperto nelle cartelle sql\ / ps1\.</summary>
    public class ScriptItem
    {
        public string FullPath { get; set; }
        public ScriptKind Kind { get; set; }

        public string Name => Path.GetFileName(FullPath);

        public string KindLabel
        {
            get
            {
                switch (Kind)
                {
                    case ScriptKind.Sql: return "SQL";
                    case ScriptKind.PowerShell: return "PS1";
                    case ScriptKind.Batch: return "BAT";
                    default: return "?";
                }
            }
        }

        // ── Colori badge per tipo (colpo d'occhio) ──────────
        private static System.Windows.Media.Brush Solid(byte r, byte g, byte b, byte a = 0xFF)
        {
            var br = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }
        private static readonly System.Windows.Media.Brush SqlC = Solid(0x52, 0xA8, 0xFF);
        private static readonly System.Windows.Media.Brush PsC = Solid(0x5B, 0xD1, 0x6A);
        private static readonly System.Windows.Media.Brush BatC = Solid(0xE8, 0xA2, 0x4D);
        private static readonly System.Windows.Media.Brush UnkC = Solid(0x88, 0x99, 0xAA);
        private static readonly System.Windows.Media.Brush SqlBg = Solid(0x52, 0xA8, 0xFF, 0x22);
        private static readonly System.Windows.Media.Brush PsBg = Solid(0x5B, 0xD1, 0x6A, 0x22);
        private static readonly System.Windows.Media.Brush BatBg = Solid(0xE8, 0xA2, 0x4D, 0x22);
        private static readonly System.Windows.Media.Brush UnkBg = Solid(0x88, 0x99, 0xAA, 0x22);

        public System.Windows.Media.Brush KindBrush
        {
            get
            {
                switch (Kind)
                {
                    case ScriptKind.Sql: return SqlC;
                    case ScriptKind.PowerShell: return PsC;
                    case ScriptKind.Batch: return BatC;
                    default: return UnkC;
                }
            }
        }

        public System.Windows.Media.Brush KindBadgeBg
        {
            get
            {
                switch (Kind)
                {
                    case ScriptKind.Sql: return SqlBg;
                    case ScriptKind.PowerShell: return PsBg;
                    case ScriptKind.Batch: return BatBg;
                    default: return UnkBg;
                }
            }
        }

        public static ScriptKind KindFromExtension(string path)
        {
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".sql": return ScriptKind.Sql;
                case ".ps1": return ScriptKind.PowerShell;
                case ".bat":
                case ".cmd": return ScriptKind.Batch;
                default: return ScriptKind.Unknown;
            }
        }
    }
}
