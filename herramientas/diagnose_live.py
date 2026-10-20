import importlib.util
import json
import socket
import time
from pathlib import Path

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("osc", root / "src/DigitalDeck.RemoteScript/DigitalDeck/osc.py")
osc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(osc)
s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
s.bind(("127.0.0.1", 11011))
s.settimeout(0.3)

def query(address, args):
    s.sendto(osc.encode(address, args), ("127.0.0.1", 11010))
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        try:
            addr, values = osc.decode(s.recv(65535))
            if addr == address or addr == "/digitaldeck/error" or (address.endswith("/ping") and addr.endswith("/pong")):
                return [addr, values]
        except socket.timeout:
            pass
    return ["timeout", []]

result = {"ping": query("/digitaldeck/ping", [])}
for name in ["Pro-MB", "Pro-Q", "Pro-DS", "soothe", "Z-Noise", "NS1", "VintageVerb", "CL 1B", "CLA-2A", "Vox", "ZENOLOGY", "Purity", "Hive", "Kick", "Splice", "DUNE", "Electra", "Pigments", "Analog Lab"]:
    result[name] = query("/digitaldeck/browser/search", ["plugins", name, 30])
result["Delay"] = query("/digitaldeck/browser/search", ["audio_effects", "Delay", 30])
(root / "live-browser-diagnostic.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(result, ensure_ascii=True))
