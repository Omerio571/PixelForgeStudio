"""Separate animated fixture objects, collection-only grouping; never joins/bakes."""
import bpy, os, sys, runpy, json
target = os.path.abspath(sys.argv[sys.argv.index('--')+1])
runpy.run_path(os.path.join(os.path.dirname(__file__),'create_animated_fixture.py'),run_name='__main__')
scene=bpy.context.scene
machine=bpy.data.collections.new('PF_Outline_Machine');scene.collection.children.link(machine)
nested=bpy.data.collections.new('Moving parts');machine.children.link(nested)
accessory=bpy.data.collections.new('PF_Outline_Accessory');scene.collection.children.link(accessory)
ambiguous=bpy.data.collections.new('PF_Outline_Z_Conflict');scene.collection.children.link(ambiguous)
for name in ('Static wooden machine base','Left support','Right support'):
    machine.objects.link(bpy.data.objects[name])
for name in ('Animated gear','Asymmetric rotating marker','Constrained piston'):
    nested.objects.link(bpy.data.objects[name])
accessory.objects.link(bpy.data.objects['Armature-driven part'])
# Explicit competing membership exercises deterministic ambiguity reporting.
ambiguous.objects.link(bpy.data.objects['Right support'])
# New ungrouped object remains independent, including its visible contour.
bpy.ops.mesh.primitive_cube_add(size=1,location=(1.45,.25,.4))
obj=bpy.context.object;obj.name='Ungrouped block';obj.scale=(.35,.5,.5)
obj.data.materials.append(bpy.data.materials['Fixture dark metal'])
# A hidden member must never generate pixels, bridges or hidden contours.
bpy.ops.mesh.primitive_cube_add(size=1,location=(10,10,10));hidden=bpy.context.object
hidden.name='Hidden group member';hidden.hide_render=True;machine.objects.link(hidden)
scene.frame_set(1)
audit=[]
for frame in (1,4,7,10):
    scene.frame_set(frame);bpy.context.view_layer.update()
    audit.append({'frame':frame,'body':list(bpy.data.objects['Static wooden machine base'].matrix_world),
                  'gearRotation':list(bpy.data.objects['Animated gear'].rotation_euler)})
    audit[-1]['body']=[list(row) for row in audit[-1]['body']]
json.dump(audit,open(target+'.audit.json','w'),indent=2)
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=target)
