import importlib.util
import json
import socket
import time
from pathlib import Path

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("osc", root / "src/DigitalDeck.RemoteScript/DigitalDeck/osc.py")
osc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(osc)
rows = json.loads((root / "live-actions-validation.json").read_text(encoding="utf-8"))
action = json.loads(next(data for label, data in rows if label == "Pro-MB"))
with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
    s.bind(("127.0.0.1", 11011))
    s.settimeout(0.5)
    s.sendto(osc.encode("/digitaldeck/browser/load",
                       [action["uri"], action["category"], action["name"]]), ("127.0.0.1", 11010))
    until = time.monotonic() + 15
    while time.monotonic() < until:
        try:
            address, args = osc.decode(s.recv(65535))
            if address == "/digitaldeck/error":
                raise RuntimeError(args)
            if address == "/digitaldeck/browser/loaded":
                assert args[0] == action["uri"], args
                print("LIVE CONFIRMED:", args)
                break
        except socket.timeout:
            pass
    else:
        raise RuntimeError("Live did not confirm the load")
