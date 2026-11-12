"""Read-only collection membership. Object IDs and evaluated meshes stay separate."""
PREFIX = 'PF_Outline_'

def resolve(scene, registry, ids):
    candidates = {}
    def visit(collection, path):
        path = path + (collection.name,)
        if collection.name.startswith(PREFIX) and len(collection.name)>len(PREFIX):
            for obj in collection.all_objects:
                if obj.name in ids:
                    candidates.setdefault(obj.name,set()).add(path)
        for child in sorted(collection.children,key=lambda c:c.name):
            visit(child,path)
    visit(scene.collection,())
    chosen, warnings = {}, []
    for name,paths in sorted(candidates.items()):
        ordered = sorted(paths,key=lambda p:(-len(p),p))
        chosen[name] = ordered[0]
        if len(ordered)>1:
            warnings.append('Outline group ambiguity for '+name+': '+', '.join('/'.join(p) for p in ordered)+
                            '. Using '+'/'.join(ordered[0])+' (deepest collection, then ordinal path).')
    members = {}
    for name,path in chosen.items(): members.setdefault(path,[]).append(ids[name])
    canonical = {path:min(values) for path,values in members.items()}
    if not members:
        warnings.append('No PF_Outline_ collections containing renderable objects were found; per-object outline rules are unchanged.')
    return {'policy':'deepest collection, then ordinal full path',
            'objects':{str(ids[name]):{'groupId':canonical[path],'collection':'/'.join(path)} for name,path in chosen.items()},
            'groups':{'/'.join(path):sorted(values) for path,values in sorted(members.items())}},warnings
