import unittest, importlib.util, sys, os, pathlib, json, hashlib, gzip
repo=pathlib.Path("/Users/sipke/.codex/worktrees/f236/elsa-foundation")
os.chdir(repo)
files=[]
for folder in ["e2e-tests/bpmn", "e2e-tests/etl", "tools/bpmn"]:
 files.extend(sorted((repo/folder).glob("test_verify_*.py")))
suite=unittest.TestSuite()
for i,p in enumerate(files):
 spec=importlib.util.spec_from_file_location(f"acceptance_{i}",p); m=importlib.util.module_from_spec(spec); sys.modules[spec.name]=m; spec.loader.exec_module(m)
 suite.addTests(unittest.defaultTestLoader.loadTestsFromModule(m))
 if p.name=="test_verify_studio_upgrade.py":
  class CurrentAdoptedTemplateStudio(m.FinalActualStudioUpgradeControls):
   evidence=repo/"specs/1017-direct-bpmn-hosting/evidence/main-adoption/931123c7e/studio-qualification/studio"
  suite.addTests(unittest.defaultTestLoader.loadTestsFromTestCase(CurrentAdoptedTemplateStudio))
result=unittest.TextTestRunner(verbosity=2).run(suite)
print(json.dumps({"files":len(files),"tests":result.testsRun,"errors":len(result.errors),"failures":len(result.failures),"skips":len(result.skipped)}))
sys.exit(not result.wasSuccessful())
