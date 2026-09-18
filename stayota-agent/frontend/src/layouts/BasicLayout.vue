<template>
  <div class="app-shell">
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark">旅</span>
        <div>
          <strong>StayOTA Agent</strong>
          <small>hotel 工作台 × Hotel-refund 受控核</small>
        </div>
      </div>
      <nav class="primary-nav" aria-label="主导航">
        <button
          v-for="item in nav"
          :key="item.path"
          class="nav-item"
          :class="{ 'is-active': route.path === item.path }"
          @click="router.push(item.path)"
        >
          {{ item.label }}
        </button>
      </nav>
      <div class="demo-badge">
        <span class="health">{{ healthText }}</span>
        <span>Mock Demo</span>
      </div>
    </header>
    <main class="page-frame">
      <router-view />
    </main>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { health } from '@/api/agent'

const route = useRoute()
const router = useRouter()
const healthText = ref('连接中…')

const nav = [
  { path: '/design', label: 'Agent 设计' },
  { path: '/workspace', label: '智能处理台' },
  { path: '/dashboard', label: '运营看板' },
]

onMounted(async () => {
  try {
    const h = await health() as {
      scenarios: number
      tools: number
      aiProvider?: string
      agent?: string
    }
    healthText.value = `${h.scenarios} 场景 / ${h.tools} Tools`
  } catch {
    healthText.value = '后端未连接'
  }
})
</script>
