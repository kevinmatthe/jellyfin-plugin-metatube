# MetaTube 适配 Jellyfin 12.x 调研

调研日期：2026-09-16。代码基线：`f7c1f33`。

## 结论

适配可行，当前证据支持先做小范围的构建与发布配置升级。临时副本仅修改目标框架和两个 Jellyfin NuGet 版本，分别针对 12.0.0、12.1.0 编译，均成功生成 DLL 和 ZIP；没有修改任何 C# 业务代码。Emby 构建也通过。

这证明源码可以针对两版 SDK 构建，尚不证明插件已在真实服务器正常运行，也不证明同一 DLL 已通过跨版本加载验证。本次仓库只新增调研报告，实验代码和产物位于仓库外。

## 版本与现状

- Jellyfin 原计划的 10.12 改名为 12.0，服务器版本为 `12.0.0`，Git tag 为 `v12.0`。12.0 于 2026-09-07 发布，要求 .NET 10，官方要求插件重新构建。[官方公告](https://jellyfin.org/posts/jellyfin-release-12.0/)
- 12.1 已于 2026-09-15 发布，是后续修复版本。[12.1 发布说明](https://github.com/jellyfin/jellyfin/releases/tag/v12.1)
- 官方 NuGet 已有 [Jellyfin.Controller 12.0.0](https://www.nuget.org/packages/Jellyfin.Controller/12.0.0) 和 [Jellyfin.Model 12.0.0](https://www.nuget.org/packages/Jellyfin.Model/12.0.0)，目标框架均为 `net10.0`；对应 12.1.0 包也已用于本次构建实验。
- 当前仓库 Jellyfin 配置是 `net9.0`、两个 `10.11.0` 包；Emby 配置是 `net8.0`、`MediaBrowser.Server.Core 4.9.1.80`。
- `.github/workflows/dotnetcore.yml` 只显式安装 .NET 8，与当前 Jellyfin 的 .NET 9 目标已不一致，升级时必须明确安装所需 SDK。
- 上游已有尚未关闭的 [Jellyfin 12.0 Compatibility #646](https://github.com/metatube-community/jellyfin-plugin-metatube/issues/646)。该 issue 罗列的服务器变化不能直接等同于本插件的必要修改项。

## 实际验证

在临时副本中使用 .NET SDK `10.0.401`，执行项目现有 Release 构建与 ZIP 打包目标。

| 实验 | 副本修改 | 结果 |
| --- | --- | --- |
| Jellyfin 12.0 | `net9.0` → `net10.0`；两个 Jellyfin 包 → `12.0.0` | 0 错误，生成 `MetaTube.dll` 和 ZIP |
| Jellyfin 12.1 | `net9.0` → `net10.0`；两个 Jellyfin 包 → `12.1.0` | 0 错误，生成 `MetaTube.dll` 和 ZIP |
| Emby | 在 12.0 实验副本执行 `Release.Emby`，Emby 配置不变 | 0 警告、0 错误，生成 DLL 和 ZIP |
| 清单生成 | 调用现有 `scripts/manifest.py` 的 `generate()` | 分别得到 `12.0.0.0` / `12.1.0.0`，ZIP 均仅包含 `MetaTube.dll` |

两个 Jellyfin 构建均出现 `NU1510`：显式引用的 `System.Memory 4.5.5` 已由目标框架提供；还原和构建重复报告，共 2 条警告。它不阻止构建，可在 Jellyfin 条件分支移除该依赖，Emby 是否同步清理应另行验证。

实验命令形式：

```sh
dotnet build Jellyfin.Plugin.MetaTube/Jellyfin.Plugin.MetaTube.csproj \
  --configuration Release -p:Version=2026.916.0.0
dotnet build Jellyfin.Plugin.MetaTube/Jellyfin.Plugin.MetaTube.csproj \
  --configuration Release.Emby -p:Version=2026.916.0.0
```

实验资料目录：`/tmp/metatube-jellyfin12-research/`。其中 `build-12.0.0.log`、`build-12.1.0.log`、`build-emby.log` 是完整日志，`probe-*` 是临时项目。SDK 安装于 `/root/.cache/metatube-jellyfin12-research/dotnet/`，未改动系统 SDK 或 shell 配置。

## 源码兼容性核查

对比官方 `v10.11.0` 与 `v12.0` 源码：

| 本插件功能 | 核查结果 | 适配判断 |
| --- | --- | --- |
| 影片、演员刮削 | `IRemoteMetadataProvider` 文件未变，`GetMetadata` / `GetSearchResults` 签名保留 | 可先保留现有实现 |
| 影片、演员图片 | `IRemoteImageProvider` 文件未变 | 可先保留现有实现 |
| 外部 ID、外部链接 | `IExternalId`、`IExternalUrlProvider` 文件未变 | 可先保留现有实现 |
| 定时任务 | `IScheduledTask` 文件未变；`ILibraryManager.GetItemList`、`UpdateItemAsync` 及所用查询字段仍存在 | 没有证据要求改成新的查询接口 |
| 演员写入 | `PersonInfo` 文件未变，`MetadataResult.AddPerson` 仍存在 | 重点回归去重、头像和关联影片 |
| 合集 | `Movie.CollectionName` 仍映射到 `TmdbCollectionName` | 编译兼容，需验证实际合集行为 |
| 预告片、字幕角标 | 所用 `ContainingFolderPath`、`DateLastSaved`、`SetImage` 仍存在 | 需验证文件扫描、图片持久化和任务重复执行 |

可核对的官方源码：[元数据接口](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Providers/IRemoteMetadataProvider.cs)、[图片接口](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Providers/IRemoteImageProvider.cs)、[媒体库接口](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Library/ILibraryManager.cs)、[Movie](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Entities/Movies/Movie.cs)。

一个需关注的行为变化：`MetadataResult` 移除了默认将 `ResultLanguage` 设为 `en` 的构造逻辑。本插件没有主动设置此字段。应回归翻译后标题、简介及刷新行为；如果需要设置，应填写实际返回语言，不能在翻译失败或禁用时一律标记成用户目标语言。[MetadataResult 源码](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Providers/MetadataResult.cs)

### 配置页

现有页面使用 jQuery、`Dashboard`、`ApiClient.getPluginConfiguration` / `updatePluginConfiguration`。Jellyfin Web 12.0 仍为插件暴露 `Dashboard` 和 jQuery；已适配 12.0 的官方 TMDb Box Sets 插件仍使用相同模式。没有证据要求把本页面重写成 React。[Web Dashboard](https://github.com/jellyfin/jellyfin-web/blob/v12.0/src/utils/dashboard.js)、[Web 打包配置](https://github.com/jellyfin/jellyfin-web/blob/v12.0/webpack.common.js)、[官方插件配置页](https://github.com/jellyfin/jellyfin-plugin-tmdbboxsets/blob/v14/Jellyfin.Plugin.TMDbBoxSets/Configuration/configurationpage.html)

仍需在实际 12.x Web 页面验证加载、保存和重启后的回显。顺带发现现有 DeepL URL 保存字段使用 `DeepLXAltUrl`，读取及 C# 配置使用 `DeepLApiUrl`，这是独立的现存问题，应单独修复和验证，不能归因于 Jellyfin 12。

### 不构成当前迁移工作的变化

本插件没有实现 `ISearchEngine`、认证提供器、用户管理器，也没有直接访问 `IItemRepository` 或播放列表/多版本存储。服务器新搜索扩展能力可作为以后独立功能，不是恢复元数据刮削的前提。

`UpdatePluginTask.cs` 整个文件受 `__EMBY__` 条件保护，只参与 Emby 构建。Jellyfin 插件更新由目录清单处理。配置页里的 `emby-input` 等组件名也不等于使用已删除的 `/emby/` HTTP 路由。

## 建议的最小落地方案

1. **依赖**：Jellyfin 分支改为 `net10.0`，两个 Jellyfin 包固定为 `12.0.0`，以支持 12.0 为最低基线；Emby 保持现有条件配置。正式发布前，让同一份 12.0 构建产物在 12.0 和 12.1 服务器上完成回归。此次两个版本分别编译成功不能替代这一步。
2. **CI**：显式配置 .NET 10 SDK；若需要 Emby 的 .NET 8 运行测试，再同时安装 .NET 8。现有 `workflow_dispatch` 会执行发布操作，建议添加独立的 PR/手动构建验证任务，再复用构建过程发布。
3. **清单**：现有脚本已经能够自动产生 `targetAbi: 12.0.0.0`，不需要仅为版本改名重写它。保留旧 10.11 发布记录，用新的插件版本号和 ZIP URL 发布 12.x 产物。
4. **兼容范围**：`targetAbi` 是最低版本条件，不是精确版本匹配或运行兼容性保证。官方安装管理器使用 `ApplicationVersion >= targetAbi` 过滤，再按插件版本排序。因此旧 10.11 记录也可能在新服务器满足此条件，应依赖正确的新发布记录，不能把清单当成完整的 ABI 隔离机制。[安装管理器](https://github.com/jellyfin/jellyfin/blob/v12.0/Emby.Server.Implementations/Updates/InstallationManager.cs)
5. **文档**：完成运行验证后，更新 `README.md`、`README.ja.md` 的版本标识与安装说明。若使用 fork 分发，同步调整清单中的仓库和下载地址，当前脚本写死了上游地址。

## 发布前验收

- 12.0 和 12.1：从插件目录安装、重启加载、配置迁移、再次更新；日志无类型/方法缺失和依赖加载错误。
- 使用同一份待发布 DLL：影片自动匹配、手动识别、强制刷新；核对标题、演员、评分、工作室、标签和系列信息。
- 演员：搜索、头像、资料、关联影片与重复人物；普通用户能查看应有的信息。
- 图片：海报、缩略图、背景图、裁剪与字幕角标；刷新和重启后仍正确。
- 预告片：在测试媒体目录执行生成任务，验证 `.strm` 被扫描和播放，以及重复执行与取消。
- 整理任务：字幕识别、类型替换、保存和重复运行，不影响不属于 MetaTube 的影片。
- 配置页：读取、修改、保存、重启后回显；翻译启用/禁用和失败回退。
- 10.11 目录仍获取旧兼容构建；Emby 产物构建与基础运行正常。

工作量判断：已证实 C# 编译迁移很小，主要剩余工作在 CI/发布配置和服务器回归。完成上述验收后，才适合标记为正式支持 Jellyfin 12.x。
