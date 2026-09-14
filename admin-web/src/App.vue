<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import EmployeeTable from './components/EmployeeTable.vue'
import { copyText } from './clipboard.js'
import {
  Activity,
  BadgeCheck,
  Building2,
  CalendarClock,
  Check,
  ChevronRight,
  Clipboard,
  Cpu,
  Download,
  Eye,
  KeyRound,
  LayoutDashboard,
  LogOut,
  Menu,
  Plus,
  Power,
  PowerOff,
  RefreshCw,
  Search,
  ShieldCheck,
  Trash2,
  Upload,
  Users,
  Zap,
  X,
} from '@lucide/vue'

const pages = [
  { id: 'overview', label: '运行概览', icon: LayoutDashboard },
  { id: 'employees', label: '员工与部门', icon: Users },
  { id: 'models', label: '模型目录', icon: Cpu },
  { id: 'activation', label: '激活码管理', icon: KeyRound },
]

const page = ref('overview')
const credentials = ref(sessionStorage.getItem('harness-admin-auth') || '')
const username = ref('admin')
const password = ref('')
const authenticating = ref(false)
const loading = ref(false)
const error = ref('')
const notice = ref('')
const menuOpen = ref(false)
const overview = ref(null)
const employees = ref([])
const models = ref([])
const modelPending = ref('')
const search = ref('')
const selected = ref(new Set())
const activationExpiryOption = ref('24')
const customActivationExpiry = ref('')
const replaceExisting = ref(true)
const generatedCodes = ref([])
const copiedCode = ref('')
let copiedCodeResetTimer
const importInput = ref(null)
const addDialog = ref(null)
const addErrorPanel = ref(null)
const confirmDialog = ref(null)
const expiryDialog = ref(null)
const addSubmitting = ref(false)
const addError = ref('')
const actionPending = ref('')
const actionTarget = ref(null)
const actionKind = ref('')
const deleteConfirmation = ref('')
const expiryTarget = ref(null)
const expiryMode = ref('longTerm')
const expiryValue = ref('')
const expirySubmitting = ref(false)
const expiryError = ref('')
const newEmployee = ref(defaultEmployee())
const logoUrl = `${import.meta.env.BASE_URL}logo.png`

const activePage = computed(() => pages.find(item => item.id === page.value))
const activationExpiryMin = computed(() => toBeijingInput(new Date(Date.now() + 60 * 1000)))
const activationExpiryMax = computed(() => toBeijingInput(new Date(Date.now() + 90 * 24 * 60 * 60 * 1000)))
const activationExpiresAt = computed(() => {
  if (activationExpiryOption.value === 'custom') return parseBeijingInput(customActivationExpiry.value)
  return new Date(Date.now() + Number(activationExpiryOption.value) * 60 * 60 * 1000)
})
const activationExpiryPreview = computed(() => {
  const value = activationExpiresAt.value
  return value && !Number.isNaN(value.getTime()) ? `${formatDate(value)}（北京时间）` : '请选择有效的失效时间'
})
const filteredEmployees = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return employees.value
  return employees.value.filter(employee =>
    [employee.employeeNumber, employee.displayName, employee.department]
      .some(value => String(value || '').toLowerCase().includes(term)))
})

function authHeader() {
  return { Authorization: `Basic ${credentials.value}` }
}

