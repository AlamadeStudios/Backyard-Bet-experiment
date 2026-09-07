# -*- coding: utf-8 -*-
"""
CARD GAME KIT - standalone asset library for the Backyard Bet bluff table.

Builds a fully-detailed, self-contained set of poker props:
  * two complete 52-card decks (different backs), every card with real
    corner ranks, suit pips laid out like an actual deck, and face-card
    borders - not placeholder colored rectangles
  * a 5-denomination poker chip set with printed values and edge spots
  * dice with correctly counted pips on all six faces (opposite faces
    sum to 7), built from a real face-normal basis so a "roll" can be
    posed to show any value face-up
  * a small demo scene + animation set (fan the deck, deal four hands,
    shake/reveal the dice cup, toss chips into the pot) proving the kit
    animates, ready to be appended into backyard_bet.blend later.

This file is deliberately independent of backyard_bet.py (no import),
since it is meant to be opened/appended as its own library asset. Run:

  blender --background --python card_game_kit.py -- probe   (fast sanity check)
  blender --background --python card_game_kit.py -- full    (full kit + renders)
"""
import bpy, bmesh, math, random, sys
from mathutils import Vector, Matrix, Quaternion

TAU = math.tau
FPS = 30
OUT = "D:/cloudi/blender"
SEED = 3
random.seed(SEED)

