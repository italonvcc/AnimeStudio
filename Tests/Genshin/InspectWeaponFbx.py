"""Read binary FBX output independently of AnimeStudio and Unity.

Checks model geometry counts and actual skin cluster payloads against the export
manifest. This is a file-contract check, not a rendering or importer test.
"""
import argparse
import json
import math
import struct
import zlib
from pathlib import Path


def read_fbx(path):
    data = path.read_bytes()
    assert data[:23] == b"Kaydara FBX Binary  \x00\x1a\x00", "Expected binary FBX"
    version = struct.unpack_from("<I", data, 23)[0]
    wide = version >= 7500
    header = "<QQQB" if wide else "<IIIB"
    header_size = struct.calcsize(header)

    def prop(pos):
        code = chr(data[pos])
        pos += 1
        scalar = {"Y": "h", "C": "?", "I": "i", "F": "f", "D": "d", "L": "q"}
        if code in scalar:
            fmt = "<" + scalar[code]
            return struct.unpack_from(fmt, data, pos)[0], pos + struct.calcsize(fmt)
        if code in "SR":
            length = struct.unpack_from("<I", data, pos)[0]
            value = data[pos + 4:pos + 4 + length]
            return (value.decode("utf-8", errors="replace") if code == "S" else value), pos + 4 + length
        assert code in "fdlibc", f"Unsupported FBX property {code}"
        count, encoding, length = struct.unpack_from("<III", data, pos)
        raw = data[pos + 12:pos + 12 + length]
        assert encoding in (0, 1)
        if encoding:
            raw = zlib.decompress(raw)
        fmt = "<" + {"f": "f", "d": "d", "l": "q", "i": "i", "b": "B", "c": "B"}[code] * count
        assert len(raw) == struct.calcsize(fmt)
        return list(struct.unpack(fmt, raw)), pos + 12 + length

    def node(pos):
        end, count, prop_size, name_size = struct.unpack_from(header, data, pos)
        if end == 0:
            return None, pos + header_size
        assert pos < end <= len(data)
        pos += header_size
        name = data[pos:pos + name_size].decode("utf-8")
        pos += name_size
        expected_props_end = pos + prop_size
        props = []
        for _ in range(count):
            value, pos = prop(pos)
            props.append(value)
        assert pos == expected_props_end
        children = []
        while pos < end:
            child, pos = node(pos)
            if child is None:
                break
            children.append(child)
        assert pos == end
        return {"name": name, "props": props, "children": children}, end

    roots = []
    pos = 27
    while pos + header_size <= len(data):
        item, pos = node(pos)
        if item is None:
            break
        roots.append(item)
    return roots


def inspect(manifest_path):
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    roots = read_fbx(manifest_path.parent / manifest["model"])
    objects = next(n for n in roots if n["name"] == "Objects")["children"]
    geometries = [n for n in objects if n["name"] == "Geometry" and n["props"][2] == "Mesh"]
    clusters = [n for n in objects if n["name"] == "Deformer" and n["props"][2] == "Cluster"]
    counts = []
    for geometry in geometries:
        fields = {n["name"]: n["props"] for n in geometry["children"]}
        vertices, indices = fields["Vertices"][0], fields["PolygonVertexIndex"][0]
        assert len(vertices) % 3 == 0 and all(math.isfinite(x) for x in vertices)
        counts.append((len(vertices) // 3, sum(i < 0 for i in indices)))
        assert all(0 <= (i if i >= 0 else -i - 1) < len(vertices) // 3 for i in indices)
    expected = [(m["vertices"], sum(s["faces"] for s in m["submeshes"])) for m in manifest["meshes"]]
    assert sorted(counts) == sorted(expected), (counts, expected)
    influence_count = 0
    for cluster in clusters:
        fields = {n["name"]: n["props"] for n in cluster["children"]}
        indices, weights = fields.get("Indexes", [[]])[0], fields.get("Weights", [[]])[0]
        assert len(indices) == len(weights)
        assert all(i >= 0 for i in indices) and all(math.isfinite(w) and 0 <= w <= 1 for w in weights)
        for key in ("Transform", "TransformLink"):
            assert len(fields[key][0]) == 16 and all(math.isfinite(x) for x in fields[key][0])
        influence_count += len(weights)
    expected_bones = sum(m["bones"] or 0 for m in manifest["meshes"])
    assert len(clusters) == expected_bones, (len(clusters), expected_bones)
    return {"manifest": str(manifest_path), "meshes": len(geometries), "verticesAndFaces": counts,
            "skinClusters": len(clusters), "weightedInfluences": influence_count, "status": "PassedFileContract"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("manifests", nargs="+", type=Path)
    args = parser.parse_args()
    print(json.dumps([inspect(path.resolve()) for path in args.manifests], indent=2))