async function api(path, options = {}) {
  const headers = new Headers(options.headers || {})
  Object.entries(authHeader()).forEach(([key, value]) => headers.set(key, value))
  if (options.body && !(options.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  const response = await fetch(path, { ...options, headers })
  if (response.status === 401) {
    logout()
    throw new Error('账号或密码不正确，请重新登录。')
  }
  const body = response.headers.get('content-type')?.includes('application/json')
    ? await response.json()
    : null
  if (!response.ok) throw new Error(body?.message || `服务请求失败（${response.status}）`)
  return body
}

async function loadData() {
  if (!credentials.value) return
  loading.value = true
  error.value = ''
  try {
    const [summary, rows, modelRows] = await Promise.all([
      api('/api/v1/admin/overview'),
      api('/api/v1/admin/employees'),
      api('/api/v1/admin/models'),
    ])
    overview.value = summary
    employees.value = rows
    models.value = modelRows
    selected.value = new Set([...selected.value].filter(id => rows.some(row => row.employeeNumber === id && canIssueActivation(row))))
  } catch (reason) {
    error.value = reason.message
  } finally {
    loading.value = false
  }
}

async function login() {
  authenticating.value = true
  error.value = ''
  const candidate = btoa(unescape(encodeURIComponent(`${username.value}:${password.value}`)))
  credentials.value = candidate
  try {
    await loadData()
    if (error.value) throw new Error(error.value)
    sessionStorage.setItem('harness-admin-auth', candidate)
    password.value = ''
  } catch (reason) {
    credentials.value = ''
    error.value = reason.message
  } finally {
    authenticating.value = false
  }
}

function logout() {
  sessionStorage.removeItem('harness-admin-auth')
  credentials.value = ''
  password.value = ''
  overview.value = null
  employees.value = []
  models.value = []
  selected.value = new Set()
}

async function updateModel(model, enabled, defaultModel = model.defaultModel) {
  modelPending.value = model.id
  error.value = ''
  notice.value = ''
  try {
    const result = await api(`/api/v1/admin/models/${encodeURIComponent(model.id)}`, {
      method: 'PUT',
      body: JSON.stringify({ enabled, defaultModel }),
    })
    notice.value = `已更新 ${result.model.name}，并同步 ${result.synchronizedVirtualKeyCount} 个有效模型密钥。员工重启 Harness 后会加载最新目录。`
    await loadData()
  } catch (reason) {
    error.value = reason.message
  } finally {
    modelPending.value = ''
  }
}

function navigate(target) {
  page.value = target
  menuOpen.value = false
  error.value = ''
  notice.value = ''
}

function defaultEmployee() {
  return {
    employeeNumber: '',
    displayName: '',
    department: '',
    status: 'active',
    accessMode: 'longTerm',
    accessExpiresAt: '',
    monthlyBudgetCny: 200,
    requestsPerMinute: 30,
    tokensPerMinute: 100000,
    maxConcurrentRequests: 3,
  }
}

function openAddEmployee() {
  newEmployee.value = defaultEmployee()
  addError.value = ''
  addDialog.value?.showModal()
  nextTick(() => document.querySelector('#employee-number')?.focus())
}

function closeAddEmployee() {
  if (!addSubmitting.value) addDialog.value?.close()
}

async function createEmployee() {
  addSubmitting.value = true
  addError.value = ''
  try {
    const accessExpiresAt = newEmployee.value.accessMode === 'dated'
      ? requireFutureBeijingDate(newEmployee.value.accessExpiresAt, '员工访问结束时间')
      : null
    const { accessMode: _accessMode, accessExpiresAt: _accessExpiresAt, ...employeeFields } = newEmployee.value
    const employee = await api('/api/v1/admin/employees', {
      method: 'POST',
      body: JSON.stringify({
        ...employeeFields,
        accessExpiresAt: accessExpiresAt?.toISOString() || null,
        employeeNumber: newEmployee.value.employeeNumber.trim(),
        displayName: newEmployee.value.displayName.trim(),
        department: newEmployee.value.department.trim(),
        monthlyBudgetCny: Number(newEmployee.value.monthlyBudgetCny),
        requestsPerMinute: Number(newEmployee.value.requestsPerMinute),
        tokensPerMinute: Number(newEmployee.value.tokensPerMinute),
        maxConcurrentRequests: Number(newEmployee.value.maxConcurrentRequests),
      }),
    })
    addDialog.value?.close()
    notice.value = `已添加员工 ${employee.displayName}（${employee.employeeNumber}）。`
    await loadData()
  } catch (reason) {
    addError.value = reason.message
    nextTick(() => addErrorPanel.value?.focus())
  } finally {
    addSubmitting.value = false
  }
}

function openExpiryEditor(employee) {
  expiryTarget.value = employee
  expiryMode.value = employee.accessExpiresAt ? 'dated' : 'longTerm'
  expiryValue.value = employee.accessExpiresAt ? toBeijingInput(new Date(employee.accessExpiresAt)) : ''
  expiryError.value = ''
  expiryDialog.value?.showModal()
}

function closeExpiryEditor() {
  if (!expirySubmitting.value) expiryDialog.value?.close()
}

async function saveEmployeeExpiry() {
  if (!expiryTarget.value) return
  expirySubmitting.value = true
  expiryError.value = ''
  try {
    const accessExpiresAt = expiryMode.value === 'dated'
      ? requireFutureBeijingDate(expiryValue.value, '员工访问结束时间')
      : null
    const employee = await api(`/api/v1/admin/employees/${encodeURIComponent(expiryTarget.value.employeeNumber)}/access-expiry`, {
      method: 'PUT',
      body: JSON.stringify({ accessExpiresAt: accessExpiresAt?.toISOString() || null }),
    })
    expiryDialog.value?.close()
    notice.value = employee.accessExpiresAt
      ? `已将 ${employee.displayName} 的访问期限设置为 ${formatDate(employee.accessExpiresAt)}。`
      : `已将 ${employee.displayName} 设置为长期有效。`
    await loadData()
  } catch (reason) {
    expiryError.value = reason.message
  } finally {
    expirySubmitting.value = false
  }
}

function requestStatusChange(employee) {
  if (employee.status === 'disabled') {
    changeEmployeeStatus(employee, 'active')
    return
  }
  openEmployeeAction('disable', employee)
}

function openEmployeeAction(kind, employee) {
  actionKind.value = kind
  actionTarget.value = employee
  deleteConfirmation.value = ''
  confirmDialog.value?.showModal()
}

function closeEmployeeAction() {
  if (!actionPending.value) confirmDialog.value?.close()
}

async function confirmEmployeeAction() {
  const employee = actionTarget.value
  if (!employee) return
  if (actionKind.value === 'delete') {
    await deleteEmployee(employee)
  } else {
    await changeEmployeeStatus(employee, 'disabled')
  }
}

async function changeEmployeeStatus(employee, status) {
  actionPending.value = employee.employeeNumber
  error.value = ''
  notice.value = ''
  try {
    const result = await api(`/api/v1/admin/employees/${encodeURIComponent(employee.employeeNumber)}/status`, {
      method: 'PUT',
      body: JSON.stringify({ status }),
    })
    if (status === 'disabled') {
      notice.value = `已禁用 ${employee.displayName}，撤销 ${result.revokedVirtualKeyCount} 个模型密钥和 ${result.revokedActivationCodeCount} 个未使用激活码。`
    } else {
      notice.value = `已启用 ${employee.displayName}。如需在新设备使用，请重新生成激活码。`
    }
    confirmDialog.value?.close()
    await loadData()
  } catch (reason) {
    error.value = reason.message
    confirmDialog.value?.close()
  } finally {
    actionPending.value = ''
  }
}

async function deleteEmployee(employee) {
  actionPending.value = employee.employeeNumber
  error.value = ''
  notice.value = ''
  try {
    await api(`/api/v1/admin/employees/${encodeURIComponent(employee.employeeNumber)}`, { method: 'DELETE' })
    confirmDialog.value?.close()
    notice.value = `已删除员工 ${employee.displayName}（${employee.employeeNumber}）。`
    await loadData()
  } catch (reason) {
    error.value = reason.message
    confirmDialog.value?.close()
  } finally {
    actionPending.value = ''
  }
}

function formatMoney(value) {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY' }).format(Number(value || 0))
}

function formatDate(value) {
  return new Intl.DateTimeFormat('zh-CN', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Asia/Shanghai',
  }).format(new Date(value))
}

function toBeijingInput(value) {
  const parts = new Intl.DateTimeFormat('sv-SE', {
    timeZone: 'Asia/Shanghai',
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
  }).formatToParts(value).reduce((result, part) => ({ ...result, [part.type]: part.value }), {})
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`
}

function parseBeijingInput(value) {
  if (!value) return null
  const normalized = value.length === 16 ? `${value}:00` : value
  return new Date(`${normalized}+08:00`)
}

function requireFutureBeijingDate(value, label, maximumDays = null) {
  const parsed = parseBeijingInput(value)
  if (!parsed || Number.isNaN(parsed.getTime())) throw new Error(`请选择有效的${label}。`)
  if (parsed.getTime() <= Date.now()) throw new Error(`${label}必须晚于当前时间。`)
  if (maximumDays && parsed.getTime() > Date.now() + maximumDays * 24 * 60 * 60 * 1000) {
    throw new Error(`${label}最长不能超过 ${maximumDays} 天。`)
  }
  return parsed
}

function prepareCustomActivationExpiry() {
  if (activationExpiryOption.value === 'custom' && !customActivationExpiry.value) {
    customActivationExpiry.value = toBeijingInput(new Date(Date.now() + 7 * 24 * 60 * 60 * 1000))
  }
}

function prepareEmployeeExpiry(mode, target) {
  if (mode !== 'dated' || target) return target
  const suggested = new Date()
  suggested.setFullYear(suggested.getFullYear() + 1)
  return toBeijingInput(suggested)
}

function toggleEmployee(employeeNumber) {
  const employee = employees.value.find(item => item.employeeNumber === employeeNumber)
  if (!employee || !canIssueActivation(employee)) return
  const next = new Set(selected.value)
  next.has(employeeNumber) ? next.delete(employeeNumber) : next.add(employeeNumber)
  selected.value = next
}

function toggleVisible() {
  const visible = filteredEmployees.value.filter(canIssueActivation).map(item => item.employeeNumber)
  const allSelected = visible.length > 0 && visible.every(id => selected.value.has(id))
  const next = new Set(selected.value)
  visible.forEach(id => allSelected ? next.delete(id) : next.add(id))
  selected.value = next
}

function canIssueActivation(employee) {
  return employee.status === 'active'
    && (!employee.accessExpiresAt || new Date(employee.accessExpiresAt).getTime() > Date.now())
}

async function importCsv(event) {
  const file = event.target.files?.[0]
  if (!file) return
  loading.value = true
  error.value = ''
  notice.value = ''
  try {
    const form = new FormData()
    form.append('file', file)
    const result = await api('/api/v1/admin/employees/import', { method: 'POST', body: form })
    notice.value = `已导入 ${result.importedCount} 名员工。`
    await loadData()
  } catch (reason) {
    error.value = reason.message
  } finally {
    loading.value = false
    if (importInput.value) importInput.value.value = ''
  }
}

function downloadTemplate() {
  const sample = '\ufeffemployee_number,display_name,department,status,access_expires_at,monthly_budget_cny,rpm_limit,tpm_limit,max_concurrent_requests\nSW001,张三,研发部,active,,200,30,100000,3\nSW002,李四,项目部,active,2027-12-31T18:00:00+08:00,200,30,100000,3\n'
  downloadBlob(sample, '员工导入模板.csv')
}

async function generateCodes() {
  if (!selected.value.size) {
    error.value = '请先选择至少一名员工。'
    return
  }
  loading.value = true
  error.value = ''
  notice.value = ''
  try {
    const expiresAt = activationExpiryOption.value === 'custom'
      ? requireFutureBeijingDate(customActivationExpiry.value, '激活码失效时间', 90)
      : activationExpiresAt.value
    const result = await api('/api/v1/admin/activation-codes/batch', {
      method: 'POST',
      body: JSON.stringify({
        employeeNumbers: [...selected.value],
        expiresAt: expiresAt.toISOString(),
        replaceExisting: replaceExisting.value,
      }),
    })
    generatedCodes.value = result.codes
    notice.value = `已生成 ${result.codes.length} 个激活码。明文仅在本页显示一次，请立即交付或下载。`
    await loadData()
  } catch (reason) {
    error.value = reason.message
  } finally {
    loading.value = false
  }
}

async function copyCode(code) {
  error.value = ''
  try {
    await copyText(code)
    copiedCode.value = code
    notice.value = '激活码已复制。'
    window.clearTimeout(copiedCodeResetTimer)
    copiedCodeResetTimer = window.setTimeout(() => {
      if (copiedCode.value === code) copiedCode.value = ''
    }, 2000)
  } catch {
    copiedCode.value = ''
    notice.value = ''
    error.value = '复制失败。请选中激活码后手动复制，或使用“下载 CSV”。'
  }
}

function downloadCodes() {
  const escape = value => `"${String(value).replaceAll('"', '""')}"`
  const rows = generatedCodes.value.map(item => [item.employeeNumber, item.displayName, item.activationCode, item.expiresAt].map(escape).join(','))
  downloadBlob(`\ufeff员工编号,姓名,激活码,失效时间\n${rows.join('\n')}\n`, `激活码-${new Date().toISOString().slice(0, 10)}.csv`)
}

function downloadBlob(content, filename) {
  const url = URL.createObjectURL(new Blob([content], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  link.click()
  URL.revokeObjectURL(url)
}

onMounted(loadData)
onBeforeUnmount(() => window.clearTimeout(copiedCodeResetTimer))
</script>

<template>
  <div v-if="!credentials" class="login-page">
    <main class="login-panel" aria-labelledby="login-title">
      <img class="login-logo" :src="logoUrl" alt="超智能战斗轮椅有限公司" />
      <p class="eyebrow">COMPANY HARNESS</p>
      <h1 id="login-title">管理控制台</h1>
      <p class="login-copy">集中管理员工权限、调用配额和一次性激活码。</p>
      <form class="login-form" @submit.prevent="login">
        <label for="username">管理员账号</label>
        <input id="username" v-model="username" autocomplete="username" required />
        <label for="password">密码</label>
        <input id="password" v-model="password" type="password" autocomplete="current-password" required autofocus />
        <p v-if="error" class="inline-alert error" role="alert">{{ error }}</p>
        <button class="button primary full" :disabled="authenticating">
          <RefreshCw v-if="authenticating" class="spin" :size="18" aria-hidden="true" />
          <ShieldCheck v-else :size="18" aria-hidden="true" />
          {{ authenticating ? '正在验证…' : '安全登录' }}
        </button>
      </form>
      <p class="login-footnote">公司内部管理入口 · 凭据仅保留在当前浏览器会话</p>
    </main>
  </div>

  <div v-else class="app-shell">
    <button class="mobile-menu" aria-label="打开导航" @click="menuOpen = true"><Menu :size="22" /></button>
    <div v-if="menuOpen" class="sidebar-scrim" @click="menuOpen = false" />
    <aside class="sidebar" :class="{ open: menuOpen }">
      <button class="sidebar-close" aria-label="关闭导航" @click="menuOpen = false"><X :size="22" /></button>
      <img class="brand-logo" :src="logoUrl" alt="超智能战斗轮椅有限公司" />
      <div class="product-label">HARNESS 管理台</div>
      <nav aria-label="主要导航">
        <button
          v-for="item in pages"
          :key="item.id"
          class="nav-item"
          :class="{ active: page === item.id }"
          :aria-current="page === item.id ? 'page' : undefined"
          @click="navigate(item.id)"
        >
          <component :is="item.icon" :size="19" aria-hidden="true" />
          <span>{{ item.label }}</span>
          <ChevronRight :size="16" class="nav-arrow" aria-hidden="true" />
        </button>
      </nav>
      <div class="sidebar-status">
        <span class="status-dot" />
        <div><strong>控制服务在线</strong><small>统一网关已接入</small></div>
      </div>
      <button class="logout" @click="logout"><LogOut :size="18" />退出登录</button>
    </aside>

    <main class="workspace">
      <header class="page-header">
        <div>
          <p class="eyebrow">CONTROL PLANE</p>
          <h1>{{ activePage.label }}</h1>
        </div>
        <button class="button secondary" :disabled="loading" @click="loadData">
          <RefreshCw :size="17" :class="{ spin: loading }" />刷新数据
        </button>
      </header>

      <p v-if="error" class="inline-alert error" role="alert">{{ error }}</p>
      <p v-if="notice" class="inline-alert success" role="status"><Check :size="17" />{{ notice }}</p>

      <template v-if="page === 'overview'">
        <section class="overview-lead" aria-label="员工概览">
          <div class="hero-metric">
            <div class="hero-icon"><Users :size="27" /></div>
            <p>已纳管员工</p>
            <strong>{{ overview?.employeeCount ?? '—' }}</strong>
            <span>其中 {{ overview?.activeEmployeeCount ?? '—' }} 人处于启用状态</span>
          </div>
          <div class="support-metrics">
            <div><Building2 :size="20" /><span>部门</span><strong>{{ overview?.departmentCount ?? '—' }}</strong></div>
            <div><KeyRound :size="20" /><span>可用激活码</span><strong>{{ overview?.availableActivationCodeCount ?? '—' }}</strong></div>
            <div><Activity :size="20" /><span>活跃模型密钥</span><strong>{{ overview?.activeVirtualKeyCount ?? '—' }}</strong></div>
          </div>
        </section>
        <section class="section-block">
          <div class="section-heading">
            <div><h2>最近员工配置</h2><p>配额由公司网关统一执行，客户端无法绕过。</p></div>
            <button class="text-button" @click="navigate('employees')">查看全部<ChevronRight :size="16" /></button>
          </div>
          <EmployeeTable :rows="employees.slice(0, 8)" :selectable="false" />
        </section>
      </template>

      <template v-else-if="page === 'employees'">
        <section class="toolbar-panel">
          <div>
            <h2>员工目录</h2>
            <p>CSV 会按员工编号新增或更新资料与调用限额。</p>
          </div>
          <div class="toolbar-actions">
            <button class="button primary" @click="openAddEmployee"><Plus :size="17" aria-hidden="true" />添加员工</button>
            <button class="button secondary" @click="downloadTemplate"><Download :size="17" />下载模板</button>
            <label class="button secondary" for="csv-file"><Upload :size="17" />导入 CSV</label>
            <input id="csv-file" ref="importInput" class="visually-hidden" type="file" accept=".csv,text/csv" @change="importCsv" />
          </div>
        </section>
        <section class="section-block">
          <div class="table-tools">
            <label class="search-box"><Search :size="18" /><span class="visually-hidden">搜索员工</span><input v-model="search" placeholder="搜索编号、姓名或部门" /></label>
            <span>{{ filteredEmployees.length }} 名员工</span>
          </div>
          <EmployeeTable
            :rows="filteredEmployees"
            :selectable="false"
            manageable
            :pending-number="actionPending"
            @change-status="requestStatusChange"
            @edit-expiry="openExpiryEditor"
            @delete="employee => openEmployeeAction('delete', employee)"
          />
        </section>
      </template>

      <template v-else-if="page === 'models'">
        <section class="toolbar-panel model-toolbar">
          <div>
            <h2>DeepSeek 官方模型</h2>
            <p>客户端只展示已启用模型；目录和虚拟 Key 权限由公司网关统一下发。</p>
          </div>
          <span class="model-count">{{ models.filter(model => model.enabled).length }} / {{ models.length }} 已启用</span>
        </section>
        <section class="section-block model-list" aria-label="模型目录">
          <article v-for="model in models" :key="model.id" class="model-row">
            <div class="model-symbol" :class="{ vision: model.inputModalities.includes('image') }">
              <Eye v-if="model.inputModalities.includes('image')" :size="22" aria-hidden="true" />
              <Zap v-else :size="22" aria-hidden="true" />
            </div>
            <div class="model-detail">
              <div class="model-title">
                <h2>{{ model.name }}</h2>
                <span v-if="model.defaultModel" class="state-pill default">默认</span>
                <span v-if="model.experimental" class="state-pill experimental">实验</span>
                <span class="state-pill" :class="model.enabled ? 'active' : 'disabled'">{{ model.enabled ? '已启用' : '已禁用' }}</span>
              </div>
              <code>{{ model.id }}</code>
              <p>{{ model.description }}</p>
              <small>输入能力：{{ model.inputModalities.includes('image') ? '文本、图片' : '文本' }}</small>
            </div>
            <div class="model-actions">
              <button
                class="button secondary"
                :disabled="modelPending === model.id || model.defaultModel"
                @click="updateModel(model, !model.enabled, false)"
              >
                <RefreshCw v-if="modelPending === model.id" class="spin" :size="16" />
                <PowerOff v-else-if="model.enabled" :size="16" />
                <Power v-else :size="16" />
                {{ model.enabled ? '禁用' : '启用' }}
              </button>
              <button
                class="button primary"
                :disabled="modelPending === model.id || model.defaultModel"
                @click="updateModel(model, true, true)"
              >设为默认</button>
            </div>
          </article>
          <p v-if="!models.length" class="empty-state">暂无模型配置</p>
        </section>
        <p class="model-policy-note">为兼容旧客户端，网关暂时保留旧别名，但不会在新版 Harness 中展示。实验模型可能变更或下线，请勿用于关键生产流程。</p>
      </template>

      <template v-else>
        <section class="activation-layout">
          <div class="section-block selection-panel">
            <div class="section-heading compact">
              <div><h2>选择员工</h2><p>已选择 {{ selected.size }} 人</p></div>
              <label class="search-box compact-search"><Search :size="17" /><span class="visually-hidden">搜索员工</span><input v-model="search" placeholder="搜索员工" /></label>
            </div>
            <div class="selection-actions">
              <button class="text-button" @click="toggleVisible">全选/取消当前结果</button>
            </div>
            <EmployeeTable :rows="filteredEmployees" :selectable="true" :selected="selected" @toggle="toggleEmployee" />
          </div>
          <aside class="issue-panel">
            <div class="issue-title"><BadgeCheck :size="23" /><div><h2>生成设置</h2><p>明文只返回一次</p></div></div>
            <div class="form-field compact-field">
              <label for="activation-expiry">激活码有效期</label>
              <p id="activation-expiry-help">员工需在此时间前完成一次性激活，最长 90 天。</p>
              <select id="activation-expiry" v-model="activationExpiryOption" aria-describedby="activation-expiry-help activation-expiry-preview" @change="prepareCustomActivationExpiry">
                <option value="1">1 小时</option>
                <option value="8">8 小时</option>
                <option value="24">1 天（推荐）</option>
                <option value="72">3 天</option>
                <option value="168">7 天</option>
                <option value="720">30 天</option>
                <option value="custom">自定义到期时间</option>
              </select>
            </div>
            <div v-if="activationExpiryOption === 'custom'" class="form-field compact-field conditional-field">
              <label for="custom-activation-expiry">失效时间（北京时间）</label>
              <input id="custom-activation-expiry" v-model="customActivationExpiry" type="datetime-local" :min="activationExpiryMin" :max="activationExpiryMax" step="60" required />
            </div>
            <p id="activation-expiry-preview" class="expiry-preview"><CalendarClock :size="16" aria-hidden="true" />将在 {{ activationExpiryPreview }} 失效</p>
            <label class="check-row"><input v-model="replaceExisting" type="checkbox" />撤销员工现有未使用激活码</label>
            <div class="selected-summary"><strong>{{ selected.size }}</strong><span>名员工待生成</span></div>
            <button class="button primary full" :disabled="loading || !selected.size" @click="generateCodes"><KeyRound :size="18" />批量生成激活码</button>
          </aside>
        </section>
        <section v-if="generatedCodes.length" class="section-block code-results">
          <div class="section-heading">
            <div><h2>本次生成结果</h2><p>关闭或刷新页面后，无法再次查看这些明文激活码。</p></div>
            <button class="button secondary" @click="downloadCodes"><Download :size="17" />下载 CSV</button>
          </div>
          <div class="code-list">
            <div v-for="item in generatedCodes" :key="item.employeeNumber" class="code-row">
              <div><strong>{{ item.displayName }}</strong><span>{{ item.employeeNumber }} · {{ formatDate(item.expiresAt) }} 失效</span></div>
              <code>{{ item.activationCode }}</code>
              <button
                class="icon-button"
                :class="{ copied: copiedCode === item.activationCode }"
                :aria-label="copiedCode === item.activationCode ? `${item.displayName} 的激活码已复制` : `复制 ${item.displayName} 的激活码`"
                :title="copiedCode === item.activationCode ? '已复制' : '复制激活码'"
                @click="copyCode(item.activationCode)"
              >
                <Check v-if="copiedCode === item.activationCode" :size="18" />
                <Clipboard v-else :size="18" />
              </button>
            </div>
          </div>
        </section>
      </template>
    </main>

    <dialog
      ref="addDialog"
      class="management-dialog employee-dialog"
      aria-labelledby="add-employee-title"
      @click.self="closeAddEmployee"
      @cancel="addSubmitting && $event.preventDefault()"
    >
      <form class="dialog-form" @submit.prevent="createEmployee">
        <header class="dialog-header">
          <div>
            <p class="dialog-kicker">员工目录</p>
            <h2 id="add-employee-title">添加员工</h2>
            <p>建立员工身份和公司网关调用额度。</p>
          </div>
          <button type="button" class="dialog-close" aria-label="关闭添加员工对话框" @click="closeAddEmployee"><X :size="20" aria-hidden="true" /></button>
        </header>

        <div class="dialog-body">
          <p v-if="addError" ref="addErrorPanel" class="dialog-error" role="alert" tabindex="-1">{{ addError }}</p>
          <fieldset class="form-section">
            <legend>员工身份</legend>
            <div class="form-field">
              <label for="employee-number">员工编号</label>
              <p id="employee-number-help">允许字母、数字、点、短横线和下划线，保存后自动转为大写。</p>
              <input id="employee-number" v-model="newEmployee.employeeNumber" name="employeeNumber" pattern="[A-Za-z0-9._-]+" maxlength="64" aria-describedby="employee-number-help" required />
            </div>
            <div class="form-field">
              <label for="display-name">姓名</label>
              <input id="display-name" v-model="newEmployee.displayName" name="displayName" maxlength="128" autocomplete="name" required />
            </div>
            <div class="form-field">
              <label for="department">部门</label>
              <input id="department" v-model="newEmployee.department" name="department" maxlength="128" required />
            </div>
            <fieldset class="status-field">
              <legend>初始状态</legend>
              <label><input v-model="newEmployee.status" type="radio" name="status" value="active" /><Power :size="16" aria-hidden="true" />启用</label>
              <label><input v-model="newEmployee.status" type="radio" name="status" value="disabled" /><PowerOff :size="16" aria-hidden="true" />禁用</label>
            </fieldset>
            <fieldset class="status-field access-mode-field">
              <legend>员工访问期限</legend>
              <label><input v-model="newEmployee.accessMode" type="radio" name="accessMode" value="longTerm" />长期有效</label>
              <label><input v-model="newEmployee.accessMode" type="radio" name="accessMode" value="dated" @change="newEmployee.accessExpiresAt = prepareEmployeeExpiry('dated', newEmployee.accessExpiresAt)" />指定结束时间</label>
            </fieldset>
            <div v-if="newEmployee.accessMode === 'dated'" class="form-field conditional-field">
              <label for="employee-access-expiry">访问结束时间（北京时间）</label>
              <p id="employee-access-expiry-help">到期后系统会禁用员工，并撤销模型密钥和未使用激活码。</p>
              <input id="employee-access-expiry" v-model="newEmployee.accessExpiresAt" name="accessExpiresAt" type="datetime-local" :min="activationExpiryMin" step="60" aria-describedby="employee-access-expiry-help" required />
            </div>
          </fieldset>

          <fieldset class="form-section quota-section">
            <legend>调用额度</legend>
            <div class="form-field">
              <label for="monthly-budget">月度金额（人民币）</label>
              <p id="monthly-budget-help">范围：0–72,000 元，每月重置。</p>
              <input id="monthly-budget" v-model="newEmployee.monthlyBudgetCny" name="monthlyBudgetCny" type="number" min="0" max="72000" step="0.01" inputmode="decimal" aria-describedby="monthly-budget-help" required />
            </div>
            <div class="form-field">
              <label for="rpm-limit">每分钟请求数（RPM）</label>
              <p id="rpm-limit-help">范围：1–600。</p>
              <input id="rpm-limit" v-model="newEmployee.requestsPerMinute" name="requestsPerMinute" type="number" min="1" max="600" step="1" inputmode="numeric" aria-describedby="rpm-limit-help" required />
            </div>
            <div class="form-field">
              <label for="tpm-limit">每分钟 Token 数（TPM）</label>
              <p id="tpm-limit-help">范围：1–2,000,000。</p>
              <input id="tpm-limit" v-model="newEmployee.tokensPerMinute" name="tokensPerMinute" type="number" min="1" max="2000000" step="1" inputmode="numeric" aria-describedby="tpm-limit-help" required />
            </div>
            <div class="form-field">
              <label for="concurrency-limit">并发请求数</label>
              <p id="concurrency-limit-help">范围：1–20。</p>
              <input id="concurrency-limit" v-model="newEmployee.maxConcurrentRequests" name="maxConcurrentRequests" type="number" min="1" max="20" step="1" inputmode="numeric" aria-describedby="concurrency-limit-help" required />
            </div>
          </fieldset>
        </div>

        <footer class="dialog-footer">
          <button type="button" class="button secondary" @click="closeAddEmployee">取消</button>
          <button class="button primary" :disabled="addSubmitting">
            <RefreshCw v-if="addSubmitting" class="spin" :size="17" aria-hidden="true" />
            <Plus v-else :size="17" aria-hidden="true" />
            {{ addSubmitting ? '正在添加…' : '添加员工' }}
          </button>
        </footer>
      </form>
    </dialog>

    <dialog
      ref="expiryDialog"
      class="management-dialog expiry-dialog"
      aria-labelledby="expiry-dialog-title"
      @click.self="closeExpiryEditor"
      @cancel="expirySubmitting && $event.preventDefault()"
    >
      <form v-if="expiryTarget" class="dialog-form" @submit.prevent="saveEmployeeExpiry">
        <header class="dialog-header action-header">
          <div class="action-icon"><CalendarClock :size="22" aria-hidden="true" /></div>
          <div>
            <h2 id="expiry-dialog-title">设置 {{ expiryTarget.displayName }} 的访问期限</h2>
            <p>{{ expiryTarget.employeeNumber }} · {{ expiryTarget.department }}</p>
          </div>
          <button type="button" class="dialog-close" aria-label="关闭访问期限对话框" @click="closeExpiryEditor"><X :size="20" aria-hidden="true" /></button>
        </header>
        <div class="dialog-body">
          <p v-if="expiryError" class="dialog-error" role="alert">{{ expiryError }}</p>
          <fieldset class="status-field access-mode-field">
            <legend>员工访问期限</legend>
            <label><input v-model="expiryMode" type="radio" name="editAccessMode" value="longTerm" />长期有效</label>
            <label><input v-model="expiryMode" type="radio" name="editAccessMode" value="dated" @change="expiryValue = prepareEmployeeExpiry('dated', expiryValue)" />指定结束时间</label>
          </fieldset>
          <div v-if="expiryMode === 'dated'" class="form-field conditional-field">
            <label for="edit-access-expiry">访问结束时间（北京时间）</label>
            <p id="edit-access-expiry-help">到期后最多约一分钟内禁用账号并撤销有效模型密钥。</p>
            <input id="edit-access-expiry" v-model="expiryValue" type="datetime-local" :min="activationExpiryMin" step="60" aria-describedby="edit-access-expiry-help" required />
          </div>
          <p v-else class="expiry-policy-note">长期有效表示持续到管理员手动禁用，不会生成长期有效的激活码。</p>
        </div>
        <footer class="dialog-footer">
          <button type="button" class="button secondary" @click="closeExpiryEditor">取消</button>
          <button class="button primary" :disabled="expirySubmitting">
            <RefreshCw v-if="expirySubmitting" class="spin" :size="17" aria-hidden="true" />
            <CalendarClock v-else :size="17" aria-hidden="true" />
            {{ expirySubmitting ? '正在保存…' : '保存访问期限' }}
          </button>
        </footer>
      </form>
    </dialog>

    <dialog
      ref="confirmDialog"
      class="management-dialog confirm-dialog"
      :aria-labelledby="actionKind === 'delete' ? 'delete-title' : 'disable-title'"
      @click.self="closeEmployeeAction"
      @cancel="actionPending && $event.preventDefault()"
    >
      <div v-if="actionTarget" class="dialog-form">
        <header class="dialog-header action-header">
          <div class="action-icon danger"><Trash2 v-if="actionKind === 'delete'" :size="22" aria-hidden="true" /><PowerOff v-else :size="22" aria-hidden="true" /></div>
          <div>
            <h2 v-if="actionKind === 'delete'" id="delete-title">删除 {{ actionTarget.displayName }}</h2>
            <h2 v-else id="disable-title">禁用 {{ actionTarget.displayName }}</h2>
            <p>{{ actionTarget.employeeNumber }} · {{ actionTarget.department }}</p>
          </div>
          <button type="button" class="dialog-close" aria-label="关闭确认对话框" @click="closeEmployeeAction"><X :size="20" aria-hidden="true" /></button>
        </header>
        <div class="dialog-body confirm-body">
          <template v-if="actionKind === 'delete'">
            <p>删除会移除员工资料和未使用激活码，且无法撤销。已签发过模型密钥的员工不能删除，只能禁用。</p>
            <div class="form-field">
              <label for="delete-confirmation">输入员工编号 <strong>{{ actionTarget.employeeNumber }}</strong> 确认</label>
              <input id="delete-confirmation" v-model="deleteConfirmation" autocomplete="off" />
            </div>
          </template>
          <p v-else>禁用后将立即撤销该员工的全部有效模型密钥，并作废尚未使用的激活码。重新启用不会恢复旧密钥。</p>
        </div>
        <footer class="dialog-footer">
          <button type="button" class="button secondary" @click="closeEmployeeAction">取消</button>
          <button
            type="button"
            class="button danger"
            :disabled="Boolean(actionPending) || (actionKind === 'delete' && deleteConfirmation.trim().toUpperCase() !== actionTarget.employeeNumber)"
            @click="confirmEmployeeAction"
          >
            <RefreshCw v-if="actionPending" class="spin" :size="17" aria-hidden="true" />
            <Trash2 v-else-if="actionKind === 'delete'" :size="17" aria-hidden="true" />
            <PowerOff v-else :size="17" aria-hidden="true" />
            {{ actionPending ? '正在处理…' : actionKind === 'delete' ? '删除员工' : '禁用员工' }}
          </button>
        </footer>
      </div>
    </dialog>
  </div>
</template>
