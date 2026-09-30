using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    // The source bundle has no type tree. This decoder uses the pinned vanilla
    // 2017.4.30f1 release layout with native-transfer-proven game additions.
    // docs/Genshin/Mona-Particle-Extension-Evidence.md records the executable
    // file offsets joining field names, target memory fields and binary reads.
    // Unknown Collision/Trail data still stays explicit; parsing is not playback.
    internal static class GenshinParticleSystemDecoder
    {
        public const string TypeHash = "A854C19E25E0A7F85417CC1807AA87CA";
        private static readonly Lazy<JObject> Schema = new(() =>
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "AnimeStudio.GenshinParticleSystem2017Schema.json")
                ?? throw new InvalidDataException("Missing pinned particle schema.");
            using var reader = new StreamReader(stream);
            return AddNativeFields(JObject.Parse(reader.ReadToEnd()));
        });

        private static JObject AddNativeFields(JObject schema)
        {
            var roots = (JArray)schema["children"];
            JObject Field(string name, string type, int size, bool align = false) => new()
            {
                ["name"] = name, ["type"] = type, ["size"] = size,
                ["flags"] = align ? 16384 : 0, ["children"] = new JArray()
            };
            JObject Copy(JToken source, string name)
            {
                var copy = (JObject)source.DeepClone(); copy["name"] = name; return copy;
            }
            JToken Root(string name) => roots.First(n => (string)n["name"] == name);
            void Before(string name, params JObject[] fields)
            {
                int index = roots.ToList().FindIndex(n => (string)n["name"] == name);
                foreach (var field in fields) roots.Insert(index++, field);
            }
            // Native header: useRigidbodyForVelocity aligns first, then a separate
            // useOptPrewarm bool aligns again. A true value is not a 32-bit enum.
            Before("startDelay", Field("useOptPrewarm", "bool", 1, true));
            Before("InitialModule", Field("useCullingUpdate", "bool", 1, true),
                Field("cullingUpdateDistance", "float", 4));
            var shape = (JArray)Root("ShapeModule")["children"];
            int alignIndex = shape.ToList().FindIndex(n => (string)n["name"] == "alignToDirection");
            // Four packed booleans occupy 1016..1019. The vanilla layout treated
            // byte 1018 as alignment-to-direction and skipped the real byte1019.
            shape.Insert(alignIndex, Field("m_UseMeshScale", "bool", 1));
            shape.Add(Copy(shape.First(n => (string)n["name"] == "m_Scale"), "m_AABBSlice"));
            shape.Add(Copy(shape.First(n => (string)n["name"] == "radius"), "m_MeshSpawn"));
            var emission = (JArray)Root("EmissionModule")["children"];
            emission.Add(Field("m_PlacingOnGround", "bool", 1));
            emission.Add(Field("m_KeepVisible", "bool", 1, true));
            emission.Add(Field("m_DetectLen", "float", 4));
            emission.Add(Field("m_YDelta", "float", 4));
            emission.Add(Field("m_LayerMask", "UInt64", 8));
            var level = Field("m_EmissionLevel", "Vector4f", 16);
            level["children"] = new JArray(new[] { "x", "y", "z", "w" }.Select(n => Field(n, "float", 4)));
            emission.Add(level);
            emission.Add(Field("m_EmissionFalloffStart", "float", 4));
            emission.Add(Field("m_EmissionFalloffEnd", "float", 4));
            emission.Add(Field("m_EnableFallOff", "bool", 1, true));
            // Preserve the original native spelling, including 'Postion'.
            emission.Add(Field("m_EnableSpecifyPostion", "bool", 1, true));
            var text = Field("TextModule", "TextModule", -1);
            var textFields = new JArray { Field("enabled", "bool", 1, true) };
            foreach (string name in new[] { "sceneCamera", "canvas", "font" })
                textFields.Add(Copy(Root("m_GameObject"), name));
            textFields.Add(Field("fontSize", "int", 4));
            textFields.Add(Field("fontStyle", "int", 4));
            textFields.Add(Field("outlineEnable", "bool", 1, true));
            var distance = Field("outlineDistance", "Vector2f", 8);
            distance["children"] = new JArray(Field("x", "float", 4), Field("y", "float", 4));
            textFields.Add(distance);
            textFields.Add(Field("emitWithWorldPosition", "bool", 1, true));
            text["children"] = textFields;
            roots.Add(text);
            roots.Add(Copy(Root("ColorModule"), "ColorOverDayModule"));
            return schema;
        }

        public static JObject Decode(byte[] bytes) => Parse(bytes, strictCheckedLayout: true);

        // Research only: dynamic keyframe arrays make related source particles
        // different lengths. This keeps the same field/extension hypothesis but
        // omits the Mona absolute checkpoints. Never export its result as verified
        // native data without a separate source-layout proof for that component.
        internal static JObject ProbeRelatedParticle(byte[] bytes) => Parse(bytes, strictCheckedLayout: false);

        private static JObject Parse(byte[] bytes, bool strictCheckedLayout)
        {
            // The five Idle source components and two InFloor controls all use
            // this type hash. Their three sizes are explained by one 56-byte
            // burst entry and twelve 16-byte rotation keyframes. Check every
            // downstream module boundary against that bounded source profile.
            int missingBurstBytes = bytes.Length is 7868 or 8060 ? 56 : 0;
            int missingRotationBytes = bytes.Length == 7868 ? 192 : 0;
            if (strictCheckedLayout && bytes.Length is not (7868 or 8060 or 8116))
                throw new InvalidDataException("Unknown Genshin particle byte length.");
            int Adjust(int baseline) => baseline
                - (baseline >= 1368 ? missingBurstBytes : 0)
                - (baseline >= 1988 ? missingRotationBytes : 0);
            var parser = new Parser(bytes);
            var nodes = (JArray)Schema.Value["children"];
            var result = new JObject();
            var byteRanges = new JObject();
            foreach (var schemaNode in nodes)
            {
                JToken node = schemaNode;
                string name = (string)node["name"];
                switch (name)
                {
                    case "CustomDataModule": SkipExtension(6180, 6560, "Genshin Trail extension"); break;
                }
                void SkipExtension(int start, int end, string label)
                {
                    if (strictCheckedLayout) parser.Skip(Adjust(start), Adjust(end), label, result);
                    else parser.SkipCurrent(end - start, label, result);
                }
                int expectedStart = name switch
                {
                    "InitialModule" => 112, "ShapeModule" => 896, "EmissionModule" => 1212,
                    "SizeModule" => 1420, "RotationModule" => 1656, "ColorModule" => 1988,
                    "UVModule" => 2364, "VelocityModule" => 2544,
                    "InheritVelocityModule" => 3036, "ForceModule" => 3088,
                    "ExternalForcesModule" => 3228, "ClampVelocityModule" => 3236,
                    "NoiseModule" => 3468, "SizeBySpeedModule" => 4040,
                    "RotationBySpeedModule" => 4284, "ColorBySpeedModule" => 4432,
                    "CollisionModule" => 4816, "TriggerModule" => 5076,
                    "SubModule" => 5172, "LightsModule" => 5200,
                    "TrailModule" => 5316, "CustomDataModule" => 6560,
                    "TextModule" => 7676, "ColorOverDayModule" => 7740,
                    _ => -1
                };
                if (strictCheckedLayout && expectedStart >= 0) parser.Check(Adjust(expectedStart), name + " start");
                int fieldStart = parser.Position;
                if (name == "VelocityModule")
                {
                    // Genshin uses the later orbital/offset/radial velocity fields,
                    // while retaining this 2017 layout's 16-byte keyframes.
                    var clone = (JObject)node.DeepClone();
                    var children = (JArray)clone["children"];
                    var curve = children.First(x => (string)x["name"] == "x");
                    int insertion = children.ToList().FindIndex(x => (string)x["name"] == "speedModifier");
                    foreach (string field in new[] { "orbitalX", "orbitalY", "orbitalZ",
                        "orbitalOffsetX", "orbitalOffsetY", "orbitalOffsetZ", "radial" })
                    {
                        var extra = (JObject)curve.DeepClone();
                        extra["name"] = field;
                        children.Insert(insertion++, extra);
                    }
                    node = clone;
                }
                if (name == "CollisionModule")
                {
                    var clone = (JObject)node.DeepClone();
                    var children = (JArray)clone["children"];
                    int insertion = children.ToList().FindIndex(x => (string)x["name"] == "maxCollisionShapes");
                    children.Insert(insertion, new JObject
                    {
                        ["name"] = "giUnknownCollisionWord", ["type"] = "int",
                        ["size"] = 4, ["flags"] = 0, ["children"] = new JArray()
                    });
                    node = clone;
                }
                var parsed = parser.Read(node, name);
                result[name] = parsed;
                byteRanges[name] = new JArray(fieldStart, parser.Position);
                if (strictCheckedLayout && name == "EmissionModule" &&
                    (int)parsed["m_BurstCount"] != (missingBurstBytes == 0 ? 1 : 0))
                    throw new InvalidDataException("Emission burst count disagrees with checked byte profile.");
                if (strictCheckedLayout && name == "RotationModule" &&
                    parser.Position - fieldStart != 332 - missingRotationBytes)
                    throw new InvalidDataException("Rotation curve bytes disagree with checked byte profile.");
                int expectedEnd = name switch
                {
                    "InitialModule" => 896, "ShapeModule" => 1212,
                    "EmissionModule" => 1420, "SizeModule" => 1656,
                    "RotationModule" => 1988, "ColorModule" => 2364,
                    "UVModule" => 2544, "VelocityModule" => 3036,
                    "InheritVelocityModule" => 3088, "ForceModule" => 3228,
                    "ExternalForcesModule" => 3236, "ClampVelocityModule" => 3468,
                    "NoiseModule" => 4040, "SizeBySpeedModule" => 4284,
                    "RotationBySpeedModule" => 4432, "ColorBySpeedModule" => 4816,
                    "CollisionModule" => 5076,
                    "TriggerModule" => 5172, "SubModule" => 5200,
                    "LightsModule" => 5316, "TrailModule" => 6180,
                    "CustomDataModule" => 7676, "TextModule" => 7740,
                    "ColorOverDayModule" => 8116,
                    _ => -1
                };
                if (strictCheckedLayout && expectedEnd >= 0) parser.Check(Adjust(expectedEnd), name + " end");
            }
            parser.Check(bytes.Length, "particle end");
            result["sourceByteRanges"] = byteRanges;
            return result;
        }

        private sealed class Parser
        {
            private readonly byte[] data;
            private int cursor;
            public int Position => cursor;
            public Parser(byte[] data) => this.data = data;
            public void Check(int offset, string label)
            {
                if (cursor != offset) throw new InvalidDataException($"{label}: byte {cursor}, expected {offset}.");
            }
            public void Skip(int start, int end, string label, JObject target)
            {
                Check(start, label);
                target[label.Replace(' ', '_')] = new JObject
                {
                    ["offset"] = start, ["byteCount"] = end - start,
                    ["base64"] = Convert.ToBase64String(data.AsSpan(start, end - start))
                };
                cursor = end;
            }
            public void SkipCurrent(int count, string label, JObject target)
            {
                Require(count, label);
                Skip(cursor, cursor + count, label, target);
            }
            public JToken Read(JToken node, string path)
            {
                string type = (string)node["type"];
                JArray children = (JArray)node["children"];
                JToken value;
                if (type == "Array")
                {
                    Require(4, path);
                    int count = BitConverter.ToInt32(data, cursor);
                    cursor += 4;
                    if (count < 0 || count > 1000 || count > data.Length - cursor)
                        throw new InvalidDataException($"{path}: invalid array count {count}.");
                    var array = new JArray();
                    for (int i = 0; i < count; i++) array.Add(Read(children[1], path + "[" + i + "]"));
                    value = array;
                }
                else if (children.Count > 0)
                {
                    var obj = new JObject();
                    foreach (var child in children)
                    {
                        string name = (string)child["name"];
                        obj[name] = Read(child, path + "." + name);
                    }
                    value = obj;
                }
                else
                {
                    int size = (int)node["size"];
                    Require(size, path);
                    value = type switch
                    {
                        "float" => new JValue(BitConverter.ToSingle(data, cursor)),
                        "double" => new JValue(BitConverter.ToDouble(data, cursor)),
                        "bool" => ReadBool(path),
                        "UInt8" or "unsigned char" => new JValue(data[cursor]),
                        "SInt8" or "char" => new JValue((sbyte)data[cursor]),
                        "UInt16" or "unsigned short" => new JValue(BitConverter.ToUInt16(data, cursor)),
                        "SInt16" or "short" => new JValue(BitConverter.ToInt16(data, cursor)),
                        "UInt32" or "unsigned int" => new JValue(BitConverter.ToUInt32(data, cursor)),
                        "SInt64" or "long long" => new JValue(BitConverter.ToInt64(data, cursor).ToString()),
                        "UInt64" or "unsigned long long" => new JValue(BitConverter.ToUInt64(data, cursor).ToString()),
                        "int" or "SInt32" => new JValue(BitConverter.ToInt32(data, cursor)),
                        _ => throw new InvalidDataException($"{path}: unknown primitive {type}/{size}.")
                    };
                    if (path.EndsWith(".minMaxState"))
                    {
                        bool gradient = path.Contains("startColor") || path.Contains(".gradient.") ||
                            path.Contains(".color0.") || path.Contains(".color1.");
                        if (value.Value<int>() > (gradient ? 4 : 3))
                            throw new InvalidDataException($"{path}: invalid curve/gradient state.");
                    }
                    if (path.EndsWith(".m_PreInfinity") || path.EndsWith(".m_PostInfinity"))
                        if (value.Value<int>() is < 0 or > 8) throw new InvalidDataException($"{path}: invalid wrap mode.");
                    if (path.EndsWith(".m_RotationOrder") && value.Value<int>() is < 0 or > 5)
                        throw new InvalidDataException($"{path}: invalid rotation order.");
                    cursor += size;
                }
                if (((int)node["flags"] & 16384) != 0) cursor = (cursor + 3) & ~3;
                if (cursor > data.Length) throw new InvalidDataException($"{path}: exceeds source length.");
                return value;
            }
            private JToken ReadBool(string path)
            {
                if (data[cursor] > 1) throw new InvalidDataException($"{path}: invalid bool {data[cursor]}.");
                return new JValue(data[cursor] != 0);
            }
            private void Require(int size, string path)
            {
                if (size < 0 || cursor > data.Length - size)
                    throw new InvalidDataException($"{path}: read outside source at {cursor}/{size}.");
            }
        }
    }
}