# ============================================================ low-level helpers
# (self-contained copies of the same conventions used in backyard_bet.py, so
#  code can move between the two files with minimal changes)

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def M(name, color, rough=0.6, metallic=0.0, spec=0.12, emit=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    def s(k, v):
        if k in b.inputs: b.inputs[k].default_value = v
    s("Base Color", (color[0], color[1], color[2], 1.0))
    s("Roughness", rough); s("Metallic", metallic)
    s("Specular IOR Level", spec); s("Specular", spec)
    if emit:
        s("Emission Color", (emit[0], emit[1], emit[2], 1.0))
        s("Emission Strength", emit[3])
    return m

def bevel_all(bm, offset, seg=2):
    if offset <= 0: return
    bmesh.ops.bevel(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                    offset=offset, segments=seg, profile=0.5, affect='EDGES')

def obj_from(bm, name, mats, loc=(0, 0, 0), rot=(0, 0, 0), scale=1.0, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    ob.location = loc; ob.rotation_euler = rot
    ob.scale = (scale, scale, scale) if isinstance(scale, (int, float)) else scale
    if isinstance(mats, (list, tuple)):
        for mm in mats: me.materials.append(mm)
    else:
        me.materials.append(mats)
    for p in me.polygons: p.use_smooth = smooth
    return ob

def dup(proto, name, loc, rot=(0, 0, 0), scale=1.0):
    ob = bpy.data.objects.new(name, proto.data)
    bpy.context.collection.objects.link(ob)
    ob.location = loc; ob.rotation_euler = rot
    ob.scale = (scale, scale, scale) if isinstance(scale, (int, float)) else scale
    return ob

def add_annulus(bm, r0, r1, a0, a1, z, steps, mi=0):
    r0 = max(r0, 0.0005)
    vin, vout = [], []
    for i in range(steps + 1):
        a = a0 + (a1 - a0) * i / steps
        ca, sa = math.cos(a), math.sin(a)
        vin.append(bm.verts.new((ca * r0, sa * r0, z)))
        vout.append(bm.verts.new((ca * r1, sa * r1, z)))
    for i in range(steps):
        f = bm.faces.new((vin[i], vout[i], vout[i + 1], vin[i + 1]))
        f.material_index = mi

# ---- Blender 5.x slotted-action compatible fcurve access ----
def fcurves_of(ob):
    ad = ob.animation_data
    if not ad or not ad.action: return []
    act = ad.action
    out = []
    try:
        for layer in act.layers:
            for strip in layer.strips:
                for cb in strip.channelbags:
                    out.extend(cb.fcurves)
    except Exception:
        try: out = list(act.fcurves)
        except Exception: out = []
    return out

def set_interp(ob, mode='LINEAR', only_after=None):
    for fc in fcurves_of(ob):
        for kp in fc.keyframe_points:
            if only_after is None or kp.co[0] >= only_after:
                kp.interpolation = mode

def key(ob, f, loc=None, rot=None, quat=None, scale=None):
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

def key_alpha(mat, f, v):
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Alpha"].default_value = v
    b.inputs["Alpha"].keyframe_insert("default_value", frame=f)

def ballistic(ob, f0, f1, p0, p1, height, spin=None, step=1):
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

# ============================================================ text -> mesh
def text_mesh(body, size=1.0):
    """A standalone, origin-centred flat bmesh of a glyph string. Cached by caller."""
    curve = bpy.data.curves.new("Glyph_%s" % body, type='FONT')
    curve.body = body
    curve.align_x = 'CENTER'; curve.align_y = 'CENTER'
    curve.size = size
    curve.extrude = 0.0; curve.bevel_depth = 0.0
    ob = bpy.data.objects.new("GlyphObj_%s" % body, curve)
    bpy.context.collection.objects.link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bpy.data.objects.remove(ob, do_unlink=True)
    bm = bmesh.new()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)
    return bm

GLYPH_CACHE = {}
def glyph(text):
    if text not in GLYPH_CACHE:
        GLYPH_CACHE[text] = text_mesh(text, 1.0)
    return GLYPH_CACHE[text]

def append_bm(dest, src, mt, mi):
    """Copy src's geometry into dest, transformed by mt, tagged with material index mi.
    bmesh.ops.duplicate(..., dest=other_bm) is unimplemented in this Blender build,
    so verts/faces are copied by hand instead."""
    vmap = {}
    for v in src.verts:
        vmap[v] = dest.verts.new(mt @ v.co)
    for f in src.faces:
        nf = dest.faces.new([vmap[v] for v in f.verts])
        nf.material_index = mi

# ============================================================ suit icon shapes
def _poly(bm, pts, mi=0, z=0.0):
    vs = [bm.verts.new((x, y, z)) for x, y in pts]
    f = bm.faces.new(vs); f.material_index = mi
    return f

def diamond_bm():
    bm = bmesh.new()
    _poly(bm, [(0, 1.0), (0.62, 0), (0, -1.0), (-0.62, 0)])
    return bm

HEART_PTS = [(0, -1.05), (0.52, -0.52), (0.60, 0.05), (0.42, 0.40), (0.16, 0.50),
             (0, 0.28), (-0.16, 0.50), (-0.42, 0.40), (-0.60, 0.05), (-0.52, -0.52)]

def heart_bm():
    bm = bmesh.new()
    _poly(bm, HEART_PTS)
    return bm

def spade_bm():
    bm = bmesh.new()
    _poly(bm, [(x, -y) for x, y in HEART_PTS])
    _poly(bm, [(-0.07, -0.55), (0.07, -0.55), (0.12, -0.98), (-0.12, -0.98)])
    return bm

def club_bm():
    bm = bmesh.new()
    def circ(cx, cy, r, n=10):
        return [(cx + math.cos(TAU * i / n) * r, cy + math.sin(TAU * i / n) * r)
                for i in range(n)]
    _poly(bm, circ(0, 0.42, 0.40))
    _poly(bm, circ(-0.36, -0.18, 0.40))
    _poly(bm, circ(0.36, -0.18, 0.40))
    _poly(bm, [(-0.09, -0.35), (0.09, -0.35), (0.14, -1.0), (-0.14, -1.0)])
    return bm

SUIT_SHAPES = None       # populated by build_mats() once materials exist
SUIT_COLOR = {'H': 'red', 'D': 'red', 'C': 'black', 'S': 'black'}
SUIT_NAME = {'H': 'Hearts', 'D': 'Diamonds', 'C': 'Clubs', 'S': 'Spades'}
RANKS = ['A', '2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K']
SUITS = ['S', 'H', 'D', 'C']

def build_suit_shapes():
    global SUIT_SHAPES
    SUIT_SHAPES = {'H': heart_bm(), 'D': diamond_bm(), 'C': club_bm(), 'S': spade_bm()}

# ============================================================ card face layout
PIP_LAYOUTS = {
    2: [(0, 0.72, 0), (0, -0.72, 1)],
    3: [(0, 0.72, 0), (0, 0, 0), (0, -0.72, 1)],
    4: [(-0.5, 0.72, 0), (0.5, 0.72, 0), (-0.5, -0.72, 1), (0.5, -0.72, 1)],
    5: [(-0.5, 0.72, 0), (0.5, 0.72, 0), (0, 0, 0),
        (-0.5, -0.72, 1), (0.5, -0.72, 1)],
    6: [(-0.5, 0.72, 0), (0.5, 0.72, 0), (-0.5, 0, 0), (0.5, 0, 0),
        (-0.5, -0.72, 1), (0.5, -0.72, 1)],
    7: [(-0.5, 0.72, 0), (0.5, 0.72, 0), (0, 0.38, 0), (-0.5, 0, 0), (0.5, 0, 0),
        (-0.5, -0.72, 1), (0.5, -0.72, 1)],
    8: [(-0.5, 0.72, 0), (0.5, 0.72, 0), (0, 0.42, 0), (-0.5, 0.06, 0), (0.5, 0.06, 0),
        (0, -0.30, 1), (-0.5, -0.72, 1), (0.5, -0.72, 1)],
    9: [(-0.5, 0.78, 0), (0.5, 0.78, 0), (-0.5, 0.26, 0), (0.5, 0.26, 0), (0, 0, 0),
        (-0.5, -0.26, 1), (0.5, -0.26, 1), (-0.5, -0.78, 1), (0.5, -0.78, 1)],
    10: [(-0.5, 0.86, 0), (0.5, 0.86, 0), (0, 0.56, 0), (-0.5, 0.26, 0), (0.5, 0.26, 0),
         (-0.5, -0.26, 1), (0.5, -0.26, 1), (0, -0.56, 1),
         (-0.5, -0.86, 1), (0.5, -0.86, 1)],
}

CARD_HW, CARD_HH, CARD_TH = 0.15, 0.21, 0.014
BACK_PATTERN = None

def build_back_pattern():
    global BACK_PATTERN
    bm = bmesh.new()
    step = 0.070
    hw, hh = CARD_HW - 0.05, CARD_HH - 0.05
    iy = 0
    y = -hh
    while y < hh:
        row_off = step * 0.5 if (iy % 2) else 0.0
        x = -hw + row_off
        while x < hw:
            pts = [(x, y + 0.030), (x + 0.024, y), (x, y - 0.030), (x - 0.024, y)]
            _poly(bm, pts, mi=0)
            x += step
        y += step * 0.82
        iy += 1
    BACK_PATTERN = bm

def add_frame(bm, hw, hh, thick, mi, z):
    segs = [(hw * 2, thick, 0, hh), (hw * 2, thick, 0, -hh),
            (thick, hh * 2, hw, 0), (thick, hh * 2, -hw, 0)]
    for sx, sy, cx, cy in segs:
        x0, x1 = cx - sx * 0.5, cx + sx * 0.5
        y0, y1 = cy - sy * 0.5, cy + sy * 0.5
        vs = [bm.verts.new((x0, y0, z)), bm.verts.new((x1, y0, z)),
              bm.verts.new((x1, y1, z)), bm.verts.new((x0, y1, z))]
        f = bm.faces.new(vs); f.material_index = mi

def build_card_bm(rank, suit):
    """Materials fixed for every card: [0]=stock, [1]=red ink, [2]=black ink,
    [3]=back accent (deck-specific colour supplied by the caller's material list).
    All layout numbers are FRACTIONS of hw/hh, not absolute offsets - CARD_HW/HH
    are small (0.15/0.21) to match backyard_bet.py's existing card slots, so any
    fixed-size margin larger than that swallows the whole card (this bit us once
    already: see probe_kit.png before this fix)."""
    hw, hh, th = CARD_HW, CARD_HH, CARD_TH
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Diagonal((hw * 2, hh * 2, th, 1.0)))
    bevel_all(bm, 0.005, 2)
    for f in bm.faces:
        f.material_index = 3 if f.normal.z < -0.3 else 0
    ink = 1 if suit in ('H', 'D') else 2
    top_z = th * 0.5 + 0.0007
    bot_z = -(th * 0.5 + 0.0007)

    def T(pos, ang=0.0, s=1.0, z=top_z):
        return (Matrix.Translation((pos[0], pos[1], z)) @ Matrix.Rotation(ang, 4, 'Z')
                @ Matrix.Diagonal((s, s, 1.0, 1.0)))

    g = glyph(rank)
    sh = SUIT_SHAPES[suit]

    # corner index: rank glyph + a small suit icon tucked just beneath it
    cs = 0.080
    cx0, cy0 = -hw + 0.062, hh - 0.062
    append_bm(bm, g, T((cx0, cy0), 0.0, cs), ink)
    append_bm(bm, sh, T((cx0, cy0 - 0.075), 0.0, cs * 0.62), ink)
    cx1, cy1 = hw - 0.062, -hh + 0.062
    append_bm(bm, g, T((cx1, cy1), math.pi, cs), ink)
    append_bm(bm, sh, T((cx1, cy1 + 0.075), math.pi, cs * 0.62), ink)

    if rank == 'A':
        append_bm(bm, sh, T((0, 0.01), 0.0, hw * 0.82), ink)
    elif rank in ('J', 'Q', 'K'):
        add_frame(bm, hw - 0.050, hh - 0.050, 0.006, ink, top_z)
        append_bm(bm, g, T((0, 0.0), 0.0, hh * 0.75), ink)
        append_bm(bm, sh, T((0, hh * 0.64), 0.0, hw * 0.42), ink)
        append_bm(bm, sh, T((0, -hh * 0.64), math.pi, hw * 0.42), ink)
    else:
        n = int(rank)
        # denser layouts (7-10 pips) need smaller icons or the rows collide
        pip_s = hw * (0.42 if n <= 6 else 0.42 * 6.4 / n)
        for px, py, flip in PIP_LAYOUTS[n]:
            pos = (px * hw * 0.75, py * hh * 0.62)
            append_bm(bm, sh, T(pos, math.pi if flip else 0.0, pip_s), ink)

    append_bm(bm, BACK_PATTERN, T((0, 0), 0.0, 1.0, bot_z), 3)
    add_frame(bm, hw - 0.030, hh - 0.030, 0.006, 3, bot_z)
    return bm

