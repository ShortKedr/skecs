import { defineConfig } from 'vitepress'
import { readFileSync } from 'node:fs'

const version = process.env.SKECS_DOCS_VERSION
if (!version || !/^\d+\.\d+\.\d+$/.test(version)) throw new Error('Build with scripts/docs.mjs to select a registered release')
const metadata = JSON.parse(readFileSync(new URL(`./cache/releases/${version}/repository.json`, import.meta.url), 'utf8'))
const { release: repository } = metadata
const api = repository.api
const docsBase = process.env.SKECS_DOCS_BASE || '/'
const releaseBase = `${docsBase}${version}/`

function locale(ru: boolean) {
  const base = ru ? '/ru' : ''
  const types = api.map((item: { text: string, link: string }) => ({
    text: item.text.replaceAll('<', '&lt;').replaceAll('>', '&gt;'), link: base + item.link
  }))
  const commonNames = ['World', 'WorldOptions', 'Entity', 'Query-2', 'QueryBuilder']
  const commonTypes = commonNames.map(name => types.find(item => item.link === `${base}/api/${repository.product}.${name}`)).filter(Boolean)
  const sidebar = [
    { text: ru ? 'Начало работы' : 'Getting started', items: [
      { text: ru ? 'Обзор' : 'Overview', link: `${base}/` },
      { text: ru ? 'Установка' : 'Installation', link: `${base}/guide/installation` },
      { text: ru ? 'Рабочий пример' : 'Runnable example', link: `${base}/guide/sample` }
    ] },
    { text: ru ? 'Руководства' : 'Guides', items: [
      { text: ru ? 'Мир, сущности и запросы' : 'Worlds, entities & queries', link: `${base}/guide/using` },
      { text: ru ? 'Производительность и память' : 'Performance & memory', link: `${base}/guide/performance` },
      ...repository.guides[ru ? 'ru' : 'en'].filter(item => /\/guide\/(migration|release-notes)$/.test(item.link))
        .map(item => ({ text: item.title, link: item.link }))
    ] },
    { text: ru ? 'Справочник API' : 'API reference', items: [
      { text: ru ? 'Обзор API' : 'API overview', link: `${base}/api/` },
      ...commonTypes
    ] },
    { text: ru ? 'Другие типы API' : 'More API types', collapsed: true, items: types.filter(item => !commonTypes.includes(item)) }
  ]
  return {
    label: ru ? 'Русский' : 'English',
    lang: ru ? 'ru-RU' : 'en',
    title: `${repository.product} ${version}`,
    description: ru ? 'Документация ECS-фреймворка для C# и .NET' : 'A small chunk-based ECS for C# and .NET',
    themeConfig: {
      ...metadata,
      nav: [
        { text: ru ? 'Руководства' : 'Manual', link: `${base}/guide/using` },
        { text: 'API', link: `${base}/api/` },
        { text: ru ? 'Пример' : 'Sample', link: `${base}/guide/sample` }
      ],
      sidebar: { [`${base}/`]: sidebar },
      outline: { label: ru ? 'На этой странице' : 'On this page', level: [2, 3] },
      docFooter: { prev: ru ? 'Предыдущая' : 'Previous page', next: ru ? 'Следующая' : 'Next page' },
      sidebarMenuLabel: ru ? 'Разделы' : 'Menu',
      returnToTopLabel: ru ? 'Наверх' : 'Return to top',
      langMenuLabel: ru ? 'Язык' : 'Language',
      darkModeSwitchLabel: ru ? 'Оформление' : 'Appearance',
      lightModeSwitchTitle: ru ? 'Светлая тема' : 'Switch to light theme',
      darkModeSwitchTitle: ru ? 'Тёмная тема' : 'Switch to dark theme',
      footer: { message: ru ? 'Код и документация — MIT.' : 'Code and documentation released under MIT.' }
    }
  }
}

export default defineConfig({
  title: `${repository.product} ${version}`,
  base: releaseBase,
  srcDir: `.vitepress/cache/releases/${version}/pages`,
  outDir: `.vitepress/dist/${version}`,
  cacheDir: `.vitepress/cache/vite/${version}`,
  cleanUrls: true,
  appearance: 'dark',
  srcExclude: ['node_modules/**'],
  head: [['link', { rel: 'icon', type: 'image/png', href: `${releaseBase}logo-32.png` }]],
  locales: { root: locale(false), ru: locale(true) },
  themeConfig: {
    docsBase,
    siteTitle: 'SKECS',
    logo: '/logo-200.png',
    socialLinks: [{ icon: 'github', link: 'https://github.com/ShortKedr/skecs' }],
    search: {
      provider: 'local',
      options: {
        locales: {
          ru: {
            translations: {
              button: { buttonText: 'Поиск', buttonAriaLabel: 'Поиск по документации' },
              modal: {
                noResultsText: 'Ничего не найдено', resetButtonTitle: 'Очистить поиск',
                displayDetails: 'Показать подробности', backButtonTitle: 'Закрыть поиск',
                footer: { selectText: 'выбрать', navigateText: 'перейти', closeText: 'закрыть' }
              }
            }
          }
        }
      }
    }
  }
})
