import { spawnSync } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { basename, dirname, join, posix, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createMarkdownRenderer } from 'vitepress'
import { validateRegistry, workingGuideVersion } from './releases.mjs'
import { prepareModule, componentRoute, guideLinks, guideRoutes } from './modules.mjs'

const site = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const root = resolve(site, '..')
const docsBase = process.env.SKECS_DOCS_BASE || '/'
if (!/^\/(?:[\w.-]+\/)*$/.test(docsBase) || docsBase.split('/').some(segment => segment === '.' || segment === '..')) {
  throw new Error('SKECS_DOCS_BASE must be an absolute URL directory, e.g. /skecs/')
}
const tooling = join(root, 'artifacts/tooling')
const temp = join(tooling, 'tmp/docs-site')
mkdirSync(temp, { recursive: true })
Object.assign(process.env, {
  DOTNET_CLI_HOME: join(tooling, 'dotnet-home'),
  NUGET_PACKAGES: join(tooling, 'nuget-packages'),
  NUGET_HTTP_CACHE_PATH: join(tooling, 'nuget-http-cache'),
  DOTNET_CLI_TELEMETRY_OPTOUT: '1',
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1',
  DOTNET_GENERATE_ASPNET_CERTIFICATE: 'false',
  TEMP: temp,
  TMP: temp
})

function run(command, args, capture = false) {
  const result = spawnSync(command, args, { cwd: site, stdio: capture ? 'pipe' : 'inherit', encoding: 'utf8', env: process.env })
  if (capture) {
    process.stdout.write(result.stdout ?? '')
    process.stderr.write(result.stderr ?? '')
  }
  if (result.error) throw result.error
  if (result.status !== 0) throw new Error(`${command} exited with ${result.status}`)
  return result.stdout ?? ''
}

function write(path, text) {
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, text)
}

const registry = JSON.parse(readFileSync(join(site, 'releases.json'), 'utf8'))
validateRegistry(registry)
const draftVersion = workingGuideVersion(process.argv.slice(3), registry)
delete process.env.SKECS_DOCS_DRAFT_VERSION
if (draftVersion) {
  if (process.env.CI) throw new Error('Working guides are available only for local verification')
  process.env.SKECS_DOCS_DRAFT_VERSION = draftVersion
}
const outputRoot = draftVersion ? join(site, '.vitepress/cache/draft', draftVersion) : join(site, '.vitepress')

function source(release, path, revision = release.sourceCommit) {
  const result = spawnSync('git', ['show', `${revision}:${path}`], { cwd: root, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 })
  if (result.status !== 0) throw new Error(`Missing release source ${release.version}:${path}: ${result.stderr}`)
  return result.stdout.replace(/\r\n/g, '\n')
}

const releaseCache = release => join(outputRoot, 'cache/releases', release.version)
const pageRoot = release => join(releaseCache(release), 'pages')

function guideSource(release, component, path) {
  if (release.version === draftVersion) return readFileSync(join(root, path), 'utf8').replace(/\r\n/g, '\n')
  return source(component, path, component.docsCommit ?? component.sourceCommit)
}

function runCoreExample(directory, release, assembly, sourceText) {
  const project = join(directory, 'Example.csproj')
  const hintPath = assembly.replaceAll('&', '&amp;').replaceAll('<', '&lt;')
  write(join(directory, 'Program.cs'), sourceText)
  write(project, `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><AllowUnsafeBlocks>true</AllowUnsafeBlocks><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Reference Include="${release.product}"><HintPath>${hintPath}</HintPath></Reference></ItemGroup></Project>`)
  return run('dotnet', ['run', '--project', project, '-c', 'Release'], true)
}

