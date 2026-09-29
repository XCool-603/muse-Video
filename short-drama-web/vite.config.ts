import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  plugins: [vue()],
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
        // 并行度更好，浏览器侧也能长期缓存这几个几乎不变的库
        manualChunks: {
          vue: ['vue', 'vue-router', 'pinia', 'pinia-plugin-persistedstate'],
          antd: ['ant-design-vue', '@ant-design/icons-vue'],
          hls: ['hls.js'],
          vendor: ['axios', 'dayjs']
        }
      }
    }
  }
})
