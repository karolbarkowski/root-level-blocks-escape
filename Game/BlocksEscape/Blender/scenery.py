# Generates the scenery and in-world UI props around the board: trees, bushes, rocks, flowers, grass, a pond, a barn
# corner, a bucket, a water tub, a butterfly, the level sign, the back button, and the menu's planks. Text on them is
# set in Unity with the game font (TextMeshPro), not modelled.
# Run inside Blender:  exec(open(r"...\Blender\scenery.py").read()); build_all()   (or build("Tree"), export_all())
#
# Blender units: 1 m = 1 cell. Pivot = centre of the prop's footprint on the ground (z = 0). Fronts face -Y, which is
# towards the camera (the bottom of the screen) in Unity after export. Props are modelled upright; the level sign is
# tilted towards the camera in the scene.
# Every prop shares one palette texture: each part is UV-mapped to the centre of its colour swatch, so all props use one
# material. Exports go to Assets/Prefabs/Scenery/; then run "Blocks Escape/Build Scenery From Blender" in Unity.
import bpy, bmesh, math, random, os, numpy as np
from mathutils import Vector, Matrix

GAME = r"D:\Dev\RootLevel.It\root-level-blocks-escape\Game\BlocksEscape"
OUT = os.path.join(GAME, "Assets", "Prefabs", "Scenery")
SCENE = "Scenery"

# ----------------------------------------------------------------------- palette
PALETTE = [
    ("wood_light", "#D29A62"), ("wood", "#A86B3D"), ("wood_dark", "#7A4A26"), ("cream", "#FFF1D6"),
    ("leaf", "#62A93C"), ("leaf_light", "#86C64C"), ("leaf_dark", "#468A2E"), ("trunk", "#8A5A36"),
    ("rock", "#A9A79F"), ("rock_light", "#C9C6BC"), ("water", "#62BEE6"), ("water_deep", "#3F9CCB"),
    ("lily", "#5DAA3E"), ("white", "#FFFFFF"), ("pink", "#FFAFC8"), ("yellow", "#FFD84A"),
    ("orange", "#F5A623"), ("barn_red", "#C8443A"), ("barn_trim", "#F6F0E4"), ("roof", "#7E7F88"),
    ("metal", "#BCC4CC"), ("metal_dark", "#848D96"), ("wing", "#F28C28"), ("dark", "#3A2618"),
    ("reed", "#6F9E3A"), ("soil", "#B98A5A"), ("lilac", "#C9B6F2"), ("red", "#E25A4A"),
    ("sign_face", "#F4CB90"), ("sign_frame", "#CC8E58"), ("letter_edge", "#6B3E22"),
    ("sleep", "#BFDDF7"), ("sleep_rim", "#6F95C9"), ("sleep_z", "#3D5A8C"),
]
GRID = 8; SW = 8  # 8x8 swatches of 8 px -> 64 px texture

def hexc(h):
    h = h.lstrip('#'); return [int(h[i:i+2], 16) / 255 for i in (0, 2, 4)]

