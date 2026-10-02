# Clash 规则：让采集源的视频 CDN 走得通

> 抓取日期：2026-10-02　·　视频 CDN 主域 68 个（覆盖 94 台主机）　·　采集站接口域 22 个
>
> 重新生成：`node scripts/gen-clash-rules.mjs`

## 一、为什么需要这份规则

平台里「大部分短剧看不了」不是代码问题，是**出口网络**问题。实测证据：

1. 被拒的 CDN 返回**裸 openresty 403**（无自定义页、无原因），加 `Referer`、换 `User-Agent` 完全一样 ——
   说明是按**客户端 IP** 做的访问控制，不是防盗链。
2. 同一台机器访问红牛 / 金鹰 / 魔都 / 如意的 CDN 是 **200** —— 不是本机网络坏了。
3. 本机出口是 Clash 代理的**美国机房 IP**（`38.14.209.58`，San Jose，Uscloud Inc）。
   服务器与浏览器同一出口，所以 CDN 拒服务器 = 拒浏览器。

结论：**把视频 CDN 域名从代理里摘出去**（或改成走国内节点），这些源立刻能播，代码一行都不用改。

## 二、域名清单

### 视频 CDN（68 个主域）

已按主域归并 —— 例如 `v1.ppqrrs.com` ~ `v12.ppqrrs.com` 只需一条 `DOMAIN-SUFFIX,ppqrrs.com`。

- `baofeng9.com`
- `bdzybf11.com`
- `bdzybf22.com`
- `bfeng10.com`
- `bfeng11.com`
- `bfllvip.com`
- `bfvvs.com`
- `bvvvvvvv7f.com`
- `bvvvvvvvvv1f.com`
- `ddbbffcdn.com`
- `didibo2.com`
- `dytt-cine.com`
- `dytt-cinema.com`
- `dytt-hot.com`
- `dytt-kan.com`
- `dytt-luck.com`
- `dytt-network.com`
- `dytt-see.com`
- `dytt-tvs.com`
- `feifei-kan.com`
- `feifei-play.com`
- `fengbao12.com`
- `fengbao13.com`
- `fengbao8.com`
- `ffzy-bofang.com`
- `ffzy-online1.com`
- `ffzy-online3.com`
- `ffzy-online5.com`
- `ffzy-online6.com`
- `ffzy-play10.com`
- `ffzy-play5.com`
- `ffzy-plays.com`
- `heicdn.com`
- `jpxm3u8.com`
- `kuktxu.com`
- `lajiao2026.com`
- `lbsl2026.com`
- `lfthirtytwo.com`
- `lsbbf11.com`
- `lzcdn27.com`
- `lzcdn28.com`
- `lzcdn31.com`
- `lzv34.com`
- `modujx10.com`
- `modujx11.com`
- `modujx12.com`
- `modujx13.com`
- `modujx14.com`
- `modujx15.com`
- `modujx16.com`
- `modujx17.com`
- `phimgood.com`
- `ppqrrs.com`
- `rrcdnbf5.com`
- `rrcdnbf6.com`
- `ryiplay18.com`
- `ryiplay21.com`
- `ryplay10.com`
- `ryplay11.com`
- `ryplay12.com`
- `ryplay14.com`
- `ryplay16.com`
- `ryplay17.com`
- `senlin2026.com`
- `sysl2026.com`
- `v155p.com`
- `vostrely.com`
- `zuidazym3u8.com`

### 采集站接口域（22 个，可选）

接口本身目前走代理是通的。若希望整站直连（本机在国内时更稳），一并加上：

- `155api.com`
- `api.apibdzy.com`
- `api.ddapi.cc`
- `api.heiapi.cc`
- `api.wujinapi.net`
- `api.zuidapi.com`
- `apilj.com`
- `apiyutu.com`
- `bfzyapi.com`
- `caiji.dyttzyapi.com`
- `cj.ffzyapi.com`
- `cj.lziapi.com`
- `cj.rycjapi.com`
- `jyzyapi.com`
- `lbapi9.com`
- `naixxzy.com`
- `shayuapi.com`
- `slapibf.com`
- `subocaiji.com`
- `www.hongniuzy2.com`
- `www.jingpinx.com`
- `www.mdzyapi.com`

## 三、怎么用

### 方案 A：本机在国内，只是挂了国外代理（推荐）

把这些域名设为 **DIRECT**。粘到 Clash 配置的 `rules:` 里，**放在最后那条 `MATCH` 之前**：

