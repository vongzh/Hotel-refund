<template>
  <a-layout class="layout-root">
    <a-layout-sider collapsible v-model:collapsed="collapsed" theme="light" width="220">
      <div class="brand">
        <span class="logo">旅</span>
        <strong v-if="!collapsed">StayOTA Agent</strong>
      </div>
      <a-menu
        mode="inline"
        :selected-keys="[route.path]"
        @click="onMenu"
        :items="menuItems"
      />
    </a-layout-sider>
    <a-layout>
      <a-layout-header class="header">
        <div>
          <h1>{{ title }}</h1>
          <p>结合 hotel 工作台体验 + Hotel-refund 受控 Workflow（.NET 10 / PG / Redis）</p>
        </div>
        <a-tag color="blue">Mock Demo</a-tag>
      </a-layout-header>
      <a-layout-content class="content">
        <router-view />
      </a-layout-content>
    </a-layout>
  </a-layout>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  DashboardOutlined,
  CustomerServiceOutlined,
} from '@ant-design/icons-vue'
import { h } from 'vue'

const collapsed = ref(false)
const route = useRoute()
const router = useRouter()
const title = computed(() => (route.meta.title as string) || 'StayOTA')

const menuItems = [
  { key: '/workspace', icon: () => h(CustomerServiceOutlined), label: '智能处理台' },
  { key: '/dashboard', icon: () => h(DashboardOutlined), label: '运营看板' },
]

function onMenu({ key }: { key: string }) {
  router.push(key)
}
</script>

<style scoped>
.layout-root { min-height: 100%; }
.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 64px;
  padding: 0 16px;
  border-bottom: 1px solid var(--border);
}
.logo {
  width: 32px; height: 32px; border-radius: 8px;
  background: linear-gradient(135deg, #1677ff, #69b1ff);
  color: #fff; display: grid; place-items: center; font-weight: 700;
}
.header {
  background: #fff;
  border-bottom: 1px solid var(--border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
  height: auto;
  min-height: 72px;
}
.header h1 { margin: 0; font-size: 18px; }
.header p { margin: 4px 0 0; color: var(--muted); font-size: 12px; }
.content { margin: 16px; min-height: calc(100vh - 104px); }
</style>
