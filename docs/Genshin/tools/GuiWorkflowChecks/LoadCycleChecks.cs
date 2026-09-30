using AnimeStudio;
using AnimeStudio.GUI;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using Newtonsoft.Json;

internal static class LoadCycleChecks
{
    // Exercise real WinForms load/reset paths with a message pump and hidden controls.
    public static int Run(string[] args)
    {
        if (args.Length < 5) throw new ArgumentException("--load-cycle <map> <new report> <name@pathID> <name@pathID> ...");
        if (File.Exists(args[2])) throw new IOException("Choose a new report file");
        ResourceMap.FromFile(args[1]);
        var selections = args.Skip(3).Select(selector => {
            var parts = selector.Split('@');
            return ResourceMap.GetEntries().Single(e => e.Type == ClassIDType.Animator && e.Name == parts[0] && e.PathID == long.Parse(parts[1]));
        }).ToArray();
        var assembly = typeof(AssetItem).Assembly;
        var mainType = assembly.GetType("AnimeStudio.GUI.MainForm", true)!;
        var studio = assembly.GetType("AnimeStudio.GUI.Studio", true)!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = (Form)Activator.CreateInstance(mainType)!;
        _ = form.Handle;
        var tree = (TreeView)mainType.GetField("sceneTreeView",flags)!.GetValue(form)!;
        _ = tree.Handle;
        _ = ((ListView)mainType.GetField("classesListView",flags)!.GetValue(form)!).Handle;
        var logger = mainType.GetField("logger",flags)!.GetValue(form)!;
        logger.GetType().GetField("WriteConsole")!.SetValue(logger, true);
        logger.GetType().GetField("ShowErrorMessage")!.SetValue(logger, false);
        Logger.Flags = LoggerEvent.Info | LoggerEvent.Warning | LoggerEvent.Error;
        var manager = (AssetsManager)studio.GetField("assetsManager")!.GetValue(null)!;
        var results = new List<object>(); string error = null;
        var clock = Stopwatch.StartNew(); double last = 0, maxGap = 0; int ticks = 0;
        using var timer = new System.Windows.Forms.Timer { Interval = 25 };
        void Sample() { double now=clock.Elapsed.TotalMilliseconds; maxGap=Math.Max(maxGap,now-last); last=now; }
        timer.Tick += (_,_) => { Sample(); ticks++; };
        using var context = new ApplicationContext();
        bool started = false;
        EventHandler start = null;
        start = async (_,_) => {
            if (started) return; started = true; Application.Idle -= start;
            try {
                foreach(var entry in selections) {
                    await Task.Delay(100);
                    var filters = new List<AssetsManager.AssetFilterDataItem> { new() { Source=entry.Source,Offset=entry.Offset,Name=entry.Name,PathID=entry.PathID,Type=entry.Type } };
                    maxGap=0; ticks=0; last=clock.Elapsed.TotalMilliseconds; timer.Start();
                    var watch=Stopwatch.StartNew(); Task load;
                    bool resolveBefore = manager.ResolveDependencies, offsetsBefore = manager.UseSelectedGenshinOffsets;
                    var selectedLoader=mainType.GetMethod("LoadSelectedPathsAsync",flags);
                    if(selectedLoader != null) load=(Task)selectedLoader.Invoke(form,new object[] { ResourceMap.GetGameType(),filters,new[] {entry.Source} })!;
                    else {
                        mainType.GetMethod("updateGame",new[] {typeof(GameType)})!.Invoke(form,new object[] {ResourceMap.GetGameType()});
                        load=(Task)mainType.GetMethod("LoadPathsAsync",flags)!.Invoke(form,new object[] {filters,new[] {entry.Source}})!;
                    }
                    double synchronousStartSeconds=watch.Elapsed.TotalSeconds;
                    await load; Sample(); timer.Stop();
                    var visible=(List<AssetItem>)studio.GetField("visibleAssets")!.GetValue(null)!;
                    bool found=visible.Any(a=>a.m_PathID==entry.PathID && a.Type==entry.Type && a.Text==entry.Name);
                    bool optionsRestored = manager.ResolveDependencies == resolveBefore && manager.UseSelectedGenshinOffsets == offsetsBefore;
                    results.Add(new {entry.Name,entry.PathID,seconds=watch.Elapsed.TotalSeconds,synchronousStartSeconds,maxUiGapMs=maxGap,ticks,
                        loadedFiles=manager.assetsFileList.Count,objects=manager.assetsFileList.Sum(f=>f.Objects.Count),treeNodes=tree.GetNodeCount(true),visibleAssets=visible.Count,selectedFound=found,optionsRestored});
                    if(!found) throw new Exception("Selected Animator absent after load");
                    if(!optionsRestored || tree.GetNodeCount(true)==0 || visible.Count!=1) throw new Exception("Model hierarchy, replacement selection or loader option restoration failed");
                }
            } catch(Exception e) {error=e.ToString();}
            finally {
                timer.Stop(); File.WriteAllText(args[2],JsonConvert.SerializeObject(new {passed=error==null,error,results},Formatting.Indented));
                await Task.Run(manager.Clear); context.ExitThread();
            }
        };
        Application.Idle += start;
        Application.Run(context);
        Console.WriteLine(File.ReadAllText(args[2]));
        return error==null ? 0 : 1;
    }
}
