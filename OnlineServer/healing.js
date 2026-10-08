'use strict';
// Healing mode: logged-in players publish a snapshot of one city they built; anyone may watch it read-only.
const fs = require('node:fs');
const crypto = require('node:crypto');

const LIMITS = Object.freeze({ entries: 2000, buildings: 40, roads: 60, keepDays: 30 });
const ID = /^[a-z0-9_-]{1,32}$/;

function cleanName(value) {
  return String(value ?? '').replace(/[\u0000-\u001f\u007f<>]/g, '').trim().slice(0, 16) || '이름 없는 시장';
}
function int(value, min, max) {
  const n = Number(value);
  return Number.isInteger(n) && n >= min && n <= max ? n : null;
}

// Returns a clean snapshot or an error string. Everything from the client is untrusted.
function validate(body) {
  if (!body || typeof body !== 'object') return { error: '잘못된 요청입니다.' };
  if (typeof body.city !== 'string' || !ID.test(body.city)) return { error: '도시 코드가 올바르지 않습니다.' };
  if (!Array.isArray(body.buildings) || body.buildings.length > LIMITS.buildings) return { error: '건물 목록이 올바르지 않습니다.' };
  if (!body.buildings.every((b) => typeof b === 'string' && ID.test(b))) return { error: '건물 코드가 올바르지 않습니다.' };
  if (!Array.isArray(body.roads) || body.roads.length > LIMITS.roads) return { error: '도로 목록이 올바르지 않습니다.' };
  const roads = [];
  for (const r of body.roads) {
    const axis = int(r?.axis, 0, 1), row = int(r?.row, 0, 4), column = int(r?.column, 0, 6);
    if (axis === null || row === null || column === null) return { error: '도로 구간이 올바르지 않습니다.' };
    roads.push({ axis, row, column });
  }
  const era = int(body.era, 0, 20), population = int(body.population, 0, 1e7);
  const happiness = int(body.happiness, 0, 100), budget = int(body.budget, -1e9, 1e9);
  if ([era, population, happiness, budget].includes(null)) return { error: '도시 수치가 올바르지 않습니다.' };
  return { snapshot: { name: cleanName(body.name), city: body.city, era, buildings: [...body.buildings], roads, population, happiness, budget } };
}

class HealingStore {
  constructor(file) {
    this.file = file;
    this.entries = new Map();
    this.saveTimer = null;
    if (file && fs.existsSync(file)) {
      for (const entry of JSON.parse(fs.readFileSync(file, 'utf8'))) this.entries.set(entry.id, entry);
    }
    this.prune();
  }

  prune() {
    const oldest = Date.now() - LIMITS.keepDays * 86400000;
    for (const [id, entry] of this.entries) if (entry.updated < oldest) this.entries.delete(id);
    const byAge = [...this.entries.values()].sort((a, b) => a.updated - b.updated);
    while (byAge.length > LIMITS.entries) this.entries.delete(byAge.shift().id);
  }

  // One public city per account; publishing again replaces it.
  publish(body, owner) {
    const result = validate(body);
    if (result.error) return result;
    const entry = [...this.entries.values()].find((e) => e.owner === owner) || { id: crypto.randomUUID().slice(0, 8), owner };
    const updated = { ...entry, ...result.snapshot, name: owner, updated: Date.now() };
    this.entries.set(updated.id, updated);
    this.prune();
    this.scheduleSave();
    return { id: updated.id };
  }

  list() {
    return [...this.entries.values()]
      .sort((a, b) => b.updated - a.updated)
      .slice(0, 100)
      .map(({ id, name, city, era, buildings, population, happiness, updated }) => ({ id, name, city, era, buildingCount: buildings.length, population, happiness, updated }));
  }

  city(id) {
    const entry = this.entries.get(id);
    if (!entry) return undefined;
    const { tokenHash, ...visible } = entry;
    return visible;
  }

  scheduleSave() {
    if (!this.file || this.saveTimer) return;
    this.saveTimer = setTimeout(() => {
      this.saveTimer = null;
      const temp = this.file + '.tmp';
      fs.writeFileSync(temp, JSON.stringify([...this.entries.values()]));
      fs.renameSync(temp, this.file);
    }, 2000);
    this.saveTimer.unref();
  }
}

module.exports = { HealingStore, validate, LIMITS };
