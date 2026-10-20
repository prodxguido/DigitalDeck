#!/usr/bin/env python3
"""
Copia el Remote Script de DigitalDeck a la carpeta de Ableton Live.

    python src/DigitalDeck.RemoteScript/install.py
    python src/DigitalDeck.RemoteScript/install.py --user-library "/otra/ruta/User Library"
    python src/DigitalDeck.RemoteScript/install.py --live-abierto

Live sólo lee los Remote Scripts al arrancar, asi que despues de correr esto hay que reiniciar
Live y elegir "DigitalDeck" como Control Surface en Preferencias > Link, Tempo & MIDI.

Existe por dos motivos. Uno es la portabilidad: reemplaza al script de PowerShell que habia antes,
que sólo corria en Windows, y el propio Remote Script ya es Python obligatoriamente, asi que
unificar resta un lenguaje en vez de sumarlo. El otro es que la User Library no está en el mismo
lugar en los dos escritorios -- Documents en Windows, Music en macOS -- y adivinarlo mal no rompe nada de forma
visible: el script termina donde Live no mira y el puente simplemente nunca levanta.

La forma normal de instalarlo sigue siendo desde la app, que lo lleva embebido. Esto es para
trabajar sobre el repo, o para una máquina donde la app todavia no corre.
"""

import argparse
import os
import platform
import shutil
import subprocess
import sys
from pathlib import Path

AQUI = Path(__file__).resolve().parent
ORIGEN = AQUI / "DigitalDeck"

#: Como se llama la carpeta que Live muestra en el desplegable de Control Surface.
NOMBRE = "DigitalDeck"


def user_libraries():
    """Las User Library que vale la pena probar, la mejor primero."""
    home = Path.home()

    if platform.system() == "Darwin":
        # En macOS la User Library cuelga de Music. Un Documents/Ableton en una Mac es algo que
        # alguien hizo a mano, y no es lo que Live lee.
        return [home / "Music" / "Ableton" / "User Library"]

    candidatas = [home / "Documents" / "Ableton" / "User Library"]

    # Windows en español, y el Documents que OneDrive se lleva a otro lado.
    for extra in ("Documentos", "OneDrive/Documents", "OneDrive/Documentos"):
        candidatas.append(home / Path(extra) / "Ableton" / "User Library")

    existentes = [ruta for ruta in candidatas if ruta.is_dir()]

    return existentes or candidatas[:1]


def live_esta_abierto():
    """
    True cuando hay un proceso de Ableton Live corriendo.

    Consultado en vez de asumido porque copiar encima de una instalación en uso deja a Live con el
    código viejo en memoria y el nuevo en disco, que es la peor combinación para diagnosticar.
    """
    try:
        if platform.system() == "Windows":
            salida = subprocess.run(
                ["tasklist"], capture_output=True, text=True, timeout=20).stdout
        else:
            salida = subprocess.run(
                ["ps", "-A"], capture_output=True, text=True, timeout=20).stdout
    except Exception:  # noqa: BLE001
        # No poder mirar no es razon para bloquear la instalación; el aviso ya está en la ayuda.
        return False

    return "Ableton Live" in salida


def instalar(destino_libreria, permitir_live_abierto):
    if not ORIGEN.is_dir():
        sys.exit("No encuentro el script en %s." % ORIGEN)

    if live_esta_abierto() and not permitir_live_abierto:
        sys.exit(
            "Ableton Live está abierto. Cerralo antes de instalar, o pasá --live-abierto si lo vas "
            "a reiniciar enseguida.")

    remote_scripts = destino_libreria / "Remote Scripts"
    destino = remote_scripts / NOMBRE

    if not remote_scripts.is_dir():
        print("Creando %s" % remote_scripts)
        remote_scripts.mkdir(parents=True, exist_ok=True)

    if destino.exists():
        print("Reemplazando la instalación anterior en %s" % destino)
        shutil.rmtree(destino)

    # __pycache__ no se copia: son los .pyc de esta máquina y de esta versión de Python, y adentro
    # de Live los escribe Live con su propio intérprete.
    shutil.copytree(ORIGEN, destino, ignore=shutil.ignore_patterns("__pycache__"))

    archivos = sorted(destino.rglob("*.py"))

    print()
    print("Instalado: %s (%d archivos .py)" % (destino, len(archivos)))
    print()
    print("Falta:")
    print("  1. Abrí Ableton Live.")
    print('  2. Preferencias > Link, Tempo & MIDI > Control Surface: elegí "DigitalDeck".')
    print("  3. El indicador de DigitalDeck tiene que ponerse en verde.")
    print()
    print("Si no aparece en la lista, mirá el Log.txt de Live:")

    if platform.system() == "Darwin":
        print("  ~/Library/Preferences/Ableton/Live <version>/Log.txt")
    else:
        print("  %s\\Ableton\\Live <version>\\Preferences\\Log.txt" % os.environ.get("APPDATA", "%APPDATA%"))

    return 0


def main():
    parser = argparse.ArgumentParser(
        description="Instala el Remote Script de DigitalDeck en Ableton Live.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__)

    parser.add_argument(
        "--user-library",
        help="Ruta a la User Library de Ableton, por si no está en el lugar de siempre.")
    parser.add_argument(
        "--live-abierto", action="store_true",
        help="Instala aunque Live esté abierto. Usalo sólo si lo vas a reiniciar enseguida.")

    args = parser.parse_args()

    if args.user_library:
        destino = Path(args.user_library).expanduser()
    else:
        destino = user_libraries()[0]
        print("User Library: %s" % destino)

    return instalar(destino, args.live_abierto)


if __name__ == "__main__":
    sys.exit(main())
