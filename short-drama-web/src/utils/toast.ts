/*
  全局轻提示（toast）—— antd message 的按需异步包装。

  为什么要有这一层：`ant-design-vue/es/message` 的传递闭包实测
  **351.3KB 未压缩源码 / 434 个源文件**。它把 vc-notification、
  notification/style、config-provider 整条链拖进依赖图，
  连带 antd 全量 locale 表（69 个语言包、源侧 192.8KB）。
  而首屏（首页/分类/搜索）静态依赖里一条 message 都不弹 ——
  这份体积纯粹是「import 了就跟着走」。

  改成动态 import 后：
    · 首屏静态闭包里没有 message，也没有它带进来的 192.8KB locale；
    · 第一次真的弹 toast 时才下载，之后是内存单例，行为与静态引入一致；
    · message 的 API（error/warning/success/info）逐个透传，调用点一行不用改语义。

  用法（与原来的 `message.error('…')` 等价）：
      import { toast } from '@/utils/toast'
      void toast('error', '该剧暂时无法入库')
*/
type MessageApi = typeof import('ant-design-vue/es/message').default

let messagePromise: Promise<MessageApi> | null = null

function loadMessage(): Promise<MessageApi> {
  // 模块级单例：antd message 本身就是全局单例，重复 import 拿到同一个 api
  messagePromise ??= import('ant-design-vue/es/message').then((m) => m.default)
  return messagePromise
}

export type ToastType = 'error' | 'warning' | 'success' | 'info'

/** 弹一条 toast。异步，调用点不需要 await（toast 本来就fire-and-forget）。 */
export function toast(type: ToastType, content: string): void {
  void loadMessage().then((message) => message[type](content))
}

/** 需要拿到 message 实例本身时用（例如带 onClose 回调的进阶用法） */
export function getMessage(): Promise<MessageApi> {
  return loadMessage()
}

export default toast
