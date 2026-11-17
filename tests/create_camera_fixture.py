"""Create a separate regression fixture with an asymmetric underside."""
import bpy
import os
import sys

source, destination = sys.argv[sys.argv.index('--')+1:]
if os.path.exists(destination):
    raise FileExistsError(destination)
bpy.ops.wm.open_mainfile(filepath=source, use_scripts=False)
bpy.ops.mesh.primitive_cube_add(size=1, location=(.72,-.36,-.12))
marker = bpy.context.object
marker.name = 'Asymmetric underside marker'
marker.dimensions = (.28,.4,.38)
material = bpy.data.materials.new('Underside marker')
material.diffuse_color = (.75,.42,.08,1)
marker.data.materials.append(material)
bpy.ops.wm.save_as_mainfile(filepath=destination)
