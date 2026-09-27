using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AnimeStudio.GUI
{
    partial class MainForm
    {
        private ToolStripMenuItem genshinExportMenu;

        private void UpdateGameExportMenu()
        {
            if (genshinExportMenu == null)
            {
                genshinExportMenu = new ToolStripMenuItem("Genshin Impact");
                genshinExportMenu.DropDownItems.Add("Export model with dependencies and manifest...", null, ExportGenshinModel);
                genshinExportMenu.DropDownItems.Add("Export selected materials (JSON)", null,
                    (_, _) => ExportGenshinSelection(ClassIDType.Material));
                genshinExportMenu.DropDownItems.Add("Export selected animation clips (.anim)", null,
                    (_, _) => ExportGenshinSelection(ClassIDType.AnimationClip));
                genshinExportMenu.DropDownItems.Add("Export selected model hierarchy with selected clips (FBX)", null,
                    (sender, args) => exportSelectedObjectsmergeWithAnimationClipToolStripMenuItem_Click(sender, args));
                genshinExportMenu.DropDownItems.Add(new ToolStripSeparator());
                genshinExportMenu.DropDownItems.Add("Animation export limitations", null, (_, _) => MessageBox.Show(this,
                    "FBX export currently omits humanoid muscle curves. Body and finger motion may be missing even when secondary bones move. Export selected clips as .anim to preserve humanoid curves; this does not bake them onto the FBX skeleton.",
                    "Genshin animation export", MessageBoxButtons.OK, MessageBoxIcon.Information));
                exportToolStripMenuItem.DropDownItems.Add(genshinExportMenu);
            }
            genshinExportMenu.Visible = Studio.Game.Type.IsGI();
        }

        private async void ExportGenshinModel(object sender, EventArgs args)
        {
            var selected = GetSelectedAssets();
            var roots = selected.Where(a => a.Asset is Animator).ToArray();
            if (roots.Length != 1 || selected.Any(a => a.Asset is not (Animator or AnimationClip)))
            {
                MessageBox.Show(this, "Select one Animator and optionally its AnimationClip assets first.", "Genshin model export");
                return;
            }
            var map = ResourceMap.GetEntries().ToArray();
            if (!ResourceMap.GetGameType().IsGI() || map.Length == 0)
            {
                MessageBox.Show(this, "Load a Genshin asset map in Asset Browser first so external meshes, rigs, materials and textures can be located.", "Genshin model export");
                return;
            }
            using var dialog = new FolderBrowserDialog { Description = "Select the parent folder for a new model export" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var animator = (Animator)roots[0].Asset;
            var clips = selected.Select(a => a.Asset).OfType<AnimationClip>().ToArray();
            var name = string.Concat(animator.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var destination = Path.Combine(dialog.SelectedPath, name);
            Enabled = false;
            try
            {
                var result = await Task.Run(() => GenshinModelExporter.Export(Studio.assetsManager, map, animator, destination, clips));
                MessageBox.Show(this, $"Exported {result}\nSee manifest.json for unresolved references and scope.", "Genshin model export");
            }
            catch (Exception ex) { Logger.Error(ex.Message); MessageBox.Show(this, ex.Message, "Genshin export failed"); }
            finally { Enabled = true; }
        }

        private void ExportGenshinSelection(ClassIDType expectedType)
        {
            var selected = GetSelectedAssets();
            if (selected.Count == 0 || selected.Any(item => item.Asset.type != expectedType))
            {
                MessageBox.Show(this, $"Select only {expectedType} assets in the asset list first.",
                    "Genshin export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            exportSelectedAssetsToolStripMenuItem_Click(this, EventArgs.Empty);
        }
    }
}
