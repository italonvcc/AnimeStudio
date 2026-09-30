using AnimeStudio;
using AnimeStudio.GUI;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--load-cycle") return LoadCycleChecks.Run(args);
        if (args.FirstOrDefault() == "--export-menu") return ExportMenuChecks.Run(args);
        var assembly = typeof(AssetItem).Assembly;
        var mainType = assembly.GetType("AnimeStudio.GUI.MainForm", true)!;
        var studio = assembly.GetType("AnimeStudio.GUI.Studio", true)!;
        // Exercise menu construction without launching the application, OpenGL or an update check.
        var main = RuntimeHelpers.GetUninitializedObject(mainType);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var menu = new MenuStrip(); var about = new ToolStripMenuItem("About");
        menu.Items.Add("File"); menu.Items.Add("Export"); menu.Items.Add(about);
        mainType.GetField("menuStrip1", flags)!.SetValue(main, menu);
        mainType.GetField("aboutToolStripMenuItem", flags)!.SetValue(main, about);
        studio.GetField("Game")!.SetValue(null, GameManager.GetGameByType(GameType.GI));
        mainType.GetMethod("UpdateGameExportMenu", flags)!.Invoke(main, null);
        var tools = menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "My tools");
        Require(menu.Items.IndexOf(tools) + 1 == menu.Items.IndexOf(about), "My tools immediately precedes About");
        Require(tools.DropDownItems.Cast<ToolStripItem>().Select(i => i.Text).SequenceEqual(new[] { "Genshin character", "Genshin manequin" }), "requested submenus only");
        foreach (ToolStripMenuItem submenu in tools.DropDownItems)
        {
            var items = submenu.DropDownItems.OfType<ToolStripMenuItem>().ToArray();
            Require(items.Take(3).Select(i => i.Text).SequenceEqual(new[] { "Export Voice clips", "Export VFX", "Export unity animations" }), submenu.Text + " checkbox labels");
            Require(items.Take(3).All(i => i.CheckOnClick) && items.Length == 4, submenu.Text + " has exactly three checkboxes and one action");
            Require(items.All(i => !i.Text.Contains("material", StringComparison.OrdinalIgnoreCase)), "no extra material option");
        }
        var mannequin = (ToolStripMenuItem)tools.DropDownItems[1];
        Require(!mannequin.DropDownItems.OfType<ToolStripMenuItem>().Last().Enabled, "mannequin export remains unimplemented and disabled");
        studio.GetField("Game")!.SetValue(null, GameManager.GetGameByType(GameType.Normal));
        mainType.GetMethod("UpdateGameExportMenu", flags)!.Invoke(main, null);
        Require(!tools.Enabled && menu.Items.Cast<ToolStripItem>().Count(i => i.Text == "My tools") == 1, "other games disable tools without duplicating menus");
        var status = mainType.GetMethod("StatusStripUpdate", flags)!;
        var progress = mainType.GetMethod("SetProgressBarValue", flags)!;
        var updates = Task.Run(() => {
            for(int i=0;i<10000;i++) { status.Invoke(main,new object[] { "stage " + i }); progress.Invoke(main,new object[] { i }); }
        });
        Require(updates.Wait(TimeSpan.FromSeconds(5)), "worker progress completes without a UI message pump");
        Require((string)mainType.GetField("pendingStatus",flags)!.GetValue(main)! == "stage 9999" &&
            (int)mainType.GetField("pendingProgress",flags)!.GetValue(main)! == 100, "progress coalesces to latest status and clamps percentage");
        var loggerType=assembly.GetType("AnimeStudio.GUI.GUILogger",true)!;
        string logged=null;
        var logger=(ILogger)Activator.CreateInstance(loggerType,new Action<string>(s=>logged=s))!;
        var oldFlags=Logger.Flags; Logger.Flags=LoggerEvent.Info|LoggerEvent.Error;
        try {
            logger.Log(LoggerEvent.Info,"visible stage");
            Require(logged=="visible stage","GUI logger follows global event flags");
            loggerType.GetField("DeferErrors")!.SetValue(logger,true);
            Require(Task.Run(()=>logger.Log(LoggerEvent.Error,"synthetic failure")).Wait(TimeSpan.FromSeconds(5)),"operation errors do not open blocking worker dialogs");
            Require((int)loggerType.GetField("DeferredErrorCount")!.GetValue(logger)! == 1 && logged.Contains("synthetic failure"),"deferred errors are counted and surfaced");
        } finally { Logger.Flags=oldFlags; }

        var previousManager = studio.GetField("assetsManager")!.GetValue(null);
        var manager = new AssetsManager();
        Require(!manager.UseSelectedGenshinOffsets, "exact GI browser offsets are opt-in for other loading/export callers");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var slowStream = new BlockingCloseStream(entered, release);
        var resources = (Dictionary<string,BinaryReader>)typeof(AssetsManager).GetField("resourceFileReaders", flags)!.GetValue(manager)!;
        resources.Add("synthetic resource", new BinaryReader(slowStream));
        studio.GetField("assetsManager")!.SetValue(null, manager);
        Task cleanup = null;
        try {
            int uiThread = Environment.CurrentManagedThreadId;
            cleanup = (Task)mainType.GetMethod("ReleaseLoadedAssetsAsync",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
            Require(entered.Wait(TimeSpan.FromSeconds(5)), "asset cleanup starts while UI caller remains free");
            Require(!cleanup.IsCompleted && slowStream.CloseThread != uiThread, "slow stream disposal runs off the UI thread");
            release.Set(); Require(cleanup.Wait(TimeSpan.FromSeconds(5)), "cleanup completes before the next load");
            Require(manager.ResourceFileCount == 0 && manager.assetsFileList.Count == 0, "previous resources are released");
        } finally {
            release.Set(); cleanup?.Wait(TimeSpan.FromSeconds(5));
            studio.GetField("assetsManager")!.SetValue(null, previousManager);
        }
        var settingsType=assembly.GetType("AnimeStudio.GUI.Properties.Settings",true)!;
        var materialDefault=settingsType.GetProperty("exportMaterials")!.GetCustomAttribute<System.Configuration.DefaultSettingValueAttribute>()!.Value;
        Require(materialDefault == "True", "material export defaults to enabled");
        return 0;
    }
    static void Require(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS " + text); }
    sealed class BlockingCloseStream(ManualResetEventSlim entered, ManualResetEventSlim release) : MemoryStream
    {
        public int CloseThread;
        protected override void Dispose(bool disposing)
        {
            if(disposing) { CloseThread=Environment.CurrentManagedThreadId; entered.Set(); if(!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("cleanup blocked the test caller"); }
            base.Dispose(disposing);
        }
    }
}
