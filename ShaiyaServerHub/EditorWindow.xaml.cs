using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using ShaiyaServerHub.Core;

namespace ShaiyaServerHub
{
    /// <summary>
    /// Editor modale per file di testo (sql / ps1 / bat / cmd / ...): apri, modifica
    /// veloce, salva. Preserva la codifica originale (BOM UTF-8/UTF-16) del file.
    /// DialogResult = true quando l'utente salva (l'host ricarica la lista).
    /// </summary>
    public partial class EditorWindow : Window
    {
        private readonly string _path;
        private Encoding _encoding = new UTF8Encoding(false); // default: UTF-8 senza BOM
        private bool _dirty;
        private bool _loading;

        public EditorWindow(string fullPath)
        {
            InitializeComponent();
            _path = fullPath;
            TbTitle.Text = Path.GetFileName(fullPath);
            Title = "ShaiyaServerHub — " + TbTitle.Text;
            TbPath.Text = fullPath;
            TbPath.ToolTip = fullPath;
            TbKind.Text = KindLabel(fullPath);
            Load();
            Loaded += (s, e) => TbEditor.Focus();
        }

        private static string KindLabel(string path)
        {
            switch (ScriptItem.KindFromExtension(path))
            {
                case ScriptKind.Sql: return "SQL";
                case ScriptKind.PowerShell: return "PS1";
                case ScriptKind.Batch: return "BAT";
                default:
                    string ext = (Path.GetExtension(path) ?? "").TrimStart('.').ToUpperInvariant();
                    return string.IsNullOrEmpty(ext) ? "FILE" : ext;
            }
        }

        private void Load()
        {
            _loading = true;
            try
            {
                if (File.Exists(_path))
                {
                    byte[] bytes = File.ReadAllBytes(_path);
                    _encoding = DetectEncoding(bytes, out int bomLen);
                    TbEditor.Text = _encoding.GetString(bytes, bomLen, bytes.Length - bomLen);
                }
                else
                {
                    TbEditor.Text = ""; // file nuovo (creato vuoto dall'host)
                }
                SetStatus("", true);
            }
            catch (Exception ex)
            {
                TbEditor.Text = "";
                SetStatus(L.Err(ex.Message), false);
            }
            finally { _loading = false; MarkClean(); }
        }

        // Rileva BOM (UTF-8, UTF-16 LE/BE); altrimenti UTF-8 senza BOM (default moderno).
        private static Encoding DetectEncoding(byte[] b, out int bomLen)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { bomLen = 3; return new UTF8Encoding(true); }
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { bomLen = 2; return new UnicodeEncoding(false, true); }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { bomLen = 2; return new UnicodeEncoding(true, true); }
            bomLen = 0; return new UTF8Encoding(false);
        }

        private void TbEditor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_loading) return;
            if (!_dirty) { _dirty = true; TbDirty.Text = "●"; }
        }

        private void MarkClean() { _dirty = false; TbDirty.Text = ""; }

        private void SetStatus(string text, bool ok)
        {
            TbStatus.Text = text;
            TbStatus.Foreground = new System.Windows.Media.SolidColorBrush(ok
                ? System.Windows.Media.Color.FromRgb(0x4D, 0xC8, 0x70)
                : System.Windows.Media.Color.FromRgb(0xF4, 0x6A, 0x6A));
        }

        private bool Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                // Normalizza i fine-riga a CRLF (Windows) e scrive con la codifica originale.
                string text = TbEditor.Text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                File.WriteAllText(_path, text, _encoding);
                MarkClean();
                SetStatus(L.FileSaved, true);
                DialogResult = true; // segnala all'host di ricaricare
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(L.Err(ex.Message), false);
                return false;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) => Save();

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { if (ConfirmDiscard()) Close(); }
        private void BtnClose_Click(object sender, MouseButtonEventArgs e) { if (ConfirmDiscard()) Close(); }

        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;
            var r = MessageBox.Show(L.ConfirmDiscardChanges, "ShaiyaServerHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return r == MessageBoxResult.Yes;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Ctrl+S = salva
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                Save();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private void DragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    }
}
