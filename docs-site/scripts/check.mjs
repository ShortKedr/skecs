import assert from 'node:assert/strict'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { dirname, join, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'

const site = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const dist = join(site, '.vitepress/dist')
const docsBase = process.env.SKECS_DOCS_BASE || '/'
const demoPath = `${docsBase}demo/`
const registry = JSON.parse(readFileSync(join(site, 'releases.json'), 'utf8'))
assert.ok(existsSync(join(dist, 'index.html')), 'Build the site before checking it')
function* htmlFiles(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) yield* htmlFiles(path)
    else if (entry.name.endsWith('.html')) yield path
  }
}
const files = [...htmlFiles(dist)]
const contents = new Map(files.map(path => [path, readFileSync(path, 'utf8')]))
const anchors = new Map([...contents].map(([path, html]) => [path, new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]))]))
let links = 0
for (const [file, html] of contents) {
  const base = new URL(relative(dist, file).split(sep).join('/'), `http://docs.local${docsBase}`)
  for (const match of html.matchAll(/\b(href|src)="([^"]+)"/g)) {
    const url = new URL(match[2].replaceAll('&amp;', '&'), base)
    if (url.origin !== base.origin) continue
    // Pages combines the independently built demo with this documentation output.
    if (docsBase !== '/' && match[1] === 'href' && url.pathname === demoPath) continue
    assert.ok(url.pathname.startsWith(docsBase), `Link escapes docs base in ${relative(dist, file)}: ${url.href}`)
    const path = '/' + decodeURIComponent(url.pathname.slice(docsBase.length))
    let target = resolve(dist, '.' + path)
    if (path.endsWith('/')) target = join(target, 'index.html')
    else if (!existsSync(target)) target += '.html'
    assert.ok(target.startsWith(dist + sep), `Link escapes site: ${url.href}`)
    assert.ok(existsSync(target), `Broken link in ${relative(dist, file)}: ${match[2]}`)
    if (url.hash && anchors.has(target)) {
      assert.ok(anchors.get(target).has(decodeURIComponent(url.hash.slice(1))), `Broken anchor in ${relative(dist, file)}: ${match[2]}`)
    }
    links++
  }
}
for (const release of registry.releases) {
 const metadata = JSON.parse(readFileSync(join(site, '.vitepress/cache/releases', release.version, 'repository.json'), 'utf8'))
 assert.equal(metadata.release.packageSha256, release.packageSha256)
 for (const [field, value, message] of [
   ['packageSha256', '0'.repeat(64), /checksum mismatch/],
   ['sourceCommit', '0'.repeat(40), /commit does not match/]
 ]) {
   const rejected = spawnSync('python', [join(site, 'scripts/package.py'), JSON.stringify({ ...release, [field]: value })], { encoding: 'utf8' })
   assert.notEqual(rejected.status, 0, 'Changed release inputs must be rejected')
   assert.match(rejected.stderr, message)
 }
 for (const locale of ['', 'ru/']) {
  const prefix = release.version + '/' + locale
  for (const page of ['index', 'guide/sample', ...release.guides.map(item => 'guide/' + item.route), 'api/index', ...['World', 'Query-2', 'RawChunkView'].map(name => `api/${release.product}.${name}`)]) {
    assert.ok(contents.has(join(dist, prefix, page + '.html')), `Missing ${prefix}${page}`)
  }
  if (release.migrationFrom) {
    const installation = contents.get(join(dist, prefix, 'guide/installation.html'))
    const article = installation?.match(/<main\b[^>]*>([\s\S]*?)<\/main>/)?.[1]
    assert.ok(article, `Missing installation article at ${prefix}guide/installation.html`)
    for (const route of ['migration', 'release-notes']) {
      const target = `${docsBase}${prefix}guide/${route}`
      assert.ok(article.includes(`href="${target}.html"`) || article.includes(`href="${target}"`),
        `Installation must link to ${target} in the current release and locale`)
    }
  }
  const sample = contents.get(join(dist, prefix, 'guide/sample.html'))
  assert.ok(sample.includes('MovementProcessor'), 'The existing repository sample must be included')
  assert.ok(sample.includes('Matching archetypes:'), 'The full sample must be included')
  assert.ok(!sample.includes('&lt;&lt;&lt;'), 'A snippet import was rendered literally')
  const apiFiles = files.filter(file => file.startsWith(join(dist, prefix, 'api') + sep))
  const generatedPages = readdirSync(join(site, '.vitepress/cache/releases', release.version, 'api')).filter(name => name.endsWith('.md'))
  assert.equal(apiFiles.length, generatedPages.length + 1, 'Every generated API page and the overview must render')
  const query = contents.get(join(dist, prefix, `api/${release.product}.Query-2.html`))
  assert.ok(query.includes('Query&lt;T1, T2&gt;'), 'Generic type parameters must remain visible')
  assert.ok(!query.includes('&amp;lt;'), 'API titles must not double-escape generic parameters')
  const home = contents.get(join(dist, prefix, 'index.html'))
  const demo = docsBase === '/' ? 'https://shortkedr.github.io/skecs/demo/' : demoPath
  assert.ok(home.includes(`href="${demo}${locale ? '?lang=ru' : ''}"`), 'Link to the demo in the selected language')
  assert.ok(home.includes('docs-version-select'), 'Every locale needs the version selector')
  assert.ok(home.includes(`${release.packageId}`), 'Show exact selected package identity')
  assert.ok(home.includes(release.version), 'Show selected release version')
  for (const [file, html] of contents) {
    if (!file.startsWith(join(dist, prefix))) continue
    for (const match of html.matchAll(/href="(\/[^"#?]*)/g)) {
      if (docsBase !== '/' && match[1] === demoPath) continue
      assert.ok(match[1].startsWith(`${docsBase}${release.version}/`), `Navigation escapes release in ${file}: ${match[1]}`)
    }
  }
 }
 assert.ok(contents.get(join(dist, release.version, 'ru/index.html')).includes('lang="ru-RU"'))
 const search = readdirSync(join(dist, release.version, 'assets/chunks')).filter(name => name.includes('localSearchIndex'))
 assert.ok(search.length >= 2, 'Each release needs independent local search indexes for both locales')
}
assert.ok(!files.some(file => /(?:Archetype|AccessorStorage|ComponentTypeRegistry)\.html$/.test(file)), 'Internal implementation types must not leak into API docs')
assert.deepEqual(readdirSync(dist, { withFileTypes: true }).filter(item => item.isDirectory() && /^\d/.test(item.name)).map(item => item.name).sort(), registry.releases.map(item => item.version).sort(), 'Do not expose unregistered release versions')
assert.ok(contents.get(join(dist, 'index.html')).includes(`location.replace("${docsBase}${registry.defaultVersion}/")`))
assert.ok(contents.get(join(dist, 'ru/index.html')).includes(`location.replace("${docsBase}${registry.defaultVersion}/ru/")`))
assert.ok(contents.get(join(dist, 'index.html')).includes(`location.pathname === "${docsBase}"`), 'Unknown version URLs must not silently redirect to latest')
assert.ok(contents.get(join(dist, '404.html')).includes('Page or version unavailable'))
console.log(`PASS: ${registry.releases.length} release(s), ${files.length} pages, ${links} local links/assets/anchors, version-scoped EN/RU navigation, package API, sample and search.`)
