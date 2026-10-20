# 来源评分与评论面板

本 Fork 的安装、配套后端及回滚步骤见 [部署手册](../deployment.md)。新增能力与适用范围也列于 [README](../../README.md)。

Jellyfin 12 的影片详情页可以显示来源原始评分、分制、已知评分人数、评论人数、评分分布、分类标签及公开评论。标准 `CommunityRating` 继续使用十分制；标准 Genres/Tags 继续写入影片元数据。来源评论单独展示，不写入影片简介或本地用户评价。

## 使用

1. 同时更新 [Go 后端 fork](https://github.com/kevinmatthe/metatube-sdk-go) 和本插件；工作区中的两份仓库均使用 `feat/metadata-enrichment` 分支。
2. Go 后端必须使用持久化数据库。例如启动时设置 `-dsn /data/metatube.db`，容器需要挂载 `/data`。原仓库默认空 DSN 是内存 SQLite，重启会丢失数据。已有 PostgreSQL 部署需要启用 `-db-auto-migrate` 以添加评分字段和评论页表；SQLite 启动时自动迁移。后端 Token 仍只配置在插件设置中。
3. 构建并安装插件，重启 Jellyfin。配置页的 **Show source ratings and reviews** 默认开启；修改后重新加载 Web 页面。新功能要求新版后端；关闭该选项可停止面板请求。
4. 进入已关联 MetaTube ProviderId 的影片详情页，自动读取第一页十条评论。首次没有缓存时才抓取，并保存该页。再次进入优先读保存结果；已保存的评论页不会自动过期。列表页和普通媒体库扫描不拉取评论。

“更多评论”只读取下一页。“刷新评论”显式重新抓取第一页，其他已经保存的页保持原数据。失败时保留数据库缓存和当前显示的评论，可以重试失败的页。空评论页同样保存。保存时间表示来源数据的获取时间，不会因缓存读取而更新。电影基础资料的独立缓存期为 24 小时。

后端丰富了 FANZA 和 FC2Hub 已有评分数据，FANZA digital 支持真实分页，其他已有评论来源使用其可获得的快照。人数未知显示为缺省，不把当前加载条数当作来源评论总数。

可选 **Use JAVDB ratings and reviews** 默认关闭。后端需安装 javdb-cli（按 v0.8.2 JSON 命令契约核对），配置 `MT_MOVIE_PROVIDER_JAVDB__ENABLED=true` 和 `MT_MOVIE_PROVIDER_JAVDB__BINARY=/path/to/javdb`，并使用持久数据库。启用插件选项后，以主来源的已验证番号查询 JAVDB，追加其原始五分制评分，并将评论来源切换为 JAVDB；不会额外请求主来源评论或在扫描逻辑中调用 JAVDB。主来源资料过期且上游失败时，Go 后端保留旧资料和时间戳，以便继续识别影片并读取保存的评论。

来源命令串行运行，开始时间间隔至少 10 秒；识别到 429、403 或登录错误后暂停至少 30 分钟。CLI 自身的番号解析、传输重试和 Token 拒绝后匿名回退可能带来额外 HTTP 请求，CLI 也不提供 Retry-After 响应头，因此该间隔不能被描述为逐 HTTP 请求限速。暂停状态只在单一后端实例内有效，重启会重置。缺失/模糊匹配、需要登录、拒绝访问及限流在面板分别提示，浏览器不会自动重试。部署和配置详情见 [Go 仓库 JAVDB 手册](https://github.com/kevinmatthe/metatube-sdk-go/blob/feat/metadata-enrichment/docs/development/javdb.md)。2026-10-20 已按用户指定的 `SSNI-081` 实连后端验证：评分 `4.37 / 5`，第一页十条评论成功保存并命中后续缓存；没有翻页。随后对历史缓存的只读检查确认下载链接过滤后返回九条，无新增来源请求。评论保留 JAVDB 接口默认顺序，该样本呈点赞数降序；不能据此保证所有返回都严格按点赞排序。Jellyfin 实际安装和展示验收仍未进行。

## 接口与权限

`GET /MetaTube/Items/{itemId}/Metadata?offset=0&limit=10&refresh=false` 使用当前 Jellyfin 用户会话。请求先通过用户存在性、影片媒体库和家长控制可见性检查，再访问后端。匿名、不可见影片和没有 MetaTube ID 的影片不能读取缓存。`limit` 为 1..50，`offset` 非负。

返回 `{provider,id,number,ratings,tags,reviews,metadata_status,review_status,review_provider,javdb_status}`。两类资料独立处理：基础资料暂不可用仍能返回已保存评论；评论暂不可用仍能返回评分。`review_status` 区分 `available`、`unsupported`、`unavailable`；`javdb_status` 另外说明该可选来源的匹配、登录、限流等状态。浏览器不会得到 Go 后端地址或 Token。公开的 `/MetaTube/metadata.js`、`metadata.css` 只包含静态资源。

Web 面板通过插件 middleware 在 Jellyfin 服务器提供的 HTML shell 中加入资源，支持 BaseUrl。Jellyfin 12 Modern 和 Legacy 当前共享的详情页 DOM 均适用。外部评论以文本节点渲染；导航中止旧请求，保留隐藏旧页的场景会将面板移动到当前页。浏览器仅请求当前同源服务器，并校验路由 serverId。

原生电视/移动客户端、单独托管的 Jellyfin Web 和 Emby 不会自动显示这个 Web 面板；这些客户端仍可使用标准评分与标签。Emby 保留原有构建兼容性。完整安装到 Jellyfin 12 服务上的人工验证尚未执行。

## 离线验证

```sh
dotnet run --project tests/MetaTube.Metadata.Checks
dotnet build Jellyfin.Plugin.MetaTube/Jellyfin.Plugin.MetaTube.csproj -c Release
dotnet build Jellyfin.Plugin.MetaTube/Jellyfin.Plugin.MetaTube.csproj -c Release.Emby
node --test tests/web/metadata.test.cjs
CHROMIUM_PATH=/path/to/chrome node tests/web/browser-check.cjs
```

需要 .NET 10 SDK、Node 22 和 Chromium。无需安装额外 npm 包。浏览器测试使用本地中性页面及 Go 序列化 JSON 样本，覆盖自动加载、分页、失败重试、隐藏旧页、导航取消、会话、BaseUrl、文本安全和手机/桌面尺寸。该测试中的来源计数器模拟后端缓存，真实数据库关闭重开由 Go 测试覆盖。

`tests/fixtures/*.json` 是 Go 仓库 `model/testdata/` 样本的逐字副本，方便独立克隆本插件时执行检查。变更合同字段时同步两份，并执行两仓库检查。
