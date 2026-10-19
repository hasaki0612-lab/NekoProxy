"""Read-only candidate verification shared by installer builders; no extraction."""
from pathlib import Path, PurePosixPath
import hashlib
import json
import struct
import tarfile

RETIRED_LOGO = "b4501cd6ba21433e111d38db69721bbeb279efe7b0cf6d625b59a1c0fc7a032f"

def sha256(data):
    return hashlib.sha256(data).hexdigest()

def linux_candidate(archive, rid, logo_sha256):
    if logo_sha256.lower() == RETIRED_LOGO:
        raise ValueError("Retired three-ring logo rejected; supply the formal logo first.")
    prefix = "NekoProxy-0.6.0-" + rid + "/"
    files = {}
    with tarfile.open(archive, "r:gz") as tar:
        for entry in tar:
            path = PurePosixPath(entry.name)
            if path.is_absolute() or ".." in path.parts:
                raise ValueError("Unsafe candidate path")
            if entry.isdir():
                continue
            if not entry.isfile() or not entry.name.startswith(prefix):
                raise ValueError("Unexpected candidate entry")
            relative = entry.name[len(prefix):]
            if relative in files:
                raise ValueError("Duplicate candidate path")
            with tar.extractfile(entry) as stream:
                files[relative] = stream.read()
    manifest = json.loads(files["package-manifest.json"])
    if manifest["rid"] != rid or not manifest["self_contained"]:
        raise ValueError("Expected self-contained candidate for " + rid)
    if manifest.get("brand_source_sha256", "").lower() != logo_sha256.lower():
        raise ValueError("Candidate is not built with the specified formal logo")
    if len(manifest["files"]) != len(files)-1:
        raise ValueError("Candidate manifest file count mismatch")
    for item in manifest["files"]:
        if sha256(files[item["path"]]) != item["sha256"]:
            raise ValueError("Candidate manifest checksum mismatch")
    for name in files:
        p = PurePosixPath(name)
        if p.name.lower() in ("servers.json", "credentials.json", "settings.json", "connection.json") or p.suffix.lower() in (".pem", ".key", ".nekobak"):
            raise ValueError("User/credential material must not be packaged")
    executable = files["NekoProxy.Desktop"]
    if executable[:6] != b"\x7fELF\x02\x01" or struct.unpack_from("<H", executable, 18)[0] != (62 if rid=="linux-x64" else 183):
        raise ValueError("ELF apphost architecture mismatch")
    return files, manifest
