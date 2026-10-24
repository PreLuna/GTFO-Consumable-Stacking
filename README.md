# GTFO-Consumable-Stacking

消耗品堆叠。本仓包含以下MOD／共享组件，各自维护文件放在自己的目录。

[定位入口](#modules) · [依赖与联动](#relations) · [开发与排查](#development) · [来源说明](#sources)

<a id="modules"></a>
## 定位入口

| 功能／组件 | 版本 | 修改位置与输入 | 维护方式 | 测试材料 |
| --- | --- | --- | --- | --- |
| 消耗品堆叠 `MaxConsumables` | 1.0.4 | [MaxConsumables/src](MaxConsumables/src) | 源码＋身份迁移 | [MaxConsumables/tests](MaxConsumables/tests) |

具体路径、版本、GUID、资源和依赖的机器登记见[repo.json](repo.json)。src是实现，tests是测试；IntegrationTests用于跨MOD场景或调度配方，Shared仅保存真正共享的接口。

仓级测试配方与共享场景见[IntegrationTests](IntegrationTests)。测试文件是否实际执行，以根Test.ps1及配方为准。

普通玩家从[Releases](https://github.com/PreLuna/GTFO-Consumable-Stacking/releases)下载已经构建的安装ZIP，按对应版本说明安装并准备前置；不需要构建工具仓。退出游戏后将ZIP中的BepInEx合并到已配置前置的游戏目录，替换同功能旧版以免重复加载；个人CFG不包含在安装包中。GitHub的Source code／Code ZIP用于开发，不能把baseline原件直接当安装DLL。

<a id="relations"></a>
## 依赖与联动

消耗品堆叠为独立模块，与Hikaria.Core及TheArchive存在可选兼容。修改堆叠或资源所有权时，应核对原生路径和这些框架的交互，不能重复接管同一行为。

| 模块 | 插件硬前置／声明版本 | 可选联动／软依赖 |
| --- | --- | --- |
| MaxConsumables | 无插件硬前置声明 | Hikaria.Core；TheArchive |

所有插件仍需GTFO及相应BepInEx IL2CPP环境。版本保留加载器原始声明表达式；编译引用、运行硬前置和可选桥分别判断。

<a id="development"></a>
## 开发与排查

| 要修改／排查的功能 | 先读哪里 | 联动范围 |
| --- | --- | --- |
| 堆叠行为与框架所有权 | [MaxConsumables/src](MaxConsumables/src) | 所有权兼容入口CoreOverlap.cs；受控框架／Harmony测试见MaxConsumables/tests和根Test.ps1。 |

开发环境：支持C#12编译的PowerShell 7、.NET 6 runtime、GTFO/BepInEx的core与生成的interop。将[共用工具仓](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools)放在本仓旁边，可通过BuildToolsRoot覆盖位置。

先审查并登记输入／维护锁，再在本仓目录执行（路径换成本机实际值，重复构建前显式清理本仓忽略的out）：

```powershell
./Build.ps1 -GameRoot 'D:/Games/GTFO' -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.16'
./Test.ps1 -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.16'
```

根Test.ps1含生产合并逻辑与受控框架下的Harmony所有权回归，需要先构建以取得out/build-manifest.json中的引用环境；不是原生联机测试。

构建与离线测试不安装到游戏。`.cs`是功能／测试／辅助源码，`.ps1`是维护脚本，编译所得`.dll`才供游戏加载；运行CFG不加入源码仓。

维护锁与提交步骤见[共用维护流程](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools#maintenance)，打包要求见[发布与打包](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools#packaging)。仅使用Releases的玩家不需要这些开发步骤。

<a id="sources"></a>
## 来源说明

- MaxConsumables：MaxConsumables及ResourceStack交互兼容；作者 原插件身份INF.MaxConsumables；兼容Hikaria／TheArchive堆叠实现；来源版本 独立补丁1.0.4，不自动更新或重命名被兼容插件。

保留源文件中的作者与版权声明。部分模组包含第三方派生实现或冻结基线，本说明不替代上游许可证，也不新增再分发授权；本仓库没有统一重授权声明。
