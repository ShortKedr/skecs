import { dirname, join, posix } from 'node:path'
import { createMarkdownRenderer } from 'vitepress'
import { moduleDependencies } from './releases.mjs'

export const componentRoute = component => component.id ? `modules/${component.id}/${component.version}/` : ''
const guideRoute = (component, route) => componentRoute(component) + 'guide/' + route

export function guideRoutes(release, lang) {
  const suffix = lang ? '.ru.md' : '.md'
  return new Map([release, ...(release.modules ?? [])].flatMap(component => component.guides.map(guide =>
    [guide.source + suffix, `/${release.version}/${lang}${guideRoute(component, guide.route)}`])))
}

export function guideLinks(text, sourceFile, component, routes) {
  return text.replace(/(!?\[[^\]]*\]\()([^\s)]+)(\))/g, (match, start, href, end) => {
    if (/^(https?:|mailto:|#|\/)/.test(href)) return match
    const [path, anchor] = href.split('#')
    const target = posix.normalize(posix.join(posix.dirname(sourceFile), decodeURIComponent(path)))
    const revision = /\.(cs|csproj|sln)$/.test(target) ? component.sourceCommit : (component.docsCommit ?? component.sourceCommit)
    const url = routes.get(target) ?? `https://github.com/ShortKedr/skecs/blob/${revision}/${target}`
    return `${start}${url}${anchor ? '#' + anchor : ''}${end}`
  })
}

const xml = value => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;')

function runSample(module, dependencies, packages, directory, sourceText, { run, write }) {
  const feeds = new Map()
  for (const component of dependencies) {
    const folder = dirname(packages.get(component.packageId).package)
    if (!feeds.has(folder)) feeds.set(folder, [])
    feeds.get(folder).push(component.packageId)
  }
  const references = dependencies.map(component => `<PackageReference Include="${component.packageId}" Version="[${component.version}]" />`).join('\n')
  const sources = [...feeds.keys()].map((folder, index) => `<add key="release-${index}" value="${xml(folder)}" />`).join('\n')
  const mappings = [...feeds.values()].map((packages, index) =>
    `<packageSource key="release-${index}">${packages.map(id => `<package pattern="${id}" />`).join('')}</packageSource>`).join('\n')
  const project = join(directory, 'Sample.csproj')
  const nuget = join(directory, 'NuGet.Config')
  write(join(directory, 'Program.cs'), sourceText)
  write(project, `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><AllowUnsafeBlocks>true</AllowUnsafeBlocks></PropertyGroup><ItemGroup>${references}</ItemGroup></Project>`)
  write(nuget, `<configuration><packageSources><clear />${sources}<add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources><packageSourceMapping>${mappings}<packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping></configuration>`)
  // Isolate NuGet's global cache so checkout builds cannot bypass the verified feeds.
  run('dotnet', ['restore', project, '--configfile', nuget, '--packages', join(directory, 'packages')])
  const output = run('dotnet', ['run', '--project', project, '-c', 'Release', '--no-restore'], true)
  const outputLines = output.split(/\r?\n/)
  let previousLine = -1
  for (const line of module.sample.expectedOutput) {
    previousLine = outputLines.indexOf(line, previousLine + 1)
    if (previousLine < 0) throw new Error(`${module.packageId} sample did not produce the documented output in order`)
  }
}

export async function prepareModule(release, repository, module, packages, context) {
  const { site, source, guideSource, write, releaseCache, pageRoot } = context
  const markdown = await createMarkdownRenderer(site, { headers: true })
  const sampleDirectory = join(releaseCache(release), 'modules', module.id, 'sample')
  const sourceText = source(module, module.sample.source)
  const dependencies = [module, ...moduleDependencies(release, module)]
  runSample(module, dependencies, packages, sampleDirectory, sourceText, context)
  const details = { ...module, route: componentRoute(module), guides: {}, api: [] }
  const releaseBase = `/${release.version}/`
  function writePage(route, text, environment = {}) {
    const file = route.endsWith('/') ? route + 'index' : route
    write(join(pageRoot(release), file + '.md'), `---\neditLink: false\n---\n\n${text.replaceAll('](' + releaseBase, '](/')}`)
    markdown.render(text, environment)
    repository.pages[route] = environment.headers.map(heading => heading.slug)
  }
  for (const lang of ['', 'ru/']) {
    const locale = lang ? 'ru' : 'en'
    const suffix = lang ? '.ru.md' : '.md'
    const prefix = lang + componentRoute(module)
    const base = releaseBase + prefix
    const routes = guideRoutes(release, lang)
    details.guides[locale] = []
    for (const guide of module.guides) {
      const sourceFile = guide.source + suffix
      const lines = guideSource(release, module, sourceFile).trim().split('\n')
      if (lines[0].includes('|') && lines[0].includes('English') && lines[0].includes('Русский')) lines.shift()
      const text = guideLinks(lines.join('\n').trim(), sourceFile, module, routes)
      const title = text.match(/^# (.+)$/m)?.[1]
      if (!title) throw new Error(`Missing title in module guide: ${sourceFile}`)
      writePage(`${prefix}guide/${guide.route}`, text, { path: sourceFile, relativePath: sourceFile })
      details.guides[locale].push({ text: title, link: `/${componentRoute(module)}guide/${guide.route}` })
    }
    const sampleTitle = lang ? 'Рабочий пример' : 'Runnable example'
    const sampleIntroduction = lang
      ? `Пример из [релиза ${module.version}](${module.releaseUrl}) проверяется с пакетами этого релиза. Подключите пакет по [инструкции установки](${base}guide/installation), замените \`Program.cs\` кодом ниже и выполните \`dotnet run -c Release\`.`
      : `This [release ${module.version} sample](${module.releaseUrl}) runs against the release packages. Follow the [installation guide](${base}guide/installation), replace \`Program.cs\` with the code below, and run \`dotnet run -c Release\`.`
    writePage(prefix + 'guide/sample', `# ${sampleTitle}\n\n${sampleIntroduction}\n\n\`\`\`csharp\n${sourceText}\n\`\`\`\n\n\`\`\`text\n${module.sample.expectedOutput.join('\n')}\n\`\`\`\n`)
    const overview = [`# ${module.title[locale]} ${module.version}`, `\`${module.packageId}\` · [${lang ? 'Релиз' : 'Release'}](${module.releaseUrl})`]
    const dependencyLinks = module.dependencies.map(id => {
      const item = dependencies.find(candidate => (candidate.id ?? 'core') === id)
      return `[${item.packageId} ${item.version}](${releaseBase}${lang}${componentRoute(item)})`
    })
    if (dependencyLinks.length) overview.push(`${lang ? 'Зависимости' : 'Dependencies'} — ${dependencyLinks.join(', ')}`)
    const items = [...details.guides[locale].map(item => `[${item.text}](${releaseBase}${lang}${item.link.slice(1)})`), `[${sampleTitle}](${base}guide/sample)`, `[${lang ? 'Справочник API' : 'API reference'}](${base}api/)`]
    overview.push(items.map(item => `- ${item}`).join('\n'))
    writePage(prefix, overview.join('\n\n') + '\n')
  }
  repository.modules.push(details)
  return details
}