def build_card(rank, suit, back_mat, mat_stock, mat_red, mat_black, name):
    bm = build_card_bm(rank, suit)
    return obj_from(bm, name, [mat_stock, mat_red, mat_black, back_mat],
                    loc=(0, 0, -300))

def build_deck(deck_name, back_mat, mat_stock, mat_red, mat_black):
    """Returns an ordered list of 52 card objects, A-spades..K-clubs."""
    cards = []
    for suit in SUITS:
        for rank in RANKS:
            c = build_card(rank, suit, back_mat, mat_stock, mat_red, mat_black,
                           "%s_%s%s" % (deck_name, rank, suit))
            cards.append(c)
    return cards

# ============================================================ poker chips
CHIP_R, CHIP_H = 0.115, 0.028
CHIP_SPECS = [('1', 'chip_white', 'black'), ('5', 'chip_red', 'white'),
              ('10', 'chip_blue', 'white'), ('25', 'chip_green', 'white'),
              ('100', 'chip_black', 'gold')]

def build_chip_bm(value_text):
    r, h = CHIP_R, CHIP_H
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=28,
                          radius1=r, radius2=r, depth=h)
    for f in bm.faces:
        f.material_index = 0
    n = 28
    for i in range(0, n, 2):
        a0 = TAU * i / n; a1 = TAU * (i + 1) / n
        add_annulus(bm, r * 0.86, r * 0.985, a0, a1, h * 0.5 + 0.0006, 3, 1)
        add_annulus(bm, r * 0.86, r * 0.985, a0, a1, -h * 0.5 - 0.0006, 3, 1)
    top_z = h * 0.5 + 0.0012
    append_bm(bm, glyph(value_text),
             Matrix.Translation((0, 0, top_z)) @ Matrix.Diagonal((0.095, 0.095, 1, 1)), 2)
    add_annulus(bm, r * 0.60, r * 0.66, 0, TAU, top_z - 0.0004, 32, 1)
    return bm

