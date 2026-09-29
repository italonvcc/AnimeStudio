using AnimeStudio;

internal static class Inspect
{
    public static object LastResolution { get; private set; }

    private static object InspectPrefab(GameObject root)
    {
        var nodes = new List<object>(); var seen = new HashSet<Transform>();
        void Visit(Transform transform, string parent)
        {
            if (transform == null || !seen.Add(transform) || !transform.m_GameObject.TryGet(out var go)) return;
            string path = parent + "/" + go.Name;
            nodes.Add(new { path, go.m_PathID, transform.m_LocalPosition, transform.m_LocalRotation, transform.m_LocalScale,
                parent = transform.m_Father.Name, components = go.m_Components.Select(p => new { p.m_PathID, p.Name, type = p.TryGet(out var c) ? c.type.ToString() : "Unresolved" }).ToArray(),
                mesh = go.m_SkinnedMeshRenderer?.m_Mesh.Name ?? go.m_MeshFilter?.m_Mesh.Name });
            foreach (var child in transform.m_Children) if (child.TryGet(out var next)) Visit(next, path);
        }
        Visit(root.m_Transform, "");
        return nodes;
    }

    private static object InspectRig(Animator animator)
    {
        var nodes = new List<object>();
        var seen = new HashSet<Transform>();
        void Visit(Transform transform, string parent)
        {
            if (transform == null || !seen.Add(transform) || !transform.m_GameObject.TryGet(out var gameObject)) return;
            var path = parent.Length == 0 ? gameObject.Name : parent + "/" + gameObject.Name;
            var renderer = gameObject.m_SkinnedMeshRenderer;
            nodes.Add(new { path, mesh = renderer?.m_Mesh.Name, meshResolved = renderer?.m_Mesh.TryGet(out _) ?? false,
                boneCount = renderer?.m_Bones.Count, materials = renderer?.m_Materials.Select(p => new { p.Name, p.m_FileID, PathID = p.m_PathID.ToString() }).ToArray() });
            foreach (var child in transform.m_Children)
                if (child.TryGet(out var next)) Visit(next, path);
        }
        if (animator.m_GameObject.TryGet(out var root)) Visit(root.m_Transform, "");
        return new { avatar = animator.m_Avatar.Name, avatarResolved = animator.m_Avatar.TryGet(out _),
            animator.m_HasTransformHierarchy, nodes };
    }

