"""Format conversion only; preserves the supplied canvas, artwork, color and alpha.
Requires Pillow. Source PNG is copied byte-for-byte from the user-provided file.
"""
from pathlib import Path
import argparse
import hashlib
import json
from PIL import Image

repo = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--source", type=Path, required=True, help="Explicit user-authorized transparent PNG")
parser.add_argument("--expected-sha256", required=True, help="SHA-256 verified against the supplied original")
args = parser.parse_args()
source = args.source.read_bytes()
digest = hashlib.sha256(source).hexdigest()
if digest != args.expected_sha256.lower():
    raise SystemExit("Source SHA-256 mismatch; no resources were changed.")
if digest == "b4501cd6ba21433e111d38db69721bbeb279efe7b0cf6d625b59a1c0fc7a032f":
    raise SystemExit("The retired three-ring icon is not the authorized formal logo.")
image = Image.open(args.source)
if image.format != "PNG":
    raise SystemExit("The authorized original must be a PNG; no resources were changed.")
image = image.convert("RGBA")
if image.getchannel("A").getextrema()[0] == 255:
    raise SystemExit("The PNG has no transparent pixels; confirm the correct original first.")

def canvas(size):
    # Fit the entire original canvas, without stretching, cropping or a backplate.
    scale = min(size / image.width, size / image.height)
    width, height = max(1, round(image.width * scale)), max(1, round(image.height * scale))
    result = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    result.paste(image.resize((width, height), Image.Resampling.LANCZOS), ((size-width)//2, (size-height)//2))
    return result

icons = repo / "packaging/icons"
icons.mkdir(parents=True, exist_ok=True)
if args.source.resolve() != (icons / "source.png").resolve():
    (icons / "source.png").write_bytes(source)
canvas(512).save(icons / "NekoProxy.png")
canvas(1024).save(icons / "NekoProxy.icns")
desktop = repo / "src/NekoProxy.Desktop/Assets"
desktop.mkdir(exist_ok=True)
canvas(256).save(desktop / "NekoProxy.ico", format="ICO", sizes=[(n, n) for n in (16, 20, 24, 32, 48, 64, 128, 256)])
(icons / "provenance.json").write_text(json.dumps({
    "source_sha256": digest, "source_size": image.size,
    "transform": "uniform resize and format conversion only; no crop, repaint or background removal",
    "outputs": {str(p.relative_to(repo)): hashlib.sha256(p.read_bytes()).hexdigest() for p in
                [icons / "NekoProxy.png", icons / "NekoProxy.icns", desktop / "NekoProxy.ico"]}
}, indent=2) + "\n", encoding="utf-8")
