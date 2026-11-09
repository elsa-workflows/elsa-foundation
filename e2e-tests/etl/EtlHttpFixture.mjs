import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { createHash } from 'node:crypto';

const readyPathIndex = process.argv.indexOf('--ready-file');
const readyPath = readyPathIndex >= 0 ? process.argv[readyPathIndex + 1] : null;
if (!readyPath) {
    throw new Error('Missing --ready-file path.');
}
const retryDelayIndex = process.argv.indexOf('--source-retry-after-seconds');
const sourceRetryAfterSeconds = retryDelayIndex >= 0 ? Number(process.argv[retryDelayIndex + 1]) : 3;
const invalidLastId = process.argv.includes('--invalid-last-id');
const allowRetryRearm = process.argv.includes('--allow-retry-rearm');
const holdSourceFailure = process.argv.includes('--hold-source-failure');
const lookupIndex = process.argv.indexOf('--lookup-snapshot-root');
const effectIndex = process.argv.indexOf('--effect-root');
const lookupRoot = lookupIndex >= 0 ? process.argv[lookupIndex + 1] : null;
const effectRoot = effectIndex >= 0 ? process.argv[effectIndex + 1] : null;
if (Boolean(lookupRoot) !== Boolean(effectRoot) || lookupRoot && (!path.isAbsolute(lookupRoot) || !path.isAbsolute(effectRoot)))
    throw new Error('Lookup failure requires explicit absolute owned snapshot and output roots.');
let sourceHeld = holdSourceFailure || Boolean(lookupRoot);
let heldLookup = null;
let effectsBeforeFailure = [];
if (!Number.isSafeInteger(sourceRetryAfterSeconds) || sourceRetryAfterSeconds < 1 || sourceRetryAfterSeconds > 60) {
    throw new Error('Source Retry-After must be an integer from 1 to 60 seconds.');
}

const requests = [];
const outputPosts = [];
let shardBAttempts = 0;
const retryRearms = [];
let capturedOutputBytes = 0;
let observationsOverflow = false;
const maximumBodyBytes = 1024 * 1024;
const maximumRequestObservations = 4096;
const maximumOutputPosts = 256;
const maximumCapturedOutputBytes = 4 * 1024 * 1024;
const fixtureStartedAt = process.hrtime.bigint();

function elapsedMilliseconds() {
    return Number(process.hrtime.bigint() - fixtureStartedAt) / 1000000;
}

function sendJson(response, status, value, headers = {}) {
    const body = Buffer.from(JSON.stringify(value), 'utf8');
    response.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'content-length': body.length, ...headers });
    response.end(body);
}

function trackRequest(request, status, details = {}) {
    if (requests.length >= maximumRequestObservations) {
        observationsOverflow = true;
        return;
    }
    requests.push({
        method: request.method,
        path: new URL(request.url, 'http://127.0.0.1').pathname,
        status,
        receivedAtMs: elapsedMilliseconds(),
        ...details
    });
}

async function readBody(request) {
    const chunks = [];
    let length = 0;
    for await (const chunk of request) {
        length += chunk.length;
        if (length > maximumBodyBytes) {
            throw new RangeError('body-limit');
        }
        chunks.push(chunk);
    }
    return Buffer.concat(chunks, length);
}

