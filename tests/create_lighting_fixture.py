import bpy, sys, os
destination = sys.argv[sys.argv.index('--')+1]
if os.path.exists(destination):
    raise FileExistsError(destination)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.mesh.primitive_cube_add(size=1, location=(0,0,.6))
cube = bpy.context.object
cube.scale = (1.7,1.2,1.2)
cube.name = 'World-axis reference block'
material = bpy.data.materials.new('Reference teal')
material.diffuse_color = (.22,.5,.53,1)
cube.data.materials.append(material)
bpy.ops.wm.save_as_mainfile(filepath=destination)
