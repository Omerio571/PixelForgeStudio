"""Generic square/rectangular animated planar framing fixture; never production artwork."""
import bpy, sys
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.mesh.primitive_plane_add(size=2)
obj=bpy.context.object
obj.name='Framing test tile'
obj.scale.y=.5 if '--rectangle' in sys.argv else 1
if '--tiny' in sys.argv:
    obj.scale *= .0001
mat=bpy.data.materials.new('Tile source material')
mat.diffuse_color=(.35,.2,.1,1)
obj.data.materials.append(mat)
obj.location.x=0
obj.keyframe_insert(data_path='location',frame=1)
obj.location.x=.5
obj.keyframe_insert(data_path='location',frame=3)
bpy.context.scene.frame_start=1
bpy.context.scene.frame_end=3
bpy.context.scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=sys.argv[sys.argv.index('--')+1])