    public static List<object> Run(List<AssetEntry> entries, bool resolve = false)
    {
        Logger.Default = new ConsoleLogger();
        Logger.Flags = LoggerEvent.Error | LoggerEvent.Warning;
        if (entries.Count == 0) return new List<object>();
        if (entries.Count > 32 || entries.Any(e => e.Offset < 0))
            throw new ArgumentException("Inspection requires at most 32 map entries, all with known bundle offsets. Narrow the regex.");
        var manager = new AssetsManager { Game = GameManager.GetGameByType(ResourceMap.GetGameType()) };
        manager.FilterData.Items = entries.Select(e => new AssetsManager.AssetFilterDataItem
        { Source = e.Source, Offset = e.Offset, PathID = e.PathID, Name = e.Name, Type = e.Type }).ToList();
        var results = new List<object>();
        try
        {
            manager.LoadFiles(entries.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), mergeSplitAssets: false);
            if (resolve)
            {
                var roots = manager.assetsFileList.SelectMany(f => f.ObjectsDic.Values).Where(obj => entries.Any(e =>
                    e.PathID == obj.m_PathID && e.Type == obj.type && string.Equals(e.Source, obj.assetsFile.originalPath, StringComparison.OrdinalIgnoreCase))).ToArray();
                var resolved = new AssetDependencyResolver(manager, ResourceMap.GetEntries()).Resolve(roots);
                LastResolution = new { resolved.LoadedBundles, resolved.Missing,
                    counts = resolved.Objects.GroupBy(o => o.type.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                    avatars = resolved.Objects.OfType<Avatar>().Select(a => new { a.Name, PathID = a.m_PathID.ToString(),
                        serializedFile = a.assetsFile.fileName, source = a.assetsFile.originalPath, a.m_Avatar, a.m_TOS }).ToArray() };
            }
            foreach (var file in manager.assetsFileList)
            foreach (var obj in file.ObjectsDic.Values)
            {
                if (!entries.Any(e => e.PathID == obj.m_PathID && e.Type == obj.type && string.Equals(e.Source, file.originalPath, StringComparison.OrdinalIgnoreCase))) continue;
                object details = obj switch
                {
                    AnimationClip clip => new
                    {
                        clip.m_Legacy, clip.m_SampleRate,
                        compression = InspectCompression(clip),
                        decodedHumanoid = !clip.m_Legacy ? AnimationClipConverter.Process(clip).Floats.Where(c => c.classID == ClassIDType.Animator)
                            .Select(c => new { c.attribute, keys = c.curve.m_Curve.Select(k => new { k.time, value = (float)k.value }).ToArray() }).ToArray() : null,
                        bindingTypes = clip.m_ClipBindingConstant?.genericBindings.GroupBy(b => b.typeID.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                        bindings = clip.m_ClipBindingConstant?.genericBindings.Select(b => new { Type = b.typeID.ToString(), b.path, b.attribute, b.customType }).ToArray()
                    },
                    Shader shader => new { shader.m_Name, parsedName = shader.m_ParsedForm?.m_Name, shader.NameSource,
                        parsed = shader.m_ParsedForm != null, shader.byteSize, shader.version,
                        footerStrings = shader.byteSize <= 64 * 1024 * 1024 ? System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.ASCII.GetString(shader.GetRawData()), @"[\x20-\x7e]{6,}")
                            .Where(m => m.Value.StartsWith("miHoYo/") || m.Value.StartsWith("MoleMole.") || m.Value.Contains("Inspector"))
                            .Select(m => new { offset = m.Index, text = m.Value }).TakeLast(16).ToArray() : null },
                    Material mat => new
                    {
                        mat.m_Shader.m_FileID, PathID = mat.m_Shader.m_PathID.ToString(), mat.m_Shader.Name,
                        external = mat.m_Shader.m_FileID > 0 && mat.m_Shader.m_FileID <= file.m_Externals.Count ? file.m_Externals[mat.m_Shader.m_FileID - 1].fileName : null
                    },
                    Animator animator => InspectRig(animator),
                    GameObject go => InspectPrefab(go),
                    Cubemap cube => new { cube.LayoutError, typeHash = Convert.ToHexString(cube.serializedType.m_OldTypeHash), width = cube.ImageLayout?.m_Width, faces = cube.ImageLayout?.m_ImageCount },
                    MonoBehaviour mono => new { script = mono.m_Script.Name, structured = mono.ToType(),
                        strings = System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.ASCII.GetString(mono.GetRawData()), @"[\x20-\x7e]{6,}")
                            .Select(m => m.Value).Take(300).ToArray() },
                    TextAsset text => new { size = text.m_Script.Length,
                        strings = System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.UTF8.GetString(text.m_Script), @"[\x20-\x7e]{6,}")
                            .Select(m => m.Value).Take(300).ToArray() },
                    _ => new { note = "Identity/dependencies only; no specialized inspection for this type." }
                };
                results.Add(new { obj.Name, Type = obj.type.ToString(), PathID = obj.m_PathID.ToString(), source = file.originalPath,
                    serializedFile = file.fileName, externalFiles = file.m_Externals.Select(e => e.fileName).ToArray(), details });
            }
        }
        finally { manager.Clear(); }
        return results;
    }

    private static object InspectCompression(AnimationClip animation)
    {
        var clip = animation.m_MuscleClip.m_Clip;
        var acl = clip.m_ACLClip as GIACLClip;
        var headers = new List<object>();
        if (acl != null)
            for (int offset = 0; offset + 32 <= acl.m_ClipData.Length;)
            {
                var bytes = acl.m_ClipData;
                var size = BitConverter.ToUInt32(bytes, offset);
                headers.Add(new { offset, size, version = BitConverter.ToUInt16(bytes, offset + 12),
                    type = bytes[offset + 15], tracks = BitConverter.ToUInt32(bytes, offset + 16),
                    samples = BitConverter.ToUInt32(bytes, offset + 20), rate = BitConverter.ToSingle(bytes, offset + 24) });
                if (size < 32 || size > bytes.Length - offset) break;
                offset += checked((int)((size + 15) & ~15u));
            }
        return new { acl?.m_CurveCount, acl?.m_ConstCurveCount, bytes = acl?.m_ClipData.Length,
            databaseBytes = acl?.m_DatabaseData.Length, headers,
            databaseHeader = acl == null ? null : Convert.ToHexString(acl.m_DatabaseData.Take(128).ToArray()),
            streamCurves = clip.m_StreamedClip.curveCount, denseCurves = clip.m_DenseClip.m_CurveCount,
            constantCurves = clip.m_ConstantClip?.data.Length };
    }
}
