import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

export function stableVersion(tag) {
  if (!/^v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$/.test(tag || ''))
    throw new Error('Release tags must use stable vMAJOR.MINOR.PATCH format.');
  return tag.slice(1);
}

export function assemblyVersion(source) {
  const file = [...source.matchAll(/AssemblyFileVersion\("(\d+\.\d+\.\d+\.\d+)"\)/g)];
  const assembly = [...source.matchAll(/AssemblyVersion\("(\d+\.\d+\.\d+\.\d+)"\)/g)];
  if (file.length !== 1 || assembly.length !== 1 || file[0][1] !== assembly[0][1])
    throw new Error('Exactly one matching assembly and file version is required.');
  return file[0][1];
}

export function releaseMetadata(env, source, git) {
  const fileVersion = assemblyVersion(source);
  const sourceSha = git(['rev-parse', 'HEAD']);
  if (!/^[0-9a-f]{40}$/.test(sourceSha)
    || git(['rev-parse', `${env.GITHUB_SHA}^{commit}`]) !== sourceSha)
    throw new Error('Checked-out commit differs from the workflow source.');
  const tagPush = (env.GITHUB_REF || '').startsWith('refs/tags/');
  if (tagPush) {
    if (env.GITHUB_EVENT_NAME !== 'push') throw new Error('Only a tag push can publish a release.');
    const version = stableVersion(env.GITHUB_REF.slice('refs/tags/'.length));
    if (fileVersion !== `${version}.0`) throw new Error('Tag does not match AssemblyFileVersion.');
    git(['merge-base', '--is-ancestor', sourceSha, 'refs/remotes/origin/main']);
  }
  return { 'is-release': String(tagPush), 'file-version': fileVersion, 'source-sha': sourceSha };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const metadata = releaseMetadata(process.env, fs.readFileSync('src/Program.cs', 'utf8'),
      args => execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim());
    if (process.env.GITHUB_OUTPUT)
      fs.appendFileSync(process.env.GITHUB_OUTPUT, Object.entries(metadata).map(([key, value]) => `${key}=${value}\n`).join(''));
    console.log(JSON.stringify(metadata));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
