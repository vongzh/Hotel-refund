<template>
  <div class="design">
    <div class="page-card hero">
      <div>
        <h2>受控 Agent Workflow</h2>
        <p>LLM 只负责理解与表达；金额、政策、权限、状态由规则引擎与 Tool 网关决定。</p>
      </div>
      <a-button type="primary" @click="$router.push('/workspace')">开始场景模拟</a-button>
    </div>

    <div class="page-card">
      <h3>决策链路</h3>
      <div class="flow">
        <div v-for="n in nodes" :key="n" class="node">{{ n }}</div>
      </div>
    </div>

    <div class="two">
      <div class="page-card">
        <h3>33 Tool 契约</h3>
        <a-table size="small" :columns="toolCols" :data-source="tools" :pagination="{ pageSize: 8 }" row-key="name" />
      </div>
      <div class="page-card">
        <h3>设计边界</h3>
        <ul>
          <li>成交政策快照优先于聊天记忆</li>
          <li>写操作需要确认令牌、订单版本、幂等键</li>
          <li>L3 禁止自动资金写，必须人工升级</li>
          <li>C 端只看结论与方案，Trace 用于质检</li>
          <li>未知写结果先查 get_action_result 再重试</li>
        </ul>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { fetchTools } from '@/api/agent'
import type { ToolContract } from '@/types'

const tools = ref<ToolContract[]>([])
const toolCols = [
  { title: 'Tool', dataIndex: 'name' },
  { title: '模式', dataIndex: 'mode', width: 90 },
  { title: '用途', dataIndex: 'purpose' },
]
const nodes = [
  'Session Memory', '意图/风险理解', '确认订单', 'READ Gate',
  '读取业务事实', '政策匹配', '规则选路', 'WRITE Gate / HITL', 'Verifier + Trace',
]

onMounted(async () => {
  try { tools.value = await fetchTools() } catch { tools.value = [] }
})
</script>

<style scoped>
.design { display: grid; gap: 12px; }
.hero { display: flex; justify-content: space-between; align-items: center; gap: 16px; }
.hero h2 { margin: 0 0 6px; }
.hero p { margin: 0; color: var(--muted); }
.flow { display: flex; flex-wrap: wrap; gap: 8px; }
.node {
  background: #e6f4ff; border: 1px solid #91caff; border-radius: 999px;
  padding: 8px 12px; font-size: 13px;
}
.two { display: grid; grid-template-columns: 2fr 1fr; gap: 12px; }
ul { margin: 0; padding-left: 18px; line-height: 1.8; color: #374151; }
@media (max-width: 1100px) { .two { grid-template-columns: 1fr; } }
</style>
