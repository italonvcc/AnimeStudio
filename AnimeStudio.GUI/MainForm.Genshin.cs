using System;
using System.Linq;
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
