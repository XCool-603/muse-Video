import { createApp } from 'vue'
import { createPinia } from 'pinia'
import piniaPluginPersistedstate from 'pinia-plugin-persistedstate'
// 只引重置样式，不再 app.use(Antd) 全量注册组件：
// 组件由 vite.config.ts 里的 unplugin-vue-components 按需引入（模板里写了才打包），
// 首屏不用再等 1.4MB 的 antd 整包。
import 'ant-design-vue/dist/reset.css'

import App from './App.vue'
import router from './router'
import './styles/main.css'
// 赛博朋克主题：必须排在 main.css **之后**，靠后加载覆盖旧样式。
// 定义全部 --cy-* 设计令牌与 .cy-* 通用类（门户页 /portal 也引用它们）。
import './styles/cyberpunk.css'

const app = createApp(App)
const pinia = createPinia()
pinia.use(piniaPluginPersistedstate)

app.use(pinia)
app.use(router)

app.mount('#app')
