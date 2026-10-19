from pathlib import Path
import hashlib,json,os,subprocess,sys
app=Path(sys.argv[1]).resolve();out=Path(sys.argv[2]).resolve()
assert sys.platform=='darwin' and app.name=='NekoProxy.app'
def snapshot():return {p.relative_to(app).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in app.rglob('*') if p.is_file()}
before=snapshot()
with (out/'codesign-before-sealing.log').open('wb') as f:
    pre=subprocess.run(['codesign','--verify','--deep','--strict','--verbose=2',str(app)],stdout=f,stderr=subprocess.STDOUT).returncode
macos=app/'Contents/MacOS'
frameworks=app/'Contents/Frameworks';payload=app/'Contents/Resources/Payload'
assert not frameworks.exists() and not payload.exists()
frameworks.mkdir();payload.mkdir()
def macho(p):
    if not p.is_file():return False
    with p.open('rb') as f:return f.read(4) in {b'\xfe\xed\xfa\xce',b'\xce\xfa\xed\xfe',b'\xfe\xed\xfa\xcf',b'\xcf\xfa\xed\xfe',b'\xca\xfe\xba\xbe',b'\xbe\xba\xfe\xca',b'\xca\xfe\xba\xbf',b'\xbf\xba\xfe\xca'}
original_native={p for p in before if macho(app/p)}
relocations=[]
for p in sorted(macos.iterdir()):
    if p.name=='NekoProxy.Desktop':continue
    destination=(frameworks if macho(p) else payload)/p.name
    old=p.relative_to(app).as_posix();p.rename(destination)
    target=os.path.relpath(destination,p.parent)
    p.symlink_to(target,target_is_directory=destination.is_dir())
    relocations.append({'old':old,'actual':destination.relative_to(app).as_posix(),'relative_link':target})
# .NET resolves the managed entry/runtimeconfig symlinks to Payload, making it
# the self-contained application's base directory. Native host/runtime lookups
# must also resolve there, while the actual Mach-O files stay in Frameworks.
for p in sorted(frameworks.iterdir()):
    (payload/p.name).symlink_to(os.path.relpath(p,payload),target_is_directory=p.is_dir())
# Sign only native leaf code when necessary. Do not sign PE DLLs, add runtime
# exceptions or use --deep for signing. The outer bundle is sealed last.
with (out/'codesign-native-leaves.log').open('wb') as f:
    for p in sorted(frameworks.rglob('*')):
        if not macho(p):continue
        check=subprocess.run(['codesign','--verify','--strict','--all-architectures',str(p)],stdout=f,stderr=subprocess.STDOUT)
        if check.returncode:
            subprocess.run(['codesign','--force','--sign','-','--timestamp=none',str(p)],stdout=f,stderr=subprocess.STDOUT,check=True)
with (out/'codesign-sealing.log').open('wb') as f:
    subprocess.run(['codesign','--force','--sign','-','--timestamp=none','--preserve-metadata=entitlements',str(app)],stdout=f,stderr=subprocess.STDOUT,check=True)
after=snapshot()
assert all((app/p).is_file() for p in before), 'A previous runtime lookup path is unavailable'
changed=[p for p in before if before[p]!=hashlib.sha256((app/p).read_bytes()).hexdigest()]
added=sorted(set(after)-set(before))
assert set(changed)<=original_native|{'Contents/_CodeSignature/CodeResources'},changed
assert all(p.startswith(('Contents/Frameworks/','Contents/Resources/Payload/')) or p=='Contents/_CodeSignature/CodeResources' for p in added),added
with (out/'codesign-after-sealing.log').open('wb') as f:
    subprocess.run(['codesign','--verify','--deep','--strict','--verbose=2',str(app)],stdout=f,stderr=subprocess.STDOUT,check=True)
with (out/'codesign-details.log').open('wb') as f:
    subprocess.run(['codesign','--display','--verbose=4',str(app)],stdout=f,stderr=subprocess.STDOUT,check=True)
with (out/'gatekeeper-assessment.log').open('wb') as f:
    gatekeeper=subprocess.run(['spctl','--assess','--type','execute','--verbose=4',str(app)],stdout=f,stderr=subprocess.STDOUT).returncode
report={'original_signature_verification_exit':pre,'bundle_ad_hoc_signature_valid':True,'developer_id_signature':False,
    'notarized':False,'signing_identity':'- (ad-hoc, no certificate)','changed_files':changed,'added_files':added,
    'all_managed_code_and_icons_byte_unchanged':True,'native_code_changes_limited_to_signing':True,
    'runtime_lookup_paths_preserved_by_relative_links':True,'relocations':relocations,'gatekeeper_assessment_exit':gatekeeper,
    'gatekeeper_bypassed':False,'user_security_settings_changed':False}
(out/'bundle-sealing.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,indent=2))
