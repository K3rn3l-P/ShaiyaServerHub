using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShaiyaServerHub.Core
{
    /// <summary>Base con notifica per il binding editabile delle DataGrid.</summary>
    public abstract class Notify : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value; Raise(name); return true;
        }
    }

    /// <summary>Tier di privilegio di un comando (rank crescente = più potere).</summary>
    public static class Tiers
    {
        // rank: SAFE=1, PLAYER=2, ECONOMY=3, SERVICE=4 (SERVICE mai via usp_RunCommand).
        public static readonly string[] All = { "SAFE", "PLAYER", "ECONOMY", "SERVICE" };
        // I profili chiamante arrivano al massimo a ECONOMY (SERVICE non è concedibile).
        public static readonly TierRank[] CallerRanks =
        {
            new TierRank(1, "SAFE"), new TierRank(2, "PLAYER"), new TierRank(3, "ECONOMY")
        };
    }

    public class TierRank
    {
        public byte Rank { get; }
        public string Label { get; }
        public TierRank(byte rank, string label) { Rank = rank; Label = label; }
        public override string ToString() => Label;
    }

    /// <summary>Riga di PSM_Cmd.dbo.GmCommandAllowlist. Editabili: Tier, Enabled, Description.</summary>
    public class AllowlistRow : Notify
    {
        // PK (Command, Service): identità di riga, non modificabile dall'editor.
        public string Command { get; set; }
        public string Service { get; set; }

        private string _tier;
        public string Tier { get => _tier; set => Set(ref _tier, value); }

        private bool _enabled;
        public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

        private string _description;
        public string Description { get => _description; set => Set(ref _description, value); }
    }

    /// <summary>Riga di PSM_Cmd.dbo.GmCallerProfile: login → tier massimo consentito.</summary>
    public class CallerProfileRow : Notify
    {
        private string _callerLogin;
        public string CallerLogin { get => _callerLogin; set => Set(ref _callerLogin, value); }

        private byte _maxTierRank = 1;
        public byte MaxTierRank { get => _maxTierRank; set { if (Set(ref _maxTierRank, value)) Raise(nameof(TierLabel)); } }

        public string TierLabel
        {
            get
            {
                switch (MaxTierRank)
                {
                    case 1: return "SAFE"; case 2: return "PLAYER";
                    case 3: return "ECONOMY"; case 4: return "SERVICE";
                    default: return MaxTierRank.ToString();
                }
            }
        }
    }

    /// <summary>Riga di PSM_Cmd.dbo.GmCommandLog (sola lettura).</summary>
    public class CommandLogRow
    {
        public long Id { get; set; }
        public System.DateTime Ts { get; set; }        // convertito a ora locale per la vista
        public string CallerLogin { get; set; }
        public string Wrapper { get; set; }
        public string Service { get; set; }
        public string Command { get; set; }
        public string Result { get; set; }             // OK | DENIED | ERROR
    }
}
