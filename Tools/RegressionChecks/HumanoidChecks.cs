using AnimeStudio;
using Newtonsoft.Json.Linq;
using System.Runtime.CompilerServices;

internal static class HumanoidChecks
{
    private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static Axes Axes(Vector3 min, Vector3 max)
    {
        var axes = Empty<Axes>(); axes.m_PreQ = axes.m_PostQ = new Vector4(0, 0, 0, 1);
        axes.m_Sgn = new Vector3(1, 1, 1); axes.m_Limit = Empty<Limit>();
        axes.m_Limit.m_Min = min; axes.m_Limit.m_Max = max;
        return axes;
    }
    public static void Run(Action<bool, string> check)
    {
        var axes = Axes(new Vector3(-MathF.PI / 2, -MathF.PI / 2, -MathF.PI / 2), new Vector3(MathF.PI / 2, MathF.PI / 2, MathF.PI / 2));
        var q = HumanoidRotationMath.Solve(axes, new Vector3(0, 1, 1), 1, out var carry);
        float v = 1 / MathF.Sqrt(3);
        check(Angle(q, new Quaternion(0, v, v, v)) < .001, "combined humanoid swing uses half-angle tangents");
        q = HumanoidRotationMath.Solve(axes, new Vector3(1, 0, 0), .35f, out carry);
        var composed = HumanoidRotationMath.ApplyParentTwist(q, carry);
        check(Angle(composed, new Quaternion(MathF.Sqrt(.5f), 0, 0, MathF.Sqrt(.5f))) < .001, "distributed twist preserves total rotation");
        axes.m_Limit.m_Min = new Vector3(-MathF.PI / 6, 0, 0);
        q = HumanoidRotationMath.Solve(axes, new Vector3(-2, 0, 0), 1, out carry);
        check(Angle(q, new Quaternion(-.5f, 0, 0, MathF.Sqrt(.75f))) < .001, "asymmetric negative limits preserve muscle overshoot");
        axes.m_Sgn = new Vector4(-1, 1, 1, 0);
        q = HumanoidRotationMath.Solve(axes, new Vector3(-2, 0, 0), 1, out carry);
        check(Angle(q, new Quaternion(.5f, 0, 0, MathF.Sqrt(.75f))) < .001, "legacy four-component Avatar sign is supported");
        foreach (var muscle in new[] { float.NaN, float.PositiveInfinity })
        {
            bool rejected = false;
            try { HumanoidRotationMath.Solve(axes, new Vector3(muscle, 0, 0), 1, out _); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "nonfinite humanoid input rejected");
        }
        bool invalidTwist = false;
        try { HumanoidRotationMath.Solve(axes, new Vector3(), 1.01f, out _); }
        catch (ArgumentOutOfRangeException) { invalidTwist = true; }
        check(invalidTwist, "invalid twist weight rejected");
    }

