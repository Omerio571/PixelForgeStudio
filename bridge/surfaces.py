"""Optional world-space surface data. Never changes the studio or beauty render."""
import bpy, json, os, struct
from array import array
from collections import Counter
from mathutils import Vector
from mathutils.bvhtree import BVHTree

def material_emission(material):
    if material is None:
        return 0
    override = material.get('pixelforge_emission')
    if isinstance(override, (int, float)):
        return max(0, min(1, float(override)))
    if not material.use_nodes or not material.node_tree:
        return 0
    output = next((node for node in material.node_tree.nodes
                   if node.type == 'OUTPUT_MATERIAL' and node.is_active_output), None)
    surface = output.inputs['Surface'] if output else None
    if surface is None or not surface.is_linked:
        return 0
    node = surface.links[0].from_node
    if node.type == 'BSDF_PRINCIPLED':
        color, strength = node.inputs['Emission Color'], node.inputs['Emission Strength']
    elif node.type == 'EMISSION':
        color, strength = node.inputs['Color'], node.inputs['Strength']
    else:
        return 0
    if color.is_linked or strength.is_linked:
        return 0
    energy = max(0, strength.default_value * max(color.default_value[:3]))
    return energy / (1 + energy)

def rendered_owners(directory, job, codes):
    """Read the very same stored ID and coverage bytes used by PixelEngine."""
    def pixels(name):
        image=bpy.data.images.load(os.path.join(directory,name), check_existing=False)
        try:
            image.colorspace_settings.name='Non-Color'
            image.alpha_mode='STRAIGHT'
            data=array('f',[0]) * len(image.pixels)
            image.pixels.foreach_get(data)
            return data
        finally:
            bpy.data.images.remove(image)

    color, labels = pixels('render.png'), pixels('ids.png')
    samples=job['SamplesPerPixel']
    width,height=job['Width']*samples,job['Height']*samples
    cache={}
    codes=list(codes)
    def distance(a,b):
        return sum((((a>>shift)&255)-((b>>shift)&255))**2 for shift in (0,8,16))
    for y in range(job['Height']):
        for x in range(job['Width']):
            hits={}
            for sy in range(samples):
                for sx in range(samples):
                    p=((height-1-y*samples-sy)*width+x*samples+sx)*4
                    if round(color[p+3]*255)<128 or round(labels[p+3]*255)<128:
                        continue
                    rgb=sum(round(labels[p+c]*255)<<(8*c) for c in range(3))
                    if rgb not in cache:
                        cache[rgb]=min(codes,key=lambda code:distance(code,rgb))
                    hits.setdefault(cache[rgb],[]).append(((sx+.5)/samples,(sy+.5)/samples))
            if sum(map(len,hits.values()))*2<samples*samples:
                yield None, []
            else:
                winner=min(hits,key=lambda code:(-len(hits[code]),code))
                yield winner,hits[winner]

