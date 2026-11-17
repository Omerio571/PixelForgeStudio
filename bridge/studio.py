"""Shared disposable Blender studio, evaluated geometry and render helpers."""
import bpy, math, os
from mathutils import Vector, Matrix

def visible_objects(collection):
    if collection.hide_render:
        return
    yield from (o for o in collection.objects if not o.hide_render)
    for child in collection.children:
        yield from visible_objects(child)

def snapshot(source, registry):
    bpy.context.window.scene = source
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    visible = set(visible_objects(source.collection))
    result = []
    for original in registry:
        if original not in visible:
            continue
        evaluated = original.evaluated_get(deps)
        mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=deps)
        if mesh and len(mesh.vertices):
            result.append((original, mesh, evaluated.matrix_world.copy(), [s.material for s in evaluated.material_slots]))
        elif mesh:
            bpy.data.meshes.remove(mesh)
    return result

def discard(snapshots):
    for _,mesh,_,_ in snapshots:
        bpy.data.meshes.remove(mesh)

def material_info(material):
    color = tuple(material.diffuse_color)
    textured = False
    if material.use_nodes and material.node_tree:
        node = next((n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
        if node:
            color = tuple(node.inputs['Base Color'].default_value)
            textured = node.inputs['Base Color'].is_linked
    return {'name':material.name,'color':color,'linked_base_color':textured}

def matte(info, job):
    # Never rewrite a source material: animate/evaluate it again on the next frame.
    material = bpy.data.materials.new('PixelForge matte '+info['name'])
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    if job.get('DynamicLighting',False):
        emission = tree.nodes.new('ShaderNodeEmission')
        emission.inputs['Color'].default_value = info['color']
        output = tree.nodes.new('ShaderNodeOutputMaterial')
        tree.links.new(emission.outputs[0],output.inputs['Surface'])
        return material
    diffuse = tree.nodes.new('ShaderNodeBsdfDiffuse')
    diffuse.inputs['Color'].default_value = info['color']
    diffuse.inputs['Roughness'].default_value = 1
    emission = tree.nodes.new('ShaderNodeEmission')
    emission.inputs['Color'].default_value = info['color']
    emission.inputs['Strength'].default_value = (1-job['ShadowIntensity'])*.6
    add = tree.nodes.new('ShaderNodeAddShader')
    tree.links.new(diffuse.outputs[0],add.inputs[0])
    tree.links.new(emission.outputs[0],add.inputs[1])
    output = tree.nodes.new('ShaderNodeOutputMaterial')
    tree.links.new(add.outputs[0],output.inputs['Surface'])
    return material

def emission_material(code):
    material = bpy.data.materials.new('PixelForge ID '+str(code))
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    emission = nodes.new('ShaderNodeEmission')
    output = nodes.new('ShaderNodeOutputMaterial')
    emission.inputs['Color'].default_value = ((code&255)/255,((code>>8)&255)/255,((code>>16)&255)/255,1)
    material.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
    return material

def assign(obj, materials):
    indices = [polygon.material_index for polygon in obj.data.polygons]
    obj.data.materials.clear()
    for material in materials:
        obj.data.materials.append(material)
    for polygon,index in zip(obj.data.polygons,indices):
        polygon.material_index = min(index,max(0,len(materials)-1))

def render(scene, path):
    bpy.context.window.scene = scene
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

def ground_passes(scene, objects, light, job, directory, ground_z, radius):
    # Paired matte receiver renders are reliable across Cycles versions and avoid
    # compositor/shadow-catcher alpha semantics. The plane never enters the beauty render.
    mesh = bpy.data.meshes.new('PixelForge shadow receiver')
    mesh.from_pydata([(-radius,-radius,ground_z),(radius,-radius,ground_z),(radius,radius,ground_z),(-radius,radius,ground_z)],[],[(0,1,2,3)])
    plane = bpy.data.objects.new('PixelForge ground receiver',mesh)
    scene.collection.objects.link(plane)
    white = matte({'name':'receiver','color':(1,1,1,1)},dict(job,ShadowIntensity=1))
    mesh.materials.append(white)
    for obj in objects:
        obj.visible_camera = False
        obj.visible_diffuse = False
        obj.visible_glossy = False
        obj.visible_shadow = True
    light.data.angle = job['GroundShadowSoftness']
    # Receiver coverage needs enough samples to form clean pixel-grid penumbra
    # bands. Beauty lighting/sampling was already rendered and is unchanged.
    previous_samples = scene.cycles.samples
    if job['GroundShadowSoftness']>0:
        scene.cycles.samples = 128
    render(scene,os.path.join(directory,'ground-shadowed.png'))
    for obj in objects:
        obj.hide_render = True
    render(scene,os.path.join(directory,'ground-clear.png'))
    scene.cycles.samples = previous_samples
    bpy.data.objects.remove(plane,do_unlink=True)
    bpy.data.meshes.remove(mesh)
    bpy.data.materials.remove(white)