```yaml
rules:
  # ↓↓↓ 短剧聚合：采集源视频 CDN 直连 ↓↓↓
  - DOMAIN-SUFFIX,baofeng9.com,DIRECT
  - DOMAIN-SUFFIX,bdzybf11.com,DIRECT
  - DOMAIN-SUFFIX,bdzybf22.com,DIRECT
  - DOMAIN-SUFFIX,bfeng10.com,DIRECT
  - DOMAIN-SUFFIX,bfeng11.com,DIRECT
  - DOMAIN-SUFFIX,bfllvip.com,DIRECT
  - DOMAIN-SUFFIX,bfvvs.com,DIRECT
  - DOMAIN-SUFFIX,bvvvvvvv7f.com,DIRECT
  - DOMAIN-SUFFIX,bvvvvvvvvv1f.com,DIRECT
  - DOMAIN-SUFFIX,ddbbffcdn.com,DIRECT
  - DOMAIN-SUFFIX,didibo2.com,DIRECT
  - DOMAIN-SUFFIX,dytt-cine.com,DIRECT
  - DOMAIN-SUFFIX,dytt-cinema.com,DIRECT
  - DOMAIN-SUFFIX,dytt-hot.com,DIRECT
  - DOMAIN-SUFFIX,dytt-kan.com,DIRECT
  - DOMAIN-SUFFIX,dytt-luck.com,DIRECT
  - DOMAIN-SUFFIX,dytt-network.com,DIRECT
  - DOMAIN-SUFFIX,dytt-see.com,DIRECT
  - DOMAIN-SUFFIX,dytt-tvs.com,DIRECT
  - DOMAIN-SUFFIX,feifei-kan.com,DIRECT
  - DOMAIN-SUFFIX,feifei-play.com,DIRECT
  - DOMAIN-SUFFIX,fengbao12.com,DIRECT
  - DOMAIN-SUFFIX,fengbao13.com,DIRECT
  - DOMAIN-SUFFIX,fengbao8.com,DIRECT
  - DOMAIN-SUFFIX,ffzy-bofang.com,DIRECT
  - DOMAIN-SUFFIX,ffzy-online1.com,DIRECT
  - DOMAIN-SUFFIX,ffzy-online3.com,DIRECT
  - DOMAIN-SUFFIX,ffzy-online5.com,DIRECT
  - DOMAIN-SUFFIX,ffzy-online6.com,DIRECT
  - DOMAIN-SUFFIX,ffzy-play10.com,DIRECT
  - DOMAIN-SUFFIX,ffzy-play5.com,DIRECT
  - DOMAIN-SUFFIX,ffzy-plays.com,DIRECT
  - DOMAIN-SUFFIX,heicdn.com,DIRECT
  - DOMAIN-SUFFIX,jpxm3u8.com,DIRECT
  - DOMAIN-SUFFIX,kuktxu.com,DIRECT
  - DOMAIN-SUFFIX,lajiao2026.com,DIRECT
  - DOMAIN-SUFFIX,lbsl2026.com,DIRECT
  - DOMAIN-SUFFIX,lfthirtytwo.com,DIRECT
  - DOMAIN-SUFFIX,lsbbf11.com,DIRECT
  - DOMAIN-SUFFIX,lzcdn27.com,DIRECT
  - DOMAIN-SUFFIX,lzcdn28.com,DIRECT
  - DOMAIN-SUFFIX,lzcdn31.com,DIRECT
  - DOMAIN-SUFFIX,lzv34.com,DIRECT
  - DOMAIN-SUFFIX,modujx10.com,DIRECT
  - DOMAIN-SUFFIX,modujx11.com,DIRECT
  - DOMAIN-SUFFIX,modujx12.com,DIRECT
  - DOMAIN-SUFFIX,modujx13.com,DIRECT
  - DOMAIN-SUFFIX,modujx14.com,DIRECT
  - DOMAIN-SUFFIX,modujx15.com,DIRECT
  - DOMAIN-SUFFIX,modujx16.com,DIRECT
  - DOMAIN-SUFFIX,modujx17.com,DIRECT
  - DOMAIN-SUFFIX,phimgood.com,DIRECT
  - DOMAIN-SUFFIX,ppqrrs.com,DIRECT
  - DOMAIN-SUFFIX,rrcdnbf5.com,DIRECT
  - DOMAIN-SUFFIX,rrcdnbf6.com,DIRECT
  - DOMAIN-SUFFIX,ryiplay18.com,DIRECT
  - DOMAIN-SUFFIX,ryiplay21.com,DIRECT
  - DOMAIN-SUFFIX,ryplay10.com,DIRECT
  - DOMAIN-SUFFIX,ryplay11.com,DIRECT
  - DOMAIN-SUFFIX,ryplay12.com,DIRECT
  - DOMAIN-SUFFIX,ryplay14.com,DIRECT
  - DOMAIN-SUFFIX,ryplay16.com,DIRECT
  - DOMAIN-SUFFIX,ryplay17.com,DIRECT
  - DOMAIN-SUFFIX,senlin2026.com,DIRECT
  - DOMAIN-SUFFIX,sysl2026.com,DIRECT
  - DOMAIN-SUFFIX,v155p.com,DIRECT
  - DOMAIN-SUFFIX,vostrely.com,DIRECT
  - DOMAIN-SUFFIX,zuidazym3u8.com,DIRECT
  # ↑↑↑ 短剧聚合 ↑↑↑
  # ……你原有的规则……
  - MATCH,你的默认代理
```

