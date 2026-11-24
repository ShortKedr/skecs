<script setup>
import { computed, nextTick, onUnmounted, ref } from 'vue'
import { useData, withBase } from 'vitepress'

const { lang, theme } = useData()
const repository = computed(() => theme.value.release)
const ru = computed(() => lang.value.startsWith('ru'))
const base = computed(() => withBase(ru.value ? '/ru' : '/').replace(/\/$/, ''))
const demoUrl = computed(() => {
  const docsBase = theme.value.docsBase
  const demo = docsBase === '/' ? 'https://shortkedr.github.io/skecs/demo/' : `${docsBase}demo/`
  return ru.value ? `${demo}?lang=ru` : demo
})
const guide = computed(() => repository.value.guides[ru.value ? 'ru' : 'en'][0])
const tab = ref('code')
const copied = ref(false)
const copyFailed = ref(false)
let copyTimer
onUnmounted(() => clearTimeout(copyTimer))
async function copyCode() {
  try {
    await navigator.clipboard.writeText(tab.value === 'code' ? repository.value.sample.source : repository.value.sample.output)
    copied.value = true
    copyFailed.value = false
    clearTimeout(copyTimer)
    copyTimer = setTimeout(() => { copied.value = false }, 2000)
  } catch {
    copyFailed.value = true
  }
}
async function switchTab(event) {
  if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  tab.value = event.key === 'Home' ? 'code' : event.key === 'End' ? 'output' : tab.value === 'code' ? 'output' : 'code'
  await nextTick()
  document.getElementById(`sample-${tab.value}-tab`)?.focus()
}
const types = computed(() => ['World', 'Entity', 'Query-2', 'QueryBuilder', 'ComponentAccessor-1']
  .map(name => repository.value.api.find(item => item.link === `/api/${repository.value.product}.` + name)).filter(Boolean))
const text = computed(() => ru.value ? {
  eyebrow: 'ДОКУМЕНТАЦИЯ РЕЛИЗА', title: `Начните с ${repository.value.product}.`,
  intro: 'Создавайте сущности, описывайте данные компонентами и обрабатывайте их через типизированные запросы. Небольшое ECS-ядро для C# и .NET.',
  install: 'Установка', installNote: `Подключите ${repository.value.product} к .NET, Unity или Godot .NET.`,
  guide: 'Мир, сущности и запросы', guideNote: 'Разберитесь с компонентами, системами и временем жизни данных.',
  api: 'Справочник API', apiNote: 'Найдите нужный тип, метод или сигнатуру.',
  example: 'Посмотрите на код', exampleNote: `Пример из релиза ${repository.value.version}, проверенный с его пакетом.`,
  code: 'Код', output: 'Результат', copy: 'Копировать', copied: 'Скопировано', failed: 'Не удалось скопировать. Выделите код вручную.',
  fullExample: 'Открыть пример и запуск', run: 'Запустить после подключения пакета',
  contents: 'В руководстве', references: 'Часто используемые типы',
  memory: 'Работаете с ref?', memoryNote: 'Ссылки на компоненты и указатели чанков действительны до структурного изменения или освобождения мира.',
  memoryLink: 'Правила времени жизни', next: 'После первого примера',
  perf: 'Производительность и память', perfNote: 'Массовое создание, accessors и диагностика хранилища.',
  battle: 'SpaceBattle', battleNote: 'Посмотрите на SKECS в игровом примере с автономными флотами.',
  current: 'Текущая версия',
  scope: 'Эта документация охватывает выпущенное ядро, включая миры и сущности, запросы и системы, хранение компонентов и диагностику.',
  modules: 'Дополнительные модули'
} : {
  eyebrow: 'RELEASE DOCUMENTATION', title: `Get started with ${repository.value.product}.`,
  intro: 'Create entities, describe data with components, and process it with typed queries. A small ECS core for C# and .NET.',
  install: 'Installation', installNote: `Add ${repository.value.product} to a .NET, Unity, or Godot .NET project.`,
  guide: 'Worlds, entities & queries', guideNote: 'Understand components, systems, and the lifetime of your data.',
  api: 'API reference', apiNote: 'Find a type, method, or exact signature.',
  example: 'Start with real code', exampleNote: `A sample from release ${repository.value.version}, verified against its package.`,
  code: 'Code', output: 'Output', copy: 'Copy', copied: 'Copied', failed: 'Could not copy. Select the code manually.',
  fullExample: 'Open the example and run instructions', run: 'Run after adding the package',
  contents: 'Inside the guide', references: 'Common API types',
  memory: 'Working with ref?', memoryNote: 'Component references and chunk pointers are valid until a structural change or world disposal.',
  memoryLink: 'Read the lifetime rules', next: 'After your first example',
  perf: 'Performance & memory', perfNote: 'Batch creation, component accessors, and storage diagnostics.',
  battle: 'SpaceBattle', battleNote: 'See SKECS in an autonomous fleet battle example.',
  current: 'Current version',
  scope: 'This documentation covers the released core, including worlds and entities, queries and systems, component storage, and diagnostics.',
  modules: 'Optional modules'
})
</script>

