using System.Runtime.CompilerServices;

namespace AnimeStudio
{
    // Avoid constructing type-tree dumps and per-key diagnostic strings when
    // verbose logging is disabled. The compiler also skips format arguments.
    [InterpolatedStringHandler]
    public ref struct VerboseLogInterpolatedStringHandler
    {
        private DefaultInterpolatedStringHandler builder;
        public bool Enabled { get; }
        public VerboseLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool shouldAppend)
        {
            Enabled = shouldAppend = !Logger.Silent && Logger.Flags.HasFlag(LoggerEvent.Verbose);
            builder = Enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
        }
        public void AppendLiteral(string value) => builder.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => builder.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string format) => builder.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment) => builder.AppendFormatted(value, alignment);
        public void AppendFormatted<T>(T value, int alignment, string format) => builder.AppendFormatted(value, alignment, format);
        public string GetFormattedText() => Enabled ? builder.ToStringAndClear() : string.Empty;
    }
}
