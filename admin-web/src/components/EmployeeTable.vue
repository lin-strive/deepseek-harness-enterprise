<script setup>
import { CalendarClock, Power, PowerOff, Trash2 } from '@lucide/vue'

defineProps({
  rows: { type: Array, required: true },
  selectable: { type: Boolean, default: false },
  selected: { type: Set, default: () => new Set() },
  manageable: { type: Boolean, default: false },
  pendingNumber: { type: String, default: '' },
})

defineEmits(['toggle', 'change-status', 'edit-expiry', 'delete'])

function money(value) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(Number(value || 0))
}

function isExpired(row) {
  return Boolean(row.accessExpiresAt) && new Date(row.accessExpiresAt).getTime() <= Date.now()
}

function accessLabel(row) {
  if (!row.accessExpiresAt) return '长期有效'
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric', month: '2-digit', day: '2-digit',
    timeZone: 'Asia/Shanghai',
  }).format(new Date(row.accessExpiresAt))
}
</script>

<template>
  <div class="table-wrap">
    <table>
      <thead>
        <tr>
          <th v-if="selectable" class="select-cell">选择</th>
          <th>员工</th>
          <th>部门</th>
          <th>访问期限</th>
          <th>月额度</th>
          <th>RPM</th>
          <th>TPM</th>
          <th>并发</th>
          <th>状态</th>
          <th v-if="manageable" class="actions-cell">操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="row.employeeId">
          <td v-if="selectable" class="select-cell">
            <input
              type="checkbox"
              :aria-label="`选择 ${row.displayName}`"
              :checked="selected.has(row.employeeNumber)"
              :disabled="row.status !== 'active' || isExpired(row)"
              @change="$emit('toggle', row.employeeNumber)"
            />
          </td>
          <td><div class="employee-name"><strong>{{ row.displayName }}</strong><span>{{ row.employeeNumber }}</span></div></td>
          <td>{{ row.department }}</td>
          <td><span class="access-expiry" :class="{ expired: isExpired(row) }">{{ accessLabel(row) }}</span></td>
          <td>¥ {{ money(row.monthlyBudgetCny) }}</td>
          <td>{{ row.requestsPerMinute }}</td>
          <td>{{ money(row.tokensPerMinute) }}</td>
          <td>{{ row.maxConcurrentRequests }}</td>
          <td><span class="state-pill" :class="row.status === 'active' && !isExpired(row) ? 'active' : 'disabled'">{{ isExpired(row) ? '已到期' : row.status === 'active' ? '已启用' : '已禁用' }}</span></td>
          <td v-if="manageable" class="actions-cell">
            <div class="row-actions">
              <button
                class="row-action"
                :disabled="pendingNumber === row.employeeNumber"
                :aria-label="`设置 ${row.displayName} 的访问期限`"
                @click="$emit('edit-expiry', row)"
              >
                <CalendarClock :size="15" aria-hidden="true" />期限
              </button>
              <button
                v-if="row.status === 'active' && !isExpired(row)"
                class="row-action"
                :disabled="pendingNumber === row.employeeNumber"
                :aria-label="`禁用 ${row.displayName}`"
                @click="$emit('change-status', row)"
              >
                <PowerOff :size="15" aria-hidden="true" />禁用
              </button>
              <button
                v-else
                class="row-action enable"
                :disabled="pendingNumber === row.employeeNumber"
                :aria-label="`启用 ${row.displayName}`"
                @click="$emit('change-status', row)"
              >
                <Power :size="15" aria-hidden="true" />启用
              </button>
              <button
                class="row-action danger"
                :disabled="pendingNumber === row.employeeNumber"
                :aria-label="`删除 ${row.displayName}`"
                @click="$emit('delete', row)"
              >
                <Trash2 :size="15" aria-hidden="true" />删除
              </button>
            </div>
          </td>
        </tr>
        <tr v-if="!rows.length">
          <td :colspan="8 + (selectable ? 1 : 0) + (manageable ? 1 : 0)"><div class="empty-state">暂无符合条件的员工</div></td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
