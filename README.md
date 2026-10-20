<h1 align="center">Jellyfin Plugin MetaTube</h1>
<p align="center"><b>English | <a href="README.ja.md">日本語</a></b></p>

> **Fork 改造版**：本仓库基于 [metatube-community/jellyfin-plugin-metatube](https://github.com/metatube-community/jellyfin-plugin-metatube) 进行二次开发，保留原有刮削能力，为 Jellyfin Web 增加来源评分和按需评论面板。当前改造位于 `feat/metadata-enrichment` 分支，需要配套使用 [Go 后端 Fork 的同名分支](https://github.com/kevinmatthe/metatube-sdk-go/tree/feat/metadata-enrichment)。

<p align="center">
<img alt="Plugin Banner" src="https://metatube-community.github.io/images/banner-dark.png"/>
<br/>
<br/>

<a href="https://github.com/metatube-community/jellyfin-plugin-metatube/actions">
<img alt="GitHub Workflow Status" src="https://img.shields.io/github/actions/workflow/status/metatube-community/jellyfin-plugin-metatube/dotnetcore.yml?branch=main&logo=github">
</a>
<a href="https://github.com/metatube-community/jellyfin-plugin-metatube/search?l=c%23">
<img alt="GitHub top language" src="https://img.shields.io/github/languages/top/metatube-community/jellyfin-plugin-metatube?color=%23239120&label=.NET&logo=csharp">
</a>
<a href="https://github.com/metatube-community/jellyfin-plugin-metatube/blob/main/LICENSE">
<img alt="License" src="https://img.shields.io/github/license/metatube-community/jellyfin-plugin-metatube">
</a>
<a href="https://github.com/metatube-community/jellyfin-plugin-metatube">
<img alt="gitHub Stars" src="https://img.shields.io/github/stars/metatube-community/jellyfin-plugin-metatube?style=flat">
</a>
<a href="https://github.com/metatube-community/jellyfin-plugin-metatube">
<img alt="Downloads" src="https://img.shields.io/github/downloads/metatube-community/jellyfin-plugin-metatube/total">
</a>
<a href="https://github.com/metatube-community/jellyfin-plugin-metatube/releases">
<img alt="Releases" src="https://img.shields.io/github/v/release/metatube-community/jellyfin-plugin-metatube?include_prereleases&logo=smartthings">
</a>
</p>

## About

MetaTube Plugin for Jellyfin/Emby.

## 本 Fork 新增了什么

| 改造 | 具体行为 |
| --- | --- |
| 影片详情页评分与评论面板 | 进入 Jellyfin Web 影片详情页自动加载第一页，支持更多评论、手动刷新、失败重试；列表页和普通媒体库扫描不新增评论抓取。 |
| 保留来源评分信息 | 展示原始分制、已知人数、分布和分类标签；Jellyfin 标准社区评分按有效来源分制映射为十分制。 |
| 持久化评论 | 配套后端按页保存评论，再次打开优先读取缓存；手动刷新失败时保留已经显示的评论。跨重启保留要求后端配置持久数据库。 |
| 可选 JAVDB 补充 | 以主来源已验证番号匹配 JAVDB，追加来源评分，并切换为 JAVDB 评论；默认关闭，需要后端与插件同时开启。 |
| 来源请求保护 | 后端提供命令间隔、限制后的暂停及下载链接过滤；浏览器不自动重试被限制的请求。 |
| 当前用户权限校验 | 读取评论前验证用户与影片可见性；后端地址和 Token 不传给面板，外部评论按纯文本渲染。 |
| Web 与构建适配 | 覆盖 BaseUrl、页面切换、移动端布局；Jellyfin 构建基于 12.0/.NET 10，保留 Emby 的独立构建。 |

新增面板面向 Jellyfin 服务器提供的 Web 页面，原生电视/移动客户端、独立托管 Web 和 Emby 不会自动显示该面板。已通过离线测试与构建，真实 Jellyfin 安装验收仍待完成。AI 评论摘要尚未接入。

[Fork 部署与安装手册](docs/deployment.md) · [功能、接口与验证说明](docs/development/metadata-enrichment.md)

## Features

- Full Metadata: Including title, overview, genres, director, actors, and studio.
- Full Search: Support searching for movies and actors across various providers.
- Trailer Video: Support trailers without downloading the full trailer videos.
- Scheduled Task: Automatically organize metadata genres and update plugin.
- Face Detection: Cut primary image with face centered by face detection engine.
- Auto Translation: Support translate certain metadata to preferred language.

## Platforms

[![Jellyfin](https://img.shields.io/static/v1?color=%2300A4DC&style=for-the-badge&label=Jellyfin&logo=jellyfin&message=12.x)](https://jellyfin.org/)
[![Emby](https://img.shields.io/static/v1?color=%2352B54B&style=for-the-badge&label=Emby&logo=emby&message=4.9.x)](https://emby.media/)

_NOTE: This project will only support stable versions._

## Documentation

- [Fork deployment and installation / 部署与安装手册](docs/deployment.md)
- [Source ratings, reviews, and JAVDB / 来源评分、评论与 JAVDB](docs/development/metadata-enrichment.md)
- [Plugin installation](https://metatube-community.github.io/wiki/plugin-installation/)
- [Server deployment](https://metatube-community.github.io/wiki/server-deployment/)
- [File naming rules](https://metatube-community.github.io/wiki/naming-rules/)
- [Auto translation](https://metatube-community.github.io/wiki/auto-translation/)
- [Build from source](https://metatube-community.github.io/wiki/build-from-source/)
- [Metadata providers](https://metatube-community.github.io/wiki/metadata-providers/)

Full documentation and examples can be found at [Wiki](https://metatube-community.github.io/wiki/).

## Community

Welcome and feel free to ask any questions at [Discussions](https://github.com/metatube-community/jellyfin-plugin-metatube/discussions).

## Licence

This plugin is released under the [MIT](https://github.com/metatube-community/jellyfin-plugin-metatube/blob/main/LICENSE) License.

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=metatube-community/jellyfin-plugin-metatube&type=Date)](https://star-history.com/#metatube-community/jellyfin-plugin-metatube&Date)
