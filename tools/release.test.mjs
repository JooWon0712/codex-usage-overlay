import { test } from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { stableVersion, assemblyVersion, releaseMetadata } from './release-policy.mjs';
import { RELEASE_FILES, releaseAssets, tagCommit, publishRelease } from './publish-release.mjs';

const SHA = 'a'.repeat(40), OTHER_SHA = 'b'.repeat(40);
const REPO = 'example/codex-usage-overlay', TAG = 'v1.2.3';
const PREFIX = `/repos/${REPO}`, RELEASE_ID = 71;
const SOURCE = '[assembly: System.Reflection.AssemblyVersion("1.2.3.0")]\n'
  + '[assembly: System.Reflection.AssemblyFileVersion("1.2.3.0")]';
const ENV = { GITHUB_EVENT_NAME: 'push', GITHUB_REF: `refs/tags/${TAG}`, GITHUB_SHA: SHA };
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const assets = () => RELEASE_FILES.map((name, index) => {
  const bytes = Buffer.from(`synthetic release fixture ${index}`);
  return { name, bytes, size: bytes.length, digest: `sha256:${hash(bytes)}` };
});

function gitFixture(overrides = {}) {
  const calls = [];
  const git = args => {
    calls.push([...args]);
    if (args[0] === 'rev-parse' && args[1] === 'HEAD') return overrides.head ?? SHA;
    if (args[0] === 'rev-parse' && args[1].endsWith('^{commit}')) return overrides.source ?? SHA;
    if (args[0] === 'merge-base') {
      if (overrides.notOnMain) throw new Error('not an ancestor');
      return '';
    }
    throw new Error(`Unexpected synthetic git command: ${args.join(' ')}`);
  };
  return { git, calls };
}

function publisherFixture(options = {}) {
  const calls = [], uploaded = [];
  let tagReads = 0;
  const draft = { id: RELEASE_ID, tag_name: TAG, draft: true,
    upload_url: `https://uploads.github.com${PREFIX}/releases/${RELEASE_ID}/assets{?name,label}`,
    ...options.draft };
  const client = {
    async request(method, endpoint, body) {
      calls.push({ method, endpoint, body });
      if (method === 'GET' && endpoint === `${PREFIX}/git/ref/tags/${TAG}`) {
        tagReads++;
        return { object: { type: 'commit', sha: tagReads === 1 ? options.initialTag ?? SHA : options.finalTag ?? SHA } };
      }
      if (method === 'GET' && endpoint.startsWith(`${PREFIX}/releases?`)) {
        const page = Number(new URL(`https://fixture.invalid${endpoint}`).searchParams.get('page'));
        return options.pages?.[page - 1] ?? [];
      }
      if (method === 'POST' && endpoint === `${PREFIX}/releases`) return { ...draft };
      if (method === 'GET' && endpoint === `${PREFIX}/releases/${RELEASE_ID}`)
        return { ...draft, ...options.stored };
      if (method === 'GET' && endpoint === `${PREFIX}/releases/${RELEASE_ID}/assets?per_page=100`) {
        const actual = uploaded.map(item => ({ ...item }));
        return options.mutateAssets ? options.mutateAssets(actual) : actual;
      }
      if (method === 'PATCH' && endpoint === `${PREFIX}/releases/${RELEASE_ID}`)
        return { id: RELEASE_ID, tag_name: TAG, draft: false, html_url: 'https://fixture.invalid/release', ...options.published };
      throw new Error(`Unexpected synthetic API call: ${method} ${endpoint}`);
    },
    async upload(url, bytes) {
      calls.push({ method: 'UPLOAD', endpoint: url.toString() });
      if (options.failUploadAt === uploaded.length) throw new Error('synthetic upload failed');
      uploaded.push({ id: 900 + uploaded.length, name: url.searchParams.get('name'),
        state: 'uploaded', size: bytes.length, digest: `sha256:${hash(bytes)}` });
      return uploaded.at(-1);
    },
  };
  return { client, calls, uploaded, draft,
    publish: (overrides = {}) => publishRelease({ repo: REPO, tag: TAG, sha: SHA, assets: assets(), runId: '1234', ...overrides }, client) };
}

