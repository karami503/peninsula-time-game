'use strict';
// Territory battle on the Korean peninsula grid (OpenFront-style): spawn, expand into empty land,
// attack neighbours with a share of your troops, and develop the military with gold.
const fs = require('node:fs');
const crypto = require('node:crypto');

const RULES = Object.freeze({
  tickMs: 500,
  spawnSeconds: 20,
  maxHumans: 8,
  botCount: 5,
  spawnRadius: 2,
  spawnGap: 8,
  startTroops: 300,
  startGold: 100,
  neutralCost: 2,
  maxAttacks: 6,
  maxTechLevel: 5,
  maxBuildings: 6,
  fortRadius: 5,
  fortDefense: 1.6,
  winShare: 0.8,
  overKeepSeconds: 300,
  idleSeconds: 120,
});

const TECHS = ['attack', 'defense', 'economy'];
const BUILDINGS = ['fort', 'barracks'];
const BOT_NAMES = ['고구려군', '백제군', '신라군', '가야군', '발해군', '탐라군', '부여군'];

function loadGrid(file) {
  const lines = fs.readFileSync(file, 'utf8').trim().split('\n');
  const [w, h, lon0, lat0, lon1, lat1] = lines[0].split(' ').map(Number);
  const land = new Uint8Array(w * h);
  let landCount = 0;
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      if (lines[1 + y][x] === '1') { land[y * w + x] = 1; landCount++; }
    }
  }
  return { w, h, lon0, lat0, lon1, lat1, land, landCount };
}

function techCost(level) { return 150 * 2 ** level; }
function buildingCost(kind, count) { return kind === 'fort' ? 250 + 150 * count : 300 + 200 * count; }
function maxTroops(p) { return 500 + p.tiles * 30 + p.barracks.length * 1500; }

class BattleRoom {
  constructor(id, grid, random = Math.random) {
    this.id = id;
    this.grid = grid;
    this.random = random;
    this.owner = new Uint8Array(grid.w * grid.h);
    this.players = [];
    this.attacks = [];
    this.phase = 'spawn';
    this.tick = 0;
    this.spawnTicks = RULES.spawnSeconds * 1000 / RULES.tickMs;
    this.overTick = 0;
    this.winner = 0;
    this.log = [];
  }

  // Plain data for saving to disk; restore() rebuilds the room after a server restart.
  save() {
    return {
      id: this.id, phase: this.phase, tick: this.tick, spawnTicks: this.spawnTicks, overTick: this.overTick,
      winner: this.winner, log: this.log, owner: Buffer.from(this.owner).toString('base64'),
      players: this.players, attacks: this.attacks,
    };
  }

  static restore(data, grid, random = Math.random) {
    const room = new BattleRoom(data.id, grid, random);
    const owner = Buffer.from(data.owner, 'base64');
    if (owner.length !== room.owner.length) throw new Error('grid size changed');
    room.owner.set(owner);
    Object.assign(room, {
      phase: data.phase, tick: data.tick, spawnTicks: data.spawnTicks, overTick: data.overTick, winner: data.winner,
      log: data.log, players: data.players.map((p) => ({ ...p, seen: data.tick })), attacks: data.attacks,
    });
    return room;
  }

  byAccount(account) { return account ? this.players.find((p) => p.account === account) : undefined; }

  humans() { return this.players.filter((p) => !p.bot); }
  player(index) { return this.players[index - 1]; }
  byToken(token) { return typeof token === 'string' && token ? this.players.find((p) => p.token === token) : undefined; }
  note(text) { this.log.unshift(text); this.log.length = Math.min(this.log.length, 8); }

  addPlayer(name, bot = false, account = '') {
    if (this.phase !== 'spawn') return { error: '이미 시작한 방입니다. 관전만 할 수 있습니다.' };
    if (!bot && this.humans().length >= RULES.maxHumans) return { error: '방이 가득 찼습니다.' };
    const p = {
      index: this.players.length + 1, token: bot ? '' : crypto.randomUUID(), name, bot, account,
      alive: true, spawned: false, troops: RULES.startTroops, gold: RULES.startGold, tiles: 0,
      tech: { attack: 0, defense: 0, economy: 0 }, forts: [], barracks: [], seen: this.tick,
    };
    this.players.push(p);
    return { player: p };
  }

