"""v0.3.3 coherence fixture: enclosing frame, gear, independent moving conveyor."""
import bpy, os, sys, math, runpy
runpy.run_path(os.path.join(os.path.dirname(__file__),'create_grouped_fixture.py'),run_name='__main__')
target=os.path.abspath(sys.argv[sys.argv.index('--')+1])
group=bpy.data.collections['PF_Outline_Machine']
wood=bpy.data.materials['Fixture wood'];metal=bpy.data.materials['Fixture dark metal']
def block(name,position,scale,material):
    bpy.ops.mesh.primitive_cube_add(size=1,location=position)
    obj=bpy.context.object;obj.name=name;obj.scale=scale;obj.data.materials.append(material);group.objects.link(obj);return obj
block('Unified frame left',(-1.25,-.05,1.55),(.2,.35,2.8),wood)
block('Unified frame right',(1.25,-.05,1.55),(.2,.35,2.8),wood)
block('Unified frame crossbeam',(0,-.05,2.85),(2.7,.35,.2),wood)
for n in range(3):
    obj=block('Moving conveyor slat '+str(n),(-.6+n*.6,-.65,.45),(.48,.25,.12),metal)
    for frame,shift in ((1,0),(4,.16),(7,0),(10,-.16),(13,0)):
        obj.location.x=-.6+n*.6+shift;obj.keyframe_insert('location',frame=frame)
bpy.context.scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=target)
