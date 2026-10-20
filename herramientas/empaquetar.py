#!/usr/bin/env python3
"""
Arma el ejecutable que se reparte: un unico DigitalDeck.exe, autocontenido.

Existe porque la receta se venia haciendo a mano y tiene detalles que no se pueden olvidar sin
darse cuenta: sin --self-contained el .exe pide un .NET instalado y falla en la maquina de otro,
sin PublishSingleFile quedan 297 archivos sueltos, y sin -p:DebugType=None viajan los simbolos.
Nada de esto avisa cuando falta: el .exe sale igual y se rompe en la maquina del otro.

    python herramientas/empaquetar.py                       # deja dist/DigitalDeck.exe
    python herramientas/empaquetar.py --destino "compi 3"   # lo deja ahi
    python herramientas/empaquetar.py --zip                 # ademas arma el .zip al lado
    python herramientas/empaquetar.py --con-tests           # corre la suite antes de publicar

En Python y no en PowerShell por la misma razon que el resto de las herramientas: el Remote Script
ya es Python obligatoriamente, asi que unificar resta un lenguaje en vez de sumarlo.
"""

import argparse
import shutil
import subprocess
import sys
import time
import zipfile
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
PROYECTO = RAIZ / "src" / "DigitalDeck.App" / "DigitalDeck.App.csproj"
SOLUCION = RAIZ / "DigitalDeck.sln"
PUBLICADO = RAIZ / "src" / "DigitalDeck.App" / "bin" / "Release" / "net8.0-windows" / "win-x64" / "publish"
EJECUTABLE = "DigitalDeck.exe"

#: La receta verificada. Cada bandera esta acá por un motivo, y el orden no importa salvo para leerla.
BANDERAS = [
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
]


def correr(comando, descripcion):
    """Corre un comando mostrando su salida. Corta el script si falla."""
    # flush explicito: sin el, estos dos renglones salen despues de toda la salida de dotnet,
    # porque el print de Python y el proceso hijo escriben al mismo stdout con buffers distintos.
    print(f"\n>> {descripcion}", flush=True)
    print(f"   {' '.join(str(parte) for parte in comando)}\n", flush=True)

    resultado = subprocess.run(comando, cwd=RAIZ)

    if resultado.returncode != 0:
        sys.exit(f"\nFallo: {descripcion} (codigo {resultado.returncode})")


def tamano(ruta):
    """El tamano de un archivo en MB, para poder mirarlo de reojo y ver si tiene sentido."""
    return ruta.stat().st_size / (1024 * 1024)


def publicar():
    if shutil.which("dotnet") is None:
        sys.exit("No encontre 'dotnet' en el PATH. Instala el SDK de .NET 8 y volve a probar.")

    # Sin esto, un publish sobre restos de otro puede dejar archivos que ya no corresponden.
    if PUBLICADO.exists():
        shutil.rmtree(PUBLICADO)

    correr(["dotnet", "publish", str(PROYECTO)] + BANDERAS, "Publicando el ejecutable")

    exe = PUBLICADO / EJECUTABLE

    if not exe.is_file():
        sys.exit(f"El publish termino bien pero no encontre {exe}.")

    # Al lado del .exe pueden quedar archivos que la carpeta que se reparte no necesita. Hasta
    # agosto de 2026 eran los .xml de WebView2; hoy no deberia quedar ninguno, y si aparece uno
    # nuevo conviene verlo en la salida en vez de repartirlo sin mirar.
    sobrantes = sorted(p.name for p in PUBLICADO.iterdir() if p.name != EJECUTABLE)

    if sobrantes:
        print("\n   Al lado del .exe quedaron estos archivos, que NO se copian:")
        for nombre in sobrantes:
            print(f"     - {nombre}")

    return exe


def entregar(exe, destino, comprimir):
    destino.mkdir(parents=True, exist_ok=True)
    final = destino / EJECUTABLE

    shutil.copy2(exe, final)
    print(f"\n   {final}  ({tamano(final):.1f} MB)")

    if not comprimir:
        return final

    zip_path = destino.with_suffix(".zip") if destino.name else destino / "DigitalDeck.zip"

    print(f"\n>> Comprimiendo (esto tarda: son {tamano(final):.0f} MB)")
    inicio = time.time()

    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(final, EJECUTABLE)

    print(f"   {zip_path}  ({tamano(zip_path):.1f} MB, {time.time() - inicio:.0f} s)")

    return final


def main():
    parser = argparse.ArgumentParser(
        description="Arma el DigitalDeck.exe que se reparte.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__)

    parser.add_argument(
        "--destino", default="dist",
        help="Carpeta donde dejar el .exe. Por defecto 'dist'. Relativa a la raiz del repo.")
    parser.add_argument(
        "--zip", action="store_true",
        help="Ademas del .exe, arma un .zip al lado de la carpeta destino.")
    parser.add_argument(
        "--con-tests", action="store_true",
        help="Corre toda la suite antes de publicar, y no publica si algo falla.")

    args = parser.parse_args()

    if args.con_tests:
        correr(["dotnet", "test", str(SOLUCION), "-v", "q", "--nologo"], "Corriendo los tests")

    exe = publicar()

    destino = Path(args.destino)
    if not destino.is_absolute():
        destino = RAIZ / destino

    entregar(exe, destino, args.zip)

    print("\nListo.")


if __name__ == "__main__":
    main()
