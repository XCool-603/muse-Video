<template>
  <div class="drama-manage">
    <div class="toolbar">
      <a-input-search
        v-model:value="filters.keyword"
        placeholder="搜索短剧标题"
        style="max-width: 260px"
        allow-clear
        @search="reload"
      />

      <a-select v-model:value="filters.category" style="width: 140px" @change="reload">
        <a-select-option value="">全部分类</a-select-option>
        <a-select-option v-for="cat in categories" :key="cat" :value="cat">{{ cat }}</a-select-option>
      </a-select>

      <a-select v-model:value="filters.platform" style="width: 150px" @change="reload">
        <a-select-option value="">全部平台</a-select-option>
        <a-select-option v-for="p in platforms" :key="p.platformCode" :value="p.platformCode">
          {{ p.platformName }}
        </a-select-option>
      </a-select>

      <div class="spacer" />

      <a-button type="primary" @click="openCreate">
        <template #icon><PlusOutlined /></template>
        新增短剧
      </a-button>
      <a-button @click="reload">
        <template #icon><ReloadOutlined /></template>
        刷新
      </a-button>
    </div>

    <a-table
      :columns="columns"
      :data-source="list"
      :loading="loading"
      :pagination="pagination"
      row-key="id"
      size="middle"
      :scroll="{ x: 960 }"
      @change="onTableChange"
    >
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'title'">
          <div class="title-cell">
            <img :src="record.coverUrl" class="cover-thumb" :alt="record.title" />
            <div class="title-text">
              <span class="t-title">{{ record.title }}</span>
              <span class="sd-muted t-desc">{{ record.description?.slice(0, 40) || '—' }}</span>
            </div>
          </div>
        </template>

        <template v-else-if="column.key === 'category'">
          <a-tag color="magenta">{{ record.category || '未分类' }}</a-tag>
        </template>

        <template v-else-if="column.key === 'platform'">
          <a-tag color="blue">{{ record.platformName || record.platformCode }}</a-tag>
        </template>

        <template v-else-if="column.key === 'status'">
          <a-badge
            :status="record.status === 'completed' ? 'success' : 'processing'"
            :text="record.status === 'completed' ? '已完结' : '连载中'"
          />
        </template>

        <template v-else-if="column.key === 'rating'">
          <span class="rating-text">{{ record.rating?.toFixed(1) ?? '0.0' }}</span>
        </template>

        <template v-else-if="column.key === 'action'">
          <a-space size="small">
            <a-button type="link" size="small" @click="openEdit(record)">编辑</a-button>
            <a-button type="link" size="small" @click="goPlay(record)">预览</a-button>
            <a-popconfirm
              title="确认删除该短剧？此操作不可恢复"
              ok-text="删除"
              cancel-text="取消"
              @confirm="handleDelete(record)"
            >
              <a-button type="link" size="small" danger>删除</a-button>
            </a-popconfirm>
          </a-space>
        </template>
      </template>
    </a-table>

    <!-- 新增 / 编辑弹窗 -->
    <a-modal
      v-model:open="modalOpen"
      :title="editingId ? '编辑短剧' : '新增短剧'"
      :confirm-loading="saving"
      width="620px"
      @ok="handleSave"
    >
      <a-form :model="form" layout="vertical">
        <a-row :gutter="16">
          <a-col :span="12">
            <a-form-item label="标题" required>
              <a-input v-model:value="form.title" placeholder="请输入短剧标题" />
            </a-form-item>
          </a-col>
          <a-col :span="12">
            <a-form-item label="分类" required>
              <a-select v-model:value="form.category" placeholder="选择分类">
                <a-select-option v-for="cat in categoryOptions" :key="cat" :value="cat">
                  {{ cat }}
                </a-select-option>
              </a-select>
            </a-form-item>
          </a-col>
        </a-row>

        <a-form-item label="简介">
          <a-textarea v-model:value="form.description" :rows="3" placeholder="请输入短剧简介" />
        </a-form-item>

        <a-form-item label="封面地址">
          <a-input v-model:value="form.coverUrl" placeholder="https://..." />
        </a-form-item>

        <a-row :gutter="16">
          <a-col :span="8">
            <a-form-item label="平台编码">
              <a-input v-model:value="form.platformCode" placeholder="hongguo" />
            </a-form-item>
          </a-col>
          <a-col :span="8">
            <a-form-item label="平台剧集 ID">
              <a-input v-model:value="form.platformDramaId" placeholder="hg_001" />
            </a-form-item>
          </a-col>
          <a-col :span="8">
            <a-form-item label="评分">
              <a-input-number v-model:value="form.rating" :min="0" :max="10" :step="0.1" style="width: 100%" />
            </a-form-item>
          </a-col>
        </a-row>

        <a-form-item label="状态">
          <a-radio-group v-model:value="form.status">
            <a-radio-button value="ongoing">连载中</a-radio-button>
            <a-radio-button value="completed">已完结</a-radio-button>
          </a-radio-group>
        </a-form-item>
      </a-form>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons-vue'
