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
                genshinExportMenu.DropDownItems.Add("Export model or prefab with dependencies and manifest...", null, ExportGenshinModel);
                genshinExportMenu.DropDownItems.Add("Export named voices / event media...", null, ExportGenshinAudio);
                var assemblyItem = new ToolStripMenuItem("Assemble compatible Manekin parts from request...") { Tag = "Assembly" };
                assemblyItem.Click += ExportGenshinRequest;
                genshinExportMenu.DropDownItems.Add(assemblyItem);
                var vfxItem = new ToolStripMenuItem("Export per-action VFX dependencies from request...") { Tag = "Vfx" };
                vfxItem.Click += ExportGenshinRequest;
                genshinExportMenu.DropDownItems.Add(vfxItem);
                var bakeItem = new ToolStripMenuItem("Export model and humanoid clips (Unity-assisted FBX + .anim)...") { Tag = "UnityBake" };
                bakeItem.Click += ExportGenshinModel;
                genshinExportMenu.DropDownItems.Add(bakeItem);
                var layerItem = new ToolStripMenuItem("Combine body + secondary clip (Unity-assisted FBX + .anim)...") { Tag = "UnityLayer" };
                layerItem.Click += ExportGenshinModel;
                genshinExportMenu.DropDownItems.Add(layerItem);
                genshinExportMenu.DropDownItems.Add("Export selected materials (JSON)", null,
                    (_, _) => ExportGenshinSelection(ClassIDType.Material));
                genshinExportMenu.DropDownItems.Add("Export selected animation clips (.anim)", null,
                    (_, _) => ExportGenshinSelection(ClassIDType.AnimationClip));
                genshinExportMenu.DropDownItems.Add("Export selected model hierarchy with selected clips (FBX)", null,
                    (sender, args) => exportSelectedObjectsmergeWithAnimationClipToolStripMenuItem_Click(sender, args));
                genshinExportMenu.DropDownItems.Add(new ToolStripSeparator());
                genshinExportMenu.DropDownItems.Add("Animation export limitations", null, (_, _) => MessageBox.Show(this,
                    "The regular FBX path omits humanoid muscle curves. Use Unity-assisted export with a locally installed, licensed Unity Editor to bake humanoid body motion. Source .anim clips are preserved in either model export. Clips containing only secondary motion still need their shared body clip; runtime controllers, IK and VFX are not recreated by animation baking.",
                    "Genshin animation export", MessageBoxButtons.OK, MessageBoxIcon.Information));
                exportToolStripMenuItem.DropDownItems.Add(genshinExportMenu);
            }
            genshinExportMenu.Visible = Studio.Game.Type.IsGI();
        }

        private async void ExportGenshinModel(object sender, EventArgs args)
        {
            var selected = GetSelectedAssets();
            var roots = selected.Where(a => a.Asset is Animator or GameObject).ToArray();
            if (roots.Length != 1 || selected.Any(a => a.Asset is not (Animator or GameObject or AnimationClip)))
            {
                MessageBox.Show(this, "Select one Animator or prefab GameObject and optionally its AnimationClip assets first.", "Genshin model export");
                return;
            }
            var map = ResourceMap.GetEntries().ToArray();
            if (!ResourceMap.GetGameType().IsGI() || map.Length == 0)
            {
                MessageBox.Show(this, "Load a Genshin asset map in Asset Browser first so external meshes, rigs, materials and textures can be located.", "Genshin model export");
                return;
            }
            string unityEditor = null;
            bool combine = sender is ToolStripMenuItem layer && (string)layer.Tag == "UnityLayer";
            System.Collections.Generic.Dictionary<long, long> animationLayers = null;
            if (combine)
            {
                var pair = selected.Select(a => a.Asset).OfType<AnimationClip>().ToArray();
                bool Body(AnimationClip c) => c.m_ClipBindingConstant?.genericBindings.Any(b => b.typeID == ClassIDType.Animator && b.customType == 8 && b.attribute >= 42 && b.attribute < 137) == true;
                if (pair.Length != 2 || pair.Count(Body) != 1)
                {
                    MessageBox.Show(this, "Select one Animator, one humanoid body clip, and one secondary-only clip. Their sample rates and durations must match. The secondary Transform curves will override matching body-clip Transform curves; original .anim files are also retained.", "Combine animation layers");
                    return;
                }
                animationLayers = new() { [pair.Single(Body).m_PathID] = pair.Single(c => !Body(c)).m_PathID };
            }
            if (combine || sender is ToolStripMenuItem item && (string)item.Tag == "UnityBake")
            {
                if (!selected.Any(a => a.Asset is AnimationClip))
                {
                    MessageBox.Show(this, "Select the humanoid AnimationClip assets together with the Animator.", "Genshin animation export");
                    return;
                }
                using var editorDialog = new OpenFileDialog { Title = "Select your installed Unity Editor", Filter = "Unity Editor|Unity.exe", CheckFileExists = true };
                if (editorDialog.ShowDialog(this) != DialogResult.OK) return;
                unityEditor = editorDialog.FileName;
            }
            using var dialog = new FolderBrowserDialog { Description = "Select the parent folder for a new model export" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var animator = roots[0].Asset;
            var clips = selected.Select(a => a.Asset).OfType<AnimationClip>().ToArray();
            var name = string.Concat(animator.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var destination = Path.Combine(dialog.SelectedPath, name);
            Enabled = false;
            try
            {
                var result = await Task.Run(() => GenshinModelExporter.Export(Studio.assetsManager, map, animator, destination, clips, unityEditor, animationLayers));
                MessageBox.Show(this, $"Exported {result}\nSee manifest.json for unresolved references and scope.", "Genshin model export");
            }
            catch (Exception ex) { Logger.Error(ex.Message); MessageBox.Show(this, ex.Message, "Genshin export failed"); }
            finally { Enabled = true; }
        }

        private async void ExportGenshinRequest(object sender, EventArgs args)
        {
            using var request = new OpenFileDialog { Title = "Select the assembly or per-action VFX request", Filter = "Export request|*.json", CheckFileExists = true };
            if (request.ShowDialog(this) != DialogResult.OK) return;
            using var maps = new OpenFileDialog { Title = "Select the original asset map and any supplemental scene maps", Filter = "Asset maps|*.map;*.json", Multiselect = true, CheckFileExists = true };
            if (maps.ShowDialog(this) != DialogResult.OK) return;
            using var output = new FolderBrowserDialog { Description = "Select a parent directory for a new export" };
            if (output.ShowDialog(this) != DialogResult.OK) return;
            string destination = Path.Combine(output.SelectedPath, Path.GetFileNameWithoutExtension(request.FileName));
            bool assembly = sender is ToolStripMenuItem item && (string)item.Tag == "Assembly";
            Enabled = false;
            try
            {
                var result = await Task.Run(() =>
                {
                    var entries = new System.Collections.Generic.List<AssetEntry>();
                    foreach (string map in maps.FileNames)
                    {
                        if (ResourceMap.FromFile(map) < 0 || !ResourceMap.GetGameType().IsGI()) throw new InvalidDataException("Select valid Genshin asset maps.");
                        entries.AddRange(ResourceMap.GetEntries());
                    }
                    var unique = entries.DistinctBy(e => (e.Source, e.Offset, e.PathID, e.Type)).ToArray();
                    return assembly ? GenshinAssemblyExporter.Export(Studio.assetsManager, unique, request.FileName, destination)
                        : GenshinVfxExporter.Export(Studio.assetsManager, unique, request.FileName, destination);
                });
                MessageBox.Show(this, $"Exported {result}\nSee manifest.json for compatibility checks, missing assets and unsupported components.", "Genshin export");
            }
            catch (Exception ex) { Logger.Error(ex.Message); MessageBox.Show(this, ex.Message, "Genshin export failed"); }
            finally { Enabled = true; }
        }

        private async void ExportGenshinAudio(object sender, EventArgs args)
        {
            using var names = new OpenFileDialog { Title = "Select evidenced voice names or event-media request", Filter = "Named audio request|*.json", CheckFileExists = true };
            if (names.ShowDialog(this) != DialogResult.OK) return;
            using var input = new FolderBrowserDialog { Description = "Select the installed game's AudioAssets folder" };
            if (input.ShowDialog(this) != DialogResult.OK) return;
            using var output = new FolderBrowserDialog { Description = "Select a parent folder for a new named-audio export" };
            if (output.ShowDialog(this) != DialogResult.OK) return;
            using var decoder = new OpenFileDialog { Title = "Optional: select vgmstream-cli for WAV output (Cancel exports WEM only)", Filter = "vgmstream CLI|vgmstream-cli.exe", CheckFileExists = true };
            string decoderPath = decoder.ShowDialog(this) == DialogResult.OK ? decoder.FileName : null;
            string destination = Path.Combine(output.SelectedPath, Path.GetFileNameWithoutExtension(names.FileName));
            Enabled = false;
            try
            {
                await Task.Run(() => GenshinAudioExporter.ExportNames(input.SelectedPath, names.FileName, destination, decoderPath));
                MessageBox.Show(this, $"Export finished: {destination}\nCheck manifest.json for missing languages, ambiguous media, and decode failures.", "Genshin audio export");
            }
            catch (Exception ex) { Logger.Error(ex.Message); MessageBox.Show(this, ex.Message, "Genshin audio export failed"); }
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
