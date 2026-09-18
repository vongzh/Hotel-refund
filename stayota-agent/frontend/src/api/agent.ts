import axios from 'axios'
import type { AgentDecision, Scenario } from '@/types'

const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://127.0.0.1:5088',
  timeout: 15000,
})

export async function fetchScenarios() {
  const { data } = await http.get<Scenario[]>('/api/scenarios')
  return data
}

export async function runAgentMessage(payload: {
  message: string
  scenarioCode?: string
  hasEvidence?: boolean
  resetDemo?: boolean
}) {
  const { data } = await http.post<AgentDecision>('/api/agent/message', payload)
  return data
}

export async function health() {
  const { data } = await http.get('/health')
  return data
}
