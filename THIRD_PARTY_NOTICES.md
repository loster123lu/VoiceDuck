# 第三方组件说明

## NAudio 2.3.0

VoiceDuck 使用 `NAudio.Core` 与 `NAudio.Wasapi` 2.3.0 进行 WASAPI 麦克风/播放端点捕获、Media Foundation 重采样、缓冲、混音与虚拟端点播放。

- 项目：https://github.com/naudio/NAudio
- NuGet：https://www.nuget.org/packages/NAudio/2.3.0
- 上游提交：`c89fee940ee6f8d7374d18714a6b85d8b7a18ab0`
- 许可证：MIT

发布包中的 `ThirdPartyLicenses/NAudio-LICENSE.txt` 包含许可证全文。仓库固定版本 DLL 与包校验值见 `third_party/NAudio/2.3.0/NOTICE.md`。

## VB-CABLE Driver Pack 45

音乐分享功能内置 VB-Audio（Vincent Burel）发布的标准 VB-CABLE Driver Pack 45，发布时间为 2024 年 10 月。官方压缩包保持未修改状态，VoiceDuck 只在用户明确点击后校验、解压并调用其官方安装程序。

- 产品：https://vb-audio.com/Cable/
- 官方包：https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip
- 分发与使用条款：https://vb-audio.com/Services/licensing.htm
- 压缩包 SHA-256：`B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB`

VB-CABLE 是 donationware。VoiceDuck 保留其产品名称、发布者身份和来源，用户可通过官方页面捐赠。专业与批量使用仍应遵守 VB-Audio 当前条款。发布包中的 `ThirdPartyLicenses/VBCABLE-NOTICE.md` 也会保留这些信息。

VB-CABLE 不采用 VoiceDuck 的 MIT 许可证；VoiceDuck 的开源许可只覆盖本项目代码。