function verifyGuideExamples(text, sourceFile, checkedExamples, release, assembly) {
  const markers = [...text.matchAll(/^<!-- docs-example(?:: ([^\n]+?))? -->[ \t]*\n/gm)]
  for (const marker of markers) {
    const block = text.slice(marker.index + marker[0].length).match(/^\n*```csharp[ \t]*\n([\s\S]*?)^```[ \t]*$/m)
    if (!block || block.index !== 0) throw new Error(`Missing C# example marked for verification in ${sourceFile}`)
    const expected = marker[1]
    const snippet = JSON.stringify([block[1], expected])
    if (checkedExamples.has(snippet)) continue
    const directory = join(releaseCache(release), 'guide-examples', String(checkedExamples.size))
    console.log(`Verifying ${release.version} guide example from ${sourceFile}.`)
    const output = runCoreExample(directory, release, assembly, block[1])
    if (expected && !output.split(/\r?\n/).includes(expected)) {
      throw new Error(`Guide example from ${sourceFile} did not produce the documented output: ${expected}`)
    }
    checkedExamples.add(snippet)
  }
}

async function prepareGuides(release, assembly) {
  const markdown = await createMarkdownRenderer(site, { headers: true })
  const project = source(release, release.project)
  if (project.match(/<Version>([^<]+)<\/Version>/)?.[1] !== release.version || project.match(/<PackageId>([^<]+)<\/PackageId>/)?.[1] !== release.packageId) {
    throw new Error('Pinned source project does not match released package identity')
  }
  const repository = {
    ...release,
    targets: project.match(/<TargetFrameworks>([^<]+)<\/TargetFrameworks>/)[1].split(';'),
    guides: {}, pages: {}, api: [], modules: []
  }
  const sampleDirectory = join(releaseCache(release), 'sample')
  const sampleSource = source(release, release.sample)
  const sampleOutput = runCoreExample(sampleDirectory, release, assembly, sampleSource)
  if (!sampleOutput.includes('Tick 5: (15, 5)') || !sampleOutput.includes('Matching archetypes: 1')) {
    throw new Error('The repository sample did not produce the documented result')
  }
  repository.sample = {
    source: sampleSource,
    html: markdown.render('```csharp\n' + sampleSource + '\n```', {}),
    output: sampleOutput.split(/\r?\n/).filter(line => /^(Tick |Matching archetypes:)/.test(line)).join('\n'),
    position: [...sampleOutput.matchAll(/Tick \d+: \(([^)]+)\)/g)].at(-1)[1]
  }
  const checkedExamples = new Set()
  for (const lang of ['', 'ru/']) {
    const suffix = lang ? '.ru.md' : '.md'
    const guides = []
    const base = `/${release.version}/${lang}`
    const routes = guideRoutes(release, lang)
    for (const { source: sourcePath, route } of release.guides) {
      const sourceFile = sourcePath + suffix
      let text = guideSource(release, release, sourceFile).replace(/^\uFEFF/, '').split('\n').slice(1).join('\n').trim()
      if (route === 'installation' && release.version === '0.1.0') {
        // The original 0.1.0 tag still describes its pre-publication candidate.
        // Correct publication status without copying a historical manual tree.
        let introduction = lang
          ? `# Установка ${release.product} ${release.version}\n\nСкачайте \`${release.packageId}.${release.version}.nupkg\` из [релиза ${release.version}](${release.releaseUrl}) и сохраните в каталоге \`packages/\` рядом с проектом. Эта документация относится именно к этому релизному пакету.\n`
          : `# Install ${release.product} ${release.version}\n\nDownload \`${release.packageId}.${release.version}.nupkg\` from [release ${release.version}](${release.releaseUrl}) and keep it in a \`packages/\` directory beside your project. This documentation describes that exact release package.\n`
        if (release.migrationFrom) {
          const migration = release.guides.find(guide => guide.route === 'migration')
          const notes = release.guides.find(guide => guide.route === 'release-notes')
          const migrationPath = posix.relative(posix.dirname(sourceFile), migration.source + suffix)
          const notesPath = posix.relative(posix.dirname(sourceFile), notes.source + suffix)
          introduction += lang
            ? `\nПеред обновлением с ${release.migrationFrom} прочитайте [статью по миграции](${migrationPath}) и [примечания к релизу](${notesPath}).\n`
            : `\nBefore upgrading from ${release.migrationFrom}, read the [migration article](${migrationPath}) and [release notes](${notesPath}).\n`
        }
        text = introduction + text.slice(text.indexOf('\n## '))
        text = text.replace('Use the locally built ', 'Use the released ').replace('Используйте локально собранный ', 'Используйте релизный ')
          .replace(/^## Godot(?: \(C#\)| C#)$/gm, '## Godot .NET (C#)')
      }
      verifyGuideExamples(text, sourceFile, checkedExamples, release, assembly)
      text = guideLinks(text, sourceFile, release, routes)
      write(join(pageRoot(release), lang, 'guide', route + '.md'), `---\neditLink: false\n---\n\n${text.replaceAll(`](/${release.version}/`, '](/')}\n`)
      const environment = { path: sourceFile, relativePath: sourceFile }
      markdown.render(text, environment)
      repository.pages[`${lang}guide/${route}`] = environment.headers.map(heading => heading.slug)
      const headings = environment.headers.filter(heading => heading.level === 2).map(heading => ({
        text: heading.title,
        link: `/${lang}guide/${route}#${heading.slug}`
      }))
      guides.push({
        title: text.match(/^# (.+)$/m)[1].trim(),
        link: `/${lang}guide/${route}`,
        source: sourceFile,
        sections: headings
      })
    }
    repository.guides[lang ? 'ru' : 'en'] = guides
    const introduction = lang
      ? `# Рабочий пример ${release.product} ${release.version}\n\nКод примера взят из исходников [релиза ${release.version}](${release.releaseUrl}). При сборке документации он выполняется с DLL из проверенного релизного пакета. После пятого обновления позиция равна \`(15, 5)\`.\n\n## Запуск\n\nВ приложении .NET 8 подключите пакет версии ${release.version} по [инструкции установки](${base}guide/installation), замените \`Program.cs\` кодом ниже и выполните \`dotnet run -c Release\`.\n\n`
      : `# Runnable ${release.product} ${release.version} sample\n\nThe sample comes from [release ${release.version}](${release.releaseUrl}). The documentation build runs it against the verified release package DLL. After the fifth update the position is \`(15, 5)\`.\n\n## Run\n\nIn a .NET 8 app, add package version ${release.version} using the [installation guide](${base}guide/installation), replace \`Program.cs\` with the code below, then run \`dotnet run -c Release\`.\n\n`
    const samplePage = introduction + '```csharp\n' + sampleSource + '\n```\n'
    write(join(pageRoot(release), lang, 'guide/sample.md'), samplePage.replaceAll(`](/${release.version}/`, '](/'))
    const env = {}
    markdown.render(samplePage, env)
    repository.pages[`${lang}guide/sample`] = env.headers.map(heading => heading.slug)
    repository.pages[lang] = ['example-title', 'guide-title', 'api-title', 'next-title']
    write(join(pageRoot(release), lang, 'index.md'), `---\nlayout: doc\nsidebar: false\naside: false\noutline: false\nprev: false\nnext: false\ntitle: ${release.product} ${release.version}\n---\n\n<RepoHome />\n`)
  }
  mkdirSync(join(pageRoot(release), 'public'), { recursive: true })
  for (const size of [32, 200]) {
    copyFileSync(join(root, `docs/branding/logo/shortkedr-ecs-${size}.png`), join(pageRoot(release), `public/logo-${size}.png`))
  }
  return repository
}

// DocFX emits CommonMark, whereas VitePress also interprets HTML/Vue syntax.
// Protect prose and preserve code blocks, explicit anchors and resolved links.
function forVitePress(markdown, targets) {
  markdown = markdown.replace(/^(#{1,6}) <a id="([^"]+)"><\/a> (.+)$/gm, '$1 $3 {#$2}')
  markdown = markdown.replace(/<xref href="([^"]+)"[^>]*><\/xref>/g, (_, uid) => {
    const decoded = decodeURIComponent(uid)
    const target = targets.get(decoded.replace(/[^A-Za-z0-9]/g, '_'))
    if (!target) throw new Error(`Unresolved API cross-reference: ${decoded}`)
    return `[\`${decoded.replace(/`/g, '')}\`](${target})`
  })
  const lines = []
  let fence = null
  for (let line of markdown.split('\n')) {
    const marker = line.match(/^\s*(`{3,}|~{3,})/)
    if (fence) {
      if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = null
    } else if (marker) {
      fence = marker[1]
    } else {
      line = line.split(/(`+[^`]*`+)/).map((part, index) => index % 2 ? part : part
        .replace(/<(?!\/?(?:a|code|pre|em|strong|br|p|ul|ol|li|span|table|thead|tbody|tr|th|td)(?=[\s>/]))/g, '\\<')
        .replace(/\{\{/g, '&#123;&#123;').replace(/\}\}/g, '&#125;&#125;')).join('')
    }
    lines.push(line)
  }
  return lines.join('\n')
}