  neighbours(tile) {
    const { w, h } = this.grid;
    const x = tile % w, y = (tile - x) / w;
    const out = [];
    if (x > 0) out.push(tile - 1);
    if (x < w - 1) out.push(tile + 1);
    if (y > 0) out.push(tile - w);
    if (y < h - 1) out.push(tile + w);
    return out;
  }

  distance(a, b) {
    const { w } = this.grid;
    return Math.hypot((a % w) - (b % w), Math.floor(a / w) - Math.floor(b / w));
  }

  spawn(p, tile) {
    if (!Number.isInteger(tile) || tile < 0 || tile >= this.owner.length || !this.grid.land[tile]) return { error: '육지를 고르세요.' };
    for (const other of this.players) {
      if (other !== p && other.spawned && this.distance(other.capital, tile) < RULES.spawnGap) return { error: '다른 세력과 너무 가깝습니다.' };
    }
    if (p.spawned) this.release(p);
    const { w } = this.grid;
    const r = RULES.spawnRadius;
    for (let dy = -r; dy <= r; dy++) {
      for (let dx = -r; dx <= r; dx++) {
        const x = (tile % w) + dx, y = Math.floor(tile / w) + dy;
        if (x < 0 || y < 0 || x >= w || y >= this.grid.h || dx * dx + dy * dy > r * r) continue;
        const t = y * w + x;
        if (this.grid.land[t] && this.owner[t] === 0) { this.owner[t] = p.index; p.tiles++; }
      }
    }
    p.spawned = true;
    p.capital = tile;
    return { ok: true };
  }

  release(p) {
    for (let t = 0; t < this.owner.length; t++) if (this.owner[t] === p.index) this.owner[t] = 0;
    p.tiles = 0;
  }

  randomSpawn(p) {
    for (let attempt = 0; attempt < 400; attempt++) {
      const tile = Math.floor(this.random() * this.owner.length);
      if (this.grid.land[tile] && this.owner[tile] === 0 && this.spawn(p, tile).ok) return true;
    }
    return false;
  }

  begin() {
    const bots = Math.max(0, RULES.botCount - Math.max(0, this.humans().length - 1));
    for (let i = 0; i < bots; i++) this.addPlayer(BOT_NAMES[i % BOT_NAMES.length], true);
    for (const p of this.players) if (!p.spawned && !this.randomSpawn(p)) p.alive = false;
    this.phase = 'play';
    this.note('전쟁이 시작되었습니다.');
  }

  // Tiles owned by target (0 = empty land) that touch the attacker's territory.
  frontier(attacker, target) {
    const out = [];
    for (let t = 0; t < this.owner.length; t++) {
      if (!this.grid.land[t] || this.owner[t] !== target) continue;
      if (this.neighbours(t).some((n) => this.owner[n] === attacker)) out.push(t);
    }
    return out;
  }

  tileCost(att, target, tile) {
    if (target === 0) return RULES.neutralCost;
    const def = this.player(target);
    const density = def.troops / Math.max(1, def.tiles);
    const fort = def.forts.some((f) => this.distance(f, tile) <= RULES.fortRadius) ? RULES.fortDefense : 1;
    const defence = (1 + 0.15 * def.tech.defense) * fort;
    return Math.max(4, density * 1.5) * defence / (1 + 0.15 * att.tech.attack);
  }

