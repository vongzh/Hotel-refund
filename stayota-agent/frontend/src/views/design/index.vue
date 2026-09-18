<template>
  <div class="design">
    <div class="workspace-heading">
      <div>
        <h1>Agent 设计</h1>
        <p>对齐 Hotel-refund：显式 Workflow + Tool 状态白名单 + Verifier。LLM 只理解与表达。</p>
      </div>
      <div class="heading-actions">
        <button class="ghost-btn" :disabled="loading" @click="runWorkflows">跑 A–L Workflow</button>
        <button class="primary-btn" @click="$router.push('/workspace')">开始场景模拟</button>
      </div>
    </div>

    <div v-if="summary" class="banner success">{{ summary }}</div>

    <section class="panel dflow-shell" aria-label="Agent 完整工作流程">
      <div class="dflow-toolbar">
        <div>
          <strong>受控核流程图</strong>
          <p>主链路 · 决策菱形 · Tool / 权限门 · Memory Trace</p>
        </div>
        <div class="dflow-legend">
          <span class="dflow-key">LLM / 规则</span>
          <span class="dflow-key" data-tone="tool">Tool</span>
          <span class="dflow-key" data-tone="guard">权限 / 人工</span>
          <span class="dflow-key" data-tone="memory">Memory / Trace</span>
        </div>
      </div>

      <div class="dflow-viewport">
        <div class="dflow-canvas">
          <svg class="dflow-lines" viewBox="0 0 980 720" aria-hidden="true">
            <defs>
              <marker id="flow-arrow" markerWidth="8" markerHeight="8" refX="6" refY="3" orient="auto">
                <path d="M0,0 L6,3 L0,6 Z" fill="oklch(0.58 0.132 225)" />
              </marker>
            </defs>
            <path data-tone="main" d="M120,70 L120,130" marker-end="url(#flow-arrow)" />
            <path data-tone="main" d="M120,190 L120,240" marker-end="url(#flow-arrow)" />
            <path data-tone="main" d="M120,300 L120,350" marker-end="url(#flow-arrow)" />
            <path data-tone="main" d="M120,410 L260,410" marker-end="url(#flow-arrow)" />
            <path data-tone="main" d="M380,410 L480,410" marker-end="url(#flow-arrow)" />
            <path d="M540,370 L540,300" marker-end="url(#flow-arrow)" />
            <path d="M540,450 L540,520" marker-end="url(#flow-arrow)" />
            <path data-tone="guard" d="M600,410 L720,410" marker-end="url(#flow-arrow)" />
            <path data-tone="main" d="M840,410 L840,500" marker-end="url(#flow-arrow)" />
            <path data-tone="loop" d="M840,560 L840,620 L120,620 L120,70" />
            <path data-tone="memory" d="M260,150 L420,150" marker-end="url(#flow-arrow)" />
            <path data-tone="memory" d="M260,270 L420,270" marker-end="url(#flow-arrow)" />
            <text x="250" y="395">风险分层</text>
            <text x="620" y="395">确认 / 幂等</text>
          </svg>

          <div class="dflow-node" style="left:40px;top:30px" data-tone="agent">
            <small>01 进线</small>
            <strong>意图 / 槽位</strong>
          </div>
          <div class="dflow-node" style="left:40px;top:140px" data-tone="tool">
            <small>Tool</small>
            <strong>查单 · 政策快照</strong>
          </div>
          <div class="dflow-node" style="left:40px;top:250px" data-tone="agent">
            <small>规则引擎</small>
            <strong>报价 · 硬规则</strong>
          </div>
          <div class="dflow-node" style="left:40px;top:360px" data-tone="agent">
            <small>决策准备</small>
            <strong>DECISION_READY</strong>
          </div>
          <div class="dflow-node diamond" style="left:280px;top:372px" data-tone="guard">
            <strong>L1 / L2 / L3</strong>
          </div>
          <div class="dflow-node" style="left:480px;top:250px" data-tone="tool">
            <small>L1 自动</small>
            <strong>确认取消路径</strong>
          </div>
          <div class="dflow-node" style="left:480px;top:480px" data-tone="guard">
            <small>L3</small>
            <strong>人工升级 / 工单</strong>
          </div>
          <div class="dflow-node" style="left:720px;top:360px" data-tone="guard">
            <small>写门禁</small>
            <strong>Token · Version · Idem</strong>
          </div>
          <div class="dflow-node" style="left:720px;top:510px" data-tone="memory">
            <small>闭环</small>
            <strong>Trace · Case · Eval</strong>
          </div>
          <div class="dflow-node" style="left:420px;top:110px" data-tone="memory">
            <small>Memory</small>
            <strong>Session / Redis</strong>
          </div>
          <div class="dflow-node" style="left:420px;top:230px" data-tone="memory">
            <small>Audit</small>
            <strong>ToolAudit · PG</strong>
          </div>

          <div class="dflow-phase" style="top: 80px"><span>02 · 核实订单与事实</span></div>
          <div class="dflow-phase" style="top: 330px"><span>03 · 决策与路由</span></div>
          <div class="dflow-phase" style="top: 560px"><span>04 · 结果闭环</span></div>
        </div>
      </div>

      <div class="dflow-summary">
        <div><span>进线</span><strong>意图置信度门</strong></div>
        <div><span>事实</span><strong>订单 + 政策 Top3</strong></div>
        <div><span>决策</span><strong>规则 / 风险分层</strong></div>
        <div><span>闭环</span><strong>确认写 + Trace</strong></div>
      </div>
    </section>

    <div class="two">
      <section class="panel pad">
        <div class="section-head">
          <h2>33 Tool 契约</h2>
          <span class="status-pill info">{{ tools.length }} registered</span>
        </div>
        <div class="tool-table">
          <div class="tool-row head">
            <span>Tool</span><span>模式</span><span>允许状态</span><span>用途</span>
          </div>
          <div class="tool-row" v-for="t in tools.slice(0, 12)" :key="t.name">
            <strong>{{ t.name }}</strong>
            <span class="status-pill" :class="t.mode === 'write' ? 'warning' : 'neutral'">{{ t.mode }}</span>
            <span class="states">{{ (t.allowedConversationStates || []).slice(0, 2).join(' · ') }}</span>
            <span class="purpose">{{ t.purpose }}</span>
          </div>
          <p class="more" v-if="tools.length > 12">其余 {{ tools.length - 12 }} 个 Tool 同样经 AIFunction + ToolGateway 门禁。</p>
        </div>
      </section>

      <section class="panel pad">
        <h2>设计边界</h2>
        <ul class="guardrails">
          <li>成交政策快照优先于聊天记忆</li>
          <li>写操作需要确认令牌、订单版本、幂等键</li>
          <li>Tool 仅允许在契约声明的 conversation state 执行</li>
          <li>L3 禁止自动资金写，必须人工升级</li>
          <li>Verifier 校验金额 / 风险 / 话术事实边界</li>
          <li>A–L 由 Microsoft Agent Framework Workflows 回放</li>
        </ul>
      </section>
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

