#!/usr/bin/env python3
"""
Genera un icono PNG para cada boton de las plantillas .deckpack y lo mete adentro
del paquete (carpeta icons/ + campo icon_file en el manifest).

Los iconos son propios: un cuadrado redondeado del color de la familia con el
nombre del plugin, ajustado para que se lea a tamano de tile. Nada de logos de
terceros, asi que la plantilla se puede repartir sin problema de marca.

Uso:  python herramientas/generar-iconos-plantillas.py [nombre.deckpack ...]
Sin argumentos procesa todas las de plantillas/.
"""
import io
import json
import re
import sys
import unicodedata
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

RAIZ = Path(__file__).resolve().parent.parent
PLANTILLAS = RAIZ / "plantillas"
ICONOS = PLANTILLAS / "iconos"

TAM = 320          # lado del PNG en px
RADIO = 46         # radio de la esquina redondeada
MARGEN = 26        # aire minimo entre el texto y el borde
MEZCLA_DEGRADE = 0.16

_fuentes: dict[int, ImageFont.FreeTypeFont] = {}


def cargar_fuente(tam: int) -> ImageFont.FreeTypeFont:
    if tam in _fuentes:
        return _fuentes[tam]
    for nombre in ("segoeuib.ttf", "seguisb.ttf", "arialbd.ttf"):
        try:
            f = ImageFont.truetype(f"C:/Windows/Fonts/{nombre}", tam)
            break
        except OSError:
            continue
    else:
        f = ImageFont.truetype("DejaVuSans-Bold.ttf", tam)  # viene con Pillow
    _fuentes[tam] = f
    return f


def slug(texto: str) -> str:
    t = unicodedata.normalize("NFKD", texto).encode("ascii", "ignore").decode()
    t = re.sub(r"[^A-Za-z0-9]+", "-", t).strip("-").lower()
    return t or "boton"


def _canal(c: float) -> float:
    return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4


def luminancia(hexcolor: str) -> float:
    h = hexcolor.lstrip("#")
    r, g, b = (int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))
    return 0.2126 * _canal(r) + 0.7152 * _canal(g) + 0.0722 * _canal(b)


def mezclar(hexcolor: str, hacia: int, cantidad: float) -> tuple[int, int, int]:
    h = hexcolor.lstrip("#")
    base = [int(h[i:i + 2], 16) for i in (0, 2, 4)]
    return tuple(round(b + (hacia - b) * cantidad) for b in base)


def envolver(draw: ImageDraw.ImageDraw, texto: str, fuente, ancho_max: float) -> list[str]:
    lineas: list[str] = []
    actual = ""
    for palabra in texto.split():
        prueba = f"{actual} {palabra}".strip()
        if not actual or draw.textlength(prueba, font=fuente) <= ancho_max:
            actual = prueba
        else:
            lineas.append(actual)
            actual = palabra
    if actual:
        lineas.append(actual)
    return lineas


def dibujar_icono(label: str, color_hex: str) -> bytes:
    img = Image.new("RGBA", (TAM, TAM), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # Fondo redondeado con un degrade vertical sutil, esquinas transparentes.
    arriba = mezclar(color_hex, 255, MEZCLA_DEGRADE)
    abajo = mezclar(color_hex, 0, MEZCLA_DEGRADE)
    columna = Image.new("RGB", (1, TAM))
    for y in range(TAM):
        t = y / (TAM - 1)
        columna.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(arriba, abajo)))
    fondo = columna.resize((TAM, TAM))

    mascara = Image.new("L", (TAM, TAM), 0)
    ImageDraw.Draw(mascara).rounded_rectangle([2, 2, TAM - 3, TAM - 3], radius=RADIO, fill=255)
    img.paste(fondo, (0, 0), mascara)

    # Texto: el nombre del plugin, agrandado hasta donde entre en dos o tres lineas.
    tinta = (11, 16, 19) if luminancia(color_hex) > 0.55 else (245, 247, 249)
    ancho_max = TAM - 2 * MARGEN

    lineas = [label]
    fuente = cargar_fuente(30)
    alto_linea = 1
    for tam in range(96, 25, -3):
        fuente = cargar_fuente(tam)
        lineas = envolver(draw, label, fuente, ancho_max)
        alto_linea = fuente.getbbox("Ag")[3]
        alto_total = alto_linea * len(lineas) * 1.14
        entra_ancho = all(draw.textlength(l, font=fuente) <= ancho_max for l in lineas)
        if entra_ancho and alto_total <= TAM - 2 * MARGEN and len(lineas) <= 3:
            break

    alto_total = alto_linea * len(lineas) * 1.14
    y = (TAM - alto_total) / 2
    for linea in lineas:
        w = draw.textlength(linea, font=fuente)
        draw.text(((TAM - w) / 2, y), linea, font=fuente, fill=tinta)
        y += alto_linea * 1.14

    buffer = io.BytesIO()
    img.save(buffer, "PNG")
    return buffer.getvalue()


def procesar(pack: Path) -> int:
    salida_dir = ICONOS / pack.stem
    salida_dir.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(pack) as z:
        manifest = json.loads(z.read("manifest.json"))

    usados: dict[str, int] = {}
    pngs: dict[str, bytes] = {}

    def recorrer(items: list) -> None:
        for item in items:
            if item.get("type") == "button":
                label = item.get("label", "").strip() or "Boton"
                base = slug(label)
                n = usados.get(base, 0)
                usados[base] = n + 1
                archivo = f"{base}.png" if n == 0 else f"{base}-{n + 1}.png"

                data = dibujar_icono(label, item.get("color_hex") or "#33C9DC")
                (salida_dir / archivo).write_bytes(data)
                pngs[archivo] = data

                # icon_file justo despues de icon_glyph, respetando el orden de claves.
                reordenado = {}
                for clave, valor in item.items():
                    reordenado[clave] = valor
                    if clave == "icon_glyph":
                        reordenado["icon_file"] = archivo
                reordenado.setdefault("icon_file", archivo)
                item.clear()
                item.update(reordenado)

            recorrer(item.get("children", []))

    for page in manifest["profile"]["pages"]:
        recorrer(page["items"])

    tmp = pack.with_suffix(".deckpack.tmp")
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("manifest.json", json.dumps(manifest, indent=2, ensure_ascii=False))
        for archivo, data in sorted(pngs.items()):
            z.writestr(f"icons/{archivo}", data)
    tmp.replace(pack)
    return len(pngs)


def main() -> None:
    pedidas = sys.argv[1:]
    packs = ([PLANTILLAS / n for n in pedidas]
             if pedidas
             else sorted(PLANTILLAS.glob("*.deckpack")))

    faltan = [p for p in packs if not p.exists()]
    if faltan:
        sys.exit("No existe: " + ", ".join(str(p) for p in faltan))

    total = 0
    for pack in packs:
        n = procesar(pack)
        total += n
        print(f"{pack.name:24} {n:4} iconos")
    print(f"\n{total} iconos, guardados en plantillas/iconos/ y dentro de cada .deckpack")


if __name__ == "__main__":
    main()
