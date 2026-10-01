#!/usr/bin/env node
/**
 * Guardrail against the class of bug that motivated the inline-JS -> TS migration.
 *
 * Razor compiles markup but treats inline <script> JS as opaque text, so a
 * ReSharper reformat (or any typo) can ship a broken/garbled script that throws
 * `Uncaught SyntaxError` and silently kills a whole page's client behaviour.
 *
 * This script enforces two rules across Views/ ** /*.cshtml:
 *   1. No LOGIC-bearing inline <script>. Client logic must live in ClientScripts/*.ts
 *      (which tsc syntax-checks during the build). Inline <script> may only carry a
 *      `window.<page>PageData = { ... }` data bootstrap.
 *   2. Any inline <script> that is pure JS (no Razor @-expressions) must PARSE.
 *
 * Exit code 1 on any violation. Run via `npm run check:views`.
 */
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, relative, join } from 'node:path';
import * as acorn from 'acorn';

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// Manual walk instead of fs.globSync so the guardrail runs on any Node the
// build image ships (globSync needs Node 22+). Scans Views/ and Areas/*/Views/.
function collectViews(dir, acc = []) {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const full = join(dir, entry.name);
        if (entry.isDirectory()) {
            collectViews(full, acc);
        } else if (entry.name.endsWith('.cshtml')) {
            acc.push(relative(webRoot, full));
        }
    }

    return acc;
}

const files = ['Views', 'Areas']
    .map(d => resolve(webRoot, d))
    .flatMap(d => {
        try { return collectViews(d); } catch { return []; }
    });

// A <script> WITHOUT a src attribute, capturing its body.
const inlineScript = /<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/gi;

// DOM / IO that marks a block as "logic" rather than a data bootstrap.
const logicSignals = /\b(addEventListener|fetch\s*\(|querySelector(All)?|getElementById|XMLHttpRequest|\.onclick\b|\.innerHTML\s*=)/;

const violations = [];

for (const rel of files) {
    const text = readFileSync(resolve(webRoot, rel), 'utf8');
    let m;
    while ((m = inlineScript.exec(text)) !== null) {
        const body = m[1];
        const lineNo = text.slice(0, m.index).split('\n').length;
        const where = `${rel.replace(/\\/g, '/')}:${lineNo}`;

        if (logicSignals.test(body)) {
            violations.push(`${where}  — logic-bearing inline <script> (move it to ClientScripts/*.ts and load via <script src>)`);
            continue;
        }

        // Pure-JS bootstrap (no Razor transition) -> must parse.
        if (!body.includes('@')) {
            try {
                acorn.parse(body, { ecmaVersion: 2020 });
            } catch (e) {
                violations.push(`${where}  — inline <script> has a JS syntax error: ${e.message}`);
            }
        }
    }
}

if (violations.length) {
    console.error(`✖ Inline-script guardrail failed (${violations.length}):`);
    for (const v of violations) console.error('  ' + v);
    console.error('\nClient logic belongs in QiadHelper.Web/ClientScripts/*.ts. Inline <script> may only set window.<page>PageData.');
    process.exit(1);
}

console.log(`✔ Inline-script guardrail passed (${files.length} views scanned).`);
