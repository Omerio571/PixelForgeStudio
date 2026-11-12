"""Explicit 12-frame machine test fixture: parenting, constraints and armature evaluation."""
import bpy, math, sys, os
bpy.ops.wm.read_factory_settings(use_empty=True)
def material(name,color):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(*color,1)
    return m
wood=material('Fixture wood',(.25,.11,.035))
steel=material('Fixture steel',(.2,.4,.5))
dark=material('Fixture dark metal',(.055,.075,.09))
brass=material('Fixture marker',(.55,.3,.06))
def cube(name,location,scale,mat):
    bpy.ops.mesh.primitive_cube_add(size=1,location=location)
    obj=bpy.context.object;obj.name=name;obj.scale=scale;obj.data.materials.append(mat);return obj
base=cube('Static wooden machine base',(0,0,.15),(3.5,2,.3),wood)
cube('Left support',(-.9,.35,.85),(.3,.35,1.5),wood)
cube('Right support',(.9,.35,.85),(.3,.35,1.5),wood)
# Gear geometry belongs only to this fixture, never to the converter.
vertices=[];segments=48
for y in (-.16,.16):
    for i in range(segments):
        angle=i*2*math.pi/segments;radius=1 if i%4 in (1,2) else .82
        vertices.append((math.cos(angle)*radius,y,math.sin(angle)*radius))
faces=[tuple(range(segments-1,-1,-1)),tuple(range(segments,segments*2))]
faces += [(i,(i+1)%segments,(i+1)%segments+segments,i+segments) for i in range(segments)]
mesh=bpy.data.meshes.new('Fixture gear mesh');mesh.from_pydata(vertices,[],faces);mesh.materials.append(steel)
gear=bpy.data.objects.new('Animated gear',mesh);bpy.context.scene.collection.objects.link(gear);gear.location=(0,-.1,1.35)
bevel=gear.modifiers.new('Small bevel fixture','BEVEL');bevel.width=.025;bevel.segments=1
marker=cube('Asymmetric rotating marker',(0,0,0),(.2,.12,.45),brass);marker.parent=gear;marker.location=(.55,-.24,.2)
gear.rotation_euler[1]=0;gear.keyframe_insert('rotation_euler',frame=1)
gear.rotation_euler[1]=2*math.pi;gear.keyframe_insert('rotation_euler',frame=13)
# Constraint-driven piston uses an animated empty, so baking raw transforms is insufficient.
target=bpy.data.objects.new('Animated constraint target',None);bpy.context.scene.collection.objects.link(target)
for frame,z in ((1,.7),(4,1.2),(7,.7),(10,.3),(13,.7)):
    target.location=(1.3,-.3,z);target.keyframe_insert('location',frame=frame)
piston=cube('Constrained piston',(0,0,0),(.25,.3,.6),dark)
constraint=piston.constraints.new('COPY_LOCATION');constraint.target=target
# A small armature-driven part verifies that the evaluated mesh is sampled each frame.
armature=bpy.data.armatures.new('Fixture armature');rig=bpy.data.objects.new('Fixture rig',armature);bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT');bone=armature.edit_bones.new('Fixture bone');bone.head=(-1.35,0,.3);bone.tail=(-1.35,0,1.2);bpy.ops.object.mode_set(mode='OBJECT')
arm=cube('Armature-driven part',(-1.35,0,.75),(.16,.2,.9),steel)
group=arm.vertex_groups.new(name='Fixture bone');group.add(list(range(len(arm.data.vertices))),1,'REPLACE')
modifier=arm.modifiers.new('Armature fixture','ARMATURE');modifier.object=rig
pose=rig.pose.bones['Fixture bone'];pose.rotation_mode='XYZ'
for frame,angle in ((1,-.3),(7,.3),(13,-.3)):
    pose.rotation_euler[0]=angle;pose.keyframe_insert('rotation_euler',frame=frame)
scene=bpy.context.scene;scene.frame_start=1;scene.frame_end=12;scene.render.fps=12;scene.frame_set(1)
scene.timeline_markers.new('Machine cycle',frame=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(sys.argv[sys.argv.index('--')+1]))
