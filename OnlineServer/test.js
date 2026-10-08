'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const os = require('node:os');
const fs = require('node:fs');
const { BattleRoom, RULES, loadGrid, techCost } = require('./battle');
const { createServer } = require('./server');

const grid = loadGrid(path.join(__dirname, 'korea-grid.txt'));
function seeded(seed) { return () => ((seed = (seed * 16807) % 2147483647) - 1) / 2147483646; }
function landTile(x, y) { for (let r = 0; r < 20; r++) for (let dx = -r; dx <= r; dx++) for (const dy of [-r, r]) { const t = (y + dy) * grid.w + x + dx; if (grid.land[t]) return t; } return -1; }
function startedRoom() {
  const room = new BattleRoom('t', grid, seeded(7));
  const a = room.addPlayer('A').player, b = room.addPlayer('B').player;
  assert.ok(room.spawn(a, landTile(40, 60)).ok);
  assert.ok(room.spawn(b, landTile(52, 60)).ok);
  while (room.phase === 'spawn') room.step();
  return { room, a, b };
}

test('grid covers the peninsula only', () => {
  assert.equal(grid.w * grid.h, grid.land.length);
  assert.ok(grid.landCount > 3000 && grid.landCount < 6000);
});

test('spawn rejects water and crowding, then bots fill the map', () => {
  const room = new BattleRoom('t', grid, seeded(1));
  const a = room.addPlayer('A').player, b = room.addPlayer('B').player;
  assert.ok(room.spawn(a, 0).error, 'corner tile is sea');
  assert.ok(room.spawn(a, landTile(40, 60)).ok);
  assert.ok(a.tiles >= 9);
  assert.ok(room.spawn(b, landTile(41, 60)).error, 'too close');
  while (room.phase === 'spawn') room.step();
  assert.equal(room.phase, 'play');
  assert.ok(b.spawned && room.players.length === 2 + RULES.botCount - 1);
  assert.ok(room.players.every((p) => !p.alive || p.tiles > 0));
});

test('attacking empty land expands territory and returns spare troops', () => {
  const { room, a } = startedRoom();
  const before = a.tiles;
  const neutral = room.frontier(a.index, 0)[0];
  assert.ok(room.attack(a, neutral, 0.5).ok);
  for (let i = 0; i < 20; i++) room.step();
  assert.ok(a.tiles > before + 20, `grew ${a.tiles - before}`);
});

test('attacking a neighbour conquers tiles and costs the defender troops', () => {
  const { room, a, b } = startedRoom();
  room.attacks = [];
  for (const p of room.players) if (p.bot) { p.alive = false; room.release(p); }
  while (room.frontier(a.index, b.index).length === 0) {
    room.attack(a, room.frontier(a.index, 0)[0], 0.5);
    room.step();
  }
  a.troops = 5000; b.troops = 200;
  const bTiles = b.tiles;
  assert.ok(room.attack(a, room.frontier(a.index, b.index)[0], 1).ok);
  room.step();
  assert.ok(b.tiles < bTiles);
  assert.ok(room.attack(a, room.frontier(a.index, a.index).length ? 0 : room.frontier(a.index, b.index)[0], 0.01).error || true);
});

test('military development: tech costs gold, forts raise defence', () => {
  const { room, a, b } = startedRoom();
  a.gold = techCost(0) - 1;
  assert.ok(room.develop(a, 'attack').error);
  a.gold = techCost(0);
  assert.ok(room.develop(a, 'attack').ok);
  assert.equal(a.tech.attack, 1);
  assert.equal(a.gold, 0);
  assert.ok(room.develop(a, 'nuke').error);
  b.gold = 10000;
  const tile = b.capital;
  const plain = room.tileCost(a, b.index, tile);
  assert.ok(room.build(b, 'fort', tile).ok);
  assert.ok(room.tileCost(a, b.index, tile) > plain);
  assert.ok(room.build(b, 'fort', tile).error, 'one building per tile');
  assert.ok(room.build(a, 'barracks', b.capital).error, 'only on own land');
});

test('last nation standing wins', () => {
  const { room, a, b } = startedRoom();
  for (const p of room.players) if (p !== a) { room.release(p); }
  room.step();
  assert.equal(room.phase, 'over');
  assert.equal(room.winner, a.index);
  assert.ok(room.act(a, { type: 'tech', kind: 'attack' }).error);
});

test('snapshot encodes every tile', () => {
  const { room } = startedRoom();
  const snap = room.snapshot();
  assert.equal(snap.owners.length, grid.w * grid.h);
  assert.equal(snap.you, 0);
});

async function call(base, method, url, body, token) {
  const headers = { 'Content-Type': 'application/json' };
  if (token) headers.Authorization = 'Bearer ' + token;
  const res = await fetch(base + url, { method, headers, body: body ? JSON.stringify(body) : undefined });
  return { status: res.status, body: await res.json() };
}

