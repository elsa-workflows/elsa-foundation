# Elsa Studio customer quote demo

The final six-slide presentation, speaker notes and synthetic API cockpit from the October 2026 demo. This folder is self-contained; it does not depend on a Documents folder, private database backup or Codex runtime.

## Present

From this folder, start the static presentation server:

```sh
python3 -m http.server 5584 --bind 127.0.0.1 --directory public
```

- Slides: http://127.0.0.1:5584/slides.html
- Speaker view: http://127.0.0.1:5584/teleprompter.html
- [Plain script](public/teleprompter.md), [walkthrough](public/walkthrough.md), [PowerPoint](public/Elsa-Studio-Demo-v4.pptx)
- [Cockpit setup and controls](cockpit/README.md)

The HTML uses local assets and also opens directly from disk. Slides support arrow keys, Previous/Next, full screen and printing all six chapters. The teleprompter supports chapter selection, text size, speed, pause and manual scrolling. It stops at each chapter boundary. The slides deliberately have no teleprompter footer link.

`story.json` is the plain narrative source. The final HTML is maintained directly; the PowerPoint is an editable alternate layout with the same story. Do not run an older generator from an earlier demo folder over these final files.

## Prepare a fresh environment

Use a disposable Elsa Workbench with HTTP, Secrets, Connections, the designer and JavaScript enabled, plus Studio. The local ports used here are backend **5595**, Studio **5589**, synthetic customer API/cockpit **5582**, and presentation **5584**. Start the backend and Studio using their normal repository instructions; this package does not create or migrate their databases.

1. Start the [synthetic customer API/cockpit](cockpit/README.md) with two distinct disposable demo keys. Keep the keys outside this checkout. It responds only on loopback.
2. In Studio, create an encrypted Text Secret named `customer-api` containing the second demo key as a raw value, without `Bearer `. Leave Scope unset for this example.
3. Create the HTTP Connection `customer-api` (display name **Customer API**). Choose Bearer authentication and the existing Secret. Allow host `127.0.0.1`; allow insecure HTTP only for this local mock.
4. Prepare CalculateOrderTotal and the quote workflow. The optional `setup_demo.py` helper can create them on a fresh environment after the Connection exists. Run `python3 setup_demo.py` for its no-write plan. Set `ELSA_DEMO_STUDIO_USERNAME` and `ELSA_DEMO_STUDIO_PASSWORD` privately, then run `python3 setup_demo.py --inspect` to check readiness and `python3 setup_demo.py --apply` to create and publish the two definitions. The helper is restricted to the local backend, refuses duplicate names, and does not create, read or rotate Secret values. Do not use its repair/fault switches on an environment you want to preserve.
5. The calculator accepts integer Subtotal and Shipping, returns integer Total using `args.Subtotal + args.Shipping`, and emits Calculated. The quote workflow receives POST `quotes`, looks up `/api/customers/C-1042` through the Connection, calls the published calculator and writes JSON containing customerId, name and total.
6. Use the cockpit **Send quote request** with 40 and 2. Expect HTTP 200 and total 42. The request list shows the customer API call and masked authentication; the workflow result is displayed separately.

Connection persistence depends on the configured backend repository. With an in-memory repository, recreate the Connection after a backend restart; Secret persistence alone does not preserve the Connection. No database, Connection state or run history is shipped here.

## Walk through an existing environment

Open the existing resources by name. Do not rerun setup or reset working data. Resource IDs and saved run links depend on the environment, so the walkthrough describes navigation rather than baking in one machine's IDs. The screenshots are illustrative receipts from the original demo, not live state. Use the cockpit for live API observations.

Diagnosis and Secret rotation are optional live demonstrations. To show the fault, publish a response Body that reads the missing `profile.segment`, send a quote, inspect the Body incident, then restore the working expression, publish and retest. Keep credentials masked during screen sharing. API key labels v1/v2 are independent of the Secret store's version number.

The recording and watch-page placeholder are intentionally excluded: the owner chose to finish source and delivery cleanup without a recording.

## Verify

```sh
python3 -m unittest discover -s cockpit -v
python3 -m unittest -v test_setup
node --check cockpit/app.js
python3 verify_presentation.py
```

The tests use fake keys and ephemeral local servers. They never alter a running demo's Secret, Connection or workflow.
