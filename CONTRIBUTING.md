# 参与贡献

感谢你愿意改进 VoiceDuck。小而明确的改动最容易审查，也最容易稳定地进入版本。

## 开始之前

1. 先搜索已有 Issue，避免重复问题。
2. 修复缺陷时，请尽量写清复现步骤、Windows 版本、播放设备和相关应用版本。
3. 新功能建议先开 Issue 讨论使用场景，尤其是涉及音频路由、驱动或权限的改动。

## 本地开发

```powershell
git clone https://github.com/1917360964/VoiceDuck.git
cd VoiceDuck
.\scripts\build.ps1
```

项目不依赖 NuGet 包。主程序使用 .NET Framework 4.8 和 Windows Core Audio COM 接口。

## 代码约定

- 保持音频线程与界面线程分离，不在音频轮询循环中阻塞 UI。
- 新的音量控制路径必须在暂停和退出时可恢复。
- COM 对象需要明确释放，避免长期托盘运行时积累资源。
- 进程名匹配应忽略大小写和可选的 `.exe` 后缀。
- 新增门限、计时或音量算法时，请在 `tests/CoreTests` 增加覆盖。
- 不引入网络、遥测或音频内容存储，除非项目方向经过明确讨论。

## 提交 Pull Request

- 一个 Pull Request 只解决一个主题。
- 标题使用简短的动词短语，例如 `Fix volume restore after device switch`。
- 描述中写明动机、实现方式、验证结果和可能影响的边界情况。
- 提交前运行 `.\scripts\build.ps1` 并确保所有测试通过。
