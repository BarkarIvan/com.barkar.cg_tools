# bake_lod.py (Blender 3.6+ / 4.x)
import bpy
import sys
import os

def _arg(name, default=None):
    argv = sys.argv
    if "--" not in argv:
        return default
    argv = argv[argv.index("--") + 1:]
    for i in range(len(argv) - 1):
        if argv[i] == name:
            return argv[i + 1]
    return default

def _clean_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)

    # Purge data blocks (best-effort)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)
    for block in list(bpy.data.images):
        bpy.data.images.remove(block)

def _import_obj(path, name_hint):
    if not os.path.isfile(path):
        raise RuntimeError(f"OBJ not found: {path}")

    before = set(bpy.data.objects)

    # Blender 4.x
    try:
        bpy.ops.wm.obj_import(filepath=path, forward_axis='-Z', up_axis='Y')
    except Exception:
        # Blender 2.9x/3.x
        bpy.ops.import_scene.obj(filepath=path, axis_forward='-Z', axis_up='Y')

    after = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in after if o.type == 'MESH']
    if not meshes:
        raise RuntimeError(f"No mesh objects imported from: {path}")

    # If multiple mesh objects -> join into one
    obj = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.select_all(action='DESELECT')
        for o in meshes:
            o.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.join()
        obj = bpy.context.view_layer.objects.active

    obj.name = name_hint
    return obj

def _ensure_uv(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    if not obj.data.uv_layers:
        obj.data.uv_layers.new(name="UVMap")

    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')

    # Smart UV Project: хороший baseline для "любых" lowpoly
    bpy.ops.uv.smart_project(angle_limit=66.0, island_margin=0.02, area_weight=0.0)

    bpy.ops.object.mode_set(mode='OBJECT')

def _set_cycles_for_bake(samples=32):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = samples
    scene.cycles.use_adaptive_sampling = True

    # Bake common settings
    scene.render.bake.use_selected_to_active = True
    scene.render.bake.use_clear = True
    scene.render.bake.margin = 8

def _make_bake_material(obj, image, image_node_name="BakeTarget"):
    mat = bpy.data.materials.new(name="BakeMat")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links

    # Clear nodes
    for n in list(nodes):
        nodes.remove(n)

    out = nodes.new(type="ShaderNodeOutputMaterial")
    bsdf = nodes.new(type="ShaderNodeBsdfPrincipled")
    img = nodes.new(type="ShaderNodeTexImage")
    img.image = image
    img.name = image_node_name

    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    # Important: active image node for baking
    nodes.active = img

    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return img

def _select_for_bake(high_obj, low_obj):
    bpy.ops.object.select_all(action='DESELECT')
    high_obj.select_set(True)
    low_obj.select_set(True)
    bpy.context.view_layer.objects.active = low_obj  # active = low

def _bake_normal(high_obj, low_obj, out_path, tex_size, cage_extrusion):
    scene = bpy.context.scene

    scene.render.bake.cage_extrusion = cage_extrusion
    scene.render.bake.use_cage = False

    img = bpy.data.images.new("NormalBake", width=tex_size, height=tex_size, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"

    _make_bake_material(low_obj, img, "NormalTarget")
    _select_for_bake(high_obj, low_obj)

    # Tangent-space normal
    scene.render.bake.normal_space = 'TANGENT'
    bpy.ops.object.bake(type='NORMAL')

    img.filepath_raw = out_path
    img.file_format = 'PNG'
    img.save()

def _bake_ao(high_obj, low_obj, out_path, tex_size, cage_extrusion):
    scene = bpy.context.scene

    scene.render.bake.cage_extrusion = cage_extrusion
    scene.render.bake.use_cage = False

    img = bpy.data.images.new("AOBake", width=tex_size, height=tex_size, alpha=False, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"

    _make_bake_material(low_obj, img, "AOTarget")
    _select_for_bake(high_obj, low_obj)

    bpy.ops.object.bake(type='AO')

    img.filepath_raw = out_path
    img.file_format = 'PNG'
    img.save()

def _export_obj(obj, out_path):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    # Blender 4.x
    try:
        bpy.ops.wm.obj_export(filepath=out_path, export_selected_objects=True, forward_axis='-Z', up_axis='Y')
    except Exception:
        # Older Blender
        bpy.ops.export_scene.obj(
            filepath=out_path,
            use_selection=True,
            axis_forward='-Z',
            axis_up='Y',
            use_materials=False,
            keep_vertex_order=True
        )

def main():
    high_path = _arg("--high")
    low_path  = _arg("--low")
    out_dir   = _arg("--out")

    tex_size  = int(_arg("--texSize", "2048"))
    cage      = float(_arg("--cage", "0.02"))
    samples   = int(_arg("--samples", "32"))

    if not high_path or not low_path or not out_dir:
        raise RuntimeError("Usage: -- --high <path> --low <path> --out <dir> [--texSize 2048] [--cage 0.02] [--samples 32]")

    os.makedirs(out_dir, exist_ok=True)

    _clean_scene()
    high = _import_obj(high_path, "HIGH")
    low  = _import_obj(low_path, "LOW")

    _ensure_uv(low)
    _set_cycles_for_bake(samples=samples)

    normal_png = os.path.join(out_dir, "normal.png")
    ao_png     = os.path.join(out_dir, "ao.png")
    low_out    = os.path.join(out_dir, "low_unwrapped.obj")

    _bake_normal(high, low, normal_png, tex_size, cage)
    _bake_ao(high, low, ao_png, tex_size, cage)
    _export_obj(low, low_out)

    print("DONE")
    print("LOW:", low_out)
    print("NORMAL:", normal_png)
    print("AO:", ao_png)

if __name__ == "__main__":
    main()
