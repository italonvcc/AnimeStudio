using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AnimeStudio.GUI
{
    partial class MainForm
    {
        private GenshinWeaponReferences weaponReferences;

        private void UpdateWeaponCatalogAvailability()
        {
            if (weaponCatalogButton == null) return;
            string reason = !Studio.Game.Type.IsGI() ? "Select Genshin Impact as the target game."
                : operationClock != null ? "Wait for the current operation to finish."
                : string.IsNullOrEmpty(characterMapPath) || characterMapEntries == null ? "Load a Genshin asset map in Asset Browser."
                : null;
            weaponCatalogButton.Enabled = reason == null;
            weaponCatalogButton.ToolTipText = reason ?? "Open the indexed weapon source catalog.";
        }

        private async void OpenWeaponCatalog(object sender, EventArgs args)
        {
            UpdateWeaponCatalogAvailability();
            if (!weaponCatalogButton.Enabled) return;
            string mapPath = characterMapPath;
            var mapEntries = characterMapEntries.ToArray();
            try
            {
                if (weaponReferences == null)
                {
                    using var preparation = new WeaponIndexProgressDialog();
                    BeginOperation("Indexing Genshin weapon sources");
                    try
                    {
                        preparation.Show(this);
                        Action<string> update = message => { CharacterProgress(message); preparation.SetProgress(message); };
                        weaponReferences = await Task.Run(() => GenshinWeaponReferences.Prepare(mapPath, mapEntries, update, preparation.Token));
                    }
                    finally { preparation.Finish(); EndOperation(); }
                }
                if (!Studio.Game.Type.IsGI() || !string.Equals(mapPath, characterMapPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The loaded map or target game changed. Reopen the weapon catalog.");
                var selected = assetBrowser != null && !assetBrowser.IsDisposed ? assetBrowser.GetSelectedMapEntries() : Array.Empty<AssetEntry>();
                using var catalog = new WeaponCatalogDialog(this, weaponReferences, selected);
                catalog.ShowDialog(this);
            }
            catch (OperationCanceledException) { CharacterProgress("Weapon source indexing cancelled."); }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
                MessageBox.Show(this, e.Message, "Weapon catalog unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { UpdateWeaponCatalogAvailability(); }
        }

        private sealed class WeaponIndexProgressDialog : Form
        {
            private readonly CancellationTokenSource cancellation = new();
            private readonly Label message = new() { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Text = "Scanning indexed weapon sources..." };
            private bool finished;
            internal CancellationToken Token => cancellation.Token;

            internal WeaponIndexProgressDialog()
            {
                Text = "Preparing weapon catalog";
                StartPosition = FormStartPosition.CenterParent;
                Size = new Size(520, 135);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                var cancel = new Button { Text = "Cancel", Dock = DockStyle.Right, Width = 90 };
                cancel.Click += (_, _) => RequestCancel();
                Controls.Add(message);
                Controls.Add(cancel);
                FormClosing += (_, e) => { if (!finished) { RequestCancel(); e.Cancel = true; } };
            }

            internal void SetProgress(string text)
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return;
                if (InvokeRequired)
                {
                    try { BeginInvoke(new Action<string>(SetProgress), text); }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                    return;
                }
                message.Text = text;
            }

            private void RequestCancel()
            {
                cancellation.Cancel();
                message.Text = "Cancellation requested. Finishing the current scan step...";
            }

            internal void Finish()
            {
                finished = true;
                Close();
                cancellation.Dispose();
            }
        }

        private sealed class WeaponCatalogDialog : Form
        {
            private readonly MainForm owner;
            private readonly GenshinWeaponReferences references;
            private readonly List<GenshinWeaponFamily> allFamilies;
            private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search source name, class or key" };
            private readonly ListView families = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
            private readonly CheckBox materials = new() { Text = "Materials and textures", Checked = true, AutoSize = true };
            private readonly CheckBox animations = new() { Text = "Weapon animations", AutoSize = true };
            private readonly CheckBox effects = new() { Text = "Weapon effects", AutoSize = true };
            private readonly CheckBox unityImport = new() { Text = "Generate Unity import setup (offline, unvalidated)", AutoSize = true };
            private readonly Button selectedButton = new() { Text = "Export Selected Weapon", AutoSize = true };
            private readonly Button catalogButton = new() { Text = "Export Whole Indexed Catalog", AutoSize = true };
            private readonly Button cancelButton = new() { Text = "Cancel", AutoSize = true, Enabled = false };
            private readonly Button outputButton = new() { Text = "Open Output Folder", AutoSize = true, Enabled = false };
            private readonly Button reportButton = new() { Text = "Open Report", AutoSize = true, Enabled = false };
            private readonly Label header = new() { Dock = DockStyle.Fill, AutoSize = true };
            private readonly Label selection = new() { Dock = DockStyle.Fill, AutoSize = true };
            private readonly Label completion = new() { Dock = DockStyle.Fill, AutoSize = true };
            private readonly TextBox progress = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
            private CancellationTokenSource cancellation;
            private bool busy;
            private string outputPath;
            private string reportPath;

            internal WeaponCatalogDialog(MainForm owner, GenshinWeaponReferences references, AssetEntry[] selectedMapEntries)
            {
                this.owner = owner;
                this.references = references;
                allFamilies = references.Catalog.Families.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
                Text = "Genshin weapons — indexed source catalog";
                StartPosition = FormStartPosition.CenterParent;
                MinimumSize = new Size(820, 620);
                Size = new Size(1060, 750);

                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 10 };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Controls.Add(layout);

                header.Text = $"Installed source: {references.GameVersion ?? "unknown"}  |  Indexed records: {allFamilies.Count} ({references.Catalog.RootCount} rooted, {references.Catalog.MissingRootMeshCount} mesh-only). This source index is not a verified complete game inventory. Search changes only the visible list; whole-catalog export uses all {allFamilies.Count} records.";
                layout.Controls.Add(header, 0, 0);
                layout.Controls.Add(search, 0, 1);
                families.Columns.Add("Source name", 225);
                families.Columns.Add("Class", 90);
                families.Columns.Add("Roots", 55);
                families.Columns.Add("Meshes", 65);
                families.Columns.Add("Animation candidates", 125);
                families.Columns.Add("Effect candidates", 110);
                families.Columns.Add("Stable key", 280);
                layout.Controls.Add(families, 0, 2);
                selection.Text = "Select a source family. All indexed roots in it are exported; source variant semantics remain unverified.";
                layout.Controls.Add(selection, 0, 3);

                var optionRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
                optionRow.Controls.AddRange(new Control[] { materials, animations, effects, unityImport });
                layout.Controls.Add(optionRow, 0, 4);
                var note = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = "Effects without materials export source data with appearance dependencies omitted. Effect-local clips may be needed even when standalone weapon animations are off. Unity setup generates a preview recipe; live import, bindings and playback remain unvalidated. Linked audio is unsupported." };
                layout.Controls.Add(note, 0, 5);
                var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
                actions.Controls.AddRange(new Control[] { selectedButton, catalogButton, cancelButton, outputButton, reportButton });
                layout.Controls.Add(actions, 0, 6);
                layout.Controls.Add(progress, 0, 7);
                completion.Text = "No export has run.";
                layout.Controls.Add(completion, 0, 8);
                layout.Controls.Add(new Label { AutoSize = true, Text = "Each run uses a new folder under your selected parent. Existing output is never overwritten." }, 0, 9);

                var preferences = ReadPreferences();
                materials.Checked = preferences.Materials;
                animations.Checked = preferences.Animations;
                effects.Checked = preferences.Effects;
                unityImport.Checked = preferences.UnityImport;
                materials.CheckedChanged += (_, _) => SavePreferences();
                animations.CheckedChanged += (_, _) => SavePreferences();
                effects.CheckedChanged += (_, _) => SavePreferences();
                unityImport.CheckedChanged += (_, _) => SavePreferences();
                search.TextChanged += (_, _) => RefreshFamilies();
                families.SelectedIndexChanged += (_, _) => RefreshSelection();
                selectedButton.Click += async (_, _) => await ExportAsync(false);
                catalogButton.Click += async (_, _) => await ExportAsync(true);
                cancelButton.Click += (_, _) => { cancellation?.Cancel(); cancelButton.Enabled = false; SetProgress("Cancellation requested. Waiting for the current safe export stage."); };
                outputButton.Click += (_, _) => OpenPath(outputPath);
                reportButton.Click += (_, _) => OpenPath(reportPath);
                FormClosing += (_, e) => { if (busy) { cancellation?.Cancel(); e.Cancel = true; SetProgress("Cancellation requested. The dialog will stay open until export stops."); } };

                RefreshFamilies();
                if (selectedMapEntries.Length > 0)
                {
                    // Source topology may group a selected child GameObject or
                    // Animator into an enclosing family. Its old deterministic
                    // selector key remains an alias even after that grouping.
                    var selectorKeys = GenshinWeaponCatalog.Build(selectedMapEntries).Families
                        .Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
                    var matches = allFamilies.Where(f =>
                        selectorKeys.Contains(f.Key) || f.SelectorAliases.Any(selectorKeys.Contains) ||
                        f.Roots.Concat(f.Meshes).Any(root => selectedMapEntries.Any(entry => SameSource(root, entry)))).ToList();
                    if (matches.Count == 1)
                    {
                        search.Text = matches[0].Key;
                        SelectFamily(matches[0]);
                    }
                    else if (matches.Count > 1)
                        selection.Text = $"The Asset Browser selection matches {matches.Count} indexed families. Select one explicitly.";
                }
            }

            private static bool SameSource(AssetEntry a, AssetEntry b) => a.PathID == b.PathID && a.Offset == b.Offset && string.Equals(a.Source, b.Source, StringComparison.OrdinalIgnoreCase);

            private GenshinWeaponFamily SelectedFamily => families.SelectedItems.Count == 1 ? (GenshinWeaponFamily)families.SelectedItems[0].Tag : null;

            private void RefreshFamilies()
            {
                var previousKey = SelectedFamily?.Key;
                string query = search.Text.Trim();
                families.BeginUpdate();
                families.Items.Clear();
                foreach (var family in allFamilies.Where(f => query.Length == 0 || f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || f.SourceName.Contains(query, StringComparison.OrdinalIgnoreCase) || f.WeaponClass.Contains(query, StringComparison.OrdinalIgnoreCase) || f.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || f.SelectorAliases.Any(alias => alias.Contains(query, StringComparison.OrdinalIgnoreCase))))
                {
                    var item = new ListViewItem(new[] { family.SourceName, family.WeaponClass, family.Roots.Count.ToString(CultureInfo.InvariantCulture), family.Meshes.Count.ToString(CultureInfo.InvariantCulture), family.AnimationCandidates.Count.ToString(CultureInfo.InvariantCulture), family.EffectCandidates.Count.ToString(CultureInfo.InvariantCulture), family.Key }) { Tag = family };
                    families.Items.Add(item);
                    if (family.Key == previousKey) item.Selected = true;
                }
                families.EndUpdate();
                RefreshSelection();
            }

            private void SelectFamily(GenshinWeaponFamily family)
            {
                foreach (ListViewItem item in families.Items)
                    if (ReferenceEquals(item.Tag, family)) { item.Selected = true; item.EnsureVisible(); break; }
            }

            private void RefreshSelection()
            {
                selectedButton.Enabled = !busy && SelectedFamily != null;
                if (SelectedFamily is { } family)
                    selection.Text = $"Selected: {family.SourceName} ({family.WeaponClass}); {family.Roots.Count} source root(s), {family.Meshes.Count} linked mesh(es). {family.DiscoveryStatus}. Animation/effect counts are candidates until source links are verified.";
            }

            private async Task ExportAsync(bool wholeCatalog)
            {
                if (busy) return;
                var family = SelectedFamily;
                if (!wholeCatalog && family == null) return;
                using var folderDialog = new FolderBrowserDialog { Description = "Select a parent folder for a new weapon export run" };
                if (folderDialog.ShowDialog(this) != DialogResult.OK) return;
                if (effects.Checked && !materials.Checked)
                {
                    var choice = MessageBox.Show(this, "Effects will be exported as source data without their material and texture appearance dependencies. Continue?", "Appearance dependencies omitted", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                    if (choice != DialogResult.OK) return;
                }
                var options = new GenshinWeaponOptions(animations.Checked, effects.Checked, materials.Checked, unityImport.Checked);
                string version = SafePathPart(references.GameVersion ?? "unknown");
                string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
                string scope = wholeCatalog ? "Catalog" : SafePathPart(family.Key);
                string nonce = Guid.NewGuid().ToString("N")[..8];
                outputPath = Path.Combine(folderDialog.SelectedPath, $"Genshin-Weapons-{version}-{scope}-{stamp}-{nonce}");
                // The service owns creation. It rejects any existing destination.
                if (Directory.Exists(outputPath) || File.Exists(outputPath))
                {
                    MessageBox.Show(this, "The export destination already exists. Select a parent again for a new run.", "Weapon export destination", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                reportPath = null;
                reportButton.Enabled = false;
                outputButton.Enabled = false;
                completion.Text = wholeCatalog ? $"Exporting all {allFamilies.Count} indexed source records (search filter ignored)..." : $"Exporting {family.Name}...";
                progress.Clear();
                cancellation = new CancellationTokenSource();
                busy = true;
                SetBusyControls();
                owner.BeginOperation(wholeCatalog ? "Exporting indexed weapon catalog" : "Exporting " + family.Name);
                try
                {
                    Action<string> update = message => { owner.CharacterProgress(message); SetProgress(message); };
                    var token = cancellation.Token;
                    reportPath = await Task.Run(() => wholeCatalog
                        ? GenshinWeaponExporter.ExportCatalog(references, outputPath, options, update, token)
                        : GenshinWeaponExporter.Export(references, family, outputPath, options, update, token));
                    completion.Text = ReadReportSummary(reportPath);
                    ShowReportResults(reportPath);
                }
                catch (OperationCanceledException)
                {
                    reportPath = FindExistingReport(outputPath);
                    completion.Text = reportPath == null ? "Cancelled. Check the run folder for partial output." : ReadReportSummary(reportPath);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.ToString());
                    reportPath = FindExistingReport(outputPath);
                    completion.Text = reportPath == null ? "Export failed. Check the run folder for any partial output." : ReadReportSummary(reportPath);
                    SetProgress(ex.Message);
                }
                finally
                {
                    owner.EndOperation();
                    cancellation.Dispose();
                    cancellation = null;
                    busy = false;
                    outputButton.Enabled = Directory.Exists(outputPath);
                    reportButton.Enabled = reportPath != null && File.Exists(reportPath);
                    SetBusyControls();
                }
            }

            private void SetBusyControls()
            {
                search.Enabled = !busy;
                families.Enabled = !busy;
                materials.Enabled = !busy;
                animations.Enabled = !busy;
                effects.Enabled = !busy;
                unityImport.Enabled = !busy;
                selectedButton.Enabled = !busy && SelectedFamily != null;
                catalogButton.Enabled = !busy && allFamilies.Count > 0;
                cancelButton.Enabled = busy && cancellation?.IsCancellationRequested != true;
            }

            private void SetProgress(string message)
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return;
                if (InvokeRequired)
                {
                    try { BeginInvoke(new Action<string>(SetProgress), message); }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                    return;
                }
                progress.AppendText(message + Environment.NewLine);
            }

            private static string SafePathPart(string value)
            {
                string text = new(value.Select(c => c <= 127 && (char.IsLetterOrDigit(c) || c == '-' || c == '_') ? c : '_').Take(32).ToArray());
                return text.Length == 0 ? "unknown" : text;
            }

            private static string ReadReportSummary(string path)
            {
                if (!File.Exists(path)) return "Exporter returned without a readable report. Inspect the output folder.";
                try
                {
                    var report = JObject.Parse(File.ReadAllText(path));
                    if (report["entries"] is JArray entries)
                    {
                        string[] statuses = { "Complete", "Partial", "Failed", "Cancelled" };
                        string counts = string.Join(", ", statuses.Select(status =>
                            $"{status.ToLowerInvariant()}: {entries.Count(entry => string.Equals((string)entry["status"], status, StringComparison.OrdinalIgnoreCase))}"));
                        return $"Indexed catalog accounted for {entries.Count}/{(int?)report["total"] ?? entries.Count} source records; {counts}. See report for each result.";
                    }
                    string familyStatus = (string)report["export"]?["status"] ?? "Unknown";
                    int variants = (report["export"]?["variants"] as JArray)?.Count ?? 0;
                    return $"Selected family export: {familyStatus}; {variants} indexed root result(s). Discovery and Unity playback have separate statuses in the report.";
                }
                catch (Exception ex) { return "Export returned a report, but its summary could not be read: " + ex.Message; }
            }

            private static string FindExistingReport(string folder)
            {
                if (!Directory.Exists(folder)) return null;
                string ledger = Path.Combine(folder, "weapon-export.json");
                if (File.Exists(ledger)) return ledger;
                return Directory.EnumerateFiles(folder, "*.weapon.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
            }

            private void ShowReportResults(string path)
            {
                try
                {
                    var report = JObject.Parse(File.ReadAllText(path));
                    if (report["entries"] is JArray entries)
                    {
                        SetProgress("Per-family results:");
                        foreach (var entry in entries)
                            SetProgress($"{(string)entry["status"] ?? "Unknown"}: {(string)entry["Name"] ?? (string)entry["Key"] ?? "unnamed"}");
                    }
                    else
                    {
                        SetProgress($"Family result: {(string)report["export"]?["status"] ?? "Unknown"}");
                        if (report["export"]?["variants"] is JArray variants)
                            foreach (var variant in variants)
                                SetProgress($"  {(string)variant["status"] ?? "Unknown"}: {(string)variant["source"]?["Name"] ?? "source root"}");
                    }
                }
                catch (Exception ex) { SetProgress("Could not list report entries: " + ex.Message); }
            }

            private static void OpenPath(string path)
            {
                if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path))) return;
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Could not open export path", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }

            private sealed class WeaponPreferences
            {
                public bool Materials { get; set; } = true;
                public bool Animations { get; set; }
                public bool Effects { get; set; }
                public bool UnityImport { get; set; }
            }

            private static string PreferencesPath => Path.Combine(Application.UserAppDataPath, "GenshinWeaponOptions.json");
            private static WeaponPreferences ReadPreferences()
            {
                try { return File.Exists(PreferencesPath) ? JsonConvert.DeserializeObject<WeaponPreferences>(File.ReadAllText(PreferencesPath)) ?? new() : new(); }
                catch { return new(); }
            }

            private void SavePreferences()
            {
                try
                {
                    Directory.CreateDirectory(Application.UserAppDataPath);
                    File.WriteAllText(PreferencesPath, JsonConvert.SerializeObject(new WeaponPreferences { Materials = materials.Checked, Animations = animations.Checked, Effects = effects.Checked, UnityImport = unityImport.Checked }));
                }
                catch (Exception ex) { Logger.Warning("Weapon option preferences could not be saved: " + ex.Message); }
            }
        }
    }
}
