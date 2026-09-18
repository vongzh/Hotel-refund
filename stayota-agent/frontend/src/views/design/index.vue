<template>
  <div class="design">
    <div class="page-card hero">
      <div>
        <h2>受控 Agent Workflow</h2>
        <p>对齐 Hotel-refund：显式状态机 + Tool 状态白名单 + Verifier。LLM 只负责理解与表达。</p>
      </div>
      <a-space>
        <a-button @click="runWorkflows" :loading="loading">跑 A–L Workflow</a-button>
        <a-button type="primary" @click="$router.push('/workspace')">开始场景模拟</a-button>
      </a-space>
    </div>
    <a-alert v-if="summary" type="success" show-icon :message="summary" />

    <div class="page-card">
      <h3>状态机主链路</h3>
      <div class="flow">
        <div v-for="n in nodes" :key="n" class="node">{{ n }}</div>
      </div>
      <h4>Tool 状态映射（节选）</h4>
      <a-table size="small" :columns="stateCols" :data-source="stateMap" :pagination="{ pageSize: 6 }" row-key="tool" />
    </div>

    <div class="two">
      <div class="page-card">
        <h3>33 Tool 契约</h3>
        <a-table size="small" :columns="toolCols" :data-source="tools" :pagination="{ pageSize: 8 }" row-key="name">
          <template #bodyCell="{ column, record }">
            <template v-if="column.dataIndex === 'allowedConversationStates'">
              <a-tag v-for="s in record.allowedConversationStates?.slice(0, 3) || []" :key="s">{{ s }}</a-tag>
            </template>
          </template>
        </a-table>
      </div>
      <div class="page-card">
        <h3>设计边界</h3>
        <ul>
          <li>成交政策快照优先于聊天记忆</li>
          <li>写操作需要确认令牌、订单版本、幂等键</li>
          <li>Tool 仅允许在契约声明的 conversation state 执行</li>
          <li>L3 禁止自动资金写，必须人工升级</li>
          <li>Verifier 校验金额/风险/话术事实边界</li>
          <li>A–L 全场景可用显式 Workflow 回放</li>
        </ul>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { message } from 'ant-design-vue'
import { fetchTools, runAllWorkflows } from '@/api/agent'
import type { ToolContract } from '@/types'

const tools = ref<ToolContract[]>([])
const loading = ref(false)
const summary = ref('')
const toolCols = [
  { title: 'Tool', dataIndex: 'name' },
  { title: '模式', dataIndex: 'mode', width: 90 },
  { title: '允许状态', dataIndex: 'allowedConversationStates' },
]
const stateCols = [
  { title: 'Tool', dataIndex: 'tool' },
  { title: '进入状态', dataIndex: 'state' },
]
const stateMap = [
  { tool: 'get_order_detail', state: 'ORDER_CONFIRMED' },
  { tool: 'calculate_refund_quote', state: 'DECISION_READY' },
  { tool: 'submit_cancellation', state: 'CONFIRMATION_REQUIRED' },
  { tool: 'create_supplier_case', state: 'CONFIRMATION_REQUIRED' },
  { tool: 'create_human_handoff', state: 'OPTION_PRESENTED' },
  { tool: 'get_refund_status', state: 'TRACKING_REFUND' },
]
const nodes = [
  'START', 'INTENT_READY', 'ORDER_CONFIRMED', 'FACTS_REQUIRED',
  'DECISION_READY', 'CONFIRMATION_REQUIRED / OPTION_PRESENTED',
  'TRACKING_REFUND / WAITING_EXTERNAL', 'ESCALATED / RESOLVED',
]

onMounted(async () => {
  try { tools.value = await fetchTools() } catch { tools.value = [] }
})

async function runWorkflows() {
  loading.value = true
  try {
    const res = await runAllWorkflows()
    summary.value = `A–L Workflow ${res.succeeded}/${res.total} succeeded`
    message.success(summary.value)
  } catch {
    message.error('Workflow 运行失败')
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.design { display: grid; gap: 12px; }
.hero { display: flex; justify-content: space-between; align-items: center; gap: 16px; }
.hero h2 { margin: 0 0 6px; }
.hero p { margin: 0; color: var(--muted); }
.flow { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 16px; }
.node {
  background: #e6f4ff; border: 1px solid #91caff; border-radius: 999px;
  padding: 8px 12px; font-size: 13px;
}
.two { display: grid; grid-template-columns: 2fr 1fr; gap: 12px; }
ul { margin: 0; padding-left: 18px; line-height: 1.8; color: #374151; }
@media (max-width: 1100px) { .two { grid-template-columns: 1fr; } }
</style>