    public static int Reference(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("--humanoid-rotations <avatar-report> <avatar-name> <rig.json> <reference-result.json>");
        var rig = JObject.Parse(File.ReadAllText(args[3]));
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(args[1])));
        if (!hash.Equals((string)rig["sourceReportSha256"]!, StringComparison.OrdinalIgnoreCase) || (string)rig["sourceAvatarName"]! != args[2])
            throw new Exception("Reference rig was prepared from a different source report or Avatar.");
        var avatar = JObject.Parse(File.ReadAllText(args[1]))["dependencyResolution"]!["avatars"]!
            .Where(a => (string)a["Name"]! == args[2]).ElementAt((int)rig["sourceAvatarIndex"]!)["m_Avatar"]!;
        var human = avatar["m_Human"]!;
        var reference = JObject.Parse(File.ReadAllText(args[4]));
        string rigHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(args[3])));
        if (!rigHash.Equals((string)reference["rigSha256"]!, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Reference poses were sampled from a different rig input.");
        if (!(bool)reference["valid"]! || !(bool)reference["human"]! || Math.Abs((double)reference["sourceScale"]! - (double)reference["rebuiltScale"]!) > 1e-5)
            throw new Exception("Reference Avatar is invalid or has a different human scale.");
        var indices = avatar["m_HumanSkeletonIndexArray"]!.Values<int>().ToArray();
        var nodes = rig["nodes"]!.ToArray();
        int checks = 0; double worst = 0;
        foreach (var clip in reference["clips"]!.Where(c => (bool)c["human"]!))
        foreach (var pose in clip["poses"]!)
        {
            var remainders = new Dictionary<string, Quaternion>();
            foreach (var bone in reference["axes"]!)
            {
                string name = (string)bone["human"]!;
                if (name == "Hips") continue; // Center-of-mass/root solve remains unimplemented.
                int avatarIndex = Array.FindIndex(nodes, n => (string)n["human"]! == name);
                int humanIndex = Array.IndexOf(indices, avatarIndex);
                int axisIndex = (int)human["m_Skeleton"]!["m_Node"]![humanIndex]!["m_AxesId"]!;
                var source = human["m_Skeleton"]!["m_AxesArray"]![axisIndex]!;
                var axes = Axes(V3(source["m_Limit"]!["m_Min"]!), V3(source["m_Limit"]!["m_Max"]!));
                axes.m_PreQ = V4(source["m_PreQ"]!); axes.m_PostQ = V4(source["m_PostQ"]!); axes.m_Sgn = V3(source["m_Sgn"]!);
                var muscleIndices = bone["muscles"]!.Values<int>().ToArray();
                var values = muscleIndices.Select(m => m < 0 ? 0 : (float)pose["muscles"]![m]!).ToArray();
                float weight = name.Contains("UpperArm") ? (float)human["m_ArmTwist"]! : name.Contains("LowerArm") ? (float)human["m_ForeArmTwist"]!
                    : name.Contains("UpperLeg") ? (float)human["m_UpperLegTwist"]! : name.Contains("LowerLeg") ? (float)human["m_LegTwist"]! : 1;
                var actual = HumanoidRotationMath.Solve(axes, new Vector3(values[0], values[1], values[2]), weight, out var carry);
                if (remainders.TryGetValue(name, out var previous)) actual = HumanoidRotationMath.ApplyParentTwist(previous, actual);
                string child = name.Replace("UpperArm", "LowerArm").Replace("UpperLeg", "LowerLeg");
                if (child == name) child = name.Replace("LowerArm", "Hand").Replace("LowerLeg", "Foot");
                if (child != name)
                {
                    int childIndex = Array.FindIndex(nodes, n => (string)n["human"]! == child);
                    if ((int)nodes[childIndex]["parent"]! != avatarIndex) throw new Exception("Reference check requires direct twist-child ancestry.");
                    remainders[child] = carry;
                }
                var expected = V4(pose["rotations"]![avatarIndex]!);
                double error = Angle(actual, new Quaternion(expected.X, expected.Y, expected.Z, expected.W));
                if (!double.IsFinite(error) || error > .01) throw new Exception($"{clip["name"]} at {pose["time"]}: {name} differs by {error:F6} degrees.");
                worst = Math.Max(worst, error); checks++;
            }
        }
        if (checks == 0) throw new Exception("No humanoid rotations checked.");
        Console.WriteLine($"PASS {checks} source-Avatar joint rotations vs Unity {reference["unityVersion"]}; maximum error {worst:F6} degrees. Hips, root motion and IK excluded.");
        return 0;
    }
    private static Vector3 V3(JToken v) => new Vector3((float)(v["X"] ?? v["x"])!, (float)(v["Y"] ?? v["y"])!, (float)(v["Z"] ?? v["z"])!);
    private static Vector4 V4(JToken v) { var xyz = V3(v); return new Vector4(xyz.X, xyz.Y, xyz.Z, (float)(v["W"] ?? v["w"])!); }
    private static double Angle(Quaternion a, Quaternion b)
    {
        double dot = (double)a.X*b.X + (double)a.Y*b.Y + (double)a.Z*b.Z + (double)a.W*b.W;
        double aa = (double)a.X*a.X + (double)a.Y*a.Y + (double)a.Z*a.Z + (double)a.W*a.W;
        double bb = (double)b.X*b.X + (double)b.Y*b.Y + (double)b.Z*b.Z + (double)b.W*b.W;
        return 2 * Math.Acos(Math.Clamp(Math.Abs(dot) / Math.Sqrt(aa * bb), 0, 1)) * 180 / Math.PI;
    }
}