### 方案 B：本机在国外（或部署在海外机房）

直连没用（出口还是海外 IP），要让这些域名**走国内节点**。把上面的 `DIRECT` 换成你的国内代理组名：

```yaml
rules:
  - DOMAIN-SUFFIX,baofeng9.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,bdzybf11.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,bdzybf22.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,bfeng10.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,bfeng11.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,bfllvip.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,bfvvs.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,bvvvvvvv7f.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,bvvvvvvvvv1f.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ddbbffcdn.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,didibo2.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,dytt-cine.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,dytt-cinema.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,dytt-hot.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,dytt-kan.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,dytt-luck.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,dytt-network.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,dytt-see.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,dytt-tvs.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,feifei-kan.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,feifei-play.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,fengbao12.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,fengbao13.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,fengbao8.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ffzy-bofang.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ffzy-online1.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ffzy-online3.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ffzy-online5.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ffzy-online6.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ffzy-play10.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ffzy-play5.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ffzy-plays.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,heicdn.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,jpxm3u8.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,kuktxu.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,lajiao2026.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,lbsl2026.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,lfthirtytwo.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,lsbbf11.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,lzcdn27.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,lzcdn28.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,lzcdn31.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,lzv34.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,modujx10.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,modujx11.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,modujx12.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,modujx13.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,modujx14.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,modujx15.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,modujx16.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,modujx17.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,phimgood.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ppqrrs.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,rrcdnbf5.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,rrcdnbf6.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ryiplay18.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ryiplay21.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ryplay10.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ryplay11.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ryplay12.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ryplay14.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ryplay16.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,ryplay17.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,senlin2026.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,sysl2026.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,v155p.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,vostrely.com,🇨🇳 国内节点
  - DOMAIN-SUFFIX,zuidazym3u8.com,🇨🇳 国内节点
```

### 方案 C：用 rule-provider 自动更新

本目录下的 `clash-shortdrama-cdn.yaml` 就是规则集，可直接引用：

```yaml
rule-providers:
  shortdrama-cdn:
    type: http
    behavior: classical
    # 仓库若为私有，改用本地路径或自己托管
    url: "https://raw.githubusercontent.com/XCool-603/muse-Video/main/docs/clash-shortdrama-cdn.yaml"
    path: ./ruleset/shortdrama-cdn.yaml
    interval: 86400

rules:
  - RULE-SET,shortdrama-cdn,DIRECT
```

> Clash Verge 也可以在「订阅 → 规则」里直接粘贴方案 A 的规则段。

## 四、验证是否生效

改完规则、重载 Clash 后，任选一种：

**1）直接测一集**（最直观）

点开一部**暴风资源 / 无尽资源 / 最大资源**的剧。改规则前是 502 / 播放失败，改完应当能播。

**2）命令行测 CDN 是否可达**

```powershell
# 无尽资源的 CDN 根路径：被拒时是 403，通了是 404/200
curl.exe -s -o NUL -w "%{http_code}\n" https://v5.ppqrrs.com/
```

**3）看平台自己的判定**

平台列表的「可播性」是按**真实播放结果**记录的（`PlayabilityTracker`）：
规则生效后随便播一部，被拒的源会自动恢复成可播，平台栏的置灰与排序会自己纠正 ——
**不需要重启后端，也不需要改代码**。

## 五、注意事项

- **规则顺序**：`DOMAIN-SUFFIX` 必须排在兜底的 `MATCH` / `GEOIP` 之前，否则不会命中。
- **fake-ip 不影响**：Clash 的 fake-ip 模式只改 DNS 解析，规则匹配用的是域名，照常生效。
- **域名会变**：采集站 CDN 更换频繁（实测暴风资源的播放地址里同时出现 10 个主机，一部分已退役）。
  本清单从**真实播放地址**抓取，建议定期重跑 `node scripts/gen-clash-rules.mjs`。
- **只抓启用的源**：脚本读 `apple-cms-sources.json` 里 `enabled: true` 的源；
  关掉的源不会进清单（想包含就先把它们启用）。