function generateApi(release, component, assembly, repository, hasComments) {
  const prefix = componentRoute(component)
  const cache = join(releaseCache(release), prefix)
  const raw = join(cache, 'api')
  // This fixed build-output directory is owned entirely by this script.
  if (existsSync(raw)) rmSync(raw, { recursive: true })
  const config = JSON.parse(readFileSync(join(site, 'docfx.json'), 'utf8'))
  config.metadata[0].src = [{ src: dirname(assembly), files: [basename(assembly)] }]
  config.metadata[0].output = raw
  config.metadata[0].properties.TargetFramework = component.assemblyTarget ?? 'net8.0'
  const configPath = join(cache, 'docfx.json')
  write(configPath, JSON.stringify(config, null, 2))
  run('dotnet', ['tool', 'run', 'docfx', '--', 'metadata', configPath, '--warningsAsErrors'])
  const pages = readdirSync(raw).filter(name => name.endsWith('.md')).sort()
  if (!pages.length || (!prefix && (!pages.includes(`${release.product}.World.md`) || !pages.includes(`${release.product}.Query-2.md`)))) {
    throw new Error('DocFX did not generate the expected release API')
  }
  const targets = new Map()
  for (const name of pages) {
    for (const match of readFileSync(join(raw, name), 'utf8').matchAll(/<a id="([^"]+)"><\/a>/g)) {
      targets.set(match[1], `${name}#${match[1]}`)
    }
  }
  const sidebar = pages.filter(name => prefix || name !== component.product + '.md').map(name => {
    const md = readFileSync(join(raw, name), 'utf8')
    const title = md.match(/^# (?:<a[^>]*><\/a> )?(?:Class|Struct|Interface|Enum) (.+)$/m)?.[1]
    return { text: (title ?? name.slice(0, -3)).replaceAll('\\', ''), link: '/' + prefix + 'api/' + name.slice(0, -3) }
  })
  for (const lang of ['', 'ru/']) {
    const directory = join(pageRoot(release), lang, prefix, 'api')
    if (existsSync(directory)) rmSync(directory, { recursive: true })
    mkdirSync(directory, { recursive: true })
    for (const name of pages) {
      write(join(directory, name), '---\neditLink: false\noutline: [2, 3]\n---\n\n' + forVitePress(readFileSync(join(raw, name), 'utf8'), targets))
      repository.pages[lang + prefix + 'api/' + name.slice(0, -3)] = [...readFileSync(join(raw, name), 'utf8').matchAll(/<a id="([^"]+)"><\/a>/g)].map(match => match[1])
    }
    const title = lang ? 'Справочник API' : 'API reference'
    const note = lang
      ? `Публичные типы и сигнатуры получены DocFX из \`${component.product}.dll\` (\`${component.assemblyTarget ?? 'net8.0'}\`) проверенного пакета \`${component.packageId}\` версии **${component.version}**. ${hasComments ? 'XML-комментарии из пакета сохранены на исходном языке.' : 'В пакете отсутствуют XML-комментарии, поэтому справочник содержит сигнатуры.'}`
      : `Public types and signatures are generated by DocFX from \`${component.product}.dll\` (\`${component.assemblyTarget ?? 'net8.0'}\`) in verified package \`${component.packageId}\` **${component.version}**. ${hasComments ? 'Packaged XML comments are included in their original language.' : 'The package has no XML comments, so this reference contains signatures.'}`
    write(join(directory, 'index.md'), `# ${title}\n\n${note}\n\n` + sidebar.map(item => `- [\`${item.text}\`](/${lang}${item.link.slice(1)})`).join('\n') + '\n')
    repository.pages[lang + prefix + 'api/'] = []
  }
  component.api = sidebar
  console.log(`Generated ${pages.length} API pages in each locale from ${component.packageId} ${component.version}.`)
}

