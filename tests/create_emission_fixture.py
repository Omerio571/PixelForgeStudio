"""One evaluated mesh with non-emissive housing and two emissive materials."""
import bpy, os, sys
path = sys.argv[sys.argv.index('--')+1]
if os.path.exists(path):
    raise FileExistsError(path)
bpy.ops.wm.read_factory_settings(use_empty=True)
objects = []
for name, position, dimensions, color in [
    ('Housing', (0,0,.25), (3,1.2,.5), (.17,.2,.24)),
    ('Power strip', (-.75,-.2,.53), (1.3,.32,.14), (.11,.11,.12)),
    ('Status strip', (.75,-.2,.53), (1.3,.32,.14), (.16,.16,.17))
]:
    bpy.ops.mesh.primitive_cube_add(size=1,location=position)
    obj=bpy.context.object
    obj.scale=dimensions
    material=bpy.data.materials.new(name)
    material.use_nodes=True
    node=material.node_tree.nodes['Principled BSDF']
    node.inputs['Base Color'].default_value=(*color,1)
    if name=='Power strip':
        material['pixelforge_emission']=1.0
    if name=='Status strip':
        node.inputs['Emission Color'].default_value=(1,1,1,1)
        strength=node.inputs['Emission Strength']
        for frame,value in ((1,1),(4,3)):
            strength.default_value=value
            strength.keyframe_insert('default_value',frame=frame)
    obj.data.materials.append(material)
    objects.append(obj)
bpy.ops.object.select_all(action='SELECT')
bpy.context.view_layer.objects.active=objects[0]
bpy.ops.object.join()
scene=bpy.context.scene
scene.frame_end=4
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=path)
