# Fork 插件部署与安装手册

本仓库是 [上游 MetaTube 插件](https://github.com/metatube-community/jellyfin-plugin-metatube) 的改造版。`feat/metadata-enrichment` 增加 Jellyfin Web 来源评分与评论面板，需要配套 [Go 后端 Fork 的同名分支](https://github.com/kevinmatthe/metatube-sdk-go/tree/feat/metadata-enrichment)。上游插件发布包不包含本分支新增功能。

本手册的 Jellyfin 构建基于 12.0 SDK、.NET 10。已通过构建、接口与本地 Chromium 检查，真实服务器的安装与展示验收尚未完成。Emby 使用独立构建，新增 Web 面板不适用于 Emby、原生客户端或独立托管的 Jellyfin Web。AI 评论摘要尚未接入。

## 一、先部署配套后端

按 [后端部署手册](https://github.com/kevinmatthe/metatube-sdk-go/blob/feat/metadata-enrichment/docs/deployment.md) 构建 Go 服务，设置 Token，使用文件 SQLite 或 PostgreSQL，并执行数据库迁移。空 DSN 的默认内存数据库不能跨重启保存评论。

可选 JAVDB 还需要在后端运行环境中安装 javdb-cli v0.8.2，并显式启用 provider；仅开启插件选项不足以完成接入。详情见 [JAVDB 配置说明](https://github.com/kevinmatthe/metatube-sdk-go/blob/feat/metadata-enrichment/docs/development/javdb.md)。

## 二、构建 Jellyfin 插件

准备 Git 和 .NET 10 SDK，在可写工作目录执行：

```sh
git clone --branch feat/metadata-enrichment https://github.com/kevinmatthe/jellyfin-plugin-metatube.git
cd jellyfin-plugin-metatube
dotnet build Jellyfin.Plugin.MetaTube/Jellyfin.Plugin.MetaTube.csproj -c Release
```

产物：

- DLL：`Jellyfin.Plugin.MetaTube/bin/Release/net10.0/MetaTube.dll`。
- 安装 ZIP：`Jellyfin.Plugin.MetaTube/bin/Jellyfin.MetaTube@v<构建版本>.zip`，其中只包含插件 DLL。

项目默认按构建时间生成版本号，可用 `-p:Version=2026.1020.1.0` 这类四段版本号固定构建版本。需要 Emby 产物时单独执行 `-c Release.Emby`，其输出为 `bin/Release.Emby/net8.0/MetaTube.dll` 及 `Emby.MetaTube@v<构建版本>.zip`，不要与 Jellyfin 的 DLL 混装。

## 三、安装到 Jellyfin

1. 停止 Jellyfin，备份现有 MetaTube 插件目录及插件配置。备份放在插件扫描目录之外，避免重复加载。
2. 确认 Jellyfin 的实际插件数据目录。原生 Linux 常见位置为 `/var/lib/jellyfin/plugins`，容器常见位置为持久化的 `/config/plugins`，以自己的服务配置为准。
3. 将本次构建的 `MetaTube.dll` 放入插件目录中的 MetaTube 子目录；保持只有一份待加载的 MetaTube 插件，避免旧新 DLL 并存。确认 Jellyfin 服务用户可读。
4. 启动 Jellyfin，检查日志中是否有插件加载错误，并在控制台的插件列表中确认 MetaTube 与本次构建版本。

示例复制命令中的路径需要先改成自己的实际持久化目录，在 Jellyfin 停止期间执行：

```sh
JELLYFIN_PLUGIN_DIR=/your/persistent/jellyfin/plugins/MetaTube
mkdir -p "$JELLYFIN_PLUGIN_DIR"
install -m 644 Jellyfin.Plugin.MetaTube/bin/Release/net10.0/MetaTube.dll "$JELLYFIN_PLUGIN_DIR/MetaTube.dll"
```

容器部署必须将文件写入挂载的数据目录，不能只写到容器的临时文件系统。升级实际服务器前保留旧版本，以便恢复。

## 四、配置

在 Jellyfin 控制台进入 MetaTube 配置页：

| 设置 | 填写方式 |
| --- | --- |
| Server | 从 Jellyfin 进程可访问的后端根地址，例如同一容器网络中的 `http://metatube:8080`；不是浏览器到后端的地址 |
| Token | 与后端 Token 一致 |
| Show source ratings and reviews | 默认开启，控制新增 Web 面板 |
| Use JAVDB ratings and reviews | 默认关闭；后端已正确安装并启用 JAVDB 后再开启 |

保存后重新加载 Jellyfin Web；若旧资源被浏览器缓存，可进行一次强制刷新。面板请求通过当前 Jellyfin 用户会话访问服务器，由插件代读后端，不向浏览器提供后端 Token。

使用 JAVDB 补充时，已有影片需具有主来源 MetaTube ProviderId，并能从主来源资料取得已验证番号。缺少番号、匹配不唯一或需要登录时会显示状态，不会猜测条目。

## 五、检查改造功能

打开一个已有 MetaTube 关联的影片详情页，核对：

1. 自动出现来源评分与评论面板，无需点击“加载”按钮。
2. 原始评分带来源与分制；未知人数不被显示为零。
3. 第一次请求保存评论页，返回详情页时读取缓存；后端重启后仍保留。普通列表页与扫描不抓取评论。
4. “更多评论”只读取下一页；“刷新评论”显式刷新第一页。失败保留已有显示内容，点击重试才再次请求。
5. JAVDB 开启时，评分追加 JAVDB 来源，评论切换为 JAVDB，不额外抓主来源评论；来源限制会明确显示状态。
6. 后端过滤下载链接，纯链接评论跳过，条数可能不足十条；分页仍沿用来源位置，不会额外补抓。顺序保留来源默认顺序。
7. 使用普通受限用户确认不可见影片不能读取缓存；有 BaseUrl 时检查资源和详情请求均带正确前缀。

目前已有单条 JAVDB 在线读取、持久化和缓存命中的验证记录，尚不能替代上述真实 Jellyfin 验收。

## 六、停用与回滚

关闭 **Use JAVDB ratings and reviews** 可恢复原评论来源；关闭 **Show source ratings and reviews** 可停止面板请求。恢复旧插件时停止 Jellyfin，将旧 DLL 与配套配置恢复到原位置后启动。后端如需回滚，使用配套旧程序和升级前数据库备份，具体见后端手册。

## 七、开发检查

```sh
dotnet run --project tests/MetaTube.Metadata.Checks
dotnet build Jellyfin.Plugin.MetaTube/Jellyfin.Plugin.MetaTube.csproj -c Release
dotnet build Jellyfin.Plugin.MetaTube/Jellyfin.Plugin.MetaTube.csproj -c Release.Emby
node --test tests/web/metadata.test.cjs
CHROMIUM_PATH=/path/to/chrome node tests/web/browser-check.cjs
```

浏览器检查需要 Node 22 和 Chromium，无需额外 npm 依赖；数据使用本地中性样本，不访问来源站点。完整接口、缓存及客户端适用范围见 [开发说明](development/metadata-enrichment.md)。历史兼容性调研保留在 `docs/research/`，实际部署以当前代码与本手册为准。
