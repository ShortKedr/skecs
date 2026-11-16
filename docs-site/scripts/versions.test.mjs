import assert from 'node:assert/strict'
import { test } from 'node:test'
import { versionDestination } from '../.vitepress/theme/versions.mjs'

// Routing fixtures only: these are not registered or built documentation releases.
const releases = [
  { version: '1.0.0', pages: { '': [], 'ru/': [], 'guide/install': ['install'], 'ru/guide/install': ['установка'], 'api/OldType': [] } },
  { version: '2.0.0', pages: { '': [], 'ru/': [], 'guide/install': ['install'], 'ru/guide/install': ['установка'], 'api/NewType': [] } }
]
test('version switch preserves the page and locale when available', () => {
  assert.equal(versionDestination(releases, '1.0.0', '2.0.0', '/1.0.0/guide/install'), '/2.0.0/guide/install')
  assert.equal(versionDestination(releases, '2.0.0', '1.0.0', '/2.0.0/ru/guide/install'), '/1.0.0/ru/guide/install')
})
test('only an anchor that exists in the target release is kept', () => {
  assert.equal(versionDestination(releases, '1.0.0', '2.0.0', '/1.0.0/guide/install', '#install'), '/2.0.0/guide/install#install')
  assert.equal(versionDestination(releases, '1.0.0', '2.0.0', '/1.0.0/ru/guide/install', '#%D1%83%D1%81%D1%82%D0%B0%D0%BD%D0%BE%D0%B2%D0%BA%D0%B0'), '/2.0.0/ru/guide/install#%D1%83%D1%81%D1%82%D0%B0%D0%BD%D0%BE%D0%B2%D0%BA%D0%B0')
  assert.equal(versionDestination(releases, '1.0.0', '2.0.0', '/1.0.0/guide/install', '#removed'), '/2.0.0/guide/install')
})
test('removed or renamed API pages fall back to the target version home with a notice', () => {
  assert.equal(versionDestination(releases, '1.0.0', '2.0.0', '/1.0.0/api/OldType'), '/2.0.0/?unavailable=1')
  assert.equal(versionDestination(releases, '2.0.0', '1.0.0', '/2.0.0/ru/api/NewType'), '/1.0.0/ru/?unavailable=1')
})
test('home pages and .html URLs remain usable', () => {
  assert.equal(versionDestination(releases, '1.0.0', '2.0.0', '/1.0.0/index.html'), '/2.0.0/')
  assert.equal(versionDestination(releases, '1.0.0', '2.0.0', '/1.0.0/ru/'), '/2.0.0/ru/')
  assert.equal(versionDestination(releases, '1.0.0', '2.0.0', '/1.0.0/guide/install.html'), '/2.0.0/guide/install')
})
test('unregistered versions and incorrect current version are rejected', () => {
  assert.throws(() => versionDestination(releases, '1.0.0', '3.0.0', '/1.0.0/'), /Unknown documentation/)
  assert.throws(() => versionDestination(releases, '1.0.0', '2.0.0', '/2.0.0/'), /outside/)
})
test('switching versions on Pages stays under the documentation base', () => {
  const base = '/skecs/docs/'
  assert.equal(versionDestination(releases, '1.0.0', '2.0.0', `${base}1.0.0/ru/guide/install`, '', base), `${base}2.0.0/ru/guide/install`)
  assert.equal(versionDestination(releases, '1.0.0', '2.0.0', `${base}1.0.0/api/OldType`, '', base), `${base}2.0.0/?unavailable=1`)
})