def swatch_uv(name):
    i = [n for n, _ in PALETTE].index(name)
    return ((i % GRID + .5) / GRID, (i // GRID + .5) / GRID)

def palette_image():
    px = np.zeros((GRID*SW, GRID*SW, 4), np.float32); px[..., 3] = 1
    for i, (_, h) in enumerate(PALETTE):
        x, y = i % GRID, i // GRID
        px[y*SW:(y+1)*SW, x*SW:(x+1)*SW, :3] = hexc(h)
    name = "Scenery_Palette"
    old = bpy.data.images.get(name)
    if old: bpy.data.images.remove(old)
    im = bpy.data.images.new(name, GRID*SW, GRID*SW, alpha=False)
    im.pixels.foreach_set(px.ravel())
    os.makedirs(OUT, exist_ok=True)
    im.filepath_raw = os.path.join(OUT, "Scenery_Palette.png"); im.file_format = 'PNG'; im.save(); im.pack()
    return im

def palette_material():
    mat = bpy.data.materials.get("Scenery") or bpy.data.materials.new("Scenery")
    mat.use_nodes = True; nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"); bsdf.inputs["Roughness"].default_value = 0.7
    tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.get("Scenery_Palette") or palette_image(); tex.interpolation = 'Closest'
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return mat

# ----------------------------------------------------------------------- scene helpers
def scene():
    sc = bpy.data.scenes.get(SCENE) or bpy.data.scenes.new(SCENE)
    if bpy.context.window and bpy.context.window.scene != sc: bpy.context.window.scene = sc
    return sc

def collection(name):
    sc = scene()
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name); sc.collection.children.link(col)
    for ob in list(col.objects): bpy.data.objects.remove(ob, do_unlink=True)
    return col

def link(ob):
    for c in list(ob.users_collection): c.objects.unlink(ob)
    scene().collection.objects.link(ob)
    return ob

def paint(ob, color):
    """Point every UV of the mesh at the colour's swatch."""
    me = ob.data
    if not me.uv_layers: me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data; u, v = swatch_uv(color)
    for l in uv: l.uv = (u, v)
    return ob

def finish(ob, color, smooth=True, subsurf=0, bevel=0.0, bevel_segments=3):
    """Apply modifiers, shade, colour."""
    if bevel > 0:
        m = ob.modifiers.new("Bevel", 'BEVEL'); m.width = bevel; m.segments = bevel_segments; m.limit_method = 'NONE'
    if subsurf:
        m = ob.modifiers.new("Subsurf", 'SUBSURF'); m.levels = subsurf; m.render_levels = subsurf
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        for m in list(ob.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)
    for p in ob.data.polygons: p.use_smooth = smooth
    return paint(ob, color)

def mesh_object(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    return link(bpy.data.objects.new(name, me))

def sphere(color, loc, scale, r=1.0, subdiv=3, rough=0.0, seed=0):
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=r)
    if rough:
        rng = random.Random(seed)
        for v in bm.verts: v.co *= 1 + rng.uniform(-rough, rough)
    ob = mesh_object("sphere", bm)
    ob.scale = scale; ob.location = loc
    return finish(ob, color)

def box(color, loc, size, bevel=0.05, rot=(0, 0, 0), segments=3):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    ob = mesh_object("box", bm)
    ob.scale = size; ob.location = loc; ob.rotation_euler = rot
    apply_transform(ob)
    return finish(ob, color, bevel=bevel, bevel_segments=segments)

def cylinder(color, loc, r, h, r2=None, verts=20, bevel=0.03, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r if r2 is None else r2, depth=h,
                          matrix=Matrix.Translation((0, 0, h/2)))
    ob = mesh_object("cyl", bm)
    ob.location = loc; ob.rotation_euler = rot
    apply_transform(ob)
    return finish(ob, color, bevel=bevel)

def disc(color, loc, r, h, verts=24, noise=0.0, seed=0, scale=(1, 1, 1)):
    bm = bmesh.new()
    rng = random.Random(seed)
    ring = []
    for i in range(verts):
        a = i / verts * 2 * math.pi; k = r * (1 + rng.uniform(-noise, noise))
        ring.append(bm.verts.new((math.cos(a)*k*scale[0], math.sin(a)*k*scale[1], 0)))
    f = bm.faces.new(ring)
    bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in bm.verts:
        if v not in ring: v.co.z = h
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = mesh_object("disc", bm)
    ob.location = loc
    return finish(ob, color, bevel=min(h*0.45, 0.02), bevel_segments=2)

def apply_transform(ob):
    ob.data.transform(ob.matrix_basis); ob.matrix_basis = Matrix.Identity(4)

def join(name, parts, col):
    for ob in parts: apply_transform(ob)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in parts: ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    ob = parts[0]; ob.name = name; ob.data.name = name
    if not ob.data.materials: ob.data.materials.append(palette_material())
    for c in list(ob.users_collection): c.objects.unlink(ob)
    col.objects.link(ob)
    return ob

# ----------------------------------------------------------------------- small pieces
def flower(loc, petal, r=0.1, n=5, rot=0.0, centre="yellow"):
    x, y, z = loc; parts = []
    for i in range(n):
        a = rot + i / n * 2 * math.pi
        parts.append(sphere(petal, (x + math.cos(a)*r*0.95, y + math.sin(a)*r*0.95, z), (r*0.75, r*0.75, r*0.35), subdiv=2))
    parts.append(sphere(centre, (x, y, z + r*0.15), (r*0.55, r*0.55, r*0.4), subdiv=2))
    return parts

def leaf(loc, length, angle, color="leaf"):
    x, y, z = loc
    ob = sphere(color, (x + math.cos(angle)*length*0.5, y + math.sin(angle)*length*0.5, z), (length*0.5, length*0.2, 0.03), subdiv=2)
    ob.rotation_euler = (0, 0, angle)
    return ob

# ----------------------------------------------------------------------- props
def tree(col, seed=1):
    rng = random.Random(seed); parts = [cylinder("trunk", (0, 0, 0), 0.26, 1.5, r2=0.18, verts=14, bevel=0.04)]
    for i in range(7):
        a = i / 7 * 2 * math.pi + rng.uniform(-.3, .3); d = 0 if i == 0 else rng.uniform(.45, .7)
        r = rng.uniform(.62, .8) if i else 0.95
        c = ["leaf", "leaf_light", "leaf_dark"][rng.randrange(3)] if i else "leaf"
        parts.append(sphere(c, (math.cos(a)*d, math.sin(a)*d, 1.9 + rng.uniform(-.15, .35) + (0.3 if i == 0 else 0)),
                            (r, r, r*0.85), rough=0.04, seed=seed*10+i))
    return join("Tree", parts, col)

def bush(col, seed=2, name="Bush"):
    rng = random.Random(seed); parts = []
    for i in range(4):
        a = i / 4 * 2 * math.pi + rng.uniform(-.4, .4); d = rng.uniform(.18, .32) if i else 0
        r = rng.uniform(.32, .42) if i else .48
        parts.append(sphere(["leaf", "leaf_light", "leaf_dark"][i % 3], (math.cos(a)*d, math.sin(a)*d, r*0.75), (r, r, r*0.85),
                            rough=0.05, seed=seed*10+i))
    return join(name, parts, col)

def flowering_bush(col, seed=3, petal="white", name="FlowerBush"):
    rng = random.Random(seed)
    base = bush(col, seed, "_tmp")
    parts = [link(base)]
    for i in range(5):
        a = rng.uniform(0, 2*math.pi); d = rng.uniform(.1, .38)
        parts += flower((math.cos(a)*d, math.sin(a)*d, 0.72 + rng.uniform(-.08, .08) - d*0.35), petal, r=0.075, rot=rng.uniform(0, 1))
    return join(name, parts, col)

def rock(col, seed=4, name="Rock", scale=(1, .8, .55)):
    parts = [sphere("rock", (0, 0, 0.18*scale[2]/0.55), (0.45*scale[0], 0.45*scale[1], 0.45*scale[2]), subdiv=2, rough=0.12, seed=seed)]
    parts.append(sphere("rock_light", (-0.08, 0.06, 0.32*scale[2]/0.55), (0.25*scale[0], 0.22*scale[1], 0.15*scale[2]), subdiv=2, rough=0.1, seed=seed+1))
    return join(name, parts, col)

def flower_clump(col, petal, seed=5, name="Flowers"):
    rng = random.Random(seed); parts = []
    for i in range(6):
        parts.append(leaf((0, 0, 0.03), rng.uniform(.25, .35), i / 6 * 2 * math.pi + rng.uniform(-.2, .2),
                          ["leaf", "leaf_dark"][i % 2]))
    for i in range(3):
        a = i / 3 * 2 * math.pi + rng.uniform(-.3, .3); d = rng.uniform(.08, .16)
        parts += flower((math.cos(a)*d, math.sin(a)*d, 0.14 + rng.uniform(0, .08)), petal, r=0.085, rot=rng.uniform(0, 1))
    return join(name, parts, col)

def grass(col, seed=6, name="Grass"):
    rng = random.Random(seed); parts = []
    for i in range(7):
        a = i / 7 * 2 * math.pi + rng.uniform(-.3, .3); lean = rng.uniform(.25, .5)
        h = rng.uniform(.28, .42)
        ob = cylinder(["leaf", "leaf_light", "leaf_dark"][i % 3], (0, 0, 0), 0.05, h, r2=0.004, verts=6, bevel=0.0)
        ob.rotation_euler = (lean*math.cos(a + math.pi/2), lean*math.sin(a + math.pi/2), 0)
        ob.location = (math.cos(a)*.05, math.sin(a)*.05, 0)
        parts.append(ob)
    return join(name, parts, col)

def pond(col, seed=7):
    rng = random.Random(seed); parts = []
    parts.append(disc("leaf_dark", (0, 0, 0.0), 1.75, 0.02, verts=32, noise=0.06, seed=seed, scale=(1.25, 1)))
    parts.append(disc("water_deep", (0, 0, 0.012), 1.55, 0.02, verts=32, noise=0.07, seed=seed+1, scale=(1.25, 1)))
    parts.append(disc("water", (0.12, 0.1, 0.02), 1.25, 0.02, verts=32, noise=0.08, seed=seed+2, scale=(1.2, 0.95)))
    for i in range(11):  # rocks along the near rim
        a = math.pi*0.95 + i / 10 * math.pi*1.05 + rng.uniform(-.06, .06)
        rr = rng.uniform(.18, .32)
        parts.append(sphere("rock" if i % 3 else "rock_light", (math.cos(a)*1.72*1.25, math.sin(a)*1.72, rr*0.35),
                            (rr, rr*0.85, rr*0.6), subdiv=2, rough=0.1, seed=seed*20+i))
    for i, (x, y, r) in enumerate([(-0.5, 0.35, .28), (0.35, -0.2, .22), (0.9, 0.5, .24), (-0.1, -0.55, .2)]):
        parts.append(disc("lily", (x, y, 0.035), r, 0.012, verts=16, seed=i))
    parts += flower((-0.45, 0.42, 0.07), "pink", r=0.08, rot=0.3)
    for i in range(6):  # reeds on the far bank
        a = math.pi*0.25 + i * 0.12; d = 1.62
        parts.append(cylinder("reed", (math.cos(a)*d*1.25, math.sin(a)*d, 0), 0.03, rng.uniform(.5, .8), r2=0.01, verts=6, bevel=0))
    return join("Pond", parts, col)

def barn(col):
    parts = []
    parts.append(box("barn_red", (0, 0, 1.2), (3.2, 2.6, 2.4), bevel=0.06))
    for x in (-1.6, 1.6):  # corner trims
        parts.append(box("barn_trim", (x, -1.3, 1.2), (0.18, 0.18, 2.45), bevel=0.04))
    parts.append(box("barn_trim", (0, -1.32, 0.12), (3.3, 0.12, 0.18), bevel=0.03))
    # doorway: dark inside, framed; the two door leaves are separate pieces (barn_door) so they can swing open
    parts.append(box("dark", (0, -1.31, 0.8), (1.3, 0.06, 1.6), bevel=0.02))
    for x in (-0.65, 0.65): parts.append(box("barn_trim", (x, -1.39, 0.8), (0.12, 0.08, 1.62), bevel=0.02))
    for z in (0.02, 1.58): parts.append(box("barn_trim", (0, -1.39, z), (1.42, 0.08, 0.12), bevel=0.02))
    # gable roof
    for s in (-1, 1):
        parts.append(box("roof", (s*0.95, 0, 2.85), (2.1, 2.9, 0.18), bevel=0.04, rot=(0, s*math.radians(38), 0)))
    parts.append(box("barn_trim", (0, 0, 3.5), (0.3, 2.95, 0.2), bevel=0.05))
    barn = join("Barn", parts, col)
    for side, name in ((-1, "Barn_DoorL"), (1, "Barn_DoorR")): barn_door(col, name, side)
    return barn

def barn_door(col, name, side):
    """One leaf of the barn's double door with its own white X, in barn coordinates; it hinges on its outer edge
    (x = side*0.6). Exported as <Barn>_<Part>, which the Unity setup hangs on the barn instead of making a prefab."""
    cx = side*0.31; parts = []
    parts.append(box("barn_red", (cx, -1.36, 0.8), (0.58, 0.08, 1.5), bevel=0.02))
    for d in (-1, 1):
        parts.append(box("barn_trim", (cx, -1.41, 0.8), (1.5, 0.05, 0.09), bevel=0.015, rot=(0, d*math.atan(1.4/0.5), 0)))
    for z in (0.1, 1.5): parts.append(box("barn_trim", (cx, -1.41, z), (0.58, 0.05, 0.09), bevel=0.015))
    parts.append(box("barn_trim", (side*0.04, -1.41, 0.8), (0.07, 0.05, 1.5), bevel=0.015))  # meeting edge
    return join(name, parts, col)

def ring(color, loc, r, thickness, verts=24):
    bpy.ops.mesh.primitive_torus_add(major_radius=r, minor_radius=thickness, major_segments=verts, minor_segments=8,
                                     location=loc)
    ob = link(bpy.context.active_object)
    return finish(ob, color)

def bucket(col):
    # Body stops below the rim so the water shows from above; the rim is a ring.
    parts = [cylinder("metal", (0, 0, 0), 0.22, 0.32, r2=0.26, verts=20, bevel=0.02),
             disc("water", (0, 0, 0.3), 0.25, 0.03, verts=20),
             ring("metal_dark", (0, 0, 0.34), 0.26, 0.03, verts=20)]
    return join("Bucket", parts, col)

def tub(col):
    parts = [cylinder("wood", (0, 0, 0), 0.62, 0.38, r2=0.66, verts=24, bevel=0.03),
             disc("water", (0, 0, 0.38), 0.62, 0.03, verts=24),
             ring("wood_dark", (0, 0, 0.44), 0.64, 0.06, verts=24)]
    for z in (0.1, 0.36): parts.append(cylinder("metal_dark", (0, 0, z), 0.645 + z*0.08, 0.05, verts=24, bevel=0.015))
    for i in range(12):  # staves
        a = i / 12 * 2 * math.pi
        parts.append(box("wood_dark", (math.cos(a)*0.64, math.sin(a)*0.64, 0.24), (0.025, 0.06, 0.46), bevel=0.01, rot=(0, 0, a)))
    return join("Tub", parts, col)

def butterfly(col):
    parts = [sphere("dark", (0, 0, 0.05), (0.03, 0.12, 0.03), subdiv=2)]
    for s in (-1, 1):
        parts.append(sphere("wing", (s*0.12, 0.05, 0.06), (0.12, 0.1, 0.012), subdiv=2))
        parts.append(sphere("wing", (s*0.09, -0.08, 0.06), (0.08, 0.07, 0.012), subdiv=2))
        parts.append(sphere("dark", (s*0.2, 0.09, 0.065), (0.035, 0.035, 0.01), subdiv=1))
    return join("Butterfly", parts, col)

SIGN_W, SIGN_H, SIGN_Z = 4.6, 1.45, 1.15  # plank width, height and centre height; LevelSign centres the number there

def sign(col):
    W, H, Z = SIGN_W, SIGN_H, SIGN_Z
    parts = []
    for x in (-W/2 + 0.15, W/2 - 0.15):
        parts.append(cylinder("wood", (x, 0.06, 0), 0.18, Z + H/2 + 0.12, verts=14, bevel=0.05))
        parts.append(sphere("wood", (x, 0.06, Z + H/2 + 0.12), (0.18, 0.18, 0.11), subdiv=2))
    parts.append(box("sign_frame", (0, 0, Z), (W, 0.26, H), bevel=0.12, segments=4))
    parts.append(box("sign_face", (0, -0.1, Z), (W - 0.3, 0.1, H - 0.28), bevel=0.06))  # lighter face panel
    for x in (-W/2 + 0.45, W/2 - 0.45):
        f = flower((0, 0, 0), "white", r=0.14, rot=0.4)
        for ob in f:
            ob.location = Vector((ob.location.x + x, -0.16, ob.location.z + Z + ob.location.y)); ob.rotation_euler = (math.pi/2, 0, 0)
        parts += f
    for i, (x, a) in enumerate([(-W/2 + 0.15, -0.6), (W/2 - 0.15, 3.6)]):  # leaves at the posts
        for k in range(3):
            parts.append(leaf((x, -0.05, 0.02), 0.32 + k*0.05, a + k*0.6, ["leaf", "leaf_dark", "leaf_light"][k]))
    return join("Sign", parts, col)

def back_button(col):
    parts = [box("wood", (0, 0, 0.14), (1.0, 1.0, 0.28), bevel=0.14, segments=4),
             box("wood_light", (0, 0, 0.27), (0.82, 0.82, 0.06), bevel=0.06, segments=3)]
    # arrow pointing left (-X): triangle head + shaft, extruded and softened
    pts = [(-0.3, 0), (-0.02, 0.26), (-0.02, 0.11), (0.28, 0.11), (0.28, -0.11), (-0.02, -0.11), (-0.02, -0.26)]
    bm = bmesh.new(); verts = [bm.verts.new((x, y, 0)) for x, y in pts]
    f = bm.faces.new(verts); r = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)]: v.co.z = 0.08
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = mesh_object("arrow", bm); ob.location = (0, 0, 0.29)
    parts.append(finish(ob, "cream", smooth=False, bevel=0.025, bevel_segments=2))
    return join("BackButton", parts, col)

