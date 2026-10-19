"""Start the actual desktop app with its existing isolated startup-check option.
The attached-visual-tree PNG is a startup smoke artifact, NOT a desktop recording
or proof of a human GUI workflow. Never uses live server profiles.
"""
from pathlib import Path
import argparse
import json
import subprocess
import tempfile
from datetime import datetime, timezone

p=argparse.ArgumentParser();p.add_argument("--exe",type=Path,required=True);p.add_argument("--output",type=Path,required=True);args=p.parse_args()
out=args.output.resolve();out.mkdir(parents=True,exist_ok=False)
data=Path(tempfile.mkdtemp(prefix="NekoProxy-startup-smoke-"))
with (out/"stdout.log").open("wb") as log, (out/"stderr.log").open("wb") as error:
    process=subprocess.Popen([str(args.exe.resolve()),"--data-dir",str(data),"--onboarding-restart-check",str(out),"--exit-after-capture"],stdout=log,stderr=error,cwd=out)
    try:code=process.wait(timeout=60)
    except subprocess.TimeoutExpired:
        process.kill();process.wait();raise
if code!=0 or not (out/"restart.json").is_file() or not (out/"restart-main.png").is_file():
    raise SystemExit("Native startup-check did not finish; preserve raw logs, do not mark GUI passed.")
(out/"smoke-result.json").write_text(json.dumps({"utc":datetime.now(timezone.utc).isoformat(),"exit":code,
    "native_window_opened_callback_completed":True,"human_gui_acceptance":False,
    "capture":"existing RenderTargetBitmap attached visual tree, not OS desktop capture","vps_access":False},indent=2)+"\n",encoding="utf-8")