import { message } from 'ant-design-vue'
import { adminApi, type DramaSaveRequest } from '@/api/admin'
import { dramaApi } from '@/api/drama'
import type { Drama, PlatformSource } from '@/api/types'

const router = useRouter()

const columns = [
  { title: '短剧', key: 'title', width: 300 },
  { title: '分类', key: 'category', width: 100 },
  { title: '来源平台', key: 'platform', width: 120 },
  { title: '集数', dataIndex: 'totalEpisodes', key: 'totalEpisodes', width: 80 },
  { title: '状态', key: 'status', width: 100 },
  { title: '评分', key: 'rating', width: 80 },
  { title: '播放量', dataIndex: 'playCount', key: 'playCount', width: 100 },
  { title: '操作', key: 'action', width: 180, fixed: 'right' as const }
]

const list = ref<Drama[]>([])
const categories = ref<string[]>([])
const platforms = ref<PlatformSource[]>([])
const loading = ref(false)
const saving = ref(false)
const modalOpen = ref(false)
const editingId = ref<number | null>(null)

const filters = reactive({ keyword: '', category: '', platform: '' })
const pagination = reactive({
  current: 1,
  pageSize: 10,
  total: 0,
  showSizeChanger: true,
  showTotal: (total: number) => `共 ${total} 条`
})

const categoryOptions = ['霸总', '穿越', '重生', '种田', '剧情', '悬疑', '古装', '都市']

const form = reactive<DramaSaveRequest>({
  title: '',
  description: '',
  coverUrl: '',
  category: '剧情',
  status: 'ongoing',
  platformCode: 'manual',
  platformDramaId: '',
  rating: 8
})

async function load() {
  loading.value = true
  try {
    const result = await adminApi.dramas({
      keyword: filters.keyword || undefined,
      category: filters.category || undefined,
      platform: filters.platform || undefined,
      page: pagination.current,
      pageSize: pagination.pageSize
    })
    list.value = result.items
    pagination.total = result.total
  } finally {
    loading.value = false
  }
}

function reload() {
  pagination.current = 1
  load()
}

function onTableChange(pag: { current?: number; pageSize?: number }) {
  pagination.current = pag.current ?? 1
  pagination.pageSize = pag.pageSize ?? 10
  load()
}

function resetForm() {
  form.title = ''
  form.description = ''
  form.coverUrl = ''
  form.category = '剧情'
  form.status = 'ongoing'
  form.platformCode = 'manual'
  form.platformDramaId = ''
  form.rating = 8
}

function openCreate() {
  editingId.value = null
  resetForm()
  modalOpen.value = true
}

function openEdit(record: Drama) {
  editingId.value = record.id
  form.title = record.title
  form.description = record.description
  form.coverUrl = record.coverUrl
  form.category = record.category
  form.status = record.status
  form.platformCode = record.platformCode
  form.platformDramaId = record.platformDramaId
  form.rating = record.rating
  modalOpen.value = true
}

async function handleSave() {
  if (!form.title.trim()) {
    message.warning('请填写标题')
    return
  }

  saving.value = true
  try {
    if (editingId.value) {
      await adminApi.updateDrama(editingId.value, { ...form })
      message.success('更新成功')
    } else {
      await adminApi.createDrama({ ...form })
      message.success('创建成功')
    }
    modalOpen.value = false
    load()
  } catch (error) {
    message.error((error as Error).message || '保存失败')
  } finally {
    saving.value = false
  }
}

async function handleDelete(record: Drama) {
  try {
    await adminApi.deleteDrama(record.id)
    message.success('已删除')
    load()
  } catch (error) {
    message.error((error as Error).message || '删除失败')
  }
}

function goPlay(record: Drama) {
  router.push(`/play/${record.id}`)
}

onMounted(async () => {
  load()
  try {
    categories.value = (await dramaApi.categories()).filter((c) => c !== '全部')
  } catch {
    categories.value = []
  }
  try {
    platforms.value = await adminApi.platforms()
  } catch {
    platforms.value = []
  }
})
</script>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}

.spacer {
  flex: 1;
}

.title-cell {
  display: flex;
  align-items: center;
  gap: 10px;
}

.cover-thumb {
  width: 38px;
  height: 50px;
  object-fit: cover;
  border-radius: 6px;
  background: #242430;
  flex-shrink: 0;
}

.title-text {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.t-title {
  font-size: 13.5px;
  color: var(--sd-text);
}

.t-desc {
  font-size: 11.5px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 220px;
}

.rating-text {
  color: #ffd666;
  font-weight: 600;
}
</style>
