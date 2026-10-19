"""Loopback-only cockpit around the existing synthetic customer API.

No Studio, workflow or backend changes. Captured request details stay in memory.
Only explicit same-origin POST may reveal a recognized disposable demo key.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import time
import urllib.error
import urllib.parse
import urllib.request

from observations import ObservationStore, RequestNotFound, CredentialNotRevealable
import demo_api as demo

ROOT = Path(__file__).resolve().parent


def now():
    return datetime.now(timezone.utc).isoformat(timespec='milliseconds').replace('+00:00', 'Z')


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class CockpitHandler(demo.DemoApiHandler):
    def _guard(self, mutation=False):
        port = self.server.server_address[1]
        hosts = {f'127.0.0.1:{port}', f'localhost:{port}'}
        origins = {f'http://{host}' for host in hosts}
        host = self.headers.get('Host', '').lower()
        origin = self.headers.get('Origin')
        if host not in hosts or (origin and origin not in origins) or self.headers.get('Sec-Fetch-Site') == 'cross-site':
            self._json(403, {'error': 'This cockpit is available only on its local address.'})
            return False
        if mutation and self.headers.get('X-Demo-Cockpit') != '1':
            self._json(403, {'error': 'Use the local cockpit controls for this action.'})
            return False
        return True

    def end_headers(self):
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('Referrer-Policy', 'no-referrer')
        self.send_header('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'")
        super().end_headers()

    def _json(self, status, payload):
        demo.DemoApiHandler._reply(self, status, payload)

    def _body(self):
        if self.headers.get_content_type() != 'application/json':
            raise ValueError('Send JSON with Content-Type application/json.')
        try:
            length = int(self.headers.get('Content-Length', '0'))
        except ValueError:
            raise ValueError('Invalid content length.') from None
        if not 0 < length <= 4096 or self.headers.get('Transfer-Encoding'):
            raise ValueError('The request body must be between 1 and 4096 bytes.')
        self.connection.settimeout(5)
        try:
            result = json.loads(self.rfile.read(length))
        except (ValueError, OSError):
            raise ValueError('Enter a valid JSON object.') from None
        if not isinstance(result, dict):
            raise ValueError('Enter a JSON object.')
        return result

    def do_GET(self):
        self._observed = False
        self._started = time.perf_counter()
        if not self._guard():
            return
        path = urllib.parse.urlsplit(self.path).path
        if path in {'/cockpit', '/cockpit/'}:
            return self._file('cockpit.html', 'text/html; charset=utf-8')
        static = {'/cockpit/app.js': ('app.js', 'text/javascript; charset=utf-8'),
                  '/cockpit/style.css': ('style.css', 'text/css; charset=utf-8'),
                  '/cockpit/elsa-icon.png': ('elsa-icon.png', 'image/png')}
        if path in static:
            return self._file(*static[path])
        if path == '/cockpit/state':
            return self._json(200, {'service': {'name': 'Customer API', 'endpoint': f'http://127.0.0.1:{self.server.server_address[1]}', 'authScheme': 'Bearer', 'startedAt': self.server.started_at}, **self.server.observations.snapshot()})
        if path.startswith('/cockpit/'):
            return self._json(404, {'error': 'not_found'})
        self._observed = self.path == f'/api/customers/{demo.CUSTOMER_ID}'
        return super().do_GET()

    def _reply(self, status, payload):
        super()._reply(status, payload)
        if getattr(self, '_observed', False):
            self.server.observations.record_request(method='GET', path=self.path, status=status,
                duration_ms=round((time.perf_counter() - self._started) * 1000, 2),
                request_headers=self.headers, response_headers={'Content-Type': 'application/json; charset=utf-8'},
                request_body=None, response_body=payload)

    def _file(self, filename, content_type):
        try:
            body = (ROOT / filename).read_bytes()
        except OSError:
            return self._json(404, {'error': 'Asset not available.'})
        self.send_response(200)
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        if not self._guard(mutation=True):
            return
        path = urllib.parse.urlsplit(self.path).path
        if path not in {'/cockpit/reveal', '/cockpit/quote'}:
            return self._json(404, {'error': 'not_found'})
        try:
            body = self._body()
        except ValueError as error:
            return self._json(400, {'error': str(error)})
        if path == '/cockpit/reveal':
            request_id = body.get('requestId')
            if not isinstance(request_id, str) or len(request_id) > 128:
                return self._json(400, {'error': 'Choose an observed request first.'})
            try:
                authorization = self.server.observations.reveal_authorization(request_id)
            except RequestNotFound:
                return self._json(404, {'error': 'This request is no longer in the live history.'})
            except CredentialNotRevealable:
                return self._json(409, {'error': 'Only recognized disposable demo keys can be revealed.'})
            return self._json(200, {'authorization': authorization})
        if set(body) != {'subtotal', 'shipping'} or any(type(body[key]) is not int or not 0 <= body[key] <= 1000000 for key in body):
            return self._json(400, {'error': 'Subtotal and shipping must be whole numbers between 0 and 1000000.'})
        request = urllib.request.Request(self.server.workflow_url, data=demo.json_bytes(body), method='POST', headers={'Content-Type': 'application/json'})
        started = time.perf_counter()
        status = 502
        try:
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
            with opener.open(request, timeout=35) as response:
                status, content = response.status, response.read(65536)
        except urllib.error.HTTPError as error:
            with error:
                status, content = error.code, error.read(65536)
        except (urllib.error.URLError, OSError):
            status, content = 502, demo.json_bytes({'error': 'The workflow could not be reached. Check Studio and try again.'})
        response_valid = True
        try:
            payload = json.loads(content)
        except ValueError:
            response_valid = False
            payload = {'error': 'The workflow did not return a JSON response.'}
        quote = self.server.observations.record_quote(status=status, duration_ms=round((time.perf_counter() - started) * 1000, 2), request=body, body=payload, response_valid=response_valid)
        self._json(200, quote)


def make_server(port, credentials, workflow_url):
    parsed = urllib.parse.urlsplit(workflow_url)
    if parsed.scheme != 'http' or parsed.hostname not in {'127.0.0.1', 'localhost'} or parsed.path != '/workflows/http/quotes' or parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise ValueError('The quote target must be the local demo workflow URL.')
    server = demo.DemoApiServer(('127.0.0.1', port), CockpitHandler)
    server.credential_versions = credentials
    server.observations = ObservationStore(credentials)
    server.started_at = now()
    server.workflow_url = workflow_url
    return server


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=5582)
    parser.add_argument('--credentials-file', type=Path)
    parser.add_argument('--workflow-url', default='http://localhost:5595/workflows/http/quotes')
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error('Choose a valid local port.')
    if args.credentials_file:
        values = json.loads(args.credentials_file.read_text())
        for name in ['DEMO_CUSTOMER_API_TOKEN_V1', 'DEMO_CUSTOMER_API_TOKEN_V2']:
            os.environ[name] = values[name]
    server = make_server(args.port, demo.credential_versions(), args.workflow_url)
    print(f'Demo cockpit ready: http://127.0.0.1:{server.server_address[1]}/cockpit', flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == '__main__':
    main()