const mutations = calls => calls.filter(call => !['GET'].includes(call.method));
const published = calls => calls.some(call => call.method === 'PATCH');

test('stable tags accept exact stable semver and reject prereleases, padding and leading zeroes', () => {
  for (const tag of ['v0.0.0', 'v1.2.3', 'v12.345.678']) assert.equal(stableVersion(tag), tag.slice(1));
  for (const tag of [undefined, null, '', '1.2.3', 'v01.2.3', 'v1.02.3', 'v1.2.03', 'v1.2',
    'v1.2.3.0', 'v1.2.3-rc.1', 'v1.2.3+build', ' v1.2.3', 'v1.2.3 ', 'v1.2.3\n']) {
    assert.throws(() => stableVersion(tag), /stable vMAJOR/);
  }
});

test('assembly metadata requires exactly one matching file and assembly version', () => {
  assert.equal(assemblyVersion(SOURCE), '1.2.3.0');
  for (const source of ['', SOURCE.replace('AssemblyFileVersion', 'MissingVersion'),
    SOURCE.replace('AssemblyFileVersion("1.2.3.0")', 'AssemblyFileVersion("1.2.4.0")'), SOURCE + SOURCE]) {
    assert.throws(() => assemblyVersion(source), /Exactly one matching/);
  }
});

test('release metadata binds the tag version, workflow commit and origin main ancestry', () => {
  const fixture = gitFixture();
  assert.deepEqual(releaseMetadata(ENV, SOURCE, fixture.git), {
    'is-release': 'true', 'file-version': '1.2.3.0', 'source-sha': SHA,
  });
  assert.deepEqual(fixture.calls, [
    ['rev-parse', 'HEAD'], ['rev-parse', `${SHA}^{commit}`],
    ['merge-base', '--is-ancestor', SHA, 'refs/remotes/origin/main'],
  ]);
  const branch = gitFixture();
  assert.equal(releaseMetadata({ ...ENV, GITHUB_REF: 'refs/heads/main' }, SOURCE, branch.git)['is-release'], 'false');
  assert.equal(branch.calls.some(call => call[0] === 'merge-base'), false);
  // An annotated workflow ref must be peeled by Git, rather than compared as a tag object ID.
  const annotated = gitFixture();
  assert.equal(releaseMetadata({ ...ENV, GITHUB_SHA: OTHER_SHA }, SOURCE, annotated.git)['source-sha'], SHA);
  assert.ok(annotated.calls.some(call => call[1] === `${OTHER_SHA}^{commit}`));
});

test('untrusted release context, mismatching checkout/version and non-main commits are refused', () => {
  for (const env of [{ ...ENV, GITHUB_EVENT_NAME: 'workflow_dispatch' },
    { ...ENV, GITHUB_REF: 'refs/tags/v1.2.4' }, { ...ENV, GITHUB_REF: 'refs/tags/v1.2.3-rc.1' }]) {
    assert.throws(() => releaseMetadata(env, SOURCE, gitFixture().git));
  }
  assert.throws(() => releaseMetadata(ENV, SOURCE, gitFixture({ source: OTHER_SHA }).git), /differs/);
  assert.throws(() => releaseMetadata(ENV, SOURCE, gitFixture({ head: 'invalid' }).git), /differs/);
  assert.throws(() => releaseMetadata(ENV, SOURCE, gitFixture({ notOnMain: true }).git), /not an ancestor/);
});

test('release assets bind the nonempty local files to the ZIP checksum', t => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'codex-overlay-release-test-'));
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const zip = Buffer.from('synthetic portable ZIP bytes');
  fs.writeFileSync(path.join(directory, RELEASE_FILES[0]), zip);
  fs.writeFileSync(path.join(directory, RELEASE_FILES[1]), `${hash(zip)}  ${RELEASE_FILES[0]}\n${'c'.repeat(64)}  codex-usage-overlay/UsageOverlay.exe\n`);
  fs.writeFileSync(path.join(directory, RELEASE_FILES[2]), '{"synthetic":"attestation"}\n');
  const selected = releaseAssets(directory);
  assert.deepEqual(selected.map(item => item.name), RELEASE_FILES);
  assert.equal(selected[0].digest, `sha256:${hash(zip)}`);
  fs.appendFileSync(path.join(directory, RELEASE_FILES[0]), 'tampered');
  assert.throws(() => releaseAssets(directory), /checksum mismatch/);
  fs.writeFileSync(path.join(directory, RELEASE_FILES[2]), '');
  assert.throws(() => releaseAssets(directory), /Invalid release file/);
});

