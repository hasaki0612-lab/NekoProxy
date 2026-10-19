"""Prepare an exact-checksummed local PKGBUILD input. Does not impersonate makepkg."""
from pathlib import Path
import argparse,json,shutil
from installer_common import linux_candidate,sha256

repo=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser()
p.add_argument("--archive",type=Path,required=True)
p.add_argument("--logo-sha256",required=True)
p.add_argument("--output",type=Path,required=True)
args=p.parse_args()
files,manifest=linux_candidate(args.archive,"linux-x64",args.logo_sha256)
output=args.output.resolve()
if output.exists():raise SystemExit("Existing Arch inputs preserved; choose a new output directory.")
output.mkdir(parents=True)
archive=output/"NekoProxy-0.6.0-linux-x64.tar.gz"
shutil.copyfile(args.archive,archive)
desktop=(repo/"packaging/linux/nekoproxy-system.desktop").read_bytes()
(output/"nekoproxy.desktop").write_bytes(desktop)
pkgbuild=(repo/"packaging/arch/PKGBUILD.in").read_text("utf-8")
for key,value in {"ARCHIVE":archive.name,"ARCHIVE_SHA256":sha256(archive.read_bytes()),"DESKTOP_SHA256":sha256(desktop)}.items():
    pkgbuild=pkgbuild.replace("@"+key+"@",value)
(output/"PKGBUILD").write_text(pkgbuild,encoding="utf-8",newline="\n")
shutil.copyfile(repo/"packaging/arch/build-native.sh",output/"build-native.sh")
(output/"build-native.sh").chmod(0o755)
(output/"preparation.json").write_text(json.dumps({"brand_source_sha256":args.logo_sha256,"archive_sha256":sha256(archive.read_bytes()),
    "makepkg_executed":False,"package_built":False,"installation_tested":False,"native_gui_tested":False},indent=2)+"\n",encoding="utf-8")
print("Prepared local PKGBUILD and exact SHA-256 inputs. Run bash build-native.sh on Arch x86_64; no package was built here.")
