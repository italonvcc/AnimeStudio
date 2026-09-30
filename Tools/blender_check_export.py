"""Run with Blender --background --python-exit-code 1 --python this_file -- model.fbx report.json."""
import bpy, json, math, sys
from pathlib import Path
source, report = map(Path, sys.argv[sys.argv.index('--') + 1:])
if report.exists():
    raise RuntimeError('Use a new report path')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(source.resolve()))
armatures = [o for o in bpy.data.objects if o.type == 'ARMATURE']
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
result = {'model': source.name, 'armatures': [], 'meshes': [], 'actions': []}
for rig in armatures:
    result['armatures'].append({'name': rig.name, 'bones': len(rig.data.bones)})
for mesh in meshes:
    skin = [m for m in mesh.modifiers if m.type == 'ARMATURE']
    result['meshes'].append({'name': mesh.name, 'vertices': len(mesh.data.vertices),
        'materials': len(mesh.data.materials), 'vertex_groups': len(mesh.vertex_groups),
        'parent': mesh.parent.name if mesh.parent else None, 'parent_type': mesh.parent_type,
        'rigs': [m.object.name if m.object else None for m in skin],
        'unweighted_vertices': sum(not v.groups for v in mesh.data.vertices),
        'finite_vertices': all(math.isfinite(x) for v in mesh.data.vertices for x in v.co)})
for action in bpy.data.actions:
    for rig in armatures:
        slots = [s for s in action.slots if s.target_id_type == 'OBJECT'] if hasattr(action, 'slots') else []
        if hasattr(action, 'slots') and not slots:
            continue  # Shape-key actions cannot be assigned to an armature Object.
        rig.animation_data_create()
        rig.animation_data.action = action
        if slots:
            rig.animation_data.action_slot = slots[0]
        for track in rig.animation_data.nla_tracks:
            track.mute = True
        for bone in rig.pose.bones:
            bone.matrix_basis.identity()
        lo, hi = action.frame_range
        samples = []
        for frame in [lo, lo + (hi-lo)*.25, lo + (hi-lo)*.5, lo + (hi-lo)*.75, hi]:
            bpy.context.scene.frame_set(int(frame), subframe=frame-int(frame))
            samples.append({p.name: tuple(x for row in p.matrix_basis for x in row) for p in rig.pose.bones})
        moving = [name for name, first in samples[0].items() if any(max(abs(a-b) for a,b in zip(first,s[name])) > 1e-5 for s in samples[1:])]
        result['actions'].append({'name': action.name, 'rig': rig.name, 'frames': [lo,hi], 'moving_local_bones': moving})
errors = []
if not armatures or not meshes: errors.append('Missing armature or mesh')
if not all(m['finite_vertices'] for m in result['meshes']): errors.append('Nonfinite vertex coordinates')
if not all(m['rigs'] and all(m['rigs']) for m in result['meshes'] if m['vertex_groups']): errors.append('Unbound skinned mesh')
if not all(m['unweighted_vertices'] == 0 for m in result['meshes'] if m['vertex_groups']): errors.append('Unweighted skinned mesh vertices')
result['errors'] = errors
report.parent.mkdir(parents=True, exist_ok=True)
report.write_text(json.dumps(result, indent=2))
assert not errors, '; '.join(errors)
print('EXPORT_CHECK ' + json.dumps({'armatures':len(armatures),'meshes':len(meshes),'actions':len(result['actions'])}))