onMounted(async () => {
  try { tools.value = await fetchTools() } catch { tools.value = [] }
})

async function runWorkflows() {
  loading.value = true
  try {
    const res = await runAllWorkflows()
    summary.value = `A–L Workflow ${res.succeeded}/${res.total} succeeded · Agent Framework`
    message.success(summary.value)
  } catch {
    message.error('Workflow 运行失败')
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.workspace-heading {
  display: flex; align-items: flex-end; justify-content: space-between; gap: 1rem; margin-bottom: 1rem;
}
.workspace-heading h1 { margin: 0 0 0.2rem; font-size: 1.55rem; letter-spacing: -0.02em; }
.workspace-heading p { margin: 0; color: var(--color-ink-muted); font-size: var(--font-sm); }
.heading-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
.ghost-btn, .primary-btn {
  min-height: 36px; padding: 0 0.95rem; border-radius: var(--radius-md); cursor: pointer; font-weight: 600;
}
.ghost-btn { border: 1px solid var(--color-border); background: var(--color-surface); }
.primary-btn { border: 0; background: var(--color-accent-strong); color: #fff; }
.banner {
  margin-bottom: 0.85rem; padding: 0.7rem 0.9rem; border-radius: var(--radius-md);
  border: 1px solid oklch(0.83 0.055 155); background: var(--color-success-soft); color: var(--color-success);
  font-size: var(--font-sm); font-weight: 600;
}

.dflow-shell { overflow: hidden; margin-bottom: 1rem; }
.dflow-toolbar {
  min-height: 54px; display: flex; align-items: center; justify-content: space-between; gap: 1rem;
  padding: 0.7rem 1rem; border-bottom: 1px solid var(--color-border); background: var(--color-surface-muted);
}
.dflow-toolbar strong { font-size: 12px; }
.dflow-toolbar p { margin: 0.15rem 0 0; color: var(--color-ink-muted); font-size: 11px; }
.dflow-legend { display: flex; flex-wrap: wrap; gap: 0.75rem; color: var(--color-ink-muted); font-size: 11px; }
.dflow-key { display: inline-flex; align-items: center; gap: 0.35rem; }
.dflow-key::before { content: ''; width: 7px; height: 7px; border-radius: 2px; background: var(--color-accent); }
.dflow-key[data-tone="tool"]::before { background: var(--color-success); }
.dflow-key[data-tone="guard"]::before { background: var(--color-danger); }
.dflow-key[data-tone="memory"]::before { background: var(--color-warning); }

.dflow-viewport { overflow: auto; background: linear-gradient(180deg, oklch(0.985 0.005 230), oklch(0.97 0.008 230)); }
.dflow-canvas { position: relative; width: 980px; height: 720px; margin: 0 auto; }
.dflow-lines { position: absolute; inset: 0; width: 980px; height: 720px; }
.dflow-lines path { fill: none; stroke: oklch(0.72 0.02 230); stroke-width: 1.5; }
.dflow-lines path[data-tone="main"] { stroke: var(--color-accent); stroke-width: 2; }
.dflow-lines path[data-tone="guard"] { stroke: var(--color-danger); }
.dflow-lines path[data-tone="memory"] { stroke: var(--color-warning); }
.dflow-lines path[data-tone="loop"] { stroke-dasharray: 6 5; opacity: 0.55; }
.dflow-lines text { fill: var(--color-ink-muted); font-size: 11px; }

.dflow-node {
  position: absolute; z-index: 1; min-width: 150px; max-width: 190px;
  padding: 0.55rem 0.7rem; border-radius: var(--radius-md);
  background: var(--color-surface); border: 1px solid var(--color-border);
  box-shadow: var(--shadow-panel);
}
.dflow-node small { display: block; color: var(--color-ink-muted); font-size: 10px; margin-bottom: 0.15rem; }
.dflow-node strong { font-size: 12px; }
.dflow-node[data-tone="tool"] { border-color: oklch(0.83 0.055 155); background: var(--color-success-soft); }
.dflow-node[data-tone="guard"] { border-color: oklch(0.82 0.06 28); background: var(--color-danger-soft); }
.dflow-node[data-tone="memory"] { border-color: oklch(0.83 0.065 82); background: var(--color-warning-soft); }
.dflow-node[data-tone="agent"] { border-color: oklch(0.82 0.055 225); background: var(--color-accent-soft); }
.dflow-node.diamond {
  min-width: 108px; text-align: center; transform: rotate(0deg);
  border-radius: 999px; padding: 0.85rem 0.7rem;
}

.dflow-phase {
  position: absolute; left: 560px; right: 24px; height: 0;
  border-top: 1px dashed oklch(0.8 0.015 230);
  pointer-events: none;
}
.dflow-phase span {
  position: absolute; top: -0.7rem; right: 0;
  padding: 0.15rem 0.45rem; border-radius: var(--radius-pill);
  background: var(--color-surface); border: 1px solid var(--color-border);
  color: var(--color-ink-muted); font-size: 10px;
}

.dflow-summary {
  display: grid; grid-template-columns: repeat(4, 1fr); gap: 0;
  border-top: 1px solid var(--color-border);
}
.dflow-summary > div {
  padding: 0.85rem 1rem; border-right: 1px solid var(--color-border);
  display: grid; gap: 0.2rem;
}
.dflow-summary > div:last-child { border-right: 0; }
.dflow-summary span { font-size: 10px; color: var(--color-ink-muted); text-transform: uppercase; letter-spacing: 0.04em; }
.dflow-summary strong { font-size: var(--font-sm); }

.two { display: grid; grid-template-columns: 1.6fr 1fr; gap: 1rem; }
.pad { padding: 1rem; }
.section-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 0.75rem; }
.section-head h2, .pad > h2 { margin: 0 0 0.75rem; font-size: 1rem; }
.tool-table { display: grid; gap: 0.35rem; }
.tool-row {
  display: grid; grid-template-columns: 1.3fr 0.55fr 1fr 1.4fr; gap: 0.5rem; align-items: center;
  padding: 0.55rem 0.4rem; border-bottom: 1px solid var(--color-border); font-size: 12px;
}
.tool-row.head { color: var(--color-ink-muted); font-size: 11px; border-bottom-color: var(--color-border-strong); }
.states, .purpose { color: var(--color-ink-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.more { margin: 0.6rem 0 0; color: var(--color-ink-muted); font-size: 12px; }
.guardrails { margin: 0; padding-left: 1.1rem; line-height: 1.85; color: var(--color-ink); font-size: var(--font-sm); }

@media (max-width: 1100px) {
  .two { grid-template-columns: 1fr; }
  .dflow-summary { grid-template-columns: 1fr 1fr; }
  .tool-row { grid-template-columns: 1fr 0.5fr; }
  .tool-row span:nth-child(3), .tool-row span:nth-child(4) { display: none; }
}
</style>
