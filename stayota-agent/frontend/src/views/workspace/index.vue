<template>
  <div class="workspace">
    <div class="workflow-strip page-card">
      <div v-for="step in pipeline" :key="step.step" class="pipe-item" :class="`status-${step.status}`">
        <small>{{ step.step }}</small>
        <strong>{{ statusLabel(step.status) }}</strong>
      </div>
      <a-space>
        <a-button @click="resetAndRun" :loading="loading">重置场景</a-button>
      </a-space>
    </div>

    <div class="grid">
      <aside class="page-card scenarios">
        <h3>A–L 场景</h3>
        <a-spin :spinning="loadingScenarios">
          <button
            v-for="item in scenarios"
            :key="item.scenarioId"
            class="scenario"
            :class="{ active: item.scenarioId === activeId }"
            @click="selectScenario(item.scenarioId)"
          >
            <strong>{{ item.scenarioId }}. {{ item.title }}</strong>
            <span>{{ item.group }} · {{ item.riskLevel }}</span>
          </button>
        </a-spin>
        <h4>边界态</h4>
        <a-space direction="vertical" style="width:100%">
          <a-button block @click="runBoundary({ lowConfidence: true })">低置信度澄清</a-button>
          <a-button block danger @click="runBoundary({ serviceError: true })">订单服务异常</a-button>
        </a-space>
      </aside>

      <section class="page-card chat">
        <div class="order" v-if="decision">
          <div>
            <strong>{{ decision.order.hotelName }}</strong>
            <div class="sub">{{ decision.order.orderId }} · {{ decision.order.roomType }} × {{ decision.order.roomCount }}</div>
          </div>
          <div class="meta">
            <span>{{ decision.order.checkIn }}</span>
            <span>{{ decision.order.currency }} {{ decision.order.amount }}</span>
            <span>{{ decision.caseStatus }}</span>
          </div>
        </div>
        <div class="messages">
          <div class="bubble agent">我是酒店售后 Agent。结论与金额来自规则与 Tool，不会由模型自由生成。</div>
          <div class="bubble user" v-if="userMessage">{{ userMessage }}</div>
          <div class="bubble agent" v-if="decision">{{ decision.reply }}</div>
          <div class="bubble error" v-if="errorText">{{ errorText }}</div>
        </div>
        <div class="actions" v-if="decision">
          <a-button v-if="decision.action === 'RequestEvidence'" type="primary" @click="withEvidence" :loading="loading">上传示例凭证</a-button>
          <a-button v-if="decision.action === 'RequestInformation'" type="primary" @click="withReason" :loading="loading">补充无法入住原因</a-button>
          <a-button v-if="decision.action === 'ConfirmCancel' || decision.action === 'ChangeOrder'" type="primary" @click="confirmWrite" :loading="loading">确认执行写操作</a-button>
          <a-tag>{{ actionLabel(decision.action) }}</a-tag>
          <a-tag color="orange">{{ decision.conversationState }}</a-tag>
        </div>
        <div class="composer">
          <a-input v-model:value="draft" placeholder="输入诉求或点左侧场景" @press-enter="runCustom" />
          <a-button type="primary" @click="runCustom" :loading="loading">发送</a-button>
        </div>
      </section>

      <aside class="page-card decision" v-if="decision">
        <h3>Agent 决策</h3>
        <div class="kpis">
          <div><span>意图</span><strong>{{ Math.round(decision.intentConfidence * 100) }}% · {{ decision.intent }}</strong></div>
          <div><span>风险</span><strong>{{ decision.riskScore }}/100 · {{ decision.riskLevel }}</strong></div>
          <div><span>动作</span><strong>{{ actionLabel(decision.action) }}</strong></div>
        </div>
        <h4>决策轨迹</h4>
        <ol>
          <li v-for="step in decision.steps" :key="step.step">
            <strong :class="`status-${step.status}`">{{ step.step }}</strong>
            <span>{{ step.detail }}</span>
          </li>
        </ol>
        <h4>政策 Top3</h4>
        <div v-for="p in decision.policyMatches" :key="p.policyId" class="policy">
          <strong>{{ p.policyId }}</strong> · {{ p.score.toFixed(2) }}
          <div>{{ p.title }}</div>
        </div>
        <h4>Tool 序列</h4>
        <div class="tools">
          <a-tag v-for="t in decision.toolSequence" :key="t">{{ t }}</a-tag>
        </div>
        <h4 v-if="decision.ticket">工单摘要</h4>
        <div v-if="decision.ticket" class="ticket">
          <div>{{ decision.ticket.ticketId }} · {{ decision.ticket.priority }} / {{ decision.ticket.queue }}</div>
          <ul>
            <li v-for="f in decision.ticket.facts" :key="f">{{ f }}</li>
          </ul>
        </div>
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
const activeId = ref('G')
const decision = ref<AgentDecision | null>(null)
const userMessage = ref('')
const draft = ref('')
const loading = ref(false)
const loadingScenarios = ref(false)
const errorText = ref('')

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
    await selectScenario(activeId.value)
  } catch {
    message.error('无法连接后端 API :5088')
  } finally {
    loadingScenarios.value = false
  }
})

