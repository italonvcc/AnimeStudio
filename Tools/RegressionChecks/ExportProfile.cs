using AnimeStudio;
using System.Diagnostics;

internal sealed class ExportProfile : ILogger
{
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly TimeSpan initialCpu = Process.GetCurrentProcess().TotalProcessorTime;
    readonly long initialAllocated = GC.GetTotalAllocatedBytes();
    readonly List<object> stages = new();
    string lastStage;
    public void Stage(string name)
    {
        lock (stages) {
            if (name == lastStage) return;
            using var process = Process.GetCurrentProcess();
            stages.Add(new {name,seconds=clock.Elapsed.TotalSeconds,cpuSeconds=(process.TotalProcessorTime-initialCpu).TotalSeconds});
            lastStage=name;
        }
    }
    public void Log(LoggerEvent level,string message)
    {
        if (level == LoggerEvent.Info) {
            if (message.StartsWith("Loading ")) Stage("Read bundles");
            else if(message.StartsWith("Read assets")) Stage("Parse objects");
            else if(message.StartsWith("Process Assets")) Stage("Link objects");
            else if(message.StartsWith("Resolving dependencies")) Stage("Resolve dependencies");
            else if(message.StartsWith("Exporting animation")) Stage("Write animations");
            else if(message.StartsWith("Exporting VFX objects")) Stage("Write VFX objects");
        }
        Console.WriteLine($"[{clock.Elapsed.TotalSeconds:F3}][{level}] {message}");
    }
    public object Finish()
    {
        Stage("Finished");
        using var process=Process.GetCurrentProcess();
        return new {seconds=clock.Elapsed.TotalSeconds,cpuSeconds=(process.TotalProcessorTime-initialCpu).TotalSeconds,
            peakWorkingSetBytes=process.PeakWorkingSet64,allocatedBytes=GC.GetTotalAllocatedBytes()-initialAllocated,
            logicalProcessors=Environment.ProcessorCount, serverGC=System.Runtime.GCSettings.IsServerGC,
            collections=Enumerable.Range(0,GC.MaxGeneration+1).Select(GC.CollectionCount).ToArray(), stages};
    }
}
