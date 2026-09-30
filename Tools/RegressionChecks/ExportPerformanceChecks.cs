using AnimeStudio;

internal static class ExportPerformanceChecks
{
    public static void Run(Action<bool,string> check)
    {
        const long GiB = 1024L * 1024 * 1024;
        check(GenshinExportWorkers.Select(32, 128 * GiB) == 16, "export workers use half the logical processors with a sixteen-worker cap");
        check(GenshinExportWorkers.Select(32, 4 * GiB) == 2, "worker count respects memory headroom");
        check(GenshinExportWorkers.Select(1, 0) == 1, "single-core and unknown-memory fallback always has one worker");
        check(GenshinExportWorkers.Select(32, 128 * GiB, 4) == 4 && GenshinExportWorkers.Select(32, 4 * GiB, 100) == 2,
            "worker override can lower concurrency but cannot exceed resource bounds");
        check(new AssetsManager().ObjectReadWorkers == 1, "parallel object parsing is opt-in");

        var flags = Logger.Flags; var silent = Logger.Silent; var logger = Logger.Default;
        var capture = new Capture(); int evaluated = 0;
        int Value() { evaluated++; return 42; }
        try
        {
            Logger.Default = capture; Logger.Silent = false; Logger.Flags = LoggerEvent.Info;
            Logger.Verbose($"disabled {Value():X4}");
            check(evaluated == 0 && capture.Messages.Count == 0, "disabled verbose logging skips argument evaluation");
            Logger.Flags = LoggerEvent.Verbose;
            Logger.Verbose($"enabled {Value(),6:X4}");
            check(evaluated == 1 && capture.Messages.Single() == "[ExportPerformanceChecks] enabled   002A", "verbose formatting and caller prefix are preserved");
            Logger.Silent = true;
            Logger.Verbose($"silent {Value()}");
            check(evaluated == 1 && capture.Messages.Count == 1, "silent logging skips interpolation even when verbose is enabled");
        }
        finally { Logger.Flags = flags; Logger.Silent = silent; Logger.Default = logger; }

        byte[] bytes = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
        using var stream = new YieldingStream(bytes);
        using var reader = new BinaryReader(stream);
        int errors = 0;
        Parallel.For(0, 2000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            int offset = i % 32 * 64;
            var resource = new ResourceReader(reader, offset, 64);
            byte[] actual;
            if (i % 2 == 0) actual = resource.GetData();
            else { actual = new byte[64]; resource.GetData(actual); }
            if (!actual.SequenceEqual(bytes.Skip(offset).Take(64))) Interlocked.Increment(ref errors);
        });
        check(errors == 0, "concurrent resource reads preserve each requested range despite forced cursor interleaving");
    }
    private sealed class Capture : ILogger
    {
        public readonly List<string> Messages = new();
        public void Log(LoggerEvent level, string message) => Messages.Add(message);
    }
    private sealed class YieldingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override long Position { get => base.Position; set { base.Position = value; Thread.Yield(); } }
        public override int Read(byte[] buffer, int offset, int count) { Thread.Yield(); return base.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { Thread.Yield(); return base.Read(buffer); }
    }
}
