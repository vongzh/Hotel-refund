<template>
  <div class="workspace">
    <div class="workflow-strip page-card">
      <div v-for="step in pipeline" :key="step.step" class="pipe-item" :class="`status-${step.status}`">
        <small>{{ step.step }}</small>
        <strong>{{ statusLabel(step.status) }}</strong>
      </div>
      <a-button @click="resetAndRun" :loading="loading">重置并运行</a-button>
    </div>

    <div class="grid">
      <aside class="page-card scenarios">
        <h3>Demo Cases</h3>
        <a-spin :spinning="loadingScenarios">
          <button
            v-for="item in scenarios"
            :key="item.code"
            class="scenario"
            :class="{ active: item.code === activeCode }"
            @click="selectScenario(item.code)"
          >
            <strong>{{ item.name }}</strong>
            <span>{{ item.goal }}</span>
            <em>{{ item.riskLevel }}</em>
          </button>
        </a-spin>
      </aside>

      <section class="page-card chat">
        <div class="order" v-if="decision">
          <div>
            <strong>{{ decision.order.hotelName }}</strong>
            <span>{{ decision.order.orderId }}</span>
          </div>
          <div class="meta">
            <span>入住 {{ decision.order.checkIn }}</span>
            <span>¥{{ decision.order.amount }}</span>
            <span>{{ decision.order.status }}</span>
          </div>
        </div>
        <div class="messages">
          <div class="bubble agent">我是酒店售后 Agent。会基于订单事实与成交政策给出可执行方案。</div>
          <div class="bubble user" v-if="userMessage">{{ userMessage }}</div>
          <div class="bubble agent" v-if="decision">{{ decision.reply }}</div>
        </div>
        <div class="composer">
          <a-button
            v-if="decision?.action === 'RequestEvidence'"
            type="primary"
            @click="uploadEvidence"
            :loading="loading"
          >
            上传示例凭证
          </a-button>
          <a-input
            v-model:value="draft"
            placeholder="输入用户诉求，或直接点左侧场景"
            @press-enter="runCustom"
          />
          <a-button type="primary" @click="runCustom" :loading="loading">发送</a-button>
        </div>
      </section>

      <aside class="page-card decision" v-if="decision">
        <h3>Agent 决策</h3>
        <div class="kpis">
          <div><span>意图置信度</span><strong>{{ Math.round(decision.intentConfidence * 100) }}%</strong></div>
          <div><span>风险分</span><strong>{{ decision.riskScore }}/100 · {{ decision.riskLevel }}</strong></div>
          <div><span>最终动作</span><strong>{{ actionLabel(decision.action) }}</strong></div>
        </div>
        <h4>决策轨迹</h4>
        <ol>
          <li v-for="step in decision.steps" :key="step.step">
            <strong :class="`status-${step.status}`">{{ step.step }}</strong>
            <span>{{ step.detail }}</span>
          </li>
        </ol>
        <h4>方案</h4>
        <p><strong>{{ decision.planTitle }}</strong></p>
        <p class="muted">{{ decision.planCopy }}</p>
        <h4>槽位</h4>
        <a-descriptions size="small" :column="1" bordered>
          <a-descriptions-item v-for="(v, k) in decision.slots" :key="k" :label="k">{{ v }}</a-descriptions-item>
        </a-descriptions>
      </aside>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { message } from 'ant-design-vue'
import { fetchScenarios, runAgentMessage } from '@/api/agent'
import type { AgentDecision, Scenario } from '@/types'

const scenarios = ref<Scenario[]>([])
const activeCode = ref('flight-cancelled')
const decision = ref<AgentDecision | null>(null)
const userMessage = ref('')
const draft = ref('')
const loading = ref(false)
const loadingScenarios = ref(false)

const pipeline = computed(() => decision.value?.steps ?? [
  { step: '意图识别', status: 'pending', detail: '' },
  { step: '槽位提取', status: 'pending', detail: '' },
  { step: '订单查询', status: 'pending', detail: '' },
  { step: '政策检索', status: 'pending', detail: '' },
  { step: '规则校验', status: 'pending', detail: '' },
  { step: '风险判断', status: 'pending', detail: '' },
  { step: '处理动作', status: 'pending', detail: '' },
])

