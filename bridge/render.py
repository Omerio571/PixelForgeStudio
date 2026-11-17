"""Static/animated Blender worker. Source geometry and materials are never saved."""
import bpy, sys, json, math, os
from mathutils import Vector, Matrix
sys.dont_write_bytecode = True  # Keep installed integration resources read-only.
sys.path.insert(0,os.path.dirname(__file__))
from studio import snapshot, discard, material_info, matte, emission_material, assign, render, ground_passes

job = json.load(open(sys.argv[sys.argv.index('--')+1],encoding='utf-8-sig'))
out = job['output']
dynamic = job.get('DynamicLighting', False)
os.makedirs(out,exist_ok=True)
def fail(message):
    json.dump({'message':message},open(os.path.join(out,'error.json'),'w'))
    raise RuntimeError(message)
def progress(message):
    print('PIXELFORGE_PROGRESS: '+message,flush=True)

bpy.ops.wm.open_mainfile(filepath=job['Model'],use_scripts=False)
source = bpy.context.scene
inspection = {'start':source.frame_start,'end':source.frame_end,'current':source.frame_current,
              'fps':source.render.fps/source.render.fps_base,
              'has_animation':any(o.animation_data or getattr(o.data,'animation_data',None) for o in source.objects),
              'markers':[{'name':m.name,'frame':m.frame} for m in source.timeline_markers],
              'shadow_catcher_supported':'is_shadow_catcher' in bpy.types.Object.bl_rna.properties}
json.dump(inspection,open(os.path.join(out,'inspection.json'),'w'),indent=2)
if job.get('inspect',False):
    sys.exit(0)
registry = [o for o in source.objects if o.type in {'MESH','CURVE','SURFACE','META','FONT'}]
if any(o.instance_type=='COLLECTION' and not o.hide_render for o in source.objects):
    fail('Collection instances are not supported. Make instances real in a separate model copy.')
if len(registry)>32767:
    fail('At most 32767 geometric objects are supported.')
for obj in registry:
    for modifier in obj.modifiers:
        modifier.show_viewport = modifier.show_render
        if modifier.type=='SUBSURF':
            modifier.levels = modifier.render_levels
frames = list(range(job['StartFrame'],job['EndFrame']+1,job['FrameStep'])) if job['Animation'] else [job.get('Frame') if job.get('Frame') is not None else source.frame_current]
if not frames or len(frames)>256:
    fail('Select 1–256 frames, using a positive frame step.')
elevation = math.asin(1/math.sqrt(3)) if job['Projection']=='TrueIsometric' else math.asin(.5)
azimuths = {'IsometricSE':-45,'IsometricSW':-135,'IsometricNE':45,'IsometricNW':135}
view = job['View']
turn = math.radians(-45-azimuths[view]) if view in azimuths else 0
assembly = Matrix.Rotation(turn,4,'Z')
if view in azimuths:
    a = math.radians(-45)
    direction = Vector((math.cos(a)*math.cos(elevation),math.sin(a)*math.cos(elevation),math.sin(elevation)))
else:
    direction = Vector({'Top':(0,0,1),'Bottom':(0,0,-1),'Front':(0,-1,0),'Back':(0,1,0),'Left':(-1,0,0),'Right':(1,0,0)}[view])
orientation = (-direction).to_track_quat('-Z','Y').to_euler()
camera_selection = job.get('Camera')
if camera_selection:
    heading = camera_selection['Direction']
    preset = camera_selection['Perspective']
    if camera_selection['Family'] == 'Isometric':
        turn = math.radians(-45-azimuths['Isometric'+heading])
        elevation = math.asin(1/math.sqrt(3)) if preset == 'TrueIsometric' else math.asin(.5)
        a = math.radians(-45)
    else:
        heading_angle = {'E':0,'N':90,'W':180,'S':-90}[heading]
        angles = {'DirectTop':90,'TopDownThreeQuarter':60,'HighTopDown':75,
                  'DirectBottom':-90,'AngledUnderside':-60,'StraightSide':0,'SlightlyElevatedSide':15}
        elevation = math.radians(camera_selection.get('Elevation') or angles[preset])
        turn = math.radians(90-heading_angle)
        a = math.pi/2
    assembly = Matrix.Rotation(turn,4,'Z')
    if preset in ('DirectTop','DirectBottom'):
        direction = Vector((0,0,1 if preset == 'DirectTop' else -1))
        up = Vector((0,1,0))
        orientation = Matrix((up.cross(direction),up,direction)).transposed().to_euler()
    else:
        direction = Vector((math.cos(a)*math.cos(elevation),math.sin(a)*math.cos(elevation),math.sin(elevation)))
        orientation = (-direction).to_track_quat('-Z','Y').to_euler()
