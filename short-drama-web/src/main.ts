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

const app = createApp(App)
const pinia = createPinia()
pinia.use(piniaPluginPersistedstate)

app.use(pinia)
app.use(router)

app.mount('#app')
