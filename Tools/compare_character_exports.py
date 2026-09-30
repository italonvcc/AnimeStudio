"""Compare exported payloads, recipes and metadata after exporter optimizations.

FBX embeds export timestamps and is excluded. Timing reports are also excluded;
toolRevision is the only ignored JSON field. Generated importer class IDs are
normalized. Shared animations are followed from
the character recipe, so sibling libraries need not contain identical characters.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

REPORTS = {"export-profile.json", "bounded-storage-check.json",
           "animation-export-performance.json", "export-performance.json"}


def digest(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def normalized(value):
    if isinstance(value, dict):
        return {key: normalized(item) for key, item in value.items() if key != "toolRevision"}
    if isinstance(value, list):
        return [normalized(item) for item in value]
    return value


def compare(before, after):
    def importer_name(value):
        return re.sub(r"AnimeStudioCharacterImport_[0-9a-f]{32}", "AnimeStudioCharacterImport_ID", value)
    def files(root):
        return {importer_name(p.relative_to(root).as_posix()): p for p in root.rglob("*")
                if p.is_file() and p.suffix.lower() != ".fbx" and p.name not in REPORTS}
    original, current = files(before), files(after)
    missing = sorted(original.keys() - current.keys())
    extra = sorted(current.keys() - original.keys())
    changed, identical, metadata, importers = [], 0, 0, 0
    for name in sorted(original.keys() & current.keys()):
        if digest(original[name]) == digest(current[name]):
            identical += 1
        elif original[name].suffix == ".json" and normalized(json.loads(original[name].read_text(encoding="utf-8-sig"))) == normalized(json.loads(current[name].read_text(encoding="utf-8-sig"))):
            metadata += 1
        elif name == "Editor/AnimeStudioCharacterImport_ID.cs" and importer_name(original[name].read_text()) == importer_name(current[name].read_text()):
            importers += 1
        else:
            changed.append(name)
    recipe_paths = list(before.glob("*.character.json"))
    if len(recipe_paths) != 1:
        raise ValueError("Expected exactly one character recipe")
    recipe = json.loads(recipe_paths[0].read_text(encoding="utf-8-sig"))
    shared = 0
    for clip in recipe["clips"]:
        path = clip["file"]
        if not (before / path).is_file() or not (after / path).is_file() or digest(before / path) != digest(after / path):
            changed.append("clip: " + path)
        elif path.startswith("../"):
            shared += 1
    return dict(passed=not (missing or extra or changed), identicalFiles=identical,
                revisionOnlyMetadata=metadata, equivalentImporters=importers, sourceClips=len(recipe["clips"]),
                identicalSharedClips=shared, missing=missing, extra=extra, changed=changed,
                exclusions=["timestamped FBX", "timing reports", "JSON toolRevision", "generated importer class ID"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    parser.add_argument("report", type=Path)
    args = parser.parse_args()
    result = compare(args.before, args.after)
    args.report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result["passed"] else 1)