elif view not in azimuths:
    elevation = math.asin(direction.z)
basis = orientation.to_matrix()
az,el = math.radians(job['LightAzimuth']),math.radians(job['LightElevation'])
world_light = basis @ Vector((math.cos(el)*math.cos(az),math.cos(el)*math.sin(az),math.sin(el)))
shadow_enabled = job['GroundShadows'] and direction.z>.01 and world_light.z>.05
include_shadow_bounds = job.get('FitBounds','ObjectOnly')=='ObjectAndGroundShadow'
warnings = []
if dynamic and shadow_enabled:
    warnings.append('Baked ground shadows can conflict with runtime lighting. Disable Ground Shadows to export only the asset silhouette.')
if job['GroundShadows'] and not shadow_enabled:
    warnings.append('Ground shadows need a camera and light above the ground plane. They are omitted for this view/light; object lighting is unchanged.')
# Pass one: union all evaluated motion. Store only bounds, never all frame meshes.
lo = Vector((float('inf'),)*3)
hi = Vector((float('-inf'),)*3)
projected_min = Vector((float('inf'),)*3)
projected_max = Vector((float('-inf'),)*3)
shadow_min = Vector((float('inf'),)*3)
shadow_max = Vector((float('-inf'),)*3)
shadow_world_min = Vector((float('inf'),)*3)
shadow_world_max = Vector((float('-inf'),)*3)
for index,frame in enumerate(frames):
    progress(f'Inspecting motion bounds {index+1} of {len(frames)}')
    source.frame_set(frame)
    snapshots = snapshot(source,registry)
    for _,mesh,matrix,_ in snapshots:
        for vertex in mesh.vertices:
            p = assembly @ matrix @ vertex.co
            for axis in range(3):
                lo[axis] = min(lo[axis],p[axis]); hi[axis] = max(hi[axis],p[axis])
            q = basis.transposed() @ p
            for axis in range(3):
                projected_min[axis] = min(projected_min[axis],q[axis]); projected_max[axis] = max(projected_max[axis],q[axis])
            if shadow_enabled:
                # Projection onto z=0 is linear. Once the shared ground height is
                # known, translate these extrema instead of inflating an AABB.
                ground_point = p-world_light*(p.z/world_light.z)
                shadow_q = basis.transposed() @ ground_point
                for axis in range(3):
                    shadow_min[axis] = min(shadow_min[axis],shadow_q[axis]);shadow_max[axis] = max(shadow_max[axis],shadow_q[axis])
                    shadow_world_min[axis] = min(shadow_world_min[axis],ground_point[axis]);shadow_world_max[axis] = max(shadow_world_max[axis],ground_point[axis])
    discard(snapshots)
if not math.isfinite(lo.z):
    fail('The selected frames contain no render-visible geometry.')
pivot = Vector((0,0,lo.z))
ground_z = lo.z-max((hi-lo).length*1e-5,1e-6)
# Reserve the tight projected motion/shadow union at every strength, including 0%.
if shadow_enabled:
    ground_shift = world_light*(ground_z/world_light.z)
    screen_shift = basis.transposed() @ ground_shift
    # A SUN angle is an angular diameter. Reserve the supported half-angle budget
    # once, so softness and strength edits cannot rescale/reposition the model.
    margin = (hi.z-ground_z)*math.tan(.5*.5)/world_light.z
    if include_shadow_bounds:
        for axis in (0,1):
            projected_min[axis] = min(projected_min[axis],shadow_min[axis]+screen_shift[axis]-margin)
            projected_max[axis] = max(projected_max[axis],shadow_max[axis]+screen_shift[axis]+margin)
    shadow_world_min += ground_shift;shadow_world_max += ground_shift
    receiver_radius = max(abs(shadow_world_min.x),abs(shadow_world_min.y),abs(shadow_world_max.x),abs(shadow_world_max.y))+margin
    shadow_distance = max((Vector((x,y,ground_z))-pivot).length for x in (shadow_world_min.x,shadow_world_max.x) for y in (shadow_world_min.y,shadow_world_max.y))+margin
