"""Render inspection poses, without saving/modifying the FBX. Run using Blender --python-exit-code 1."""
import bpy, json, sys
from pathlib import Path
from mathutils import Vector

source, output = map(Path, sys.argv[sys.argv.index('--') + 1:])
output.mkdir(parents=True, exist_ok=False)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(source.resolve()))
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
for mesh in meshes:
    # AO helper volumes are rigid attachments, not visible character surfaces.
    mesh.hide_render = mesh.name.startswith('AO_')
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x = 720
scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.display.shading.background_type = 'WORLD'
scene.world = scene.world or bpy.data.worlds.new('InspectionWorld')
scene.world.color = (.08, .08, .08)
camera = bpy.data.objects.new('InspectionCamera', bpy.data.cameras.new('InspectionCamera'))
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = 'ORTHO'
records = []
for label, fragment, fraction in [('idle', 'Standby__WithBody', .25), ('run', 'RunCycle__WithBody', .25),
                                   ('attack', 'Mona_Attack_01|', .35), ('burst', 'Mona_ElementalBurst|', .5),
                                   ('skill', 'Mona_ElementalArt_BS|', .5)]:
    matches = [a for a in bpy.data.actions if a.name.startswith(rig.name+'|') and fragment in a.name]
    if len(matches) != 1:
        raise RuntimeError(f'Expected one {label} action, found {len(matches)}')
    action = matches[0]
    rig.animation_data_create()
    rig.animation_data.action = action
    rig.animation_data.action_slot = next(s for s in action.slots if s.target_id_type == 'OBJECT')
    for track in rig.animation_data.nla_tracks:
        track.mute = True
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()
    lo, hi = action.frame_range
    frame = lo + fraction * (hi-lo)
    scene.frame_set(int(frame), subframe=frame-int(frame))
    graph = bpy.context.evaluated_depsgraph_get()
    points = [o.evaluated_get(graph).matrix_world @ Vector(v) for o in meshes if not o.hide_render
              for v in o.evaluated_get(graph).bound_box]
    lower = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    upper = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    center = (lower + upper) / 2
    size = max(upper-lower)
    camera.location = center + Vector((2, -5, 1.5)).normalized() * size * 3
    camera.rotation_euler = (center-camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.ortho_scale = size * 1.5
    camera.data.clip_start = size * .001
    camera.data.clip_end = size * 20
    scene.render.filepath = str((output/(label+'.png')).resolve())
    bpy.ops.render.render(write_still=True)
    records.append({'action': action.name, 'frame': frame, 'bounds_min': list(lower), 'bounds_max': list(upper)})
(output/'poses.json').write_text(json.dumps(records, indent=2))