  attack(p, tile, ratio) {
    if (this.phase !== 'play') return { error: '전쟁이 시작된 뒤 공격할 수 있습니다.' };
    if (!Number.isInteger(tile) || tile < 0 || tile >= this.owner.length || !this.grid.land[tile]) return { error: '육지를 고르세요.' };
    const target = this.owner[tile];
    if (target === p.index) return { error: '내 영토입니다.' };
    if (target !== 0 && !this.player(target).alive) return { error: '이미 멸망한 세력입니다.' };
    if (this.frontier(p.index, target).length === 0) return { error: '국경이 맞닿아 있지 않습니다.' };
    const share = Math.min(1, Math.max(0.05, Number(ratio) || 0.3));
    const troops = Math.floor(p.troops * share);
    if (troops < 5) return { error: '병력이 부족합니다.' };
    const existing = this.attacks.find((a) => a.from === p.index && a.target === target);
    if (!existing && this.attacks.filter((a) => a.from === p.index).length >= RULES.maxAttacks) return { error: '동시에 진행할 수 있는 공격이 가득 찼습니다.' };
    p.troops -= troops;
    if (existing) existing.troops += troops;
    else this.attacks.push({ from: p.index, target, troops });
    return { ok: true };
  }

  develop(p, kind) {
    if (!TECHS.includes(kind)) return { error: '알 수 없는 기술입니다.' };
    const level = p.tech[kind];
    if (level >= RULES.maxTechLevel) return { error: '최고 단계입니다.' };
    if (p.gold < techCost(level)) return { error: '금이 부족합니다.' };
    p.gold -= techCost(level);
    p.tech = { ...p.tech, [kind]: level + 1 };
    return { ok: true };
  }

  build(p, kind, tile) {
    if (!BUILDINGS.includes(kind)) return { error: '알 수 없는 시설입니다.' };
    if (this.phase !== 'play') return { error: '전쟁이 시작된 뒤 건설할 수 있습니다.' };
    if (!Number.isInteger(tile) || tile < 0 || tile >= this.owner.length || this.owner[tile] !== p.index) return { error: '내 영토를 고르세요.' };
    const list = kind === 'fort' ? p.forts : p.barracks;
    if (list.length >= RULES.maxBuildings) return { error: '더 지을 수 없습니다.' };
    if (p.forts.includes(tile) || p.barracks.includes(tile)) return { error: '이미 시설이 있습니다.' };
    const cost = buildingCost(kind, list.length);
    if (p.gold < cost) return { error: '금이 부족합니다.' };
    p.gold -= cost;
    if (kind === 'fort') p.forts = [...p.forts, tile]; else p.barracks = [...p.barracks, tile];
    return { ok: true };
  }

  act(p, body) {
    if (!p.alive) return { error: '멸망한 세력은 관전만 할 수 있습니다.' };
    if (this.phase === 'over') return { error: '전쟁이 끝났습니다.' };
    switch (body.type) {
      case 'spawn': return this.phase === 'spawn' ? this.spawn(p, body.tile) : { error: '배치 시간이 끝났습니다.' };
      case 'attack': return this.attack(p, body.tile, body.ratio);
      case 'tech': return this.develop(p, body.kind);
      case 'build': return this.build(p, body.kind, body.tile);
      default: return { error: '알 수 없는 명령입니다.' };
    }
  }

  conquer(tile, att, target) {
    this.owner[tile] = att.index;
    att.tiles++;
    if (target === 0) return;
    const def = this.player(target);
    def.tiles--;
    def.forts = def.forts.filter((t) => t !== tile);
    def.barracks = def.barracks.filter((t) => t !== tile);
  }

  stepAttack(a) {
    const att = this.player(a.from);
    if (!att.alive || (a.target !== 0 && !this.player(a.target).alive)) return false;
    const front = this.frontier(a.from, a.target);
    if (front.length === 0) return false;
    const limit = 3 + att.tech.attack;
    for (let n = 0; n < limit && front.length > 0; n++) {
      const pick = Math.floor(this.random() * front.length);
      const tile = front[pick];
      front[pick] = front[front.length - 1];
      front.pop();
      const cost = this.tileCost(att, a.target, tile);
      if (a.troops < cost) return false;
      a.troops -= cost;
      if (a.target !== 0) {
        const def = this.player(a.target);
        def.troops = Math.max(0, def.troops - cost * 0.6);
      }
      this.conquer(tile, att, a.target);
    }
    return a.troops >= 1;
  }

