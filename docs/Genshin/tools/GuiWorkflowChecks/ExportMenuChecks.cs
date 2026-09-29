using AnimeStudio;
using AnimeStudio.GUI;
using Newtonsoft.Json;
using System.Reflection;
using System.Windows.Forms;

internal static class ExportMenuChecks
{
    public static int Run(string[] args)
    {
        if(args.Length!=4) throw new ArgumentException("--export-menu <map> <new report> <character@pathID>");
        if(File.Exists(args[2])) throw new IOException("Choose a new report file");
        ResourceMap.FromFile(args[1]);
        var selector=args[3].Split('@');
        var entry=ResourceMap.GetEntries().Single(e=>e.Type==ClassIDType.Animator && e.Name==selector[0] && e.PathID==long.Parse(selector[1]));
        var assembly=typeof(AssetItem).Assembly;
        var mainType=assembly.GetType("AnimeStudio.GUI.MainForm",true)!;
        var browserType=assembly.GetType("AnimeStudio.GUI.AssetBrowser",true)!;
        var studio=assembly.GetType("AnimeStudio.GUI.Studio",true)!;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var main=(Form)Activator.CreateInstance(mainType)!;
        _=main.Handle;
        foreach(string field in new[]{"assetListView","classesListView","sceneTreeView"}) _=((Control)mainType.GetField(field,flags)!.GetValue(main)!).Handle;
        var logger=mainType.GetField("logger",flags)!.GetValue(main)!;
        logger.GetType().GetField("WriteConsole")!.SetValue(logger,true);
        logger.GetType().GetField("ShowErrorMessage")!.SetValue(logger,false);
        var manager=(AssetsManager)studio.GetField("assetsManager")!.GetValue(null)!;
        var checks=new List<string>(); string error=null;
        void Check(bool ok,string message) {if(!ok) throw new Exception(message); checks.Add(message);}
        object Field(string name)=>mainType.GetField(name,flags)!.GetValue(main)!;
        object Call(string name,params object[] values)=>mainType.GetMethod(name,flags)!.Invoke(main,values)!;
        void Refresh()=>Call("UpdateCharacterExportAvailability");
        using var context=new ApplicationContext(); bool started=false;
        EventHandler start=null;
        start=async (_,_)=> {
            if(started)return; started=true; Application.Idle-=start;
            try {
                studio.GetField("Game")!.SetValue(null,GameManager.GetGameByType(GameType.Normal));
                Call("UpdateGameExportMenu");
                await (Task)Call("OnGenshinAssetMapLoaded",args[1],ResourceMap.GetEntries());
                var references=Field("characterReferences") as Task<GenshinCharacterReferences>;
                Check(references?.IsCompletedSuccessfully==true,"GI map prepares character references while the app starts in Normal");
                using var browser=(Form)Activator.CreateInstance(browserType,main)!;
                mainType.GetField("assetBrowser",flags)!.SetValue(main,browser);
                ((List<AssetEntry>)browserType.GetField("_assetEntries",flags)!.GetValue(browser)!).Add(entry);
                var grid=(DataGridView)browserType.GetField("assetDataGridView",flags)!.GetValue(browser)!;
                _=grid.Handle; grid.ColumnCount=1; grid.RowCount=1; grid.Rows[0].Selected=true;
                var filters=new List<AssetsManager.AssetFilterDataItem>{new(){Name=entry.Name,Type=entry.Type,PathID=entry.PathID,Source=entry.Source,Offset=entry.Offset}};
                await (Task)Call("LoadSelectedPathsAsync",GameType.GI,filters,new[]{entry.Source});
                var list=(ListView)Field("assetListView"); list.SelectedIndices.Add(0);
                Refresh(); var button=(ToolStripMenuItem)Field("characterExportButton");
                Check(button.Enabled,"Export Character enables after Load Selected switches to GI");
                browserType.GetMethod("AssetBrowser_FormClosing",flags)!.Invoke(browser,new object[]{browser,new FormClosingEventArgs(CloseReason.UserClosing,false)});
                browser.Dispose();
                Refresh();
                Check(ReferenceEquals(Field("characterReferences"),references) && button.Enabled,"Closing Asset Browser preserves export for the loaded Animator");
                Check(ReferenceEquals(Call("SelectedCharacter"),entry),"Main-window selection resolves the original qualified map entry after browser close");
                mainType.GetField("characterExportBusy",flags)!.SetValue(main,true); Refresh();
                Check(!button.Enabled && button.ToolTipText.Contains("progress"),"Busy export disables the command with an explanation");
                mainType.GetField("characterExportBusy",flags)!.SetValue(main,false); Refresh();
                Check(button.Enabled,"Export command recovers when busy state ends");
                Call("ClearGenshinReferences"); Refresh();
                Check(!button.Enabled && button.ToolTipText.Contains("map"),"Explicit map clear invalidates references and explains the disabled command");
                ResourceMap.Clear();
                await (Task)Call("OnGenshinAssetMapLoaded",args[1],Array.Empty<AssetEntry>());
                Check(Field("characterReferences")==null,"A non-GI map does not prepare GI references");
            } catch(Exception e){error=e.ToString();}
            finally {
                await Task.Run(manager.Clear);
                File.WriteAllText(args[2],JsonConvert.SerializeObject(new{passed=error==null,error,checks},Formatting.Indented)); context.ExitThread();
            }
        };
        Application.Idle+=start; Application.Run(context);
        Console.WriteLine(File.ReadAllText(args[2])); return error==null?0:1;
    }
}
