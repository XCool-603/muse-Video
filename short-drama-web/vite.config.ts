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
    Components({
      dts: false,
      resolvers: [
        AntDesignVueResolver({
          // ant-design-vue 4.x 的 es/ 目录里只有 JS，没有编译好的 css
          // （style/ 下是样式生成函数，样式运行时由 CSS-in-JS 注入）。
          // 所以这里不能要 css/less —— 只要组件的 JS，样式靠运行时 + reset.css。
          importStyle: false
        })
      ]
    })
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
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
        // 注意这里不再列 ant-design-vue：改成按需引入后它由 Components 插件
        // 按用到的组件自动引入，再钉成整包 chunk 就把它整个拉回首屏了。
        manualChunks: {
          vue: ['vue', 'vue-router', 'pinia', 'pinia-plugin-persistedstate'],
          hls: ['hls.js'],
          vendor: ['axios', 'dayjs']
        }
      }
    }
  }
})
