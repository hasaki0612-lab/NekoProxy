"""CI-only: fetch pinned test cores from their official release assets.
Requires GitHub's asset SHA-256 digest; refuses unverified downloads. No VPS use.
"""
from pathlib import Path
import argparse
import gzip
import hashlib
import json
import os
import platform
import tarfile
import urllib.request
import zipfile

p=argparse.ArgumentParser();p.add_argument("--output",type=Path,required=True);args=p.parse_args()
root=args.output.resolve();root.mkdir(parents=True,exist_ok=True)
system={"Windows":"windows","Linux":"linux","Darwin":"darwin"}[platform.system()]
arch="arm64" if platform.machine().lower() in ("arm64","aarch64") else "amd64"
records=[]
def download(repo,tag,name):
    request=urllib.request.Request(f"https://api.github.com/repos/{repo}/releases/tags/{tag}",headers={"User-Agent":"NekoProxy-CI","Accept":"application/vnd.github+json"})
    with urllib.request.urlopen(request,timeout=45) as r:release=json.load(r)
    matches=[a for a in release["assets"] if a["name"]==name]
    if len(matches)!=1:raise RuntimeError("Pinned release asset not found: "+name)
    asset=matches[0];digest=asset.get("digest","")
    if not digest.startswith("sha256:") or len(digest)!=71:raise RuntimeError("Missing trusted release SHA-256: "+name)
    url=asset["browser_download_url"]
    if not url.startswith(f"https://github.com/{repo}/releases/download/{tag}/"):raise RuntimeError("Unexpected release URL")
    path=root/name
    with urllib.request.urlopen(url,timeout=120) as r, path.open("xb") as f:
        while chunk:=r.read(1024*1024):f.write(chunk)
    actual=hashlib.sha256(path.read_bytes()).hexdigest()
    if actual!=digest[7:]:raise RuntimeError("Release checksum mismatch")
    records.append({"repo":repo,"tag":tag,"asset":name,"sha256":actual,"digest_source":"GitHub official release asset metadata"})
    return path
sb_system="darwin" if system=="darwin" else system
sb_name=f"sing-box-1.14.3-{sb_system}-{arch}"
archive=download("SagerNet/sing-box","v1.14.3",sb_name+(".zip" if system=="windows" else ".tar.gz"))
exe="sing-box.exe" if system=="windows" else "sing-box"
sb_out=root/("sing-box/sing-box-1.14.3-windows-amd64/sing-box.exe" if system=="windows" else "sing-box/sing-box")
sb_out.parent.mkdir(parents=True,exist_ok=True)
if system=="windows":
    with zipfile.ZipFile(archive) as z:sb_out.write_bytes(z.read(sb_name+"/"+exe))
else:
    with tarfile.open(archive) as t:
        stream=t.extractfile(sb_name+"/"+exe);assert stream is not None;sb_out.write_bytes(stream.read())
mh_arch="amd64-compatible" if system=="linux" and arch=="amd64" else arch
mh_name=f"mihomo-{system}-{mh_arch}-v1.19.32"+(".zip" if system=="windows" else ".gz")
archive=download("MetaCubeX/mihomo","v1.19.32",mh_name)
mh_out=root/("mihomo/mihomo-windows-amd64.exe" if system=="windows" else "mihomo/mihomo")
mh_out.parent.mkdir(parents=True,exist_ok=True)
if system=="windows":
    with zipfile.ZipFile(archive) as z:
        names=[n for n in z.namelist() if n.endswith(".exe")];assert len(names)==1;mh_out.write_bytes(z.read(names[0]))
else:
    with gzip.open(archive,"rb") as f:mh_out.write_bytes(f.read())
sb_out.chmod(0o755);mh_out.chmod(0o755)
(root/"core-provenance.json").write_text(json.dumps(records,indent=2)+"\n",encoding="utf-8")
print(json.dumps(records,indent=2))
