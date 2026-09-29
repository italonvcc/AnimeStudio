using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    public static class MaterialJsonExporter
    {
        public static string Serialize(Material material, JsonSerializerSettings settings)
        {
            var json = JObject.FromObject(material, JsonSerializer.Create(settings));
            var pointer = material.m_Shader;
            var resolved = pointer != null && pointer.TryGet(out _);
            Shader shader = null;
            if (resolved) pointer.TryGet(out shader);
            var external = pointer != null && pointer.m_FileID > 0 &&
                pointer.m_FileID <= material.assetsFile.m_Externals.Count
                ? material.assetsFile.m_Externals[pointer.m_FileID - 1].fileName : null;
            json["ShaderReference"] = JObject.FromObject(new
            {
                Status = pointer == null || pointer.IsNull ? "Null" : !resolved ? "Unresolved" :
                    string.IsNullOrEmpty(shader.Name) ? "Unnamed" : "Resolved",
                NameSource = shader?.NameSource ?? "Unavailable",
                SerializedFile = external ?? (pointer?.m_FileID == 0 ? material.assetsFile.fileName : null),
                PathID = pointer?.m_PathID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ParsedShader = shader?.m_ParsedForm != null
            });
            return json.ToString(Formatting.Indented);
        }
    }
}
