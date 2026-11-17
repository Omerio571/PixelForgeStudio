"""Optional world-space surface data. Never changes the studio or beauty render."""
import json, os, struct
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
    undo_presentation = assembly.to_3x3().transposed()
    for y in range(job['Height']):
        for x in range(job['Width']):
            hits = []
            for sy in range(samples):
                for sx in range(samples):
                    u=(x+(sx+.5)/samples)/job['Width']
                    v=(y+(sy+.5)/samples)/job['Height']
                    origin = camera.location + rotation @ Vector((left+(right-left)*u,top-(top-bottom)*v,0))
                    point,normal,index,depth = tree.ray_cast(origin,ray,camera.data.clip_end)
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
        with open(os.path.join(directory,'normals.bin'),'wb') as stream:
            stream.write(normal_data)
    with open(os.path.join(directory,'surfaces.json'),'w') as stream:
        json.dump({'width':job['Width'],'height':job['Height'],'worldUnitsPerPixel':pixel_world,
                   'light':list(light),'pixels':pixels},stream,separators=(',',':'))
