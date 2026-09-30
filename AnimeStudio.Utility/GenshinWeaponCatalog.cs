using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AnimeStudio
{
    /// <summary>
    /// A deterministic inventory of weapon-shaped source objects. A family is
    /// initially one source-qualified root, or one mesh with no proven root.
    /// Similar names are candidate evidence, never proof of shared variants.
    /// </summary>
    public sealed class GenshinWeaponCatalog
    {
        private static readonly (string Prefix, string WeaponClass)[] Classes =
        {
            ("Equip_Sword_", "Sword"), ("Equip_Claymore_", "Claymore"),
            ("Equip_Pole_", "Polearm"), ("Equip_Bow_", "Bow"),
            ("Equip_Catalyst_", "Catalyst")
        };

        public List<GenshinWeaponFamily> Families { get; private set; } = new();
        public List<GenshinWeaponExcludedCandidate> ExcludedCandidates { get; private set; } = new();
        public int RootCount => Families.Count(f => f.Roots.Count != 0);
        public int MissingRootMeshCount => Families.Count(f => f.Roots.Count == 0);
        public int MeshCandidateCount => Families.Sum(f => f.MeshCandidates.Count);
        public int AnimationCandidateCount => Families.Sum(f => f.AnimationCandidates.Count);
        public int EffectCandidateCount => Families.Sum(f => f.EffectCandidates.Count);

        public static GenshinWeaponCatalog Build(IEnumerable<AssetEntry> entries,
            IEnumerable<AssetsHelper.GenshinWeaponSourceLink> sourceLinks = null,
            IEnumerable<AssetsHelper.GenshinWeaponSourceFile> serializedFiles = null)
        {
            ArgumentNullException.ThrowIfNull(entries);
            var source = entries.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name)
                    && !string.IsNullOrWhiteSpace(e.Source))
                .DistinctBy(Identity)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Offset).ThenBy(e => e.PathID).ThenBy(e => e.Type).ToList();
            var model = source.Select(e => (Entry: e, Class: ParseClass(e.Name)))
                .Where(x => x.Class != null).ToList();
            var roots = model.Where(x => x.Entry.Type is ClassIDType.GameObject or ClassIDType.Animator).ToList();
            var meshes = model.Where(x => x.Entry.Type == ClassIDType.Mesh).ToList();
            var animation = source.Where(e => e.Type is ClassIDType.AnimationClip
                    or ClassIDType.AnimatorController or ClassIDType.AnimatorOverrideController)
                .Where(e => e.Name.StartsWith("Ani_Equip_", StringComparison.OrdinalIgnoreCase)
                    || e.Name.StartsWith("Eff_Ani_Weapon_", StringComparison.OrdinalIgnoreCase)
                    || ParseClass(e.Name) != null).ToList();
            var effects = source.Where(e => e.Name.StartsWith("Eff_Weapon_", StringComparison.OrdinalIgnoreCase)
                    || e.Name.StartsWith("Eff_Ani_Weapon_", StringComparison.OrdinalIgnoreCase)).ToList();
            var families = new List<GenshinWeaponFamily>();
            foreach (var item in roots)
            {
                var family = NewFamily(item.Entry, item.Class, true);
                family.Roots.Add(item.Entry);
                family.MeshCandidates.AddRange(meshes.Where(x => x.Class == item.Class
                    && NameCandidate(item.Entry.Name, x.Entry.Name, item.Class)).Select(x => x.Entry));
                AddCandidates(family, animation, effects);
                families.Add(family);
            }
            // A mesh name matching a root does not prove a renderer link. Keep
            // every mesh as an explicit unverified record until a typed renderer
            // link can promote it into a rooted family.
            foreach (var item in meshes)
            {
                var family = NewFamily(item.Entry, item.Class, false);
                family.Meshes.Add(item.Entry);
                family.DiscoveryStatus = "mesh-only; root association unverified";
                AddCandidates(family, animation, effects);
                families.Add(family);
            }
            var excluded = source.Where(e => e.Name.Contains("MonEquip", StringComparison.OrdinalIgnoreCase)
                    || e.Name.StartsWith("Prop_", StringComparison.OrdinalIgnoreCase))
                .Select(e => new GenshinWeaponExcludedCandidate { Entry = e,
                    Reason = e.Name.Contains("MonEquip", StringComparison.OrdinalIgnoreCase)
                        ? "monster equipment naming; no equippable link" : "prop naming; no equippable link" })
                .ToList();
            families = GroupProvenTopology(families, sourceLinks, serializedFiles);
            return new GenshinWeaponCatalog
            {
                Families = families.OrderBy(f => f.WeaponClass, StringComparer.Ordinal)
                    .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.Key, StringComparer.Ordinal).ToList(),
                ExcludedCandidates = excluded
            };
        }

        private static List<GenshinWeaponFamily> GroupProvenTopology(List<GenshinWeaponFamily> families,
            IEnumerable<AssetsHelper.GenshinWeaponSourceLink> sourceLinks,
            IEnumerable<AssetsHelper.GenshinWeaponSourceFile> serializedFiles)
        {
            if (sourceLinks == null || serializedFiles == null) return families;
            var files = serializedFiles.Where(f => f != null && !string.IsNullOrEmpty(f.SerializedFile))
                .GroupBy(f => f.SerializedFile, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.DistinctBy(f => (f.Source.ToUpperInvariant(), f.Offset)).ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            var meshOwners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in sourceLinks.Where(l => l != null && l.Kind is "child" or "animator" or "mesh"))
            {
                if (string.IsNullOrEmpty(link.TargetFile)) continue;
                string owner = SourceIdentity(link.Source, link.Offset, link.OwnerPathID);
                string target;
                if (link.TargetFile.Equals(link.OwnerFile, StringComparison.OrdinalIgnoreCase))
                    target = SourceIdentity(link.Source, link.Offset, link.TargetPathID);
                else if (files.TryGetValue(link.TargetFile, out var destinations) && destinations.Length == 1)
                    target = SourceIdentity(destinations[0].Source, destinations[0].Offset, link.TargetPathID);
                else continue;
                if (link.Kind == "mesh")
                {
                    if (!meshOwners.TryGetValue(target, out var renderers))
                        meshOwners.Add(target, renderers = new HashSet<string>(StringComparer.Ordinal));
                    renderers.Add(owner);
                    continue;
                }
                if (owners.TryGetValue(target, out var prior) && prior != owner) ambiguous.Add(target);
                else owners[target] = owner;
            }
            foreach (var target in ambiguous) owners.Remove(target);
            var modelRoots = families.Where(f => f.Roots.Count == 1 && f.Roots[0].Type == ClassIDType.GameObject)
                .ToDictionary(f => SourceIdentity(f.Roots[0]), f => f, StringComparer.Ordinal);
            GenshinWeaponFamily Trace(string start, out string uncertainty)
            {
                string current = start;
                GenshinWeaponFamily root = null;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                uncertainty = null;
                while (true)
                {
                    if (ambiguous.Contains(current))
                    {
                        uncertainty = "multiple source owners at " + current;
                        return null;
                    }
                    if (!visited.Add(current))
                    {
                        uncertainty = "cycle in source parent links at " + current;
                        return null;
                    }
                    if (modelRoots.TryGetValue(current, out var candidate)) root = candidate;
                    if (!owners.TryGetValue(current, out var parent)) break;
                    current = parent;
                }
                return root;
            }
            GenshinWeaponFamily EnclosingRoot(AssetEntry entry, out string uncertainty)
            {
                string identity = SourceIdentity(entry);
                if (entry.Type != ClassIDType.Mesh || !meshOwners.TryGetValue(identity, out var renderers))
                    return Trace(identity, out uncertainty);
                GenshinWeaponFamily common = null;
                foreach (var rendererOwner in renderers.OrderBy(x => x, StringComparer.Ordinal))
                {
                    var root = Trace(rendererOwner, out var ownerUncertainty);
                    if (ownerUncertainty != null)
                    {
                        uncertainty = "mesh renderer owner has " + ownerUncertainty;
                        return null;
                    }
                    if (root == null)
                    {
                        uncertainty = "mesh renderer owner has no indexed enclosing weapon root";
                        return null;
                    }
                    if (common != null && common != root)
                    {
                        uncertainty = "mesh is referenced by distinct enclosing weapon roots";
                        return null;
                    }
                    common = root;
                }
                uncertainty = null;
                return common;
            }
            var grouped = new Dictionary<string, GenshinWeaponFamily>(StringComparer.Ordinal);
            foreach (var family in families)
            {
                var entry = family.Roots.FirstOrDefault() ?? family.Meshes.FirstOrDefault();
                string uncertainty = null;
                var enclosing = entry == null ? null : EnclosingRoot(entry, out uncertainty);
                if (entry != null && uncertainty != null)
                {
                    family.GroupingEvidence.Add("unresolved source topology: " + uncertainty);
                    family.DiscoveryStatus = "source ancestry ambiguous; no automatic family merge";
                }
                if (enclosing == null || enclosing == family)
                {
                    grouped[family.Key] = family;
                    family.SelectorAliases.Add(family.Key);
                    continue;
                }
                enclosing.SelectorAliases.Add(family.Key);
                if (entry.Type == ClassIDType.Mesh)
                    enclosing.Meshes.Add(entry);
                enclosing.MeshCandidates.RemoveAll(e => SourceIdentity(e) == SourceIdentity(entry));
                enclosing.AnimationCandidates.AddRange(family.AnimationCandidates);
                enclosing.EffectCandidates.AddRange(family.EffectCandidates);
                enclosing.GroupingEvidence.Add($"source topology: {entry.Type} {SourceIdentity(entry)} belongs to {SourceIdentity(enclosing.Roots[0])}");
                enclosing.DiscoveryStatus = "source-linked model hierarchy; equippable identity and variants unverified";
            }
            // Root families may be processed before or after children. Return only
            // distinct enclosing families and ungrouped records, regardless of order.
            return grouped.Values.Where(f =>
            {
                var entry = f.Roots.FirstOrDefault() ?? f.Meshes.FirstOrDefault();
                if (entry == null) return true;
                var enclosing = EnclosingRoot(entry, out _);
                return enclosing == null || enclosing == f;
            }).Select(f =>
            {
                f.SelectorAliases = f.SelectorAliases.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
                f.Meshes = f.Meshes.DistinctBy(Identity).ToList();
                f.AnimationCandidates = f.AnimationCandidates.DistinctBy(Identity).ToList();
                f.EffectCandidates = f.EffectCandidates.DistinctBy(Identity).ToList();
                return f;
            }).ToList();
        }

        private static string SourceIdentity(AssetEntry entry) => SourceIdentity(entry.Source, entry.Offset, entry.PathID);
        private static string SourceIdentity(string source, long offset, long pathID) =>
            source.Replace('\\', '/').ToUpperInvariant() + "|" + offset + "|" + pathID;

        private static GenshinWeaponFamily NewFamily(AssetEntry entry, string weaponClass, bool hasRoot)
        {
            string prefix = Classes.First(x => x.WeaponClass == weaponClass).Prefix;
            string name = entry.Name.Substring(prefix.Length);
            string token = name.EndsWith("_Model", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - "_Model".Length) : name;
            return new GenshinWeaponFamily
            {
                Key = SafePathSegment(weaponClass + "-" + token) + "__" + IdentityHash(entry),
                Name = name, SourceName = entry.Name, SourceToken = token,
                WeaponClass = weaponClass,
                DiscoveryStatus = hasRoot ? "source-named root; equippable identity unverified"
                    : "mesh-only; root association unverified"
            };
        }

        private static void AddCandidates(GenshinWeaponFamily family, List<AssetEntry> animation, List<AssetEntry> effects)
        {
            family.AnimationCandidates.AddRange(animation.Where(e => CandidateToken(e.Name, family.SourceToken)));
            family.EffectCandidates.AddRange(effects.Where(e => CandidateToken(e.Name, family.SourceToken)));
        }

        private static string ParseClass(string name)
        {
            if (name.Contains("MonEquip", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var item in Classes)
                if (name.StartsWith(item.Prefix, StringComparison.OrdinalIgnoreCase)
                    && name.Length > item.Prefix.Length) return item.WeaponClass;
            return null;
        }

        private static bool NameCandidate(string root, string mesh, string weaponClass)
        {
            string prefix = Classes.First(x => x.WeaponClass == weaponClass).Prefix;
            string rootToken = root.Substring(prefix.Length);
            if (rootToken.EndsWith("_Model", StringComparison.OrdinalIgnoreCase))
                rootToken = rootToken.Substring(0, rootToken.Length - 6);
            string meshToken = mesh.Substring(prefix.Length);
            if (meshToken.EndsWith("_Model", StringComparison.OrdinalIgnoreCase))
                meshToken = meshToken.Substring(0, meshToken.Length - 6);
            return rootToken.Equals(meshToken, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CandidateToken(string name, string token)
        {
            if (token.Length == 0) return false;
            for (int start = 0; (start = name.IndexOf(token, start, StringComparison.OrdinalIgnoreCase)) >= 0; start++)
            {
                bool before = start == 0 || !char.IsLetterOrDigit(name[start - 1]);
                int end = start + token.Length;
                bool after = end == name.Length || !char.IsLetterOrDigit(name[end])
                    || (char.IsUpper(name[end]) && char.IsLower(token[token.Length - 1]));
                if (before && after) return true;
            }
            return false;
        }

        private static (string Source, long Offset, long PathID, ClassIDType Type) Identity(AssetEntry e) =>
            (e.Source.Replace('\\', '/').ToUpperInvariant(), e.Offset, e.PathID, e.Type);

        private static string IdentityHash(AssetEntry e)
        {
            var identity = Identity(e);
            string value = $"{identity.Source}|{identity.Offset}|{identity.PathID}|{(int)identity.Type}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).Substring(0, 20).ToLowerInvariant();
        }

        public static string SafePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unnamed";
            var text = new StringBuilder(value.Length);
            foreach (char c in value)
                text.Append(char.IsLetterOrDigit(c) && c <= 127 || c is '-' or '_' ? c : '_');
            var segment = text.ToString().Trim(' ', '.', '_');
            if (segment.Length == 0) segment = "unnamed";
            if (segment.Length > 72) segment = segment.Substring(0, 72).TrimEnd('_');
            string baseName = segment.Split('.')[0];
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }
                .Contains(baseName, StringComparer.OrdinalIgnoreCase)) segment = "_" + segment;
            return segment;
        }
    }

    public sealed class GenshinWeaponFamily
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string SourceName { get; set; }
        public string SourceToken { get; set; }
        public string WeaponClass { get; set; }
        public string DiscoveryStatus { get; set; }
        public List<string> SelectorAliases { get; set; } = new();
        public List<string> GroupingEvidence { get; set; } = new();
        public bool HasExportableRoot => Roots.Count != 0;
        public List<AssetEntry> Roots { get; set; } = new();
        // Populated only when a source link is proved. The initial name inventory
        // cannot prove a mesh belongs to a root, so root records leave this empty.
        public List<AssetEntry> Meshes { get; set; } = new();
        public List<AssetEntry> MeshCandidates { get; set; } = new();
        public List<AssetEntry> AnimationCandidates { get; set; } = new();
        public List<AssetEntry> EffectCandidates { get; set; } = new();
    }

    public sealed class GenshinWeaponExcludedCandidate
    {
        public AssetEntry Entry { get; set; }
        public string Reason { get; set; }
    }
}
