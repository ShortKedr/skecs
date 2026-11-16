// The destination must exist in the chosen release. Never redirect to another
// version's similarly named API silently; fall back to that release's overview.
export function versionDestination(releases, currentVersion, targetVersion, pathname, hash = '', docsBase = '/') {
  const target = releases.find(release => release.version === targetVersion)
  if (!target) throw new Error(`Unknown documentation version: ${targetVersion}`)
  const currentBase = `${docsBase}${currentVersion}/`
  if (!pathname.startsWith(currentBase)) throw new Error('Page is outside the selected release')
  const route = pathname.slice(currentBase.length).replace(/\.html$/, '').replace(/(^|\/)index$/, '$1')
  const ru = route.startsWith('ru/')
  const home = ru ? 'ru/' : ''
  const exists = Object.hasOwn(target.pages, route)
  const anchors = exists ? target.pages[route] : []
  const anchor = hash && anchors.includes(decodeURIComponent(hash.slice(1))) ? hash : ''
  return `${docsBase}${targetVersion}/${exists ? route : home}${exists ? anchor : '?unavailable=1'}`
}
