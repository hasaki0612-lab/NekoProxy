"""Create a standard Debian 2.0 ar/control.tar.xz/data.tar.xz candidate.
Cross-packaging is not a claim of dpkg installation or native GUI acceptance.
No maintainer hooks and no user-profile files are included.
"""
from pathlib import Path
import argparse
import hashlib
import io
import json
import lzma
import tarfile
from installer_common import linux_candidate, sha256

repo = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument("--archive", type=Path, required=True)
p.add_argument("--arch", choices=["amd64", "arm64"], required=True)
p.add_argument("--logo-sha256", required=True)
p.add_argument("--output", type=Path, required=True)
args = p.parse_args()
rid = "linux-x64" if args.arch == "amd64" else "linux-arm64"
files, source_manifest = linux_candidate(args.archive, rid, args.logo_sha256)
output = args.output.resolve()
if output.exists():
    raise SystemExit("Use a new output directory; existing files are preserved.")

payload = {}
excluded = {"install-user.sh", "org.nekoproxy.desktop.desktop", "NekoProxy.png", "package-manifest.json"}
for name, data in files.items():
    if name not in excluded:
        payload["opt/nekoproxy/" + name] = data
payload["usr/share/applications/org.nekoproxy.desktop.desktop"] = (repo/"packaging/linux/nekoproxy-system.desktop").read_bytes()
payload["usr/share/icons/hicolor/512x512/apps/org.nekoproxy.desktop.png"] = files["NekoProxy.png"]
payload["usr/share/doc/nekoproxy/copyright"] = files["LICENSE"] + b"\n\n" + files["THIRD_PARTY_NOTICES.md"]
payload["usr/share/doc/nekoproxy/upstream-candidate-manifest.json"] = files["package-manifest.json"]

def tar_bytes(entries, symlink=False):
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w", format=tarfile.USTAR_FORMAT) as tar:
        directories = {str(parent) for name in entries for parent in Path(name).parents if str(parent) != "."}
        if symlink:
            directories.update(["usr", "usr/bin"])
        for directory in sorted(directories):
            info=tarfile.TarInfo("./"+directory.replace("\\", "/"));info.type=tarfile.DIRTYPE;info.mode=0o755;tar.addfile(info)
        for name, data in sorted(entries.items()):
            info=tarfile.TarInfo("./"+name);info.size=len(data)
            info.mode=0o755 if name=="opt/nekoproxy/NekoProxy.Desktop" else 0o644
            tar.addfile(info,io.BytesIO(data))
        if symlink:
            info=tarfile.TarInfo("./usr/bin/nekoproxy");info.type=tarfile.SYMTYPE;info.linkname="/opt/nekoproxy/NekoProxy.Desktop";info.mode=0o777;tar.addfile(info)
    return lzma.compress(buffer.getvalue(), preset=6)

control = (repo/"packaging/debian/control.in").read_text("utf-8")
for key,value in {"VERSION":"0.6.0","ARCH":args.arch,"INSTALLED_SIZE":str((sum(map(len,payload.values()))+1023)//1024)}.items():
    control=control.replace("@"+key+"@",value)
md5="".join(hashlib.md5(data).hexdigest()+"  "+name+"\n" for name,data in sorted(payload.items()))
members = {"debian-binary":b"2.0\n", "control.tar.xz":tar_bytes({"control":control.encode(),"md5sums":md5.encode()}), "data.tar.xz":tar_bytes(payload,True)}
output.mkdir(parents=True)
deb=output/("NekoProxy-"+args.arch+".deb")
with deb.open("xb") as stream:
    stream.write(b"!<arch>\n")
    for name,data in members.items():
        header=f"{name+'/':<16}{0:<12}{0:<6}{0:<6}{'100644':<8}{len(data):<10}`\n".encode("ascii")
        assert len(header)==60
        stream.write(header);stream.write(data)
        if len(data)%2:stream.write(b"\n")
receipt={"file":deb.name,"bytes":deb.stat().st_size,"sha256":sha256(deb.read_bytes()),"architecture":args.arch,
    "source_archive_sha256":sha256(args.archive.read_bytes()),"brand_source_sha256":args.logo_sha256,
    "built":True,"native_dpkg_installation":False,"native_gui":False,"uninstall_data_retention_tested":False,
    "maintainer":next(line.removeprefix("Maintainer:").strip() for line in control.splitlines() if line.startswith("Maintainer:")),
    "payload":[{"path":name,"sha256":sha256(data)} for name,data in sorted(payload.items())]}
deb.with_name(deb.name+".sha256").write_text(receipt["sha256"]+"  "+deb.name+"\n",encoding="ascii")
(output/"deb-manifest.json").write_text(json.dumps(receipt,indent=2)+"\n",encoding="utf-8")
print(json.dumps({k:v for k,v in receipt.items() if k!="payload"},indent=2))
