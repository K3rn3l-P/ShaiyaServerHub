using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub
{
    /// <summary>Dialog per creare o modificare una regola inbound.</summary>
    public partial class FirewallRuleDialog : Window
    {
        public string RuleName { get; private set; }
        public string Protocol { get; private set; }
        public string LocalPort { get; private set; }
        public string RuleAction { get; private set; }
        public string Scope { get; private set; }

        /// <summary>In modifica: DisplayName originale (immutabile).</summary>
        public string EditDisplayName { get; private set; }
        public bool IsEdit { get; private set; }

        public FirewallRuleDialog() { InitializeComponent(); }

        /// <summary>Precarica i campi da una regola esistente (modalità modifica).</summary>
        public void LoadForEdit(FirewallRuleRow r, string lanCidr = null)
        {
            _lanNet = string.IsNullOrWhiteSpace(lanCidr) ? "" : lanCidr.Split('/')[0].Trim();
            IsEdit = true;
            EditDisplayName = r.DisplayName;
            TbHeader.Text = L.FwDlgEditTitle;
            BtnOk.SetResourceReference(ContentProperty, "FwEditRule");
            TbName.Text = r.DisplayName;
            TbName.IsEnabled = false;   // il nome è l'identità: non si cambia

            SelectByContent(CbProto, r.Protocol);
            TbPort.Text = r.LocalPort == "Any" ? "" : r.LocalPort;
            SelectByContent(CbAction, r.Action);

            // Ambito: Any / LAN / personalizzato.
            string remote = r.RemoteAddress ?? "";
            if (string.IsNullOrWhiteSpace(remote) || remote.Equals("Any", StringComparison.OrdinalIgnoreCase))
                SelectByTag(CbScope, "Any");
            else if (IsLan(remote))
                SelectByTag(CbScope, "LAN");
            else
            {
                SelectByTag(CbScope, "CUSTOM");
                TbCustomScope.Text = remote;
            }
            UpdateCustomVisibility();
        }

        private string _lanNet = "";
        private bool IsLan(string remote) =>
            _lanNet.Length > 0 && remote.StartsWith(_lanNet, StringComparison.OrdinalIgnoreCase);

        private void CbScope_Changed(object sender, SelectionChangedEventArgs e) => UpdateCustomVisibility();

        private void UpdateCustomVisibility()
        {
            bool custom = (CbScope.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "CUSTOM";
            var vis = custom ? Visibility.Visible : Visibility.Collapsed;
            if (LblCustom != null) LblCustom.Visibility = vis;
            if (TbCustomScope != null) TbCustomScope.Visibility = vis;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string name = (TbName.Text ?? "").Trim();
            string port = (TbPort.Text ?? "").Trim();
            string scopeTag = (CbScope.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Any";

            if (name.Length == 0 || !Regex.IsMatch(name, @"^[\w \-]+$")) { Err(L.FwDlgErrName); return; }
            if (port.Length == 0 || !Regex.IsMatch(port, @"^\s*\d{1,5}(\s*[-,]\s*\d{1,5})*\s*$")) { Err(L.FwDlgErrPort); return; }

            string scope;
            if (scopeTag == "CUSTOM")
            {
                scope = (TbCustomScope.Text ?? "").Trim();
                if (scope.Length == 0) { Err(L.FwDlgErrCustom); return; }
            }
            else scope = scopeTag;   // "Any" | "LAN"

            RuleName = name;
            Protocol = (CbProto.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "TCP";
            LocalPort = port.Replace(" ", "");
            RuleAction = (CbAction.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Allow";
            Scope = scope;

            DialogResult = true;
            Close();
        }

        private static void SelectByContent(ComboBox cb, string content)
        {
            foreach (ComboBoxItem it in cb.Items)
                if (string.Equals(it.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase)) { cb.SelectedItem = it; return; }
        }
        private static void SelectByTag(ComboBox cb, string tag)
        {
            foreach (ComboBoxItem it in cb.Items)
                if (string.Equals(it.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase)) { cb.SelectedItem = it; return; }
        }

        private void Err(string m) { TbErr.Text = m; TbErr.Visibility = Visibility.Visible; }

        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
        private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    }
}