offset = basis.transposed() @ pivot
xmin,xmax = projected_min.x-offset.x,projected_max.x-offset.x
ymin,ymax = projected_min.y-offset.y,projected_max.y-offset.y
if job.get('sharedBounds'):
    xmin,xmax,ymin,ymax = job['sharedBounds']
aspect = job['Width']/job['Height']; usable = 1-2*job['Padding']; ppu = job['PixelsPerUnit']
required = [math.ceil((xmax-xmin)*ppu/usable),math.ceil((ymax-ymin)*ppu/usable)]
json.dump({'bounds':[xmin,xmax,ymin,ymax],'projected_world_size':[xmax-xmin,ymax-ymin],'required_pixels':required,'canvas':[job['Width'],job['Height']],
           'pixels_per_unit':ppu,'world_scale':job['WorldScale'],'fit_bounds':job.get('FitBounds','ObjectOnly'),'shared_frames':frames},open(os.path.join(out,'framing.json'),'w'),indent=2)
if job['WorldScale'] and (required[0]>job['Width'] or required[1]>job['Height']):
    subject = 'The animation' if job['Animation'] else 'The model'
    fail(f"{subject} needs approximately {required[0]}×{required[1]} pixels at {ppu:g} px/unit for the selected fit bounds, but your canvas is {job['Width']}×{job['Height']}. Reduce pixels per unit, increase canvas dimensions, or switch to Fit to Canvas. No rendering was started.")
if job.get('boundsOnly'):
    sys.exit(0)
fit_size = max(ymax-ymin,(xmax-xmin)/aspect)
if not math.isfinite(fit_size) or fit_size <= 0:
    fail('The selected view has no finite projected extent to frame.')
scale = job['Height']/ppu if job['WorldScale'] else fit_size/usable
if dynamic:
    job['NeutralAoDistance'] = scale*.045
shift_x = (xmin+xmax)/2
shift_y = ymin+scale*(.5-job['Padding'])
distance = max(max((Vector((x,y,z))-pivot).length for x in (lo.x,hi.x) for y in (lo.y,hi.y) for z in (lo.z,hi.z))*3,10)
if shadow_enabled and include_shadow_bounds:
    distance = max(distance,shadow_distance*3)
ids = {obj.name:((i&31)*8+4)|((((i>>5)&31)*8+4)<<8)|((((i>>10)&31)*8+4)<<16) for i,obj in enumerate(registry,1)}
names = {str(value):name for name,value in ids.items()}
if job.get('RespectOutlineGroups',False):
    from outline_groups import resolve
    outline_groups, group_warnings = resolve(source,registry,ids)
    warnings.extend(group_warnings)
    json.dump(outline_groups,open(os.path.join(out,'outline-groups.json'),'w'),indent=2)