  stepBots() {
    for (const bot of this.players) {
      if (!bot.bot || !bot.alive || (this.tick + bot.index) % 3 !== 0) continue;
      if (bot.gold > 600) this.develop(bot, TECHS[Math.floor(this.random() * TECHS.length)]);
      if (bot.troops < 0.5 * maxTroops(bot)) continue;
      const borders = new Map();
      for (let t = 0; t < this.owner.length; t++) {
        if (this.owner[t] !== bot.index) continue;
        for (const n of this.neighbours(t)) {
          if (this.grid.land[n] && this.owner[n] !== bot.index && !borders.has(this.owner[n])) borders.set(this.owner[n], n);
        }
      }
      if (borders.has(0)) { this.attack(bot, borders.get(0), 0.25); continue; }
      let best = null;
      for (const [target, tile] of borders) {
        const enemy = this.player(target);
        if (enemy.troops < bot.troops * 0.8 && (!best || enemy.troops < best.troops)) best = { troops: enemy.troops, tile };
      }
      if (best) this.attack(bot, best.tile, 0.35);
    }
  }

  step() {
    this.tick++;
    if (this.phase === 'spawn') {
      if (this.tick >= this.spawnTicks) this.begin();
      return;
    }
    if (this.phase !== 'play') return;
    for (const p of this.players) {
      if (!p.alive) continue;
      const cap = maxTroops(p);
      const growth = (3 + p.tiles * 0.12) * (1 + 0.25 * p.barracks.length) * Math.max(0.05, 1 - p.troops / cap);
      p.troops = Math.min(cap, p.troops + growth);
      p.gold += (1 + p.tiles * 0.03) * (1 + 0.2 * p.tech.economy);
    }
    this.attacks = this.attacks.filter((a) => {
      const going = this.stepAttack(a);
      if (!going) this.player(a.from).troops += Math.max(0, Math.floor(a.troops));
      return going;
    });
    this.stepBots();
    for (const p of this.players) {
      if (p.alive && p.spawned && p.tiles <= 0) { p.alive = false; p.troops = 0; this.note(p.name + ' 세력이 멸망했습니다.'); }
    }
    const alive = this.players.filter((p) => p.alive);
    const leader = alive.reduce((best, p) => (!best || p.tiles > best.tiles ? p : best), null);
    if (alive.length <= 1 || (leader && leader.tiles >= this.grid.landCount * RULES.winShare)) {
      this.phase = 'over';
      this.overTick = this.tick;
      this.winner = leader ? leader.index : 0;
      this.note(leader ? leader.name + ' 세력이 한반도를 통일했습니다.' : '전쟁이 끝났습니다.');
    }
  }

  expired() {
    const ticksPerSecond = 1000 / RULES.tickMs;
    if (this.phase === 'over') return this.tick - this.overTick > RULES.overKeepSeconds * ticksPerSecond;
    return this.humans().every((p) => this.tick - p.seen > RULES.idleSeconds * ticksPerSecond);
  }

  snapshot(viewer) {
    let owners = '';
    for (let t = 0; t < this.owner.length; t++) owners += String.fromCharCode(48 + this.owner[t]);
    return {
      id: this.id,
      phase: this.phase,
      tick: this.tick,
      spawnSeconds: this.phase === 'spawn' ? Math.ceil((this.spawnTicks - this.tick) * RULES.tickMs / 1000) : 0,
      you: viewer ? viewer.index : 0,
      winner: this.winner,
      width: this.grid.w,
      height: this.grid.h,
      owners,
      players: this.players.map((p) => ({
        index: p.index, name: p.name, bot: p.bot, alive: p.alive,
        troops: Math.floor(p.troops), maxTroops: maxTroops(p), gold: Math.floor(p.gold), tiles: p.tiles,
        attackLevel: p.tech.attack, defenseLevel: p.tech.defense, economyLevel: p.tech.economy,
        forts: p.forts, barracks: p.barracks,
      })),
      attacks: this.attacks.map((a) => ({ from: a.from, target: a.target, troops: Math.floor(a.troops) })),
      log: this.log,
    };
  }
}

module.exports = { RULES, BattleRoom, loadGrid, techCost, buildingCost, maxTroops };