async function selectScenario(id: string) {
  activeId.value = id
  const scenario = scenarios.value.find((s) => s.scenarioId === id)
  if (!scenario) return
  await run({ message: scenario.entryMessage, scenarioId: id, resetDemo: true })
}

async function withEvidence() {
  await run({ message: userMessage.value || '已上传证明', scenarioId: activeId.value, hasEvidence: true })
}
async function withReason() {
  await run({
    message: '因家人临时住院无法入住，请协助与酒店沟通。',
    scenarioId: activeId.value,
    hasNegotiationReason: true,
  })
}
async function confirmWrite() {
  await run({
    message: userMessage.value,
    scenarioId: activeId.value,
    confirmWrite: true,
    idempotencyKey: `ui-${activeId.value}-${Date.now()}`,
  })
}
async function runBoundary(flags: Record<string, boolean>) {
  await run({
    message: flags.lowConfidence ? '我想问一下这个事情怎么处理' : userMessage.value || scenarios.value.find(s => s.scenarioId === activeId.value)?.entryMessage,
    scenarioId: activeId.value,
    ...flags,
  })
}
async function runCustom() {
  if (!draft.value.trim()) return
  await run({ message: draft.value, scenarioId: activeId.value })
  draft.value = ''
}
async function resetAndRun() {
  await selectScenario(activeId.value)
}

async function run(payload: Record<string, unknown>) {
  loading.value = true
  errorText.value = ''
  userMessage.value = String(payload.message || '')
  try {
    decision.value = await runAgentMessage(payload)
  } catch (e: unknown) {
    const err = e as { response?: { data?: { message?: string } } }
    errorText.value = err.response?.data?.message || 'Agent 调用失败'
    decision.value = null
  } finally {
    loading.value = false
  }
}

function statusLabel(status: string) {
  return ({ success: '成功', warning: '需关注', error: '失败', active: '进行中', pending: '待执行' } as Record<string, string>)[status] || status
}
function actionLabel(action: string) {
  return ({
    ConfirmCancel: '确认取消', RequestEvidence: '补充材料', RequestInformation: '补充信息',
    NegotiateWithHotel: '酒店协商', HumanHandoff: '转人工', ExplainProgress: '同步进度',
    Clarify: '澄清', ChangeOrder: '改期', FinanceReview: '财务核验', SpecialReview: '特殊审核',
    ServiceDispute: '服务争议', Recovery: '履约恢复', AutoRefund: '自动退款',
  } as Record<string, string>)[action] || action
}
</script>

<style scoped>
.workspace { display: flex; flex-direction: column; gap: 12px; }
.workflow-strip {
  display: grid; grid-template-columns: repeat(7, 1fr) auto; gap: 8px; align-items: center;
}
.pipe-item { background: #f8fafc; border-radius: 8px; padding: 8px 10px; display: flex; flex-direction: column; gap: 2px; }
.pipe-item small { color: var(--muted); }
.grid { display: grid; grid-template-columns: 260px 1fr 360px; gap: 12px; min-height: 680px; }
.scenarios { display: flex; flex-direction: column; gap: 8px; overflow: auto; }
.scenario {
  text-align: left; border: 1px solid var(--border); background: #fff; border-radius: 10px;
  padding: 10px; cursor: pointer; display: grid; gap: 4px;
}
.scenario.active { border-color: var(--primary); background: #e6f4ff; }
.scenario span { color: var(--muted); font-size: 12px; }
.chat { display: flex; flex-direction: column; gap: 12px; }
.order { display: flex; justify-content: space-between; gap: 12px; padding-bottom: 12px; border-bottom: 1px solid var(--border); }
.sub { color: var(--muted); font-size: 12px; margin-top: 4px; }
.order .meta { display: flex; gap: 12px; color: var(--muted); font-size: 13px; }
.messages { flex: 1; display: flex; flex-direction: column; gap: 10px; overflow: auto; }
.bubble { max-width: 88%; padding: 10px 12px; border-radius: 12px; line-height: 1.5; }
.bubble.agent { background: #f3f4f6; align-self: flex-start; }
.bubble.user { background: #1677ff; color: #fff; align-self: flex-end; }
.bubble.error { background: #fff1f0; color: #a8071a; align-self: flex-start; }
.actions, .composer { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
.decision ol { padding-left: 18px; }
.decision li { margin-bottom: 8px; display: grid; gap: 2px; }
.kpis { display: grid; gap: 8px; margin-bottom: 12px; }
.kpis div { background: #f8fafc; border-radius: 8px; padding: 8px 10px; display: flex; justify-content: space-between; gap: 8px; }
.policy { margin-bottom: 8px; font-size: 13px; }
.tools { display: flex; flex-wrap: wrap; gap: 4px; }
.ticket { background: #fff7e6; border-radius: 8px; padding: 8px 10px; font-size: 13px; }
@media (max-width: 1100px) {
  .grid { grid-template-columns: 1fr; }
  .workflow-strip { grid-template-columns: 1fr 1fr; }
}
</style>
