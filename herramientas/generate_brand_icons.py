from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

OUT = Path('generated-brand-icons')
OUT.mkdir(exist_ok=True)

FONT_DIR = Path(r'C:\Windows\Fonts')

def font(names, size):
    for name in names:
        p = FONT_DIR / name
        if p.exists():
            return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()

def fit_font(draw, text, names, max_size, max_width):
    for size in range(max_size, 10, -1):
        f = font(names, size)
        box = draw.textbbox((0, 0), text, font=f)
        if box[2] - box[0] <= max_width:
            return f
    return font(names, 10)

def make_icon(filename, label, brand, bg, fg, accent, font_names, subtitle=None, accent2=None):
    size = 128
    im = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((3, 3, size-3, size-3), radius=22, fill=bg, outline=accent, width=2)
    d.rounded_rectangle((10, 10, size-10, 17), radius=3, fill=accent)
    if accent2:
        d.rounded_rectangle((size-31, 10, size-10, 17), radius=3, fill=accent2)
    main = fit_font(d, label, font_names, 58, 102)
    box = d.textbbox((0, 0), label, font=main)
    x = (size - (box[2]-box[0])) // 2 - box[0]
    y = 39 - box[1]
    d.text((x+1, y+2), label, font=main, fill=(0,0,0,130))
    d.text((x, y), label, font=main, fill=fg)
    sub = subtitle or brand.upper()
    sf = fit_font(d, sub, ['segoeuib.ttf', 'arialbd.ttf', 'segoeui.ttf'], 13, 103)
    sb = d.textbbox((0,0), sub, font=sf)
    d.text(((size-(sb[2]-sb[0]))//2, 101), sub, font=sf, fill=accent)
    im.save(OUT / filename)

icons = [
    ('brand-fabfilter-q4.png', 'Q4', 'FabFilter', '#151923', '#f6f7fb', '#5ee7ff', ['segoeuib.ttf','segoeui.ttf'], 'FABFILTER', '#e56cff'),
    ('brand-fabfilter-mb.png', 'MB', 'FabFilter', '#151923', '#f6f7fb', '#5ee7ff', ['segoeuib.ttf','segoeui.ttf'], 'FABFILTER', '#e56cff'),
    ('brand-fabfilter-ds.png', 'DS', 'FabFilter', '#151923', '#f6f7fb', '#5ee7ff', ['segoeuib.ttf','segoeui.ttf'], 'FABFILTER', '#e56cff'),
    ('brand-oeksound-soothe2.png', 'soothe2', 'oeksound', '#10242a', '#e6ffff', '#52d4c5', ['segoeui.ttf','arial.ttf'], 'OEKSOUND'),
    ('brand-waves-znoise.png', 'Z', 'Waves', '#151515', '#ffffff', '#f2a900', ['arialbd.ttf','segoeuib.ttf'], 'WAVES'),
    ('brand-waves-ns1.png', 'NS1', 'Waves', '#151515', '#ffffff', '#f2a900', ['arialbd.ttf','segoeuib.ttf'], 'WAVES'),
    ('brand-waves-cla2a.png', 'CLA-2A', 'Waves', '#151515', '#ffffff', '#f2a900', ['arialbd.ttf','segoeuib.ttf'], 'WAVES'),
    ('brand-waves-rvox.png', 'R-Vox', 'Waves', '#151515', '#ffffff', '#f2a900', ['arialbd.ttf','segoeuib.ttf'], 'WAVES'),
    ('brand-valhalla-vintageverb.png', 'VV', 'Valhalla', '#21172f', '#f9f1ff', '#c08cff', ['timesbd.ttf','times.ttf'], 'VALHALLA'),
    ('brand-ableton-delay.png', 'DLY', 'Ableton', '#271b12', '#fff5e8', '#ff9b35', ['bahnschrift.ttf','segoeuib.ttf'], 'ABLETON'),
    ('brand-ableton-live.png', 'LIVE', 'Ableton', '#271b12', '#fff5e8', '#ff9b35', ['bahnschrift.ttf','segoeuib.ttf'], 'ABLETON'),
    ('brand-softube-cl1b.png', 'CL1B', 'Softube', '#1b252c', '#f4f7f8', '#d5a85b', ['bahnschrift.ttf','arialbd.ttf'], 'SOFTUBE'),
    ('brand-roland-zenology.png', 'ZEN', 'Roland', '#10213c', '#eaf4ff', '#4e9bff', ['segoeuib.ttf','arialbd.ttf'], 'ROLAND'),
    ('brand-purity.png', 'PURITY', 'Purity', '#2d183c', '#fff4d3', '#f0b84d', ['segoeuib.ttf','arialbd.ttf'], 'PURITY'),
    ('brand-uhe-hive.png', 'HIVE', 'u-he', '#172d20', '#ecfff0', '#86df65', ['segoeuib.ttf','arialbd.ttf'], 'U-HE'),
    ('brand-kick3.png', 'K3', 'Kick 3', '#24160d', '#fff4e7', '#ff8b35', ['bahnschrift.ttf','arialbd.ttf'], 'KICK 3'),
    ('brand-splice.png', 'S', 'Splice', '#18131d', '#ffffff', '#d99aff', ['segoeuib.ttf','arialbd.ttf'], 'SPLICE'),
    ('brand-dune3.png', 'D3', 'DUNE 3', '#101f32', '#e9f4ff', '#5ab8ff', ['segoeuib.ttf','arialbd.ttf'], 'DUNE 3'),
    ('brand-electra.png', 'E', 'Electra', '#291633', '#fff1ff', '#ce72ff', ['segoeuib.ttf','arialbd.ttf'], 'ELECTRA'),
    ('brand-arturia-pigments.png', 'P', 'Arturia', '#321b13', '#fff6ed', '#ff8a4c', ['arialbd.ttf','segoeuib.ttf'], 'PIGMENTS'),
    ('brand-arturia-analoglab.png', 'AL', 'Arturia', '#321b13', '#fff6ed', '#ff8a4c', ['arialbd.ttf','segoeuib.ttf'], 'ANALOG LAB'),
]

for item in icons:
    make_icon(*item)
print(f'Generated {len(icons)} icons in {OUT.resolve()}')
