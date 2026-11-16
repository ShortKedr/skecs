<script setup>
import { computed, onMounted, ref } from 'vue'
import { useData } from 'vitepress'
const { theme, lang } = useData()
const ru = computed(() => lang.value.startsWith('ru'))
const unavailable = ref(false)
onMounted(() => { unavailable.value = new URLSearchParams(window.location.search).has('unavailable') })
</script>

<template>
  <div class="docs-release-notice">
    <p v-if="unavailable" role="status">{{ ru ? 'Этой страницы нет в выбранной версии. Открыт её обзор.' : 'This page is unavailable in the selected version. Its overview is shown instead.' }}</p>
    <span>{{ theme.release.packageId }} <strong>{{ theme.release.version }}</strong></span>
    <a :href="theme.release.releaseUrl" target="_blank" rel="noreferrer">{{ ru ? 'Релиз ↗' : 'Release ↗' }}</a>
  </div>
</template>
