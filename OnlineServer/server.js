'use strict';
// Peninsula Time online server: accounts, territory battle rooms and the healing-mode city gallery.
// Run: node server.js   (PORT, HOST and DATA_DIR are optional environment variables)
const http = require('node:http');
const path = require('node:path');
const fs = require('node:fs');
const crypto = require('node:crypto');
const { BattleRoom, RULES, loadGrid } = require('./battle');
const { HealingStore } = require('./healing');
const { AccountStore } = require('./accounts');

const MAX_BODY = 16 * 1024;
const MAX_ROOMS = 20;
const RATE_PER_SECOND = 25;
const SAVE_SECONDS = 10;

function createServer({ dataDir = path.join(__dirname, 'data'), random = Math.random, ratePerSecond = RATE_PER_SECOND } = {}) {
  fs.mkdirSync(dataDir, { recursive: true });
  const grid = loadGrid(path.join(__dirname, 'korea-grid.txt'));
  const healing = new HealingStore(path.join(dataDir, 'healing.json'));
  const accounts = new AccountStore(path.join(dataDir, 'accounts.json'));
  const battleFile = path.join(dataDir, 'battles.json');
  const rooms = new Map();
  const hits = new Map();

  if (fs.existsSync(battleFile)) {
    for (const data of JSON.parse(fs.readFileSync(battleFile, 'utf8'))) {
      try { rooms.set(data.id, BattleRoom.restore(data, grid, random)); } catch (error) { process.stderr.write(`skip room ${data.id}: ${error.message}\n`); }
    }
  }
  function saveBattles() {
    const temp = battleFile + '.tmp';
    fs.writeFileSync(temp, JSON.stringify([...rooms.values()].map((room) => room.save())), { mode: 0o600 });
    fs.renameSync(temp, battleFile);
  }

  let ticks = 0;
  const ticker = setInterval(() => {
    for (const [id, room] of rooms) {
      room.step();
      if (room.expired()) rooms.delete(id);
    }
    hits.clear();
    if (++ticks % (SAVE_SECONDS * 1000 / RULES.tickMs) === 0) saveBattles();
  }, RULES.tickMs);
  ticker.unref();

  function send(res, status, body) {
    res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
    res.end(JSON.stringify(body));
  }

  function readBody(req) {
    return new Promise((resolve, reject) => {
      let size = 0;
      const chunks = [];
      req.on('data', (chunk) => {
        size += chunk.length;
        if (size > MAX_BODY) { reject(new Error('too large')); req.destroy(); return; }
        chunks.push(chunk);
      });
      req.on('end', () => {
        try { resolve(JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}')); } catch { reject(new Error('bad json')); }
      });
      req.on('error', reject);
    });
  }

  function bearer(req) {
    const header = req.headers.authorization || '';
    return header.startsWith('Bearer ') ? header.slice(7) : '';
  }

  function openRoom() {
    for (const room of rooms.values()) if (room.phase === 'spawn' && room.humans().length < RULES.maxHumans) return room;
    if (rooms.size >= MAX_ROOMS) return null;
    const room = new BattleRoom(crypto.randomUUID().slice(0, 6), grid, random);
    rooms.set(room.id, room);
    return room;
  }

  // A logged-in player still alive in an unfinished room rejoins it instead of starting over.
  function currentRoom(username) {
    for (const room of rooms.values()) {
      const player = room.byAccount(username);
      if (player && player.alive && room.phase !== 'over') return { room, player };
    }
    return null;
  }

  async function route(req, res) {
    const url = new URL(req.url, 'http://localhost');
    const ip = req.socket.remoteAddress || '';
    const count = (hits.get(ip) || 0) + 1;
    hits.set(ip, count);
    if (count > ratePerSecond * RULES.tickMs / 1000) return send(res, 429, { error: '요청이 너무 많습니다.' });
    const username = accounts.user(bearer(req));

    if (req.method === 'GET' && url.pathname === '/health') return send(res, 200, { ok: true, rooms: rooms.size });

    if (req.method === 'POST' && (url.pathname === '/auth/register' || url.pathname === '/auth/login')) {
      const body = await readBody(req);
      const result = url.pathname === '/auth/register'
        ? await accounts.register(body.username, body.password)
        : await accounts.login(body.username, body.password);
      if (result.error) return send(res, result.status || 400, { error: result.error });
      return send(res, 200, result);
    }
    if (req.method === 'GET' && url.pathname === '/auth/me') {
      return username ? send(res, 200, { username }) : send(res, 401, { error: '로그인이 필요합니다.' });
    }
    if (req.method === 'POST' && url.pathname === '/auth/logout') {
      accounts.logout(bearer(req));
      return send(res, 200, { ok: true });
    }

    if (req.method === 'GET' && url.pathname === '/battle/rooms') {
      return send(res, 200, {
        rooms: [...rooms.values()].map((r) => ({ id: r.id, phase: r.phase, humans: r.humans().length, players: r.players.length, tick: r.tick })),
      });
    }

    if (req.method === 'GET' && url.pathname === '/battle/state') {
      const room = rooms.get(url.searchParams.get('room') || '');
      if (!room) return send(res, 404, { error: '방을 찾을 수 없습니다.' });
      const viewer = room.byToken(url.searchParams.get('token') || '');
      if (viewer) viewer.seen = room.tick;
      return send(res, 200, room.snapshot(viewer));
    }

    if (req.method === 'POST' && url.pathname === '/battle/join') {
      if (!username) return send(res, 401, { error: '대전은 로그인 후 참가할 수 있습니다.' });
      const current = currentRoom(username);
      if (current) return send(res, 200, { room: current.room.id, token: current.player.token, index: current.player.index, rejoined: true });
      const room = openRoom();
      if (!room) return send(res, 503, { error: '서버의 방이 가득 찼습니다.' });
      const result = room.addPlayer(username, false, username);
      if (result.error) return send(res, 409, result);
      return send(res, 200, { room: room.id, token: result.player.token, index: result.player.index });
    }

    if (req.method === 'POST' && url.pathname === '/battle/act') {
      const body = await readBody(req);
      const room = rooms.get(String(body.room || ''));
      if (!room) return send(res, 404, { error: '방을 찾을 수 없습니다.' });
      const player = room.byToken(body.token);
      if (!player) return send(res, 403, { error: '관전자는 명령할 수 없습니다.' });
      player.seen = room.tick;
      const result = room.act(player, { type: String(body.type || ''), kind: String(body.kind || ''), tile: Number(body.tile), ratio: Number(body.ratio) });
      return send(res, result.error ? 400 : 200, result);
    }

    if (req.method === 'GET' && url.pathname === '/healing/list') return send(res, 200, { cities: healing.list() });

    if (req.method === 'GET' && url.pathname === '/healing/city') {
      const city = healing.city(url.searchParams.get('id') || '');
      return city ? send(res, 200, city) : send(res, 404, { error: '공개된 도시를 찾을 수 없습니다.' });
    }

    if (req.method === 'POST' && url.pathname === '/healing/publish') {
      if (!username) return send(res, 401, { error: '도시 공개는 로그인 후 할 수 있습니다.' });
      const result = healing.publish(await readBody(req), username);
      return send(res, result.error ? 400 : 200, result);
    }

    return send(res, 404, { error: '없는 주소입니다.' });
  }

  const server = http.createServer((req, res) => {
    route(req, res).catch((error) => {
      const status = error.message === 'too large' ? 413 : 400;
      send(res, status, { error: status === 413 ? '요청이 너무 큽니다.' : '잘못된 요청입니다.' });
    });
  });
  server.on('close', () => { clearInterval(ticker); saveBattles(); });
  return { server, rooms, healing, accounts, saveBattles };
}

if (require.main === module) {
  const port = Number(process.env.PORT) || 8787;
  const host = process.env.HOST || '0.0.0.0';
  const { server, saveBattles } = createServer({ dataDir: process.env.DATA_DIR || path.join(__dirname, 'data') });
  for (const signal of ['SIGINT', 'SIGTERM']) {
    process.on(signal, () => { saveBattles(); process.exit(0); });
  }
  server.listen(port, host, () => process.stdout.write(`Peninsula Time online server on http://${host}:${port}\n`));
}

module.exports = { createServer };