def build_chip(value_text, body_mat, ink_mat, accent_mat, name):
    bm = build_chip_bm(value_text)
    return obj_from(bm, name, [body_mat, accent_mat, ink_mat], loc=(0, 0, -300),
                    smooth=True)

# ============================================================ dice
DIE_S = 0.11
FACE_BASIS = {
    1: (Vector((0, 0, 1)), Vector((1, 0, 0)), Vector((0, 1, 0))),
    6: (Vector((0, 0, -1)), Vector((1, 0, 0)), Vector((0, -1, 0))),
    2: (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))),
    5: (Vector((-1, 0, 0)), Vector((0, -1, 0)), Vector((0, 0, 1))),
    3: (Vector((0, 1, 0)), Vector((-1, 0, 0)), Vector((0, 0, 1))),
    4: (Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))),
}
DIE_LAYOUTS = {
    1: [(0, 0)],
    2: [(-0.5, 0.5), (0.5, -0.5)],
    3: [(-0.5, 0.5), (0, 0), (0.5, -0.5)],
    4: [(-0.5, 0.5), (0.5, 0.5), (-0.5, -0.5), (0.5, -0.5)],
    5: [(-0.5, 0.5), (0.5, 0.5), (0, 0), (-0.5, -0.5), (0.5, -0.5)],
    6: [(-0.5, 0.5), (0.5, 0.5), (-0.5, 0), (0.5, 0), (-0.5, -0.5), (0.5, -0.5)],
}

def build_die_bm():
    s = DIE_S
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Diagonal((s, s, s, 1.0)))
    bevel_all(bm, 0.016, 3)
    for f in bm.faces:
        f.material_index = 0
    hs = s * 0.5
    pip_r = s * 0.11
    for val, (n, u, v) in FACE_BASIS.items():
        basis = Matrix((tuple(u), tuple(v), tuple(n))).transposed().to_3x3().to_4x4()
        for pu, pv in DIE_LAYOUTS[val]:
            local = u * (pu * hs * 0.60) + v * (pv * hs * 0.60) + n * (hs * 1.002)
            mt = Matrix.Translation(local) @ basis
            vs = [bm.verts.new(mt @ Vector((math.cos(TAU * i / 10) * pip_r,
                                            math.sin(TAU * i / 10) * pip_r, 0)))
                  for i in range(10)]
            f = bm.faces.new(vs); f.material_index = 1
    return bm

def die_up_quat(value, spin=0.0):
    n, u, v = FACE_BASIS[value]
    q = n.rotation_difference(Vector((0, 0, 1)))
    if spin:
        q = Quaternion((0, 0, 1), spin) @ q
    return q

