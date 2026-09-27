using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AnimeStudio.GUI
{
    partial class MainForm
    {
        private ToolStripMenuItem myToolsMenu, characterExportButton;
        private ToolStripMenuItem characterVoices, characterVfx, characterAnimations;
        private Task<GenshinCharacterReferences> characterReferences;
        private string characterMapPath;
        private List<AssetEntry> characterMapEntries;
        private bool characterExportBusy;

        private void UpdateGameExportMenu()
        {
            if (myToolsMenu == null)
            {
                myToolsMenu = new ToolStripMenuItem("My tools");
                var character = new ToolStripMenuItem("Genshin character");
                var mannequin = new ToolStripMenuItem("Genshin manequin");
                ToolStripMenuItem Check(string text) => new(text) { CheckOnClick = true };
                characterVoices = Check("Export Voice clips");
                characterVfx = Check("Export VFX");
                characterAnimations = Check("Export unity animations");
                characterExportButton = new ToolStripMenuItem("Export Character", null, ExportCharacter);
                character.DropDownItems.AddRange(new ToolStripItem[] { characterVoices, characterVfx, characterAnimations, new ToolStripSeparator(), characterExportButton });
                mannequin.DropDownItems.AddRange(new ToolStripItem[] { Check("Export Voice clips"), Check("Export VFX"), Check("Export unity animations"), new ToolStripSeparator(),
                    new ToolStripMenuItem("Export all manequins") { Enabled = false, ToolTipText = "Mannequin export will be implemented after the character workflow." } });
                foreach (var menu in new[] { character, mannequin })
                    menu.DropDown.Closing += (_, e) => {
                        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && menu.DropDown.GetItemAt(menu.DropDown.PointToClient(Cursor.Position)) is ToolStripMenuItem item && item.CheckOnClick) e.Cancel = true;
                    };
                character.DropDownOpening += (_, _) => characterExportButton.Enabled = !characterExportBusy && characterReferences?.IsCompletedSuccessfully == true && SelectedCharacter() != null;
                myToolsMenu.DropDownItems.AddRange(new ToolStripItem[] { character, mannequin });
                menuStrip1.Items.Insert(menuStrip1.Items.IndexOf(aboutToolStripMenuItem), myToolsMenu);
            }
            myToolsMenu.Enabled = Studio.Game.Type.IsGI();
        }
        internal async Task OnGenshinAssetMapLoaded(string path, IEnumerable<AssetEntry> entries)
        {
            characterMapPath = path; characterMapEntries = entries.ToList(); characterReferences = null;
            if (!Studio.Game.Type.IsGI() || !ResourceMap.GetGameType().IsGI()) return;
            characterExportButton.Enabled = false;
            BeginOperation("Preparing character references");
            try
            {
                var snapshot = characterMapEntries;
                characterReferences = Task.Run(() => GenshinCharacterReferences.Prepare(path, snapshot, CharacterProgress));
                await characterReferences;
                CharacterProgress("Genshin references ready. Select one Animator, then My tools > Genshin character.");
            }
            catch (Exception e) { Logger.Error(e.ToString()); MessageBox.Show(this, e.Message, "Genshin reference refresh failed"); }
            finally { EndOperation(); }
        }
        internal void ClearGenshinReferences() { characterReferences = null; characterMapEntries = null; characterMapPath = null; }
        private AssetEntry SelectedCharacter()
        {
            if (assetBrowser != null && !assetBrowser.IsDisposed)
            {
                var entries = assetBrowser.GetSelectedMapEntries();
                if (entries.Length > 0) return entries.Length == 1 && entries[0].Type == ClassIDType.Animator ? entries[0] : null;
            }
            var selected = GetSelectedAssets();
            if (selected.Count != 1 || selected[0].Asset is not Animator animator) return null;
            return characterMapEntries?.SingleOrDefault(e => e.Type == ClassIDType.Animator && e.PathID == animator.m_PathID && e.Offset == animator.assetsFile.offset && string.Equals(e.Source, animator.assetsFile.originalPath, StringComparison.OrdinalIgnoreCase));
        }
        private void CharacterProgress(string message)
        {
            Logger.Info(message);
            StatusStripUpdate(message);
        }
        private async void ExportCharacter(object sender, EventArgs args)
        {
            var selected = SelectedCharacter();
            if (selected == null || characterReferences?.IsCompletedSuccessfully != true) { MessageBox.Show(this, "Load a Genshin asset map and select one character Animator in Asset Browser."); return; }
            var options = new GenshinCharacterOptions(characterVoices.Checked, characterVfx.Checked, characterAnimations.Checked, Properties.Settings.Default.exportMaterials);
            using var dialog = new FolderBrowserDialog { Description = "Select where to create the character folder" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string name = string.Concat(selected.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string destination = Path.Combine(dialog.SelectedPath, name);
            characterExportBusy = true; BeginOperation("Exporting " + selected.Name);
            try
            {
                var references = await characterReferences;
                await Task.Run(() => GenshinCharacterExporter.Export(references, selected, destination, options, CharacterProgress));
                operationClock.Stop();
                CharacterProgress("Export complete: " + destination);
                MessageBox.Show(this, "Character exported to:\n" + destination + "\n\nCopy this character folder and its sibling Generic folder under the same parent in Unity Assets. Its importer creates the character prefab, Avatar and animation controller automatically.", "Character export complete");
            }
            catch (Exception e) { Logger.Error(e.ToString()); MessageBox.Show(this, e.Message + "\nAny partial output is marked EXPORT-INCOMPLETE.txt.", "Character export failed"); }
            finally
            {
                characterExportBusy = false; EndOperation();
            }
        }
    }
}
