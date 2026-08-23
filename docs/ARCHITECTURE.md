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

所选播放端点 ──► WASAPI loopback ─► 重采样 ─► 350 ms 延迟 ─► Echo gate ─┐
物理麦克风 ───► WASAPI capture  ─► 重采样 ─► 麦克风增益 ───────────────┼─► 限幅 ─► CABLE Input
CallAudioActivity ─► 通话进程输出峰值 ──────────────────────► 控制 Echo gate ─┘             │
                                                                                CABLE Output ─► 通话输入

默认输出端点峰值（衰减前） ─► HearingProtectionService ─► 端点 dB 快速衰减 / 慢速恢复
默认输出端点主音量 ───────────────────────────────────────► 系统音量上限钳制
```

## 模块职责

### CoreAudio.cs

封装 MMDevice 与 Audio Session COM 接口。`AudioSessionGraph` 使用“设备 ID + 会话实例 ID”作为稳定键，支持同一进程的多个会话。COM 操作在专用 MTA 音频线程执行，界面只读取快照。

### DuckingCoordinator.cs / VoiceGate.cs

纯状态机与音量策略。它们决定何时闪避、保存基准音量、平滑改变音量、识别用户手动调整，并在暂停或退出时恢复。

### AudioEndpoints.cs

使用 NAudio 的 Core Audio 封装枚举活动端点。物理设备选择器排除已知虚拟端点；VB-CABLE 探测要求同时找到 `CABLE Input` 播放端和 `CABLE Output` 录音端。

### MusicShareAudioEngine.cs

声音分享实时路径固定为 48 kHz、双声道、32 位浮点：

- WASAPI 以共享模式捕获所选物理麦克风；
- WASAPI loopback 捕获所选真实播放端点的全部播放声；
- 两路原生格式经 Media Foundation 重采样为目标格式；
- 播放分支通过有界缓冲形成约 350 ms 延迟，再经过实时回声保护门；
- `AudioEngineService` 独立读取已知或配置通话进程的输出峰值。超过安全阈值时，保护门持续读取并丢弃播放数据，保持至延迟队列越过远端语音；
- 播放分支与麦克风混合、限幅后送 `CABLE Input`；暂停播放声或触发回声保护时，麦克风仍保持在线；
- 所有设备与流在停止、异常启动回滚和应用退出时释放。

Windows 10 无法稳定提供面向普通桌面应用的逐进程 PCM loopback，因此端点捕获会包含该设备上的所有声音。延迟与通话活动门共同避免将远端语音回传；代价是对方讲话时，本地播放声会短暂从分享总线消失。

### VirtualAudioInstaller.cs

内置未修改的 VB-CABLE Driver Pack 45。只有用户明确点击时才：

1. 验证嵌入压缩包 SHA-256；
2. 在受控临时目录内防路径穿越解压；
3. 验证对应架构安装程序的固定 SHA-256；
4. 通过 WinVerifyTrust 验证 Authenticode，并核对 VB-Audio 发布者；
5. 请求 UAC 后运行官方安装或卸载命令；
6. 删除临时文件并重新探测端点。

安装器不写系统默认音频端点，也不自动重启。`CallSafetyInspector` 在微信、QQ 等通话音频会话存在时触发默认选“否”的风险确认，明确显示预计耗时和可能受影响的驱动写入阶段。

官方安装程序退出后，`AudioRecoveryUtility` 继续观察 Windows Audio、默认真实输入输出设备及物理端点，只有连续稳定后才向界面报告完成。若服务未恢复，用户可通过“恢复声音”明确授权一个独立的提升权限恢复进程；该进程仅重启 `Audiosrv`，必要时启动 `AudioEndpointBuilder`。

### MusicShareForm.cs

提供驱动状态、麦克风/播放端点选择、双增益、电平、播放声暂停控制、回声保护状态与路由确认。开始与停止复用一个状态按钮。启用自动默认麦克风后，输入跟随“系统默认设备”的通话应用无需再次手动选择；被应用单独固定的输入端点仍需在应用内修改。

### HearingProtection.cs / HearingProtectionForm.cs

独立 MTA 线程每 25 ms 读取默认渲染端点。`IAudioMeterInformation` 返回端点音量衰减前的峰值，服务把峰值 dB 与当前端点音量 dB 相加估算最终输出；超过配置上限时立即降低 `IAudioEndpointVolume`，安静后按配置速度恢复。系统音量标量同时被限制在用户上限内，外部音量调整会被识别：低于上限的手动调整成为新基础，高于上限的调整被钳制。

该服务不接管 WASAPI 播放流、不修改驱动和默认端点。关闭保护、切换默认输出或退出时，仅恢复由突发峰值产生的临时衰减；音量上限已经钳制的值不会突然跳回高音量。独占模式若绕过软件峰值或软件端点音量则不保证生效。

### SettingsStore.cs

使用 `DataContractJsonSerializer` 保存配置。写入先落临时文件再替换正式文件，减少异常退出造成半截配置的概率。

## 线程模型

- UI 线程：WinForms、配置与用户动作。
- 自动闪避线程：音频会话枚举、峰值读取、通话活动快照、会话音量写入与状态机。
- WASAPI 捕获线程：物理麦克风与播放端点回环数据分别进入有界缓冲区。
- 回声保护线程：每 25 ms 读取通话活动快照并控制播放门。
- 音量保护线程：每 25 ms 读取默认输出端点衰减前峰值并管理端点音量上限、Attack 与 Release。
- WASAPI 播放线程：把最终混音写入虚拟麦克风。
- 驱动操作工作线程：仅在用户明确触发后等待官方安装程序退出。

## 恢复保证

自动闪避只记录自己实际接管的会话，暂停与正常退出均恢复基准音量。音乐分享可在用户勾选后临时把三个系统默认输入角色切到 CABLE Output；切换前持久化原端点，停止、失败和退出时只恢复仍由 VoiceDuck 管理的角色。停止时同时释放 WASAPI 流；若分享启动中任一步失败，会执行同一清理路径回滚已经创建的流和默认输入。
