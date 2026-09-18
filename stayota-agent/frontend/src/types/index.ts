export interface Scenario {
  code: string
  name: string
  group: string
  goal: string
  entryMessage: string
  riskLevel: string
}

export interface DecisionStep {
  step: string
  status: string
  detail: string
  score?: number
}

export interface AgentDecision {
  traceId: string
  runId: string
  caseId: string
  scenarioCode: string
  intent: string
  intentConfidence: number
  riskLevel: string
  riskScore: number
  action: string
  conclusion: string
  planTitle: string
  planCopy: string
  refundAmount?: number
  feeAmount?: number
  reply: string
  steps: DecisionStep[]
  slots: Record<string, string>
  order: {
    orderId: string
    hotelName: string
    checkIn: string
    checkOut: string
    amount: number
    status: string
    arrived: boolean
    cancelPolicyCode: string
    version: number
  }
}