const server = http.createServer(async (request, response) => {
    const url = new URL(request.url, 'http://127.0.0.1');

    if (request.method === 'GET' && url.pathname === '/__fixture/health') {
        sendJson(response, 200, { ready: true });
        return;
    }
    if (request.method === 'GET' && url.pathname === '/__fixture/state') {
        sendJson(response, 200, { requests, outputPosts, overflow: observationsOverflow, ...(allowRetryRearm ? { retryRearms } : {}), ...(holdSourceFailure || lookupRoot ? { sourceHeld } : {}), ...(lookupRoot ? { heldLookup, effectsBeforeFailure } : {}) });
        return;
    }
    if ((holdSourceFailure || lookupRoot) && request.method === 'POST' && url.pathname === '/__fixture/restore-source') {
        if (!sourceHeld) { sendJson(response, 409, { error: 'source-already-restored' }); return; }
        if (lookupRoot) {
            if (!heldLookup) { sendJson(response, 409, { error: 'lookup-failure-not-reached' }); return; }
            const retained = fs.readFileSync(heldLookup.heldPath);
            if (createHash('sha256').update(retained).digest('hex') !== heldLookup.sha256)
                throw new Error('The retained owned lookup changed.');
            fs.renameSync(heldLookup.heldPath, heldLookup.originalPath);
        }
        sourceHeld = false;
        shardBAttempts = 1;
        sendJson(response, 200, { sourceHeld });
        return;
    }
    if (allowRetryRearm && request.method === 'POST' && url.pathname === '/__fixture/rearm-source-retry') {
        if (retryRearms.length !== 0 || shardBAttempts !== 1) {
            sendJson(response, 409, { error: 'retry-rearm-not-current' });
            return;
        }
        const receipt = { rearmNumber: 1, previousShardBAttempts: shardBAttempts, rearmedAtMs: elapsedMilliseconds() };
        retryRearms.push(receipt);
        shardBAttempts = 0;
        sendJson(response, 200, receipt);
        return;
    }

    const shardMatch = /^\/source\/(shard-a|shard-b)\/page$/.exec(url.pathname);
    if (request.method === 'GET' && shardMatch) {
        const shard = shardMatch[1];
        const hasContinuation = url.searchParams.has('pageToken');
        const continuation = url.searchParams.get('pageToken');
        if (lookupRoot && sourceHeld && shard === 'shard-b') {
            const files = fs.readdirSync(effectRoot, { recursive: true }).map(name => path.join(effectRoot, name))
                .filter(name => name.endsWith('.jsonl') && fs.statSync(name).isFile() && fs.statSync(name).size > 0);
            if (!heldLookup && files.length) {
                const inputs = fs.readdirSync(lookupRoot, { recursive: true }).map(name => path.join(lookupRoot, name)).filter(name => name.endsWith('.csv'));
                if (inputs.length !== 1) throw new Error('Expected one owned retained CSV lookup.');
                const originalPath = inputs[0], heldPath = originalPath + '.held';
                const bytes = fs.readFileSync(originalPath);
                heldLookup = { originalPath, heldPath, sha256: createHash('sha256').update(bytes).digest('hex'), bytes: bytes.length };
                effectsBeforeFailure = files.map(file => ({ file, sha256: createHash('sha256').update(fs.readFileSync(file)).digest('hex'), bytes: fs.statSync(file).size }));
                fs.renameSync(originalPath, heldPath);
            }
            trackRequest(request, 429, { shard, page: 0, lookupUnavailable: Boolean(heldLookup) });
            sendJson(response, 429, { error: 'retry' }, { 'retry-after': '1' });
            return;
        }
        if (sourceHeld && shard === 'shard-b') {
            trackRequest(request, 503, { shard, page: 0 });
            sendJson(response, 503, { error: 'dependency-unavailable' });
            return;
        }
        if (shard === 'shard-b' && !hasContinuation && shardBAttempts++ === 0) {
            trackRequest(request, 429, { shard, page: 0 });
            sendJson(response, 429, { error: 'retry' }, { 'retry-after': String(sourceRetryAfterSeconds) });
            return;
        }

        const page = hasContinuation ? 1 : 0;
        const expectedToken = `opaque-token-${shard}`;
        if (page === 1 && continuation !== expectedToken) {
            trackRequest(request, 400, { shard, page });
            sendJson(response, 400, { error: 'request-invalid' });
            return;
        }

        const firstId = shard === 'shard-a' ? 1 : 3;
        const item = page === 0
            ? { id: firstId, name: shard === 'shard-a' ? 'Ada' : 'Linus' }
            : { id: firstId + 1, name: shard === 'shard-a' ? 'Grace' : 'Margaret' };
        if (invalidLastId && shard === 'shard-b' && page === 1) item.id = 'invalid';
        const body = page === 0
            ? { items: [item], next: expectedToken }
            : { items: [item], next: null };
        trackRequest(request, 200, { shard, page });
        sendJson(response, 200, body);
        return;
    }

    if (request.method === 'GET' && url.pathname === '/wait/page') {
        trackRequest(request, 429, { shard: 'wait', page: 0 });
        sendJson(response, 429, { error: 'retry' }, { 'retry-after': '30' });
        return;
    }

    if (request.method === 'POST' && url.pathname === '/output/records') {
        let body;
        try {
            body = await readBody(request);
        } catch (error) {
            const status = error instanceof RangeError ? 413 : 400;
            trackRequest(request, status);
            sendJson(response, status, { error: status === 413 ? 'body-too-large' : 'body-invalid' });
            return;
        }

        let records;
        try {
            records = JSON.parse(body.toString('utf8'));
        } catch {
            trackRequest(request, 400);
            sendJson(response, 400, { error: 'json-invalid' });
            return;
        }
        if (!Array.isArray(records)) {
            trackRequest(request, 400);
            sendJson(response, 400, { error: 'json-array-required' });
            return;
        }
        trackRequest(request, 200, { recordCount: records.length });
        if (outputPosts.length >= maximumOutputPosts || capturedOutputBytes + body.length > maximumCapturedOutputBytes) {
            observationsOverflow = true;
        } else {
            capturedOutputBytes += body.length;
            outputPosts.push({
                recordCount: records.length,
                records,
                hasIdempotencyKey: typeof request.headers['idempotency-key'] === 'string' && request.headers['idempotency-key'].trim().length > 0
            });
        }
        sendJson(response, 200, { accepted: true });
        return;
    }

    trackRequest(request, 404);
    sendJson(response, 404, { error: 'not-found' });
});

server.on('clientError', (_error, socket) => {
    if (socket.writable) {
        socket.end('HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n');
    }
});

server.listen(0, '127.0.0.1', () => {
    const address = server.address();
    const temporaryReadyPath = `${readyPath}.${process.pid}.tmp`;
    fs.writeFileSync(temporaryReadyPath, JSON.stringify({ port: address.port }), { encoding: 'utf8', flag: 'wx' });
    fs.renameSync(temporaryReadyPath, readyPath);
});

function stop() {
    server.close(() => process.exit(0));
}
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
