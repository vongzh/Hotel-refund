<template>
  <div class="dashboard">
    <div class="hero page-card">
      <div>
        <span>北极星 · Demo</span>
        <h2>正确退款任务闭环率</h2>
        <p>结果、金额、权限与流程均正确，且取消/退款/替代方案得到确认。</p>
      </div>
      <strong>62.4%</strong>
    </div>

    <div class="cards">
      <div class="page-card" v-for="item in metrics" :key="item.name">
        <span>{{ item.group }}</span>
        <h3>{{ item.name }}</h3>
        <strong>{{ item.value }}</strong>
        <p>{{ item.note }}</p>
      </div>
    </div>

    <div class="page-card">
      <div class="head">
        <h3>处理漏斗</h3>
      </div>
      <a-table :columns="funnelCols" :data-source="funnel" :pagination="false" row-key="stage" size="small" />
    </div>

    <div class="page-card">
      <div class="head">
        <h3>Badcase 闭环 / 回归验证</h3>
        <a-space>
          <a-button :loading="wfLoading" @click="runWorkflowBatch">跑 A–L Workflow</a-button>
          <a-button type="primary" :loading="evalLoading" @click="runOfflineEval">跑 36 条离线 Eval</a-button>
        </a-space>
      </div>
      <a-alert v-if="evalSummary" type="success" show-icon :message="evalSummary" style="margin-bottom: 12px" />
      <a-alert v-if="wfSummary" type="info" show-icon :message="wfSummary" style="margin-bottom: 12px" />
      <a-table :columns="badCols" :data-source="badcases" :pagination="false" row-key="type" size="small" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { message } from 'ant-design-vue'
import { runAllWorkflows, runEval } from '@/api/agent'

const evalLoading = ref(false)
const wfLoading = ref(false)
const evalSummary = ref('')
const wfSummary = ref('')

const metrics = [
  { group: '结果', name: '一次解决率', value: '71.2%', note: '未达目标，需压缩二次进线' },
  { group: '风险', name: '错误承诺率', value: '1.8%', note: '政策冲突拦截有效' },
  { group: '效率', name: '自动处理覆盖率', value: '54.6%', note: 'L1 场景可继续提升' },
  { group: '能力', name: '规则判断准确率', value: '96.1%', note: '金额与权限不交给模型' },
]

const funnelCols = [
  { title: '阶段', dataIndex: 'stage' },
  { title: '进入', dataIndex: 'in' },
  { title: '流失', dataIndex: 'drop' },
  { title: '说明', dataIndex: 'note' },
]
const funnel = [
  { stage: '进线识别', in: 10000, drop: 320, note: '低置信度澄清' },
  { stage: '订单确认', in: 9680, drop: 410, note: '多订单歧义' },
  { stage: '规则决策', in: 9270, drop: 880, note: '信息不足' },
  { stage: '外部协同', in: 8390, drop: 1310, note: '供应商/支付等待' },
  { stage: '结果确认', in: 7080, drop: 840, note: '到账未确认' },
]

const badCols = [
  { title: 'Badcase 类型', dataIndex: 'type' },
  { title: '影响案件', dataIndex: 'count' },
  { title: '风险', dataIndex: 'risk' },
  { title: '当前阶段', dataIndex: 'stage' },
  { title: 'Owner', dataIndex: 'owner' },
]
const badcases = [
  { type: '越权承诺退款', count: 12, risk: '高', stage: '修复', owner: '规则引擎' },
  { type: '到账预期管理失败', count: 31, risk: '中', stage: '回归验证', owner: '支付协同' },
  { type: '人工摘要缺失', count: 7, risk: '中', stage: '发现', owner: 'HITL' },
  { type: '团体写操作未阻断', count: 3, risk: '高', stage: '关闭', owner: '权限门' },
]

async function runOfflineEval() {
  evalLoading.value = true
  try {
    const res = await runEval()
    evalSummary.value = `Eval ${res.passed}/${res.total} passed，失败 ${res.failed}`
    message.success(evalSummary.value)
  } catch {
    message.error('Eval 运行失败')
  } finally {
    evalLoading.value = false
  }
}

async function runWorkflowBatch() {
  wfLoading.value = true
  try {
    const res = await runAllWorkflows()
    wfSummary.value = `Workflow ${res.succeeded}/${res.total} succeeded`
    message.success(wfSummary.value)
  } catch {
    message.error('Workflow 运行失败')
  } finally {
    wfLoading.value = false
  }
}
</script>

<style scoped>
.dashboard { display: grid; gap: 12px; }
.hero {
  display: flex; justify-content: space-between; align-items: center;
  background: linear-gradient(135deg, #eff6ff, #ffffff);
}
.hero h2 { margin: 6px 0; }
.hero p { margin: 0; color: var(--muted); }
.hero strong { font-size: 42px; color: var(--primary); }
.cards { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; }
.cards span { color: var(--muted); font-size: 12px; }
.cards h3 { margin: 6px 0; font-size: 15px; }
.cards strong { font-size: 24px; }
.cards p { color: var(--muted); margin: 8px 0 0; font-size: 13px; }
.head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
.head h3 { margin: 0; }
@media (max-width: 1100px) { .cards { grid-template-columns: 1fr 1fr; } }
</style>