# ============================================================ materials
mat = {}
def build_mats():
    mat['stock'] = M("Stock", (0.97, 0.95, 0.90), 0.55)
    mat['red'] = M("Ink_Red", (0.72, 0.06, 0.08), 0.55)
    mat['black'] = M("Ink_Black", (0.05, 0.05, 0.06), 0.55)
    mat['backA'] = M("BackA", (0.55, 0.08, 0.10), 0.55)
    mat['backB'] = M("BackB", (0.10, 0.20, 0.55), 0.55)
    mat['felt'] = M("TableFelt", (0.06, 0.32, 0.19), 0.92)
    mat['wood'] = M("TableWood", (0.42, 0.26, 0.14), 0.75)
    mat['cup'] = M("CupMetal", (0.20, 0.21, 0.24), 0.35, metallic=0.8)
    mat['white'] = M("PipWhite", (0.96, 0.96, 0.94), 0.55)
    mat['diceblack'] = M("DiceBlack", (0.10, 0.11, 0.13), 0.42)
    mat['dicepip'] = M("DicePip", (0.86, 0.09, 0.09), 0.45)
    mat['gold'] = M("Gold", (0.85, 0.66, 0.20), 0.30, metallic=0.5)
    mat['chip_white'] = M("ChipWhite", (0.92, 0.92, 0.89), 0.55)
    mat['chip_red'] = M("ChipRed", (0.72, 0.10, 0.10), 0.55)
    mat['chip_blue'] = M("ChipBlue", (0.10, 0.28, 0.66), 0.55)
    mat['chip_green'] = M("ChipGreen", (0.10, 0.46, 0.20), 0.55)
    mat['chip_black'] = M("ChipBlack", (0.08, 0.08, 0.09), 0.55)
    mat['chip_ink_w'] = mat['white']
    mat['chip_ink_b'] = mat['black']
    mat['chip_ink_g'] = mat['gold']
    mat['floor'] = M("Floor", (0.14, 0.15, 0.18), 0.85)

# ============================================================ demo table + rig
RIG = {}
TBL = (0.0, 0.0)
TBL_TOP = 0.80

def build_table():
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=48,
                          radius1=1.15, radius2=1.15, depth=0.12,
                          matrix=Matrix.Translation((0, 0, TBL_TOP - 0.06)))
    bevel_all(bm, 0.03, 2)
    obj_from(bm, "TableTop", mat['wood'], loc=TBL + (0,), smooth=True)
    bm2 = bmesh.new()
    bmesh.ops.create_cone(bm2, cap_ends=True, cap_tris=False, segments=48,
                          radius1=1.00, radius2=1.00, depth=0.02,
                          matrix=Matrix.Translation((0, 0, TBL_TOP + 0.005)))
    obj_from(bm2, "TableFelt", mat['felt'], loc=TBL + (0,), smooth=True)
    bm3 = bmesh.new()
    bmesh.ops.create_cone(bm3, cap_ends=True, cap_tris=False, segments=16,
                          radius1=0.24, radius2=0.34, depth=TBL_TOP - 0.06)
    obj_from(bm3, "TableLeg", mat['wood'], loc=(TBL[0], TBL[1], (TBL_TOP - 0.06) * 0.5),
             smooth=True)
    bmf = bmesh.new()
    bmesh.ops.create_cone(bmf, cap_ends=True, cap_tris=False, segments=48,
                          radius1=15.0, radius2=15.0, depth=0.05,
                          matrix=Matrix.Translation((0, 0, -0.025)))
    obj_from(bmf, "Floor", mat['floor'], loc=(0, 0, 0), smooth=True)

def build_dice_cup():
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=20,
                          radius1=0.20, radius2=0.23, depth=0.40)
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=20,
                          radius1=0.20, radius2=0.20, depth=0.03,
                          matrix=Matrix.Translation((0, 0, 0.20)))
    bevel_all(bm, 0.02, 2)
    RIG['cup'] = obj_from(bm, "DiceCup", mat['cup'], loc=(0.65, -0.35, TBL_TOP + 0.24),
                          smooth=True)

