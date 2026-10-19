"""Build/package existing Avalonia code. No installation or network changes.
dotnet may restore official NuGet build/runtime dependencies. No VPS access.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import tarfile
import zipfile
import plistlib

repo = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument("--rid", required=True, choices=["win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"])
p.add_argument("--output", type=Path, default=repo / "artifacts/packages")
p.add_argument("--dotnet", default="dotnet")
p.add_argument("--framework-dependent", action="store_true")
p.add_argument("--publish-dir", type=Path, help="Package an already-published folder; no dotnet command")
args = p.parse_args()
out = args.output.resolve()
out.mkdir(parents=True, exist_ok=True)
publish = args.publish_dir.resolve() if args.publish_dir else out / ("publish-" + args.rid)
if not args.publish_dir:
    if publish.exists():
        raise SystemExit("Use a fresh output directory; existing publications are preserved.")
    subprocess.run([args.dotnet, "publish", str(repo / "src/NekoProxy.Desktop"), "-c", "Release", "-r", args.rid,
                    "--self-contained", str(not args.framework_dependent).lower(), "-p:PublishSingleFile=false",
                    "-p:DebugType=None", "-p:UsedAvaloniaProducts=", "-o", str(publish)], cwd=repo, check=True)
appname = "NekoProxy.Desktop.exe" if args.rid.startswith("win") else "NekoProxy.Desktop"
if not (publish / appname).is_file():
    raise SystemExit("Missing native apphost: " + appname)
stage = out / ("NekoProxy-0.6.0-" + args.rid)
if stage.exists():
    raise SystemExit("Existing package staging directory preserved: " + str(stage))
if args.rid.startswith("osx"):
    root = stage / "NekoProxy.app/Contents"
    shutil.copytree(publish, root / "MacOS")
    (root / "Resources").mkdir()
    shutil.copyfile(repo / "packaging/macos/Info.plist", root / "Info.plist")
    shutil.copyfile(repo / "packaging/icons/NekoProxy.icns", root / "Resources/NekoProxy.icns")
    with (root / "Info.plist").open("rb") as stream:
        assert plistlib.load(stream)["CFBundleExecutable"] == appname
    payload = root / "MacOS"
else:
    shutil.copytree(publish, stage)
    payload = stage
for name in ["LICENSE", "THIRD_PARTY_NOTICES.md"]:
    shutil.copyfile(repo / name, payload / name)
if args.rid.startswith("linux"):
    shutil.copyfile(repo / "packaging/icons/NekoProxy.png", stage / "NekoProxy.png")
    shutil.copyfile(repo / "packaging/linux/org.nekoproxy.desktop.desktop", stage / "org.nekoproxy.desktop.desktop")
    shutil.copyfile(repo / "packaging/linux/install-user.sh", stage / "install-user.sh")
    (stage / "install-user.sh").chmod(0o755)
if not args.rid.startswith("win"):
    (payload / appname).chmod(0o755)
manifest = {"version": "0.6.0", "rid": args.rid, "self_contained": not args.framework_dependent,
            "native_gui_verified": False, "signed": False,
            "brand_source_sha256": json.loads((repo / "packaging/icons/provenance.json").read_text("utf-8"))["source_sha256"],
            "files": [{"path": str(f.relative_to(stage)).replace("\\", "/"), "sha256": hashlib.sha256(f.read_bytes()).hexdigest()}
                      for f in sorted(stage.rglob("*")) if f.is_file()]}
(stage / "package-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
suffix = ".zip" if args.rid.startswith("win") else ".tar.gz"
archive = out / (stage.name + suffix)
if archive.exists():
    raise SystemExit("Existing archive preserved: " + str(archive))
if suffix == ".zip":
    with zipfile.ZipFile(archive, "x", zipfile.ZIP_DEFLATED) as z:
        for f in stage.rglob("*"):
            if f.is_file(): z.write(f, f.relative_to(stage))
    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None
else:
    def modes(info):
        # Cross-packaging on Windows must explicitly preserve POSIX executable bits.
        info.mode = 0o755 if info.isdir() or info.name.endswith(("/NekoProxy.Desktop", "/install-user.sh")) else 0o644
        info.uid = info.gid = 0; info.uname = info.gname = ""
        return info
    with tarfile.open(archive, "w:gz") as t:
        t.add(stage, arcname=stage.name, filter=modes)
    with tarfile.open(archive) as t:
        for entry in t:
            if entry.isfile():
                f = t.extractfile(entry); assert f is not None
                assert hashlib.sha256(f.read()).hexdigest() == hashlib.sha256((out / entry.name).read_bytes()).hexdigest()
receipt = {"archive": archive.name, "bytes": archive.stat().st_size, "sha256": hashlib.sha256(archive.read_bytes()).hexdigest(),
           "rid": args.rid, "self_contained": not args.framework_dependent, "native_gui_verified": False}
archive.with_name(archive.name + ".sha256").write_text(receipt["sha256"] + "  " + archive.name + "\n", encoding="ascii")
print(json.dumps(receipt, indent=2))
