"""Local SSH/SFTP integration fixture; never runs received shell commands.
Linux/systemd deployment responses are tested separately, not fabricated here.
"""
import sys,os,json,socket,threading,time,hashlib,base64
from pathlib import Path
import paramiko
root=Path(sys.argv[1]).resolve();root.mkdir(parents=True,exist_ok=True)
(root/'uploads').mkdir(exist_ok=True)
paramiko.util.log_to_file(str(root/'protocol-debug.log'))
hostkey=paramiko.RSAKey.generate(2048);clientkey=paramiko.RSAKey.generate(2048)
clientkey.write_private_key_file(str(root/'client.pem'))
clientkey.write_private_key_file(str(root/'encrypted.pem'),password='local-fixture-passphrase')
class Server(paramiko.ServerInterface):
 def check_auth_publickey(self,user,key):
  same=key.asbytes()==clientkey.asbytes()
  print(json.dumps({'public_key_user':user,'matches_expected_fixture_key':same}),flush=True)
  return paramiko.AUTH_SUCCESSFUL if user=='local-test' and same else paramiko.AUTH_FAILED
 def check_auth_password(self,user,password):return paramiko.AUTH_SUCCESSFUL if user=='local-test' and password=='local-fixture-password' else paramiko.AUTH_FAILED
 def get_allowed_auths(self,user):return 'publickey,password'
 def check_channel_pty_request(self,channel,term,width,height,pixelwidth,pixelheight,modes):return True
 def check_channel_shell_request(self,channel):
  def shell():
   try:
    channel.send('本机隔离测试\r\n> '.encode('utf-8'));pending=b''
    while True:
     b=channel.recv(1024)
     if not b:break
     pending+=b
     if b'\r' in pending:
      if pending.strip()==b'fixture-test':
       output='NEKOPROXY-TERMINAL-TEST 中文\r\n> '.encode('utf-8')
       for byte in output:channel.send(bytes([byte]));time.sleep(.001)
      else:channel.send(b'Fixture does not run commands\r\n> ')
      pending=b''
   finally:channel.close()
  threading.Thread(target=shell,daemon=True).start();return True
 def check_channel_request(self,kind,channel):return paramiko.OPEN_SUCCEEDED if kind=='session' else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED
 def check_channel_exec_request(self,channel,command):
  def reply():
   time.sleep(.02)
   if command.decode() == "printf 'NEKOPROXY-SSH-TEST'":channel.send(b'NEKOPROXY-SSH-TEST');channel.send_exit_status(0)
   elif command.decode() == "printf 'LINE1'\nprintf 'LINE2'":channel.send(b'LINE1LINE2');channel.send_exit_status(0)
   else:channel.send_stderr(b'Fixture rejects shell execution');channel.send_exit_status(42)
   channel.close()
  threading.Thread(target=reply,daemon=True).start();return True
class Files(paramiko.SFTPServerInterface):
 def open(self,path,flags,attr):
  if path!='/tmp/nekoproxy.fixturetest/payload.bin':return paramiko.SFTP_PERMISSION_DENIED
  f=open(root/'uploads/payload.bin','wb' if flags & os.O_WRONLY or flags & os.O_RDWR else 'rb')
  handle=paramiko.SFTPHandle(flags);handle.writefile=f;handle.readfile=f;return handle
 def stat(self,path):
  try:return paramiko.SFTPAttributes.from_stat((root/'uploads/payload.bin').stat())
  except OSError:return paramiko.SFTP_NO_SUCH_FILE
listener=socket.socket();listener.bind(('127.0.0.1',0));listener.listen(20);listener.settimeout(.4)
stall=socket.socket();stall.bind(('127.0.0.1',0));stall.listen(5);stall.settimeout(.4)
info={'host':'127.0.0.1','port':listener.getsockname()[1],'stallPort':stall.getsockname()[1],'username':'local-test','keyPath':str(root/'client.pem'),'encryptedKeyPath':str(root/'encrypted.pem'),'uploadedFile':str(root/'uploads/payload.bin'),'fingerprint':'SHA256:'+base64.b64encode(hashlib.sha256(hostkey.asbytes()).digest()).decode().rstrip('=')}
(root/'connection.json').write_text(json.dumps(info),encoding='utf-8')
def serve(sock):
 t=paramiko.Transport(sock)
 try:
  t.add_server_key(hostkey);t.set_subsystem_handler('sftp',paramiko.SFTPServer,Files);t.start_server(server=Server())
  while t.is_active() and not (root/'stop').exists():time.sleep(.1)
 except Exception:pass
 finally:t.close()
def stall_loop():
 while not (root/'stop').exists():
  try:s,_=stall.accept();threading.Thread(target=lambda:(time.sleep(3),s.close()),daemon=True).start()
  except socket.timeout:pass
threading.Thread(target=stall_loop,daemon=True).start()
while not (root/'stop').exists():
 try:s,_=listener.accept();threading.Thread(target=serve,args=(s,),daemon=True).start()
 except socket.timeout:pass
listener.close();stall.close()
