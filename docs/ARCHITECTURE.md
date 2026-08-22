# 架构说明

VoiceDuck 把自动闪避、实时混音和驱动安装分成独立边界，驱动操作不会混入应用启动或自动闪避路径。

## 数据流

```text
Windows Core Audio Sessions
        │
        ▼
AudioSessionGraph ──► DuckingCoordinator ──► 按会话调节背景应用音量
                              │
                              ├── VoiceGate：阈值 / 触发延迟 / 断句保持
                              └── VolumeState：基准音量 / 外部调整 / 恢复

本地音乐文件 ──► Media Foundation 解码 ─┬─► Music only ─► 真实耳机
                                       └─► 音乐增益 ─┐
物理麦克风 ──► WASAPI capture ─► 麦克风增益 ───────┼─► 限幅 ─► CABLE Input
                                                   └────────► CABLE Output ─► 通话输入
通话输出 ───────────────────────────────────────────────────► 真实耳机
```

## 模块职责

### CoreAudio.cs

封装 MMDevice 与 Audio Session COM 接口。`AudioSessionGraph` 使用“设备 ID + 会话实例 ID”作为稳定键，支持同一进程的多个会话。COM 操作在专用 MTA 音频线程执行，界面只读取快照。

### DuckingCoordinator.cs / VoiceGate.cs

纯状态机与音量策略。它们决定何时闪避、保存基准音量、平滑改变音量、识别用户手动调整，并在暂停或退出时恢复。

### AudioEndpoints.cs

使用 NAudio 的 Core Audio 封装枚举活动端点。物理设备选择器排除已知虚拟端点；VB-CABLE 探测要求同时找到 `CABLE Input` 播放端和 `CABLE Output` 录音端。

### MusicShareAudioEngine.cs

音乐分享实时路径固定为 48 kHz、双声道、32 位浮点：

- WASAPI 以共享模式捕获所选物理麦克风；
- Windows Media Foundation 解码两条独立音乐读取流；
- 一条音乐流只送真实耳机；
- 另一条音乐流与麦克风混合、限幅后送 `CABLE Input`；
- 暂停音乐时，音乐提供器输出静音但混音器保持运行，因此麦克风不会断线；
- 所有设备与流在停止、异常启动回滚和应用退出时释放。

该模块不捕获默认扬声器，所以通话远端声音不会进入发给对方的总线。

### VirtualAudioInstaller.cs

内置未修改的 VB-CABLE Driver Pack 45。只有用户明确点击时才：

1. 验证嵌入压缩包 SHA-256；
2. 在受控临时目录内防路径穿越解压；
3. 验证对应架构安装程序的固定 SHA-256；
4. 通过 WinVerifyTrust 验证 Authenticode，并核对 VB-Audio 发布者；
5. 请求 UAC 后运行官方安装或卸载命令；
6. 删除临时文件并重新探测端点。

安装器不写系统默认音频端点，也不自动重启。`CallSafetyInspector` 在微信、QQ 等通话音频会话存在时阻止安装和卸载。

### MusicShareForm.cs

提供驱动状态、设备选择、音乐文件、双增益、电平、播放控制与路由确认。通话应用的输入端点需要用户首次在应用自身设置中选择，因为微信、QQ 等没有统一公开的第三方配置接口。

### SettingsStore.cs

使用 `DataContractJsonSerializer` 保存配置。写入先落临时文件再替换正式文件，减少异常退出造成半截配置的概率。

## 线程模型

- UI 线程：WinForms、配置与用户动作。
- 自动闪避线程：音频会话枚举、峰值读取、会话音量写入与状态机。
- WASAPI 捕获线程：物理麦克风数据进入有界缓冲区。
- 两个 WASAPI 播放线程：真实耳机音乐与虚拟麦克风混音。
- 驱动操作工作线程：仅在用户明确触发后等待官方安装程序退出。

## 恢复保证

自动闪避只记录自己实际接管的会话，暂停与正常退出均恢复基准音量。音乐分享不修改系统默认设备，停止时释放 WASAPI 流；若分享启动中任一步失败，会执行同一清理路径回滚已经创建的流。
