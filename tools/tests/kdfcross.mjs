// Cross-check of 1Key's own BLAKE2b / Argon2id against independent implementations (Codex 15:19 Q3).
// Node's crypto.argon2Sync (OpenSSL) for Argon2id. BLAKE2b with variable output lengths is checked by Python's hashlib in kdfcross.ps1.
// Usage: node kdfcross.mjs <cases-out> <expected-out>   (deterministic: fixed seed, same cases every run)
// Case line formats (also read by 1Key --selftest-kdf):
//   B|dataHex|outLen
//   A|passwordHex|saltHex|t|mKiB|p|tagLen|secretHex|adHex
// Expected file: one line per case - lowercase hex, or "PY" for BLAKE2b lines (Python fills them), or "ERR" where both must refuse.
import { argon2Sync } from 'node:crypto';
import { writeFileSync } from 'node:fs';

let state = 0x1Ce5eed5n;   // xorshift64*
function rnd() { state ^= state >> 12n; state ^= (state << 25n) & 0xFFFFFFFFFFFFFFFFn; state ^= state >> 27n; return Number(((state * 0x2545F4914F6CDD1Dn) & 0xFFFFFFFFFFFFFFFFn) >> 33n); }
function bytes(n) { const b = Buffer.alloc(n); for (let i = 0; i < n; i++) b[i] = rnd() & 0xFF; return b; }
const hex = b => Buffer.from(b).toString('hex');

const cases = [], expected = [];
function argon(pw, salt, t, m, p, len, secret = Buffer.alloc(0), ad = Buffer.alloc(0)) {
  cases.push(['A', hex(pw), hex(salt), t, m, p, len, hex(secret), hex(ad)].join('|'));
  const params = { message: pw, nonce: salt, parallelism: p, tagLength: len, memory: m, passes: t };
  if (secret.length) params.secret = secret;
  if (ad.length) params.associatedData = ad;
  expected.push(hex(argon2Sync('argon2id', params)));
}

// BLAKE2b: input lengths around the 128-byte block, output lengths 1..64 (Python computes expected)
for (const n of [0, 1, 63, 64, 65, 127, 128, 129, 255, 256, 257, 1000])
  for (const out of [1, 32, 63, 64]) { cases.push(['B', hex(bytes(n)), out].join('|')); expected.push('PY'); }

// The supported profile (m=64 MiB, t=3, p=1, 32 bytes): empty / short / long / UTF-8 non-ASCII passwords, salt changes
const prof = (pw, salt) => argon(pw, salt, 3, 65536, 1, 32);
prof(Buffer.alloc(0), bytes(16));
prof(Buffer.from('a'), bytes(16));
prof(Buffer.from('password'), bytes(16));
prof(Buffer.from('마스터 비밀번호 ☃', 'utf8'), bytes(16));
prof(bytes(200), bytes(16));
const same = bytes(12); prof(same, bytes(16)); prof(same, bytes(16));   // same password, different salts

// Smaller memory: lanes 1-4, passes 1-4, tag lengths across H' boundaries (4, 63, 64, 65, 128, 129, 1024), memory not a multiple of 4p
for (const [t, m, p, len] of [[1, 32, 1, 32], [2, 64, 2, 64], [3, 37, 2, 65], [4, 256, 4, 4], [1, 1024, 3, 128], [2, 100, 4, 129], [3, 8, 1, 1024], [2, 513, 2, 63]])
  argon(bytes(1 + (rnd() % 40)), bytes(8 + (rnd() % 24)), t, m, p, len, bytes(rnd() % 3 === 0 ? 0 : 8), bytes(rnd() % 2 === 0 ? 0 : 12));

// Invalid arguments: both must refuse (no output)
cases.push(['A', hex(Buffer.from('x')), hex(bytes(4)), 1, 64, 1, 32, '', ''].join('|')); expected.push('ERR');   // salt shorter than 8
cases.push(['A', hex(Buffer.from('x')), hex(bytes(16)), 1, 7, 1, 32, '', ''].join('|')); expected.push('ERR');  // memory below 8p
cases.push(['A', hex(Buffer.from('x')), hex(bytes(16)), 1, 64, 1, 3, '', ''].join('|')); expected.push('ERR');  // tag shorter than 4
cases.push(['A', hex(Buffer.from('x')), hex(bytes(16)), 0, 64, 1, 32, '', ''].join('|')); expected.push('ERR');  // zero passes

writeFileSync(process.argv[2], cases.join('\n') + '\n');
writeFileSync(process.argv[3], expected.join('\n') + '\n');
