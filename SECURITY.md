# Security Policy

## 支持范围

当前维护 `1.x` 系列。安全修复会进入最新的稳定版本。

## 报告安全问题

请优先使用 GitHub 的私密漏洞报告功能，不要在公开 Issue 中披露可利用细节。报告中请包含：

- 受影响版本与 Windows 版本；
- 清晰的复现步骤；
- 可能造成的影响；
- 已尝试的缓解方式。

普通功能缺陷和误触发问题请使用公开 Issue 模板。

## 第三方音频组件

VoiceDuck 内置未修改的 VB-CABLE Driver Pack 45，但绝不在应用启动时静默安装。安装和卸载必须由用户明确点击并通过 Windows UAC；程序不会改变系统默认音频设备或自动重启。

执行官方安装程序前，VoiceDuck 会验证嵌入压缩包与安装程序的固定 SHA-256、Windows Authenticode 信任结果以及 VB-Audio 发布者身份。检测到微信、QQ 等通话音频会话时会拒绝驱动操作。

报告相关问题时，请附上 VoiceDuck 版本、Windows 版本、`CABLE Input` / `CABLE Output` 的端点状态以及驱动操作前后的日志；不要上传含个人通话内容的录音。