def glyph(text, color, offset, extrude, bevel, y):
    """One standing glyph mesh (X/Z, front facing -Y) at depth y, pivot at baseline centre."""
    font = bpy.data.fonts.load("<builtin>", check_existing=True)
    cu = bpy.data.curves.new("glyph", 'FONT'); cu.body = text; cu.font = font
    cu.size = 1.0; cu.offset = offset; cu.extrude = extrude; cu.bevel_depth = bevel; cu.bevel_resolution = 3
    cu.align_x = 'CENTER'
    ob = link(bpy.data.objects.new("glyph", cu))
    bpy.context.view_layer.update()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); bpy.data.curves.remove(cu)
    mo = link(bpy.data.objects.new("glyph", me))
    mo.rotation_euler = (math.pi/2, 0, 0); mo.location = (0, y, 0); apply_transform(mo)
    return finish(mo, color, smooth=True)

def menu_plank(col):
    """Menu button: a wide plank lying on the ground like the back button (top at z = 0.30); WorldLabel lays the
    word on top of it."""
    return join("MenuPlank", [box("wood", (0, 0, 0.14), (3.6, 1.0, 0.28), bevel=0.14, segments=4),
                              box("wood_light", (0, 0, 0.27), (3.42, 0.82, 0.06), bevel=0.06, segments=3)], col)