def build_kit_scene(n_dice=5):
    build_suit_shapes()
    build_back_pattern()
    build_mats()
    build_table()
    build_dice_cup()

    RIG['deckA'] = build_deck("DeckA", mat['backA'], mat['stock'], mat['red'], mat['black'])
    RIG['deckB'] = build_deck("DeckB", mat['backB'], mat['stock'], mat['red'], mat['black'])

    ax, ay = TBL[0] - 0.55, TBL[1] + 0.50
    for i, c in enumerate(RIG['deckA']):
        c.rotation_mode = 'QUATERNION'
        c.location = (ax, ay, TBL_TOP + 0.008 + i * (CARD_TH + 0.0006))
        c.rotation_quaternion = Quaternion((0, 0, 1), math.pi)  # start face-down
    bx, by = TBL[0] + 0.55, TBL[1] + 0.50
    for i, c in enumerate(RIG['deckB']):
        c.rotation_mode = 'QUATERNION'
        c.location = (bx, by, TBL_TOP + 0.008 + i * (CARD_TH + 0.0006))
        c.rotation_quaternion = Quaternion((0, 0, 1), math.pi)

    die_proto = obj_from(build_die_bm(), "DieProto",
                         [mat['diceblack'], mat['dicepip']], loc=(0, 0, -300))
    RIG['dice'] = []
    cx, cy = 0.65, -0.35
    for i in range(n_dice):
        d = dup(die_proto, "Die%d" % i, (cx + (i - 2) * 0.10, cy, TBL_TOP + 0.10))
        d.rotation_mode = 'QUATERNION'
        RIG['dice'].append(d)

    RIG['chip_stacks'] = {}
    around = [(-1.05, -0.75), (-0.35, -1.0), (0.35, -1.0), (1.05, -0.75)]
    for (val, body_key, ink_key), (sx, sy) in zip(CHIP_SPECS, around + [(0, -1.15)]):
        proto = build_chip(val, mat[body_key],
                           mat['chip_ink_w'] if ink_key == 'white' else
                           (mat['chip_ink_g'] if ink_key == 'gold' else mat['chip_ink_b']),
                           mat['gold'] if ink_key == 'gold' else mat['stock'],
                           "Chip_%s_proto" % val)
        stack = []
        for j in range(14):
            ob = dup(proto, "Chip_%s_%d" % (val, j),
                    (sx, sy, TBL_TOP + 0.006 + j * (CHIP_H + 0.0008)))
            stack.append(ob)
        proto.location = (0, 0, -300)
        RIG['chip_stacks'][val] = stack

# ============================================================ animations
F_END = 330

def animate_deck_fan():
    """Deck A fans open in an arc, showing every face for a beat."""
    deck = RIG['deckA']
    ax, ay = TBL[0] - 0.55, TBL[1] + 0.50
    n = len(deck)
    for i, c in enumerate(deck):
        z0 = TBL_TOP + 0.008 + i * (CARD_TH + 0.0006)
        key(c, 1, loc=(ax, ay, z0), quat=Quaternion((0, 0, 1), math.pi))
        t = i / (n - 1)
        fx = TBL[0] - 0.9 + t * 1.8
        fy = TBL[1] + 1.55
        a = -0.55 + t * 1.10
        key(c, 55 + i, loc=(fx, fy, TBL_TOP + 0.05 + i * 0.0009),
            quat=Quaternion((0, 0, 1), a))
        key(c, 100, loc=(fx, fy, TBL_TOP + 0.05 + i * 0.0009),
            quat=Quaternion((0, 0, 1), a))
        key(c, 118, loc=(ax, ay, z0), quat=Quaternion((0, 0, 1), math.pi))
    set_interp(deck[0] if deck else None, 'BEZIER') if False else None

HANDS = [((0, -1.75), 0.0), ((1.75, 0), math.pi * 1.5), ((0, 1.75), math.pi),
         ((-1.75, 0), math.pi * 0.5)]

def animate_deal():
    """Deal five cards to each of four seats, flipping each face-up as it lands."""
    deck = RIG['deckA']
    idx = len(deck) - 1
    f = 130
    for round_i in range(5):
        for (hx, hy), rz in HANDS:
            c = deck[idx]; idx -= 1
            p0 = tuple(c.location)
            spread = (round_i - 2) * 0.16
            tx = hx + math.cos(rz + math.pi / 2) * spread
            ty = hy + math.sin(rz + math.pi / 2) * spread
            p1 = (tx, ty, TBL_TOP + 0.02 + round_i * 0.004)
            c.rotation_mode = 'XYZ'
            key(c, f, loc=p0, rot=(0, 0, math.pi + rz))
            ballistic(c, f, f + 14, p0, p1, 0.55)
            key(c, f, rot=(0, 0, math.pi + rz))
            key(c, f + 14, rot=(0, 0, math.pi + rz))
            key(c, f + 24, rot=(0, 0, rz))
            f += 5
        f += 6

