import DefaultTheme from 'vitepress/theme'
import { defineComponent, h } from 'vue'
import { useData, useRouter, withBase } from 'vitepress'
import RepoHome from './RepoHome.vue'
import VersionSelector from './VersionSelector.vue'
import ReleaseNotice from './ReleaseNotice.vue'
import './style.css'

export default {
  extends: DefaultTheme,
  Layout: defineComponent({ setup() {
    const { theme } = useData()
    const router = useRouter()
    // VitePress copies the fragment when switching languages. Resolve it against
    // the headings of the translated page, including the generated sample.
    if (typeof window !== 'undefined') router.onBeforeRouteChange = to => {
      const destination = new URL(to, window.location.href)
      const releaseBase = withBase('/')
      if (destination.origin !== window.location.origin || !destination.pathname.startsWith(releaseBase) || !destination.hash) return
      const [current, target] = [window.location.pathname, destination.pathname].map(path =>
        path.slice(releaseBase.length).replace(/\.html$/, '').replace(/(^|\/)index$/, '$1'))
      const translation = current.startsWith('ru/') ? current.slice(3) : `ru/${current}`
      if (target !== translation) return

      const pages = theme.value.release.pages as Record<string, string[]>
      if (!Object.hasOwn(pages, current) || !Object.hasOwn(pages, target)) return
      const targetAnchors = pages[target]
      let hash = destination.hash.slice(1)
      try { hash = decodeURIComponent(hash) } catch { /* Invalid fragments are absent from the index. */ }
      if (targetAnchors.includes(hash)) return
      const index = pages[current].indexOf(hash)
      destination.hash = targetAnchors[index] ?? ''
      void router.go(destination.pathname + destination.search + destination.hash)
      return false
    }
    return () => h(DefaultTheme.Layout, null, {
      'nav-bar-content-before': () => h(VersionSelector),
      'doc-before': () => h(ReleaseNotice)
    })
  } }),
  enhanceApp({ app }) {
    app.component('RepoHome', RepoHome)
  }
}