def sleep_bubble(col):
    """Badge floating over a frozen animal: a soft blue disc standing up (front -Y, pivot at its centre), with
    "z z" rising off its upper right; Block puts the turns-to-wake digits on its front."""
    parts = []
    rim = cylinder("sleep_rim", (0, 0.03, 0), 0.5, 0.1, verts=32, bevel=0.04, rot=(math.pi/2, 0, 0))
    face = cylinder("sleep", (0, -0.02, 0), 0.43, 0.06, verts=32, bevel=0.025, rot=(math.pi/2, 0, 0))
    parts += [rim, face]
    for size, x, z in ((0.32, 0.5, 0.42), (0.24, 0.72, 0.68)):
        zg = glyph("z", "sleep_z", 0.03, 0.03, 0.015, -0.02)
        zg.scale = (size, size, size); zg.location = (x, 0, z)
        parts.append(zg)
    return join("SleepBubble", parts, col)

def axis_arrow(col):
    """Flat double-headed arrow along X lying on the ground plane (pivot at its centre, bottom at z = 0): cream on a
    fatter dark-brown base, like the lettering. Block lays it on top of axis-locked blocks."""
    def arrow(color, grow, z, h):
        L, head, shaft = 0.42 + grow, 0.16 + grow, 0.07 + grow
        hw = 0.17 + grow
        pts = [(-L, 0), (-L + head, hw), (-L + head, shaft), (L - head, shaft), (L - head, hw), (L, 0),
               (L - head, -hw), (L - head, -shaft), (-L + head, -shaft), (-L + head, -hw)]
        bm = bmesh.new(); verts = [bm.verts.new((x, y, 0)) for x, y in pts]
        f = bm.faces.new(verts); r = bmesh.ops.extrude_face_region(bm, geom=[f])
        for v in [e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)]: v.co.z = h
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        ob = mesh_object("arrow", bm); ob.location = (0, 0, z)
        return finish(ob, color, smooth=False, bevel=min(0.02, h*0.45), bevel_segments=2)
    return join("AxisArrow", [arrow("letter_edge", 0.045, 0.0, 0.05), arrow("cream", 0.0, 0.03, 0.05)], col)

