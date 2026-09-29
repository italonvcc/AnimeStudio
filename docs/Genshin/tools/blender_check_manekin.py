"""Pose an exported Manekin assembly to check skinning; this is synthetic validation, not a game animation."""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector

source, output = [Path(p).resolve() for p in sys.argv[sys.argv.index('--') + 1:]]
output.mkdir(parents=True, exist_ok=False)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(source.resolve()))
rigs = [o for o in bpy.data.objects if o.type == 'ARMATURE']
assert len(rigs) == 1, f'Expected one rig, found {len(rigs)}'
rig = rigs[0]
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
assert len(meshes) >= 2
for mesh in meshes:
    assert any(m.type == 'ARMATURE' and m.object == rig for m in mesh.modifiers), mesh.name
    assert all(v.groups for v in mesh.data.vertices), f'Unweighted vertices: {mesh.name}'

def vertices(mesh):
    obj = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    data = obj.to_mesh()
    points = [obj.matrix_world @ v.co for v in data.vertices]
    obj.to_mesh_clear()
    assert all(math.isfinite(x) for p in points for x in p)
    return points

scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x = 720
scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.show_cavity = True
scene.world = bpy.data.worlds.new('InspectionWorld')
scene.world.color = (.08, .08, .08)
camera = bpy.data.objects.new('InspectionCamera', bpy.data.cameras.new('InspectionCamera'))
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = 'ORTHO'
rest = {m.name: vertices(m) for m in meshes}
posed = []
for frame, sign in [(1, 0), (15, 1), (30, -1)]:
    scene.frame_set(frame)
    for bone in rig.pose.bones:
        bone.rotation_mode = 'XYZ'
        bone.rotation_euler = (0, 0, 0)
        amount = .25 if bone.name in ['Bip001 Spine', 'Bip001 Head'] else .4 if bone.name in ['Bip001 L UpperArm', 'Bip001 R UpperArm', 'Bip001 L Thigh', 'Bip001 R Thigh'] else 0
        bone.rotation_euler.x = sign * amount
        bone.keyframe_insert('rotation_euler', frame=frame)
    bpy.context.view_layer.update()
    all_points, movement = [], []
    for mesh in meshes:
        points = vertices(mesh)
        assert len(points) == len(rest[mesh.name])
        delta = max((a-b).length for a, b in zip(points, rest[mesh.name]))
        assert delta < 1.5, f'Implausible skin displacement: {mesh.name}: {delta}'
        movement.append({'mesh':mesh.name, 'vertices':len(points), 'max_displacement':delta})
        all_points.extend(points)
    if sign:
        assert all(m['max_displacement'] > 1e-5 for m in movement), 'Detached/unmoving assembly part'
    low = Vector([min(p[i] for p in all_points) for i in range(3)])
    high = Vector([max(p[i] for p in all_points) for i in range(3)])
    center = (low+high)/2
    size = max(high-low)
    camera.location = center + Vector((3, -6, 1.6)).normalized()*size*3
    camera.rotation_euler = (center-camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.ortho_scale = size*1.35
    camera.data.clip_start = size*.001
    camera.data.clip_end = size*20
    scene.render.filepath = str(output/f'pose-{frame}.png')
    bpy.ops.render.render(write_still=True)
    posed.append({'frame':frame, 'meshes':movement, 'bounds_min':list(low), 'bounds_max':list(high)})
(output/'report.json').write_text(json.dumps({'source':source.name, 'synthetic_pose_test':True, 'bones':len(rig.data.bones), 'poses':posed}, indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str((output/'inspection.blend').resolve()))
print('MANEKIN_CHECK', len(meshes), 'meshes remain bound through both synthetic poses')
