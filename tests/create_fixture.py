"""Clearly identified asymmetric two-material test fixture, not production artwork."""
import bpy, sys, os
bpy.ops.wm.read_factory_settings(use_empty=True)
def material(name,color):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); return m
stone=material('Fixture warm stone',(.3,.21,.12))
metal=material('Fixture blue metal',(.12,.3,.5))
bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1)
rock=bpy.context.object;rock.name='Test rock';rock.scale=(1.2,.8,.7);rock.data.materials.append(stone)
bpy.ops.mesh.primitive_cube_add(size=.6,location=(.8,-.4,.65))
cube=bpy.context.object;cube.name='Asymmetric test block';cube.data.materials.append(metal)
if '--parented' in sys.argv:
    from mathutils import Matrix
    root=bpy.data.objects.new('Fixture transformed parent',None)
    bpy.context.scene.collection.objects.link(root)
    root.location=(2,-3,1)
    root.rotation_euler=(.1,.2,.3)
    bpy.context.view_layer.update()
    for obj in (rock,cube):
        world=obj.matrix_world.copy()
        obj.parent=root
        obj.matrix_world=world
if '--double' in sys.argv:
    from mathutils import Matrix
    bpy.context.view_layer.update()
    for obj in (rock,cube):
        obj.matrix_world=Matrix.Scale(2,4) @ obj.matrix_world
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(sys.argv[sys.argv.index('--')+1]))
