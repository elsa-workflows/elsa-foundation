#!/usr/bin/env python3
"""Owned loopback effect destination with durable, unique invocation-key recognition."""

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import sqlite3
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--database', required=True, type=Path)
    parser.add_argument('--ready-file', required=True, type=Path)
    parser.add_argument('--port', type=int, default=0)
    parser.add_argument('--stop-file', required=True, type=Path)
    args = parser.parse_args()
    database = args.database.resolve()
    with sqlite3.connect(str(database)) as connection:
        connection.execute('PRAGMA journal_mode=WAL')
        connection.execute('PRAGMA synchronous=FULL')
        connection.execute('CREATE TABLE IF NOT EXISTS effects '
                           '(invocation_key TEXT PRIMARY KEY, effect_id TEXT NOT NULL UNIQUE, subtotal TEXT NOT NULL)')
        connection.execute('CREATE TABLE IF NOT EXISTS requests '
                           '(request_id TEXT PRIMARY KEY, invocation_key TEXT NOT NULL, attempt_id TEXT NOT NULL)')
    database.chmod(0o600)

    class Destination(BaseHTTPRequestHandler):
        def log_message(self, _format, *arguments):
            pass

        def respond(self, status, value):
            body = json.dumps(value).encode()
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if self.path != '/state':
                self.respond(404, {'error': 'unknown-path'})
                return
            with sqlite3.connect(str(database)) as connection:
                effects = connection.execute('SELECT invocation_key, effect_id, subtotal FROM effects ORDER BY invocation_key').fetchall()
                requests = connection.execute('SELECT invocation_key, attempt_id FROM requests ORDER BY request_id').fetchall()
            self.respond(200, {'effects': [{'invocationKey': row[0], 'effectId': row[1], 'subtotal': row[2]} for row in effects],
                               'requests': [{'invocationKey': row[0], 'attemptId': row[1]} for row in requests]})

        def do_POST(self):
            if self.path != '/effects':
                self.respond(404, {'error': 'unknown-path'})
                return
            try:
                length = int(self.headers.get('Content-Length', '0'))
                if not 0 < length <= 8192:
                    raise ValueError('Body outside qualification bound.')
                value = json.loads(self.rfile.read(length))
                key, attempt = value['invocationKey'], value['attemptId']
                if not isinstance(key, str) or not key.startswith('elsa.jobs.invocation.v1:') or len(key) > 256:
                    raise ValueError('Invalid invocation identity.')
                if not isinstance(attempt, str) or not 0 < len(attempt) <= 256:
                    raise ValueError('Invalid attempt identity.')
                subtotal = str(value['subtotal'])
                with sqlite3.connect(str(database), timeout=5) as connection:
                    connection.execute('PRAGMA synchronous=FULL')
                    connection.execute('BEGIN IMMEDIATE')
                    inserted = connection.execute('INSERT OR IGNORE INTO effects VALUES (?, ?, ?)',
                                                  (key, uuid.uuid4().hex, subtotal)).rowcount == 1
                    effect_id, original_total = connection.execute(
                        'SELECT effect_id, subtotal FROM effects WHERE invocation_key = ?', (key,)).fetchone()
                    if original_total != subtotal:
                        connection.rollback()
                        self.respond(409, {'error': 'invocation-content-conflict'})
                        return
                    connection.execute('INSERT INTO requests VALUES (?, ?, ?)', (uuid.uuid4().hex, key, attempt))
                self.respond(200, {'effectId': effect_id, 'duplicate': not inserted})
            except (ValueError, KeyError, TypeError):
                self.respond(400, {'error': 'invalid-qualification-request'})

    if not 0 <= args.port <= 65535:
        parser.error('Port must be zero or a valid explicit owned port.')
    server = ThreadingHTTPServer(('127.0.0.1', args.port), Destination)
    ready = {'url': 'http://127.0.0.1:' + str(server.server_port), 'database': str(database)}
    temporary = args.ready_file.with_suffix('.tmp')
    temporary.write_text(json.dumps(ready))
    temporary.replace(args.ready_file)
    try:
        server.timeout = 0.1
        while not args.stop_file.is_file():
            server.handle_request()
    finally:
        server.server_close()


if __name__ == '__main__':
    main()
