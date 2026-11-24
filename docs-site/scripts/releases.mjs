const stableVersion = /^\d+\.\d+\.\d+$/
const moduleVersion = /^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/
const slug = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/
const name = /^[A-Za-z0-9][A-Za-z0-9._-]*$/
const sourcePath = /^(?:[A-Za-z0-9_][A-Za-z0-9_.-]*\/)*[A-Za-z0-9_][A-Za-z0-9_.-]*$/

function validatePins(component) {
  if (typeof component.sourceCommit !== 'string' || !/^[a-f0-9]{40}$/.test(component.sourceCommit) ||
      typeof component.packageSha256 !== 'string' || !/^[a-f0-9]{64}$/.test(component.packageSha256)) {
    throw new Error('Pin each release commit and package checksum')
  }
  if (component.docsCommit !== undefined && (typeof component.docsCommit !== 'string' || !/^[a-f0-9]{40}$/.test(component.docsCommit))) {
    throw new Error('Documentation corrections require a pinned commit')
  }
  if ([component.product, component.packageId].some(value => typeof value !== 'string' || !name.test(value))) throw new Error('Invalid package or assembly name')
  if (component.assemblyTarget !== undefined && (typeof component.assemblyTarget !== 'string' || !/^net(?:standard|coreapp)?\d+\.\d+$/.test(component.assemblyTarget))) {
    throw new Error('Expected a .NET target framework for the documented assembly')
  }
  for (const url of [component.packageUrl, component.releaseUrl]) {
    if (typeof url !== 'string' || new URL(url).protocol !== 'https:') throw new Error('Release and package URLs must use HTTPS')
  }
  const routes = new Set(['sample'])
  for (const guide of component.guides) {
    if (typeof guide.route !== 'string' || typeof guide.source !== 'string' || !slug.test(guide.route) || !sourcePath.test(guide.source) || routes.has(guide.route)) throw new Error(`Invalid or duplicate guide route: ${guide.route}`)
    routes.add(guide.route)
  }
  if (component.migrationFrom && !['migration', 'release-notes'].every(route => routes.has(route))) {
    throw new Error(`${component.packageId} ${component.version} requires migration and release-notes articles`)
  }
}

export function validateRegistry(registry) {
  const versions = new Set()
  for (const release of registry.releases) {
    if (typeof release.version !== 'string' || !stableVersion.test(release.version) || versions.has(release.version)) throw new Error('Expected unique stable release versions')
    versions.add(release.version)
    validatePins(release)
    const ids = new Set(['core'])
    const packages = new Set([release.packageId.toLowerCase()])
    const paths = new Set(release.guides.map(guide => guide.source))
    const modules = release.modules ?? []
    for (const module of modules) {
      validatePins(module)
      if (typeof module.id !== 'string' || !slug.test(module.id) || ids.has(module.id)) throw new Error(`Invalid or duplicate module ID: ${module.id}`)
      if (typeof module.version !== 'string' || !moduleVersion.test(module.version)) throw new Error(`Invalid module package version: ${module.version}`)
      if (packages.has(module.packageId.toLowerCase())) throw new Error(`Duplicate package ID: ${module.packageId}`)
      if (!module.guides.some(guide => guide.route === 'installation')) throw new Error(`Missing installation guide for ${module.id}`)
      if (typeof module.sample?.source !== 'string' || !sourcePath.test(module.sample.source) || !module.sample.source.endsWith('.cs') ||
          !Array.isArray(module.sample.expectedOutput) || !module.sample.expectedOutput.length ||
          module.sample.expectedOutput.some(line => typeof line !== 'string' || !line.trim())) {
        throw new Error(`Module ${module.id} needs a pinned .cs sample and expected output`)
      }
      if ([module.title?.en, module.title?.ru].some(label => typeof label !== 'string' || !label.trim())) {
        throw new Error(`Module ${module.id} needs EN/RU labels`)
      }
      ids.add(module.id)
      packages.add(module.packageId.toLowerCase())
      for (const guide of module.guides) {
        if (paths.has(guide.source)) throw new Error(`Duplicate guide source: ${guide.source}`)
        paths.add(guide.source)
      }
    }
    const visited = new Set(['core'])
    const pending = new Set()
    function visit(module) {
      if (pending.has(module.id)) throw new Error(`Circular module dependency: ${module.id}`)
      if (visited.has(module.id)) return
      if (!Array.isArray(module.dependencies) || new Set(module.dependencies).size !== module.dependencies.length) {
        throw new Error(`Module ${module.id} needs a list of distinct dependency IDs`)
      }
      pending.add(module.id)
      for (const dependency of module.dependencies) {
        if (!ids.has(dependency)) throw new Error(`Unknown module dependency: ${dependency}`)
        if (dependency !== 'core') visit(modules.find(candidate => candidate.id === dependency))
      }
      pending.delete(module.id)
      visited.add(module.id)
    }
    for (const module of modules) visit(module)
  }
  if (!versions.has(registry.defaultVersion)) throw new Error('Default version is not an available release')
}

export function moduleDependencies(release, module) {
  const components = new Map([['core', release], ...release.modules.map(component => [component.id, component])])
  const dependencies = new Set()
  function visit(component) {
    for (const id of component.dependencies ?? []) {
      if (dependencies.has(id)) continue
      dependencies.add(id)
      visit(components.get(id))
    }
  }
  visit(module)
  return [...dependencies].map(id => components.get(id))
}

export function workingGuideVersion(options, registry) {
  if (options.length === 0) return null
  const version = options[0].match(/^--working-guides=(\d+\.\d+\.\d+)$/)?.[1]
  if (options.length !== 1 || !registry.releases.some(release => release.version === version)) {
    throw new Error('Use --working-guides=<registered-version> for local guide verification')
  }
  return version
}