test('publication creates a draft, verifies all uploaded digests and publishes only the created release ID', async () => {
  const fixture = publisherFixture();
  const result = await fixture.publish();
  assert.equal(result.id, RELEASE_ID);
  assert.equal(result.draft, false);
  const create = fixture.calls.find(call => call.method === 'POST');
  assert.equal(create.body.draft, true);
  assert.equal(create.body.prerelease, false);
  assert.equal(create.body.target_commitish, SHA);
  assert.ok(create.body.body.includes(`--source-digest ${SHA}`));
  assert.ok(create.body.body.includes('not Windows Authenticode signing'));
  const uploads = fixture.calls.filter(call => call.method === 'UPLOAD');
  assert.deepEqual(uploads.map(call => new URL(call.endpoint).searchParams.get('name')), RELEASE_FILES);
  assert.ok(uploads.every(call => new URL(call.endpoint).origin === 'https://uploads.github.com'));
  assert.deepEqual(fixture.calls.at(-1), { method: 'PATCH', endpoint: `${PREFIX}/releases/${RELEASE_ID}`,
    body: { draft: false, make_latest: 'legacy' } });
  const assetCheck = fixture.calls.findIndex(call => call.endpoint.endsWith('/assets?per_page=100'));
  assert.ok(assetCheck > fixture.calls.findLastIndex(call => call.method === 'UPLOAD'));
  assert.equal(fixture.calls.at(-2).endpoint, `${PREFIX}/git/ref/tags/${TAG}`);
  assert.equal(fixture.calls.filter(call => call.method === 'PATCH').length, 1);
  assert.equal(fixture.calls.some(call => call.method === 'DELETE'), false);
});

test('existing published releases and drafts reject without creating, deleting or uploading anything', async () => {
  for (const draft of [true, false]) {
    const fixture = publisherFixture({ pages: [[{ tag_name: TAG, draft }]] });
    await assert.rejects(fixture.publish(), /already exists/);
    assert.deepEqual(mutations(fixture.calls), []);
  }
  const fixture = publisherFixture({ pages: [Array.from({ length: 100 }, (_, index) => ({ tag_name: `other-${index}` })), [{ tag_name: TAG, draft: true }]] });
  await assert.rejects(fixture.publish(), /already exists/);
  assert.ok(fixture.calls.some(call => call.endpoint.endsWith('page=2')));
  assert.deepEqual(mutations(fixture.calls), []);
});

test('untrusted upload hosts and paths leave the created draft without uploading or publishing', async () => {
  for (const upload_url of ['https://uploads.github.com.evil.invalid/assets', 'http://uploads.github.com/assets',
    `https://uploads.github.com${PREFIX}/releases/999/assets`, 'https://api.github.com/uploads']) {
    const fixture = publisherFixture({ draft: { upload_url } });
    await assert.rejects(fixture.publish(), /upload host or path/);
    assert.deepEqual(mutations(fixture.calls).map(call => call.method), ['POST']);
  }
});

test('a failed upload keeps an inspectable draft and never retries, deletes assets or publishes it', async () => {
  const fixture = publisherFixture({ failUploadAt: 1 });
  await assert.rejects(fixture.publish(), /not confirmed published.*synthetic upload failed/);
  assert.equal(fixture.uploaded.length, 1);
  assert.deepEqual(mutations(fixture.calls).map(call => call.method), ['POST', 'UPLOAD', 'UPLOAD']);
  assert.equal(fixture.draft.draft, true);
});

