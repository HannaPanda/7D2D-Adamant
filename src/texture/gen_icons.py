"""Generate 160x160 RGBA item icons for Adamant Ore and Adamant Ingot."""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 160
PURPLE_HI  = (196, 150, 235)
PURPLE_MID = (123,  79, 176)
PURPLE_LO  = ( 78,  46, 120)
PURPLE_DK  = ( 44,  24,  70)
CRYST_HI   = (214, 170, 255)
CRYST_MID  = (150,  92, 220)

def shadow(draw_img, cx, cy, rx, ry):
    sh = Image.new("RGBA", (S, S), (0,0,0,0))
    d = ImageDraw.Draw(sh)
    d.ellipse([cx-rx, cy-ry, cx+rx, cy+ry], fill=(0,0,0,110))
    sh = sh.filter(ImageFilter.GaussianBlur(6))
    draw_img.alpha_composite(sh)

def vgrad_poly(img, pts, c_top, c_bot, ymin, ymax):
    """Fill polygon pts with a vertical gradient c_top->c_bot between ymin..ymax."""
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).polygon(pts, fill=255)
    grad = np.zeros((S, S, 3), np.uint8)
    for y in range(S):
        t = np.clip((y - ymin) / max(1, (ymax - ymin)), 0, 1)
        grad[y, :] = [int(c_top[i]*(1-t) + c_bot[i]*t) for i in range(3)]
    g = Image.fromarray(grad, "RGB").convert("RGBA")
    img.paste(g, (0, 0), mask)

# ---------------- Adamant Ingot ----------------
def make_ingot():
    img = Image.new("RGBA", (S, S), (0,0,0,0))
    shadow(img, 82, 128, 58, 16)
    d = ImageDraw.Draw(img)
    # top face (trapezoid, receding)
    top = [(40,84),(120,84),(104,58),(56,58)]
    vgrad_poly(img, top, PURPLE_HI, PURPLE_MID, 58, 84)
    # front face
    front = [(40,84),(120,84),(126,122),(34,122)]
    vgrad_poly(img, front, PURPLE_MID, PURPLE_LO, 84, 122)
    d = ImageDraw.Draw(img)
    # edges
    d.line(top+[top[0]], fill=PURPLE_HI+(255,), width=2)
    d.line([(40,84),(120,84)], fill=(230,205,255,255), width=2)     # bright top-front edge
    d.line(front+[front[0]], fill=PURPLE_DK+(255,), width=2)
    # specular highlight on top
    hi = Image.new("RGBA",(S,S),(0,0,0,0))
    ImageDraw.Draw(hi).polygon([(58,74),(92,74),(86,64),(64,64)], fill=(255,255,255,60))
    img.alpha_composite(hi.filter(ImageFilter.GaussianBlur(2)))
    # stamped "A" hint on front
    d.text((72,92), "", fill=(255,255,255,0))
    return img

# ---------------- Adamant Ore ----------------
def make_ore():
    img = Image.new("RGBA", (S, S), (0,0,0,0))
    shadow(img, 82, 132, 52, 14)
    # angular rocky body (sharp, faceted silhouette)
    cx, cy = 80, 96
    body = [(40,92),(52,64),(80,54),(108,66),(120,96),(108,124),(74,130),(48,116)]
    vgrad_poly(img, body, (104,78,132), PURPLE_DK, 54, 130)
    d = ImageDraw.Draw(img)
    d.line(body+[body[0]], fill=(30,16,50,255), width=3)
    # inner facet shading (darker lower-right plane)
    vgrad_poly(img, [(80,54),(120,96),(74,130),(80,96)], PURPLE_LO, PURPLE_DK, 54, 130)
    # glow core behind the crystals
    gl = Image.new("RGBA",(S,S),(0,0,0,0))
    ImageDraw.Draw(gl).ellipse([54,50,110,100], fill=(150,90,220,90))
    img.alpha_composite(gl.filter(ImageFilter.GaussianBlur(7)))
    # a cluster of big bright crystal shards bursting from the top
    shards = [
        [(62,84),(74,40),(84,84)],     # tall center-left
        [(80,86),(96,50),(104,86)],    # tall right
        [(50,90),(60,66),(72,92)],     # short left
    ]
    for tri in shards:
        ys = [p[1] for p in tri]
        vgrad_poly(img, tri, CRYST_HI, CRYST_MID, min(ys), max(ys))
        d.line(tri+[tri[0]], fill=(235,210,255,255), width=2)
        # bright facet edge down the middle
        apex = min(tri, key=lambda p:p[1]); base=( (tri[0][0]+tri[2][0])/2, max(ys) )
        d.line([apex, base], fill=(255,240,255,200), width=1)
    # sparkles on the crystal tips
    for (sx,sy,r) in [(74,42,3),(96,52,3),(60,67,2)]:
        s2 = Image.new("RGBA",(S,S),(0,0,0,0))
        ImageDraw.Draw(s2).ellipse([sx-r,sy-r,sx+r,sy+r], fill=(255,250,255,230))
        img.alpha_composite(s2.filter(ImageFilter.GaussianBlur(1)))
    return img

make_ingot().save("adamant_ingot.png")
make_ore().save("adamant_ore.png")
print("wrote adamant_ingot.png + adamant_ore.png (160x160 RGBA)")
