import pathlib,subprocess,time
p=pathlib.Path('/private/tmp/elsa-native-perf-20261031');gate=p/'profile.gate'
for f in p.glob('profile.gate*'):f.unlink()
with (p/'profile-app.log').open('w') as log:
 app=subprocess.Popen(['dotnet',str(p/'baseline-bin/Perf.dll'),str(p/'profile.config.json')],stdout=log,stderr=subprocess.STDOUT)
 while not list(p.glob('profile.gate.*.ready')):
  if app.poll() is not None: raise Exception((p/'profile-app.log').read_text())
  time.sleep(.2)
 with (p/'profile-trace.log').open('w') as tlog:
  trace=subprocess.Popen([str(p/'tools/dotnet-trace'),'collect','--process-id',str(app.pid),'--profile','dotnet-sampled-thread-time,gc-verbose','--format','Speedscope','--duration','00:00:20','--output',str(p/'profile.nettrace')],stdout=tlog,stderr=subprocess.STDOUT)
  time.sleep(2);gate.touch()
  app.wait(timeout=180);trace.wait(timeout=180)
  print('app',app.returncode,'trace',trace.returncode)