test('missing, duplicate, unfinished, wrong-size or wrong-digest assets all prevent publication', async () => {
  const scenarios = {
    missing: actual => actual.slice(1),
    extra: actual => [...actual, { ...actual[0], id: 999 }],
    duplicateName: actual => [{ ...actual[0] }, { ...actual[1], name: actual[0].name }, actual[2]],
    duplicateId: actual => [{ ...actual[0] }, { ...actual[1], id: actual[0].id }, actual[2]],
    invalidId: actual => [{ ...actual[0], id: 0 }, ...actual.slice(1)],
    unfinished: actual => [{ ...actual[0], state: 'new' }, ...actual.slice(1)],
    size: actual => [{ ...actual[0], size: actual[0].size + 1 }, ...actual.slice(1)],
    digest: actual => [{ ...actual[0], digest: `sha256:${'0'.repeat(64)}` }, ...actual.slice(1)],
  };
  for (const [name, mutateAssets] of Object.entries(scenarios)) {
    const fixture = publisherFixture({ mutateAssets });
    await assert.rejects(fixture.publish(), /asset (count mismatch|verification failed)/, name);
    assert.equal(published(fixture.calls), false, name);
    assert.equal(fixture.calls.some(call => call.method === 'DELETE'), false, name);
  }
});

test('changed draft identity, tag or publication state stops before the publish operation', async () => {
  for (const stored of [{ id: RELEASE_ID + 1 }, { tag_name: 'v9.9.9' }, { draft: false }]) {
    const fixture = publisherFixture({ stored });
    await assert.rejects(fixture.publish(), /draft changed/);
    assert.equal(published(fixture.calls), false);
  }
});

test('a tag changed before creation or after verification cannot publish the attested source as another commit', async () => {
  const before = publisherFixture({ initialTag: OTHER_SHA });
  await assert.rejects(before.publish(), /tag moved/);
  assert.deepEqual(mutations(before.calls), []);
  const after = publisherFixture({ finalTag: OTHER_SHA });
  await assert.rejects(after.publish(), /tag moved before publication/);
  assert.equal(after.uploaded.length, RELEASE_FILES.length);
  assert.equal(published(after.calls), false);
});

test('invalid publisher inputs are rejected before touching the remote API', async () => {
  for (const overrides of [{ tag: 'v1.2.3-rc.1' }, { sha: 'not-a-commit' }, { repo: '../wrong' },
    { runId: '123/secret' }, { assets: assets().reverse() }, { assets: assets().slice(1) }]) {
    const fixture = publisherFixture();
    await assert.rejects(fixture.publish(overrides));
    assert.deepEqual(fixture.calls, []);
  }
});

test('annotated tags are peeled to their commit and cycles or non-commit objects fail safely', async () => {
  const calls = [], first = 'c'.repeat(40), second = 'd'.repeat(40);
  const client = { async request(method, endpoint) {
    calls.push({ method, endpoint });
    if (endpoint === `${PREFIX}/git/ref/tags/${TAG}`) return { object: { type: 'tag', sha: first } };
    if (endpoint === `${PREFIX}/git/tags/${first}`) return { object: { type: 'tag', sha: second } };
    if (endpoint === `${PREFIX}/git/tags/${second}`) return { object: { type: 'commit', sha: SHA } };
    throw new Error('Unexpected synthetic tag endpoint');
  } };
  assert.equal(await tagCommit(client, PREFIX, TAG), SHA);
  assert.deepEqual(calls.map(call => call.endpoint), [`${PREFIX}/git/ref/tags/${TAG}`, `${PREFIX}/git/tags/${first}`, `${PREFIX}/git/tags/${second}`]);
  for (const object of [null, { type: 'commit', sha: 'invalid' }, { type: 'tree', sha: SHA }]) {
    await assert.rejects(tagCommit({ request: async () => ({ object }) }, PREFIX, TAG), /tag object|resolve to a commit/);
  }
  let reads = 0;
  await assert.rejects(tagCommit({ request: async () => { reads++; return { object: { type: 'tag', sha: first } }; } }, PREFIX, TAG), /nesting/);
  assert.equal(reads, 9, 'a cyclic annotation chain has a bounded number of requests');
});
