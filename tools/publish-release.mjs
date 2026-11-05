import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { pathToFileURL } from 'node:url';
import { stableVersion } from './release-policy.mjs';

export const RELEASE_FILES = ['codex-usage-overlay-windows-x64.zip', 'SHA256SUMS.txt', 'codex-usage-overlay.intoto.jsonl'];
const digest = bytes => `sha256:${crypto.createHash('sha256').update(bytes).digest('hex')}`;

export function releaseAssets(directory) {
  const assets = RELEASE_FILES.map(name => {
    const file = path.join(directory, name);
    const stat = fs.lstatSync(file);
    if (!stat.isFile() || stat.isSymbolicLink() || stat.size === 0) throw new Error(`Invalid release file: ${name}`);
    const bytes = fs.readFileSync(file);
    return { name, bytes, size: bytes.length, digest: digest(bytes) };
  });
  const lines = assets[1].bytes.toString('utf8').trim().split(/\r?\n/);
  if (lines.length !== 2) throw new Error('Expected the ZIP and EXE checksums.');
  const expected = new Map();
  for (const line of lines) {
    const match = /^([0-9a-f]{64})  (codex-usage-overlay-windows-x64\.zip|codex-usage-overlay\/UsageOverlay\.exe)$/.exec(line);
    if (!match || expected.has(match[2])) throw new Error('Invalid or duplicate release checksum.');
    expected.set(match[2], `sha256:${match[1]}`);
  }
  if (expected.get(assets[0].name) !== assets[0].digest) throw new Error('Release ZIP checksum mismatch.');
  return assets;
}

export async function tagCommit(client, prefix, tag) {
  let object = (await client.request('GET', `${prefix}/git/ref/tags/${encodeURIComponent(tag)}`)).object;
  for (let depth = 0; depth < 8; depth++) {
    if (!object || !/^[0-9a-f]{40}$/.test(object.sha || '')) throw new Error('Invalid remote tag object.');
    if (object.type === 'commit') return object.sha;
    if (object.type !== 'tag') throw new Error('Remote tag does not resolve to a commit.');
    object = (await client.request('GET', `${prefix}/git/tags/${object.sha}`)).object;
  }
  throw new Error('Remote tag nesting exceeds the supported limit.');
}

function verifyAssets(actual, expected) {
  if (!Array.isArray(actual) || actual.length !== expected.length) throw new Error('Release asset count mismatch.');
  const ids = new Set();
  for (const wanted of expected) {
    const matches = actual.filter(item => item.name === wanted.name);
    const item = matches[0];
    if (matches.length !== 1 || !Number.isSafeInteger(item.id) || item.id <= 0 || ids.has(item.id)
      || item.state !== 'uploaded' || item.size !== wanted.size || item.digest !== wanted.digest)
      throw new Error(`Release asset verification failed: ${wanted.name}`);
    ids.add(item.id);
  }
}

async function assertUnusedTag(client, prefix, tag) {
  for (let page = 1; page <= 100; page++) {
    const releases = await client.request('GET', `${prefix}/releases?per_page=100&page=${page}`);
    if (!Array.isArray(releases)) throw new Error('Invalid releases response.');
    if (releases.some(item => item.tag_name === tag)) throw new Error('A release or draft already exists for this tag; no files were replaced.');
    if (releases.length < 100) return;
  }
  throw new Error('Could not establish that the release tag is unused.');
}

