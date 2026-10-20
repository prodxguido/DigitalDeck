#!/usr/bin/env python3
"""
Arma el DigitalDeck.app para macOS, desde cualquier maquina.

    python herramientas/empaquetar-mac.py                 # deja dist/mac/DigitalDeck.app
    python herramientas/empaquetar-mac.py --arch x64      # para una Mac Intel
    python herramientas/empaquetar-mac.py --zip           # ademas arma el .zip

.NET compila de forma cruzada: el binario para macOS sale igual desde Windows, y armar el bundle
es copiar archivos y escribir un Info.plist. Lo que **no** se puede hacer desde acá es firmarlo ni
notarizarlo, y sin eso Gatekeeper lo va a frenar la primera vez. Como abrirlo igual esta al final
de este script.

Empaqueta el shell de Avalonia, que es el que corre en Mac. La app de WPF sigue siendo la de
Windows y se arma con empaquetar.py.
"""

import argparse
import plistlib
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
PROYECTO = RAIZ / "src" / "DigitalDeck.Avalonia" / "DigitalDeck.Avalonia.csproj"

#: Como se llama el ejecutable adentro del bundle. Tiene que coincidir con CFBundleExecutable.
EJECUTABLE = "DigitalDeckAvalonia"

#: Reverse-DNS, el mismo que usa el LaunchAgent para abrir al iniciar sesion.
BUNDLE_ID = "com.digitaldeck.deck"


def correr(comando, descripcion):
    print(f"\n>> {descripcion}", flush=True)
    print(f"   {' '.join(str(parte) for parte in comando)}\n", flush=True)

    if subprocess.run(comando, cwd=RAIZ).returncode != 0:
        sys.exit(f"\nFallo: {descripcion}")


def publicar(arch):
    runtime = f"osx-{arch}"

    salida = RAIZ / "src" / "DigitalDeck.Avalonia" / "bin" / "Release" / "net8.0" / runtime / "publish"

    if salida.exists():
        shutil.rmtree(salida)

    correr(
        ["dotnet", "publish", str(PROYECTO),
         "-c", "Release", "-r", runtime, "--self-contained", "true",
         "-p:DebugType=None", "-p:DebugSymbols=false"],
        f"Publicando para {runtime}")

    if not (salida / EJECUTABLE).is_file():
        sys.exit(f"El publish termino bien pero no encontre {salida / EJECUTABLE}.")

    return salida


def info_plist(arch):
    """
    Lo minimo que macOS necesita para tratar a esto como una aplicacion.

    LSMinimumSystemVersion 11.0 porque es el piso de .NET 8 en macOS. NSHighResolutionCapable
    porque sin eso el sistema lo escala como una app vieja y todo se ve borroso en un Retina.
    """
    return {
        "CFBundleName": "DigitalDeck",
        "CFBundleDisplayName": "DigitalDeck",
        "CFBundleIdentifier": BUNDLE_ID,
        "CFBundleVersion": "1.0.0",
        "CFBundleShortVersionString": "1.0",
        "CFBundlePackageType": "APPL",
        "CFBundleExecutable": EJECUTABLE,
        "CFBundleInfoDictionaryVersion": "6.0",
        "LSMinimumSystemVersion": "11.0",
        "NSHighResolutionCapable": True,
        "LSApplicationCategoryType": "public.app-category.music",
    }


def armar_bundle(publicado, destino, arch):
    app = destino / "DigitalDeck.app"

    if app.exists():
        shutil.rmtree(app)

    contents = app / "Contents"
    macos = contents / "MacOS"

    macos.mkdir(parents=True)
    (contents / "Resources").mkdir()

    for archivo in publicado.iterdir():
        if archivo.is_file():
            shutil.copy2(archivo, macos / archivo.name)
        else:
            shutil.copytree(archivo, macos / archivo.name)

    with open(contents / "Info.plist", "wb") as f:
        plistlib.dump(info_plist(arch), f)

    return app


def tamano(ruta):
    total = sum(f.stat().st_size for f in ruta.rglob("*") if f.is_file())
    return total / (1024 * 1024)


def main():
    parser = argparse.ArgumentParser(
        description="Arma el DigitalDeck.app de macOS.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__)

    parser.add_argument("--arch", default="arm64", choices=["arm64", "x64"],
                        help="arm64 para Apple Silicon (por defecto), x64 para Mac Intel.")
    parser.add_argument("--destino", default="dist/mac",
                        help="Carpeta donde dejar el .app. Relativa a la raiz del repo.")
    parser.add_argument("--zip", action="store_true", help="Ademas del .app, arma un .zip al lado.")

    args = parser.parse_args()

    if shutil.which("dotnet") is None:
        sys.exit("No encontre 'dotnet' en el PATH. Instala el SDK de .NET 8 y volve a probar.")

    publicado = publicar(args.arch)

    destino = Path(args.destino)
    if not destino.is_absolute():
        destino = RAIZ / destino

    destino.mkdir(parents=True, exist_ok=True)

    app = armar_bundle(publicado, destino, args.arch)

    print(f"\n   {app}  ({tamano(app):.1f} MB, {args.arch})")

    if args.zip:
        # Un .zip y no un .tar: es lo que macOS abre con doble clic. Ojo que zipfile no guarda el
        # bit de ejecutable, asi que del otro lado hace falta el chmod que esta abajo.
        zip_path = destino / "DigitalDeck-mac.zip"

        print("\n>> Comprimiendo", flush=True)

        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
            for archivo in app.rglob("*"):
                if archivo.is_file():
                    z.write(archivo, archivo.relative_to(destino))

        print(f"   {zip_path}  ({zip_path.stat().st_size / (1024 * 1024):.1f} MB)")

    print("""
Del lado de la Mac, antes del primer arranque:

  chmod +x DigitalDeck.app/Contents/MacOS/DigitalDeckAvalonia
  xattr -dr com.apple.quarantine DigitalDeck.app

El chmod porque el bit de ejecutable no sobrevive el zip. El xattr porque esto no esta firmado ni
notarizado, y sin eso Gatekeeper dice que la app esta danada, que es su forma de decir que no sabe
quien la hizo.""")

    return 0


if __name__ == "__main__":
    sys.exit(main())