onMounted(async () => {
  loadingScenarios.value = true
  try {
    scenarios.value = await fetchScenarios()
    await selectScenario(activeCode.value)
  } catch (e) {
    message.error('无法连接后端 API，请确认已启动 .NET 服务（5088）')
  } finally {
    loadingScenarios.value = false
  }
})

async function selectScenario(code: string) {
  activeCode.value = code
  const scenario = scenarios.value.find((s) => s.code === code)
  if (!scenario) return
  await run({
    message: scenario.entryMessage,
    scenarioCode: code,
    hasEvidence: false,
    resetDemo: true,
  })
}

async function uploadEvidence() {
  await run({
    message: userMessage.value || '已上传航班取消证明',
    scenarioCode: activeCode.value,
    hasEvidence: true,
    resetDemo: false,
  })
}

async function runCustom() {
  if (!draft.value.trim()) return
  await run({
    message: draft.value,
    scenarioCode: activeCode.value,
    hasEvidence: false,
    resetDemo: false,
  })
  draft.value = ''
}

async function resetAndRun() {
  await selectScenario(activeCode.value)
}

async function run(payload: {
  message: string
  scenarioCode?: string
  hasEvidence?: boolean
  resetDemo?: boolean
}) {
  loading.value = true
  userMessage.value = payload.message
  try {
    decision.value = await runAgentMessage(payload)
  } catch {
    message.error('Agent 调用失败')
  } finally {
    loading.value = false
  }
}

function statusLabel(status: string) {
  return ({ success: '成功', warning: '需关注', error: '失败', active: '进行中', pending: '待执行' } as Record<string, string>)[status] || status
}

function actionLabel(action: string) {
  return ({
    ConfirmCancel: '确认取消',
    RequestEvidence: '补充材料',
    NegotiateWithHotel: '酒店协商',
    HumanHandoff: '转人工',
    ExplainProgress: '同步进度',
    AutoRefund: '自动退款',
  } as Record<string, string>)[action] || action
}
</script>

<style scoped>
.workspace { display: flex; flex-direction: column; gap: 12px; }
.workflow-strip {
  display: grid;
  grid-template-columns: repeat(7, 1fr) auto;
  gap: 8px;
  align-items: center;
}
.pipe-item {
  background: #f8fafc;
  border-radius: 8px;
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.pipe-item small { color: var(--muted); }
.grid {
  display: grid;
  grid-template-columns: 240px 1fr 340px;
  gap: 12px;
  min-height: 640px;
}
.scenarios { display: flex; flex-direction: column; gap: 8px; }
.scenario {
  text-align: left;
  border: 1px solid var(--border);
  background: #fff;
  border-radius: 10px;
  padding: 10px;
  cursor: pointer;
  display: grid;
  gap: 4px;
}
.scenario.active { border-color: var(--primary); background: #e6f4ff; }
.scenario span, .scenario em { color: var(--muted); font-size: 12px; font-style: normal; }
.chat { display: flex; flex-direction: column; gap: 12px; }
.order {
  display: flex; justify-content: space-between; gap: 12px;
  padding-bottom: 12px; border-bottom: 1px solid var(--border);
}
.order .meta { display: flex; gap: 12px; color: var(--muted); font-size: 13px; }
.messages { flex: 1; display: flex; flex-direction: column; gap: 10px; overflow: auto; }
.bubble {
  max-width: 85%;
  padding: 10px 12px;
  border-radius: 12px;
  line-height: 1.5;
}
.bubble.agent { background: #f3f4f6; align-self: flex-start; }
.bubble.user { background: #1677ff; color: #fff; align-self: flex-end; }
.composer { display: flex; gap: 8px; }
.decision ol { padding-left: 18px; }
.decision li { margin-bottom: 8px; display: grid; gap: 2px; }
.kpis { display: grid; gap: 8px; margin-bottom: 12px; }
.kpis div {
  background: #f8fafc; border-radius: 8px; padding: 8px 10px;
  display: flex; justify-content: space-between; gap: 8px;
}
.muted { color: var(--muted); }
@media (max-width: 1100px) {
  .grid { grid-template-columns: 1fr; }
  .workflow-strip { grid-template-columns: 1fr 1fr; }
}
</style>
