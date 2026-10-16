import { createApp } from 'vue'
import { createPinia } from 'pinia'
import piniaPluginPersistedstate from 'pinia-plugin-persistedstate'
// 不再 app.use(Antd) 全量注册组件：组件由 vite.config.ts 里的 unplugin-vue-components
// 按需引入（模板里写了才打包），首屏不用再等 1.4MB 的 antd 整包。
//
// ⚠️ 也**不要** `import 'ant-design-vue/dist/reset.css'`：那一行会把 ant-design-vue 的
// 整个模块图（含全部组件 JS）拖进首屏依赖 —— 实测首屏 entry 因此静态依赖了全部 15 个
// 视图（含 DramaManageView 275KB），入口从 27KB 涨到 332KB。
// 样式重置由 styles/main.css 与 styles/minimal.css 提供（含盒模型与表单元素归零）。

import App from './App.vue'
import router from './router'
import './styles/main.css'
// 极简主题：必须排在 main.css **之后**，靠后加载覆盖布局骨架里的默认值。
// 定义全部 --mn-* 设计令牌与 .mn-* 通用类。
import './styles/minimal.css'

const app = createApp(App)
const pinia = createPinia()
pinia.use(piniaPluginPersistedstate)

app.use(pinia)
app.use(router)

app.mount('#app')
