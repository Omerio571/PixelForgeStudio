"""Optional world-space surface data. Never changes the studio or beauty render."""
import json, os
from collections import Counter
from mathutils import Vector
from mathutils.bvhtree import BVHTree

def capture(scene, camera, snapshots, assembly, ids, light, job, directory):
    vertices, polygons, descriptions = [], [], []
    for original, mesh, matrix, slots in snapshots:
        transform = assembly @ matrix
        offset = len(vertices)
        vertices.extend(transform @ v.co for v in mesh.vertices)
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
            normal_matrix = transform.to_3x3().inverted_safe().transposed()
            descriptions.append((ids[original.name],float(metallic),float(roughness),
                                 (normal_matrix @ face.normal).normalized()))
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
                        code,metal,rough,normal = descriptions[index]
                        if normal.dot(ray)>0: normal=-normal
                        # Key visibility is geometry-based, not brightness inferred from the beauty PNG.
                        blocked = job['CastShadows'] and tree.ray_cast(point+normal*epsilon+light*epsilon,light)[0] is not None
                        hits.append((code,normal,depth,metal,rough,0.0 if blocked else 1.0))
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
    with open(os.path.join(directory,'surfaces.json'),'w') as stream:
        json.dump({'width':job['Width'],'height':job['Height'],'worldUnitsPerPixel':pixel_world,
                   'light':list(light),'pixels':pixels},stream,separators=(',',':'))
