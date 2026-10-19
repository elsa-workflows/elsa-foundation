# Customer API cockpit

This local demo page observes real calls to the synthetic customer API and can send a quote request to the prepared Studio workflow. It does not create or modify Studio resources.

## Start

Set `DEMO_CUSTOMER_API_TOKEN_V1` and `DEMO_CUSTOMER_API_TOKEN_V2` in the process environment to two distinct raw disposable values. Do not put token values in this directory, shell history, logs, screenshots, or recordings. Then run:

```sh
python3 -u server.py --port 5582 --workflow-url http://localhost:5595/workflows/http/quotes
```

Open `http://127.0.0.1:5582/cockpit`. The API binds to loopback. The browser controls and quote-workflow target are restricted to local addresses.

If an operator-managed JSON file is preferred, keep it outside this package and pass its path explicitly:

```sh
python3 -u server.py --port 5582 --credentials-file /secure/path/customer-api-keys.json
```

The JSON file must contain the two environment-variable names above. The server reads it at startup and does not copy it into this package. The default quote target is `http://localhost:5595/workflows/http/quotes`; both the port and target can be changed with the command-line options.

## Runtime data and credentials

The API call ledger defaults to `state/demo-api-calls.jsonl` in this directory. Git ignores the entire `state/` folder. Set `ELSA_DEMO_STATE_DIR` to an operator-chosen external directory when running an isolated copy or test. The ledger records safe request metadata and the matched API fixture label (`v1` or `v2`), never a token. Cockpit observations are kept in memory and are cleared when the server restarts.

The `v1` and `v2` labels identify the two disposable API fixtures; they do not assert an Elsa Secret version. Check the current Secret metadata in the running Studio environment when that matters.

## Check

The package uses Python’s standard library. Run its tests and check the browser script with:

```sh
python3 -m unittest discover -v
node --check app.js
```

Tests use synthetic credentials and a local fake workflow endpoint; they do not access Studio or the live demo ledger.
