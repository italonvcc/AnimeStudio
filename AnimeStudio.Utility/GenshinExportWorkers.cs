using System;

namespace AnimeStudio
{
    public static class GenshinExportWorkers
    {
        public static int Count
        {
            get
            {
                int.TryParse(Environment.GetEnvironmentVariable("ANIMESTUDIO_EXPORT_WORKERS"), out int requested);
                return Select(Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, requested);
            }
        }
        // Leave headroom for loaded bundles and decoded curves. This bounds
        // concurrent serialization, not total process memory or parser loading.
        public static int Select(int processors, long memoryBytes, int requested = 0)
        {
            int memoryLimit = (int)Math.Clamp(memoryBytes / (2L * 1024 * 1024 * 1024), 1, 16);
            int limit = Math.Max(1, Math.Min(Math.Max(1, processors / 2), memoryLimit));
            return requested > 0 ? Math.Clamp(requested, 1, limit) : limit;
        }
    }
}
