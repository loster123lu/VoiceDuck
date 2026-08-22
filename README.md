# VoiceDuck

> 微信、QQ 或其他通话软件里有人说话时，自动降低音乐音量；安静后平滑恢复。

[![Windows build](https://github.com/1917360964/VoiceDuck/actions/workflows/build.yml/badge.svg)](https://github.com/1917360964/VoiceDuck/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-4c7cf3.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-287be7.svg)](#系统要求)

![VoiceDuck 主界面](docs/images/preview.png)

VoiceDuck 是一个轻量的 Windows 音频闪避工具。它监测指定通话应用的实时输出电平，只在对方实际发声时降低选定的音乐或背景应用音量，不需要虚拟声卡、音频驱动或管理员权限。

## 主要特点

- **通话与音乐分开选择**：微信、QQ 等作为触发端，网易云音乐、QQ 音乐、Spotify 等作为被降低端。
- **短提示音过滤**：声音需要持续一小段时间才触发，减少消息提示音造成的误动作。
- **自然的断句保持**：跨过说话中的短暂停顿，音乐不会在字词之间频繁起伏。
- **按原音量成比例降低**：音乐原来是 40%，保留 25% 时降到 10%，恢复后仍回到 40%。
- **多个设备与会话**：扫描所有活动输出设备，并独立处理同一进程创建的多个音频会话。
- **尊重手动调节**：闪避期间在系统音量混合器里手动调整，VoiceDuck 会以新的音量为基准。
- **退出自动恢复**：暂停或正常退出时，恢复由程序调整过的会话音量。
- **托盘常驻与开机启动**：适合长期后台运行。

## 下载与使用

从 [Releases](https://github.com/1917360964/VoiceDuck/releases) 下载最新便携版，解压后直接运行 `VoiceDuck.exe`。

1. 先让通话软件和音乐软件各播放一次声音。
2. 点击“刷新”，在左侧勾选通话应用，在右侧勾选音乐应用。
3. 点击“开始自动闪避”。第一次使用建议选择“均衡（推荐）”。
4. 如果消息提示音仍会触发，提高“防提示音延迟”；较轻的人声无法触发时，降低“触发阈值”。

关闭主窗口默认会最小化到托盘。要完全退出，请右键托盘图标并选择“退出并恢复音量”。

## 常用预设

| 预设 | 适合场景 | 特点 |
| --- | --- | --- |
| 均衡 | 日常微信、QQ 通话 | 响应速度与自然度平衡 |
| 游戏语音 | 游戏内语音、Discord | 降低更快、恢复更利落 |
| 会议专注 | Teams、Zoom 长时间会议 | 背景压得更低，断句保持更长 |
| 柔和音乐 | 轻音乐、播客背景 | 音量变化更缓和 |

## 工作原理

VoiceDuck 使用 Windows Core Audio API 枚举每个播放设备上的音频会话，并读取会话峰值。触发应用的声音连续超过阈值后，程序按配置的 Attack 曲线降低目标会话；声音回落并经过 Hold 时间后，再按 Release 曲线恢复。

详细设计见 [架构说明](docs/ARCHITECTURE.md)。

## 隐私

VoiceDuck 不录音、不保存音频内容，也不连接网络。程序只读取 Windows 提供的会话峰值、进程名和音量，并在本机调整被选中会话的音量。配置保存在 `%APPDATA%\VoiceDuck\settings.json`。

## 已知限制

微信和 QQ 没有向第三方提供“远端用户正在说话”的公开接口，因此 VoiceDuck 根据应用输出电平判断，而不是识别语音内容。持续时间较长的铃声或提示声仍可能触发，可以通过提高“防提示音延迟”改善。

浏览器既可能播放音乐，也可能承载网页通话。同一个浏览器同时被选为触发端和目标端时，VoiceDuck 会优先保护触发端，不降低它。

## 从源码构建

系统自带的 .NET Framework 4.8 编译器即可完成构建，不需要下载第三方包：

```powershell
.\scripts\build.ps1
```

构建产物写入 `artifacts/`。脚本会依次编译主程序、运行核心逻辑测试、执行只读音频会话冒烟测试，并编译界面渲染测试。

生成版本发布包：

```powershell
.\scripts\package-release.ps1 -Version 1.0.0
```

也可以使用 Visual Studio 2022 打开 `VoiceDuck.sln`。

## 系统要求

- Windows 10 或 Windows 11
- .NET Framework 4.8
- 与目标应用运行在同一个 Windows 用户会话中

## 参与维护

欢迎提交问题和改进。开始前请阅读 [贡献指南](CONTRIBUTING.md)。版本变化记录在 [CHANGELOG](CHANGELOG.md)。

## 许可证

[MIT License](LICENSE)