<template>
  <div class="docs-home">
    <header class="docs-welcome">
      <div class="docs-kicker"><span>{{ text.eyebrow }}</span><span class="docs-version" :aria-label="text.current">v{{ repository.version }}</span></div>
      <h1>{{ text.title }}</h1>
      <p class="docs-lead">{{ text.intro }}</p>
      <p v-if="repository.version === '0.2.0'">{{ text.scope }}</p>
      <div class="docs-platforms"><span v-for="target in repository.targets" :key="target">{{ target.replace('netstandard', '.NET Standard ').replace(/^net(\d)/, '.NET $1') }}</span><span class="docs-license">MIT</span></div>
    </header>

    <nav class="docs-entry" :aria-label="ru ? 'С чего начать' : 'Where to start'">
      <a class="docs-entry-card docs-entry-primary" :href="base + '/guide/installation'">
        <span class="docs-entry-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none"><path d="M12 3v12m-4-4 4 4 4-4M5 15v5h14v-5" /></svg></span>
        <strong>{{ text.install }}</strong><span>{{ text.installNote }}</span><b class="docs-card-arrow" aria-hidden="true">→</b>
      </a>
      <a class="docs-entry-card" :href="base + '/guide/using'">
        <span class="docs-entry-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none"><path d="M4 4h6l2 2 2-2h6v15h-6l-2 2-2-2H4zM12 6v15" /></svg></span>
        <strong>{{ text.guide }}</strong><span>{{ text.guideNote }}</span><b class="docs-card-arrow" aria-hidden="true">→</b>
      </a>
      <a class="docs-entry-card" :href="base + '/api/'">
        <span class="docs-entry-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none"><path d="m8 7-5 5 5 5m8-10 5 5-5 5m-3-14-2 18" /></svg></span>
        <strong>{{ text.api }}</strong><span>{{ text.apiNote }}</span><b class="docs-card-arrow" aria-hidden="true">→</b>
      </a>
    </nav>

    <section v-if="repository.modules?.length" aria-labelledby="modules-title">
      <h2 id="modules-title">{{ text.modules }}</h2>
      <ul>
        <li v-for="module in repository.modules" :key="module.id">
          <a :href="base + '/' + module.route">{{ module.title[ru ? 'ru' : 'en'] }} {{ module.version }}</a>
        </li>
      </ul>
    </section>

    <div class="docs-workspace">
      <section class="docs-example" aria-labelledby="example-title">
        <div class="docs-section-heading"><h2 id="example-title">{{ text.example }}</h2><p>{{ text.exampleNote }}</p></div>
        <div class="docs-editor">
          <div class="docs-editor-toolbar">
            <div class="docs-tabs" role="tablist" :aria-label="text.example" @keydown="switchTab">
              <button id="sample-code-tab" role="tab" :aria-selected="tab === 'code'" :tabindex="tab === 'code' ? 0 : -1" aria-controls="sample-code-panel" @click="tab = 'code'">{{ text.code }}</button>
              <button id="sample-output-tab" role="tab" :aria-selected="tab === 'output'" :tabindex="tab === 'output' ? 0 : -1" aria-controls="sample-output-panel" @click="tab = 'output'">{{ text.output }}</button>
            </div>
            <button class="docs-copy" :class="{ 'is-copied': copied }" @click="copyCode"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path :d="copied ? 'm5 12 4 4L19 6' : 'M8 8h12v13H8zM16 8V3H3v13h5'" /></svg><span class="docs-copy-label" aria-live="polite" aria-atomic="true"><Transition name="copy-feedback" mode="out-in"><span :key="copied">{{ copied ? text.copied : text.copy }}</span></Transition></span></button>
          </div>
          <div v-show="tab === 'code'" id="sample-code-panel" class="docs-source" role="tabpanel" aria-labelledby="sample-code-tab" tabindex="0" v-html="repository.sample.html"></div>
          <pre v-show="tab === 'output'" id="sample-output-panel" class="docs-output" role="tabpanel" aria-labelledby="sample-output-tab" tabindex="0"><code>{{ repository.sample.output }}</code></pre>
          <p v-if="copyFailed" class="docs-copy-error" role="status">{{ text.failed }}</p>
          <div class="docs-editor-footer"><span>{{ repository.sample ? repository.product + '.Sample / Program.cs' : '' }}</span><a :href="base + '/guide/sample'">{{ text.fullExample }} <span aria-hidden="true">↗</span></a></div>
        </div>
        <div class="docs-run"><span>{{ text.run }}</span><code>dotnet run -c Release</code></div>
      </section>

      <aside class="docs-shortcuts">
        <section aria-labelledby="guide-title"><h2 id="guide-title">{{ text.contents }}</h2><ul class="docs-topic-list"><li v-for="(section, index) in guide.sections" :key="section.link"><a :href="withBase(section.link)"><span class="docs-topic-number">{{ String(index + 1).padStart(2, '0') }}</span><span>{{ section.text }}</span><b aria-hidden="true">›</b></a></li></ul></section>
        <section class="docs-api-shortcuts" aria-labelledby="api-title"><h2 id="api-title">{{ text.references }}</h2><a v-for="type in types" :key="type.link" :href="base + type.link"><code>{{ type.text }}</code><span aria-hidden="true">↗</span></a></section>
      </aside>
    </div>

    <aside class="docs-lifetime"><span class="docs-lifetime-icon" aria-hidden="true">i</span><div><strong>{{ text.memory }}</strong><p>{{ text.memoryNote }}</p></div><a :href="withBase(guide.sections[1].link)">{{ text.memoryLink }} →</a></aside>

    <section class="docs-next" aria-labelledby="next-title"><h2 id="next-title">{{ text.next }}</h2><div><a :href="base + '/guide/performance'"><strong>{{ text.perf }} <span aria-hidden="true">→</span></strong><p>{{ text.perfNote }}</p></a><a class="docs-demo-link" :href="demoUrl" target="_blank" rel="noreferrer"><strong>{{ text.battle }} <span aria-hidden="true">↗</span></strong><p>{{ text.battleNote }}</p></a></div></section>
  </div>
</template>
