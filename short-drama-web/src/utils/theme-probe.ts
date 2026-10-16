/*
  验证用：暂存 locale 处理，先测「去掉 zh_CN locale 后 entry 掉多少」。
  这是实验分支，不提交；验证完立刻改回真实实现。
*/
import { ref } from 'vue'
import antdTheme from 'ant-design-vue/es/theme'
import ConfigProvider from 'ant-design-vue/es/config-provider'

const themeConfig = { algorithm: antdTheme.defaultAlgorithm, token: {}, components: {} }
const locale = ref(undefined)
void ConfigProvider

export { themeConfig, locale }
