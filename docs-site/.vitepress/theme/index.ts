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
    // VitePress preserves the hash when switching languages. Translate canonical
    // guide headings too, so a Russian section does not become a dead EN anchor.
    if (typeof window !== 'undefined') router.onBeforeRouteChange = to => {
      const repository = theme.value.release
      const destination = new URL(to, window.location.href)
      const currentLocale = /\/ru\//.test(window.location.pathname) ? 'ru' : 'en'
      const nextLocale = /\/ru\//.test(destination.pathname) ? 'ru' : 'en'
      if (destination.origin !== window.location.origin || currentLocale === nextLocale || !destination.hash) return
      const from = repository.guides[currentLocale].find(guide => withBase(guide.link) === window.location.pathname)
      const target = repository.guides[nextLocale].find(guide => withBase(guide.link) === destination.pathname)
      const hash = decodeURIComponent(destination.hash.slice(1))
      const index = from?.sections.findIndex(section => section.link.split('#')[1] === hash) ?? -1
      if (index >= 0 && target?.sections[index]) {
        void router.go(withBase(target.sections[index].link))
        return false
      }
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
