"""Prepare a local Unity animation oracle from AssetStudy avatar metadata and exported .anim files."""
import argparse, hashlib, json, shutil
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('avatar_report', type=Path)
p.add_argument('avatar_name')
p.add_argument('clip_directory', type=Path)
p.add_argument('project', type=Path)
p.add_argument('--replace-input', action='store_true')
p.add_argument('--avatar-index', type=int, help='Explicit occurrence among matching names if the report contains duplicates')
a = p.parse_args()
report_bytes = a.avatar_report.read_bytes()
matches = [x for x in json.loads(report_bytes)['dependencyResolution']['avatars'] if x['Name'] == a.avatar_name]
if not matches or (len(matches) > 1 and a.avatar_index is None):
    p.error('Avatar name is absent or ambiguous; use --avatar-index for duplicate names.')
index = 0 if a.avatar_index is None else a.avatar_index
if not 0 <= index < len(matches):
    p.error('--avatar-index is outside the matching Avatars.')
avatar = matches[index]
constant = avatar['m_Avatar']
human = constant['m_Human']
body_names = 'Hips LeftUpperLeg RightUpperLeg LeftLowerLeg RightLowerLeg LeftFoot RightFoot Spine Chest UpperChest Neck Head LeftShoulder RightShoulder LeftUpperArm RightUpperArm LeftLowerArm RightLowerArm LeftHand RightHand LeftToes RightToes LeftEye RightEye Jaw'.split()
names = {}
for name, node in zip(body_names, human['m_HumanBoneIndex']):
    if node >= 0:
        names[constant['m_HumanSkeletonIndexArray'][node]] = name
for side in ['Left', 'Right']:
    for i, node in enumerate(human['m_' + side + 'Hand']['m_HandBoneIndex']):
        if node >= 0:
            names[constant['m_HumanSkeletonIndexArray'][node]] = side + ['Thumb','Index','Middle','Ring','Little'][i//3] + ['Proximal','Intermediate','Distal'][i%3]
def vector(v): return {k.lower(): val for k, val in v.items()}
nodes = []
for i, (node, hash_id, pose) in enumerate(zip(constant['m_AvatarSkeleton']['m_Node'], constant['m_AvatarSkeleton']['m_ID'], constant['m_AvatarSkeletonPose']['m_X'])):
    path = avatar['m_TOS'][str(hash_id)]
    nodes.append(dict(name=path.split('/')[-1] or 'ReferenceRoot', parent=node['m_ParentId'], human=names.get(i, ''),
                      position=vector(pose['t']), rotation=vector(pose['q']), scale=vector(pose['s'])))
if len({n['name'] for n in nodes}) != len(nodes) or any(n['parent'] >= i for i, n in enumerate(nodes)):
    p.error('Reference builder requires unique bone names and parent-before-child node order.')
clips = list(a.clip_directory.glob('*.anim'))
if not clips:
    p.error('No exported .anim clips found.')
output = a.project / 'Assets' / 'ReferenceInput'
if output.exists() and {f.name for f in output.glob('*.anim')} - {f.name for f in clips}:
    p.error('ReferenceInput contains other clips; use a fresh scratch project for a different clip set.')
output.mkdir(parents=True, exist_ok=a.replace_input)
(output/'rig.json').write_text(json.dumps(dict(sourceAvatarName=a.avatar_name, sourceAvatarIndex=index,
    sourceReportSha256=hashlib.sha256(report_bytes).hexdigest(), nodes=nodes, armTwist=human['m_ArmTwist'], foreArmTwist=human['m_ForeArmTwist'],
    upperLegTwist=human['m_UpperLegTwist'], legTwist=human['m_LegTwist'], humanScale=human['m_Scale'],
    armStretch=human['m_ArmStretch'], legStretch=human['m_LegStretch'], feetSpacing=human['m_FeetSpacing'], hasTranslationDoF=human['m_HasTDoF'])))
for clip in clips:
    shutil.copy2(clip, output/clip.name)
editor = a.project/'Assets'/'Editor'
editor.mkdir(exist_ok=True)
shutil.copy2(Path(__file__).with_name('HumanoidReference.cs'), editor/'HumanoidReference.cs')
print(output)
