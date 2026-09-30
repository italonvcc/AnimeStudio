"""Prepare an empty disposable Unity Assets folder for automatic batch-import testing."""
import argparse
import json
import shutil
from pathlib import Path


def vector(value):
    return {key.lower(): component for key, component in value.items()}


def prepare(exports, project, reports):
    assets = project / "Assets"
    packages = sorted(exports.glob("*/*.character.json"))
    if len(packages) != 2 or len(reports) != 2:
        raise ValueError("This regression checks exactly two character packages and source reports")
    destinations = [assets / p.parent.name for p in packages] + [assets / "Generic", assets / "Tests"]
    if any(p.exists() for p in destinations):
        raise ValueError("Use an empty validation project; existing characters/tests are never overwritten")
    tests = assets / "Tests"
    (tests / "Editor").mkdir(parents=True)
    shutil.copy2(Path(__file__).with_name("UnityCharacterBatchCheck.cs"), tests / "Editor")
    shutil.copy2(Path(__file__).with_name("UnityCharacterDefaultsCheck.cs"), tests / "Editor")
    (tests / "Editor/CharacterBatchTests.asmdef").write_text(json.dumps({
        "name": "CharacterBatchTests", "references": [], "includePlatforms": ["Editor"],
        "optionalUnityReferences": ["TestAssemblies"]}), encoding="utf-8")
    for package, report in zip(packages, reports):
        recipe = json.loads(package.read_text(encoding="utf-8-sig"))
        source = json.loads(report.read_text(encoding="utf-8-sig"))
        if source.get("character") != recipe["character"]:
            raise ValueError("Reports must match character packages in alphabetical folder order")
        avatar = source["m_Avatar"]
        defaults = [{"path": source["m_TOS"][str(identifier)], "position": vector(p["t"]),
                     "rotation": vector(p["q"]), "scale": vector(p["s"])}
                    for identifier, p in zip(avatar["m_AvatarSkeleton"]["m_ID"], avatar["m_DefaultPose"]["m_X"])]
        original = [{"path": p["path"], "position": vector(p["m_LocalPosition"]),
                     "rotation": vector(p["m_LocalRotation"]), "scale": vector(p["m_LocalScale"])}
                    for p in source["transforms"]]
        (tests / (recipe["character"] + ".json")).write_text(json.dumps({"defaults": defaults, "source": original}), encoding="utf-8")
        shutil.copytree(package.parent, assets / package.parent.name)
    shutil.copytree(exports / "Generic", assets / "Generic")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exports", type=Path)
    parser.add_argument("project", type=Path)
    parser.add_argument("reports", nargs=2, type=Path)
    args = parser.parse_args()
    prepare(args.exports, args.project, args.reports)
