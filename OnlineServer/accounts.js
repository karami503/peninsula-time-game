'use strict';
// Player accounts: scrypt password hashes, bearer sessions stored as SHA-256 hashes, and a lockout
// after repeated wrong passwords. Persisted to one JSON file so logins survive a server restart.
const fs = require('node:fs');
const crypto = require('node:crypto');

const USERNAME = /^[가-힣A-Za-z0-9_]{2,16}$/;
const LIMITS = Object.freeze({ minPassword: 8, maxPassword: 72, sessionDays: 30, maxFailures: 5, lockSeconds: 60, keyLength: 64 });

function sha256(text) { return crypto.createHash('sha256').update(text).digest('hex'); }
function scrypt(password, salt) {
  return new Promise((resolve, reject) => crypto.scrypt(password, salt, LIMITS.keyLength, (error, key) => (error ? reject(error) : resolve(key))));
}

class AccountStore {
  constructor(file, now = Date.now) {
    this.file = file;
    this.now = now;
    this.users = new Map();
    this.sessions = new Map();
    this.failures = new Map();
    if (file && fs.existsSync(file)) {
      const saved = JSON.parse(fs.readFileSync(file, 'utf8'));
      for (const user of saved.users || []) this.users.set(user.username.toLowerCase(), user);
      for (const session of saved.sessions || []) if (session.expires > this.now()) this.sessions.set(session.hash, session);
    }
  }

  checkPassword(password) {
    if (typeof password !== 'string' || password.length < LIMITS.minPassword) return `비밀번호는 ${LIMITS.minPassword}자 이상이어야 합니다.`;
    if (password.length > LIMITS.maxPassword) return '비밀번호가 너무 깁니다.';
    return null;
  }

  async register(username, password) {
    if (typeof username !== 'string' || !USERNAME.test(username)) return { error: '아이디는 한글·영문·숫자·_ 2~16자입니다.' };
    const problem = this.checkPassword(password);
    if (problem) return { error: problem };
    if (this.users.has(username.toLowerCase())) return { error: '이미 있는 아이디입니다.' };
    const salt = crypto.randomBytes(16).toString('hex');
    const hash = (await scrypt(password, salt)).toString('hex');
    this.users.set(username.toLowerCase(), { username, salt, hash, created: this.now() });
    return this.openSession(username);
  }

  async login(username, password) {
    const key = typeof username === 'string' ? username.toLowerCase() : '';
    const failed = this.failures.get(key);
    if (failed && failed.count >= LIMITS.maxFailures && this.now() - failed.last < LIMITS.lockSeconds * 1000) {
      return { error: '로그인 실패가 많습니다. 1분 뒤 다시 시도하세요.', status: 429 };
    }
    const user = this.users.get(key);
    // Hash even for unknown users so response time does not reveal which names exist.
    const salt = user ? user.salt : '00000000000000000000000000000000';
    const actual = await scrypt(typeof password === 'string' ? password.slice(0, LIMITS.maxPassword) : '', salt);
    const expected = user ? Buffer.from(user.hash, 'hex') : Buffer.alloc(LIMITS.keyLength);
    if (!user || !crypto.timingSafeEqual(actual, expected)) {
      const count = failed && this.now() - failed.last < LIMITS.lockSeconds * 1000 ? failed.count + 1 : 1;
      this.failures.set(key, { count, last: this.now() });
      return { error: '아이디 또는 비밀번호가 맞지 않습니다.', status: 401 };
    }
    this.failures.delete(key);
    return this.openSession(user.username);
  }

  openSession(username) {
    const token = crypto.randomBytes(32).toString('hex');
    this.sessions.set(sha256(token), { hash: sha256(token), username, expires: this.now() + LIMITS.sessionDays * 86400000 });
    this.save();
    return { token, username };
  }

  // Username for a bearer token, or undefined.
  user(token) {
    if (typeof token !== 'string' || token.length === 0) return undefined;
    const session = this.sessions.get(sha256(token));
    if (!session) return undefined;
    if (session.expires <= this.now()) { this.sessions.delete(session.hash); return undefined; }
    return session.username;
  }

  logout(token) {
    if (typeof token === 'string' && this.sessions.delete(sha256(token))) this.save();
  }

  save() {
    if (!this.file) return;
    const temp = this.file + '.tmp';
    fs.writeFileSync(temp, JSON.stringify({ users: [...this.users.values()], sessions: [...this.sessions.values()] }), { mode: 0o600 });
    fs.renameSync(temp, this.file);
  }
}

module.exports = { AccountStore, LIMITS };
