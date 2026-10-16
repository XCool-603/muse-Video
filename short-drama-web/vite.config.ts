import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import Components from 'unplugin-vue-components/vite'
import { AntDesignVueResolver } from 'unplugin-vue-components/resolvers'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  plugins: [
    vue(),
    // antd 按需引入：模板里写了 <a-button> 才打包 Button，不再把整个库塞进首屏。
    // 原先 main.ts 里 app.use(Antd) 全量注册，加上下面 manualChunks 把整个
    // ant-design-vue 钉成一个 chunk —— 光 antd 就有 1.4MB，首屏必须等它下载完。
    //
    // importStyle 只能设 false（不能设 'css'）：ant-design-vue 4.x 的 es/ 目录里
    // 只有 JS，没有编译好的 css（style/ 下是样式生成函数，运行时由 CSS-in-JS 注入）。
    // 设 'css' 会让构建直接报 external 解析失败（实测）。
    // 代价是 @ant-design/cssinjs 运行时（含 desaturate/toHsl 等色彩计算）进 entry。
    Components({
      dts: false,
      resolvers: [
        AntDesignVueResolver({
          importStyle: false
        })
      ]
    })
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      // 工程层：ConfigProvider 换成「去掉 message/notification 静态链」的瘦身版。
      // 上游 ant-design-vue/es/config-provider/index.js 顶部无条件
      //   import message from '../message'
      //   import notification from '../notification'
      // 只为一段 direction==='rtl' 的副作用服务（本项目 direction 从未设置、恒为 ltr），
      // 实测把 66.4KB 的 message+notification+vc-notification 及其连带依赖
      // （@emotion/hash、stylis、@ctrl/tinycolor、@ant-design/colors、vue-types）
      // 整条拖进首屏 entry chunk。替身见 src/antd-slim/config-provider.ts。
      // 只影响「ConfigProvider 自己」，模板里的其它 <a-*> 组件仍走
      // unplugin-vue-components 的正常解析，不受这条 alias 影响。
      'ant-design-vue/es/config-provider': fileURLToPath(new URL('./src/antd-slim/config-provider.ts', import.meta.url))
    }
  },
  server: {
    port: 5173,
    host: '127.0.0.1',
    proxy: {
      // 开发期把 API 与 m3u8 代理转发到 .NET 后端
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true
      }
    }
  },
  build: {
    outDir: 'dist',
    chunkSizeWarningLimit: 1500,
    // 跳过每个 chunk 的 gzip 体积计算。产物越大越明显，
    // 容器里构建（CPU 弱）时这一段常常被误以为卡死
    reportCompressedSize: false,
    rollupOptions: {
      output: {
        // 把体积最大的依赖拆成独立 chunk：单块更小 => 压缩阶段峰值内存更低、
        // 并行度更好，浏览器侧也能长期缓存这几个几乎不变的库。
        //
        // antd 单独成块（vite metafile 实测依据）：unplugin-vue-components 会把各视图
        // 模板里用到的 antd 组件自动 import 进 entry —— 实测 198 个 ant-design-vue
        // 模块、合计 462 KB（minify 后 entry 323 KB），大头是 CSS-in-JS 的样式模板
        // （es/*/style/index.js）。antd 4.x 的 es/ 与 lib/ 都没有编译好的 css，
        // 所以 importStyle 只能设 false，这套运行时是固有成本，无法靠配置消除。
        // 单独成块的收益：与 entry 并行下载（浏览器 6 条并发）、发版只失效 entry、
        // 构建峰值内存更低。代价：首屏多一个约 60-100KB gzip 的请求。
        manualChunks: {
          vue: ['vue', 'vue-router', 'pinia', 'pinia-plugin-persistedstate'],
          hls: ['hls.js'],
          vendor: ['axios', 'dayjs'],
          antd: [
            'ant-design-vue',
            '@ant-design/icons-vue',
            '@ctrl/tinycolor',
            '@ant-design/colors',
            '@emotion/hash',
            'lodash-es',
            'dom-align',
            'resize-observer-polyfill',
            'stylis',
            'vue-types'
          ]
        }
      }
    }
  }
})
