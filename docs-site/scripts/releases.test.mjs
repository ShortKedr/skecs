import assert from 'node:assert/strict'
import { test } from 'node:test'
import { guideLinks, guideRoutes } from './modules.mjs'
import { moduleDependencies, validateRegistry, workingGuideVersion } from './releases.mjs'
import { versionDestination } from '../.vitepress/theme/versions.mjs'

const registry = () => {
  const pins = { sourceCommit: 'a'.repeat(40), packageSha256: 'b'.repeat(64), releaseUrl: 'https://example.com/release', packageUrl: 'https://example.com/package' }
  const module = (id, product, dependencies) => ({
    ...pins, id, product, packageId: 'Sdk.' + product, version: '2.1.0-rc.1', title: { en: product, ru: product },
    guides: [{ source: `docs/${id}/install`, route: 'installation' }],
    dependencies, sample: { source: `samples/${id}/Program.cs`, expectedOutput: ['Sample complete.'] }
  })
  return {
    defaultVersion: '2.0.0',
    releases: [{
      ...pins, version: '2.0.0', product: 'Core', packageId: 'Sdk.Core',
      guides: [{ source: 'docs/core/install', route: 'installation' }],
      modules: [module('codecs', 'Codecs', ['core']), module('replication', 'Replication', ['codecs', 'core'])]
    }]
  }
}

test('validate independent module versions and collect exact transitive dependencies once', () => {
  const input = registry()
  assert.doesNotThrow(() => validateRegistry(input))
  const [release] = input.releases
  assert.deepEqual(moduleDependencies(release, release.modules[1]).map(item => [item.packageId, item.version]), [
    ['Sdk.Codecs', '2.1.0-rc.1'], ['Sdk.Core', '2.0.0']
  ])
})

test('reject missing dependencies, dependency cycles and route collisions', () => {
  for (const [replacement, message] of [
    [release => release.modules[1].dependencies.push('unknown'), /Unknown module dependency/],
    [release => release.modules[0].dependencies.push('replication'), /Circular module dependency/],
    [release => { release.modules[1].id = 'codecs' }, /duplicate module ID/],
    [release => { release.modules[1].guides[0].source = release.guides[0].source }, /Duplicate guide source/],
    [release => { release.modules[1].assemblyTarget = '../bin' }, /target framework/],
    [release => { release.modules[1].packageId = ['Sdk.Replication'] }, /package or assembly name/],
    [release => { release.modules[1].version = ['2.1.0'] }, /module package version/],
    [release => { release.modules[1].sample.expectedOutput = [] }, /expected output/]
  ]) {
    const input = registry()
    replacement(input.releases[0])
    assert.throws(() => validateRegistry(input), message)
  }
})

test('resolve cross-module guides in the selected release and locale', () => {
  const [release] = registry().releases
  const [, replication] = release.modules
  const text = guideLinks('[Codecs](../codecs/install.ru.md#setup) [Core](../core/install.ru.md)', 'docs/replication/install.ru.md', replication, guideRoutes(release, 'ru/'))
  assert.equal(text, '[Codecs](/2.0.0/ru/modules/codecs/2.1.0-rc.1/guide/installation#setup) [Core](/2.0.0/ru/guide/installation)')
  const releases = [{ version: '2.0.0', pages: { 'ru/modules/codecs/2.1.0-rc.1/guide/installation': ['setup'] } }, { version: '3.0.0', pages: { 'ru/': [] } }]
  assert.equal(versionDestination(releases, '2.0.0', '3.0.0', '/2.0.0/ru/modules/codecs/2.1.0-rc.1/guide/installation'), '/3.0.0/ru/?unavailable=1')
})

test('working guides require an explicitly selected registered core version', () => {
  const input = registry()
  assert.equal(workingGuideVersion([], input), null)
  assert.equal(workingGuideVersion(['--working-guides=2.0.0'], input), '2.0.0')
  assert.throws(() => workingGuideVersion(['--working-guides=3.0.0'], input), /registered-version/)
  assert.throws(() => workingGuideVersion(['--working-guides=2.0.0', '--working-guides=2.0.0'], input), /registered-version/)
})

test('default core version must be a registered stable release', () => {
  const input = registry()
  input.defaultVersion = input.releases[0].modules[0].version
  assert.throws(() => validateRegistry(input), /not an available release/)
  input.releases[0].version = input.defaultVersion
  assert.throws(() => validateRegistry(input), /unique stable release versions/)
})
