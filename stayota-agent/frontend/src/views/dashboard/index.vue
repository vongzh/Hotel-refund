<template>
  <div class="dashboard">
    <div class="hero page-card">
      <div>
        <span>北极星指标 · Demo</span>
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
      <h3>Badcase 闭环（来自 Hotel-refund 运营口径）</h3>
      <a-table :columns="columns" :data-source="badcases" :pagination="false" row-key="type" />
    </div>
  </div>
</template>

<script setup lang="ts">
const metrics = [
  { group: '结果', name: '一次解决率', value: '71.2%', note: '未达目标，需压缩二次进线' },
  { group: '风险', name: '错误承诺率', value: '1.8%', note: '政策冲突拦截有效' },
  { group: '效率', name: '自动处理覆盖率', value: '54.6%', note: 'L1 场景可继续提升' },
  { group: '能力', name: '规则判断准确率', value: '96.1%', note: '金额与权限不交给模型' },
]

const columns = [
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
]
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
.cards {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
.cards span { color: var(--muted); font-size: 12px; }
.cards h3 { margin: 6px 0; font-size: 15px; }
.cards strong { font-size: 24px; }
.cards p { color: var(--muted); margin: 8px 0 0; font-size: 13px; }
@media (max-width: 1100px) {
  .cards { grid-template-columns: 1fr 1fr; }
}
</style>