def capture(scene, camera, snapshots, assembly, ids, light, job, directory):
    vertices, polygons, descriptions = [], [], []
    dynamic = job.get("DynamicLighting",False)
    emissions = {}
    for original, mesh, matrix, slots in snapshots:
        transform = assembly @ matrix
        offset = len(vertices)
        vertices.extend(transform @ v.co for v in mesh.vertices)
        normal_matrix = transform.to_3x3().inverted_safe().transposed()
        for face in mesh.polygons:
            polygons.append([offset+i for i in face.vertices])
            mat = slots[min(face.material_index,len(slots)-1)] if slots else None
            metallic, roughness = (mat.metallic, mat.roughness) if mat else (0,.8)
            if mat and mat.use_nodes and mat.node_tree:
                node = next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
                if node:
                    metallic = node.inputs['Metallic'].default_value
                    roughness = node.inputs['Roughness'].default_value
            # Geometric surface normals retain facets; gentle polygon transitions are ignored by the pixel stage.
            if dynamic and mat not in emissions:
                emissions[mat] = material_emission(mat)
            descriptions.append((ids[original.name],float(metallic),float(roughness),
                                 (normal_matrix @ face.normal).normalized(),emissions.get(mat,0)))
    tree = BVHTree.FromPolygons(vertices,polygons,all_triangles=False)
    corners = camera.data.view_frame(scene=scene)
    left,right = min(p.x for p in corners),max(p.x for p in corners)
    bottom,top = min(p.y for p in corners),max(p.y for p in corners)
    rotation = camera.rotation_euler.to_matrix()
    ray = rotation @ Vector((0,0,-1))
    pixel_world = (top-bottom)/job['Height']
    epsilon = max(pixel_world*1e-4,1e-7)
    samples = job['SamplesPerPixel']
    pixels = []
    normal_data = bytearray()
    owners=rendered_owners(directory,job,ids.values()) if dynamic else None
    recovered=[]
    unresolved=[]
    def origin(x,y):
        u,v=x/job['Width'],y/job['Height']
        return camera.location + rotation @ Vector((left+(right-left)*u,top-(top-bottom)*v,0))
    undo_presentation = assembly.to_3x3().transposed()
    for y in range(job['Height']):
        for x in range(job['Width']):
            hits = []
            for sy in range(samples):
                for sx in range(samples):
                    start=origin(x+(sx+.5)/samples,y+(sy+.5)/samples)
                    point,normal,index,depth = tree.ray_cast(start,ray,camera.data.clip_end)
                    if index is not None:
                        code,metal,rough,normal,emission = descriptions[index]
                        if not dynamic and normal.dot(ray)>0: normal=-normal
                        # Key visibility is geometry-based, not brightness inferred from the beauty PNG.
                        blocked = not dynamic and job.get('StudioLighting',True) and job['CastShadows'] and tree.ray_cast(point+normal*epsilon+light*epsilon,light)[0] is not None
                        hits.append((code,normal,depth,metal,rough,0.0 if blocked else 1.0,emission))
            if dynamic:
                # Keep the nearest facet for each visible object. The pixel converter
                # selects which object contributes; never blend across object IDs.
                nearest = {}
                for hit in hits:
                    if hit[0] not in nearest or hit[2]<nearest[hit[0]][2]:
                        nearest[hit[0]] = hit
                winner,positions=next(owners)
                if winner is not None and winner not in nearest:
                    # A Cycles raster hit can land between analytic sample centers.
                    # Probe only this pixel and accept only its foremost visible owner.
                    # Rank by the raster subpixels supporting the selected object.
                    offsets=sorted((( (u+.5)/16,(v+.5)/16) for v in range(16) for u in range(16)),
                                   key=lambda uv:min((uv[0]-px)**2+(uv[1]-py)**2 for px,py in positions))
                    for u,v in offsets:
                        _,_,face,depth=tree.ray_cast(origin(x+u,y+v),ray,camera.data.clip_end)
                        if face is None or descriptions[face][0]!=winner:
                            continue
                        code,metal,rough,normal,emission=descriptions[face]
                        nearest[winner]=(code,normal,depth,metal,rough,1.0,emission)
                        world=(undo_presentation @ normal).normalized()
                        recovered.append([x,y,winner,face,*world,round(emission*255)])
                        break
                    else:
                        unresolved.append([x,y,winner,sorted(nearest)])
                normal_data.append(len(nearest))
                for code,normal,_,_,_,_,emission in nearest.values():
                    world = (undo_presentation @ normal).normalized()
                    normal_data.extend(struct.pack('<I3fB',code,*world,round(emission*255)))
            if len(hits)*2 < samples*samples:
                pixels.append(None);continue
            code = sorted(Counter(h[0] for h in hits).items(),key=lambda p:(-p[1],p[0]))[0][0]
            chosen = [h for h in hits if h[0]==code]
            normal = sum((h[1] for h in chosen),Vector()).normalized()
            count = len(chosen)
            pixels.append([code,*[round(n,6) for n in normal],
                           round(sum(h[2] for h in chosen)/count,6),
                           round(sum(h[3] for h in chosen)/count,4),
                           round(sum(h[4] for h in chosen)/count,4),
                           round(sum(h[5] for h in chosen)/count,4)])
    if dynamic:
        if recovered or unresolved:
            with open(os.path.join(directory,'normal-sampling.json'),'w') as stream:
                json.dump({'recovered':recovered,'unresolved':unresolved},stream,separators=(',',':'))
        with open(os.path.join(directory,'normals.bin'),'wb') as stream:
            stream.write(normal_data)
    with open(os.path.join(directory,'surfaces.json'),'w') as stream:
        json.dump({'width':job['Width'],'height':job['Height'],'worldUnitsPerPixel':pixel_world,
                   'light':list(light),'pixels':pixels},stream,separators=(',',':'))