BUILDERS = {
    "Tree": lambda c: tree(c, 1), "Tree2": lambda c: _rename(tree(c, 11), "Tree2"),
    "Bush": lambda c: bush(c, 2), "Bush2": lambda c: bush(c, 12, "Bush2"),
    "FlowerBushWhite": lambda c: flowering_bush(c, 3, "white", "FlowerBushWhite"),
    "FlowerBushPink": lambda c: flowering_bush(c, 13, "pink", "FlowerBushPink"),
    "Rock": lambda c: rock(c, 4), "RockFlat": lambda c: rock(c, 14, "RockFlat", (1.2, .9, .35)),
    "FlowersWhite": lambda c: flower_clump(c, "white", 5, "FlowersWhite"),
    "FlowersPink": lambda c: flower_clump(c, "pink", 15, "FlowersPink"),
    "FlowersYellow": lambda c: flower_clump(c, "yellow", 25, "FlowersYellow"),
    "FlowersLilac": lambda c: flower_clump(c, "lilac", 35, "FlowersLilac"),
    "Grass": lambda c: grass(c, 6), "Grass2": lambda c: grass(c, 16, "Grass2"),
    "Pond": pond, "Barn": barn, "Bucket": bucket, "Tub": tub, "Butterfly": butterfly,
    "Sign": sign, "BackButton": back_button, "MenuPlank": menu_plank, "SleepBubble": sleep_bubble, "AxisArrow": axis_arrow,
}