const command = process.argv[2] ?? 'build'
if (!['build', 'dev', 'preview', 'api'].includes(command)) throw new Error(`Unknown command: ${command}`)
if (command !== 'preview') {
  run('dotnet', ['tool', 'restore'])
  const repositories = []
  for (const release of registry.releases) {
    const packages = new Map([release, ...(release.modules ?? [])].map(component => {
      const result = run('python', [join(site, 'scripts/package.py'), JSON.stringify(component)], true)
      return [component.packageId, JSON.parse(result.trim().split(/\r?\n/).at(-1))]
    }))
    const { assembly, hasComments } = packages.get(release.packageId)
    const pages = pageRoot(release)
    if (existsSync(pages)) rmSync(pages, { recursive: true })
    const repository = await prepareGuides(release, assembly)
    generateApi(release, repository, assembly, repository, hasComments)
    for (const module of release.modules ?? []) {
      const details = await prepareModule(release, repository, module, packages, { site, source, guideSource, write, run, releaseCache, pageRoot })
      const artifact = packages.get(module.packageId)
      generateApi(release, details, artifact.assembly, repository, artifact.hasComments)
    }
    repositories.push(repository)
  }
  const releases = repositories.map(({ version, product, packageId, pages }) => ({ version, product, packageId, pages }))
  for (const repository of repositories) {
    write(join(releaseCache(repository), 'repository.json'), JSON.stringify({ release: repository, releases, defaultVersion: registry.defaultVersion }, null, 2))
  }
}
if (command === 'preview') {
  // VitePress 1's preview ignores --host; Vite's preview honors loopback binding.
  run(process.execPath, [join(site, 'node_modules/vite/bin/vite.js'), 'preview',
    '--outDir', join(outputRoot, 'dist'), '--base', docsBase, '--host', '127.0.0.1', '--port', '4187', '--strictPort'])
} else if (command !== 'api') {
  if (command === 'build') {
    const dist = join(outputRoot, 'dist')
    if (existsSync(dist)) rmSync(dist, { recursive: true })
    for (const release of registry.releases) {
      process.env.SKECS_DOCS_VERSION = release.version
      run(process.execPath, [join(site, 'node_modules/vitepress/bin/vitepress.js'), 'build'])
    }
    for (const lang of ['', 'ru/']) {
      const href = `${docsBase}${registry.defaultVersion}/${lang}`
      // Preview hosts may serve index.html for an unknown URL. Redirect only
      // actual entry URLs, so /an-unavailable-version/ cannot become latest docs.
      const entry = docsBase + lang
      const listing = registry.releases.map(release => `<li><a href="${docsBase}${release.version}/${lang}">${release.packageId} ${release.version}</a></li>`).join('')
      const html = `<!doctype html><html lang="${lang ? 'ru' : 'en'}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>SKECS documentation</title></head><body><main><h1>${lang ? 'Страница или версия недоступна' : 'Page or version unavailable'}</h1><p>${lang ? 'Доступная документация релизов:' : 'Available release documentation:'}</p><ul>${listing}</ul></main><script>if (location.pathname === ${JSON.stringify(entry)} || location.pathname === ${JSON.stringify(entry + 'index.html')}) location.replace(${JSON.stringify(href)})</script></body></html>`
      write(join(dist, lang, 'index.html'), html)
      if (!lang) write(join(dist, '404.html'), html.replace(/<script>.*?<\/script>/, ''))
    }
  } else {
    process.env.SKECS_DOCS_VERSION = draftVersion ?? registry.defaultVersion
    run(process.execPath, [join(site, 'node_modules/vitepress/bin/vitepress.js'), 'dev', '--host', '127.0.0.1', '--port', '4187', '--strictPort'])
  }
}