def animate_dice():
    f0 = 210
    cup = RIG['cup']
    home = tuple(cup.location)
    key(cup, f0, loc=home)
    key(cup, f0 + 8, loc=(home[0], home[1], home[2] + 0.7))
    key(cup, f0 + 16, loc=home)
    key(cup, f0 + 19, loc=(home[0], home[1], home[2] + 0.06))
    key(cup, f0 + 22, loc=home)
    key(cup, f0 + 60, loc=home)
    key(cup, f0 + 74, loc=(home[0], home[1], home[2] + 1.05))
    key(cup, f0 + 96, loc=(home[0], home[1], home[2] + 1.05))
    values = [4, 4, 6, 1, 3]
    for i, d in enumerate(RIG['dice']):
        p = tuple(d.location)
        for f, rq in ((f0, Quaternion((1, 0.3, 0.2), random.random() * TAU)),
                     (f0 + 8, Quaternion((1, 0.3, 0.2), random.random() * TAU))):
            key(d, f, loc=p, quat=rq)
        key(d, f0 + 22, loc=p, quat=Quaternion((1, 0.3, 0.2), random.random() * TAU))
        settle = die_up_quat(values[i % len(values)], spin=random.uniform(0, TAU))
        key(d, f0 + 74, loc=p, quat=Quaternion((0.4, 0.7, 0.1), random.random() * TAU))
        key(d, f0 + 90, loc=(p[0] + random.uniform(-0.05, 0.05),
                             p[1] + random.uniform(-0.05, 0.05), p[2]), quat=settle)
        key(d, f0 + 110, loc=d.location, quat=settle)
        set_interp(d, 'LINEAR', only_after=f0 - 0.5)

def animate_chips():
    f0 = 250
    pot = (TBL[0], TBL[1] + 0.05, TBL_TOP + 0.03)
    for val, stack in RIG['chip_stacks'].items():
        for i in range(0, min(6, len(stack))):
            ob = stack[-1 - i]
            p0 = tuple(ob.location)
            p1 = (pot[0] + random.uniform(-0.30, 0.30),
                 pot[1] + random.uniform(-0.30, 0.30), pot[2] + i * 0.03)
            ff = f0 + i * 3
            ballistic(ob, ff, ff + 16, p0, p1, 0.35,
                     spin=((0, 0, 1), random.uniform(1, 2)))

# ============================================================ world / camera
def build_world_and_lights():
    w = bpy.data.worlds.new("Studio"); bpy.context.scene.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.10, 0.11, 0.14, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.35

    kd = bpy.data.lights.new("Key", type='AREA')
    kd.energy = 700.0; kd.color = (1.0, 0.93, 0.82); kd.size = 2.2
    ko = bpy.data.objects.new("Key", kd); bpy.context.collection.objects.link(ko)
    ko.location = (2.0, -2.0, 3.2); ko.rotation_euler = (math.radians(48), 0, math.radians(45))

    fd = bpy.data.lights.new("Fill", type='AREA')
    fd.energy = 220.0; fd.color = (0.55, 0.68, 1.0); fd.size = 3.0
    fo = bpy.data.objects.new("Fill", fd); bpy.context.collection.objects.link(fo)
    fo.location = (-2.4, -1.0, 2.2); fo.rotation_euler = (math.radians(55), 0, math.radians(-55))

    rd = bpy.data.lights.new("Rim", type='AREA')
    rd.energy = 300.0; rd.color = (0.8, 0.85, 1.0); rd.size = 2.0
    ro = bpy.data.objects.new("Rim", rd); bpy.context.collection.objects.link(ro)
    ro.location = (0.2, 2.4, 2.6); ro.rotation_euler = (math.radians(120), 0, math.radians(10))

def make_cam(name, loc, look, lens=45.0):
    tgt = bpy.data.objects.new(name + "_T", None)
    bpy.context.collection.objects.link(tgt); tgt.location = look
    cd = bpy.data.cameras.new(name); cd.lens = lens
    cam = bpy.data.objects.new(name, cd)
    bpy.context.collection.objects.link(cam); cam.location = loc
    c = cam.constraints.new('TRACK_TO'); c.target = tgt
    c.track_axis = 'TRACK_NEGATIVE_Z'; c.up_axis = 'UP_Y'
    return cam

def build_grid_display(cards, origin, cols=13):
    """A flat lay of every card in the deck, face-up, for visual QA."""
    for i, c in enumerate(cards):
        row = i // cols; col = i % cols
        c.rotation_mode = 'QUATERNION'
        c.rotation_quaternion = Quaternion((0, 0, 1), 0.0)
        c.location = (origin[0] + (col - cols / 2 + 0.5) * 0.34,
                     origin[1] - row * 0.46, 0.02)

def enable_gpu():
    try:
        pr = bpy.context.preferences.addons['cycles'].preferences
        for kind in ('OPTIX', 'CUDA', 'HIP', 'ONEAPI', 'METAL'):
            try: pr.compute_device_type = kind
            except Exception: continue
            try: pr.get_devices()
            except Exception: pass
            ds = [d for d in pr.devices if d.type == kind]
            if ds:
                for d in pr.devices: d.use = (d.type == kind)
                print("### GPU", kind, [d.name for d in ds]); return True
    except Exception as e:
        print("### gpu err", e)
    return False

