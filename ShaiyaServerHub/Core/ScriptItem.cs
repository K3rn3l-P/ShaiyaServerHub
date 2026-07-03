using System;
using System.ComponentModel;
using System.IO;

namespace ShaiyaServerHub.Core
{
    public enum ScriptKind { Sql, PowerShell, Batch, Unknown }

    /// <summary>Uno script scoperto nelle cartelle sql\ / ps1\.</summary>
    public class ScriptItem : INotifyPropertyChanged
    {
        public string FullPath { get; set; }
        public ScriptKind Kind { get; set; }

        public string Name => Path.GetFileName(FullPath);

        // ── Esito ultima esecuzione (per il badge cliccabile) ──
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPch(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private string _runStatus;   // null | PASS | WARN | FAIL | ERRORE | INFO
        public string RunStatus
        {
            get => _runStatus;
            set { _runStatus = value; OnPch(nameof(RunStatus)); OnPch(nameof(RunStatusLabel)); OnPch(nameof(RunStatusFg)); OnPch(nameof(RunStatusBg)); }
        }
        public string LastLog { get; set; }
        public string RunStatusLabel => string.IsNullOrEmpty(_runStatus) ? "—" : _runStatus;

        private static readonly System.Windows.Media.Brush StGreen = Solid(0x4D, 0xC8, 0x70);
        private static readonly System.Windows.Media.Brush StAmber = Solid(0xE8, 0xA2, 0x4D);
        private static readonly System.Windows.Media.Brush StRed = Solid(0xF4, 0x6A, 0x6A);
        private static readonly System.Windows.Media.Brush StBlue = Solid(0x52, 0xA8, 0xFF);
        private static readonly System.Windows.Media.Brush StGray = Solid(0x88, 0x99, 0xAA);
        private static readonly System.Windows.Media.Brush StGreenBg = Solid(0x4D, 0xC8, 0x70, 0x28);
        private static readonly System.Windows.Media.Brush StAmberBg = Solid(0xE8, 0xA2, 0x4D, 0x28);
        private static readonly System.Windows.Media.Brush StRedBg = Solid(0xF4, 0x6A, 0x6A, 0x28);
        private static readonly System.Windows.Media.Brush StBlueBg = Solid(0x52, 0xA8, 0xFF, 0x28);
        private static readonly System.Windows.Media.Brush StGrayBg = Solid(0x88, 0x99, 0xAA, 0x1E);

        public System.Windows.Media.Brush RunStatusFg
        {
            get
            {
                switch (_runStatus)
                {
                    case "PASS": return StGreen;
                    case "WARN": return StAmber;
                    case "FAIL": case "ERRORE": return StRed;
                    case "INFO": return StBlue;
                    default: return StGray;
                }
            }
        }
        public System.Windows.Media.Brush RunStatusBg
        {
            get
            {
                switch (_runStatus)
                {
                    case "PASS": return StGreenBg;
                    case "WARN": return StAmberBg;
                    case "FAIL": case "ERRORE": return StRedBg;
                    case "INFO": return StBlueBg;
                    default: return StGrayBg;
                }
            }
        }

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