// The injected client makes ordering/error paths testable without publishing.
// Never replaces/deletes releases, assets or tags, including an incomplete draft.
export async function publishRelease({ repo, tag, sha, assets, runId }, client) {
  stableVersion(tag);
  const validRepo = typeof repo === 'string' && /^[\w.-]+\/[\w.-]+$/.test(repo)
    && repo.split('/').every(part => part !== '.' && part !== '..');
  if (!validRepo || !/^[0-9a-f]{40}$/.test(sha || '') || !/^\d+$/.test(String(runId)))
    throw new Error('Invalid release repository, commit or run ID.');
  if (assets.length !== RELEASE_FILES.length || assets.some((asset, index) => asset.name !== RELEASE_FILES[index]))
    throw new Error('Unexpected release asset selection.');
  const prefix = `/repos/${repo}`;
  if (await tagCommit(client, prefix, tag) !== sha) throw new Error('Remote tag moved away from the attested source.');
  await assertUnusedTag(client, prefix, tag);
  const notes = [
    `Windows x64 portable build for ${tag}.`, '', `Source commit: \`${sha}\`.`,
    `Build and provenance: https://github.com/${repo}/actions/runs/${runId}`, '',
    'Download and extract the portable ZIP; keep the whole folder together.',
    'Node.js and an authenticated Codex CLI must already be installed on the target PC.',
    'GitHub build provenance is included; this is not Windows Authenticode signing.', '',
    'Verify the ZIP with:', '```shell',
    `gh attestation verify codex-usage-overlay-windows-x64.zip --repo ${repo} --bundle codex-usage-overlay.intoto.jsonl --source-ref refs/tags/${tag} --source-digest ${sha} --signer-workflow ${repo}/.github/workflows/windows-build.yml --deny-self-hosted-runners`,
    '```'
  ].join('\n');
  const draft = await client.request('POST', `${prefix}/releases`, {
    tag_name: tag, target_commitish: sha, name: `Codex Usage Overlay ${tag}`,
    body: notes, draft: true, prerelease: false
  });
  if (!Number.isSafeInteger(draft.id) || draft.id <= 0 || draft.tag_name !== tag || draft.draft !== true)
    throw new Error('Unexpected draft release response; publication stopped.');
  const releasePath = `${prefix}/releases/${draft.id}`;
  try {
    const uploadBase = new URL(draft.upload_url.replace(/\{.*$/, ''));
    if (uploadBase.origin !== 'https://uploads.github.com'
      || uploadBase.pathname.toLowerCase() !== `${releasePath}/assets`.toLowerCase())
      throw new Error('Unexpected release upload host or path.');
    for (const asset of assets) {
      const url = new URL(uploadBase); url.searchParams.set('name', asset.name);
      await client.upload(url, asset.bytes);
    }
    const stored = await client.request('GET', releasePath);
    if (stored.id !== draft.id || stored.tag_name !== tag || stored.draft !== true)
      throw new Error('The created draft changed before publication.');
    verifyAssets(await client.request('GET', `${releasePath}/assets?per_page=100`), assets);
    if (await tagCommit(client, prefix, tag) !== sha) throw new Error('Remote tag moved before publication.');
    const published = await client.request('PATCH', releasePath, { draft: false, make_latest: 'legacy' });
    if (published.id !== draft.id || published.tag_name !== tag || published.draft !== false)
      throw new Error('Published release response did not match the created draft.');
    return published;
  } catch (error) {
    throw new Error(`Release ${draft.id} was not confirmed published. Existing files were not replaced; inspect the release state before retrying. ${error.message}`);
  }
}

function githubClient(token) {
  if (!token) throw new Error('A scoped GitHub Actions token is required.');
  const headers = { authorization: `Bearer ${token}`, accept: 'application/vnd.github+json',
    'x-github-api-version': '2026-03-10', 'user-agent': 'codex-usage-overlay-release' };
  async function fetchJson(url, options) {
    const response = await fetch(url, { ...options, headers: { ...headers, ...options.headers },
      redirect: 'error', signal: AbortSignal.timeout(60000) });
    if (!response.ok) throw new Error(`GitHub request failed with HTTP ${response.status}.`);
    return response.json();
  }
  return {
    request: (method, endpoint, body) => fetchJson(`https://api.github.com${endpoint}`, {
      method, headers: body ? { 'content-type': 'application/json' } : {}, body: body ? JSON.stringify(body) : undefined
    }),
    upload: (url, bytes) => fetchJson(url, { method: 'POST', headers: { 'content-type': 'application/octet-stream' }, body: bytes })
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    if (process.env.GITHUB_EVENT_NAME !== 'push' || process.env.GITHUB_REF !== `refs/tags/${process.env.GITHUB_REF_NAME}`)
      throw new Error('Release publication requires a version-tag push.');
    const release = await publishRelease({ repo: process.env.GITHUB_REPOSITORY, tag: process.env.GITHUB_REF_NAME,
      sha: process.env.RELEASE_SOURCE_SHA, runId: process.env.GITHUB_RUN_ID,
      assets: releaseAssets(process.argv[2] || 'release') }, githubClient(process.env.GH_TOKEN));
    console.log(JSON.stringify({ tag: release.tag_name, url: release.html_url, id: release.id }));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
