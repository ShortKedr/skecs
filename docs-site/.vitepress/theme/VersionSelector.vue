<script setup>
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { useData } from 'vitepress'
import { versionDestination } from './versions.mjs'

const { theme, lang } = useData()
const ru = computed(() => lang.value.startsWith('ru'))
const root = ref()
const trigger = ref()
const menu = ref()
const open = ref(false)
const active = ref(0)
const label = computed(() => ru.value ? 'Версия документации' : 'Documentation version')

async function show(index = theme.value.releases.findIndex(release => release.version === theme.value.release.version)) {
  active.value = Math.max(0, index)
  open.value = true
  await nextTick()
  menu.value?.querySelectorAll('[role="menuitemradio"]')[active.value]?.focus()
}
function close(restoreFocus = false) {
  open.value = false
  if (restoreFocus) trigger.value?.focus()
}
function onKeydown(event) {
  if (event.key === 'Escape' && open.value) {
    event.preventDefault()
    event.stopPropagation()
    close(true)
  } else if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
    event.preventDefault()
    const last = theme.value.releases.length - 1
    const index = event.key === 'Home' ? 0 : event.key === 'End' ? last
      : !open.value ? (event.key === 'ArrowUp' ? last : 0)
      : (active.value + (event.key === 'ArrowDown' ? 1 : last)) % (last + 1)
    void show(index)
  }
}
function onOutsidePointer(event) {
  if (!root.value?.contains(event.target)) close()
}
function onFocusOut(event) {
  if (!root.value?.contains(event.relatedTarget)) close()
}
onMounted(() => document.addEventListener('pointerdown', onOutsidePointer))
onUnmounted(() => document.removeEventListener('pointerdown', onOutsidePointer))

function selectVersion(version) {
  close(true)
  if (version === theme.value.release.version) return
  // Each release has its own VitePress bundle and search index.
  window.location.assign(versionDestination(theme.value.releases, theme.value.release.version,
    version, window.location.pathname, window.location.hash, theme.value.docsBase))
}
</script>

<template>
  <div ref="root" class="docs-version-select" @keydown="onKeydown" @focusout="onFocusOut">
    <button ref="trigger" type="button" class="docs-version-trigger" :aria-label="`${label}: ${theme.release.version}`"
      aria-haspopup="menu" aria-controls="docs-version-menu" :aria-expanded="open" @click="open ? close() : show()">
      <span>v{{ theme.release.version }}</span>
      <svg viewBox="0 0 16 16" fill="none" aria-hidden="true"><path d="m4 6 4 4 4-4" /></svg>
    </button>
    <Transition name="version-menu">
      <div v-if="open" id="docs-version-menu" ref="menu" class="docs-version-menu" role="menu" :aria-label="label">
        <div class="docs-version-menu-title" aria-hidden="true">{{ label }}</div>
        <button v-for="(release, index) in theme.releases" :key="release.version" type="button" role="menuitemradio"
          class="docs-version-option" :aria-checked="release.version === theme.release.version" :tabindex="index === active ? 0 : -1"
          @focus="active = index" @click="selectVersion(release.version)">
          <span class="docs-version-number">{{ release.version }}</span>
          <span v-if="release.version === theme.defaultVersion" class="docs-version-latest">{{ ru ? 'Последняя' : 'Latest' }}</span>
          <svg v-if="release.version === theme.release.version" viewBox="0 0 16 16" fill="none" aria-hidden="true"><path d="m3 8 3 3 7-7" /></svg>
        </button>
      </div>
    </Transition>
  </div>
</template>
