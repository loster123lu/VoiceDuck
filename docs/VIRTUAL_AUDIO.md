# 虚拟音频设计

## 为什么只需要一组虚拟线缆

普通单线虚拟声卡本身只有播放端和录音端。若外部音乐软件与麦克风都写入这条线，再把录音端监听到耳机，本机就会听到延迟的自己。若抓取整台电脑的扬声器，对方声音还会被送回通话，形成回声。

VoiceDuck 避开这两个问题的方法是自己掌握音乐源：

```text
音乐文件解码 ─┬─► 真实耳机
              └─► 与麦克风混合 ─► CABLE Input ─► 通话输入
麦克风 ────────────────────────┘
通话输出 ─────────────────────────► 真实耳机
```

本地监听分支在混入麦克风之前产生，所以耳机里没有麦克风延迟回听。通话输出从不被捕获，所以对方不会听到自己的回声。

## 内置驱动边界

VoiceDuck 内置 VB-Audio 发布的标准 `VBCABLE_Driver_Pack45.zip`，保持官方包逐字节不变。它只增加一组端点：

- `CABLE Input`：VoiceDuck 写入混合音频的播放端；
- `CABLE Output`：微信、QQ 等选择的录音端。

驱动安装是可选、明确且可回滚的操作。自动闪避完全不依赖它。VoiceDuck 不会在启动时安装驱动，不会把虚拟端点设为 Windows 默认设备，也不会自动重启。

安装前会验证：

- 官方压缩包 SHA-256：`B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB`；
- 当前架构安装程序的固定 SHA-256；
- Windows Authenticode 信任结果；
- 签名者名称为 VB-Audio 的发布者 Vincent Burel / BUREL VINCENT。

Windows 10 x64 包内驱动目录由 Microsoft Windows Hardware Compatibility Publisher 签名。VoiceDuck 本身不提供、不修改也不重签内核驱动。

## 为什么安装时必须结束通话

音频驱动的安装和卸载会让 Windows 重新枚举端点。即使不改变默认设备，正在通话的软件也可能在端点变化时短暂丢失音频。因此 VoiceDuck 检测到已配置的微信、QQ、Discord、Teams、Zoom 等音频会话时，会显示预计总用时和最可能受影响的驱动写入阶段，并使用默认选“否”的二次确认。只有用户明确选择继续后才会调用官方安装程序。

驱动安装后如果端点没有立即同时出现，VoiceDuck 只提示稍后手动重启，不会代替用户重启。

## 为什么通话软件仍需首次选择一次

Windows 没有一套能让第三方程序可靠修改所有通话软件内部麦克风设置的公开接口。强行改系统默认麦克风会影响其他程序，也可能重现通话无声问题。因此 VoiceDuck 自动完成驱动校验、安装、端点探测和混音路由，只把 `CABLE Output` 的首次选择留在微信或 QQ 自身设置里。

## 许可

VB-CABLE 是 donationware。VoiceDuck 保留 VB-Audio 的产品名、来源与捐赠入口，并按其公开分发条款包含未修改的标准包。详情见 [第三方组件说明](../THIRD_PARTY_NOTICES.md) 与 [VB-Audio licensing](https://vb-audio.com/Services/licensing.htm)。