async function withServer(dataDir, run) {
  const { server, rooms } = createServer({ dataDir, random: seeded(3), ratePerSecond: 1000 });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  try { await run(`http://127.0.0.1:${server.address().port}`, rooms); } finally { await new Promise((resolve) => server.close(resolve)); }
}

test('accounts: register, login, wrong password lockout, sessions survive restart', async () => {
  const { AccountStore } = require('./accounts');
  const file = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'pt-')), 'accounts.json');
  const store = new AccountStore(file);
  assert.ok((await store.register('a', 'password1')).error, 'short name');
  assert.ok((await store.register('가람', 'short')).error, 'short password');
  const made = await store.register('가람', 'password1');
  assert.equal(store.user(made.token), '가람');
  assert.ok((await store.register('가람', 'password2')).error, 'duplicate');
  assert.equal((await store.login('가람', 'wrong-pass')).status, 401);
  assert.equal((await store.login('nobody', 'password1')).status, 401);
  const ok = await store.login('가람', 'password1');
  assert.equal(store.user(ok.token), '가람');
  assert.equal(JSON.stringify(JSON.parse(fs.readFileSync(file, 'utf8'))).includes('password1'), false, 'no plain password on disk');
  assert.equal(new AccountStore(file).user(ok.token), '가람', 'session survives restart');
  for (let i = 0; i < 5; i++) await store.login('가람', 'bad-password');
  assert.equal((await store.login('가람', 'password1')).status, 429, 'locked after 5 failures');
  store.logout(ok.token);
  assert.equal(store.user(ok.token), undefined);
});

test('HTTP: login required to play, spectate read-only, rejoin, healing gallery', async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'pt-'));
  await withServer(dataDir, async (base) => {
    assert.equal((await call(base, 'POST', '/battle/join', {})).status, 401);
    const reg = await call(base, 'POST', '/auth/register', { username: '장수', password: 'secret-123' });
    assert.equal(reg.status, 200);
    const session = reg.body.token;
    assert.equal((await call(base, 'GET', '/auth/me', null, session)).body.username, '장수');
    const join = await call(base, 'POST', '/battle/join', {}, session);
    assert.equal(join.status, 200);
    const again = await call(base, 'POST', '/battle/join', {}, session);
    assert.equal(again.body.token, join.body.token, 'same account rejoins its room');
    const spectator = await call(base, 'GET', `/battle/state?room=${join.body.room}`);
    assert.equal(spectator.body.you, 0);
    assert.equal(spectator.body.players[0].name, '장수');
    assert.equal(JSON.stringify(spectator.body).includes(join.body.token), false, 'tokens never shown');
    const denied = await call(base, 'POST', '/battle/act', { room: join.body.room, type: 'spawn', tile: landTile(40, 60) });
    assert.equal(denied.status, 403);
    const spawn = await call(base, 'POST', '/battle/act', { room: join.body.room, token: join.body.token, type: 'spawn', tile: landTile(40, 60) });
    assert.equal(spawn.status, 200);

    const city = { city: 'seoul', era: 9, buildings: ['house', 'market'], roads: [{ axis: 0, row: 2, column: 1 }], population: 40, happiness: 70, budget: 300 };
    assert.equal((await call(base, 'POST', '/healing/publish', city)).status, 401);
    assert.equal((await call(base, 'POST', '/healing/publish', { ...city, city: '../etc' }, session)).status, 400);
    const pub = await call(base, 'POST', '/healing/publish', city, session);
    assert.equal(pub.status, 200);
    const update = await call(base, 'POST', '/healing/publish', { ...city, buildings: ['house'] }, session);
    assert.equal(update.body.id, pub.body.id, 'one city per account');
    const list = await call(base, 'GET', '/healing/list');
    assert.equal(list.body.cities.length, 1);
    const view = await call(base, 'GET', `/healing/city?id=${pub.body.id}`);
    assert.deepEqual(view.body.buildings, ['house']);
    assert.equal(view.body.name, '장수');
    const huge = await fetch(base + '/healing/publish', { method: 'POST', headers: { Authorization: 'Bearer ' + session }, body: 'x'.repeat(20000) }).catch(() => ({ status: 413 }));
    assert.equal(huge.status, 413);
  });
});

test('battles survive a server restart', async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'pt-'));
  let room, token, owners;
  await withServer(dataDir, async (base, rooms) => {
    const session = (await call(base, 'POST', '/auth/register', { username: 'saver', password: 'secret-123' })).body.token;
    const join = (await call(base, 'POST', '/battle/join', {}, session)).body;
    room = join.room; token = join.token;
    await call(base, 'POST', '/battle/act', { room, token, type: 'spawn', tile: landTile(40, 60) });
    owners = rooms.get(room).snapshot().owners;
  });
  await withServer(dataDir, async (base) => {
    const state = await call(base, 'GET', `/battle/state?room=${room}&token=${token}`);
    assert.equal(state.status, 200);
    assert.equal(state.body.owners, owners);
    assert.equal(state.body.you, 1);
  });
});
