# Designer Test Drive acceptance journey

Run against an isolated, already running Development Workbench host with its SQLite runtime modules enabled:

```sh
python3 e2e-tests/test-drive/test_designer_test_drive.py --base-url http://localhost:5095 --output-dir /tmp/elsa-test-drive-evidence
```

The script uses Python 3 standard-library HTTP support. It signs in with the seeded local development administrator and exchanges that cookie session for a bearer token, then runs the journey without cookies, matching Studio's API transport. Pass `--auth-mode cookie` to check the cookie path separately. It creates an unpublished draft and checks required workflow inputs, exact Event/HTTP/Timer/Cron start selection, a real HTTP request and response, an exact active Event wait, durable retry/conflict behavior, global-stimulus exclusion, and actual root cancellation after Stop. Timer/Cron preview uses the authored UTC schedule and fires once without installing a recurring schedule.

It starts or stops no host process. It leaves its authored draft and test runs for inspection; run it only against disposable local demo/test data. The output contains the authored recording workflow and acceptance evidence, without login cookies or transport credentials.

The workflow root can remain Running while its child Event waits. The acceptance check therefore observes a suspended child with an active bookmark rather than assuming the root status becomes Suspended.
