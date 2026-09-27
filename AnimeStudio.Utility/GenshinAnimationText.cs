using System.Text.RegularExpressions;

namespace AnimeStudio
{
    public static class GenshinAnimationText
    {
        // Change whitespace/layout only. Keep every numeric token and every key.
        // The guarded pattern covers unweighted Unity 2017 keys used by GI;
        // newer weighted key layouts are deliberately left alone.
        static readonly Regex Key = new Regex(@"(?m)^([ ]*)- serializedVersion: ([0-9]+)\r?\n\1  time: ([^\r\n]+)\r?\n\1  value: ([^\r\n]+)\r?\n\1  inSlope: ([^\r\n]+)\r?\n\1  outSlope: ([^\r\n]+)\r?\n(?!\1  (?:weightedMode|inWeight|outWeight):)", RegexOptions.Compiled);
        public static string Compact(string yaml) => Key.Replace(yaml, m => m.Groups[1].Value + "- {serializedVersion: " + m.Groups[2].Value +
            ", time: " + m.Groups[3].Value + ", value: " + m.Groups[4].Value + ", inSlope: " + m.Groups[5].Value + ", outSlope: " + m.Groups[6].Value + "}\n");
    }
}
