# -*- coding: utf-8 -*-
"""
BACKYARD BET - full procedural scene + animation set for the party game.

Builds, in one pass:
  * a 76x76 m fenced backyard with a cottage and an attached bowling annex
  * every mini-game rig: bluff table, darts, billiards, beer pong,
    axe throwing, slingshot cans, bowling lane, potato cannon, piranha pool
  * four cartoon characters with swingable arms
  * nine animated segments (1090 frames @ 30 fps) with bound cameras

Run:  blender --background --python backyard_bet.py
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix, Quaternion, noise

SEED = 7
random.seed(SEED)
OUT = "D:/cloudi/blender"
TAU = math.tau

# ============================================================ mesh helpers
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def M(name, color, rough=0.75, metallic=0.0, spec=0.12, alpha=1.0, emit=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    def s(k, v):
        if k in b.inputs: b.inputs[k].default_value = v
    s("Base Color", (color[0], color[1], color[2], 1.0))
    s("Roughness", rough); s("Metallic", metallic)
    s("Specular IOR Level", spec); s("Specular", spec)
    s("Alpha", alpha)
    if emit:
        s("Emission Color", (emit[0], emit[1], emit[2], 1.0))
        s("Emission Strength", emit[3])
    return m

def M_patchy(name, c1, c2, scale=5.0, rough=0.9):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexNoise"); tex.location = (-700, 0)
    tex.inputs["Scale"].default_value = scale
    if "Detail" in tex.inputs: tex.inputs["Detail"].default_value = 2.0
    ramp = nt.nodes.new("ShaderNodeValToRGB"); ramp.location = (-450, 0)
    ramp.color_ramp.interpolation = 'EASE'
    ramp.color_ramp.elements[0].position = 0.38
    ramp.color_ramp.elements[0].color = (c1[0], c1[1], c1[2], 1)
    ramp.color_ramp.elements[1].position = 0.62
    ramp.color_ramp.elements[1].color = (c2[0], c2[1], c2[2], 1)
    nt.links.new(tex.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = rough
    for k in ("Specular IOR Level", "Specular"):
        if k in b.inputs: b.inputs[k].default_value = 0.10
    return m

def finalize(ob, smooth=True, angle=35.0):
    if not smooth:
        for p in ob.data.polygons: p.use_smooth = False
        return
    for p in ob.data.polygons: p.use_smooth = True
    try:
        es = ob.modifiers.new("es", 'EDGE_SPLIT')
        es.use_edge_angle = True; es.use_edge_sharp = False
        es.split_angle = math.radians(angle)
    except Exception:
        for p in ob.data.polygons: p.use_smooth = False

def obj_from(bm, name, mat=None, loc=(0, 0, 0), rot=(0, 0, 0), scale=1.0,
             smooth=True, angle=35.0):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    ob.location = loc; ob.rotation_euler = rot
    ob.scale = (scale, scale, scale) if isinstance(scale, (int, float)) else scale
    if mat:
        if isinstance(mat, (list, tuple)):
            for m in mat: me.materials.append(m)
        else:
            me.materials.append(mat)
    finalize(ob, smooth, angle)
    return ob

def dup(proto, loc, rot=(0, 0, 0), scale=1.0, smooth=True, angle=35.0):
    ob = bpy.data.objects.new(proto.name + ".d", proto.data)
    bpy.context.collection.objects.link(ob)
    ob.location = loc; ob.rotation_euler = rot
    ob.scale = (scale, scale, scale) if isinstance(scale, (int, float)) else scale
    finalize(ob, smooth, angle)
    return ob

def bevel_all(bm, offset, seg=3):
    if offset <= 0: return
    bmesh.ops.bevel(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                    offset=offset, segments=seg, profile=0.5, affect='EDGES')

def bm_boxes(specs, bevel=0.04, seg=3):
    """specs: [((sx,sy,sz),(x,y,z)) ...] or [((sx,sy,sz),(x,y,z),rz) ...]"""
    bm = bmesh.new()
    for spec in specs:
        size, off = spec[0], spec[1]
        rz = spec[2] if len(spec) > 2 else 0.0
        mt = (Matrix.Translation(off) @ Matrix.Rotation(rz, 4, 'Z')
              @ Matrix.Diagonal((size[0], size[1], size[2], 1.0)))
        bmesh.ops.create_cube(bm, size=1.0, matrix=mt)
    bevel_all(bm, bevel, seg)
    return bm

def bm_box(sx, sy, sz, bevel=0.05, seg=3):
    return bm_boxes([((sx, sy, sz), (0, 0, 0))], min(bevel, min(sx, sy, sz) * 0.4), seg)

def bm_prism(profile, depth, bevel=0.04, seg=3):
    bm = bmesh.new()
    fv = [bm.verts.new((x, -depth * 0.5, z)) for x, z in profile]
    bv = [bm.verts.new((x, depth * 0.5, z)) for x, z in profile]
    bm.verts.ensure_lookup_table()
    bm.faces.new(fv); bm.faces.new(list(reversed(bv)))
    n = len(profile)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((fv[i], bv[i], bv[j], fv[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bevel_all(bm, bevel, seg)
    return bm

def bm_cyl(r1, r2, h, seg=16, bevel=0.03, bseg=2, off=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg,
                          radius1=r1, radius2=r2, depth=h,
                          matrix=Matrix.Translation(off))
    bevel_all(bm, min(bevel, max(0.001, min(r1, r2, h) * 0.3)), bseg)
    return bm

def bm_cyls(specs, bevel=0.03, bseg=2):
    """specs: [(r1, r2, h, (x,y,z), seg) ...]"""
    bm = bmesh.new()
    for r1, r2, h, off, sg in specs:
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=sg,
                              radius1=r1, radius2=r2, depth=h,
                              matrix=Matrix.Translation(off))
    bevel_all(bm, bevel, bseg)
    return bm

def bm_blobs(blobs, lumpy=0.0, nscale=1.0, seed=0, subdiv=3):
    bm = bmesh.new()
    for r, off, sq in blobs:
        mt = Matrix.Translation(off) @ Matrix.Diagonal((1.0, 1.0, sq, 1.0))
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=r, matrix=mt)
    if lumpy > 0:
        rng = random.Random(seed)
        o = Vector((rng.random() * 40, rng.random() * 40, rng.random() * 40))
        for v in bm.verts:
            v.co += v.co.normalized() * (noise.noise(v.co * nscale + o) * lumpy)
    return bm

def bm_shift(bm, d):
    for v in bm.verts:
        v.co.x += d[0]; v.co.y += d[1]; v.co.z += d[2]
    return bm

def add_annulus(bm, r0, r1, a0, a1, z, steps, mi=0, flip=False):
    """Flat ring sector. r0 may be 0 for a solid disc.

    flip разворачивает нормали: нужно, когда кольцо кладётся на обратную
    сторону детали, иначе оно окажется видно только изнутри.
    """
    r0 = max(r0, 0.0005)
    vin, vout = [], []
    for i in range(steps + 1):
        a = a0 + (a1 - a0) * i / steps
        ca, sa = math.cos(a), math.sin(a)
        vin.append(bm.verts.new((ca * r0, sa * r0, z)))
        vout.append(bm.verts.new((ca * r1, sa * r1, z)))
    for i in range(steps):
        quad = (vin[i], vout[i], vout[i + 1], vin[i + 1])
        f = bm.faces.new(tuple(reversed(quad)) if flip else quad)
        f.material_index = mi

# ============================================================ animation helpers
FPS = 30

def key(ob, f, loc=None, rot=None, scale=None, quat=None):
    if loc is not None:
        ob.location = loc; ob.keyframe_insert("location", frame=f)
    if rot is not None:
        ob.rotation_euler = rot; ob.keyframe_insert("rotation_euler", frame=f)
    if quat is not None:
        ob.rotation_mode = 'QUATERNION'
        ob.rotation_quaternion = quat
        ob.keyframe_insert("rotation_quaternion", frame=f)
    if scale is not None:
        ob.scale = scale if not isinstance(scale, (int, float)) else (scale,) * 3
        ob.keyframe_insert("scale", frame=f)

def fcurves_of(ob):
    """Blender 5.x stores fcurves in slotted actions; fall back to the old API."""
    ad = ob.animation_data
    if not ad or not ad.action:
        return []
    act = ad.action
    out = []
    try:
        for layer in act.layers:
            for strip in layer.strips:
                for cb in strip.channelbags:
                    out.extend(cb.fcurves)
    except Exception:
        try:
            out = list(act.fcurves)
        except Exception:
            out = []
    return out

def set_interp(ob, mode='LINEAR', only_after=None):
    for fc in fcurves_of(ob):
        for kp in fc.keyframe_points:
            if only_after is None or kp.co[0] >= only_after:
                kp.interpolation = mode

def ease(ob, mode='BEZIER'):
    set_interp(ob, mode)

def key_alpha(mat, f, v):
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Alpha"].default_value = v
    b.inputs["Alpha"].keyframe_insert("default_value", frame=f)

def key_emit(mat, f, v):
    b = mat.node_tree.nodes["Principled BSDF"]
    k = "Emission Strength"
    if k in b.inputs:
        b.inputs[k].default_value = v
        b.inputs[k].keyframe_insert("default_value", frame=f)

def ballistic(ob, f0, f1, p0, p1, height, spin=None, step=1):
    """Sampled parabola. spin = (axis_vector, turns) applied as quaternion."""
    n = max(1, f1 - f0)
    for i in range(0, n + 1, step):
        t = i / n
        x = p0[0] + (p1[0] - p0[0]) * t
        y = p0[1] + (p1[1] - p0[1]) * t
        z = p0[2] + (p1[2] - p0[2]) * t + height * 4.0 * t * (1.0 - t)
        q = None
        if spin:
            axis, turns = spin
            q = Quaternion(Vector(axis).normalized(), turns * TAU * t)
        key(ob, f0 + i, loc=(x, y, z), quat=q)
    set_interp(ob, 'LINEAR', only_after=f0 - 0.5)

def roll(ob, f0, f1, p0, p1, radius, step=1, z=None):
    """Ground roll: translation + matching spin so the ball does not skate."""
    n = max(1, f1 - f0)
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    dist = math.hypot(dx, dy)
    axis = Vector((-dy, dx, 0.0))
    axis = axis.normalized() if axis.length > 1e-6 else Vector((1, 0, 0))
    for i in range(0, n + 1, step):
        t = i / n
        loc = (p0[0] + dx * t, p0[1] + dy * t,
               p0[2] + ((p1[2] - p0[2]) * t if z is None else 0.0))
        key(ob, f0 + i, loc=loc, quat=Quaternion(axis, dist * t / radius))
    set_interp(ob, 'LINEAR', only_after=f0 - 0.5)

def tumble(ob, f0, f1, p0, p1, height, axis, turns, land_rot=None):
    """Knocked-away prop: arc + spin, then settles flat."""
    ballistic(ob, f0, f1, p0, p1, height, spin=(axis, turns))
    if land_rot is not None:
        key(ob, f1 + 6, loc=p1, quat=Quaternion(Vector(axis).normalized(), land_rot))

def hand_pos(root_loc, root_rz, shoulder, angle_x, arm_len=0.86):
    """World position of a hand given the character root and arm swing angle."""
    lx, ly, lz = shoulder
    hy = ly + arm_len * math.sin(angle_x)
    hz = lz - arm_len * math.cos(angle_x)
    cz, sz = math.cos(root_rz), math.sin(root_rz)
    return (root_loc[0] + lx * cz - hy * sz,
            root_loc[1] + lx * sz + hy * cz,
            root_loc[2] + hz)

# ============================================================ palette
mat = {}

def build_mats():
    mat['grass'] = M_patchy("Grass", (0.19, 0.46, 0.13), (0.28, 0.57, 0.17), 2.6, 0.95)
    mat['dirt'] = M_patchy("Dirt", (0.34, 0.24, 0.15), (0.42, 0.31, 0.20), 8.0, 0.95)
    mat['wall'] = M("Wall", (0.93, 0.83, 0.60), 0.80)
    mat['wall2'] = M("Wall2", (0.80, 0.40, 0.18), 0.75)
    mat['roof'] = M_patchy("Roof", (0.55, 0.12, 0.10), (0.66, 0.20, 0.14), 9.0, 0.72)
    mat['frame'] = M("Frame", (0.98, 0.97, 0.92), 0.60)
    # Стекло было полностью непрозрачным - окна выглядели закрашенными
    # панелями. Прозрачность переносится в Unity через палитру (alpha < 1
    # включает там прозрачный режим материала).
    mat['glass'] = M("Glass", (0.55, 0.78, 0.90), 0.10, spec=1.0, alpha=0.30)
    mat['mirror'] = M("Mirror", (0.82, 0.86, 0.90), 0.05, metallic=1.0, spec=1.0)
    mat['door'] = M("Door", (0.13, 0.42, 0.66), 0.55)
    mat['stone'] = M_patchy("Stone", (0.58, 0.56, 0.52), (0.68, 0.66, 0.62), 7.0, 0.88)
    mat['concrete'] = M("Concrete", (0.62, 0.60, 0.57), 0.92)
    mat['garage'] = M("Garage", (0.26, 0.27, 0.31), 0.85)
    mat['fence'] = M("Fence", (0.72, 0.55, 0.34), 0.80)
    mat['wood'] = M("Wood", (0.52, 0.32, 0.18), 0.80)
    mat['wood_l'] = M("WoodLight", (0.74, 0.54, 0.32), 0.78)
    mat['lane'] = M("Lane", (0.80, 0.62, 0.34), 0.18, spec=0.6)
    mat['bark'] = M_patchy("Bark", (0.38, 0.24, 0.14), (0.48, 0.32, 0.19), 12.0, 0.9)
    mat['leafA'] = M_patchy("LeafA", (0.15, 0.42, 0.16), (0.24, 0.55, 0.20), 5.0, 0.85)
    mat['leafB'] = M_patchy("LeafB", (0.11, 0.35, 0.22), (0.18, 0.46, 0.18), 5.0, 0.85)
    mat['felt'] = M("Felt", (0.06, 0.34, 0.20), 0.95)
    mat['felt_r'] = M("FeltRed", (0.42, 0.07, 0.10), 0.95)
    mat['metal'] = M("Metal", (0.42, 0.44, 0.48), 0.30, metallic=0.85)
    mat['metal_d'] = M("MetalDark", (0.16, 0.17, 0.20), 0.42, metallic=0.7)
    mat['chrome'] = M("Chrome", (0.78, 0.80, 0.84), 0.12, metallic=1.0)
    mat['rubber'] = M("Rubber", (0.10, 0.10, 0.12), 0.85)
    mat['white'] = M("White", (0.95, 0.95, 0.93), 0.55)
    mat['black'] = M("Black", (0.07, 0.07, 0.08), 0.60)
    mat['cream'] = M("Cream", (0.94, 0.86, 0.66), 0.65)
    mat['red'] = M("Red", (0.78, 0.11, 0.11), 0.55)
    mat['green'] = M("Green", (0.10, 0.52, 0.22), 0.55)
    mat['yellow'] = M("Yellow", (0.95, 0.74, 0.10), 0.55)
    mat['blue'] = M("Blue", (0.12, 0.34, 0.72), 0.55)
    mat['orange'] = M("Orange", (0.92, 0.45, 0.08), 0.55)
    mat['purple'] = M("Purple", (0.44, 0.18, 0.60), 0.55)
    mat['maroon'] = M("Maroon", (0.45, 0.09, 0.14), 0.55)
    mat['water'] = M("Water", (0.10, 0.48, 0.68), 0.06, spec=1.0, alpha=0.82)
    mat['piranha'] = M("Piranha", (0.36, 0.36, 0.40), 0.45)
    mat['skin1'] = M("Skin1", (0.94, 0.75, 0.60), 0.62)
    mat['skin2'] = M("Skin2", (0.72, 0.50, 0.34), 0.62)
    mat['skin3'] = M("Skin3", (0.48, 0.31, 0.20), 0.62)
    mat['hair1'] = M("Hair1", (0.20, 0.13, 0.08), 0.70)
    mat['hair2'] = M("Hair2", (0.80, 0.62, 0.24), 0.70)
    mat['jeans'] = M("Jeans", (0.20, 0.28, 0.45), 0.85)
    mat['shorts'] = M("Shorts", (0.30, 0.30, 0.34), 0.85)
    mat['shirtA'] = M("ShirtA", (0.85, 0.22, 0.22), 0.75)
    mat['shirtB'] = M("ShirtB", (0.18, 0.55, 0.72), 0.75)
    mat['shirtC'] = M("ShirtC", (0.94, 0.78, 0.16), 0.75)
    mat['shirtD'] = M("ShirtD", (0.32, 0.62, 0.28), 0.75)
    mat['bulb'] = M("Bulb", (1.0, 0.90, 0.65), 0.30, emit=(1.0, 0.82, 0.45, 1.4))
    mat['neon'] = M("Neon", (0.30, 0.85, 1.0), 0.30, emit=(0.25, 0.80, 1.0, 2.2))
    mat['flame'] = M("Flame", (1.0, 0.55, 0.12), 0.4, emit=(1.0, 0.38, 0.05, 0.9))
    mat['smoke'] = M("Smoke", (0.86, 0.86, 0.88), 1.0, spec=0.05, alpha=0.55)
    mat['potato'] = M("Potato", (0.76, 0.62, 0.38), 0.90)
    mat['beer'] = M("Beer", (0.62, 0.36, 0.06), 0.25, spec=0.8, alpha=0.75)

# ============================================================ terrain & fence
FENCE = 38.0

def terrain_z(x, y):
    k = max(abs(x), abs(y)) / (FENCE + 1.5)
    if k <= 1.0:
        return 0.0
    t = min((k - 1.0) / 0.5, 1.0); t = t * t * (3 - 2 * t)
    h = (noise.noise(Vector((x * 0.022, y * 0.022, 0.0))) * 7.0
         + noise.noise(Vector((x * 0.070, y * 0.070, 5.1))) * 1.6)
    return (h + 2.2) * t

def build_ground():
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=300, y_segments=300, size=140.0)
    for v in bm.verts:
        v.co.z = terrain_z(v.co.x, v.co.y)
    obj_from(bm, "Ground", mat['grass'], smooth=True, angle=70)

GATE_W = 2.4

def build_fence():
    board = obj_from(bm_box(0.18, 1.9, 0.02 + 1.0, 0.03), "Board",
                     mat['fence'], loc=(0, 0, -200))
    post_p = [(-0.14, 0), (0.14, 0), (0.14, 2.1), (0.0, 2.35), (-0.14, 2.1)]
    post = obj_from(bm_prism(post_p, 0.28, 0.04, 2), "FPost", mat['wood'],
                    loc=(0, 0, -200))
    step = 1.9
    n = int(FENCE * 2 / step)
    # Доска длинная по Y (0.18 x 1.9), поэтому вдоль стороны, идущей по X,
    # её надо повернуть на 90 - иначе доски торчат поперёк линии забора.
    quarter = math.radians(90)
    for sy in (-FENCE, FENCE):
        for i in range(n + 1):
            x = -FENCE + i * step
            if sy < 0 and abs(x) < GATE_W + 1.0: continue
            for z in (0.55, 1.55):
                dup(board, (x, sy, z), rot=(0, 0, quarter))
            dup(post, (x + step * 0.5, sy, 0.0), rot=(0, 0, quarter))
    for sx in (-FENCE, FENCE):
        for i in range(n + 1):
            y = -FENCE + i * step
            for z in (0.55, 1.55):
                dup(board, (sx, y, z))
            dup(post, (sx, y + step * 0.5, 0.0))
    for sx in (-1, 1):
        obj_from(bm_box(0.34, 0.34, 2.7, 0.05), "GatePost", mat['wood'],
                 loc=(sx * (GATE_W + 0.5), -FENCE, 1.35))
        leaf = [((0.10, 0.10, 1.7), (dx, 0, 0)) for dx in (-0.65, -0.3, 0, 0.3, 0.65)]
        leaf += [((1.6, 0.09, 0.14), (0, 0, 0.55)), ((1.6, 0.09, 0.14), (0, 0, -0.4))]
        obj_from(bm_boxes(leaf, 0.03), "GateLeaf", mat['fence'],
                 loc=(sx * (GATE_W + 1.3), -FENCE - 0.75, 1.15),
                 rot=(0, 0, math.radians(-62 * sx)))

# ============================================================ house + annex
HX, HY = -6.0, 26.0
HW, HD, HH = 16.0, 12.0, 5.5           # main block
AX0, AX1 = 2.0, 30.0                    # annex span in x
AY0, AY1 = 20.5, 29.5                  # annex span in y
AH = 4.2                               # annex height
WALL = 0.4

def wall_with_openings(name, material, x0, x1, y, z0, z1, thickness, openings):
    """Стена вдоль X с настоящими прямоугольными проёмами.

    openings: [(центр по X, ширина, низ проёма, верх проёма)].

    Собирается из плит: сплошные куски между проёмами плюс подоконная и
    надпроёмная части. Раньше окна и дверь просто приставлялись к глухой
    стене - снаружи они выглядели нарисованными, а внутрь свет не шёл и
    пройти было нельзя.
    """
    holes = sorted(openings, key=lambda o: o[0])
    edge = x0
    for cx, w, hz0, hz1 in holes:
        left, right = cx - w * 0.5, cx + w * 0.5
        if left - edge > 0.01:                       # сплошной кусок слева от проёма
            obj_from(bm_box(left - edge, thickness, z1 - z0, 0.08), name, material,
                     loc=((edge + left) * 0.5, y, (z0 + z1) * 0.5))
        if hz0 - z0 > 0.01:                          # подоконная часть
            obj_from(bm_box(w, thickness, hz0 - z0, 0.06), name, material,
                     loc=(cx, y, (z0 + hz0) * 0.5))
        if z1 - hz1 > 0.01:                          # перемычка над проёмом
            obj_from(bm_box(w, thickness, z1 - hz1, 0.06), name, material,
                     loc=(cx, y, (hz1 + z1) * 0.5))
        edge = right
    if x1 - edge > 0.01:
        obj_from(bm_box(x1 - edge, thickness, z1 - z0, 0.08), name, material,
                 loc=((edge + x1) * 0.5, y, (z0 + z1) * 0.5))


def build_house():
    # ---- main block.  This used to be one solid cube, so the visible front
    # door could never lead anywhere.  Build a real shell with a doorway.
    obj_from(bm_box(HW + 1.0, HD + 1.0, 0.7, 0.08), "Found", mat['stone'],
             loc=(HX, HY, 0.25))
    floor_z = 0.6
    front_y, back_y = HY - HD * 0.5, HY + HD * 0.5
    door_w, door_h = 2.20, 3.20
    side_w = (HW - door_w) * 0.5
    # Фасад строим с проёмами под дверь и оба окна: иначе окна оказываются
    # замурованы в 0.4 м стены, а наружу торчат одни подоконники.
    win_cx, win_w, win_h = 5.2, 2.2, 2.0
    win_z = 0.6 + 3.1                                # центр окна по высоте
    wall_with_openings(
        "HouseFrontWall", mat['wall'],
        HX - HW * 0.5, HX + HW * 0.5, front_y,
        floor_z, floor_z + HH, WALL,
        [(HX, door_w, floor_z, floor_z + door_h),
         (HX - win_cx, win_w, win_z - win_h * 0.5, win_z + win_h * 0.5),
         (HX + win_cx, win_w, win_z - win_h * 0.5, win_z + win_h * 0.5)])
    obj_from(bm_box(HW, WALL, HH, 0.10), "HouseBackWall", mat['wall'],
             loc=(HX, back_y, floor_z + HH * 0.5))
    for sx in (-1, 1):
        obj_from(bm_box(WALL, HD - 2 * WALL, HH, 0.10), "HouseSideWall", mat['wall'],
                 loc=(HX + sx * (HW * 0.5 - WALL * 0.5), HY, floor_z + HH * 0.5))
    obj_from(bm_box(HW - 2 * WALL, HD - 2 * WALL, 0.16, 0.04), "HouseWoodFloor", mat['lane'],
             loc=(HX, HY, floor_z + 0.08))
    for sx in (-1, 1):
        for sy in (-1, 1):
            obj_from(bm_box(0.55, 0.55, HH, 0.06), "Corner", mat['wall2'],
                     loc=(HX + sx * HW * 0.5, HY + sy * HD * 0.5, 0.6 + HH * 0.5))
    roof_p = [(-(HD * 0.5 + 1.4), -0.42), ((HD * 0.5 + 1.4), -0.42),
              ((HD * 0.5 + 1.4), 0.06), (0.0, 3.6), (-(HD * 0.5 + 1.4), 0.06)]
    obj_from(bm_prism(roof_p, HW + 2.8, 0.12), "Roof", mat['roof'],
             loc=(HX, HY, 0.6 + HH), rot=(0, 0, math.radians(90)))
    obj_from(bm_box(1.4, 1.4, 5.0, 0.10), "Chimney", mat['stone'],
             loc=(HX - 5.0, HY + 2.6, 0.6 + HH + 1.4))
    # ---- patio slab in front of the house
    obj_from(bm_box(21.0, 5.4, 0.36, 0.08), "Patio", mat['concrete'],
             loc=(HX, 17.4, 0.12))
    # ---- annex shell (bowling): floor, 4 walls, ceiling
    acx, acy = (AX0 + AX1) * 0.5, (AY0 + AY1) * 0.5
    aw, ad = AX1 - AX0, AY1 - AY0
    obj_from(bm_box(aw, ad, 0.5, 0.06), "AnnexFloor", mat['concrete'],
             loc=(acx, acy, 0.1))
    afloor_z = 0.35
    front_ay = acy - (ad * 0.5 - WALL * 0.5)
    back_ay = acy + (ad * 0.5 - WALL * 0.5)
    obj_from(bm_box(aw, WALL, AH, 0.05), "AnnexWallY", mat["garage"],
             loc=(acx, back_ay, afloor_z + AH * 0.5))

    # Фасад пристройки с настоящими проёмами. Под вывеской боулинга должна
    # быть дверь, а окна - сквозными: раньше стена была сплошной, внутрь не
    # попасть и не заглянуть.
    adoor_cx, adoor_w, adoor_h = acx - 4.0, 2.2, 2.9
    awin_z, awin_w, awin_h = afloor_z + 2.6, 2.4, 1.6
    wall_with_openings(
        "AnnexWallY", mat["garage"], AX0, AX1, front_ay,
        afloor_z, afloor_z + AH, WALL,
        [(adoor_cx, adoor_w, afloor_z, afloor_z + adoor_h)] +
        [(wx, awin_w, awin_z - awin_h * 0.5, awin_z + awin_h * 0.5)
         for wx in (8.0, 15.0, 22.0)])

    # Дверь пристройки по той же схеме, что у дома: петля-пустышка, чтобы в
    # Unity вращать створку вокруг настоящего пивота, а не угаданного.
    ajamb = 0.14
    obj_from(bm_boxes([
        ((ajamb, WALL + 0.12, adoor_h + ajamb), (-(adoor_w * 0.5 + ajamb * 0.5), 0, 0)),
        ((ajamb, WALL + 0.12, adoor_h + ajamb), ((adoor_w * 0.5 + ajamb * 0.5), 0, 0)),
        ((adoor_w + ajamb * 2, WALL + 0.12, ajamb), (0, 0, (adoor_h + ajamb) * 0.5)),
    ], 0.02), "AnnexDoorFrame", mat['frame'],
        loc=(adoor_cx, front_ay, afloor_z + adoor_h * 0.5))

    ahinge = bpy.data.objects.new("ANX_BowlingDoor_Hinge", None)
    bpy.context.collection.objects.link(ahinge)
    ahinge.location = (adoor_cx - adoor_w * 0.5, front_ay - 0.08, afloor_z)
    ahinge["unity_interactable"] = True
    ahinge["interaction"] = "open_close"
    adoor = obj_from(bm_box(adoor_w, 0.16, adoor_h, 0.055), "ANX_BowlingDoor",
                     mat['door'], loc=(adoor_cx, front_ay - 0.10,
                                       afloor_z + adoor_h * 0.5))
    adoor.parent = ahinge
    # Обратную матрицу строим из координат петли напрямую. Через
    # hinge.matrix_world нельзя: у только что созданной пустышки он ещё не
    # пересчитан и равен единичному, из-за чего смещение применяется дважды
    # и створка улетает за пределы двора.
    adoor.matrix_parent_inverse = Matrix.Translation(-ahinge.location)
    obj_from(bm_box(WALL, ad, AH, 0.05), "AnnexWallX", mat["garage"],
             loc=(AX1 - WALL * 0.5, acy, 0.35 + AH * 0.5))
    obj_from(bm_box(aw, ad, 0.35, 0.05), "AnnexCeil", mat["garage"],
             loc=(acx, acy, 0.35 + AH + 0.17))
    aroof = [(-(ad * 0.5 + 0.9), -0.30), ((ad * 0.5 + 0.9), -0.30),
             ((ad * 0.5 + 0.9), 0.04), (0.0, 2.1), (-(ad * 0.5 + 0.9), 0.04)]
    obj_from(bm_prism(aroof, aw + 1.6, 0.10), "AnnexRoof", mat['roof'],
             loc=(acx, acy, 0.35 + AH + 0.34), rot=(0, 0, math.radians(90)))
    # Front door: pivot, named action and custom properties survive FBX export
    # and become an obvious Unity interactable/animation anchor.
    # Рама проёма: два косяка и перемычка. Раньше здесь стояла сплошная
    # плита - она закрывала проём белым щитом, и войти было нельзя.
    jamb = 0.14
    obj_from(bm_boxes([
        ((jamb, WALL + 0.12, door_h + jamb), (-(door_w * 0.5 + jamb * 0.5), 0, 0)),
        ((jamb, WALL + 0.12, door_h + jamb), ((door_w * 0.5 + jamb * 0.5), 0, 0)),
        ((door_w + jamb * 2, WALL + 0.12, jamb), (0, 0, (door_h + jamb) * 0.5)),
    ], 0.02), "DoorFrame", mat['frame'],
        loc=(HX, front_y, floor_z + door_h * 0.5))
    hinge = bpy.data.objects.new("HOU_FrontDoor_Hinge", None)
    bpy.context.collection.objects.link(hinge)
    hinge.location = (HX - door_w * 0.5, front_y - 0.08, floor_z)
    hinge["unity_interactable"] = True
    hinge["interaction"] = "open_close"
    hinge["animation"] = "Door_Open"
    door = obj_from(bm_box(door_w, 0.16, door_h, 0.055), "HOU_FrontDoor", mat['door'],
                    loc=(HX, front_y - 0.10, floor_z + door_h * 0.5))
    door.parent = hinge
    door.matrix_parent_inverse = Matrix.Translation(-hinge.location)
    # raised panels and handle, parented with the leaf rather than left behind
    for z in (1.05, 2.30):
        panel = obj_from(bm_box(1.62, 0.035, 0.82, 0.025), "DoorPanel", mat['wall2'],
                         loc=(HX, front_y - 0.195, floor_z + z))
        panel.parent = hinge; panel.matrix_parent_inverse = Matrix.Translation(-hinge.location)
    handle = obj_from(bm_cyl(0.075, 0.075, 0.12, 12, 0.015), "DoorHandle", mat['chrome'],
                      loc=(HX + 0.68, front_y - 0.23, floor_z + 1.65),
                      rot=(math.radians(90), 0, 0), smooth=True, angle=60)
    handle.parent = hinge; handle.matrix_parent_inverse = Matrix.Translation(-hinge.location)
    hinge.rotation_euler = (0, 0, 0); hinge.keyframe_insert("rotation_euler", index=2, frame=1)
    hinge.rotation_euler = (0, 0, math.radians(-105)); hinge.keyframe_insert("rotation_euler", index=2, frame=18)
    hinge.rotation_euler = (0, 0, 0); hinge.keyframe_insert("rotation_euler", index=2, frame=36)
    set_interp(hinge, 'BEZIER')
    if hinge.animation_data and hinge.animation_data.action:
        hinge.animation_data.action.name = "Door_Open"
    bpy.context.scene.frame_set(1)
    # windows on the main block
    for wx in (-5.2, 5.2):
        add_window((HX + wx, HY - HD * 0.5, 0.6 + 3.1), 2.2, 2.0, 0.0)
    # annex windows (light for the bowling room)
    for wx in (8.0, 15.0, 22.0):
        # по центру стены, ровно в проёме - раньше окно сидело в её толще
        add_window((wx, front_ay, awin_z), awin_w, awin_h, 0.0)

    # ---- gutters along both roofs ----
    for gy in (HY - HD * 0.5 - 1.3, HY + HD * 0.5 + 1.3):
        obj_from(bm_cyl(0.06, 0.06, HW + 1.6, 10, 0.015, 1), "Gutter",
                 mat['metal_d'], loc=(HX, gy, 0.6 + HH - 0.38),
                 rot=(0, math.radians(90), 0), smooth=True, angle=60)
    for gy in (AY0 - 0.8, AY1 + 0.8):
        obj_from(bm_cyl(0.05, 0.05, aw + 1.2, 10, 0.012, 1), "AnnexGutter",
                 mat['metal_d'], loc=(acx, gy, 0.35 + AH - 0.24),
                 rot=(0, math.radians(90), 0), smooth=True, angle=60)

    # ---- chimney smoke (a static soft wisp, always drifting) ----
    obj_from(bm_blobs([(0.35, (0, 0, 0), 0.9), (0.24, (0.18, 0.10, 0.22), 0.9)],
                      0.16, 1.6, 44, 3), "ChimneySmoke", mat['smoke'],
             loc=(HX - 5.0, HY + 2.6, 10.35), angle=180)

    # ---- porch: mat, potted plants, light, house number ----
    fwall_y = HY - HD * 0.5

    # Ступени к двери. Пол в доме на 0.76 м выше двора, а персонаж
    # перешагивает заметно меньше - без крыльца в открытую дверь просто
    # не войти, упираешься в невидимый уступ.
    porch_w = door_w + 1.0
    found_edge = HY - (HD + 1.0) * 0.5
    for top, depth in ((0.20, 1.15), (0.40, 0.78), (0.60, 0.42)):
        obj_from(bm_box(porch_w, depth, top, 0.03), "PorchStep", mat['stone'],
                 loc=(HX, found_edge - depth * 0.5 + 0.05, top * 0.5))

    # коврик кладём на площадку фундамента, иначе он утоплен в бетон
    obj_from(bm_box(1.1, 0.55, 0.03, 0.012), "Doormat", mat['maroon'],
             loc=(HX, fwall_y - 0.55, 0.615))
    for sxp in (-1, 1):
        obj_from(bm_cyls([(0.20, 0.15, 0.30, (0, 0, 0), 14)], 0.02), "PlanterPot",
                 mat['wood_l'], loc=(HX + sxp * 2.0, fwall_y - 0.42, 0.34),
                 smooth=True, angle=45)
        obj_from(bm_blobs([(0.24, (0, 0, 0), 0.8), (0.16, (0.15, 0.10, 0.15), 0.85),
                           (0.15, (-0.14, -0.10, 0.12), 0.85)], 0.14, 1.6,
                          int(sxp) + 3, 3), "PlanterLeaf", mat['leafA'],
                 loc=(HX + sxp * 2.0, fwall_y - 0.42, 0.58), angle=180)
    obj_from(bm_cyl(0.025, 0.025, 0.20, 8, 0.0), "PorchLampArm", mat['metal_d'],
             loc=(HX, fwall_y - 0.02, 4.30), rot=(math.radians(80), 0, 0),
             smooth=True, angle=60)
    obj_from(bm_cyls([(0.11, 0.15, 0.20, (0, 0, 0), 14)], 0.02), "PorchLampShade",
             mat['metal_d'], loc=(HX, fwall_y - 0.20, 4.14), smooth=True, angle=45)
    obj_from(bm_blobs([(0.075, (0, 0, 0), 1.0)], 0, 1, 0, 2), "PorchLampBulb",
             mat['bulb'], loc=(HX, fwall_y - 0.20, 4.06), angle=180)
    pld = bpy.data.lights.new("PorchLight", type='POINT')
    pld.energy = 180.0; pld.color = (1.0, 0.85, 0.65); pld.shadow_soft_size = 0.4
    plo = bpy.data.objects.new("PorchLight", pld)
    bpy.context.collection.objects.link(plo)
    plo.location = (HX, fwall_y - 0.20, 4.00)
    obj_from(bm_box(0.28, 0.025, 0.18, 0.01), "NumberPlaque", mat['metal_d'],
             loc=(HX + 1.55, fwall_y - 0.02, 3.2))

    # ---- exterior bowling sign, mounted above the annex windows ----
    sign_cx = acx - 4.0
    obj_from(bm_box(2.6, 0.06, 0.60, 0.03), "BowlSignBoard", mat['maroon'],
             loc=(sign_cx, AY0 - 0.08, 4.25))
    obj_from(bm_box(2.7, 0.02, 0.05, 0.008), "BowlSignTrim", mat['bulb'],
             loc=(sign_cx, AY0 - 0.11, 4.57))
    obj_from(bm_cyls([(0.05, 0.07, 0.06, (0, 0, -0.26), 12),
                      (0.07, 0.04, 0.16, (0, 0, -0.12), 12),
                      (0.04, 0.05, 0.09, (0, 0, 0.02), 12),
                      (0.05, 0.02, 0.08, (0, 0, 0.12), 12)], 0.008),
             "BowlSignPin", mat['white'], loc=(sign_cx - 0.75, AY0 - 0.14, 4.25),
             smooth=True, angle=45)
    obj_from(bm_blobs([(0.20, (0, 0, 0), 1.0)], 0, 1, 0, 2), "BowlSignBall",
             mat['blue'], loc=(sign_cx + 0.65, AY0 - 0.14, 4.20), angle=180)
    build_house_interior()

def build_house_interior():
    """Furnished, low-poly interior intended as a usable Unity gameplay space."""
    def box(name, size, loc, material, bevel=0.04):
        return obj_from(bm_box(*size, bevel), name, material, loc=loc, smooth=True, angle=45)
    def anchor(name, loc):
        ob = bpy.data.objects.new("ANCHOR_" + name, None)
        bpy.context.collection.objects.link(ob); ob.location = loc
        ob["unity_anchor"] = True
        return ob

    # Room dividers keep the home legible but leave wide passages for the player.
    # Планировка. Передняя перегородка шла вдоль той же оси, что и входная
    # дверь, и упиралась ребром прямо в проём - войдя, ты утыкался в стену.
    # Убрана: перед входом теперь общая зона, гостиная и кухня открыты.
    # Спальню и санузел в задней половине по-прежнему отделяет перегородка.
    box("InteriorDivider", (0.16, 3.6, 3.2), (-6.0, 30.0, 2.2), mat['wall2'])

    # Проход из передней половины в заднюю был 0.4 м - персонаж диаметром
    # 0.76 м в него просто не пролезал. Раздвинуто до двух метров.
    box("InteriorDivider", (6.8, 0.16, 3.2), (-10.4, 26.0, 2.2), mat['wall2'])
    box("InteriorDivider", (6.8, 0.16, 3.2), (-1.6, 26.0, 2.2), mat['wall2'])

    # Living room (front-left): couch, rug, coffee table and TV wall.
    box("LivingRug", (5.1, 3.5, 0.035), (-10.1, 23.0, 0.79), mat['maroon'], 0.015)
    box("LivingSofa", (3.4, 0.95, 0.72), (-10.2, 21.45, 1.15), mat['blue'])
    box("LivingSofaBack", (3.4, 0.16, 0.88), (-10.2, 21.93, 1.56), mat['blue'])
    box("CoffeeTable", (1.55, 0.85, 0.48), (-10.2, 23.35, 1.05), mat['wood_l'])
    box("TVStand", (2.4, 0.48, 0.65), (-10.2, 25.25, 1.10), mat['wood'])
    box("TV", (2.05, 0.10, 1.18), (-10.2, 25.00, 1.90), mat['black'], 0.02)
    anchor("House_LivingSpawn", (-10.2, 23.8, 0.82))

    # Kitchen (front-right): counter run, fridge, cooker, island and light.
    box("KitchenCounter", (0.72, 4.4, 0.92), (-2.65, 22.9, 1.25), mat['wood_l'])
    box("KitchenTop", (0.86, 4.55, 0.10), (-2.65, 22.9, 1.75), mat['stone'])
    box("Fridge", (1.05, 0.88, 2.25), (0.85, 21.55, 1.92), mat['chrome'])
    box("Cooker", (0.78, 0.75, 0.90), (-2.65, 24.55, 1.24), mat['metal_d'])
    box("KitchenIsland", (1.45, 1.95, 0.92), (-0.85, 23.2, 1.25), mat['wood_l'])
    box("KitchenIslandTop", (1.60, 2.10, 0.10), (-0.85, 23.2, 1.75), mat['stone'])
    anchor("House_KitchenInteract", (-0.85, 24.5, 0.82))

    # Bedroom (back-left): bed, bedside tables, wardrobe and lamp.
    box("BedroomRug", (4.8, 3.6, 0.035), (-10.0, 29.1, 0.79), mat['cream'], 0.015)
    box("BedBase", (3.35, 2.15, 0.48), (-10.1, 29.25, 1.08), mat['wood'])
    box("BedMattress", (3.22, 2.02, 0.38), (-10.1, 29.25, 1.48), mat['white'])
    box("BedHeadboard", (3.45, 0.18, 1.38), (-10.1, 30.25, 1.72), mat['wood'])
    for x in (-12.25, -7.95): box("BedsideTable", (0.52, 0.52, 0.62), (x, 30.0, 1.18), mat['wood_l'])
    box("Wardrobe", (1.25, 0.65, 2.45), (-13.05, 27.6, 2.02), mat['wood'])
    anchor("House_BedroomSpawn", (-8.2, 28.0, 0.82))

    # Bathroom / utility (back-right) with clear interaction anchors.
    box("BathTub", (1.45, 2.35, 0.72), (-1.15, 29.65, 1.18), mat['white'])
    box("BathWater", (1.20, 2.08, 0.05), (-1.15, 29.65, 1.56), mat['water'], 0.01)
    box("BathroomVanity", (1.10, 0.55, 0.88), (-3.65, 27.45, 1.23), mat['wood_l'])
    # зеркало отдельным материалом: прозрачное стекло тут читалось бы дырой
    box("BathroomMirror", (0.78, 0.08, 1.10), (-3.65, 27.16, 2.25), mat['mirror'], 0.01)
    box("Washer", (0.78, 0.78, 0.92), (0.85, 27.45, 1.25), mat['chrome'])
    anchor("House_BathroomInteract", (-2.2, 28.1, 0.82))

    # Warm practical ceiling lamps make the room feel occupied in Blender and Unity.
    for i, loc in enumerate([(-10.0, 23.2, 5.35), (-2.0, 23.2, 5.35),
                             (-10.0, 29.2, 5.35), (-2.0, 29.2, 5.35)]):
        box("HouseCeilingLamp", (0.46, 0.46, 0.12), loc, mat['bulb'], 0.04)
        ld = bpy.data.lights.new("HouseLight_%d" % i, type='POINT')
        ld.energy = 240.0; ld.color = (1.0, 0.78, 0.52); ld.shadow_soft_size = 0.65
        lo = bpy.data.objects.new("HouseLight_%d" % i, ld)
        bpy.context.collection.objects.link(lo); lo.location = (loc[0], loc[1], loc[2] - 0.18)

def add_window(loc, w=2.0, h=1.8, rz=0.0):
    fr = [((w + 0.4, 0.34, 0.28), (0, 0, h * 0.5 + 0.14)),
          ((w + 0.6, 0.52, 0.30), (0, -0.08, -h * 0.5 - 0.15)),
          ((0.28, 0.34, h + 0.28), (-w * 0.5 - 0.14, 0, 0)),
          ((0.28, 0.34, h + 0.28), (w * 0.5 + 0.14, 0, 0)),
          ((0.12, 0.30, h), (0, -0.03, 0)),
          ((w, 0.30, 0.12), (0, -0.03, 0))]
    obj_from(bm_boxes(fr, 0.045), "WinFrame", mat['frame'], loc=loc, rot=(0, 0, rz))
    obj_from(bm_box(w, 0.12, h, 0.03), "WinGlass", mat['glass'], loc=loc, rot=(0, 0, rz))

# ============================================================ characters

# eyebrow tilt (radians) per expression - rotated about Y so the brow bar
# pitches at its outer end, giving a furrowed/relaxed/surprised read
BROW_ANGLE = {'normal': 0.05, 'angry': 0.34, 'raised': -0.28}

def make_character(name, shirt, pants, skin, hair, cap=False,
                    hair_style='short', brow='normal', stocky=False, cap_mat=None):
    """Parts authored around the origin, feet at z=0, parented to an empty root.

    hair_style/brow/stocky/cap_mat exist so the four players at the table read
    as distinct people at a glance (silhouette + expression), not one mesh
    recoloured four times - see the STOOL/table specs list below.
    """
    root = bpy.data.objects.new("CH_" + name, None)
    bpy.context.collection.objects.link(root)
    root.empty_display_size = 0.5
    P = {'root': root, 'name': name}

    def add(bm, key_, m, loc, rot=(0, 0, 0), angle=180):
        ob = obj_from(bm, name + "_" + key_, m, loc=loc, rot=rot, angle=angle)
        ob.parent = root
        P[key_] = ob
        return ob

    for sx, k in ((-1, 'leg_l'), (1, 'leg_r')):
        add(bm_cyl(0.17, 0.15, 0.98, 22, 0.012), k, pants,
            (sx * 0.20, 0, 0.50), angle=40)
        add(bm_box(0.28, 0.46, 0.18, 0.06), 'shoe' + k[-2:], mat['black'],
            (sx * 0.20, -0.08, 0.09), angle=40)
    # stocky build widens the torso and adds a belly bump - a different
    # silhouette rather than just a recolour
    tw, td = (0.82, 0.50) if stocky else (0.72, 0.42)
    add(bm_boxes([((tw, td, 0.98), (0, 0, 0))], 0.16, 4), 'body', shirt,
        (0, 0, 1.50), angle=40)
    if stocky:
        add(bm_blobs([(0.28, (0, 0, 0), 0.55)], 0.0, 1, 0, 3), 'belly', shirt,
            (0, 0.16, 1.28))
    add(bm_blobs([(0.32, (0, 0, 0), 1.06)], 0.0, 1, 0, 3), 'head', skin,
        (0, 0, 2.22))

    # hair - deterministic per-name RNG so runs stay reproducible regardless
    # of call order, and independent of the global scatter's random stream
    rng = random.Random(hash(name) & 0xffff)
    if hair_style != 'bald':
        # short cap covers more forehead; other styles pull back a bit so the
        # eyebrows don't fuse visually with the hairline
        cap_r = 0.326 if hair_style == 'short' else 0.30
        add(bm_blobs([(cap_r, (0, 0, 0.11), 0.70)], 0.0, 1, 0, 3),
            'hair', hair, (0, 0, 2.22))
    if hair_style == 'pony':
        add(bm_blobs([(0.13, (0, 0, 0), 1.7)], 0.12, 3, 5, 3), 'ponytail', hair,
            (0, -0.30, 2.05), rot=(math.radians(100), 0, 0))
    elif hair_style == 'messy':
        # a front-to-back ridge of same-size tufts (fauxhawk), not random
        # scattered blobs - reads as a deliberate style, not broken geometry
        for i, dy in enumerate((-0.11, 0.02, 0.15)):
            add(bm_blobs([(0.10, (0, 0, 0), 1.5)], 0.10, 3, rng.randint(0, 999), 2),
                'spike%d' % i, hair, (0, dy, 2.49 - abs(dy) * 0.35),
                rot=(math.radians(-15), 0, 0))

    # face - without eyes they read as shop mannequins. Kept close together
    # and slightly bigger reads softer/cuter than wide-set small eyes.
    for sx, k in ((-1, 'eye_l'), (1, 'eye_r')):
        add(bm_blobs([(0.062, (0, 0, 0), 1.0)], 0, 1, 0, 2), k, mat['white'],
            (sx * 0.092, 0.285, 2.28))
        add(bm_blobs([(0.034, (0, 0, 0), 1.0)], 0, 1, 0, 2), k + '_p', mat['black'],
            (sx * 0.096, 0.325, 2.28))
    ang = BROW_ANGLE[brow]
    for sx, k in ((-1, 'brow_l'), (1, 'brow_r')):
        add(bm_box(0.11, 0.03, 0.035, 0.006, 2), k, hair,
            (sx * 0.095, 0.28, 2.365), rot=(0, sx * ang, 0))
    # wider open-mouth grin (party game, not a shop mannequin) with a hint
    # of teeth instead of the old flat dot
    add(bm_blobs([(0.095, (0, 0, 0), 0.32)], 0, 1, 0, 3), 'mouth', mat['maroon'],
        (0, 0.278, 2.05))
    add(bm_boxes([((0.11, 0.02, 0.045), (0, 0, 0))], 0.006, 2), 'teeth', mat['white'],
        (0, 0.298, 2.075))
    if cap:
        cm = cap_mat or mat['red']
        # dome sized to just hug the head (was bigger than the head itself,
        # which read as a second ball stacked on top rather than a cap)
        add(bm_blobs([(0.335, (0, 0.01, 0), 0.62)], 0, 1, 0, 3), 'cap', cm,
            (0, 0, 2.42))
        # brim: a flattened, elongated blob poking out the front, not a
        # full-width cylinder (that was the bug - it read as a second dome)
        bb = bmesh.new()
        bmesh.ops.create_icosphere(bb, subdivisions=2, radius=1.0)
        for v in bb.verts:
            v.co.x *= 0.15; v.co.y *= 0.20; v.co.z *= 0.022
        add(bb, 'brim', cm, (0, 0.27, 2.315), angle=180)
    # arms: geometry hangs below the origin so the object pivots at the shoulder
    for sx, k in ((-1, 'arm_l'), (1, 'arm_r')):
        a = bm_cyls([(0.115, 0.10, 0.72, (0, 0, -0.36), 22),
                     (0.145, 0.145, 0.18, (0, 0, -0.80), 22)], 0.012)
        arm = add(a, k, skin, (sx * 0.46, 0, 1.86), angle=40)
        # short sleeve cuff, parented to the arm itself so it swings along
        # with it instead of hanging fixed on the root
        cuff = obj_from(bm_cyl(0.155, 0.155, 0.12, 22, 0.01),
                        name + "_cuff" + k[-2:], shirt, loc=(0, 0, -0.06), angle=40)
        cuff.parent = arm
    P['shoulder_r'] = (0.46, 0.0, 1.86)
    P['shoulder_l'] = (-0.46, 0.0, 1.86)
    return P

def place(P, loc, rz):
    P['root'].location = loc
    P['root'].rotation_euler = (0, 0, rz)
    P['loc'] = loc
    P['home'] = (loc, rz)
    P['rz'] = rz
    return P

def arm_key(P, side, f, ang, lean=None):
    a = P['arm_' + side]
    a.rotation_euler = (ang, 0, 0)
    a.keyframe_insert("rotation_euler", frame=f)
    if lean is not None:
        b = P['body']
        b.rotation_euler = (lean, 0, 0)
        b.keyframe_insert("rotation_euler", frame=f)

def root_key(P, f, loc=None, rz=None):
    r = P['root']
    if loc is not None:
        r.location = loc; r.keyframe_insert("location", frame=f)
    if rz is not None:
        r.rotation_euler = (0, 0, rz); r.keyframe_insert("rotation_euler", frame=f)

def bm_rotate(bm, angle, axis='Y'):
    m = Matrix.Rotation(angle, 4, axis)
    for v in bm.verts:
        v.co = m @ v.co
    return bm

def add_prism(bm, profile, depth, off=(0, 0, 0), mi=0):
    fv = [bm.verts.new((x + off[0], -depth * 0.5 + off[1], z + off[2]))
          for x, z in profile]
    bv = [bm.verts.new((x + off[0], depth * 0.5 + off[1], z + off[2]))
          for x, z in profile]
    faces = [bm.faces.new(fv), bm.faces.new(list(reversed(bv)))]
    n = len(profile)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((fv[i], bv[i], bv[j], fv[j])))
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    for f in faces:
        f.material_index = mi
    return faces

def bm_tube(r_in, r_out, h, seg=32):
    bm = bmesh.new()
    for r in (r_out, r_in):
        bmesh.ops.create_cone(bm, cap_ends=False, cap_tris=False, segments=seg,
                              radius1=r, radius2=r, depth=h)
    add_annulus(bm, r_in, r_out, 0, TAU, h * 0.5, seg)
    add_annulus(bm, r_in, r_out, 0, TAU, -h * 0.5, seg)
    return bm

RIG = {}

# ============================================================ 1. bluff table
TBL = (0.0, 4.0)
TBL_TOP = 1.02

def build_bluff_table():
    x, y = TBL
    obj_from(bm_cyl(1.38, 1.38, 0.15, 40, 0.045), "TableTop", mat['wood_l'],
             loc=(x, y, TBL_TOP - 0.08), smooth=True, angle=45)
    obj_from(bm_cyl(1.22, 1.22, 0.04, 40, 0.015), "TableFelt", mat['felt_r'],
             loc=(x, y, TBL_TOP + 0.02), smooth=True, angle=45)
    obj_from(bm_cyl(0.30, 0.42, 0.86, 16, 0.05), "TablePost", mat['wood'],
             loc=(x, y, 0.47), smooth=True, angle=45)
    obj_from(bm_boxes([((1.9, 0.32, 0.14), (0, 0, 0)),
                       ((0.32, 1.9, 0.14), (0, 0, 0))], 0.04), "TableFoot",
             mat['wood'], loc=(x, y, 0.09))
    stool = obj_from(bm_cyls([(0.32, 0.32, 0.12, (0, 0, 0.52), 20),
                              (0.10, 0.14, 0.56, (0, 0, 0.24), 12)], 0.03),
                     "Stool", mat['wood'], loc=(0, 0, -200), smooth=True, angle=45)
    for i in range(4):
        a = TAU * i / 4 + math.radians(45)
        dup(stool, (x + math.cos(a) * 2.15, y + math.sin(a) * 2.15, 0.0), angle=45)
    # cards lying face-down, ready to be flipped
    RIG['cards'] = []
    for i in range(4):
        a = TAU * i / 4 + math.radians(45)
        cx, cy = x + math.cos(a) * 0.80, y + math.sin(a) * 0.80
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0,
                              matrix=Matrix.Diagonal((0.30, 0.42, 0.014, 1.0)))
        bevel_all(bm, 0.012, 2)
        for f in bm.faces:                      # face-up side is the white face
            f.material_index = 1 if f.normal.z > 0.5 else 0
        # start face-down: flipped 180 deg so the maroon back shows
        card = obj_from(bm, "Card%d" % i, [mat['maroon'], mat['white']],
                        loc=(cx, cy, TBL_TOP + 0.05),
                        rot=(math.pi, 0, a + math.radians(90)), angle=45)
        RIG['cards'].append(card)
    # dice cup (slams down, lifts to reveal)
    RIG['cup'] = obj_from(bm_cyls([(0.20, 0.23, 0.40, (0, 0, 0), 20),
                                   (0.20, 0.20, 0.03, (0, 0, 0.20), 20)], 0.03),
                          "DiceCup", mat['metal_d'],
                          loc=(x + 0.55, y - 0.30, TBL_TOP + 0.24),
                          smooth=True, angle=45)
    RIG['dice'] = []
    for i in range(5):
        a = TAU * i / 5
        d = obj_from(bm_box(0.11, 0.11, 0.11, 0.010, 2), "Die%d" % i, mat["white"],
                     loc=(x + 0.55 + math.cos(a) * 0.09,
                          y - 0.30 + math.sin(a) * 0.09, TBL_TOP + 0.09),
                     rot=(random.uniform(0, 3), random.uniform(0, 3),
                          random.uniform(0, 3)), angle=45)
        RIG['dice'].append(d)
    # chip stacks
    RIG['chips'] = []
    cols = ['red', 'blue', 'white', 'green']
    for i, (dx, dy) in enumerate(((-0.55, 0.30), (-0.30, 0.62), (0.10, 0.66))):
        for j in range(6):
            ch = obj_from(bm_cyl(0.115, 0.115, 0.028, 20, 0.008),
                          "Chip%d_%d" % (i, j), mat[cols[(i + j) % 4]],
                          loc=(x + dx, y + dy, TBL_TOP + 0.05 + j * 0.03),
                          smooth=True, angle=45)
            RIG['chips'].append(ch)
    # bottles
    for i in range(4):
        a = TAU * i / 4 + math.radians(20)
        obj_from(bm_cyls([(0.075, 0.075, 0.26, (0, 0, 0), 14),
                          (0.075, 0.032, 0.09, (0, 0, 0.175), 14),
                          (0.032, 0.032, 0.10, (0, 0, 0.27), 14)], 0.015),
                 "Bottle%d" % i, mat['beer'],
                 loc=(x + math.cos(a) * 1.02, y + math.sin(a) * 1.02, TBL_TOP + 0.17),
                 smooth=True, angle=45)

# ============================================================ shared: pub-style target wall
# Reused by darts and axe-throwing so both read as a real mounted station,
# not a target floating on bare posts. Everything here faces -x, matching
# the board/target convention (thrower stands on the -x side).
def build_target_wall(cx, cy, cz, w=3.2, h=3.2, plank_mat=None, trim_mat=None):
    pm = plank_mat or mat['wood_l']
    tm = trim_mat or mat['wood']
    obj_from(bm_boxes([((0.42, 0.42, h + 0.9), (0, -w * 0.5 - 0.35, -0.3)),
                       ((0.42, 0.42, h + 0.9), (0, w * 0.5 + 0.35, -0.3))], 0.06),
             "WallPost", tm, loc=(cx, cy, cz))
    n = max(6, int(w / 0.32))
    pw = w / n
    specs = [((0.10, pw - 0.022, h), (0, -w * 0.5 + pw * 0.5 + i * pw, 0))
             for i in range(n)]
    obj_from(bm_boxes(specs, 0.016, 2), "WallPlanks", pm, loc=(cx, cy, cz))
    fr = [((0.16, w + 0.20, 0.18), (0, 0, h * 0.5)),
          ((0.16, w + 0.20, 0.18), (0, 0, -h * 0.5)),
          ((0.16, 0.18, h + 0.20), (0, -w * 0.5, 0)),
          ((0.16, 0.18, h + 0.20), (0, w * 0.5, 0))]
    obj_from(bm_boxes(fr, 0.03), "WallFrame", tm, loc=(cx, cy, cz))
    obj_from(bm_box(0.46, w + 0.55, 0.07, 0.025), "WallCap", mat['roof'],
             loc=(cx - 0.22, cy, cz + h * 0.5 + 0.20), rot=(0, math.radians(-16), 0))

def build_chalk_scoreboard(cx, cy, cz, w=0.55, h=0.72, seed=0):
    """Small wall-mounted chalkboard with a scribbled tally, facing -x."""
    obj_from(bm_box(0.06, w + 0.10, h + 0.10, 0.02), "ChalkFrame", mat['wood'],
             loc=(cx, cy, cz))
    obj_from(bm_box(0.03, w, h, 0.006), "ChalkBoard", mat['black'],
             loc=(cx - 0.045, cy, cz))
    rng = random.Random(seed)
    for i in range(rng.randint(5, 8)):
        dy = rng.uniform(-w * 0.35, w * 0.35)
        dz = rng.uniform(-h * 0.35, h * 0.35)
        obj_from(bm_box(0.006, 0.028, 0.15, 0.003), "ChalkMark", mat['white'],
                 loc=(cx - 0.065, cy + dy, cz + dz), rot=(rng.uniform(-0.3, 0.3), 0, 0))
    obj_from(bm_cyl(0.012, 0.010, 0.09, 8, 0.002), "ChalkStub", mat['white'],
             loc=(cx - 0.03, cy - w * 0.5 + 0.06, cz - h * 0.5 - 0.05),
             rot=(0, math.radians(90), 0), smooth=True, angle=50)

def build_wall_lamp(cx, cy, cz):
    """A gooseneck cone lamp hanging over a target, matching the bowling lamps."""
    obj_from(bm_cyl(0.03, 0.03, 0.30, 8, 0.0), "LampArm", mat['metal_d'],
             loc=(cx - 0.05, cy, cz), rot=(0, math.radians(60), 0),
             smooth=True, angle=60)
    obj_from(bm_cyls([(0.16, 0.09, 0.14, (0, 0, 0), 16)], 0.02), "WLampShade",
             mat['red'], loc=(cx - 0.30, cy, cz - 0.10), smooth=True, angle=50)
    obj_from(bm_blobs([(0.07, (0, 0, 0), 1.0)], 0, 1, 0, 2), "WLampBulb",
             mat['bulb'], loc=(cx - 0.30, cy, cz - 0.16), angle=180)
    ld = bpy.data.lights.new("WallLamp", type='POINT')
    ld.energy = 220.0; ld.color = (1.0, 0.88, 0.70); ld.shadow_soft_size = 0.4
    lo = bpy.data.objects.new("WallLamp", ld)
    bpy.context.collection.objects.link(lo)
    lo.location = (cx - 0.30, cy, cz - 0.18)

# ============================================================ 2. darts
DART_BOARD = (20.0, 2.0, 1.95)
DART_LINE = (12.2, 2.0)

def build_darts():
    bx, by, bz = DART_BOARD
    build_target_wall(bx + 0.55, by, bz, w=3.0, h=3.0)
    build_wall_lamp(bx + 0.08, by, bz + 1.85)
    build_chalk_scoreboard(bx + 0.08, by - 2.5, bz + 0.05, seed=1)
    # spare-darts quiver mounted on the wall
    obj_from(bm_box(0.10, 0.26, 0.10, 0.02), "DartHolder", mat['wood'],
             loc=(bx + 0.14, by + 1.65, bz - 1.05))
    for i in range(3):
        obj_from(bm_cyl(0.011, 0.011, 0.20, 8, 0.0), "SpareDart%d" % i,
                 mat['orange'] if i % 2 else mat['red'],
                 loc=(bx + 0.02 - i * 0.02, by + 1.55 + i * 0.10, bz - 0.90),
                 rot=(0, math.radians(-72), 0), smooth=True, angle=50)
    R = 1.45                                                   # board scale-up
    bm = bmesh.new()
    add_annulus(bm, 0.0, 0.62 * R, 0, TAU, 0.000, 48, 0)       # black rim
    for i in range(20):
        a0 = TAU * i / 20 - math.radians(9)
        a1 = a0 + TAU / 20
        mi = 1 if i % 2 else 2                                 # cream / black
        add_annulus(bm, 0.075 * R, 0.315 * R, a0, a1, 0.006, 4, mi)
        add_annulus(bm, 0.355 * R, 0.545 * R, a0, a1, 0.006, 4, mi)
        rg = 3 if i % 2 else 4                                 # red / green
        add_annulus(bm, 0.315 * R, 0.355 * R, a0, a1, 0.012, 4, rg)
        add_annulus(bm, 0.545 * R, 0.600 * R, a0, a1, 0.012, 4, rg)
    add_annulus(bm, 0.030 * R, 0.075 * R, 0, TAU, 0.014, 24, 4)   # outer bull
    add_annulus(bm, 0.0, 0.030 * R, 0, TAU, 0.018, 24, 3)         # bull
    bm_rotate(bm, math.radians(-90), 'Y')      # normals must face -x, toward the throw line
    board = obj_from(bm, "DartBoard",
                     [mat['black'], mat['cream'], mat['black'],
                      mat['red'], mat['green']],
                     loc=(bx, by, bz), smooth=False)
    RIG['board'] = board
    RIG['darts'] = []
    for i in range(3):
        d = bmesh.new()
        bmesh.ops.create_cone(d, cap_ends=True, cap_tris=False, segments=10,
                              radius1=0.010, radius2=0.028, depth=0.10,
                              matrix=Matrix.Translation((0, 0, 0.11)))
        bmesh.ops.create_cone(d, cap_ends=True, cap_tris=False, segments=10,
                              radius1=0.028, radius2=0.022, depth=0.17,
                              matrix=Matrix.Translation((0, 0, -0.02)))
        bmesh.ops.create_cone(d, cap_ends=True, cap_tris=False, segments=8,
                              radius1=0.010, radius2=0.010, depth=0.11,
                              matrix=Matrix.Translation((0, 0, -0.16)))
        for k in range(4):
            ang = TAU * k / 4
            mt = (Matrix.Translation((math.cos(ang) * 0.045,
                                      math.sin(ang) * 0.045, -0.235))
                  @ Matrix.Rotation(ang, 4, 'Z')
                  @ Matrix.Diagonal((0.09, 0.008, 0.11, 1.0)))
            bmesh.ops.create_cube(d, size=1.0, matrix=mt)
        bevel_all(d, 0.004, 2)
        bm_rotate(d, math.radians(90), 'Y')                     # tip toward +x
        dart = obj_from(d, "Dart%d" % i, mat['orange'] if i else mat['red'],
                        loc=(bx - 8.5, by + 0.6 + i * 0.4, 1.2), smooth=True, angle=50)
        RIG['darts'].append(dart)

# ============================================================ 3. billiards
POOL = (-18.0, 10.0)
POOL_TOP = 0.86

def build_billiards():
    px, py = POOL
    obj_from(bm_box(3.10, 5.70, 0.42, 0.08), "PoolFrame", mat['wood'],
             loc=(px, py, POOL_TOP - 0.21))
    obj_from(bm_box(2.62, 5.22, 0.06, 0.02), "PoolFelt", mat['felt'],
             loc=(px, py, POOL_TOP + 0.02))
    rails = []
    for sx in (-1, 1):
        rails.append(((0.26, 5.60, 0.20), (sx * 1.42, 0, 0.10)))
    for sy in (-1, 1):
        rails.append(((3.06, 0.26, 0.20), (0, sy * 2.66, 0.10)))
    obj_from(bm_boxes(rails, 0.05), "PoolRails", mat['wood'],
             loc=(px, py, POOL_TOP + 0.05))
    for sx in (-1, 1):
        for sy in (-1, 0, 1):
            obj_from(bm_cyl(0.15, 0.15, 0.12, 16, 0.02), "Pocket", mat['black'],
                     loc=(px + sx * 1.32, py + sy * 2.56, POOL_TOP + 0.03),
                     smooth=True, angle=50)
        for sy in (-1, 1):
            obj_from(bm_box(0.34, 0.34, 0.86, 0.06), "PoolLeg", mat['wood'],
                     loc=(px + sx * 1.28, py + sy * 2.40, 0.32))
    ball_cols = ['yellow', 'blue', 'red', 'purple', 'orange', 'green',
                 'maroon', 'black', 'yellow', 'blue', 'red', 'purple',
                 'orange', 'green', 'maroon']
    RIG['balls'] = []
    r = 0.088
    idx = 0
    for row in range(5):
        for col in range(row + 1):
            bx = px + (col - row * 0.5) * (r * 2.05)
            by = py + 1.10 + row * (r * 1.78)
            b = obj_from(bm_blobs([(r, (0, 0, 0), 1.0)], 0, 1, 0, 3),
                         "Ball%d" % idx, mat[ball_cols[idx]],
                         loc=(bx, by, POOL_TOP + 0.05 + r), angle=180)
            b.rotation_mode = 'QUATERNION'
            RIG['balls'].append(b)
            idx += 1
    cue = obj_from(bm_blobs([(r, (0, 0, 0), 1.0)], 0, 1, 0, 3), "CueBall",
                   mat['white'], loc=(px, py - 1.60, POOL_TOP + 0.05 + r), angle=180)
    cue.rotation_mode = 'QUATERNION'
    RIG['cueball'] = cue
    RIG['ball_r'] = r
    stick = bm_cyls([(0.026, 0.050, 2.90, (0, 0, 0), 14),
                     (0.024, 0.024, 0.10, (0, 0, -1.50), 12)], 0.012)
    bm_rotate(stick, math.radians(90), 'X')                     # along +y
    RIG['cue'] = obj_from(stick, "CueStick", mat['wood_l'],
                          loc=(px, py - 3.30, POOL_TOP + 0.18 + r),
                          rot=(math.radians(-5), 0, 0),
                          smooth=True, angle=50)
    # pergola over the table
    for sx in (-1, 1):
        for sy in (-1, 1):
            obj_from(bm_box(0.30, 0.30, 3.4, 0.05), "PergPost", mat['wood'],
                     loc=(px + sx * 2.2, py + sy * 3.4, 1.7))
    beams = [((0.24, 7.2, 0.30), (sx * 2.2, 0, 0)) for sx in (-1, 1)]
    beams += [((4.8, 0.24, 0.30), (0, sy * 3.4, 0)) for sy in (-1, 1)]
    beams += [((4.9, 0.16, 0.18), (0, -3.0 + i * 0.75, 0.24)) for i in range(9)]
    obj_from(bm_boxes(beams, 0.04), "Pergola", mat['wood'], loc=(px, py, 3.55))

# ============================================================ 4. beer pong
BP = (-10.0, -12.0)
BP_TOP = 0.80

def build_beerpong():
    x, y = BP
    obj_from(bm_box(1.30, 4.40, 0.14, 0.04), "BPTop", mat['wood_l'],
             loc=(x, y, BP_TOP))
    for sx in (-1, 1):
        for sy in (-1, 1):
            obj_from(bm_box(0.16, 0.16, 0.76, 0.03), "BPLeg", mat['metal'],
                     loc=(x + sx * 0.52, y + sy * 1.95, 0.38))
    RIG['cups'] = []
    for end in (-1, 1):
        base = y + end * 1.55
        i = 0
        for row in range(3):
            for col in range(row + 1):
                cx = x + (col - row * 0.5) * 0.20
                cy = base + end * (-row * 0.18)
                cup = obj_from(bm_cyls([(0.085, 0.105, 0.22, (0, 0, 0), 18)], 0.012),
                               "Cup", mat['red'],
                               loc=(cx, cy, BP_TOP + 0.18), smooth=True, angle=50)
                RIG['cups'].append(cup)
                i += 1
    RIG['pongball'] = obj_from(bm_blobs([(0.11, (0, 0, 0), 1.0)], 0, 1, 0, 3),
                               "PongBall", mat['white'],
                               loc=(x, y - 2.6, BP_TOP + 0.25), angle=180)
    RIG['pongball'].rotation_mode = 'QUATERNION'

# ============================================================ 5. axe throwing
AXE_T = (22.0, -12.0, 1.85)
AXE_LINE = (15.6, -12.0)

def build_axe():
    tx, ty, tz = AXE_T
    build_target_wall(tx + 0.60, ty, tz, w=3.4, h=3.4)
    build_wall_lamp(tx + 0.10, ty, tz + 2.05)
    build_chalk_scoreboard(tx + 0.10, ty - 2.7, tz + 0.05, seed=2)
    bm = bmesh.new()
    add_annulus(bm, 0.0, 0.95, 0, TAU, 0.0, 40, 0)
    add_annulus(bm, 0.62, 0.78, 0, TAU, 0.008, 40, 1)
    add_annulus(bm, 0.30, 0.46, 0, TAU, 0.008, 40, 1)
    add_annulus(bm, 0.0, 0.14, 0, TAU, 0.012, 24, 2)
    bm_rotate(bm, math.radians(-90), 'Y')
    RIG['axe_target'] = obj_from(bm, "AxeTarget",
                                 [mat['wood_l'], mat['red'], mat['blue']],
                                 loc=(tx, ty, tz), smooth=False)
    # one mesh, two material slots: handle (wood) + head (steel)
    a = bmesh.new()
    bmesh.ops.create_cube(a, size=1.0, matrix=(
        Matrix.Translation((0, 0, 0.0)) @ Matrix.Diagonal((0.055, 0.055, 0.80, 1.0))))
    for f in a.faces:
        f.material_index = 0
    add_prism(a, [(-0.05, -0.17), (0.05, -0.17), (0.05, 0.17),
                  (-0.21, 0.12), (-0.21, -0.12)], 0.11, (0.02, 0, 0.34), 1)
    bevel_all(a, 0.012, 2)
    axe = obj_from(a, "Axe", [mat['wood'], mat['chrome']],
                   loc=(AXE_LINE[0] + 0.6, AXE_LINE[1], 1.6), smooth=True, angle=40)
    RIG['axe'] = axe

# ============================================================ 6. slingshot
SLING = (-6.0, -22.0, 1.5)
CANS = (-19.0, -22.0)

def build_slingshot():
    sx, sy, sz = SLING
    obj_from(bm_boxes([((0.26, 0.26, 2.0), (0, 0, -0.75)),
                       ((0.9, 0.9, 0.22), (0, 0, -1.72))], 0.04),
             "SlingPost", mat['wood'], loc=(sx, sy, sz))
    for s in (-1, 1):
        obj_from(bm_box(0.18, 0.18, 1.10, 0.04), "SlingFork", mat['wood'],
                 loc=(sx, sy + s * 0.34, sz + 0.60), rot=(math.radians(-s * 18), 0, 0))
    RIG['bands'] = []
    for s in (-1, 1):
        b = obj_from(bm_box(0.05, 0.05, 1.0, 0.012), "Band",
                     mat['rubber'], loc=(sx, sy + s * 0.5, sz + 1.05),
                     rot=(0, math.radians(90), 0))
        RIG['bands'].append(b)
    RIG['pouch'] = obj_from(bm_box(0.16, 0.22, 0.05, 0.02), "Pouch", mat['rubber'],
                            loc=(sx + 0.55, sy, sz + 1.05))
    RIG['pellet'] = obj_from(bm_blobs([(0.10, (0, 0, 0), 1.0)], 0, 1, 0, 3),
                             "Pellet", mat['metal'],
                             loc=(sx + 0.55, sy, sz + 1.05), angle=180)
    RIG['pellet'].rotation_mode = 'QUATERNION'
    cx, cy = CANS
    obj_from(bm_boxes([((0.22, 4.2, 0.22), (0, 0, 0.0)),
                       ((0.22, 0.22, 1.4), (0, -1.9, -0.7)),
                       ((0.22, 0.22, 1.4), (0, 1.9, -0.7))], 0.04),
             "CanRack", mat['wood'], loc=(cx, cy, 1.4))
    RIG['cans'] = []
    for i in range(6):
        can = obj_from(bm_cyls([(0.13, 0.13, 0.34, (0, 0, 0), 18)], 0.02),
                       "Can%d" % i, mat['chrome'] if i % 2 else mat['blue'],
                       loc=(cx, cy - 1.55 + i * 0.62, 1.68), smooth=True, angle=50)
        can.rotation_mode = 'QUATERNION'
        RIG['cans'].append(can)

# ============================================================ 7. bowling (indoor)
LANE_Y = 25.0
LANE_X0, LANE_X1 = 10.0, 24.0
LANE_Z = 0.36

# tiny 3x5 pixel font for the scoreboard screen - blocky, matches the low-poly look
DIGIT_FONT = {
    '0': ["111", "101", "101", "101", "111"], '1': ["010", "110", "010", "010", "111"],
    '2': ["111", "001", "111", "100", "111"], '3': ["111", "001", "111", "001", "111"],
    '4': ["101", "101", "111", "001", "001"], '5': ["111", "100", "111", "001", "111"],
    '6': ["111", "100", "111", "101", "111"], '7': ["111", "001", "010", "010", "010"],
    '8': ["111", "101", "111", "101", "111"], '9': ["111", "101", "111", "001", "111"],
    ':': ["000", "010", "000", "010", "000"],
}

def stamp_text(text, cx, cy, cz, px=0.075, gap=0.03, m=None):
    """Pixel-font text on a vertical panel facing -x (y = horizontal, z = vertical)."""
    mo = m or mat['metal_d']
    total_w = len(text) * (3 * px + gap) - gap
    y0 = cy - total_w * 0.5
    for ci, ch in enumerate(text):
        pat = DIGIT_FONT.get(ch, ["000"] * 5)
        for row in range(5):
            for col in range(3):
                if pat[row][col] != '1': continue
                y = y0 + ci * (3 * px + gap) + col * px + px * 0.5
                z = cz + (4 - row) * px - 2 * px
                obj_from(bm_box(0.02, px * 0.85, px * 0.85, 0.0), "Pixel", mo,
                         loc=(cx, y, z))

def build_bowling():
    cx = (LANE_X0 + LANE_X1) * 0.5
    obj_from(bm_box(LANE_X1 - LANE_X0, 1.90, 0.10, 0.02), "Lane", mat['lane'],
             loc=(cx, LANE_Y, LANE_Z))
    for s in (-1, 1):
        obj_from(bm_box(LANE_X1 - LANE_X0, 0.42, 0.16, 0.03), "Gutter",
                 mat['metal_d'], loc=(cx, LANE_Y + s * 1.18, LANE_Z - 0.05))
    obj_from(bm_box(4.0, 3.0, 0.08, 0.02), "Approach", mat['wood_l'],
             loc=(LANE_X0 - 2.0, LANE_Y, LANE_Z))
    obj_from(bm_box(0.5, 4.6, 2.6, 0.06), "PinDeckWall", mat['metal_d'],
             loc=(LANE_X1 + 1.7, LANE_Y, 1.6))
    obj_from(bm_box(0.3, 4.0, 0.7, 0.05), "NeonStripe", mat['neon'],
             loc=(LANE_X1 + 1.42, LANE_Y, 2.3))
    # ball return rack
    obj_from(bm_boxes([((1.5, 0.9, 0.7), (0, 0, 0)),
                       ((1.6, 1.0, 0.12), (0, 0, 0.40))], 0.05),
             "BallReturn", mat['metal_d'], loc=(LANE_X0 - 2.6, LANE_Y - 2.1, 0.75))
    # hanging lamps
    for lx in (12.0, 17.0, 22.0):
        obj_from(bm_cyl(0.03, 0.03, 1.1, 8, 0.0), "LampCord", mat['black'],
                 loc=(lx, LANE_Y, 3.55), smooth=True, angle=60)
        obj_from(bm_cyls([(0.42, 0.20, 0.34, (0, 0, 0), 20)], 0.03), "LampShade",
                 mat['red'], loc=(lx, LANE_Y, 2.85), smooth=True, angle=50)
        obj_from(bm_blobs([(0.14, (0, 0, 0), 1.0)], 0, 1, 0, 2), "LampBulb",
                 mat['bulb'], loc=(lx, LANE_Y, 2.78), angle=180)
        ld = bpy.data.lights.new("InLamp", type='POINT')
        ld.energy = 300.0; ld.color = (1.0, 0.88, 0.72); ld.shadow_soft_size = 0.5
        lo = bpy.data.objects.new("InLamp", ld)
        bpy.context.collection.objects.link(lo)
        lo.location = (lx, LANE_Y, 2.7)
    # pins
    def pin_bm():
        return bm_cyls([(0.075, 0.105, 0.10, (0, 0, 0.05), 16),
                        (0.105, 0.062, 0.26, (0, 0, 0.23), 16),
                        (0.062, 0.075, 0.13, (0, 0, 0.42), 16),
                        (0.075, 0.030, 0.11, (0, 0, 0.54), 16)], 0.018)
    RIG['pins'] = []
    sp = 0.42
    for row in range(4):
        for col in range(row + 1):
            pxx = LANE_X1 - 1.9 + row * sp * 0.87
            pyy = LANE_Y + (col - row * 0.5) * sp
            p = obj_from(pin_bm(), "Pin%d_%d" % (row, col), mat['white'],
                         loc=(pxx, pyy, LANE_Z + 0.05), smooth=True, angle=50)
            p.rotation_mode = 'QUATERNION'
            RIG['pins'].append(p)
            obj_from(bm_cyl(0.064, 0.064, 0.05, 16, 0.01), "PinBand", mat['red'],
                     loc=(pxx, pyy, LANE_Z + 0.05 + 0.45), smooth=True, angle=50)
    ball = obj_from(bm_blobs([(0.30, (0, 0, 0), 1.0)], 0, 1, 0, 3), "BowlBall",
                    mat['purple'], loc=(LANE_X0 - 2.4, LANE_Y - 2.1, 1.45), angle=180)
    ball.rotation_mode = 'QUATERNION'
    RIG['bowlball'] = ball

    # ---------- lobby: benches, shoe shelf, snack counter, disco ball ----------
    for sy2 in (AY0 + 0.55, AY1 - 0.55):
        obj_from(bm_boxes([((2.4, 0.62, 0.10), (0, 0, 0.46)),
                           ((0.10, 0.55, 0.46), (-1.05, 0, 0.0)),
                           ((0.10, 0.55, 0.46), (1.05, 0, 0.0))], 0.03),
                 "LobbyBench", mat['wood_l'], loc=(4.6, sy2, 0.0))
    obj_from(bm_boxes([((1.8, 0.32, 0.06), (0, 0, 0)),
                       ((1.8, 0.32, 0.06), (0, 0, 0.55)),
                       ((1.8, 0.32, 0.06), (0, 0, 1.10)),
                       ((0.06, 0.32, 1.2), (-0.9, 0, 0.55)),
                       ((0.06, 0.32, 1.2), (0.9, 0, 0.55))], 0.02),
             "ShoeShelf", mat['wood'], loc=(2.6, AY0 + 1.0, 0.60))
    shoe_cols = [mat['red'], mat['blue'], mat['yellow'], mat['green'], mat['orange']]
    for i in range(5):
        obj_from(bm_box(0.30, 0.13, 0.14, 0.03), "Shoe%d" % i,
                 shoe_cols[i % len(shoe_cols)],
                 loc=(2.62, AY0 + 0.65 + i * 0.32, [0.10, 0.65, 1.20][i % 3]))
    obj_from(bm_boxes([((1.3, 0.7, 0.85), (0, 0, 0)),
                       ((1.4, 0.8, 0.10), (0, 0, 0.46))], 0.04),
             "SnackCounter", mat['wood_l'], loc=(6.4, AY1 - 1.1, 0.0))
    for i, cc in enumerate((mat['red'], mat['yellow'])):
        obj_from(bm_cyls([(0.09, 0.11, 0.20, (0, 0, 0), 14)], 0.012), "SnackCup%d" % i,
                 cc, loc=(6.0 + i * 0.35, AY1 - 1.1, 0.98), smooth=True, angle=50)
    obj_from(bm_prism([(-0.16, 0.0), (0.16, 0.0), (0.12, 0.36), (-0.12, 0.36)],
                      0.26, 0.02, 1), "Popcorn", mat['red'],
             loc=(6.75, AY1 - 1.1, 0.94))
    disco_x = (LANE_X0 + LANE_X1) * 0.5 - 1.5
    obj_from(bm_cyl(0.015, 0.015, 0.5, 6, 0.0), "DiscoRope", mat['black'],
             loc=(disco_x, LANE_Y, 3.85), smooth=True, angle=60)
    obj_from(bm_blobs([(0.19, (0, 0, 0), 1.0)], 0, 1, 0, 3), "DiscoBall",
             mat['chrome'], loc=(disco_x, LANE_Y, 3.55), angle=180)
    dl = bpy.data.lights.new("DiscoLight", type='POINT')
    dl.energy = 40.0; dl.color = (0.8, 0.5, 1.0); dl.shadow_soft_size = 0.1
    dlo = bpy.data.objects.new("DiscoLight", dl)
    bpy.context.collection.objects.link(dlo)
    dlo.location = (disco_x, LANE_Y, 3.50)

    # ---------- wall posters: alternating pin / ball icons down both walls ----------
    wall_specs = [(AY0 + 0.45, 1), (AY1 - 0.45, -1)]
    for i, px2 in enumerate((12.5, 17.5, 22.5)):
        pm = [mat['red'], mat['blue'], mat['yellow']][i % 3]
        for wy, pdir in wall_specs:
            obj_from(bm_box(0.85, 0.03, 1.05, 0.015), "Poster", mat['cream'],
                     loc=(px2, wy + pdir * 0.02, 2.35))
            if i % 2 == 0:
                obj_from(bm_cyls([(0.05, 0.07, 0.06, (0, 0, -0.30), 12),
                                  (0.07, 0.04, 0.16, (0, 0, -0.14), 12),
                                  (0.04, 0.05, 0.09, (0, 0, 0.00), 12),
                                  (0.05, 0.02, 0.08, (0, 0, 0.10), 12)], 0.006),
                         "PosterPin", pm, loc=(px2, wy + pdir * 0.05, 2.42),
                         smooth=True, angle=45)
            else:
                obj_from(bm_blobs([(0.22, (0, 0, 0), 1.0)], 0, 1, 0, 2),
                         "PosterBall", pm, loc=(px2, wy + pdir * 0.05, 2.35),
                         angle=180)

    # ---------- score digits on the neon screen ----------
    stamp_text("12:08", LANE_X1 + 1.24, LANE_Y, 2.30, px=0.09, gap=0.035)

# ============================================================ 8. cannon + pool
CANNON = (14.0, -28.0, 1.35)
PPOOL = (2.0, -30.0)
PPOOL_R = 5.5

def build_cannon():
    cx, cy, cz = CANNON
    bm = bm_cyls([(0.30, 0.30, 1.10, (0, 0, -0.75), 20),
                  (0.34, 0.34, 0.16, (0, 0, -0.14), 20),
                  (0.17, 0.17, 2.60, (0, 0, 1.25), 20),
                  (0.22, 0.22, 0.16, (0, 0, 2.50), 20)], 0.03)
    bm_rotate(bm, math.radians(90), 'Y')       # barrel along +x
    cannon = obj_from(bm, "PotatoCannon", mat['metal_d'],
                      loc=(cx, cy, cz), rot=(0, 0, math.radians(196)),
                      smooth=True, angle=50)
    RIG['cannon'] = cannon
    RIG['cannon_home'] = (cx, cy, cz)
    obj_from(bm_boxes([((0.22, 0.22, 1.5), (0.5, 0.4, -0.75)),
                       ((0.22, 0.22, 1.5), (0.5, -0.4, -0.75)),
                       ((0.22, 0.22, 1.5), (-0.6, 0.0, -0.75))], 0.04),
             "CannonLegs", mat['wood'], loc=(cx, cy, cz))
    RIG['potato'] = obj_from(bm_blobs([(0.16, (0, 0, 0), 0.78)], 0.20, 3.0, 5, 3),
                             "Potato", mat['potato'],
                             loc=(cx - 2.4, cy - 0.7, cz + 0.05), angle=180)
    RIG['potato'].rotation_mode = 'QUATERNION'
    sm = M("SmokePuff", (0.88, 0.88, 0.90), 1.0, spec=0.05, alpha=0.0)
    RIG['smoke_mat'] = sm
    RIG['smoke'] = obj_from(bm_blobs([(0.5, (0, 0, 0), 0.8), (0.36, (0.5, 0.2, 0.1), 0.8),
                                      (0.32, (-0.4, -0.3, 0.15), 0.8)], 0.15, 1.2, 3, 3),
                            "MuzzleSmoke", sm,
                            loc=(cx - 2.9, cy - 0.85, cz + 0.05),
                            scale=0.2, angle=180)

def build_piranha_pool():
    px, py = PPOOL
    obj_from(bm_tube(PPOOL_R, PPOOL_R + 0.35, 1.70, 44), "PoolWall", mat['metal'],
             loc=(px, py, 0.85), smooth=True, angle=45)
    obj_from(bm_tube(PPOOL_R - 0.02, PPOOL_R + 0.50, 0.22, 44), "PoolRim",
             mat['wood_l'], loc=(px, py, 1.78), smooth=True, angle=45)
    obj_from(bm_cyl(PPOOL_R, PPOOL_R, 1.5, 44, 0.0), "PoolLiner", mat['blue'],
             loc=(px, py, 0.75), smooth=True, angle=45)
    RIG['water'] = obj_from(bm_cyl(PPOOL_R - 0.04, PPOOL_R - 0.04, 0.08, 44, 0.0),
                            "PoolWater", mat['water'],
                            loc=(px, py, 1.42), smooth=True, angle=60)
    # fins on a spinning hub -> continuous circular motion
    hub = bpy.data.objects.new("PiranhaHub", None)
    bpy.context.collection.objects.link(hub)
    hub.location = (px, py, 1.46)
    RIG['fin_hub'] = hub
    for i in range(5):
        a = TAU * i / 5
        r = 2.2 + (i % 3) * 0.9
        fin = obj_from(bm_prism([(-0.22, 0.0), (0.22, 0.0), (0.0, 0.34)], 0.06, 0.01, 1),
                       "Fin%d" % i, mat['piranha'],
                       loc=(math.cos(a) * r, math.sin(a) * r, 0.0),
                       rot=(0, 0, a + math.radians(90)), smooth=False)
        fin.parent = hub
    # splash rig
    sm = M("SplashRing", (0.86, 0.94, 0.98), 0.25, spec=0.7, alpha=0.0)
    RIG['splash_mat'] = sm
    bm = bmesh.new()
    add_annulus(bm, 0.72, 1.0, 0, TAU, 0.0, 40, 0)
    RIG['splash'] = obj_from(bm, "Splash", sm, loc=(px - 1.0, py + 0.5, 1.50),
                             scale=0.4, smooth=False)
    RIG['drops'] = []
    for i in range(10):
        d = obj_from(bm_blobs([(0.16, (0, 0, 0), 1.1)], 0, 1, 0, 2), "Drop%d" % i,
                     mat['water'], loc=(px - 1.0, py + 0.5, 1.4), angle=180)
        RIG['drops'].append(d)

# ============================================================ decor
# the establishing camera flies along this arc - keep tall props off it
EST_PATH = [(64, -60), (56, -54), (48, -47), (42, -43), (37, -39), (33, -36),
            (29, -33), (26, -30), (22, -26), (18, -22)]

def off_flight_path(x, y, clear=21.0):
    return not any(math.hypot(x - cx, y - cy) < clear for cx, cy in EST_PATH)

# ============================================================ колесо фортуны
# Открытое место: (9, 10) упиралось в бар, (7, -5) - в указатели на (6, -6).
WHEEL = (-2.0, -6.0)
WHEEL_HUB_Z = 2.30
WHEEL_R = 1.25

# Восемь секторов: четыре "0", три "2x", один "3x". Разложены вперемешку,
# чтобы нули не слипались в одну половину - так колесо и выглядит честнее,
# и играется напряжённее.
#
# ВАЖНО: этот же порядок продублирован в FortuneWheel.cs на стороне Unity.
# Там он решает, на каком секторе остановиться, здесь - каким цветом его
# покрасить. Расходиться они не должны.
WHEEL_PAYOUTS = (0, 2, 0, 3, 0, 2, 0, 2)

def _text_object(text, size, material):
    """
    Настоящая надпись шрифтом, а не мозаика из кубиков.

    Пиксельный шрифт годится для табло боулинга, но на колесе читается
    грубо: буквы выходят жирными и корявыми. Здесь берём текстовый объект
    Blender и сразу превращаем его в меш - так он уедет в FBX.
    """
    cu = bpy.data.curves.new(type='FONT', name="WheelText")
    cu.body = text
    cu.size = size
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    cu.extrude = 0.012                     # небольшой объём, чтобы не был плёнкой

    ob = bpy.data.objects.new("WheelLabel", cu)
    bpy.context.collection.objects.link(ob)
    ob.data.materials.append(material)

    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.convert(target='MESH')
    ob.select_set(False)
    return ob


def _wheel_label(disc, text, angle, radius, face_y, flip, material, size=0.30):
    """
    Подпись сектора. Отдельный объект, прицепленный к диску - поэтому
    крутится вместе с ним.

    Ставится с обеих сторон колеса: диск объёмный, и сзади он тоже виден,
    а надпись, положенная лишь на одну грань, с другой стороны пропадала бы.
    """
    ob = _text_object(text, size, material)

    # Текст рождается плашмя лицом вверх: ставим вертикально и доворачиваем
    # по сектору. Плюс 90 градусов - чтобы верх буквы смотрел наружу от
    # центра, а не по касательной: тогда сектор, вставший под стрелку,
    # читается ровно, как на настоящем призовом колесе.
    q = (Quaternion((0, 1, 0), angle + math.pi * 0.5) @
         Quaternion((1, 0, 0), math.radians(90)))
    if flip:
        q = q @ Quaternion((0, 1, 0), math.pi)

    ob.rotation_mode = 'QUATERNION'
    ob.rotation_quaternion = q
    ob.location = (disc.location.x + math.cos(angle) * radius,
                   face_y,
                   disc.location.z - math.sin(angle) * radius)

    ob.parent = disc
    # обратную матрицу строим из координат диска: matrix_world у только что
    # созданного объекта ещё не пересчитан, и смещение применилось бы дважды
    ob.matrix_parent_inverse = Matrix.Translation(-disc.location)
    return ob


def build_fortune_wheel():
    """Колесо фортуны во дворе: подходи и крути на свой страх."""
    wx, wy = WHEEL

    # Стойка стоит позади диска (игрок подходит со стороны -Y), иначе она
    # перекрывает секторы и колесо не прочитать.
    post_y = wy + 0.30
    obj_from(bm_box(1.30, 1.30, 0.18, 0.04), "WheelBase", mat['stone'],
             loc=(wx, post_y, 0.09))
    obj_from(bm_cyl(0.17, 0.21, WHEEL_HUB_Z, 14, 0.03), "WheelPost", mat['wood'],
             loc=(wx, post_y, WHEEL_HUB_Z * 0.5), smooth=True, angle=45)

    # Диск делаем объёмным телом, а не плоским кольцом секторов: плоскость
    # видна лишь с одной стороны, и сзади колесо просвечивало насквозь.
    # Строим плашмя в XY и ставим вертикально - секторы удобно резать
    # через add_annulus, а он работает именно плашмя.
    thick = 0.12
    colour = {0: 1, 2: 2, 3: 3}                      # выплата -> слот материала
    n = len(WHEEL_PAYOUTS)

    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=48,
                          radius1=WHEEL_R, radius2=WHEEL_R, depth=thick)
    # цветные секторы кладём на обе грани, чуть выступая над телом
    for z, flip in ((thick * 0.5 + 0.004, False), (-thick * 0.5 - 0.004, True)):
        add_annulus(bm, 0.0, WHEEL_R * 0.17, 0, TAU, z, 24, 0, flip)
        for i, payout in enumerate(WHEEL_PAYOUTS):
            a0 = TAU * i / n
            add_annulus(bm, WHEEL_R * 0.17, WHEEL_R, a0, a0 + TAU / n, z, 6,
                        colour[payout], flip)
    bm_rotate(bm, math.radians(-90), 'X')

    disc = obj_from(bm, "FortuneWheel",
                    [mat['metal_d'], mat['maroon'], mat['yellow'], mat['green']],
                    loc=(wx, wy, WHEEL_HUB_Z), smooth=False)

    # Ориентиры для Unity: пустышки в середине нулевого и первого секторов.
    # По ним игра сама измеряет, где какой сектор и в какую сторону они идут,
    # вместо того чтобы выводить это формулой через пересчёт осей при
    # экспорте - на таком выводе легко ошибиться знаком, и тогда колесо
    # встаёт на одном секторе, а засчитывается другой.
    for k in (0, 1):
        am = TAU * k / n + TAU / (2 * n)
        mark = bpy.data.objects.new("WheelSector%d" % k, None)
        bpy.context.collection.objects.link(mark)
        mark.empty_display_size = 0.12
        mark.location = (wx + math.cos(am) * WHEEL_R * 0.75, wy,
                         WHEEL_HUB_Z - math.sin(am) * WHEEL_R * 0.75)
        mark.parent = disc
        mark.matrix_parent_inverse = Matrix.Translation(-disc.location)

    # Подписи - отдельными объектами на обеих сторонах диска. Прицеплены к
    # нему, поэтому крутятся вместе с колесом.
    for i, payout in enumerate(WHEEL_PAYOUTS):
        am = TAU * i / n + TAU / (2 * n)
        for face_y, flip in ((wy - thick * 0.5 - 0.02, False),
                             (wy + thick * 0.5 + 0.02, True)):
            _wheel_label(disc, "%dx" % payout, am, WHEEL_R * 0.60,
                         face_y, flip, mat['cream'])

    # обод и колышки между секторами - по ним щёлкает стрелка
    obj_from(bm_tube(WHEEL_R, WHEEL_R + 0.09, 0.14, 40), "WheelRim", mat['metal'],
             loc=(wx, wy, WHEEL_HUB_Z), rot=(math.radians(90), 0, 0),
             smooth=True, angle=50)
    for i in range(n):
        a = TAU * i / n
        obj_from(bm_cyl(0.022, 0.022, 0.16, 8, 0.0), "WheelPeg", mat['chrome'],
                 loc=(wx + math.cos(a) * (WHEEL_R - 0.05), wy,
                      WHEEL_HUB_Z + math.sin(a) * (WHEEL_R - 0.05)),
                 rot=(math.radians(90), 0, 0), smooth=True, angle=50)

    # стрелка сверху
    obj_from(bm_prism([(-0.10, 0.0), (0.10, 0.0), (0.0, -0.30)], 0.06, 0.015),
             "WheelPointer", mat['red'],
             loc=(wx, wy - 0.10, WHEEL_HUB_Z + WHEEL_R + 0.20))


def build_decor():
    # string lights over the bluff table
    tx, ty = TBL
    poles = [(tx - 5.5, ty - 4.0), (tx + 5.5, ty - 4.0),
             (tx + 5.5, ty + 4.5), (tx - 5.5, ty + 4.5)]
    for p in poles:
        obj_from(bm_box(0.20, 0.20, 4.9, 0.04), "LightPole", mat['wood'],
                 loc=(p[0], p[1], 2.45))
    bulb = obj_from(bm_blobs([(0.085, (0, 0, 0), 1.2)], 0, 1, 0, 3), "Bulb",
                    mat['bulb'], loc=(0, 0, -200), angle=180)
    for i in range(len(poles)):
        a, b = poles[i], poles[(i + 1) % len(poles)]
        n = 11
        for j in range(1, n):
            t = j / n
            bx = a[0] + (b[0] - a[0]) * t
            by = a[1] + (b[1] - a[1]) * t
            sag = 0.75 * 4 * t * (1 - t)
            dup(bulb, (bx, by, 4.72 - sag), angle=180)
    for p in poles:
        ld = bpy.data.lights.new("Party", type='POINT')
        ld.energy = 210.0; ld.color = (1.0, 0.78, 0.48); ld.shadow_soft_size = 1.2
        lo = bpy.data.objects.new("Party", ld)
        bpy.context.collection.objects.link(lo)
        lo.location = (p[0] * 0.6, p[1] * 0.6 + ty * 0.4, 4.4)
    # bbq grill with a looping smoke plume
    gx, gy = 10.0, 10.0
    obj_from(bm_boxes([((1.5, 0.95, 0.62), (0, 0, 0.75)),
                       ((1.6, 1.05, 0.12), (0, 0, 1.12)),
                       ((0.14, 0.14, 0.9), (-0.6, -0.35, 0.0)),
                       ((0.14, 0.14, 0.9), (0.6, -0.35, 0.0)),
                       ((0.14, 0.14, 0.9), (0.0, 0.4, 0.0))], 0.05),
             "Grill", mat['metal_d'], loc=(gx, gy, 0.45))
    RIG['grill_smoke'] = []
    for i in range(4):
        m2 = M("GrillSmoke%d" % i, (0.85, 0.85, 0.88), 1.0, spec=0.05, alpha=0.35)
        s = obj_from(bm_blobs([(0.30, (0, 0, 0), 0.85),
                               (0.22, (0.26, 0.1, 0.08), 0.85)], 0.14, 1.4, i, 2),
                     "GSmoke%d" % i, m2, loc=(gx, gy, 1.7 + i * 0.9),
                     scale=0.6 + i * 0.25, angle=180)
        RIG['grill_smoke'].append((s, m2, i))
    # cooler + tiki torches
    obj_from(bm_boxes([((1.3, 0.8, 0.7), (0, 0, 0)),
                       ((1.36, 0.86, 0.12), (0, 0, 0.40))], 0.06),
             "Cooler", mat['blue'], loc=(-3.5, -1.0, 0.35))
    for tp in ((-10, -6), (10, -6), (-10, 13), (10, 13), (-21, -8), (21, -6)):
        obj_from(bm_cyl(0.10, 0.13, 2.4, 12, 0.03), "Torch", mat['bark'],
                 loc=(tp[0], tp[1], 1.2), smooth=True, angle=50)
        obj_from(bm_blobs([(0.15, (0, 0, 0), 1.6)], 0.12, 3.0, 2, 3), "Flame",
                 mat['flame'], loc=(tp[0], tp[1], 2.55), angle=180)
    # trees around the plot
    trunk = obj_from(bm_cyl(0.42, 0.28, 3.4, 12, 0.06), "Trunk", mat['bark'],
                     loc=(0, 0, -200), smooth=True, angle=50)
    can1 = obj_from(bm_blobs([(2.4, (0, 0, 0), 0.86), (1.7, (1.4, 0.6, -0.6), 0.9),
                              (1.6, (-1.3, -0.7, -0.4), 0.9)], 0.16, 0.75, 4, 3),
                    "Canopy", mat['leafA'], loc=(0, 0, -200), angle=180)
    can2 = obj_from(bm_blobs([(2.1, (0, 0, 0), 0.95), (1.6, (-1.2, 0.9, -0.3), 0.9),
                              (1.5, (1.1, -0.9, 0.4), 0.9)], 0.16, 0.8, 9, 3),
                    "Canopy2", mat['leafB'], loc=(0, 0, -200), angle=180)
    spots = [(-32, -30), (-33, 2), (-30, 26), (-20, 34), (34, 10), (34, -2),
             (30, -30), (20, -35), (-22, -35), (24, 8), (-26, -14), (-24, 18)]
    spots = [p for p in spots if off_flight_path(p[0], p[1])]
    for i, (x, y) in enumerate(spots):
        s = random.uniform(0.85, 1.3)
        dup(trunk, (x, y, 1.7 * s), scale=s, angle=50)
        dup(can1 if i % 2 else can2, (x, y, (3.4 + 1.7) * s), scale=s, angle=180)
    for i in range(40):
        x = random.uniform(-120, 120); y = random.uniform(-120, 120)
        if max(abs(x), abs(y)) < FENCE + 6: continue
        if not off_flight_path(x, y): continue
        s = random.uniform(0.8, 1.5); z = terrain_z(x, y)
        dup(trunk, (x, y, z + 1.7 * s), scale=s, angle=50)
        dup(can1 if i % 2 else can2, (x, y, z + 5.1 * s), scale=s, angle=180)
    # stone path from the gate to the patio
    stone = obj_from(bm_blobs([(0.70, (0, 0, 0), 0.14)], 0.18, 2.0, 3, 3), "PathStone",
                     mat['stone'], loc=(0, 0, -200), smooth=True, angle=45)
    # the path must not run through any game station
    KEEPOUT = [(TBL[0], TBL[1], 3.4), (PPOOL[0], PPOOL[1], PPOOL_R + 1.3),
               (BP[0], BP[1], 3.2), (SLING[0], SLING[1], 2.6),
               (CANNON[0], CANNON[1], 2.4)]
    yy = -FENCE + 1.0
    while yy < 15.0:
        t = (yy + FENCE) / 53.0
        off = HX * t - math.sin(t * math.pi) * 4.5     # bows around the table
        skip = any(math.hypot(off - kx, yy - ky) < kr for kx, ky, kr in KEEPOUT)
        if not skip:
            for d in (-0.7, 0.7):
                dup(stone, (off + d + random.uniform(-0.1, 0.1), yy, 0.05),
                    rot=(0, 0, random.uniform(0, 3.14)),
                    scale=random.uniform(0.85, 1.2), angle=45)
        yy += 1.35

# ============================================================ character motion
def snap_root(P, f, loc, rz):
    """Hard cut of a character to another station, hidden by the camera cut."""
    r = P['root']
    r.keyframe_insert("location", frame=f - 1)
    r.keyframe_insert("rotation_euler", frame=f - 1)
    r.location = loc
    r.rotation_euler = (0, 0, rz)
    r.keyframe_insert("location", frame=f)
    r.keyframe_insert("rotation_euler", frame=f)
    P['loc'] = loc; P['rz'] = rz          # 'home' stays the table seat
    for fc in fcurves_of(r):
        for kp in fc.keyframe_points:
            if abs(kp.co[0] - (f - 1)) < 0.01:
                kp.interpolation = 'CONSTANT'

def throw(P, side, f0, f_cock, f_rel, prop=None, cock=-2.4, rel=1.0,
          hold_quat=None, follow=True):
    """Wind-up + release. The prop stays glued to the hand until f_rel."""
    arm = P['arm_' + side]
    sh = P['shoulder_' + side]
    pts = [(f0, 0.0), (f_cock, cock), (f_rel, rel)]
    for f, a in pts:
        arm.rotation_euler = (a, 0, 0)
        arm.keyframe_insert("rotation_euler", frame=f)
    if follow:
        arm.rotation_euler = (rel + 0.35, 0, 0)
        arm.keyframe_insert("rotation_euler", frame=f_rel + 9)
        arm.rotation_euler = (0.0, 0, 0)
        arm.keyframe_insert("rotation_euler", frame=f_rel + 34)
    set_interp(arm, 'LINEAR', only_after=f0 - 0.5)
    if prop is not None:
        prop.rotation_mode = 'QUATERNION'
        q = hold_quat or Quaternion((1, 0, 0, 0))
        for i in range(len(pts) - 1):
            fa, aa = pts[i]; fb, ab = pts[i + 1]
            for f in range(fa, fb + 1):
                t = (f - fa) / max(1, fb - fa)
                a = aa + (ab - aa) * t
                key(prop, f, loc=hand_pos(P['loc'], P['rz'], sh, a), quat=q)
        set_interp(prop, 'LINEAR', only_after=f0 - 0.5)
    return hand_pos(P['loc'], P['rz'], sh, rel)

def wobble(ob, f0, base_rot, amp, cycles=4, span=26):
    """Damped shake for a struck target."""
    for i in range(cycles * 2 + 1):
        t = i / (cycles * 2.0)
        a = amp * (1.0 - t) * (1 if i % 2 == 0 else -1)
        key(ob, f0 + int(span * t),
            rot=(base_rot[0], base_rot[1] + a, base_rot[2] + a * 0.6))

def face_dir(dx, dy):
    """Root Z rotation so the character's local +Y points along (dx, dy).
    local +Y rotated by rz is (-sin rz, cos rz), so rz = atan2(-dx, dy)."""
    return math.atan2(-dx, dy)

# ============================================================ build the world
reset()
build_mats()
build_ground()
build_fence()
build_house()
build_bluff_table()
build_darts()
build_billiards()
build_beerpong()
build_axe()
build_slingshot()
build_bowling()
build_cannon()
build_piranha_pool()
build_fortune_wheel()
build_decor()

# ============================================================ dressing the yard
FIRE = (-14.0, -4.0)
BAR = (9.0, 14.0)
SCORE = (6.5, 9.5)
HAMMOCK = ((-27.0, -2.0), (-27.0, -8.5))

# circles the scatter must stay out of: every station, plus hand-placed props
STATIONS = [
    (TBL[0], TBL[1], 4.2), (POOL[0], POOL[1], 5.0),
    (DART_BOARD[0], DART_BOARD[1], 2.6), (DART_LINE[0], DART_LINE[1], 2.2),
    (BP[0], BP[1], 3.6), (AXE_T[0], AXE_T[1], 2.6), (AXE_LINE[0], AXE_LINE[1], 2.2),
    (SLING[0], SLING[1], 2.8), (CANS[0], CANS[1], 3.2),
    (PPOOL[0], PPOOL[1], PPOOL_R + 2.0), (CANNON[0], CANNON[1], 3.2),
    (FIRE[0], FIRE[1], 4.0), (BAR[0], BAR[1], 3.4), (SCORE[0], SCORE[1], 2.2),
    (10.0, 10.0, 2.6),                                     # bbq grill
    (HAMMOCK[0][0], HAMMOCK[0][1], 2.0), (HAMMOCK[1][0], HAMMOCK[1][1], 2.0),
]
CAM_LINES = [((16.4, -7.6), (16.4, 2.0)), ((-11.0, 10.4), (-18.2, 10.0)),
             ((-6.0, -16.6), (-10.0, -12.2)), ((18.8, -19.0), (18.8, -12.0)),
             ((-12.5, -31.8), (-12.5, -22.0)), ((-14.6, -27.6), (-18.6, -22.0)),
             ((15.2, -36.6), (5.5, -29.5)), ((26.0, -30.0), (0.0, 6.0)),
             ((64.0, -60.0), (0.0, 6.0)), ((40.0, -42.0), (0.0, 6.0)),
             ((2.6, 1.0), (0.0, 4.0))]

def blocks_cam(x, y, half=2.6):
    for (cx, cy), (tx, ty) in CAM_LINES:
        d = Vector((tx - cx, ty - cy)); L = d.length
        if L < 1e-6: continue
        d = d / L
        p = Vector((x - cx, y - cy))
        t = p.dot(d)
        if t < -1.0 or t > L + 2.0: continue
        if abs(p.x * -d.y + p.y * d.x) < half: return True
    return False

def spot_free(x, y, pad=0.0, tall=False, cam_half=2.6):
    for sx, sy, sr in STATIONS:
        if math.hypot(x - sx, y - sy) < sr + pad: return False
    if -15.0 < x < 3.0 and 19.0 < y < 33.0: return False       # house
    if 1.0 < x < 31.0 and 19.0 < y < 30.5: return False        # annex
    if -17.0 < x < 6.0 and 14.0 < y < 21.0: return False       # patio
    if max(abs(x), abs(y)) > FENCE - 2.2: return False
    t = (y + FENCE) / 53.0
    if 0.0 <= t <= 1.0:
        if abs(x - (HX * t - math.sin(t * math.pi) * 4.5)) < 1.7: return False
    if tall and blocks_cam(x, y, cam_half): return False
    return True

def build_dressing():
    # ---------- prototypes reused by the scatters ----------
    cup = obj_from(bm_cyls([(0.075, 0.095, 0.20, (0, 0, 0), 14)], 0.012),
                   "LooseCup", mat['red'], loc=(0, 0, -200), smooth=True, angle=50)
    can = obj_from(bm_cyls([(0.062, 0.062, 0.16, (0, 0, 0), 14)], 0.012),
                   "LooseCan", mat['chrome'], loc=(0, 0, -200), smooth=True, angle=50)
    can2 = obj_from(bm_cyls([(0.062, 0.062, 0.16, (0, 0, 0), 14)], 0.012),
                    "LooseCan2", mat['blue'], loc=(0, 0, -200), smooth=True, angle=50)
    btl = obj_from(bm_cyls([(0.062, 0.062, 0.22, (0, 0, 0), 12),
                            (0.062, 0.028, 0.08, (0, 0, 0.15), 12),
                            (0.028, 0.028, 0.09, (0, 0, 0.23), 12)], 0.012),
                   "LooseBottle", mat['beer'], loc=(0, 0, -200), smooth=True, angle=50)
    plate = obj_from(bm_cyl(0.16, 0.16, 0.02, 18, 0.006), "Plate", mat['white'],
                     loc=(0, 0, -200), smooth=True, angle=50)
    crate = obj_from(bm_boxes([((0.62, 0.62, 0.10), (0, 0, -0.22)),
                               ((0.62, 0.10, 0.46), (0, 0.26, 0)),
                               ((0.62, 0.10, 0.46), (0, -0.26, 0)),
                               ((0.10, 0.62, 0.46), (0.26, 0, 0)),
                               ((0.10, 0.62, 0.46), (-0.26, 0, 0))], 0.03),
                     "Crate", mat['wood_l'], loc=(0, 0, -200))
    pallet = obj_from(bm_boxes(
        [((1.2, 0.16, 0.09), (0, -0.42, 0.09)), ((1.2, 0.16, 0.09), (0, 0, 0.09)),
         ((1.2, 0.16, 0.09), (0, 0.42, 0.09))] +
        [((0.14, 1.05, 0.09), (-0.5 + i * 0.5, 0, 0.0)) for i in range(3)], 0.02),
        "Pallet", mat['wood_l'], loc=(0, 0, -200))
    tire = obj_from(bm_tube(0.24, 0.46, 0.26, 26), "Tire", mat['rubber'],
                    loc=(0, 0, -200), smooth=True, angle=45)
    keg = obj_from(bm_cyls([(0.28, 0.28, 0.74, (0, 0, 0), 20),
                            (0.22, 0.22, 0.08, (0, 0, 0.40), 20),
                            (0.31, 0.31, 0.07, (0, 0, 0.16), 20),
                            (0.31, 0.31, 0.07, (0, 0, -0.20), 20)], 0.02),
                  "Keg", mat['chrome'], loc=(0, 0, -200), smooth=True, angle=45)
    bale = obj_from(bm_boxes([((1.05, 0.72, 0.66), (0, 0, 0))] +
                             [((1.07, 0.06, 0.06), (0, -0.2 + i * 0.2, 0.34))
                              for i in range(3)], 0.10, 3),
                    "StrawBale", mat['yellow'], loc=(0, 0, -200), angle=45)
    log = obj_from(bm_cyl(0.28, 0.26, 1.10, 14, 0.04), "Log", mat['bark'],
                   loc=(0, 0, -200), smooth=True, angle=45)
    lantern = obj_from(bm_cyls([(0.05, 0.05, 0.55, (0, 0, 0), 8)], 0.0),
                       "LanternPost", mat['metal_d'], loc=(0, 0, -200),
                       smooth=True, angle=60)
    lantern_top = obj_from(bm_blobs([(0.11, (0, 0, 0), 1.1)], 0, 1, 0, 2),
                           "LanternTop", mat['bulb'], loc=(0, 0, -200), angle=180)
    tuft = obj_from(bm_cyls([(0.055, 0.005, 0.44, (0.05, 0.02, 0), 5),
                             (0.05, 0.005, 0.38, (-0.06, 0.04, -0.03), 5),
                             (0.05, 0.005, 0.34, (0.01, -0.07, -0.05), 5)], 0.0),
                    "GrassTuft", mat['leafA'], loc=(0, 0, -200), smooth=True, angle=60)
    bushes = [obj_from(bm_blobs([(0.78, (0, 0, 0), 0.72), (0.54, (0.6, 0.3, -0.12), 0.8),
                                 (0.5, (-0.55, -0.26, -0.08), 0.8)], 0.18, 1.4, 60 + i, 3),
                       "DBush%d" % i, mat['leafA'] if i % 2 else mat['leafB'],
                       loc=(0, 0, -200), angle=180) for i in range(3)]
    rock = obj_from(bm_blobs([(0.42, (0, 0, 0), 0.66)], 0.28, 2.4, 12, 3), "DRock",
                    mat['stone'], loc=(0, 0, -200), smooth=True, angle=50)
    flowers = [obj_from(bm_blobs([(0.10, (0, 0, 0), 0.7), (0.05, (0, 0, 0.06), 1.0)],
                                 0, 1, 0, 2), "DFlower%d" % i, m,
                        loc=(0, 0, -200), angle=180)
               for i, m in enumerate([mat['red'], mat['yellow'], mat['white'],
                                      mat['purple']])]

    # ---------- station aprons: gravel pads, bales, marker lines ----------
    def apron(cx, cy, sx, sy, rz=0.0):
        obj_from(bm_box(sx, sy, 0.09, 0.05), "Apron", mat['stone'],
                 loc=(cx, cy, 0.045), rot=(0, 0, rz))
    apron(DART_LINE[0] + 1.6, DART_LINE[1], 5.0, 4.2)
    apron(AXE_LINE[0] + 1.4, AXE_LINE[1], 4.6, 4.0)
    apron(SLING[0] + 0.6, SLING[1], 4.0, 3.6)
    apron(BP[0], BP[1], 3.4, 6.4)
    apron(POOL[0], POOL[1], 6.2, 8.6)
    apron(PPOOL[0], PPOOL[1] + PPOOL_R + 1.6, 7.0, 2.6)
    for lx, ly in ((DART_LINE[0], DART_LINE[1]), (AXE_LINE[0], AXE_LINE[1])):
        obj_from(bm_box(0.14, 3.4, 0.03, 0.01), "ThrowLine", mat['white'],
                 loc=(lx - 0.5, ly, 0.10))
    # straw bales backing the two throwing targets
    # both throwing cameras sit on the -y side, so stack the bales on the far
    # side only - otherwise they cover the target
    for tx, ty in ((DART_BOARD[0], DART_BOARD[1]), (AXE_T[0], AXE_T[1])):
        for i, (dy, dz) in enumerate(((2.2, 0.33), (2.2, 0.99), (3.2, 0.33))):
            dup(bale, (tx + 0.25, ty + dy, dz), rot=(0, 0, math.radians(90)),
                angle=45)
        dup(bale, (tx + 1.4, ty - 2.4, 0.33), rot=(0, 0, math.radians(90)),
            angle=45)
    for s in (-1, 1):                                   # crates flanking the cans
        for k in range(2):
            dup(crate, (CANS[0] + 0.9, CANS[1] + s * (2.4 + k * 0.1),
                        0.25 + k * 0.55), rot=(0, 0, random.uniform(0, 3)))

    # ---------- fire pit with log seats ----------
    fx, fy = FIRE
    obj_from(bm_cyl(1.5, 1.5, 0.12, 30, 0.03), "FireBase", mat['dirt'],
             loc=(fx, fy, 0.06), smooth=True, angle=50)
    for i in range(14):
        a = TAU * i / 14
        dup(rock, (fx + math.cos(a) * 1.45, fy + math.sin(a) * 1.45, 0.14),
            rot=(0, 0, random.uniform(0, 3)), scale=random.uniform(0.6, 0.95), angle=50)
    for i in range(5):                                   # criss-crossed firewood
        a = TAU * i / 5
        obj_from(bm_cyl(0.09, 0.07, 1.0, 10, 0.02), "Firewood", mat['bark'],
                 loc=(fx + math.cos(a) * 0.16, fy + math.sin(a) * 0.16, 0.34),
                 rot=(math.radians(66), 0, a), smooth=True, angle=45)
    obj_from(bm_blobs([(0.42, (0, 0, 0), 1.7), (0.26, (0.2, 0.16, 0.4), 1.5)],
                      0.22, 2.6, 4, 3), "Campfire", mat['flame'],
             loc=(fx, fy, 0.75), angle=180)
    fl = bpy.data.lights.new("FireLight", type='POINT')
    fl.energy = 420.0; fl.color = (1.0, 0.60, 0.25); fl.shadow_soft_size = 0.7
    flo = bpy.data.objects.new("FireLight", fl)
    bpy.context.collection.objects.link(flo)
    flo.location = (fx, fy, 0.95)
    for i in range(5):                                   # log seats around it
        a = TAU * i / 5 + 0.4
        dup(log, (fx + math.cos(a) * 2.9, fy + math.sin(a) * 2.9, 0.28),
            rot=(0, math.radians(90), a + math.radians(90)), angle=45)

    # ---------- pallet bar ----------
    bx, by = BAR
    obj_from(bm_boxes([((3.4, 0.7, 1.15), (0, 0, 0)),
                       ((3.7, 1.0, 0.14), (0, -0.05, 0.64)),
                       ((3.4, 0.12, 0.5), (0, -0.4, -0.15))], 0.05),
             "BarCounter", mat['wood_l'], loc=(bx, by, 0.58))
    for i in range(3):
        dup(keg, (bx - 1.1 + i * 1.1, by + 1.0, 0.37), angle=45)
    for i in range(7):
        dup(btl, (bx - 1.4 + i * 0.46, by - 0.1, 1.36),
            rot=(0, 0, random.uniform(0, 3)), angle=50)
    for i in range(6):
        dup(cup, (bx - 1.2 + i * 0.42, by + 0.22, 1.32), angle=50)
    for s in (-1, 1):                                    # bar stools
        obj_from(bm_cyls([(0.20, 0.20, 0.09, (0, 0, 0.36), 16),
                          (0.06, 0.08, 0.72, (0, 0, 0), 10)], 0.02),
                 "BarStool", mat['metal'], loc=(bx + s * 1.1, by - 1.3, 0.4),
                 smooth=True, angle=45)
    dup(pallet, (bx + 2.4, by + 0.6, 0.05), rot=(0, 0, math.radians(20)))
    dup(crate, (bx + 2.5, by + 0.5, 0.32), rot=(0, 0, math.radians(15)))

    # ---------- scoreboard ----------
    sx2, sy2 = SCORE
    face = math.radians(-24)
    for s in (-1, 1):
        obj_from(bm_box(0.20, 0.20, 3.0, 0.04), "ScorePost", mat['wood'],
                 loc=(sx2 + math.cos(face + math.pi / 2) * s * 1.5,
                      sy2 + math.sin(face + math.pi / 2) * s * 1.5, 1.5))
    obj_from(bm_box(3.3, 0.16, 1.9, 0.06), "ScoreBoard", mat['black'],
             loc=(sx2, sy2, 2.35), rot=(0, 0, face))
    obj_from(bm_box(3.5, 0.10, 0.22, 0.04), "ScoreTitle", mat['red'],
             loc=(sx2 - math.sin(face) * -0.10, sy2 + math.cos(face) * -0.10, 3.14),
             rot=(0, 0, face))
    for r in range(4):                                   # name plates + score chips
        for c2 in range(2):
            w = 1.25 if c2 == 0 else 0.42
            ox = -0.85 + c2 * 1.30
            oy = 0.62 - r * 0.40
            obj_from(bm_box(w, 0.06, 0.24, 0.02), "ScoreRow",
                     [mat['white'], mat['yellow']][c2],
                     loc=(sx2 + ox * math.cos(face) - 0.10 * -math.sin(face),
                          sy2 + ox * math.sin(face) - 0.10 * math.cos(face),
                          2.35 + oy), rot=(0, 0, face))

    # ---------- hammock between two planted trees ----------
    trunk2 = obj_from(bm_cyl(0.42, 0.30, 3.6, 12, 0.06), "HamTrunk", mat['bark'],
                      loc=(0, 0, -200), smooth=True, angle=50)
    can_h = obj_from(bm_blobs([(2.3, (0, 0, 0), 0.86), (1.6, (1.3, 0.5, -0.5), 0.9)],
                              0.16, 0.75, 21, 3), "HamCanopy", mat['leafA'],
                     loc=(0, 0, -200), angle=180)
    for hp in HAMMOCK:
        dup(trunk2, (hp[0], hp[1], 1.8), angle=50)
        dup(can_h, (hp[0], hp[1], 5.1), angle=180)
    hb = bmesh.new()
    ax, ay = HAMMOCK[0]; bx2, by2 = HAMMOCK[1]
    seg = 14
    rows = []
    for i in range(seg + 1):
        t = i / seg
        px = ax + (bx2 - ax) * t
        py = ay + (by2 - ay) * t
        pz = 1.9 - math.sin(t * math.pi) * 0.85
        rows.append([hb.verts.new((px, py - 0.45, pz)),
                     hb.verts.new((px, py + 0.45, pz))])
    for i in range(seg):
        hb.faces.new((rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0]))
    bmesh.ops.recalc_face_normals(hb, faces=hb.faces[:])
    obj_from(hb, "Hammock", mat['shirtB'], smooth=True, angle=80)

    # ---------- sun loungers by the pool ----------
    for i, (lx, ly, lr) in enumerate(((-4.6, -27.0, 0.5), (-6.4, -24.4, 0.9),
                                      (9.2, -31.4, -2.4))):
        obj_from(bm_boxes([((1.9, 0.72, 0.10), (0, 0, 0)),
                           ((0.62, 0.72, 0.10), (1.05, 0, 0.26), 0),
                           ((0.10, 0.10, 0.36), (-0.8, 0.3, -0.23)),
                           ((0.10, 0.10, 0.36), (-0.8, -0.3, -0.23)),
                           ((0.10, 0.10, 0.36), (0.8, 0.3, -0.23)),
                           ((0.10, 0.10, 0.36), (0.8, -0.3, -0.23))], 0.04),
                 "Lounger", [mat['white'], mat['shirtB'], mat['shirtC']][i % 3],
                 loc=(lx, ly, 0.55), rot=(0, 0, lr))
    obj_from(bm_tube(0.42, 0.72, 0.34, 26), "PoolRing", mat['orange'],
             loc=(PPOOL[0] - 2.2, PPOOL[1] + 1.4, 1.5), smooth=True, angle=45)
    obj_from(bm_boxes([((0.9, 0.06, 0.9), (0, 0, 0))], 0.02), "Towel",
             mat['red'], loc=(-4.6, -27.0, 0.66), rot=(0, 0, 0.5))

    # ---------- boombox, cooler stack, wheelbarrow, tires ----------
    obj_from(bm_boxes([((1.05, 0.42, 0.55), (0, 0, 0)),
                       ((1.05, 0.10, 0.14), (0, 0, 0.34))], 0.05),
             "Boombox", mat['metal_d'], loc=(3.4, 0.6, 0.30), rot=(0, 0, -0.5))
    for s in (-1, 1):
        obj_from(bm_cyl(0.17, 0.17, 0.07, 18, 0.02), "Speaker", mat['black'],
                 loc=(3.4 + s * 0.28 * math.cos(-0.5), 0.6 + s * 0.28 * math.sin(-0.5)
                      - 0.21, 0.34), rot=(math.radians(90), 0, -0.5),
                 smooth=True, angle=45)
    for i, (tx2, ty2, tz2) in enumerate(((-20.5, 4.0, 0.15), (-20.5, 4.0, 0.42),
                                         (-19.6, 5.2, 0.15), (23.0, -4.0, 0.15),
                                         (23.6, -5.0, 0.15))):
        dup(tire, (tx2, ty2, tz2), rot=(0, 0, random.uniform(0, 3)), angle=45)
    obj_from(bm_boxes([((0.95, 0.62, 0.34), (0, 0, 0.30)),
                       ((0.09, 1.5, 0.09), (0.3, -0.5, 0.10)),
                       ((0.09, 1.5, 0.09), (-0.3, -0.5, 0.10))], 0.05),
             "Barrow", mat['metal'], loc=(-24.0, 12.0, 0.28), rot=(0, 0, 0.7))
    obj_from(bm_tube(0.10, 0.30, 0.14, 18), "BarrowWheel", mat['rubber'],
             loc=(-23.7, 12.6, 0.30), rot=(math.radians(90), 0, 0.7),
             smooth=True, angle=45)

    # ---------- bunting along the fence + garden lanterns on the path ----------
    flag = obj_from(bm_prism([(-0.24, 0.0), (0.24, 0.0), (0.0, -0.56)], 0.012,
                             0.004, 1), "Flag", mat['red'],
                    loc=(0, 0, -200), smooth=False)
    flag_mats = [mat['red'], mat['yellow'], mat['blue'], mat['white'], mat['green']]
    flags = [flag] + [obj_from(bm_prism([(-0.24, 0.0), (0.24, 0.0), (0.0, -0.56)],
                                        0.012, 0.004, 1), "Flag%d" % i, m,
                               loc=(0, 0, -200), smooth=False)
                      for i, m in enumerate(flag_mats[1:], 1)]
    for side in range(4):
        for i in range(46):
            t = i / 46.0
            span = FENCE * 2
            if side == 0: fx2, fy2 = -FENCE + span * t, -FENCE + 0.35
            elif side == 1: fx2, fy2 = FENCE - 0.35, -FENCE + span * t
            elif side == 2: fx2, fy2 = FENCE - span * t, FENCE - 0.35
            else: fx2, fy2 = -FENCE + 0.35, FENCE - span * t
            sag = math.sin((i % 6) / 6.0 * math.pi) * 0.16
            dup(flags[i % len(flags)], (fx2, fy2, 2.28 - sag),
                rot=(0, 0, random.uniform(-0.2, 0.2)), smooth=False)
    yy = -FENCE + 2.0
    while yy < 14.0:
        t = (yy + FENCE) / 53.0
        px = HX * t - math.sin(t * math.pi) * 4.5
        for s in (-1, 1):
            dup(lantern, (px + s * 1.35, yy, 0.28), angle=60)
            dup(lantern_top, (px + s * 1.35, yy, 0.60), angle=180)
        yy += 4.2

    # ---------- signposts pointing at the stations ----------
    signs = [((6.0, -6.0), (DART_BOARD[0], DART_BOARD[1]), mat['yellow']),
             ((6.0, -6.0), (AXE_T[0], AXE_T[1]), mat['red']),
             ((-4.0, -14.0), (CANS[0], CANS[1]), mat['blue']),
             ((-4.0, -14.0), (PPOOL[0], PPOOL[1]), mat['green'])]
    obj_from(bm_cyl(0.10, 0.10, 2.6, 10, 0.02), "SignPost", mat['wood'],
             loc=(6.0, -6.0, 1.3), smooth=True, angle=50)
    obj_from(bm_cyl(0.10, 0.10, 2.6, 10, 0.02), "SignPost2", mat['wood'],
             loc=(-4.0, -14.0, 1.3), smooth=True, angle=50)
    for i, (base, tgt2, m) in enumerate(signs):
        ang = math.atan2(tgt2[1] - base[1], tgt2[0] - base[0])
        obj_from(bm_prism([(-0.30, -0.16), (0.30, -0.16), (0.46, 0.0),
                           (0.30, 0.16), (-0.30, 0.16)], 0.06, 0.02, 1),
                 "Sign%d" % i, m,
                 loc=(base[0] + math.cos(ang) * 0.42,
                      base[1] + math.sin(ang) * 0.42, 2.35 - (i % 2) * 0.46),
                 rot=(0, 0, ang), smooth=False)

    # ---------- party litter around the social spots ----------
    litter = [cup, cup, cup, can, can2, btl, plate]
    PARTY = [(TBL[0], TBL[1], 2.6, 6.5), (FIRE[0], FIRE[1], 3.4, 6.5),
             (BAR[0], BAR[1], 2.4, 5.0), (BP[0], BP[1], 2.6, 5.5),
             (PPOOL[0], PPOOL[1] + PPOOL_R + 1.0, 1.0, 3.5)]
    placed_litter = 0
    tries = 0
    while placed_litter < 74 and tries < 4000:
        tries += 1
        px2, py2, r0, r1 = random.choice(PARTY)
        a = random.uniform(0, TAU)
        r = random.uniform(r0, r1)
        x, y = px2 + math.cos(a) * r, py2 + math.sin(a) * r
        if not spot_free(x, y, -1.4): continue
        p = random.choice(litter)
        tipped = random.random() < 0.45
        dup(p, (x, y, 0.05 if tipped else 0.10),
            rot=(math.radians(90) if tipped else 0, 0, random.uniform(0, TAU)),
            scale=random.uniform(0.9, 1.1), angle=50)
        placed_litter += 1

    # ---------- canopy tent with a picnic table under it ----------
    tcx, tcy = -8.0, 9.0
    for sx3 in (-1, 1):
        for sy3 in (-1, 1):
            obj_from(bm_box(0.16, 0.16, 2.9, 0.03), "TentPost", mat['metal'],
                     loc=(tcx + sx3 * 2.1, tcy + sy3 * 2.1, 1.45))
    tent = bmesh.new()
    add_prism(tent, [(-2.5, 0.0), (2.5, 0.0), (0.0, 0.85)], 5.0, (0, 0, 0), 0)
    obj_from(tent, "TentRoof", mat['shirtA'], loc=(tcx, tcy, 2.9),
             rot=(0, 0, math.radians(90)), smooth=False)
    obj_from(bm_boxes([((2.6, 0.95, 0.12), (0, 0, 0.34)),
                       ((2.6, 0.42, 0.10), (0, 0.82, 0.02)),
                       ((2.6, 0.42, 0.10), (0, -0.82, 0.02)),
                       ((0.14, 1.9, 0.42), (-1.05, 0, 0.06)),
                       ((0.14, 1.9, 0.42), (1.05, 0, 0.06))], 0.04),
             "PicnicTable", mat['wood_l'], loc=(tcx, tcy, 0.44))
    for i in range(5):
        dup(btl, (tcx - 0.9 + i * 0.45, tcy + random.uniform(-0.25, 0.25), 0.99),
            angle=50)
    obj_from(bm_blobs([(0.42, (0, 0, 0), 0.62)], 0.05, 2.0, 8, 3), "Watermelon",
             mat['green'], loc=(tcx + 0.9, tcy - 0.2, 1.10), angle=180)
    # ---------- firewood stack + tire swing + tent ----------
    for r3 in range(3):
        for c3 in range(4):
            obj_from(bm_cyl(0.11, 0.10, 0.85, 10, 0.02), "Stack", mat['bark'],
                     loc=(-17.8 - c3 * 0.24, -6.4 + (c3 % 2) * 0.05,
                          0.12 + r3 * 0.23), rot=(math.radians(90), 0, 0.02),
                     smooth=True, angle=45)
    obj_from(bm_boxes([((3.0, 2.6, 0.06), (0, 0, 0))], 0.02), "TentFloor",
             mat['dirt'], loc=(-24.0, -18.0, 0.04))
    dome = bm_blobs([(1.5, (0, 0, 0), 0.85)], 0.06, 1.4, 3, 3)
    for v in dome.verts:
        if v.co.z < 0.0: v.co.z = 0.0
    obj_from(dome, "Tent", mat['shirtD'], loc=(-24.0, -18.0, 0.06), angle=180)
    obj_from(bm_prism([(-0.55, 0.0), (0.55, 0.0), (0.0, 1.05)], 0.06, 0.02, 1),
             "TentDoor", mat['black'], loc=(-24.0, -19.45, 0.08), smooth=False)

    # ---------- trees inside the fence (the yard reads empty without them) ----------
    tr_in = obj_from(bm_cyl(0.40, 0.28, 3.3, 14, 0.06), "YardTrunk", mat['bark'],
                     loc=(0, 0, -200), smooth=True, angle=50)
    cn_in = [obj_from(bm_blobs([(2.35, (0, 0, 0), 0.88), (1.65, (1.35, 0.55, -0.6), 0.9),
                                (1.55, (-1.25, -0.65, -0.4), 0.9),
                                (1.35, (0.25, -1.3, 0.5), 0.9)], 0.16, 0.78, 40 + i, 3),
                      "YardCanopy%d" % i, [mat['leafA'], mat['leafB']][i % 2],
                      loc=(0, 0, -200), angle=180) for i in range(2)]
    yard_trees, guard = [], 0
    while len(yard_trees) < 9 and guard < 5000:
        guard += 1
        x = random.uniform(-FENCE + 4, FENCE - 4)
        y = random.uniform(-FENCE + 4, FENCE - 4)
        if not spot_free(x, y, 3.2, tall=True, cam_half=6.0): continue
        if not off_flight_path(x, y, 20.0): continue
        if any(math.hypot(x - a, y - b) < 9.0 for a, b in yard_trees): continue
        s = random.uniform(0.85, 1.25)
        dup(tr_in, (x, y, 1.65 * s), scale=s, angle=50)
        dup(random.choice(cn_in), (x, y, 4.9 * s), scale=s, angle=180)
        yard_trees.append((x, y))
    if yard_trees:                                       # tire swing on the first one
        sxw, syw = yard_trees[0]
        for dxw in (-0.3, 0.3):
            obj_from(bm_cyl(0.035, 0.035, 2.0, 6, 0.0), "SwingRope", mat['bark'],
                     loc=(sxw + 1.7 + dxw, syw, 2.35), smooth=True, angle=60)
        dup(tire, (sxw + 1.7, syw, 1.35), rot=(math.radians(90), 0, 0.3), angle=45)

    # ---------- greenery: bushes, tufts, flowers, rocks ----------
    def scatter(proto_pick, count, pad, zfun, scale_rng, angle=180, tall=False):
        n, guard = 0, 0
        while n < count and guard < count * 60:
            guard += 1
            x = random.uniform(-FENCE + 2.5, FENCE - 2.5)
            y = random.uniform(-FENCE + 2.5, FENCE - 2.5)
            if not spot_free(x, y, pad, tall): continue
            dup(proto_pick(), (x, y, zfun()), rot=(0, 0, random.uniform(0, TAU)),
                scale=random.uniform(*scale_rng), angle=angle)
            n += 1
    scatter(lambda: random.choice(bushes), 56, 1.2, lambda: 0.48, (0.6, 1.15))
    scatter(lambda: tuft, 210, -0.5, lambda: 0.20, (0.7, 1.5), angle=60)
    scatter(lambda: random.choice(flowers), 96, -0.3, lambda: 0.10, (0.8, 1.6))
    scatter(lambda: rock, 34, 0.6, lambda: 0.12, (0.5, 1.2), angle=50)
    # dense planting hugging the fence line
    for i in range(64):
        side = i % 4
        t = (i // 4) / 16.0 + random.uniform(-0.02, 0.02)
        d = FENCE - random.uniform(1.2, 2.4)
        v = -FENCE + FENCE * 2 * t
        x, y = [(v, -d), (d, v), (v, d), (-d, v)][side]
        if not spot_free(x, y, 0.4): continue
        if random.random() < 0.5:
            dup(random.choice(bushes), (x, y, 0.46),
                rot=(0, 0, random.uniform(0, TAU)),
                scale=random.uniform(0.7, 1.25), angle=180)
        else:
            dup(tuft, (x, y, 0.20), rot=(0, 0, random.uniform(0, TAU)),
                scale=random.uniform(0.9, 1.6), angle=60)
    # worn dirt patches where people stand
    for cx3, cy3, rr in ((DART_LINE[0], DART_LINE[1], 1.5),
                         (AXE_LINE[0], AXE_LINE[1], 1.5),
                         (FIRE[0], FIRE[1], 3.6), (BAR[0], BAR[1], 2.2)):
        obj_from(bm_blobs([(rr, (0, 0, 0), 0.02)], 0.22, 0.9, int(rr * 7), 3),
                 "WornPatch", mat['dirt'], loc=(cx3, cy3, 0.03),
                 smooth=True, angle=60)

build_dressing()

PL = []
# hair_style/brow/stocky/cap_mat give each seat a distinct silhouette+expression:
# Bo - capped, calm; Mia - ponytail, cheerful; Rex - messy-haired, scowling
# and stockier; Sam - capped (different colour), calm.
specs = [("Bo", mat['shirtA'], mat['jeans'], mat['skin1'], mat['hair1'], True,
          'short', 'normal', False, mat['red']),
         ("Mia", mat['shirtB'], mat['shorts'], mat['skin2'], mat['hair2'], False,
          'pony', 'raised', False, None),
         ("Rex", mat['shirtC'], mat['jeans'], mat['skin3'], mat['hair1'], False,
          'messy', 'angry', True, None),
         ("Sam", mat['shirtD'], mat['shorts'], mat['skin1'], mat['hair1'], True,
          'short', 'normal', False, mat['blue'])]
for i, (nm, sh, pt, sk, hr, cap, hstyle, brow, stocky, cap_mat) in enumerate(specs):
    a = TAU * i / 4 + math.radians(45)
    px = TBL[0] + math.cos(a) * 2.28
    py = TBL[1] + math.sin(a) * 2.28
    P = make_character(nm, sh, pt, sk, hr, cap, hair_style=hstyle, brow=brow,
                        stocky=stocky, cap_mat=cap_mat)
    place(P, (px, py, 0.0), face_dir(-math.cos(a), -math.sin(a)))
    PL.append(P)
BO, MIA, REX, SAM = PL

# ============================================================ timeline
F_END = 1090
SEG = {'est': (1, 120), 'cards': (121, 260), 'darts': (261, 380),
       'pool': (381, 500), 'pong': (501, 620), 'axe': (621, 740),
       'sling': (741, 850), 'bowl': (851, 970), 'punish': (971, 1090)}

# ---- ambient loops across the whole timeline
key(RIG['fin_hub'], 1, rot=(0, 0, 0))
key(RIG['fin_hub'], F_END, rot=(0, 0, TAU * 3.0))
set_interp(RIG['fin_hub'], 'LINEAR')

for smk, smat, si in RIG['grill_smoke']:
    period = 100
    c = 0
    while True:
        f0 = c * period + si * 25 + 1
        if f0 > F_END: break
        key(smk, f0, loc=(10.0, 10.0, 1.55), scale=0.30)
        key_alpha(smat, f0, 0.0)
        key_alpha(smat, f0 + 22, 0.40)
        key(smk, f0 + period - 8, loc=(10.9, 10.5, 5.4), scale=1.55)
        key_alpha(smat, f0 + period - 8, 0.0)
        c += 1

# ---- 02 CARDS: cup slam, reveal, card flip, chip toss ---------------
f0, f1 = SEG['cards']
cup = RIG['cup']
cup_home = tuple(cup.location)
key(cup, f0, loc=cup_home)
key(cup, f0 + 8, loc=(cup_home[0], cup_home[1], cup_home[2] + 0.75))
key(cup, f0 + 16, loc=cup_home)                       # slam
key(cup, f0 + 19, loc=(cup_home[0], cup_home[1], cup_home[2] + 0.06))
key(cup, f0 + 22, loc=cup_home)
key(cup, f0 + 74, loc=cup_home)
key(cup, f0 + 88, loc=(cup_home[0], cup_home[1], cup_home[2] + 0.62))   # reveal
key(cup, f0 + 120, loc=(cup_home[0], cup_home[1], cup_home[2] + 0.62))
arm_key(BO, 'r', f0, 0.0)
arm_key(BO, 'r', f0 + 8, -0.75)
arm_key(BO, 'r', f0 + 16, 0.35)
arm_key(BO, 'r', f0 + 74, 0.35)
arm_key(BO, 'r', f0 + 88, -0.55)
arm_key(BO, 'r', f0 + 120, 0.0)
for i, d in enumerate(RIG['dice']):                    # dice settle after reveal
    hx, hy, hz = d.location
    key(d, f0 + 88, loc=(hx, hy, hz))
    key(d, f0 + 94, loc=(hx + random.uniform(-0.1, 0.1),
                         hy + random.uniform(-0.1, 0.1), hz + 0.16))
    key(d, f0 + 102, loc=(hx + random.uniform(-0.16, 0.16),
                          hy + random.uniform(-0.16, 0.16), hz))
for i, cd in enumerate(RIG['cards']):                  # flip face-up in sequence
    fs = f0 + 96 + i * 7
    rz = cd.rotation_euler[2]
    key(cd, fs, rot=(math.pi, 0, rz))
    key(cd, fs + 12, rot=(0.0, 0, rz))
for i, ch in enumerate(RIG['chips'][:8]):              # chips tossed to the pot
    p0 = tuple(ch.location)
    p1 = (TBL[0] + random.uniform(-0.35, 0.35),
          TBL[1] + random.uniform(-0.35, 0.35), TBL_TOP + 0.05)
    ballistic(ch, f0 + 118 + i * 2, f0 + 134 + i * 2, p0, p1, 0.55)
for i, P in enumerate(PL):                             # reactions: lean + arms
    b = P['body']
    key(b, f0 + 60, rot=(0, 0, 0))
    key(b, f0 + 92, rot=(-0.16 if i % 2 else 0.13, 0, 0))
    key(b, f0 + 128, rot=(0, 0, 0))
    if P is not BO:
        arm_key(P, 'l', f0 + 86, 0.0)
        arm_key(P, 'l', f0 + 100, -0.9 - 0.2 * i)
        arm_key(P, 'l', f0 + 132, 0.0)

# ---- 03 DARTS -------------------------------------------------------
f0, f1 = SEG['darts']
snap_root(BO, f0, (DART_LINE[0], DART_LINE[1], 0.0), face_dir(1, 0))
dart = RIG['darts'][0]
rel = throw(BO, 'r', f0 + 6, f0 + 32, f0 + 40, prop=dart, cock=-2.35, rel=0.95)
hit = (DART_BOARD[0] - 0.30, DART_BOARD[1] + 0.10, DART_BOARD[2] + 0.05)
ballistic(dart, f0 + 40, f0 + 62, rel, hit, 0.55, spin=((1, 0, 0), 3.0))
key(dart, f0 + 78, loc=hit, quat=Quaternion((1, 0, 0), TAU * 3.0))
wobble(RIG['board'], f0 + 62, (0, 0, 0), 0.035, 4, 22)
for i in (1, 2):                                       # spare darts already stuck
    d = RIG['darts'][i]
    d.location = (DART_BOARD[0] - 0.30,
                  DART_BOARD[1] + (0.34 if i == 1 else -0.28),
                  DART_BOARD[2] + (0.28 if i == 1 else -0.34))

# ---- 04 BILLIARDS ---------------------------------------------------
f0, f1 = SEG['pool']
snap_root(BO, f0, (POOL[0] - 0.55, POOL[1] - 3.5, 0.0), face_dir(0, 1))
cue = RIG['cue']
cue_home = tuple(cue.location)
key(cue, f0 + 4, loc=cue_home)
key(cue, f0 + 24, loc=(cue_home[0], cue_home[1] - 0.55, cue_home[2]))
key(cue, f0 + 31, loc=(cue_home[0], cue_home[1] + 0.30, cue_home[2]))
key(cue, f0 + 46, loc=cue_home)
arm_key(BO, 'r', f0 + 4, 0.55)
arm_key(BO, 'r', f0 + 24, 0.15)
arm_key(BO, 'r', f0 + 31, 0.80)
arm_key(BO, 'r', f0 + 60, 0.35)
cb = RIG['cueball']
r = RIG['ball_r']
cb_p0 = tuple(cb.location)
cb_p1 = (POOL[0], POOL[1] + 0.92, cb_p0[2])
roll(cb, f0 + 31, f0 + 55, cb_p0, cb_p1, r)
key(cb, f0 + 70, loc=(POOL[0] - 0.18, POOL[1] + 0.40, cb_p0[2]))
pocket_ball = RIG['balls'][6]
for i, b in enumerate(RIG['balls']):
    p0 = tuple(b.location)
    ang = random.uniform(0, TAU)
    dist = random.uniform(0.5, 2.0)
    tx = max(POOL[0] - 1.18, min(POOL[0] + 1.18, p0[0] + math.cos(ang) * dist))
    ty = max(POOL[1] - 2.40, min(POOL[1] + 2.40, p0[1] + math.sin(ang) * dist))
    if b is pocket_ball:
        tx, ty = POOL[0] + 1.32, POOL[1] + 2.56       # corner pocket
    roll(b, f0 + 55 + i, f0 + 88 + i, p0, (tx, ty, p0[2]), r)
    if b is pocket_ball:
        key(b, f0 + 96, loc=(tx, ty, p0[2] - 0.55))
        set_interp(b, 'BEZIER', only_after=f0 + 89)

# ---- 05 BEER PONG ---------------------------------------------------
f0, f1 = SEG['pong']
snap_root(BO, f0, (BP[0] + 0.3, BP[1] - 3.3, 0.0), face_dir(0, 1))
snap_root(MIA, f0, (BP[0] - 0.4, BP[1] + 3.3, 0.0), face_dir(0, -1))
pb = RIG['pongball']
rel = throw(BO, 'r', f0 + 6, f0 + 22, f0 + 30, prop=pb, cock=-1.9, rel=1.15)
target = RIG['cups'][6]                               # apex cup, far triangle
tp = tuple(target.location)
rim = (tp[0] + 0.10, tp[1] - 0.06, tp[2] + 0.13)
ballistic(pb, f0 + 30, f0 + 58, rel, rim, 1.75, spin=((0.3, 1, 0.2), 2.0))
ballistic(pb, f0 + 58, f0 + 68, rim, (tp[0], tp[1], tp[2] + 0.10), 0.22)
key(pb, f0 + 76, loc=(tp[0], tp[1], tp[2] - 0.06))
key(target, f0 + 58, loc=tp)
key(target, f0 + 63, loc=(tp[0], tp[1], tp[2] + 0.05))
key(target, f0 + 72, loc=tp)
for P, s in ((MIA, 1), (BO, -1)):                      # celebration / despair
    arm_key(P, 'l', f0 + 74, 0.0)
    arm_key(P, 'l', f0 + 88, -2.6 if s > 0 else -0.4)
    arm_key(P, 'l', f0 + 116, -2.2 if s > 0 else 0.0)
    key(P['body'], f0 + 74, rot=(0, 0, 0))
    key(P['body'], f0 + 90, rot=(-0.25 * s, 0, 0))
    key(P['body'], f0 + 116, rot=(0, 0, 0))

# ---- 06 AXE ---------------------------------------------------------
f0, f1 = SEG['axe']
snap_root(MIA, f0, MIA['home'][0], MIA['home'][1])     # back to the table
snap_root(BO, f0, (AXE_LINE[0], AXE_LINE[1], 0.0), face_dir(1, 0))
axe = RIG['axe']
rel = throw(BO, 'r', f0 + 6, f0 + 34, f0 + 43, prop=axe, cock=-2.85, rel=0.85)
stuck = (AXE_T[0] - 0.16, AXE_T[1] - 0.05, AXE_T[2] + 0.10)
ballistic(axe, f0 + 43, f0 + 73, rel, stuck, 0.85, spin=((0, 1, 0), 2.25))
key(axe, f0 + 92, loc=stuck, quat=Quaternion((0, 1, 0), TAU * 2.25))
wobble(RIG['axe_target'], f0 + 73, (0, 0, 0), 0.045, 4, 24)
for i in range(6):                                     # splinters
    sp = obj_from(bm_box(0.09, 0.03, 0.03, 0.008, 1), "Splinter%d" % i,
                  mat['wood_l'], loc=stuck, angle=40)
    sp.rotation_mode = 'QUATERNION'
    a = TAU * i / 6
    ballistic(sp, f0 + 73, f0 + 96,
              stuck, (stuck[0] - 1.4, stuck[1] + math.cos(a) * 1.5,
                      max(0.1, stuck[2] + math.sin(a) * 1.2 - 1.4)),
              0.6, spin=((math.cos(a), math.sin(a), 0.4), 2.5))

# ---- 07 SLINGSHOT ---------------------------------------------------
f0, f1 = SEG['sling']
snap_root(BO, f0, (SLING[0] + 1.5, SLING[1] - 0.9, 0.0), face_dir(-1, 0))
sx, sy, sz = SLING
band_z = sz + 1.05
pouch, pellet = RIG['pouch'], RIG['pellet']
draw_pts = [(f0 + 4, sx + 0.55), (f0 + 32, sx + 2.05), (f0 + 34, sx + 0.30)]
for f, px in draw_pts:
    key(pouch, f, loc=(px, sy, band_z))
    key(pellet, f, loc=(px, sy, band_z))
    for b in RIG['bands']:
        length = max(0.4, px - sx + 0.5)
        key(b, f, loc=(sx + (px - sx) * 0.5, b.location[1], band_z),
            scale=(1.0, 1.0, length))
key(pouch, f0 + 44, loc=(sx + 0.55, sy, band_z))
for b in RIG['bands']:
    key(b, f0 + 44, loc=(sx + 0.25, b.location[1], band_z), scale=(1, 1, 1.0))
arm_key(BO, 'l', f0 + 4, 0.9)
arm_key(BO, 'l', f0 + 32, 1.5)
arm_key(BO, 'l', f0 + 36, 0.4)
arm_key(BO, 'l', f0 + 60, 0.0)
hit_x = CANS[0] + 0.5
ballistic(pellet, f0 + 34, f0 + 62, (sx + 0.30, sy, band_z),
          (hit_x, CANS[1], 1.70), 0.9, spin=((0, 1, 0), 4.0))
for i, can in enumerate(RIG['cans']):
    p0 = tuple(can.location)
    if i in (2, 3, 4):                                 # the ones actually hit
        fs = f0 + 62 + (i - 2) * 3
        a = random.uniform(-0.7, 0.7)
        p1 = (p0[0] - 2.6 - random.uniform(0, 1.4),
              p0[1] + a * 2.0, 0.14)
        ballistic(can, fs, fs + 26, p0, p1, 1.5,
                  spin=((0.4, 1.0, 0.3), 2.5))
        key(can, fs + 34, loc=(p1[0] - 0.3, p1[1] + a, 0.14),
            quat=Quaternion(Vector((0.4, 1.0, 0.3)).normalized(), TAU * 2.5 + 1.4))

# ---- 08 BOWLING -----------------------------------------------------
f0, f1 = SEG['bowl']
snap_root(BO, f0, (LANE_X0 - 1.2, LANE_Y + 0.75, 0.35), face_dir(1, -0.1))
ball = RIG['bowlball']
rel = throw(BO, 'r', f0 + 6, f0 + 24, f0 + 32, prop=ball, cock=-1.5, rel=1.35)
lane_top = LANE_Z + 0.05 + 0.30
start = (LANE_X0 + 0.3, LANE_Y - 0.35, lane_top)
key(ball, f0 + 36, loc=start, quat=Quaternion((1, 0, 0, 0)))
roll(ball, f0 + 36, f0 + 82, start, (LANE_X1 - 1.75, LANE_Y + 0.10, lane_top), 0.30)
roll(ball, f0 + 82, f0 + 96, (LANE_X1 - 1.75, LANE_Y + 0.10, lane_top),
     (LANE_X1 + 0.55, LANE_Y + 0.35, lane_top), 0.30)
for i, p in enumerate(RIG['pins']):
    p0 = tuple(p.location)
    fs = f0 + 80 + int(abs(p0[0] - (LANE_X1 - 1.9)) * 12) + (i % 3) * 2
    a = math.atan2(p0[1] - LANE_Y, 0.35) + random.uniform(-0.6, 0.6)
    dist = random.uniform(1.0, 2.6)
    p1 = (p0[0] + abs(math.cos(a)) * dist * 0.9,
          p0[1] + math.sin(a) * dist, LANE_Z + 0.10)
    ax = (math.sin(a) * 0.5, math.cos(a), 0.25)
    ballistic(p, fs, fs + 22, p0, p1, random.uniform(0.35, 0.95),
              spin=(ax, random.uniform(0.8, 1.6)))
    key(p, fs + 30, loc=(p1[0] + 0.15, p1[1], LANE_Z + 0.10),
        quat=Quaternion(Vector(ax).normalized(), TAU * 1.2))

# ---- 09 PUNISHMENT --------------------------------------------------
f0, f1 = SEG['punish']
VICT = (7.6, -27.4)
snap_root(MIA, f0, (VICT[0], VICT[1], 0.0), face_dir(1.0, 0.4))
snap_root(BO, f0, (CANNON[0] + 1.4, CANNON[1] + 1.2, 0.0), face_dir(-1, -0.3))
aim = math.atan2(VICT[1] - CANNON[1], VICT[0] - CANNON[0])
cannon = RIG['cannon']
cannon.rotation_euler = (0, 0, aim)
cx, cy, cz = RIG['cannon_home']
muzzle = (cx + math.cos(aim) * 2.75, cy + math.sin(aim) * 2.75, cz + 0.05)
key(cannon, f0 + 10, loc=(cx, cy, cz), rot=(0, 0, aim))
key(cannon, f0 + 22, loc=(cx - math.cos(aim) * 0.55,
                          cy - math.sin(aim) * 0.55, cz + 0.08), rot=(0, 0, aim))
key(cannon, f0 + 46, loc=(cx, cy, cz), rot=(0, 0, aim))
smoke, smat = RIG['smoke'], RIG['smoke_mat']
key(smoke, f0 + 20, loc=muzzle, scale=0.25)
key_alpha(smat, f0 + 20, 0.0)
key_alpha(smat, f0 + 24, 0.85)
key(smoke, f0 + 26, loc=(muzzle[0] + math.cos(aim) * 0.9,
                         muzzle[1] + math.sin(aim) * 0.9, muzzle[2] + 0.35),
    scale=1.5)
key(smoke, f0 + 62, loc=(muzzle[0] + math.cos(aim) * 2.2,
                         muzzle[1] + math.sin(aim) * 2.2, muzzle[2] + 1.5),
    scale=3.4)
key_alpha(smat, f0 + 62, 0.0)
potato = RIG['potato']
key(potato, f0 + 20, loc=muzzle, quat=Quaternion((1, 0, 0, 0)))
ballistic(potato, f0 + 21, f0 + 41, muzzle, (VICT[0], VICT[1], 1.55), 0.35,
          spin=((0.4, 0.6, 1.0), 3.0))
key(potato, f0 + 52, loc=(VICT[0] - 0.9, VICT[1] + 0.5, 0.12))
# victim launched into the piranha pool
# lands sitting in the water, chest-deep - water surface is at z = 1.42
land = (PPOOL[0] + 0.9, PPOOL[1] + 0.7, 0.85)
mroot = MIA['root']
key(mroot, f0 + 41, loc=(VICT[0], VICT[1], 0.0), rot=(0, 0, MIA['rz']))
n = 34
for i in range(n + 1):
    t = i / n
    lx = VICT[0] + (land[0] - VICT[0]) * t
    ly = VICT[1] + (land[1] - VICT[1]) * t
    lz = land[2] * t + 3.4 * 4 * t * (1 - t)
    key(mroot, f0 + 41 + i, loc=(lx, ly, lz),
        rot=(-TAU * 0.25 * t - 0.55 * t, 0, MIA['rz']))
set_interp(mroot, 'LINEAR', only_after=f0 + 40)
key(mroot, f0 + 84, loc=(land[0] - 0.25, land[1], 0.72), rot=(-0.55, 0, MIA['rz']))
key(mroot, f0 + 100, loc=(land[0] - 0.25, land[1], 0.80), rot=(-0.42, 0, MIA['rz']))
arm_key(MIA, 'l', f0 + 41, 0.0)
arm_key(MIA, 'l', f0 + 52, -2.7)
arm_key(MIA, 'r', f0 + 41, 0.0)
arm_key(MIA, 'r', f0 + 52, -2.5)
# splash
spl, spmat = RIG['splash'], RIG['splash_mat']
key(spl, f0 + 74, loc=(land[0], land[1], 1.50), scale=0.35)
key_alpha(spmat, f0 + 74, 0.0)
key_alpha(spmat, f0 + 78, 0.92)
key(spl, f0 + 108, loc=(land[0], land[1], 1.50), scale=5.6)
key_alpha(spmat, f0 + 108, 0.0)
for i, d in enumerate(RIG['drops']):
    a = TAU * i / len(RIG['drops'])
    p0 = (land[0], land[1], 1.5)
    p1 = (land[0] + math.cos(a) * random.uniform(2.0, 4.2),
          land[1] + math.sin(a) * random.uniform(2.0, 4.2), 1.45)
    key(d, f0 + 72, loc=p0, scale=0.15)
    key(d, f0 + 75, loc=p0, scale=1.0)
    ballistic(d, f0 + 75, f0 + 100, p0, p1, random.uniform(1.4, 2.6))
    key(d, f0 + 104, loc=p1, scale=0.05)
arm_key(BO, 'r', f0 + 60, 0.0)
arm_key(BO, 'r', f0 + 78, -2.6)
arm_key(BO, 'r', f0 + 112, -2.2)

# ============================================================ world & light
sc = bpy.context.scene
sc.frame_start = 1
sc.frame_end = F_END
sc.render.fps = FPS

w = bpy.data.worlds.new("Sky"); sc.world = w; w.use_nodes = True
nt = w.node_tree; bg = nt.nodes["Background"]
geo = nt.nodes.new("ShaderNodeNewGeometry"); geo.location = (-900, 0)
sep = nt.nodes.new("ShaderNodeSeparateXYZ"); sep.location = (-700, 0)
mr = nt.nodes.new("ShaderNodeMapRange"); mr.location = (-520, 0)
mr.inputs['From Min'].default_value = -0.15
mr.inputs['From Max'].default_value = 0.55
ramp = nt.nodes.new("ShaderNodeValToRGB"); ramp.location = (-320, 0)
ramp.color_ramp.elements[0].color = (0.86, 0.68, 0.48, 1)     # warm horizon
ramp.color_ramp.elements[1].color = (0.20, 0.42, 0.82, 1)     # blue zenith
nt.links.new(geo.outputs["Incoming"], sep.inputs["Vector"])
nt.links.new(sep.outputs["Z"], mr.inputs["Value"])
nt.links.new(mr.outputs["Result"], ramp.inputs["Fac"])
bg.inputs["Strength"].default_value = 0.40
nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
bg_cam = nt.nodes.new("ShaderNodeBackground"); bg_cam.location = (-120, -220)
bg_cam.inputs["Strength"].default_value = 1.0
nt.links.new(ramp.outputs["Color"], bg_cam.inputs["Color"])
lp = nt.nodes.new("ShaderNodeLightPath"); lp.location = (-120, 260)
mixs = nt.nodes.new("ShaderNodeMixShader"); mixs.location = (120, 0)
nt.links.new(lp.outputs["Is Camera Ray"], mixs.inputs["Fac"])
nt.links.new(bg.outputs["Background"], mixs.inputs[1])
nt.links.new(bg_cam.outputs["Background"], mixs.inputs[2])
nt.links.new(mixs.outputs["Shader"], nt.nodes["World Output"].inputs["Surface"])

sd = bpy.data.lights.new("Sun", type='SUN')
sd.energy = 3.0; sd.color = (1.0, 0.86, 0.64); sd.angle = math.radians(3)
sun = bpy.data.objects.new("Sun", sd); bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(62), math.radians(4), math.radians(-118))

ad = bpy.data.lights.new("Fill", type='AREA')
ad.energy = 900.0; ad.color = (0.58, 0.74, 1.0); ad.size = 60.0
fill = bpy.data.objects.new("Fill", ad); bpy.context.collection.objects.link(fill)
fill.location = (40, -30, 30)
fill.rotation_euler = (math.radians(52), 0, math.radians(56))

# ============================================================ cameras
def make_cam(name, loc, look, lens=40.0):
    tgt = bpy.data.objects.new(name + "_T", None)
    bpy.context.collection.objects.link(tgt); tgt.location = look
    cd = bpy.data.cameras.new(name); cd.lens = lens
    cam = bpy.data.objects.new(name, cd)
    bpy.context.collection.objects.link(cam); cam.location = loc
    c = cam.constraints.new('TRACK_TO'); c.target = tgt
    c.track_axis = 'TRACK_NEGATIVE_Z'; c.up_axis = 'UP_Y'
    return cam, tgt

CAMS = {}
CAMS['est'], _ = make_cam("CAM_est", (64, -60, 34), (0, 6, 3), 34)
key(CAMS['est'], 1, loc=(64, -60, 34))
key(CAMS['est'], 120, loc=(26, -30, 15))
CAMS['cards'], _ = make_cam("CAM_cards", (2.6, 1.0, 6.2), (0, 4.0, 1.10), 35)
key(CAMS['cards'], SEG['cards'][0], loc=(2.6, 1.0, 6.2))
key(CAMS['cards'], SEG['cards'][1], loc=(2.0, 1.7, 5.5))
# throw scenes are shot from the side of the flight path: thrower and target both in frame
CAMS['darts'], _ = make_cam("CAM_darts", (16.4, -7.6, 3.3), (16.4, 2.0, 1.9), 28)
CAMS['pool'], _ = make_cam("CAM_pool", (-11.0, 10.4, 3.3), (-18.2, 10.0, 0.95), 34)
CAMS['pong'], _ = make_cam("CAM_pong", (-6.0, -16.6, 2.7), (-10.0, -12.2, 1.15), 34)
CAMS['axe'], _ = make_cam("CAM_axe", (18.8, -19.0, 3.2), (18.8, -12.0, 1.9), 32)
CAMS['sling'], _ = make_cam("CAM_sling", (-12.5, -31.8, 3.8), (-12.5, -22.0, 1.7), 24)
CAMS['sling2'], _ = make_cam("CAM_sling2", (-14.6, -27.6, 2.7), (-18.6, -22.0, 1.7), 32)
CAMS['bowl'], _ = make_cam("CAM_bowl", (4.6, 24.4, 2.4), (22.0, 25.1, 0.85), 28)
CAMS['bowl2'], _ = make_cam("CAM_bowl2", (24.6, 28.3, 1.7), (22.6, 25.0, 0.65), 32)
CAMS['punish'], _ = make_cam("CAM_punish", (15.2, -36.6, 6.2), (5.5, -29.5, 1.7), 32)

MARKS = [('est', 1), ('cards', 121), ('darts', 261), ('pool', 381),
         ('pong', 501), ('axe', 621), ('sling', 741), ('sling2', 800),
         ('bowl', 851), ('bowl2', 926), ('punish', 971)]
for k, f in MARKS:
    mk = sc.timeline_markers.new(k, frame=f)
    mk.camera = CAMS[k]
sc.camera = CAMS['est']

# ============================================================ render
def enable_gpu():
    try:
        pr = bpy.context.preferences.addons['cycles'].preferences
        for kind in ('OPTIX', 'CUDA', 'HIP', 'ONEAPI', 'METAL'):
            try:
                pr.compute_device_type = kind
            except Exception:
                continue
            try:
                pr.get_devices()
            except Exception:
                pass
            ds = [d for d in pr.devices if d.type == kind]
            if ds:
                for d in pr.devices:
                    d.use = (d.type == kind)
                print("### GPU", kind, [d.name for d in ds])
                return True
    except Exception as e:
        print("### gpu err", e)
    return False

sc.render.engine = 'CYCLES'
sc.cycles.device = 'GPU' if enable_gpu() else 'CPU'
sc.cycles.samples = 96
sc.cycles.use_denoising = True
sc.cycles.max_bounces = 5
sc.view_settings.view_transform = 'Standard'
sc.render.resolution_x = 1280
sc.render.resolution_y = 720
sc.render.film_transparent = False

tris = 0
for o in sc.objects:
    if o.type == 'MESH':
        o.data.calc_loop_triangles()
        tris += len(o.data.loop_triangles)
print("### OBJECTS=%d TRIS=%d FRAMES=%d" % (len(sc.objects), tris, F_END))

bpy.ops.wm.save_as_mainfile(filepath=OUT + "/backyard_bet.blend")
print("### SAVED blend")

# The .blend keeps its camera markers for playback, but they override
# scene.camera during a render, so drop them for the still passes below.
sc.timeline_markers.clear()

STILLS = [('est', 96), ('cards', 226), ('darts', 325), ('pool', 452),
          ('pong', 566), ('axe', 700), ('sling', 812), ('bowl', 916),
          ('bowl2', 950), ('punish', 1053)]
import sys
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

def export_fbx():
    """Static geometry (mesh + empties, rest pose, no baked animation) as one
    FBX for a first Unity import. Empties (character roots, table rig points)
    come along so their transforms exist in Unity even before we wire up
    real anchor components - see BackyardAnchor-style components later."""
    sc.frame_set(1)
    for o in bpy.data.objects:
        o.select_set(o.type in ('MESH', 'EMPTY'))
    path = OUT + "/BackyardBet.fbx"
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, use_active_collection=False,
        # FBX_SCALE_ALL, not FBX_SCALE_NONE: otherwise Blender bakes the
        # metres->centimetres conversion into each object's local scale and
        # everything arrives in Unity at scale=100 with correct-looking coords.
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y',                 # Unity axis convention
        object_types={'MESH', 'EMPTY'},
        use_mesh_modifiers=True, mesh_smooth_type='FACE',
        use_triangles=False, add_leaf_bones=False, bake_anim=False,
        path_mode='COPY', embed_textures=False)
    print("### EXPORTED FBX %s (%.1f KB)" % (path, os.path.getsize(path) / 1024.0))

def export_characters():
    """Каждый персонаж - отдельным FBX, поставленным в начало координат.

    В общей карте персонажи стоят вокруг стола, и вытаскивать их оттуда в
    префаб игрока неудобно. Здесь каждый временно переносится в (0,0,0) с
    нулевым поворотом, экспортируется, и возвращается на место - карта от
    этого не меняется."""
    sc.frame_set(1)
    for P in PL:
        root = P['root']
        keep_loc = tuple(root.location)
        keep_rot = tuple(root.rotation_euler)
        root.location = (0.0, 0.0, 0.0)
        root.rotation_euler = (0.0, 0.0, 0.0)
        bpy.context.view_layer.update()

        for o in bpy.data.objects:
            o.select_set(False)
        root.select_set(True)
        for child in root.children_recursive:
            child.select_set(True)

        path = "%s/%s.fbx" % (OUT, root.name)
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, use_active_collection=False,
            global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
            axis_forward='-Z', axis_up='Y',
            object_types={'MESH', 'EMPTY'},
            use_mesh_modifiers=True, mesh_smooth_type='FACE',
            use_triangles=False, add_leaf_bones=False, bake_anim=False,
            path_mode='COPY', embed_textures=False)

        root.location = keep_loc
        root.rotation_euler = keep_rot
        bpy.context.view_layer.update()
        print("### EXPORTED CHARACTER %s (%.1f KB)"
              % (path, os.path.getsize(path) / 1024.0))

def export_palette():
    """Material colours as JSON for the Unity side to apply by name.

    FBX cannot carry a procedural node graph, so every M_patchy() material
    (grass, bark, leaves, stone...) arrives in Unity as plain white. Here the
    ramp's two colours are averaged into the flat colour those surfaces should
    actually be, and plain M() materials export their base colour directly."""
    import json
    out = []
    for m in bpy.data.materials:
        if not m.use_nodes or "Principled BSDF" not in m.node_tree.nodes:
            continue
        b = m.node_tree.nodes["Principled BSDF"]
        base = b.inputs["Base Color"]
        if base.is_linked:
            # M_patchy: Base Color <- ColorRamp <- Noise. Average the stops.
            node = base.links[0].from_node
            if node.type == 'VALTORGB':
                els = node.color_ramp.elements
                col = [sum(e.color[i] for e in els) / len(els) for i in range(3)]
            else:
                col = [0.8, 0.8, 0.8]
        else:
            col = list(base.default_value)[:3]
        emit = 0.0
        emit_col = [0.0, 0.0, 0.0]
        if "Emission Strength" in b.inputs:
            emit = float(b.inputs["Emission Strength"].default_value)
        if "Emission Color" in b.inputs and not b.inputs["Emission Color"].is_linked:
            emit_col = list(b.inputs["Emission Color"].default_value)[:3]
        out.append(dict(
            name=m.name,
            color=[round(c, 5) for c in col],
            rough=round(float(b.inputs["Roughness"].default_value), 4),
            metal=round(float(b.inputs["Metallic"].default_value), 4),
            alpha=round(float(b.inputs["Alpha"].default_value), 4),
            emit=round(emit, 4),
            emit_color=[round(c, 5) for c in emit_col]))
    path = OUT + "/backyard_palette.json"
    with open(path, "w", encoding="utf-8") as f:
        json.dump(dict(materials=out), f, ensure_ascii=False, indent=1)
    print("### EXPORTED PALETTE %s (%d materials)" % (path, len(out)))

if "fbx" in ARGS:
    export_fbx()
    export_characters()
    export_palette()

if "cardsonly" in ARGS:                      # fast iteration on character look
    STILLS = [('cards', 226)]
    sc.cycles.samples = 48

if "novstills" not in ARGS:
    for name, f in STILLS:
        sc.frame_set(f)
        sc.camera = CAMS[name]
        sc.render.filepath = OUT + "/shot_%s.png" % name
        bpy.ops.render.render(write_still=True)
        print("### STILL %s f%d" % (name, f))

def contact_sheet(out_name, items, cols=4, cell=(460, 259)):
    """Render animation phases and tile them into one PNG (no ffmpeg needed)."""
    import numpy as np
    sc.render.resolution_x, sc.render.resolution_y = cell
    w, h = cell
    tiles = []
    for cam_key, f in items:
        sc.frame_set(f)
        sc.camera = CAMS[cam_key]
        p = "%s/_tile_%s_%d.png" % (OUT, cam_key, f)
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(p)
        buf = np.empty(len(im.pixels), dtype=np.float32)
        im.pixels.foreach_get(buf)
        tiles.append(buf.reshape(im.size[1], im.size[0], 4).copy())
        bpy.data.images.remove(im)
        os.remove(p)
    rows = (len(tiles) + cols - 1) // cols
    sheet = np.zeros((rows * h, cols * w, 4), dtype=np.float32)
    sheet[..., 3] = 1.0
    for i, a in enumerate(tiles):
        r, c = i // cols, i % cols
        # Blender image rows run bottom-up, so row 0 goes at the bottom
        sheet[(rows - 1 - r) * h:(rows - r) * h, c * w:(c + 1) * w] = a
    out = bpy.data.images.new(out_name, cols * w, rows * h, alpha=True)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = "%s/%s.png" % (OUT, out_name)
    out.file_format = 'PNG'
    out.save()
    print("### SHEET %s (%d tiles)" % (out_name, len(tiles)))

if "sheets" in ARGS:
    sc.cycles.samples = 48
    contact_sheet("anim_throws", [
        ('darts', 286), ('darts', 301), ('darts', 312), ('darts', 324),
        ('axe', 652), ('axe', 666), ('axe', 680), ('axe', 700)])
    contact_sheet("anim_table", [
        ('cards', 136), ('cards', 212), ('cards', 231), ('cards', 256),
        ('pool', 410), ('pool', 442), ('pool', 464), ('pool', 488)])
    contact_sheet("anim_pong_sling", [
        ('pong', 530), ('pong', 548), ('pong', 566), ('pong', 590),
        ('sling', 770), ('sling', 790), ('sling2', 812), ('sling2', 834)])
    contact_sheet("anim_bowl_punish", [
        ('bowl', 878), ('bowl', 906), ('bowl', 930), ('bowl2', 950),
        ('punish', 992), ('punish', 1014), ('punish', 1048), ('punish', 1064)])

if "interior" in ARGS:
    # One deterministic review frame for the house; useful while iterating on
    # the playable interior without having to navigate the full yard timeline.
    review_cam, _ = make_cam("CAM_house_review", (-5.1, 24.65, 2.75),
                             (-10.15, 22.90, 1.45), 28)
    sc.frame_set(18)
    sc.camera = review_cam
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    sc.render.filepath = OUT + "/house_interior_review.png"
    bpy.ops.render.render(write_still=True)
    print("### INTERIOR REVIEW")
print("### DONE")