def _rename(ob, name): ob.name = name; ob.data.name = name; return ob

def build(name):
    scene(); palette_material()
    col = collection(name)
    return BUILDERS[name](col)

def export(name):
    os.makedirs(OUT, exist_ok=True)
    for ob in bpy.data.collections[name].objects:
        loc = ob.location.copy(); ob.location = (0, 0, 0)  # pivot at the origin in the FBX
        try: export_fbx(ob, os.path.join(OUT, ob.name + ".fbx"))
        finally: ob.location = loc

def export_fbx(ob, path):
    # Same convention as animals.py: the FBX axis conversion maps Blender +Y to Unity -Z, so the mesh is turned 180° for
    # the export to keep Blender -Y (the prop's front) facing Unity -Z, towards the camera.
    turn = Matrix.Rotation(math.pi, 4, 'Z')
    ob.data.transform(turn)
    try:
        bpy.ops.object.select_all(action='DESELECT')
        ob.hide_set(False); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, global_scale=1.0,
            apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', use_space_transform=True,
            bake_space_transform=True, axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE',
            use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode='STRIP')
    finally:
        ob.data.transform(turn.inverted())

def build_all(do_export=True):
    palette_image()
    for name in BUILDERS:
        build(name)
        if do_export: export(name)
    print("built", len(BUILDERS))