all_materials = {}; manifest = []
for index,frame in enumerate(frames):
    progress(f'Rendering frame {index+1} of {len(frames)} (Blender {frame})')
    source.frame_set(frame)
    snapshots = snapshot(source,registry)
    scene = bpy.data.scenes.new('PixelForge temporary studio')
    objects = []; local_materials = []; descriptions = {}
    for original,mesh,matrix,slots in snapshots:
        obj = bpy.data.objects.new(original.name+' render',mesh)
        scene.collection.objects.link(obj); obj.matrix_world = assembly @ matrix
        obj.visible_shadow = job['CastShadows']
        mapped = []
        for material in slots:
            if material:
                info = material_info(material); key = material.name
                if key not in descriptions:
                    descriptions[key] = matte(info,job);local_materials.append(descriptions[key]);all_materials[key] = info
                mapped.append(descriptions[key])
            else:
                fallback = matte({'name':'unassigned','color':(.8,.8,.8,1)},job);mapped.append(fallback);local_materials.append(fallback)
        assign(obj,mapped)
        objects.append(obj)
    camera_data = bpy.data.cameras.new('PixelForge orthographic')
    camera = bpy.data.objects.new('PixelForge camera',camera_data)
    scene.collection.objects.link(camera);scene.camera = camera
    camera.rotation_euler = orientation;camera.location = pivot+direction*distance
    camera_data.type = 'ORTHO';camera_data.sensor_fit = 'HORIZONTAL';camera_data.ortho_scale = scale*aspect
    camera_data.shift_x = shift_x/(scale*aspect);camera_data.shift_y = shift_y/(scale*aspect)
    camera_data.clip_start = .001;camera_data.clip_end = distance*5
    scene.render.engine = 'CYCLES';scene.cycles.device = 'CPU';scene.cycles.samples = 32;scene.cycles.seed = 0;scene.cycles.use_denoising = False
    scene.render.resolution_x = job['Width']*job['SamplesPerPixel'];scene.render.resolution_y = job['Height']*job['SamplesPerPixel'];scene.render.resolution_percentage = 100
    scene.render.film_transparent = True;scene.render.image_settings.file_format = 'PNG';scene.render.image_settings.color_mode = 'RGBA';scene.render.image_settings.color_depth = '8'
    scene.render.dither_intensity = 0;scene.view_settings.view_transform = 'Standard';scene.view_settings.look = 'None'
    scene.world = bpy.data.worlds.new('PixelForge ambient');scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get('Background');background.inputs['Color'].default_value = (1,1,1,1);background.inputs['Strength'].default_value = job['AmbientIntensity']
    data = bpy.data.lights.new('PixelForge key','SUN');data.energy = job['LightIntensity'];data.angle = .05
    light = bpy.data.objects.new('PixelForge key',data);scene.collection.objects.link(light);light.rotation_euler = (-world_light).to_track_quat('-Z','Y').to_euler()
    directory = out if not job['Animation'] else os.path.join(out,f'frame-{index:04d}')
    os.makedirs(directory,exist_ok=True)
    render(scene,os.path.join(directory,'render.png'))
    if dynamic or (job.get('Outlines',False) and job.get('OutlineType')=='Selective') or (job.get('StudioLighting',True) and job.get('EdgeHighlights',False) and job.get('EdgeHighlightStrength',.3)>0):
        from surfaces import capture
        progress(f'Capturing evaluated edge geometry {index+1} of {len(frames)}')
        capture(scene,camera,snapshots,assembly,ids,world_light,job,directory)
    if shadow_enabled and job['GroundShadowStrength']>0:
        ground_passes(scene,objects,light,job,directory,ground_z,receiver_radius*2+10)
    for obj,(original,_,_,_) in zip(objects,snapshots):
        obj.hide_render = False;obj.visible_camera = True
        mask = emission_material(ids[original.name]);local_materials.append(mask)
        assign(obj,[mask]*max(1,len(obj.material_slots)))
    scene.cycles.samples = 1;scene.view_settings.view_transform = 'Raw';scene.render.filter_size = .01;background.inputs['Strength'].default_value = 0
    render(scene,os.path.join(directory,'ids.png'))
    manifest.append({'source_frame':frame,'directory':directory,'camera_scale':scale,'camera_shift':[camera_data.shift_x,camera_data.shift_y]})
    if index==0:
        json.dump({'objects':names,'materials':list(all_materials.values()),'bounds':[list(lo),list(hi)],'elevation_degrees':math.degrees(elevation),'ortho_scale':scale,
                   'camera_rotation':list(orientation),'camera_location':list(camera.location),'light_rotation':list(light.rotation_euler),
                   'model_rotation_degrees':math.degrees(turn),'ground_pivot':list(pivot),'warnings':warnings,
                   'ground_shadow_backend':'paired matte receiver / Cycles','axis_convention':'+X east, +Y north, +Z up; front is -Y'},open(os.path.join(out,'model.json'),'w'),indent=2)
    bpy.context.window.scene = source
    for obj in list(scene.objects):
        bpy.data.objects.remove(obj,do_unlink=True)
    world = scene.world
    bpy.data.scenes.remove(scene);bpy.data.cameras.remove(camera_data);bpy.data.lights.remove(data);bpy.data.worlds.remove(world)
    discard(snapshots)
    for material in local_materials:
        if material.users==0:bpy.data.materials.remove(material)
json.dump({'frames':manifest,'ground_pivot':list(pivot),'ortho_scale':scale,'shared_camera':True,'camera_to_world':[list(row) for row in assembly.to_3x3().transposed() @ basis],
           'presentation_yaw_degrees':math.degrees(turn),'elevation_degrees':math.degrees(elevation)},open(os.path.join(out,'frames.json'),'w'),indent=2)