def render_setup(samples=96):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'GPU' if enable_gpu() else 'CPU'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = 'Standard'
    sc.render.resolution_x = 1280
    sc.render.resolution_y = 720
    return sc

# ============================================================ entry points
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

def run_probe():
    """Fast sanity check: 1 die + 4 hand-picked cards, close camera, few samples."""
    reset()
    build_suit_shapes(); build_back_pattern(); build_mats()
    bmf = bmesh.new()
    bmesh.ops.create_cone(bmf, cap_ends=True, cap_tris=False, segments=4,
                          radius1=2.0, radius2=2.0, depth=0.05,
                          matrix=Matrix.Translation((0, 0, -0.025)))
    obj_from(bmf, "Floor", mat['felt'], loc=(0, 0, 0), smooth=True)

    picks = [('A', 'S'), ('K', 'H'), ('10', 'D'), ('7', 'C')]
    for i, (r, s) in enumerate(picks):
        c = build_card(r, s, mat['backA'], mat['stock'], mat['red'], mat['black'],
                       "Probe_%s%s" % (r, s))
        c.location = (-0.6 + i * 0.42, 0, 0.03)
        c.rotation_euler = (0, 0, 0)

    die = obj_from(build_die_bm(), "ProbeDie", [mat['diceblack'], mat['dicepip']],
                   loc=(0.9, -0.6, 0.10))
    die.rotation_euler = (math.radians(18), math.radians(24), 0.3)

    build_world_and_lights()
    cam = make_cam("CAM_probe", (0.4, -1.7, 1.6), (0.2, 0, 0.15), 40)
    sc = render_setup(48)
    sc.camera = cam
    sc.render.filepath = OUT + "/probe_kit.png"
    bpy.ops.render.render(write_still=True)
    print("### PROBE DONE")

def run_full():
    reset()
    build_kit_scene(n_dice=5)
    animate_deck_fan()
    animate_deal()
    animate_dice()
    animate_chips()

    sc = bpy.context.scene
    sc.frame_start = 1; sc.frame_end = F_END; sc.render.fps = FPS
    build_world_and_lights()

    cam_table = make_cam("CAM_table", (0.0, -2.75, 2.35), (0.0, 0.2, TBL_TOP + 0.1), 42)
    key(cam_table, 1, loc=(0.0, -2.75, 2.35))
    key(cam_table, F_END, loc=(0.8, -2.2, 2.05))
    cam_deal = make_cam("CAM_deal", (0.0, -1.7, 3.4), (0.0, 0.25, TBL_TOP), 30)
    cam_dice = make_cam("CAM_dice", (1.55, -1.05, 1.55), (0.65, -0.35, TBL_TOP + 0.2), 42)

    tris = 0
    for o in sc.objects:
        if o.type == 'MESH':
            o.data.calc_loop_triangles()
            tris += len(o.data.loop_triangles)
    print("### OBJECTS=%d TRIS=%d FRAMES=%d" % (len(sc.objects), tris, F_END))

    bpy.ops.wm.save_as_mainfile(filepath=OUT + "/card_game_kit.blend")
    print("### SAVED blend")
    sc.timeline_markers.clear()

    render_setup(96)
    sc.camera = cam_table
    sc.frame_set(30)
    sc.render.filepath = OUT + "/kit_table.png"
    bpy.ops.render.render(write_still=True)
    print("### STILL table")

    sc.camera = cam_deal
    sc.frame_set(230)
    sc.render.filepath = OUT + "/kit_deal.png"
    bpy.ops.render.render(write_still=True)
    print("### STILL deal")

    sc.camera = cam_dice
    sc.frame_set(300)
    sc.render.filepath = OUT + "/kit_dice.png"
    bpy.ops.render.render(write_still=True)
    print("### STILL dice")

    # move the two decks off-table into flat-lay grids for a full visual QA pass
    for c in RIG['deckA']:
        c.animation_data_clear()
    for c in RIG['deckB']:
        c.animation_data_clear()
    build_grid_display(RIG['deckA'], (0, 6.0))
    build_grid_display(RIG['deckB'], (0, -6.0))
    sc.render.resolution_x = 1600; sc.render.resolution_y = 1000
    cam_gridA = make_cam("CAM_gridA", (0, 5.31, 5.2), (0, 5.31, 0), 30)
    sc.camera = cam_gridA
    sc.render.filepath = OUT + "/kit_deck_A.png"
    bpy.ops.render.render(write_still=True)
    print("### STILL deckA")
    cam_gridB = make_cam("CAM_gridB", (0, -6.69, 5.2), (0, -6.69, 0), 30)
    sc.camera = cam_gridB
    sc.render.filepath = OUT + "/kit_deck_B.png"
    bpy.ops.render.render(write_still=True)
    print("### STILL deckB")
    print("### DONE")

if "full" in ARGS:
    run_full()
else:
    run_probe()
