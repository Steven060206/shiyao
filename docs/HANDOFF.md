# 拾要（原消息提纯）：项目交接文档

最后更新：2026-10-16（北京时间）。后续每次需求或实现发生变化，在同一轮工作中更新本文。

当前总览：M3 正在实施；完整编辑、可读变更记录与桌面卡片变化标记、手动新增任务、手动合并/保持独立/拆分、跨群可能重复建议、消息缓存清理、已接入本地数据的备份恢复及桌面卡片恢复已写入代码，并通过自动回归与 Native Smoke 合成验证。待确认批量忽略和加入任务已实现、部署，真实页面控件已核对，批量修改仅用合成数据验证。消息时间倒序、独立回收站与无待办时隐藏提醒卡片已实现、部署并实机核对，忽略和确认记录保留。从桌面快捷方式启动拾要或点击“自动同步”触发一轮同步并恢复普通 QQ 已实现、部署并实机核对；托盘只显示窗口，定时同步已取消。原图未下载，备份明确不完整；完整图片恢复、真实 QQ 编辑场景、实际游戏、Windows 登录自启动、真实桌面恢复及长期稳定性仍未验收。历史静音呈现显示与用户实际点击通过，对应报告继续保留，仅作历史记录；Windows 系统通知和声音已取消。QQ/NapCat 继续只读，应用不得向 QQ 群聊或私聊发送消息。

### 待确认通知修复（2026-10-16，代码已实现，验证与发布进行中）

用户截图中的赛前打磨通知被识别为 Inform，因课程或群关联未确认进入 Pending；AccountWorkspace.AcceptanceProblem 对 Inform 返回“此项需要确认为通知，当前事项入口尚不支持。”，正式页面仅有 accept-review 加入任务入口，因此无法确认到通知。用户明确要求修复该运行逻辑。有效规则已同步需求：提供“确认并加入通知”，无需任务日期，保留原文、证据和审核历史，不自动确认群课程、不创建任务；旧待确认直接可用，重复提交/同步不重复创建通知，后续可按现有入口转任务。

已写入代码：ApplicationItem 追加 CanAcceptNotice；独立 accept-notice 命令将 routing.notices、reviews Accepted/Version+1 及 operations before/after 在三库回滚日志事务中一起提交，AcceptedTaskId=null，不新增任务，不改 schema 1。通知ID为 notice-review-{review.Id}，按原路由ID核对来源而非按证据排序猜原消息；原审核候选、原文、证据、课程未确认状态和群关系保留。桥仅允许账号、ID、操作ID、版本及可选generation；页面显示“确认并加入通知”且不渲染任务日期表单，当前仍查看同项时成功后跳到通知并定位原文。旧Pending直接可用，备份恢复、清理保留和后续通知转任务复用既有流程。

已实际运行：Core Release323/323；Infrastructure Release718/718（含新增14项）；Windows Release477/477（含新增21项协议）；node --test tests/*.test.mjs 最终177/177；Python release_packaging_test.py 4/4、OCR25/25及pip check通过。清理后首次 --no-restore 曾退出0但无测试输出，发现obj缺失后已正常restore并确认真实测试计数，未把空运行记为通过。浏览器新回归修复前显示“确认并加入任务”而失败，后续发现openItem不支持notice，接通后89/89通过。Node初次176通过/1失败因README提前换beta.4但当前已发布仍beta.3，保留beta.3示例并复跑177/177通过；发布成功后再同步版本。首次Native合成检查在重开后定位通知超时，根因为Smoke仅等待NavigationCompleted就导航，被首个snapshot默认页覆盖；已按既有模式等待snapshot和initialized，产品流程无需改动；重新publish后 python tests/app_desktop_test.py --executable .tools/published/notice-review-test/MessagePurify.exe 退出0，126项Native/真实桌面父窗检查通过（含7项新增通知审核），synthetic=true。

下一步：限定提交，再从已提交干净源码构建beta.4，核对本机部署及GitHub源码/Release。当前尚未部署或发布。现有 app.css、index.html、application-ui.test.mjs 三项未提交视觉工作保留，只提交本轮新增通知审核测试，不混入既有视觉改动。maintain-context-hygiene技能文件仍未找到，已人工核对有效需求、交接与实现；不操作真实审核、不确认群课程、不触发QQ同步。

### 本机旧产物清理（2026-10-15，已完成并核验）

用户确认项目继续使用，旧副本和测试产物可删除，只保留最新版。保留正式入口 `.tools/published/app-card-controls/` 和最新 `.tools/releases/Shiyao-0.1.0-beta.3-win-x64.zip`；其余33个发布副本及诊断/安装测试、验包和打包中间目录、旧 ZIP、已解压 SDK 的安装压缩包、`bin/obj` 构建输出合计73个目标已删除，文件大小合计13,297,808,549字节（约13.30 GB）。所有删除目标均未纳入Git；源码和测试源码、`.tools/dotnet/`、`.tools/ocr/`、`.tools/napcat/`、账号配置和真实数据保护快照保留。历史记录中的已清理产物路径只表示当时的证据位置，不能继续作为当前仍存在的文件使用。

清理后的文件核验脚本退出0：73个目标均不存在，487个现用程序静态文件的大小、文件标识和修改时间原样，beta.3 ZIP完整性检查通过；正式PID23888仍从原目录运行且Responding=true，没有关闭或重开软件。`node --test tests/project-rules.test.mjs`50/50通过。新清理小节最初放在“当前总览”之前，导致两项按首小节读取总览的文档回归失败；已将清理小节放在总览之后，复跑全部50项通过，没有调整测试断言或产品行为。本轮只清理本机产物并同步文档，不重建、不部署新程序或发布新版本。操作清单和核验结果保存在本次空间清理会话的outputs目录。

### 交付后启动环境排查（2026-10-15，用户确认并非手动退出，已独立启动并核对真实数据）

用户确认不是手动退出并要求继续排查。先前PID48588及14764由工具Start-Process启动后消失，无Application1000/1026崩溃事件；未捕获这两个进程的直接终止事件或退出码。工具进程实际属于启用KILL_ON_JOB_CLOSE的Job（LimitFlags10240），普通子进程继承该执行环境。最初仅检查IsProcessInJob(任意Job)不足以判定是否属于工具Job；后续通过QueryInformationJobObject当前工具Job的PID列表直接核对，最终Explorer桌面COM启动的PID23888不在工具Job中，且GetPackageFullName返回NO_PACKAGE。已有桌面宿主通过Shell.Application.Windows().FindWindowSW(0,0,8,ref hwnd,1).Document.Application.ShellExecute以--background启动，不发起QQ同步。WMI对照35336及普通Shell COM对照35024均由目标NotifyIcon菜单正常退出，未当作自然崩溃；创建后未恢复的挂起诊断进程41168已核对命令行和唯一挂起线程后清理，不留后台辅助进程。

进一步发现关键数据差异来自Codex MSIX AppData虚拟化：工具Python有包身份，其GetFinalPathNameByHandle实际指向含Packages/Codex的重定向路径，WMI/Explorer无包身份访问真实物理AppData；两者逻辑LOCALAPPDATA、账号和KnownFolder相同，并不证明打开同一物理文件。前述82reviews/167operations/pending0及14表原样的结果只适用于包虚拟profile，不能作为真实物理profile迁移前后保持的证明；“WMI卡片3但数据库0”的临时结论已被两份不同物理数据纠正，没有据此改动审核状态或产品代码。GLOBALROOT尝试也会被重定向，真实验证改由无包身份独立Python执行，报告只写D盘工作区。

两套数据已分别用SQLite Backup API保护，不相互合并：包虚拟profile保存于`.tools/message-review-validation/package-physical-initial/`，82reviews、1task、167operations；真实物理profile保存于`actual-physical-initial/`，99reviews、1task、189operations，状态为Ignored82/Accepted1/Rejected1/Pending3/Consolidated12，两者各四库integrity=ok、各4份设置。真实物理保护是在新版首次无包身份启动和自动兼容整理后执行，不能宣称为迁移前快照。`python .tools/message-review-validation/verify-physical-audit.py`退出0：原始路由ID与候选索引、完整原文、版本递增和子项去向均通过；14条原自动Pending经2笔原子合并审计保留2个主项和12个子项，另1条Pending保留，合计由15项待确认整理为3项。99条审核与最新历史after快照一致，84条已处理记录（Ignored82/Accepted1/Rejected1）、原1条任务和187条其他历史操作保留，操作序号1..189连续。

首次无包身份WMI Python执行`python .tools/message-review-validation/verify-physical-delivery.py`验证通过：真实四库integrity=ok、14表与上述迁移后保护快照全部原样，4份设置字节不变，498个正式文件与已验证发布包相同；当时PID23888的activeTasks=0、pending=3，提醒卡隐藏、待确认卡显示，两卡均附着SHELLDLL_DefView、child/noActivate=true、topmost=false。tray-physical-final.json确认目标进程托盘可打开主窗口、QQ进程未变、3001/6099无监听；没有代处理真实条目。工具Job的KILL_ON_JOB_CLOSE是已确认的环境风险，先前两次退出未捕获直接终止事件或退出码，不能据此宣称退出根因已完全证实；独立桌面启动后当前进程仍正常响应，长期稳定性仍未验收。

收尾复核时数据继续使用，首次直接重查原快照的physical-delivery.json失败于actual_database_all_rows_unchanged，保留失败报告，未还原或覆盖任何真实数据。只读差异核对确认新增4笔操作：accept-review1、complete1、ignore2；原189笔操作、原任务、84条原已处理审核和12个合并子项原样保留，消息与路由表未变。账号设置仅ReadVersions变化，另3份设置不变，符合已读状态更新；新增任务与审核当前字段均与最新操作after审计一致。另建`.tools/message-review-validation/actual-physical-after-use-164919/`保护当前数据，99条审核中87条已处理、12个合并子项，当前待确认0；2条任务、193笔操作，序号连续。无包身份WMI执行`capture-physical-current.py`以及`verify-physical-delivery.py --snapshot actual-physical-after-use-164919 --report physical-delivery-after-use.json --audit physical-audit-after-use.json`均成功，报告success=true；四库/14表、4份设置与该使用后快照相同，498个发布文件一致，当前activeTasks=0、pending=0，两卡均隐藏。审计及保留证明为physical-audit-after-use.json、after-use-preservation.json，旧快照和失败结果保留，不将正常新增操作误报为迁移损坏。首次WMI复核包装命令误取多个Python路径导致未启动，改为取第一个真实解释器后执行；只读诊断脚本的“设置应原样”断言也因ReadVersions变化失败，核对字段后按当前快照验证，不撤销已读状态。

证据位于`.tools/message-review-validation/`的package-virtualization.json、final-process-isolation.json、physical-data-protection.json、packaged-data-protection.json、physical-audit-validation.json、physical-audit-after-use.json、physical-delivery-after-use.json、after-use-preservation.json、tray-physical-final.json、process-state-final.json及诊断日志；原仅包环境验证留作历史。QQ与NapCat始终只读，不代用户确认日期、审核或跨profile迁移数据。本轮只更新排查记录和相关回归，程序及beta.3下载包字节未变，无需重建Release。

### 审核粒度反馈（2026-10-15，用户已选择 A，已实现、部署并发布beta.3）

发布构建与部署过程：修复提交 bc4e45f、Native 测试群名恢复提交 b9d109f 已完成；从已提交 b9d109f 的 git archive 导出干净源码到 `.tools/release-stage-beta3-final/`，自包含发布 `.tools/published/github-release-0.1.0-beta.3-final/`，排除另一个界面任务的三项未提交修改。`python tests/app_desktop_test.py --executable .tools/releases/验包 中文空格 0.1.0-beta.3/Shiyao/app/MessagePurify.exe` 最终退出0、119项 Native/桌面检查通过，包括整条审核、完整原文、一次确认、真实表单手动拆分、重开与重放。初次 Native 仅因合成工作区重开未恢复群名失败，b9d109f 修正后通过。ZIP 为 `.tools/releases/Shiyao-0.1.0-beta.3-win-x64.zip`、119924077字节、518条目；`python .tools/message-review-validation/verify-archive.py` 与 `python .tools/releases/verify-package-beta3.py` 均退出0，程序字节一致、ZIP完整性、官方NapCat原包、个人文件缺失及合成CheckOnly/Setup通过，未启动QQ。日志和JSON报告在 `.tools/message-review-validation/` 及 `.tools/releases/`。正式旧进程31140已通过托盘正常退出，exit-formal.log确认ExitVerified=true；停机后执行`python .tools/message-review-validation/protect-data.py`保护的是包虚拟profile，四库integrity=ok，不能作为用户物理profile迁移前保护的证据。确认无正式进程后，安全复制498个已验证发布文件至原正式目录，保留个人配置；工具重开PID48588后消失，最终由桌面Explorer独立后台启动PID23888。真实物理数据和卡片已另行核对，beta.3源码和下载包已公开，具体结果见上方排查段、下方当前接续与GitHub同步记录。

用户反馈同一条 QQ 原消息被拆成多条待确认，造成重复审核；已确认需要减少同一原消息的重复审核。根因是原 MessageRuleExtractor.SplitActions 按标点及动作词产生多个候选，AccountCandidateRouter.Route 逐候选保存，AccountWorkspace.Snapshot 逐条展示。新版仍内部分析行动，但只输出一个完整业务候选，规则版本 m2-local-v2-message-review；多个行动整体待确认，全部为普通通知时整体保存通知，不同日期/角色或不一致建议清空正式和建议时间，保留完整原文。不同目标或混合变更不自动应用到已有任务。

用户已选择 A：默认一条 QQ 原消息只生成一个完整待确认项，保留完整内容，需要独立管理时手动拆开；B 多事项集中审核卡的方案不采用。已写入 Core MessageReviewCandidate、Infrastructure MessageReviewConsolidation 与 AccountWorkspace.MessageReviews、路由兼容及 Native Smoke.MessageReview。同一原消息已有未处理项按旧路由 ID 与原始候选索引核对后原子合并，保留首项 ID、递增版本，子项保留原候选并标记 Consolidated/ConsolidatedIntoId；已确认、已忽略、已拒绝、手动拆分及操作历史保留，不同群/消息 ID 分开，不能因证据排序把引用消息当原消息。旧未路由提取及旧备份恢复也覆盖；单片段遗漏其他日期时重核对完整原文，不能自动任选日期。数据库 schema 仍为 1，JSON 追加可选合并去向、ReviewState 追加内部合并状态。正式入口已更新并正常重开，beta.3下载包已公开发布。工作区已有 app.css、index.html、application-ui.test.mjs 的视觉改版未提交修改，属于已有工作，不能混入本次提交。maintain-context-hygiene 技能文件仍未找到，已人工核对需求、交接与实际源文件。

实际验证：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release` 323/323；同参数对 Infrastructure、Windows 执行分别 704/704、456/456，均退出0。`node --test tests/*.test.mjs` 175/175；`python -m unittest discover -s tests -p release_packaging_test.py` 4/4；`.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py` 25/25；同 Python `-m pip check` 通过。`.tools/dotnet/dotnet.exe run --project tools/MessagePurify.RuleBaseline --no-restore --configuration Release -- --output docs/verification/rule-baseline.md` 退出0，68/68合成样本、高影响错误0，生成新版基线。Native 新增真实审核/拆分表单测试，App `build --no-restore --no-dependencies --configuration Release` 退出0、无警告错误；完整干净发布产物119项Native/桌面检查随后通过，见本节首段。

修复前 Core 新回归 21失败/82通过，旧兼容回归6失败/2通过；修复后均通过。全量初次暴露7处依赖旧自动拆分的测试，按整条审核/显式拆分调整；后续备份夹具遍历新增内部枚举导致数量5而预期4，改为明确四种人工状态，并新增合并状态备份保留回归。新加测试曾误用 ignore-review 操作名，改为实际 ignore 后最终704/704通过。前轮文档定位提交77db15c、51项文档及2项旧行为检查保留为历史。本轮日志位于 `.tools/message-review-validation/`。修复已限定文件提交，随后完成干净源码构建、Native/ZIP/本机部署验证与beta.3发布；本轮提交标识以 Git 日志为准。

### GitHub 更新同步（2026-10-15，beta.3源码和下载包已发布）

一条原消息一项完整审核及旧未处理自动拆分项兼容已发布。完成全部产品验证和修复提交后，`git -c http.https://github.com.proxy= push origin HEAD:main` 退出0，远端main从79ea7bd更新到b9d109fb002531833d2d005aa178c67ebc92c63d；从该已提交源码构建并验证 beta.3，未带入三个未提交界面改版文件。`gh release create v0.1.0-beta.3 --repo Steven060206/shiyao --draft --prerelease --target b9d109fb002531833d2d005aa178c67ebc92c63d --title '拾要 0.1.0-beta.3 · Windows 测试版' --notes-file .tools/releases/release-notes-beta3.md`、`gh release upload v0.1.0-beta.3 .tools/releases/Shiyao-0.1.0-beta.3-win-x64.zip --repo Steven060206/shiyao` 均退出0。按Release列表核对ID413555250、target完整SHA和附件uploaded/119924077字节后，`gh release edit v0.1.0-beta.3 --repo Steven060206/shiyao --draft=false` 退出0。

Release https://github.com/Steven060206/shiyao/releases/tag/v0.1.0-beta.3；`gh api repos/Steven060206/shiyao/releases/tags/v0.1.0-beta.3` 核对draft=false、prerelease=true、附件uploaded、119924077字节、target=b9d109f。`curl.exe --noproxy '*' --head --location --silent --show-error --output NUL --write-out '%{http_code}' https://github.com/Steven060206/shiyao/releases/download/v0.1.0-beta.3/Shiyao-0.1.0-beta.3-win-x64.zip` 退出0，匿名下载最终HTTP200。旧beta.1/beta.2保留。发布说明记录托盘退出后覆盖程序、保留原napcat与app/qq-sync.json，事项沿用本机用户目录；公开源码和下载包不含聊天数据、账号数据库、Token或登录材料。发布结果、README版本及需求文档同步后，运行相关文档回归并提交、推送记录，具体结果见最新交付段。

#### 上次 GitHub 更新（2026-10-14，beta.2源码和下载包已发布）

用户明确要求软件更新后，GitHub也要同步。后续每次软件更新，都同步 GitHub 源码和 Release 下载包；已有直接上传授权持续有效，无需重复确认，规则已写入AGENTS.md与需求。完成测试、验证和Git commit后推送main，从干净的自包含发布目录构建下载包，上传后核对附件状态和公开下载链接。失败或尚未完成时如实记录，不把本地实现记为远端已发布。

本轮开始时只读核对：本地git status --short为空，HEAD为c496dc2，新增e48639e、d4ae69d、d46ed07、c496dc2四项提交；当时GitHub main为7affe498bf3e8f328dcc6c9fd483c2ffbd63cf28，最新Release为v0.1.0-beta.1。本次已发布0.1.0-beta.2，包含保留忽略与确认、消息倒序、回收站及空待办卡片隐藏。构建、本机解压包验证与GitHub上传均已完成。保持现有正式应用和个人数据，使用隔离发布与合成配置，没有发起真实QQ同步。

实际执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`306/306；相同参数依次对Infrastructure与Windows测试项目执行695/695、456/456，均退出0。`node --test tests/*.test.mjs`最终173/173、退出0；新增持续同步规则回归初次发现交接遗漏“无需重复确认”，补齐后全量通过。`python -m unittest discover -s tests -p release_packaging_test.py`4/4；`.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py`25/25及同Python的 `-m pip check`通过。

App csproj用 `--no-restore --configuration Release --self-contained true --output .tools/published/github-release-0.1.0-beta.2`完成publish，退出0。`python tools/build_release.py --publish-directory .tools/published/github-release-0.1.0-beta.2 --napcat-archive .tools/napcat/framework/v4.18.37/NapCat.Framework.zip --output-directory .tools/releases --version 0.1.0-beta.2`生成`.tools/releases/Shiyao-0.1.0-beta.2-win-x64.zip`，119916177字节、518条目。`python .tools/releases/beta2-validation/verify-archive.py`退出0，ZIP完整性、所有程序文件与publish逐字节一致、官方NapCat原包一致、个人数据文件未包含；首次临时检查脚本误用不存在的styles.css文件名，改为核对实际全部发布文件后通过，软件和打包器不改。

解压到`.tools/releases/验包 中文空格 0.1.0-beta.2/Shiyao/`后，`python tests/app_desktop_test.py --executable .tools/releases/验包 中文空格 0.1.0-beta.2/Shiyao/app/MessagePurify.exe`退出0，115项Native与真实父窗/styles检查通过。`python .tools/releases/verify-package-beta2.py`退出0，用合成QQ路径/账号完成CheckOnly与Setup、原包解压、本机接口和桥核对；没有启动QQ或安装真实快捷方式。日志与报告位于`.tools/releases/beta2-validation/`及`.tools/releases/package-validation-beta2.json`；发布说明为`.tools/releases/release-notes-beta2.md`。其他电脑及QQ版本的试用仍待反馈。

实际远端发布：文档验证`node --test tests/project-rules.test.mjs`49/49及`git diff --check`退出0后创建提交`2657e25bec986b143b688dab3e339e33027fabf6`；`git -c http.https://github.com.proxy= push origin HEAD:main`退出0，远端main核对为同一SHA。以完整SHA作为target，执行`gh release create v0.1.0-beta.2 --repo Steven060206/shiyao --draft --prerelease --target 2657e25bec986b143b688dab3e339e33027fabf6 --title '拾要 0.1.0-beta.2 · Windows 测试版' --notes-file .tools/releases/release-notes-beta2.md`、`gh release upload v0.1.0-beta.2 .tools/releases/Shiyao-0.1.0-beta.2-win-x64.zip --repo Steven060206/shiyao`，均退出0。草稿未生成tag时按tag读取API曾返回404，随后按Release ID412687085读取并核对附件uploaded、119916177字节及target一致，再执行`gh release edit v0.1.0-beta.2 --repo Steven060206/shiyao --draft=false`，退出0。

发布地址https://github.com/Steven060206/shiyao/releases/tag/v0.1.0-beta.2；`gh api repos/Steven060206/shiyao/releases/tags/v0.1.0-beta.2`核对draft=false、prerelease=true、附件uploaded、大小与本地一致。`curl.exe --noproxy '*' --head --location --silent --show-error --output NUL --write-out '%{http_code}' https://github.com/Steven060206/shiyao/releases/download/v0.1.0-beta.2/Shiyao-0.1.0-beta.2-win-x64.zip`退出0，匿名下载最终HTTP200。旧beta.1保留。发布说明包含旧版托盘退出后覆盖原目录的升级方式，保留个人napcat和app/qq-sync.json配置，事项存储沿用本机用户目录。

maintain-context-hygiene在两处技能目录仍无SKILL.md，按项目规则人工核对新需求、交接、实际源码、测试结果、下载包和远端状态。发布结果同步需求与交接，并新增最新版本/构建示例一致性检查；`node --test tests/project-rules.test.mjs`50/50、最终`node --test tests/*.test.mjs`174/174，均退出0，日志docs-published-final.log及node-published-final.log。`git diff --check`退出0；通过后创建发布结果记录commit并推main，标识以Git日志为准。下一步供同学下载试用；后续软件改动交付时继续同步源码与下载包，其他电脑及QQ版本试用、实际游戏、多屏/恢复和长期稳定性保留既有验收范围。

### 当前接续（一条原消息一项审核，2026-10-15，已实现、部署并发布beta.3）

正式入口已切换到最新版 `.tools/published/app-card-controls/`，498个正式文件与已验证干净发布包相同，保留个人配置，不从含界面改版修改的工作区重新构建。用户明确先前退出不是手动操作；工具Start-Process启动的PID48588及14764已消失，未捕获直接退出码，无Application1000/1026崩溃事件。当前由已有Explorer桌面COM以`--background`独立启动PID23888，GetPackageFullName返回NO_PACKAGE，直接核对当前工具Job的PID列表确认应用不在工具Job内；当前仍正常响应。已确认工具Job启用KILL_ON_JOB_CLOSE，退出根因尚不能完全证实。桌面/开始菜单与Run入口及开关沿用，MessagePurify.Shiyao、黑色标题栏与约2 DIP吸附间距保留。

数据差异根因为Codex MSIX AppData虚拟化，两套profile分别保护、未合并。先前82条审核/167条操作/pending0及before-reopen快照仅属于包虚拟profile，不再作为用户真实数据结论。无包身份Python核对真实物理profile：99条审核、84条已处理记录、1条原任务与187条其他历史操作保留；14条原自动Pending经2笔合并审计整理为2个主项、保留12个子项，另1条Pending保留，整理后pending=3，当时提醒卡隐藏、待确认卡显示。完整原文、原路由、版本与全部最新after快照通过审计，历史操作序号1..189连续；真实保护快照在迁移后建立。随后新增accept-review1、complete1、ignore2共4笔正常操作，账号设置仅ReadVersions变化，原已处理与合并子项、原任务和189笔旧操作原样；当前87条已处理审核、2条任务、193笔操作，审计一致且序号连续。最终四库integrity=ok、14表及4份设置与使用后快照原样，498个正式文件匹配。当前activeTasks=0、pending=0，两卡均隐藏；两卡均为SHELLDLL_DefView子窗、child/noActivate=true、topmost=false。目标PID托盘打开主窗口成功，QQ进程未变、3001/6099无监听，未代审核或触发真实同步。证据为`.tools/message-review-validation/physical-audit-validation.json`、`physical-audit-after-use.json`、`physical-delivery-after-use.json`、`after-use-preservation.json`、`tray-physical-final.json`、`final-process-isolation.json`及独立保护目录；原快照对照失败的physical-delivery.json和旧delivery.json等保留，最新正常操作不回滚。

main源码与v0.1.0-beta.3下载包已公开，target=b9d109f、附件119924077字节、uploaded/draft=false/prerelease=true、匿名HTTP200，见上方GitHub同步段及 `.tools/message-review-validation/github-release.json`。maintain-context-hygiene技能文件仍不可用，已人工核对需求、交接、已提交源码、正式产物和真实数据报告；未修改无关文档或个人数据。交付收尾实际执行 `node --test tests/*.test.mjs` 175/175、退出0，日志node-delivery-final.log；已补充README整条审核/保留人工结果的回归断言。`git diff --check -- README.md docs/HANDOFF.md docs/requirements.md tests/message-review-requirements.test.mjs` 退出0；全工作区检查仅报告另一个任务原有app.css末尾空行，未修改该文件。本次仅提交上述四个交付文件并推main，提交标识以Git日志为准。本次启动排查新增真实物理数据及退出证据范围回归，`node --test tests/project-rules.test.mjs tests/message-review-requirements.test.mjs`52/52通过；`git diff --check -- docs/HANDOFF.md docs/requirements.md tests/message-review-requirements.test.mjs`退出0。16:45:31的process-state-final.json确认PID23888从16:34:38起持续运行、Responding=true、无包身份且不在当前工具Job内；使用后物理核对也针对同一PID通过。此轮仅提交排查记录和相关回归并推main，beta.3附件未变，实际提交标识以Git日志为准。下一步正常使用整条审核，需要独立管理时先确认任务再手动拆分；其他电脑、实际游戏、多屏/恢复、真实Windows登录自启动与长期稳定性继续按既有范围验收。

### 上一段（无待办时隐藏提醒事项卡片，2026-10-14，已实现、部署并实机核对）

用户截图要求无待办时不显示“提醒事项”桌面卡片。需求与实现已同步，替代此前空卡片收紧并显示暂无待办的规则：当前账号active任务为0时隐藏，有待办时自动显示，保留位置、尺寸、主题和透明度；待确认仍按自身未处理项决定显示。DesktopCardHost.Render原来只隐藏空待确认，现对两种卡片均按条目数量决定显示；构造窗口时先创建原生句柄和设置位置，首次有内容后再Show，空账号或桌面恢复重建不短暂显示空卡片。产物为src/MessagePurify.Windows/App/DesktopCardHost.cs、SmokeCheck.DesktopAppearance.cs、README.md及tests/app_desktop_test.py。

Native新增/替换回归核对完成最后一项后WPF和原生窗口均隐藏、待确认独立显示、无待办时关闭并重建卡片仍隐藏、新待办自动恢复显示和原尺寸、隐藏提醒卡片不参与吸附；四方向可见卡片间距与布局重读仍通过。独立桌面脚本按activeTaskCount核对提醒卡片可见性。实际执行 `.tools/dotnet/dotnet.exe test` 分别针对Core、Infrastructure、Windows项目，参数均为 `--no-restore --configuration Release`，306/306、695/695、456/456、退出0；`node --test tests/*.test.mjs`172/172、退出0；`.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py`25/25、同Python的 `-m pip check`通过；`python -m unittest discover -s tests -p release_packaging_test.py`4/4、退出0。App csproj以 `--no-restore --configuration Release --self-contained true` 发布 `.tools/published/app-empty-task-card-check/` 成功，`python tests/app_desktop_test.py --executable .tools/published/app-empty-task-card-check/MessagePurify.exe`最终退出0、115项Native与真实父窗/styles通过。初次Native失败来自替换紧凑空卡片测试后仍向屏幕下方拖64 DIP的测试坐标：请求Y981.25、高450超出工作区1380，正确被夹到Y930；记录诊断后改为在工作区内水平拖64 DIP，最终通过，软件的边界处理不变。

SQLite Backup API保护四库及设置于 `.tools/empty-task-card-validation/before-reopen/`，integrity均ok。`powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .tools/empty-task-card-validation/exit-dpi-aware.ps1 -ProcessId 42596 -ExitApplication`退出0、ExitVerified=true；确认旧进程退出后正式publish至 `.tools/published/app-card-controls/`退出0，以 `--background`正常重开PID47420。正式入口已切换到最新版，桌面/开始菜单和Run路径与开关沿用；MessagePurify.Shiyao、黑色标题栏及约2 DIP吸附间距保留。实际执行同目录check-real-tray.ps1 `-ProcessId 47420 -OpenApplication`退出0，主窗口可用、QQ进程未变、3001/6099无监听。`python .tools/empty-task-card-validation/verify-data-cards.py`及 `python .tools/empty-task-card-validation/verify-delivery.py`退出0，实机提醒与待确认窗口均visible=false（当前均无未处理项）；窗口仍附着SHELLDLL_DefView、child/noActivate=true、topmost=false。四库integrity=ok，tasks库所有表含1条任务、82条review、167条operations与快照完全一致，布局/界面偏好字节不变；正式exe、三个DLL及三个页面文件SHA256与隔离已验证产物一致。没有代处理真实事项或重新QQ同步。日志、native-failure.json诊断及delivery.json/real-data-cards.json均在 `.tools/empty-task-card-validation/`。

maintain-context-hygiene两处无SKILL.md的已核对结论沿用，已人工核对需求、交接、实际源码、测试日志、正式产物和真实数据。交付前同步两份文档，实际执行 `node --test tests/*.test.mjs`172/172、退出0，日志 `.tools/empty-task-card-validation/node-delivery-final.log`；`git diff --check`退出0。通过后创建对应Git commit，标识以日志为准。下一步正常使用，有新待办自动显示卡片；真实Windows登录自启动仍未验收，实际游戏、多屏/恢复、其他电脑和长期稳定性保持既有验收范围。

#### 当前视觉方向（2026-10-14，方向已确认，设计待审批）

用户确认采用“安静的个人助理风格”。当前设计结论：以米白纸张、墨绿和少量暖金建立“温润资料室”气质；主界面、深色模式与桌面卡片共享品牌色和文字层级；减少全局线框、同质圆角和小胶囊标签，突出标题、时间、来源和主要动作；导航可按处理/记录/偏好分组，但不改变功能入口。深色仍遵守主界面深色底约束，使用暖黑与深墨绿层次；桌面卡片保留半透明、圆角和桌面驻留能力，但采用同一套颜色与层级。

本段只记录已确认的视觉方向，尚未表示设计稿或代码已实现。下一步先在聊天中确认具体改版设计，再写设计规格和实现计划；在用户审批前不修改界面代码。 已先保存实施方案：`docs/superpowers/plans/2026-10-14-calm-personal-assistant-visual-redesign-v2.md`（替代 v1）；用户已明确要求按 v2 实施。Task 0 基线已记录到 `.tools/visual-redesign-validation/baseline.json`，页面行为基线 `tests/application-ui.test.mjs` 为 88/88；当前进入 Task 1。

### 上一段接续（回收站入口，2026-10-14，已实现、部署并实机核对）

用户要求把“已忽略”单独设为“回收站”，在左侧导航增加入口，功能保持一致。需求与实现已同步：左侧新增回收站，待确认包括“全部状态”不再混入已忽略；回收站继续查看原文、搜索/课程筛选、最新消息倒序及原24小时撤销期限。复用现有ignored状态与undo-ignore，期限过后仍保留原记录。入口来自桌面打开已忽略项时也定位回收站；空列表显示回收站为空。批量忽略的提示已改为回收站入口，批量审核仍只在待确认页面。变更产物为src/MessagePurify.Windows/App/ui/app.js、ui/index.html、SmokeCheck.BatchReview.cs和tests/application-ui.test.mjs，不改数据格式。

新增浏览器回归覆盖独立入口、状态分离、搜索/课程筛选、倒序、原文、未过期撤销/过期保留、恢复待确认及空状态；Native批量验证通过正式WebView2进入回收站撤销合成条目。实际执行 `.tools/dotnet/dotnet.exe test` 分别针对Core、Infrastructure、Windows测试项目，均带 `--no-restore --configuration Release`，结果306/306、695/695、456/456、退出0；`node --test tests/application-ui.test.mjs`88/88、`node --test tests/*.test.mjs`172/172、退出0。`.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py`25/25，同Python的 `-m pip check`通过；`python -m unittest discover -s tests -p release_packaging_test.py`4/4、退出0。App csproj以 `--no-restore --configuration Release --self-contained true` 发布隔离 `.tools/published/app-trash-page-check/` 成功；`python tests/app_desktop_test.py --executable .tools/published/app-trash-page-check/MessagePurify.exe`退出0、113项Native与真实父窗/styles检查通过。`.tools/desktop-validation/ui-trash-dark.png`已查看，导航、原文与撤销正常；Impeccable detect仅有沿用浅色配色的4.47:1对比度提示，本轮没有改动配色。

SQLite Backup API保护四库和设置于 `.tools/trash-page-validation/before-reopen/`。初次旧托盘退出脚本未找到菜单，未确认退出即发布，导致旧PID36728锁定DLL、publish失败；随后查明验证脚本DPI坐标与屏幕缩放不一致。仅在临时验证脚本启用线程DPI感知并保留UTF-8 BOM，`powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .tools/trash-page-validation/exit-dpi-aware.ps1 -ProcessId 36728 -ExitApplication -Verbose`退出0，Success/ExitVerified=true，经托盘正常退出。确认没有拾要进程后重新publish至正式 `.tools/published/app-card-controls/`退出0（publish-formal-final.log），并以 `--background`重开PID42596；没有强制终止应用。正式入口已切换到最新版，桌面/开始菜单与Run路径和开关沿用，MessagePurify.Shiyao、黑色标题栏及约2 DIP间距保留。

实际执行同目录check-real-tray.ps1（启用同一DPI感知）`-ProcessId 42596 -OpenApplication`退出0，QQ进程ID/启动时刻不变、3001/6099无监听；UIAutomation只点击真实左侧回收站并核对页面说明可见，没有撤销或修改真实条目。`python .tools/trash-page-validation/verify-data-cards.py`与`python .tools/trash-page-validation/verify-delivery.py`均退出0：四库integrity=ok，tasks库所有表（含82条reviews、167条operations）与保护快照一致，已忽略与已确认记录保留；布局及界面偏好字节不变，正式exe、三个DLL与三个页面文件的SHA256均匹配隔离已验证产物。两卡片附着SHELLDLL_DefView、child/noActivate=true、topmost=false，提醒卡片可见，空待确认卡片隐藏。正式界面停留回收站；日志与delivery.json、real-data-cards.json、trash-real-ui.json均在 `.tools/trash-page-validation/`。

maintain-context-hygiene两处仍只有agents目录、无SKILL.md，按项目规则人工核对有效需求、交接正文、源文件、日志、正式产物及真实数据。本轮替代原待确认中的“已忽略”筛选入口，忽略持久性、确认保留和24小时撤销规则继续有效。文档同步后实际执行 `node --test tests/*.test.mjs`172/172、退出0，日志 `.tools/trash-page-validation/node-delivery-final.log`；`git diff --check`退出0。通过后创建对应Git commit，标识以Git日志为准。下一步正常使用左侧回收站；真实Windows登录自启动仍未验收，实际游戏、多屏/恢复、其他电脑和长期稳定性验收继续沿用既有范围。

### 本轮消息排序（2026-10-14，已实现并部署）

用户要求所有消息按时间倒序、最新在前。有效需求已同步：主窗口任务、待确认、通知、已完成、回收站（已忽略）与两张桌面卡片统一按原消息最新发送时间倒序；手动新增按创建时间，无可用时间放最后，同一时间按稳定 ID 排列。替代优先级/截止日期/标题排序；优先级、正式日期及临期高亮保留。AccountWorkspace 根据证据最新 SentAt 产生 MessageTime，手动任务回退 CreatedAt；不修改数据库格式。正式页面与 DesktopCardProjection 使用该时间，新关联消息更新排序，普通人工编辑或重复同步不伪造新消息时间。

新增工作区时间投影回归、桌面三种条目排序回归，以及浏览器各列表/已忽略筛选/刷新排序回归，覆盖时区、同时间稳定ID、多证据、无效时间和高优先级旧消息。实际执行 `.tools/dotnet/dotnet.exe test` 分别针对 Core、Infrastructure、Windows 测试项目，参数均为 `--no-restore --configuration Release`，最终306/306、695/695、456/456、退出0；Infrastructure时间投影专项1/1。`node --test tests/application-ui.test.mjs`87/87、`node --test tests/*.test.mjs`171/171，均退出0。`.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py`25/25、同Python的`-m pip check`通过；`python -m unittest discover -s tests -p release_packaging_test.py`4/4、退出0。Impeccable context与detect仅检查既有列表行为，detect返回空列表；排序页面截图 `.tools/desktop-validation/ui-message-time-descending-dark.png` 已查看。初期测试失败来自新增用例错误插入初始加载流程、重复嵌套导致父测试超时，以及手动创建夹具缺课程；修正测试隔离与夹具后通过。变更已读测试显式设置另一事项为最新，保留原“未打开事项不标已读”断言。

App csproj以`--no-restore --configuration Release --self-contained true`分别publish到隔离`.tools/published/app-message-time-sort-check/`和正式`.tools/published/app-card-controls/`，两次退出0。`python tests/app_desktop_test.py --executable .tools/published/app-message-time-sort-check/MessagePurify.exe`退出0、113项Native与真实父窗/styles通过。SQLite Backup API保护四库与设置到 `.tools/message-time-sort-validation/before-reopen/`、integrity均ok；旧PID38220经正式托盘脚本正常退出，正式发布后以`--background`启动PID53472。真实托盘菜单打开成功，普通QQ进程不变、Framework端口关闭；正式exe、应用DLL、Infrastructure DLL及app.js与隔离已验证产物逐字节相同。只读核对tasks库所有表（含reviews与操作历史）与快照一致，布局/界面偏好字节一致，四库integrity均ok。当前81项已忽略、1项已接受、0项待确认，提醒卡片可见，待确认卡片隐藏；两卡片均附着SHELLDLL_DefView、child/noActivate=true、topmost=false。旧验证脚本误要求两卡均可见，已依据实际空待确认状态修正验证条件，不改软件隐藏规则。日志、数据保护脚本及delivery.json/real-data-cards.json位于`.tools/message-time-sort-validation/`。

maintain-context-hygiene两处已核对目录仍无SKILL.md，按项目规则人工核对需求、交接、源码、正式产物和真实数据。最终文档同步后`node --test tests/project-rules.test.mjs`48/48、`git diff --check`退出0，通过后创建对应Git commit。下一步正常使用倒序列表，既有跨电脑试用和其他实机验收范围继续沿用后文。提交标识以Git日志为准。

### 本轮补充：启动同步保留忽略与确认结果（2026-10-14，已核对并通过回归）

用户明确要求再次打开软件同步时跳过已忽略的原消息，保留已确认消息。需求已同步，启动触发范围继续沿用桌面快捷方式与“自动同步”，托盘只打开窗口。代码核对：AccountWorkspace.EnqueueCore 先检查持久化路由回执，已处理消息不重新入队；消息键为账号、群、消息 ID，忽略和接受结果持久化于 tasks.sqlite3，忽略的 24 小时仅限制撤销。现有实现满足该规则，本轮产物为测试和文档，运行版继续使用现有机制。

新增回归位于 tests/MessagePurify.Infrastructure.Tests/ApplicationMessageHostTests.cs（三种状态）和 AccountWorkspaceTests.cs（一项撤销过期）：真实本机合成 OneBot WebSocket 首次按次同步两条消息，忽略一条、接受并手动修改另一条，重建工作区后连续同步两轮。已确认任务的待办/已完成/已归档状态、完整记录、人工标题/课程/日期/优先级、证据和历史保持一致，原忽略与接受记录不变；第三轮新增消息正常进入待确认。另一项核对忽略两天后重新同步仍跳过原消息，已到期的撤销时间和忽略记录保留。以上使用隔离合成账号，没有真实 QQ 同步操作。

实际验证：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release --filter FullyQualifiedName~ReopenedRunOnceSkipsIgnoredMessagesAndPreservesAcceptedTasks` 3/3；相同 test 命令移除 filter 后 Infrastructure694/694，分别针对 Core 和 Windows 测试项目执行结果306/306、454/454，全部退出0。`node --test tests/*.test.mjs`170/170、`python -m unittest discover -s tests -p release_packaging_test.py`4/4、`.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py`25/25 均退出0；同 Python 的 `-m pip check` 返回 No broken requirements found。首轮 Infrastructure693通过/1失败，原因是新增测试误以为过期会清空撤销操作ID；核对工作区投影及生命周期后，改为断言原期限过期且完整记录不变，最终全量694/694。同步最终文档后，`node --test tests/project-rules.test.mjs`48/48、`git diff --check`退出0；通过后创建对应 Git commit，提交标识以日志为准。

交接核对：maintain-context-hygiene 在 C:/Users/Administrator/.codex/skills 与 D:/CodexSkills/codex/skills 两处目录仍只有 agents，没有 SKILL.md；已按项目规则人工核对有效需求、交接正文和实际源码/测试。既有真实部署与其他电脑试用状态沿用下文记录。下一步正常打开拾要即可使用；若出现旧消息重新进入待确认，核对其账号、群与原消息ID以定位具体来源。

### 同学分发与 GitHub 发布准备（2026-10-14，首个测试版已发布）

用户要求提供可供其他同学使用的软件版本，并由助手上传到用户的 GitHub，供其他人下载。2026-10-14 用户选择 A：公开源码，同时提供软件下载，并明确授权助手直接上传。首个公开测试版已发布；M3 阶段与既有本机部署保持上述真实状态，分发完成不代表剩余产品验收完成。

此前初始只读核对：`git status --short` 无改动，基线279a7a5；当时`git remote -v` 无远端；`gh auth status` 退出0，账号Steven060206。App csproj为win-x64、WPF + WebView2；旧QqSyncSession默认固定`D:/QQ/QQ.exe`，NapCat与置顶桥脚本依赖项目目录；旧AppRuntime按项目布局读取NapCat配置。本轮用下方便携配置适配分发，现有本机`.tools/published/app-card-controls/`及入口保留。

QQ路径与首次配置已写入：便携目录为app、napcat/package和bridge，Setup选择本机QQ并创建个人本机OneBot配置，未Setup的包不回退开发者配置。新增README、MIT及第三方完整许可、原始NapCat包打包工具和相关测试。分发包和公开仓库不得包含用户的QQ聊天数据、账号数据库、Token或登录材料，同学使用各自账号与本地数据。公开仓库https://github.com/Steven060206/shiyao的main分支与首次Release已上传，上传授权已确认。

本轮实际已执行Core Release306/306、Infrastructure Release690/690、Windows Release454/454，均使用`--no-restore --configuration Release`退出0；App csproj自包含Release publish到`.tools/published/github-release-0.1.0-beta.1/`退出0。`python tests/app_desktop_test.py --executable .tools/published/github-release-0.1.0-beta.1/MessagePurify.exe`退出0、113项Native和真实父窗/styles通过。Python release_packaging_test四项含子场景已通过。首次配置Node初跑7/9：修复WindowsPowerShell5.1把File.Replace的$null绑定为空路径导致重复配置失败，测试另将PublicKeyToken程序集类型误判为凭据；首次配置最终9/9、全量结果见下方。保持现有本机运行、快捷方式及个人数据，未发起真实QQ同步。

最终分发验证：`node --test tests/*.test.mjs`170/170、退出0；`python -m unittest discover -s tests -p release_packaging_test.py`4/4（含污染包和不安全ZIP子场景）、退出0。`python tools/build_release.py --publish-directory .tools/published/github-release-0.1.0-beta.1 --napcat-archive .tools/napcat/framework/v4.18.37/NapCat.Framework.zip --output-directory .tools/releases --version 0.1.0-beta.1`退出0，生成`.tools/releases/Shiyao-0.1.0-beta.1-win-x64.zip`，119915264字节、518个条目；ZIP完整性通过，内部DLL与最终publish一致。解压到`.tools/releases/验包 中文空格 0.1.0-beta.1/Shiyao/`后再次执行`python tests/app_desktop_test.py --executable .tools/releases/验包 中文空格 0.1.0-beta.1/Shiyao/app/MessagePurify.exe`，113项及真实父窗/styles通过、退出0。`python .tools/releases/verify-package.py`在该解压目录用合成QQ路径/账号执行原始NapCat包的CheckOnly与实际Setup，通过本机接口、桥和不输出凭据核对；未运行QQ或安装真实快捷方式，报告`.tools/releases/package-validation.json`。源码历史扫描没有私人材料命中，完整许可保留。上述为本机独立路径/合成配置验证，其他同学的电脑与QQ版本仍需实际试用。

远端实际发布：产品提交`c221e66`已推到origin/main；仓库public、默认分支main。Git原有GitHub专属代理127.0.0.1:10808无监听，通用proxy覆盖无效，使用按次`git -c http.https://github.com.proxy= push --set-upstream origin HEAD:main`成功，不修改全局设置。Release创建首次短SHA被API拒绝，改用完整提交SHA后draft创建成功；`gh release upload v0.1.0-beta.1 .tools/releases/Shiyao-0.1.0-beta.1-win-x64.zip --repo Steven060206/shiyao`退出0，资产state=uploaded且大小与本地一致。`gh release edit v0.1.0-beta.1 --repo Steven060206/shiyao --draft=false`退出0；`gh release view ... --json tagName,isDraft,isPrerelease,url,assets,publishedAt`核对tag=v0.1.0-beta.1、draft=false、prerelease=true、119915264字节。发布时间2026-10-14 12:41（北京时间），Release https://github.com/Steven060206/shiyao/releases/tag/v0.1.0-beta.1 ，下载 https://github.com/Steven060206/shiyao/releases/download/v0.1.0-beta.1/Shiyao-0.1.0-beta.1-win-x64.zip 。下一步让同学下载、完整解压、Setup后使用自己的QQ，收集其他电脑/版本的反馈；本机剩余验收范围保持总览。

发布后匿名下载核对：`curl.exe --noproxy '*' --head --location`指向正式ZIP下载地址，最终HTTP200，未下载完整文件。发布状态同步后再次`node --test tests/*.test.mjs`170/170、退出0；`git diff --check`通过。

上一轮准备记录（首次询问发布方式时）：仅同步`docs/requirements.md`、本文与`tests/project-rules.test.mjs`的分发状态检查；当时实际运行`node --test tests/*.test.mjs`，161/161通过、退出0；`git diff --check`退出0。该轮验证范围为文档与既有Node回归，不代表分发包或跨电脑验收。`maintain-context-hygiene`两处目录均只有agents子目录，未找到SKILL.md，本轮继续按项目规则人工核对需求、交接、实际源码及发布状态，不删除文件。上一轮提交为5caaf75，本轮产品提交c221e66；最终发布记录同步提交标识以Git日志为准。

### 本轮修复前 QQ 连接诊断（2026-10-14，历史）

修复前实机核对正式实例 PID 22140：QQ.exe、NapCat/Framework 进程均不存在，127.0.0.1:3001 无监听，拾要进程对该端口周期性处于 SYN_SENT。旧版应用固定连接 `ws://127.0.0.1:3001/`，不会自动启动 QQ/NapCat；外层连接失败后每 5 秒重试，桌面卡片将所有非 connected 状态统一显示为“QQ 连接断开，正在重连”。NapCat 配置中唯一启用的本机 WebSocket 服务为 127.0.0.1:3001，未发现端口配置错位。当时直接根因是 QQ/NapCat Framework 未运行。该诊断阶段未启动或改动 QQ；随后已用下方按次启动、同步后恢复普通 QQ 的方案替代常驻连接，旧版手动启动并保持 Framework 常驻的恢复步骤不再作为日常使用方式。

### 新接入需求（2026-10-14，已实现、部署并实机核对）

用户确认启动拾要自动同步一次：每次点击拾要时自动启动一轮同步（指从桌面快捷方式启动）；从系统托盘打开只显示窗口，不同步，也不切换 QQ；拾要已打开时，点击“自动同步”再同步一次；同步完成后结束项目专用 NapCat Framework，恢复原本的普通 QQ。普通 QQ 已运行时允许自动退出，再在同步后重新打开普通 QQ。取消每天 08:00、12:00、20:00 定时同步，只在点击拾要或“自动同步”时同步；登录 Windows 的后台自启动只恢复本地界面和卡片，不切换 QQ。同步失败、超时或退出应用时也尝试恢复普通 QQ；游戏期间禁止同步，游戏导致错过同步时，结束游戏后补同步一次，重复点击合并为一轮。代码已写入，完整全量、Native及真实切换验证通过，已部署；详见下方当前接续。

### 上一段接续（待确认批量审核，2026-10-14，已实现、部署并核对真实控件）

基线8263aa1，用户要求多选批量忽略和加入任务。已在正式页面加入勾选、全选当前筛选结果、批量忽略与加入任务；复用现有ignore/accept-review逐条版本化命令与24小时忽略撤销，不新增后端接口。加入任务提供统一日期（清空钟点，保留日期精度）和显式采用建议日期，空课程可显式确认；缺信息、关联变更、动态可能重复建议不自动处理。每条使用独立操作ID及勾选时版本，部分失败继续，其余保留原因；超时停止队列、保留在途锁并显示结果未确认，迟到回执核对账号与工作区后更新结果。不代处理真实事项。正式入口继续.tools/published/app-card-controls/，新版PID20352以--background运行，MessagePurify.Shiyao独立身份与约2 DIP间距保留，托盘打开不同步。

验证：新增页面六项与Native三项合成检查，涵盖筛选/撤销、显式日期确认、新任务ID、日期精度、独立凭据、原文证据、重开、重复点击、部分失败、超时和账号/恢复边界。最初缺按钮的页面回归失败；一轮只读复核确认旧账号完整迟到快照会回切账号、超时回执会被丢弃，均新增复现并修复。node --test tests/application-ui.test.mjs最终86/86；node --test tests/*.test.mjs全量160/160。分别执行.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests、tests/MessagePurify.Infrastructure.Tests、tests/MessagePurify.Windows.Tests，均带--no-restore --configuration Release，结果306/306、690/690、446/446，退出0。App csproj以--no-restore --configuration Release --self-contained true发布到隔离app-bulk-review-check及正式app-card-controls，两次最终发布退出0；python tests/app_desktop_test.py --executable .tools/published/app-bulk-review-check/MessagePurify.exe退出0，113项Native与真实父窗/styles通过（新增batch-ignore-undo、batch-accept-task、batch-reopen-evidence）。页面截图.tools/desktop-validation/ui-batch-review-dark.png已实际查看，无裁切。

部署与真实核对：python .tools/bulk-review-validation/protect-data.py通过SQLite Backup API保护四库及设置于before-reopen/，四库integrity=ok。powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/app_tray_test.ps1 -ProcessId 26536 -ExitApplication退出0、Success/ExitVerified=true；同目录发布后以--background正常启动PID20352。正式exe、DLL及ui/app.js、ui/app.css、ui/index.html的SHA256均与隔离验证产物相同。复制前轮check-real-tray.ps1后实际执行-ProcessId 20352 -OpenApplication，菜单打开主窗口通过，QQ进程ID/启动时刻未变、3001/6099无监听。UIAutomation只读核对全选、批量忽略、批量加入任务与建议日期控件均可见；两个批量按钮在未勾选时禁用，未执行任何真实批量操作。python .tools/bulk-review-validation/verify-data-cards.py退出0，原tasks完整记录、布局及界面偏好字节未变，四库integrity=ok，两张卡片visible/child/noActivate=true、topmost=false、父窗SHELLDLL_DefView。相关安全状态报告、命令日志、保护脚本与快照均位于.tools/bulk-review-validation/。未重新发起QQ同步；既有桌面启动与按钮按次切换证据保留，实际游戏、真实Windows登录、多屏/恢复和长期稳定性仍未验收。

变更记录：用户新增批量审核需求，替代只能逐条操作的页面；同步触发范围沿用8263aa1，不把托盘打开加入同步。maintain-context-hygiene技能未找到的既有结论保持，已人工核对需求、源码、验证日志、正式产物和真实数据。交付前更新两份文档后，node --test tests/*.test.mjs最终160/160、退出0，日志.tools/bulk-review-validation/node-delivery-final.log；git diff --check退出0。提交说明feat: add bulk pending review actions，标识以Git日志为准。下一步由用户使用批量入口；剩余实机验收范围保持上述记录。

### 上一段（按次自动同步并恢复普通 QQ，2026-10-14，已实现、部署并实机核对）

补充澄清（2026-10-14，已修正、部署并实机核对）：用户说明“打开”是开机后从桌面快捷方式启动拾要，不包括系统托盘打开。桌面启动（含唤回后台实例）与“自动同步”仍触发一轮；托盘双击和菜单“打开拾要”只显示窗口，不同步，也不切换 QQ。基线3770ee9误将托盘也接入同步，根因是两个托盘事件共用OpenAndSync回调；本轮均改为ShowMainWindow。

本轮托盘纠正验证：新增Native检查实际调用NotifyIcon的菜单点击与双击事件，分别断言主窗口可见且SyncPhase仍idle。旧回调在新检查上真实失败（--smoke-test退出1），修正后完整python tests/app_desktop_test.py --executable .tools/published/app-tray-no-sync-check/MessagePurify.exe退出0、110项Native及真实父窗/styles通过。Windows Release全量446/446、node --test tests/*.test.mjs全量153/153，均退出0；两次Release自包含publish退出0，正式exe及DLL与已验证隔离产物逐字节相同。

本轮部署：SQLite Backup API保护四库与设置于.tools/tray-no-sync-validation/before-reopen/（integrity均ok），PID43240经正式托盘退出脚本正常退出；同目录app-card-controls发布后以--background启动PID26536，不发起同步。实际托盘菜单“打开拾要”经UIAutomation点击通过，主窗口显示、QQ进程ID/启动时刻未变、3001/6099无监听；原tasks完整记录和布局/界面偏好字节未变，四库integrity=ok，两卡片仍附着SHELLDLL_DefView、child/noActivate=true、topmost=false。报告native-before-fix.json、native.log、published-artifact-match.json、real-tray-open.json、real-data-cards.json及windows/node/publish日志均位于.tools/tray-no-sync-validation/。本轮未重复真实同步，桌面启动及按钮切换的此前实机证据保留；实际游戏、真实Windows登录和长期稳定性仍未验收。maintain-context-hygiene技能未找到的既有结论保持，已人工核对本轮需求、代码、日志、发布产物和真实数据。

接续基线83bf963，分支codex/m3-integration-probes。用户确认允许自动退出普通 QQ、同步后恢复，取消08:00/12:00/20:00定时同步，仅点击拾要/“自动同步”触发；后台登录自启动不接管 QQ。设计与实现记录docs/superpowers/plans/2026-10-14-qq-on-demand-sync.md。正常同步后主动断开不显示重连；原重连直接根因是 Framework 未运行。

代码：AppRuntime按次消费并合并请求；QqSyncSession先核对配置/依赖和准备项目置顶桥，再退出指定路径QQ、隐藏启动napimain、使用nativeLoaderPinned.cjs，完成后恢复普通QQ。操作进程核对路径/PID/启动时刻和本次父链；QQ仅隐藏时等待3秒后精准退出。NapCatMessageHost.RunOnceAsync只读七天置顶群、完成/partial返回、游戏开始中止在途接口；同步失败、五分钟超时和应用正常退出均尝试恢复QQ，失败后等待点击重试。保留游戏取消原因，避免游戏刚退出误判超时而丢补同步请求。正式页桥新增空参数sync-now，按钮忙时禁用，刷新仍仅读本地；桌面快捷方式启动、单实例唤回触发一轮；托盘双击和菜单只显示窗口，不同步，查看具体事项也不额外触发。--read-only-check仅读取现有接口，不操作QQ进程。

实际验证：.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release：306/306；同命令Infrastructure690/690、Windows446/446；node --test tests/*.test.mjs：153/153（页面80、文档46），均退出0。两次App csproj自包含Release publish（--no-restore --configuration Release --self-contained true）到隔离app-sync-session-check和正式app-card-controls，退出0。python tests/app_desktop_test.py --executable .tools/published/app-sync-session-check/MessagePurify.exe及正式目录相同命令均退出0，108项Native、两卡片真实父窗/styles通过；新增按钮/桥/正常空闲三项。页面截图已打开核对，按钮无裁切。

部署：旧PID22140经powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/app_tray_test.ps1 -ProcessId 22140 -ExitApplication正常退出（Success/ExitVerified=true）；SQLite Backup API保护四库和设置于.tools/sync-session-validation/before-reopen/，四库integrity=ok。正式目录继续.tools/published/app-card-controls/，两入口目标/空参数已实读核对，Run开关/路径未迁移；MessagePurify.Shiyao、黑色标题栏、约2 DIP吸附间距及透明度保留。

真实验证：临时SyncFlowCheck.dll（源在.tools/sync-session-validation/）通过正式AppRuntime执行，首次无QQ成功23.61秒，普通QQ已运行时成功24.93秒；均52群/9置顶、history=complete、失败0、1个任务和75个未处理待确认，普通QQ恢复且3001/6099关闭。正式exe正常打开PID43240，UIAutomation实际验证启动同步一次，随后Invoke“自动同步”再同步一次；两次显示“同步完成，已切回 QQ”、按钮恢复可用，普通QQ运行且Framework端口关闭。设置成功时间2026-10-14 09:50（北京时间），无下次计划和固定时刻说明。新报告real-session.json、qq-already-running/real-session.json、real-gui-sync.json仅记录安全状态、计数和时间，不含消息正文/Token。

最终python .tools/sync-session-validation/verify-data-cards.py退出0：原tasks表完整记录、desktop-layout/interface-preferences字节未变，四库integrity=ok，两卡片实际SHELLDLL_DefView、visible/child/noActivate=true、topmost=false；real-data-cards.json与entries-final.json保存证据。未代确认、编辑或完成真实事项，QQ继续只读。实际游戏、真实Windows登录、多屏/Explorer/睡眠恢复、原图/OCR及长期稳定性仍未验收；取消定时后不再验收三个固定时刻。

失败记录：按钮/桥先真实失败后实现通过；文档首轮45/46，已把旧常驻断言同步为新需求，后全量通过。一次旧UTF8无BOM退出脚本被Windows PowerShell5.1错误解析，未点击，改用正式BOM测试脚本正常退出。临时验证工具构建与隔离Native争用DLL，7次复制重试警告后退出0；正式publish顺序执行、最终Native通过。maintain-context-hygiene技能本机两处技能目录仍未找到，已人工核对需求、源码、报告、正式入口、进程与真实数据，不删除文件。本文与相关文档/测试同步通过后创建Git commit，标识以Git日志为准。

交付前文档核对：总览已按按次同步更新，旧 PID22140 的常驻连接诊断改标历史，需求中的标题栏实机记录也明确为历史。首次新增标题复用了旧测试的分段标记，导致文档回归152/153；改用独立标题后，node --test tests/*.test.mjs 最终153/153、退出0，日志.tools/sync-session-validation/node-delivery-final.log；普通git diff --check退出0。新增总览回归检查确保取消定时同步和实机切换状态不再被旧描述覆盖。只读核对五份真实验证报告与正式入口，结论和现有记录一致。

### 上一段（主窗口标题栏主题同步，2026-10-13～14，已修复、部署并实机核对）

接续基线e860411，初始工作区干净，原正式实例PID52336运行于 `.tools/published/app-card-controls/`；已有2 DIP卡片间距与MessagePurify.Shiyao独立任务栏标识保持有效。用户截图指出主窗口顶上的白色标题栏丑；根因是网页深色主题已应用，WPF原生窗口标题栏未同步主题。已新增WindowTheme，使用Windows DWM原生标题栏颜色、文字及边框属性，与初始偏好、页面主题切换及重开同步；system/缺失主题读取Windows应用主题，保留原生窗口操作，旧Windows不支持颜色属性时不阻止启动。AppWindow保留独立窗口身份初始化并接入主题桥。依据微软DWM属性官方说明（https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute），已用agent-reach阅读核对。

最终验证：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release` 419/419；`node --test tests/*.test.mjs` 152/152，退出0。隔离与正式目录自包含Release publish均退出0；隔离版 `--smoke-test --report .tools/titlebar-validation/settle/native-smoke-final.json` 105项通过，最终正式版 `python tests/app_desktop_test.py --executable .tools/published/app-card-controls/MessagePurify.exe` 105项Native及真实父窗/styles检查通过、退出0。新增四项Native检查覆盖浅深标题栏真实像素、重开和原生窗口控制/缩放样式；main-titlebar-dark/light/reopen.png三张已实际查看。深色标题栏黑色、浅色淡灰绿，重开深色保持。正式脚本日志 `desktop-formal-passing.log`，Windows及Node最终日志均位于 `.tools/titlebar-validation/`。

实际部署：四库SQLite Backup API和布局/界面/账号设置先保护于 `before-reopen/`，旧PID52336经托盘正常退出；同目录发布后正常后台启动PID58592。该发布版首次及一次重试在原有编辑冲突Smoke场景超时：源码只等正式存储及历史已读，却未等上一笔编辑回执处理结束，随后注入并发更新可能被迟到成功回执重建草稿。SmokeCheck已补“上一笔edit-task请求处理结束且草稿版本匹配”的等待条件，隔离105项通过；PID58592再次经托盘正常退出，第二份四库/设置保护快照 `before-final-publish/` 保存后正式目录再次发布，最终正常后台实例PID61372。两次托盘退出日志均Success=true、ExitVerified=true；没有强制结束应用，入口路径和自启动开关未迁移。两入口目标/参数/ICO和Run值与前轮备份相同，入口及实际主窗口仍使用MessagePurify.Shiyao。

最终真实核对：当前实例PID57432两卡片parentClass=SHELLDLL_DefView、visible/child/noActivate=true、topmost=false；任务完整记录、布局文件字节及界面偏好与原重开前相同，四库integrity=ok。当前桌面scale=1.5，保存x=1390.4 DIP转换为2086px，完整510px窗口会超出2560px工作区，原有Fit规则将实际x约束为2050px；y/尺寸与保存值按当前缩放一致。先前辅助脚本仅比较原始坐标因此失败，已按当前工作区Fit核对通过，不能写成原始坐标完全一致。真实主窗口已唤回，PrintWindow实际标题栏像素为(0,0,0)，`real-main-titlebar-final.png`已实际查看；`real-titlebar-final.json`、`window-identity-final.json`、`entries-final.json`、`real-desktop-final.json`、`real-data-check-final.json`保存证据，卡片截图 `real-desktop-cards-final.png`。未代确认、编辑或完成真实事项，QQ继续只读。本轮不扩展自然固定时刻、实际游戏、多屏/Explorer或长期稳定性验收，审美反馈待用户提供。

交付末尾追加核对（2026-10-14）：原40秒桌面脚本等待上限在新增105项全流程下偏短，重复检查在报告生成前失败；一次单独诊断也被45秒等待上限终止。正式exe在不提前终止时完整运行60.37秒、退出0、105项通过（`final-timing/native-smoke.json`、`timing.json`），说明这些无报告失败由验证工具超时导致，不是单实例启动问题。已将 `tests/app_desktop_test.py` 等待上限40秒改为90秒，仍保留原生各项断言；同一正式命令最终退出0、105项及真实父窗/styles检查通过，最终日志 `desktop-formal-final-90s.log`。期间PID61372经托盘正常退出（`exit-before-smoke.log`），验证仅结束隔离合成进程；正式版随后正常后台重开PID57432。其主窗口身份MessagePurify.Shiyao和黑色标题栏再次实测，卡片/四库/任务/布局/界面偏好也再次核对通过。产品提交c6b8a20，验证等待修正提交bae11a6；本段同步两份文档并通过相关文档测试后另行提交，标识以Git日志为准。

失败记录保留：首次并行test/publish争用DLL，后续改顺序构建；DWM颜色读取不受支持，改用PrintWindow真实像素验证；一次新报告目录未预建导致截图路径不存在，目录建立后重跑通过。maintain-context-hygiene本机技能目录未找到，已人工核对有效需求、实际源码、发布目录、入口及新报告，不删除文件。提交说明 `fix: match native titlebar to application theme`，标识以Git日志为准。

### 上一段（缩小吸附间距与独立任务栏分组，2026-10-13，已修复、部署并实机核对）

接续基线a689449，初始工作区干净。用户反馈卡片吸附后中间空隙过大；实际源码按窗口边界吸附，面板两侧各5 DIP外边距形成10 DIP可见空隙。四方向目标已靠近8 DIP，使面板留约2 DIP；已更新几何回归，新增100%/125%/200%缩放下四向面板间距测试及真实WPF面板边缘检查。

用户同时反馈拾要任务栏图标归到Codex组。原独立进程PID19968的主窗口图标已设置，但原源码未显式指定AppUserModelID。唤回主窗口时UIAutomation读到“ChatGPT - 2个运行窗口”、Appid为OpenAI.Codex；WM_CLOSE正常隐藏后同组恢复1个窗口，复现身份跟随启动器。先设置进程ID与两入口ID，Native身份检查通过，但首次正式PID61032仍归Codex；读取其窗口属性为空。依据微软STARTF_TITLEISAPPID与窗口属性优先规则，在AppWindow.SourceInitialized直接写主窗口MessagePurify.Shiyao，随后实机分组通过。进程、主窗口及两入口共用该ID，保留拾要ICO；新增Win32身份覆盖/重复初始化、真实窗口属性、快捷方式扩展属性及Native启动身份检查。官方依据：`https://learn.microsoft.com/en-us/windows/win32/shell/appids` 与 `https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-startupinfow`，使用agent-reach网页阅读核对。

最终验证：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release` 415/415，退出0、无警告；`node --test tests/*.test.mjs` 152/152（含8项快捷方式及真实扩展属性），退出0；App自包含Release publish退出0。最终正式版 `python tests/app_desktop_test.py --executable .tools/published/app-card-controls/MessagePurify.exe` 101项Native及真实父窗/styles检查通过，退出0；隔离版 `--smoke-test --report` 的101项新报告也通过。日志、报告与截图位于 `.tools/card-gap-validation/`。几何测试覆盖100%/125%/200%缩放，Native检查四方向实际WPF面板边缘；snap-right.png与snap-vertical-compact.png已实际查看。首次publish的WPF/WinForms Point、Size冲突已用别名修正；首次截图VisualBrush缩放阴影及紧凑卡片，改为按实际屏幕坐标直接渲染窗口，最终截图与面板测量一致。首次正式位置检查未匹配保存值，未标通过；最终重开后再次核对坐标/尺寸通过。

正式目录继续使用 `.tools/published/app-card-controls/`，没有迁移入口路径或改自启动开关。两入口及原Run备份 `entry-backup/`，四库SQLite Backup API和布局/界面/账号设置保护快照 `before-reopen/`，均在本轮验证目录。托盘退出脚本返回NaN坐标失败，未继续点击；保护数据后结束旧PID19968，首次进程ID方案PID61032也在保护后结束，随后重新publish并正常后台启动最终PID52336。两入口实际扩展属性均MessagePurify.Shiyao，Run值与备份相同；实际主窗口属性同ID。最终UIAutomation读取独立“拾要 - 1个运行窗口”、Appid: MessagePurify.Shiyao，Codex组仅1个窗口。两卡片parentClass=SHELLDLL_DefView、visible/child/noActivate=true、topmost=false，实际坐标/尺寸匹配保存DIP；任务完整记录、布局字节和界面偏好与最终重开前保护快照相同，四库integrity=ok。`taskbar-formal-final.json`、`window-identity-final.json`、`real-data-check-final.json`、`preferences-final.json`和`real-desktop-cards-final.png`保存证据，真实桌面截图已打开查看。主窗口已唤回供用户查看独立任务栏；真实事项和QQ未操作。未新增长期稳定/自然时刻/游戏/多屏验收。

文档同步后原入口测试误取历史外观交付段；已改为定位需求最新交付和交接当前接续，并同时核对2 DIP与独立标识，最终全量日志 `node-delivery-final-tests.log`。maintain-context-hygiene本机技能路径未找到，已人工核对有效需求、交接、源码、发布目录、入口和运行报告，不删除文件。提交说明 `fix: tighten card snapping and separate taskbar identity`，标识以Git日志为准。

### 上一段（卡片自动吸附与透明度，2026-10-13，已实现、验证并正常重开）

用户要求两张桌面卡片靠近时自动吸附，并能自行调节透明度。用户明确选择A：左右吸附只对齐顶部，保留各自高度，空待办继续紧凑；上下按可见高度衔接并对齐左边。已接入标题拖动：边缘距离16 DIP以内、另一方向有可见重叠时吸附，面板间留10 DIP边距；拉开即可解除，松手仍以原schema 1布局保存，只移动正在拖动的卡片。隐藏待确认不参与，屏幕容不下完整窗口时保留原工作区域约束。没有持久绑定，内容高度随后变化不自动移动另一张卡片。

设置→桌面卡片提醒新增“卡片底色透明度”滑块，0%实色到100%透明，两卡片共用。即时同步只改变面板底色alpha，文字及行内容不整体变淡；未设置沿用原浅深材质。桥校验整数0..100/null，其他事项命令拒绝该字段；interface-preferences.json及页面localStorage持久化，旧偏好缺少字段兼容。已有备份的可选DesktopTransparency字段接入导出、预览、恢复及恢复前保护包，旧schema 1包缺少字段恢复原主题材质。没有新增账号/事项格式。

实际验证：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release` 410/410；Infrastructure相同Release命令681/681（新增6项透明度备份边界），App自包含Release publish退出0。页面浏览器79/79覆盖滑块input及真实CDP鼠标拖动不中断、主题保留和旧偏好恢复；最终 `node --test tests/*.test.mjs` 152/152通过（正式页面79项），退出0，日志 `node-final-tests.log`；`git diff --check`退出0。正式发布版 `python tests/app_desktop_test.py --executable .tools/published/app-card-controls/MessagePurify.exe` 退出0、100项Native及真实父窗/styles检查通过：新增四向吸附、空卡片衔接、拖离、位置重读、隐藏卡片、正式滑块/文字/重开/备份检查。Native移动检查调用与标题拖动相同的宿主回调，不移动用户鼠标或真实卡片。首次Native透明度超时根因是新WebView首个账号快照重置设置导航，改为等待snapshot与initialized后导航；真实CDP拖动首次因控件尚在视口外失败，先scrollIntoView后通过；脱离测试原沿衔接边平移仍应吸附，改沿法线拉离后通过。最初构建缺少几何namespace和截图函数，补导入并使用现有CapturePreviewAsync后通过。失败报告保留。

接续基线a3970ac。**正式入口已切换到最新版 `.tools/published/app-card-controls/`。** 21:18实际迁移桌面/开始菜单和已启用Run，保留原开关及--background；入口/Run备份 `.tools/card-controls-validation/entry-backup-20261013-211840/`，旧exe保留。PID41884通过托盘正常退出后，SQLite Backup API保存四库及共用/账号设置于 `before-reopen/`，四库integrity=ok。新版PID19968正常后台运行，两卡片真实附着SHELLDLL_DefView、visible/child/noActivate=true、topmost=false；实际坐标/尺寸匹配保存DIP，布局字节及任务完整记录与重开前相同，四库integrity=ok。实际部署未改变透明度选择或代拖动真实卡片。日志与实际截图 `real-desktop-cards-final.png` 位于 `.tools/card-controls-validation/`，合成设置及真实桌面截图已打开查看，原透明度与卡片位置保留；真实Windows登录、自然固定时刻、实际游戏及长期稳定性仍未验收。commit说明 `feat: add desktop card snapping and transparency controls`，标识以Git日志为准。maintain-context-hygiene先前本机未找到，按有效需求/源码/入口/运行报告人工核对，不删文件。

### 上一段（桌面卡片外观优化，2026-10-13，已实现、验证并正常重开）

用户截图反馈两张纯黑大框在桌面过于突兀，要求优化外观。已实现 WPF 透明窗口、半透明蓝灰/浅色面板、16 DIP 圆角、细边框与轻阴影；数量独立徽标、轻量列表、自定义按钮/7 DIP 滚动条及缩放手柄。“提醒事项”无待办时可见面板缩至约154 DIP，下方透明；有事项恢复原列表高度，主题切换保留滚动位置。`DesktopCardStyles.xaml` 与 `SmokeCheck.DesktopAppearance.cs` 已加入。主界面深色纯黑规则不变，QQ只读和同步/事项行为不变。

接续基线9414a77。**正式入口已切换到最新版 `.tools/published/app-desktop-appearance/`。** 20:04迁移桌面/开始菜单“拾要”及已启用Run，保留原开关与 `--background`；原入口/Run备份 `.tools/card-appearance-validation/entry-backup-20261013-200438/`，旧产物保留。原PID40208经托盘正常退出后，四库SQLite Backup API保护快照及布局等设置保存于 `before-reopen/`，四库integrity=ok。已正常重开新版，最终后台实例PID41884；两张卡片实际parentClass=SHELLDLL_DefView，visible/child/noActivate=true、topmost=false，托盘显示拾要。真实Windows登录自启动仍未验收。

真实窗口截图发现首次重开的卡片使用默认位置并提示布局读取失败，虽然布局文件字节未变，不能据此宣称位置保留。根因是原 `DesktopGeometry.Clamp` 用“原点+尺寸-尺寸”，保存的y=206.4经浮点计算成为206.39999999999998，`Math.Clamp`拒绝上界小于下界。新增同一小数坐标的读取、两种Load及单卡保存回归，首次实际失败后改为先相减尺寸。Windows全量391/391通过，隔离发布版91项Native及真实父窗/styles检查通过后，PID49904经托盘正常退出；重新发布正式目录并启动PID41884。最终实际窗口坐标/宽高均与原保存DIP×1.25一致，布局文件字节及任务完整记录与保护快照相同，四库integrity=ok。`real-desktop-final.json`、`real-data-check-final.json`与实际桌面截图 `real-desktop-cards-final.png` 位于 `.tools/card-appearance-validation/`；截图已打开核对半透明圆角及空待办收紧。早先屏幕裁图 `real-desktop-cards.png` 被浏览器遮挡，不作为卡片视觉证据；最终改用桌面宿主PrintWindow截取，不切换用户前台窗口。

实际验证：`node --test tests/*.test.mjs` 全量151/151、`.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release` 最终391/391、同一dotnet自包含Release publish退出0。`python tests/app_desktop_test.py --executable .tools/published/app-desktop-appearance-final/MessagePurify.exe` 对最终代码的隔离发布版通过91项Native和真实父窗/styles检查；正式目录随后同一代码再次publish退出0。新增7项Native外观检查覆盖浅深材质、圆角外侧透明、空面板下方透明/收紧、内容高度恢复、真实ScrollBar Thumb DragDelta及主题滚动位置保留。日志包括 `desktop-layout-final.log`、`publish-delivery-final.log`；早先151项Node、390项Windows及91项Native完整日志仍保留。首次构建的WPF/WinForms类型冲突用明确别名解决；细滚动条实际宽度最初16.8 DIP，根因为系统模板覆盖宽度，窗口资源设置VerticalScrollBarWidthKey=7d后通过。两次托盘正常退出诊断成功，不能把第一次找不到菜单归因于DPI；正式托盘测试脚本未修改。本轮提交说明 `fix: soften desktop cards and restore fractional layouts`，提交标识以Git日志为准。

原两小时观察报告completed=true、success=true、failure=null、238样本，18:40结束；本轮只读取完成标记，未逐样本验收，不能宣称稳定性通过。自然固定时刻、实际游戏、物理多屏/Explorer重启/睡眠和Windows登录仍待验收。当前外观实现及正常重开完成，审美反馈待用户提供。maintain-context-hygiene技能先前本机搜索未找到，已人工核对有效需求、实际源码、发布入口、运行窗口和报告；不删除文件。

### 上一段（拾要命名与图标，2026-10-13，已接入、发布及更新入口）

用户要求设计软件图标并换有深意的名字。本轮采用“拾要”（Shiyao），寓意从纷杂消息中拾取要事；用户尚未提供外观反馈。内置 image_gen 生成深青绿底、米白消息气泡和暖金折页图标，原图已保存 `src/MessagePurify.Windows/App/ui/assets/shiyao.png`（1254×1254、RGBA、外侧透明）；同图编码九尺寸 `shiyao.ico`。`ui/assets/README.md` 保存含义、规格、生成方式和最终提示词。

正式页面、主窗口、托盘、桌面卡片标题、备份对话框及 exe 产品信息已写入“拾要”；exe/窗口/托盘共用嵌入ICO，页面使用本地PNG。内部 MessagePurify 标识、单实例管道/互斥锁、数据目录、备份格式不变。**正式入口已切换到最新版 `.tools/published/app-shiyao/`。** 安装脚本预检新旧两种名称入口后创建“拾要”，只清理目标及参数匹配的原“消息提纯”入口；其他程序入口保持拒绝。

17:44实际迁移：`.tools/shiyao-validation/entry-backup-20261013-174413/` 保存原两入口及Run值；执行 `.tools/shiyao-validation/install-branding.ps1`，内部调用 `packaging/app/Install-MessagePurifyShortcuts.ps1 -PreviousPublishDirectory .tools/published/app-desktop-recovery`，退出0。`installed.json` success=true、两入口名称为拾要、target及图标为新exe、参数为空；自启动保持开启，只更新原Run值指向新exe加 `--background`，未写偏好。异常分支会恢复入口和Run，本次未触发。旧exe保留，PID40208仍存活，未退出或重启应用/QQ、未代改事项。新入口属性已核对，正常重开新版与真实Windows登录自启动仍未验收。

实际验证：`node --test tests/*.test.mjs` 151/151（正式页面78、文档46、图标2、快捷方式8等）、`.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release` 390/390，均退出0；同一dotnet执行 `publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-shiyao` 退出0，无警告错误。新exe通过隐藏启动的 `--smoke-test --report .tools/shiyao-validation/native-smoke.json`，退出0，新报告success=true、synthetic=true、84项（新增branding）通过，实际验证窗口名称/ICO、WebViewPNG加载和两卡片名称。原生截图 `native-smoke.png` 已实际查看，浅深主题图片加载及可见性由浏览器回归验证。日志与报告均在 `.tools/shiyao-validation/`。

首次Node失败根因已核对：两条原测试把新品牌图片误当正文图片，改为正文范围和排除品牌；生成图中心alpha=253，保留原alpha，验证接近不透明；文档测试原先只读取第一段，改为明确读取正在进行的观察记录。修正后全量151项通过，首次失败日志保留。首次exe FileDescription仍为MessagePurify，SDK生成AssemblyInfo证实Title属性未设置AssemblyTitle；改用AssemblyTitle并再次实际发布，`published-version.json`验证ProductName/FileDescription均为拾要，系统可提取40像素exe图标。元数据修正不改程序集名、数据或运行逻辑。本轮提交说明 feat: brand the application as Shiyao，标识以Git日志为准。

最终发布的独立桌面脚本实际验证：`python tests/app_desktop_test.py --executable .tools/published/app-shiyao/MessagePurify.exe` 首次因旧脚本固定假设最终待确认为空而失败；核对现有84项合成流程与最终报告，当前有待确认，改为按 pendingCount+mergeSuggestionCount 核对Win32真实可见性。修正后再次执行退出0、success=true、synthetic=true、84项通过，两窗口真实parentClass=SHELLDLL_DefView且child/noActivate/非topmost，日志 `desktop-script-final.log`；空待确认隐藏仍由Native流程内 pending-hide 检查覆盖。更新后的托盘检查脚本经PowerShell Parser实际解析，错误0。

上下文核对：本机两技能根目录未找到 maintain-context-hygiene 的SKILL.md，按项目规则人工核对有效需求、交接、源码、实际发布和报告。17:53观察146样本、completed=false、failure=null、原应用仍运行，仅记录时间点；下一步沿用下段观察接续，不能预先标为稳定通过。品牌范围完成，外观反馈及下次正常重开后实际使用反馈待用户提供。

PID40208及原两小时观察继续运行；为保留16:40开始的连续观察，本轮不退出或重开原应用。新版本已单独发布并迁移入口，运行中的旧界面/托盘将在用户下次正常退出重开时更换；不会代改事项或QQ数据。已有观察验收状态仍按下段真实报告核对，品牌改动不代表两小时验收完成。

### 上一段（正式入口更新及两小时连续观察，2026-10-13，启动时历史记录）

**正式入口已切换到最新版 `.tools/published/app-desktop-recovery/`。** 用户要求先完成入口更新，接续基线 `9b566a0`。默认脚本已改，快捷方式安装脚本新增 `PreviousPublishDirectory` 仅接受明确指定旧版的空参数exe入口，其他程序和带参数旧入口仍拒绝；相关测试覆盖迁移、重复安装、旧产物保留和拒绝意外入口。

16:55先将桌面/开始菜单两入口及Run原值保存到 `.tools/entry-upgrade/20261013-165509/`（指针 `current-location.txt`），真实默认入口预检Ready=true、AuthenticationReady=true，目标为最新exe。实际运行 `packaging/app/Install-MessagePurifyShortcuts.ps1 -PreviousPublishDirectory .tools/published/app-launcher`；两入口target/工作目录/图标均为最新版、参数为空、正常窗口。`app-settings.json`不存在时沿用产品默认开启，原Run也已开启；只更新HKCU Run的MessagePurify值为最新exe加 `--background`，偏好不写，保留开关状态。迁移脚本包在try/catch内，异常恢复原两入口及Run；本轮未触发回滚。`result.json` success=true、startupMatches=true、旧exe仍存在；QQ入口和其他程序未操作。没有重启Windows，真实登录自启动仍未验收。

16:55～16:56使用现有桌面检查脚本向当前主窗发送WM_CLOSE隐藏到托盘，再分别通过桌面/开始菜单真实.lnk打开，均唤回同一PID40208、主窗visible=true，短暂启动进程退出0、实例数始终1，两卡片parent/styles仍正确。原应用未退出或重开，真实任务和课程未操作，最终主窗打开可供用户使用。证据 `hidden-{0,1}.json`、`activated-{0,1}.json`、`activation.json`；检查时观察进程存活、32样本、completed=false、failure=null。两个入口的退出重开目标由实际COM属性和默认预检确认；持续观察期间不为验证重开而中断应用。入口更新不代表两小时观察、固定时刻、游戏或系统恢复验收完成。

入口相关验证实际通过：`node --test tests/application-shortcuts.test.mjs tests/application-launcher.test.mjs` 9/9；更新后入口和文档组合55/55；`node --test tests/*.test.mjs` 全量146/146（页面77、文档46），均退出0，日志 `.tools/entry-upgrade/{entry-tests,node-tests}.log`。默认预检Ready/AuthenticationReady=true，HKCU Run实际值等于同版exe加 `--background`，16:59观察40样本、failure=null、无脱挂/无响应样本。`git diff --check`退出0。应用产品源码未改，不重新发布或重复.NET/Native全量；脚本、相关回归及文档一并提交，提交说明 fix: point application entries to the current release，标识以Git日志为准。人工核对当前入口与需求、交接、源码、实际报告一致，保留历史时间点记录。

接续基线 `d5e0901`，工作区干净。用户再次要求继续；16:37核对QQ/应用均无进程、接口未监听，沿用已授权安全只读验证，未改变课程/日期/任务。16:38在应用停止时再次用只读SQLite Backup API保护四库和本地设置，逐库integrity=ok。产物 `.tools/unattended-validation/continuous-20261013-163828/`；当前目录指针 `continuous-location.txt`。仍为停机保护快照，不是正式ZIP恢复验收。

隐藏启动已有 `packaging/napcat/Start-NapCat-Framework.ps1`，QQ已有登录恢复成功；3001/6099监听同一QQ主进程39020。16:39通过最新发布版预检后正常启动 `.tools/published/app-desktop-recovery/MessagePurify.exe --background`，PID40208；未并行另开同库只读check。观察启动时恢复启动前Run值并记录 `startup-preserved.json` preserved=true，当时默认快捷方式未改；16:55后入口以本段最新记录为准。实际执行 `python .tools/desktop-validation/inspect-resident-desktop.py --pid 40208 --report <本轮目录>/desktop-start.json`，退出0、success=true、synthetic=false，两卡片实际parentClass=SHELLDLL_DefView、visible/child/noActivate=true、topmost=false。本轮不再次点击托盘或操作真实事项。

**两小时连续观察已启动，尚未完成。** 16:40:00隐藏启动现有 `tests/watch_application.ps1 -ProcessId 40208 -Report <本轮目录>/watch-2h.json -Seconds 7200 -IntervalSeconds 30`，观察进程16160，预计北京时间18:40结束。`observer-launch.json`记录真实启动和PID，`watch-2h.json`已新写入并按30秒增长；16:41核对已有2个样本、completed=false、failure=null，观察与应用进程均存活。两卡片均附着、responding=true、连接提示等待下次同步，待确认75。后台工具只观察，不点击、不发送QQ消息、不终止应用；用户可正常使用电脑。聊天结束后报告继续落盘，但没有自动回访/通知；接续时须读取实际报告，逐样本核对连接、响应、附着、持续时间和资源变化，不能预先标为两小时稳定通过。

16:41只读数据检查 `initial-data-check.json`：四库integrity=ok，任务完整记录与课程关系字节均和本轮保护快照相同，来源键重复均0、证据1。当前0个待办/1个已完成任务，75项未处理、1项已忽略、1项已接受；同步前70未处理，正常启动新增5项，operations77→82，未代确认。该数为本轮初始检查时间点，不替代后续用户操作或到点同步结果。

观察启动这一段产品源码和观察脚本未改，仅同步交接、需求、测试说明及相关文档回归。16:46核对报告已增长为14样本、elapsed394.3秒，failure=null、completed=false，卡片脱挂和responding异常均0，连接提示全部等待下次同步，应用与观察进程均存活。实际 `node --test tests/project-rules.test.mjs` 为45/45、退出0；`node --test tests/*.test.mjs` 为143/143（页面77、文档45、观察工具2）、退出0，日志 `.tools/unattended-validation/continuous-node-tests.log`；`git diff --check`退出0。对应提交说明 docs: record active two hour application observation，标识以Git日志为准。沿用首轮真实冷启动及短观察证据，不重复.NET/Native全量。人工核对需求、交接、真实进程和报告完成上下文整理；maintain-context-hygiene技能先前搜索未找到，不因此改无关文件。下一步读取本次完整观察结果及结束后数据核对；自然20:00同步点不在16:40～18:40窗口内，固定时刻、实际游戏、物理多屏/Explorer重启/睡眠/Windows登录与长期稳定性仍未验收。

### 上一段（无人值守真实验收，2026-10-13，首轮结果已记录，连续观察未验收）

接续基线 `f615cbd`，开始时工作区干净。用户说明 QQ 已退出，将休息约两小时，授权安全操作；北京时间14:09核对QQ/应用均无进程、3001/6099未监听，Framework和最新发布版预检Ready=true、AuthenticationReady=true。沿用QQ只读，不代改课程、日期、任务，不重启电脑/Explorer、不睡眠、不改屏幕。窗口不覆盖08:00/12:00/20:00自然同步点，不能替代定时验收；关机/睡眠漏同步政策仍未确认。

14:12在QQ/应用停止时，用只读SQLite源及Backup API保护四库，逐库integrity_check=ok，拷贝账号设置、共用布局及active marker，不含QQ凭据。位置 `.tools/unattended-validation/checkpoint-20261013-141200/`，指针 `checkpoint-location.txt`。这是停机保护快照，不是正式ZIP备份导出/恢复验收。

隐藏启动现有 `packaging/napcat/Start-NapCat-Framework.ps1`，已有登录自动恢复成功，无需代登录。执行 `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .tools/desktop-validation/run-resident-check.ps1 -ReportName unattended-read-only-first.json -Seconds 90 -PublishDirectory .tools/published/app-desktop-recovery`，退出0；报告 `.tools/desktop-validation/unattended-read-only-first.json` 为success=true、synthetic=false、connected/online、receiving=paused、history=complete、error=null、failures=[]；52群/9置顶群，上次成功14:13:24、下次计划20:00。其tasks=1及pending=71是快照类别计数，不能当作1个待办/71项未处理；该检查最多120秒、无正常卡片/托盘，不能替代常驻观察。只读约束指QQ接口，本地同步及自动缓存维护仍会写库。

14:16启动最新 `.tools/published/app-desktop-recovery/MessagePurify.exe --background`，当时PID20308。`python .tools/desktop-validation/inspect-resident-desktop.py --pid 20308 --report .tools/unattended-validation/desktop-normal-first.json` 和 `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/app_tray_test.ps1 -ProcessId 20308 -WaitSeconds 10` 均通过；两卡片visible/child/noActivate=true、parentClass=SHELLDLL_DefView、topmost=false，托盘可见，未执行菜单退出。默认快捷方式不变；正常启动自动改写的Run值已恢复原值，16:23再核对startupValuePreserved=true，记录 `startup-final-check.json`。

新增 `tests/watch_application.ps1`：按PID/StartTime只观察，采样CPU/内存/句柄/响应、两卡片真实附着和白名单连接提示/数量，原子写报告；不保存私人标题、正文、凭据或原始异常，不终止应用。Windows PowerShell 5中文脚本首次解析失败，根因为无UTF8 BOM，修正后实际运行通过。`powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/watch_application.ps1 -ProcessId 20308 -Report .tools/unattended-validation/watch-short.json -Seconds 5 -IntervalSeconds 1` 退出0，实际5.1秒、4个样本，responding=true、两卡片附着有效、提示均为等待下次同步、未处理70；working set约214～218MB，包含UIA观察影响，不能作为空闲资源基线。无卡片数量时null不猜为1。completed/success只表示观察完成及附着有效，持续在线和响应必须逐样本判断。

**两小时连续观察未启动、仍未验收。** 16:20接续核对发现QQ/应用均已退出；16:23确认3001/6099未监听，记录 `.tools/unattended-validation/final-runtime-check.json`。Application/System日志可读，14:09起未找到匹配QQ/应用的1000/1001/1026崩溃记录或匹配的睡眠/重启记录，本机启动时间07:45:55。退出原因未确定，不将无事件等同正常退出，不宣称两小时稳定运行。用户给定约两小时窗口已过去，未自动再次重开QQ或应用，也没有后台观察任务或自动通知待运行。

16:22只读核对 `.tools/unattended-validation/final-data-check.json`：四库integrity=ok、原任务完整记录与课程关系字节均未变，task_evidence=1、operations=77，tasks/reviews来源键重复均为0。TaskState=1在启动前就存在，按枚举是Completed；当前0个待办、1个已完成任务，不代改状态。reviews为70项未处理、1项已忽略、1项已接受；同步前58项未处理，新增12个，未代确认。旧“1个待办、46未处理”只作为10月12日历史，不作为当前基线。冷启动前后对比保留在 `cold-start-data-check.json`。

新增 `tests/application-watch.test.mjs` 已实际2/2通过，覆盖其他进程及已退出PID的安全失败报告、不终止被观察进程和无临时报告残留。实际执行 `node --test tests/*.test.mjs`：首次142项141通过，新文档断言要求“70项未处理”而交接漏写“项”；纠正文案后全量142/142（页面77、文档44、观察工具2）通过、退出0，日志 `node-tests-final.log`。`git diff --check`退出0。产品源码未改，不重复前段已通过的.NET全量和83项合成。所有本机日志、快照及报告在忽略目录 `.tools/`，不提交原日志。未找到maintain-context-hygiene技能，人工核对需求、交接、枚举、运行报告及实际文件。下一步在正常运行实例上完成两小时连续观察，再完成自然固定时刻及用户已延后的实际游戏/多屏/Explorer/睡眠/Windows登录验收；M3/M8继续实施。

### 上一段（桌面恢复，2026-10-13，已实现并通过自动/Native 合成验证）

接续基线 `7b7ff0e`，先读交接和需求并核对实际代码及干净工作区，沿用 `codex/m3-integration-probes`。依据设计10.1节，计划 `docs/superpowers/plans/2026-10-13-desktop-recovery.md`。`DesktopEnvironment.cs` 捕获真实父窗、统一父窗 DPI 和映射到父窗坐标的各屏工作区域，主屏优先；真实核对 parent、child/noActivate/非topmost。`DesktopCardHost.cs` 每两秒检查附着与环境变化；桌面不可用时释放旧窗并等待，返回后重建当前两卡片。主窗 hook 接收显示、工作区域、DPI、TaskbarCreated 和电源恢复事件，仅刷新当前显示，不新增 QQ 同步请求或系统通知。跨 await 再核对环境、attachment epoch、账号/工作区/generation；旧按钮与旧拖动/缩放回调失效。定位失败不更新 Bounds，不保存错误位置；交互内取消失败，退出和 Dispose 防止重复释放。

`DesktopPlacement.cs` 将整张卡片夹进单块屏幕 work area，最大相交面积优先，无相交按夹回移动距离选择，平手保持输入顺序；避开任务栏及屏幕空隙。schema1 DIP 不变，自动屏幕/DPI变化重新读取原布局偏好但不写文件，原区域返回恢复位置。`SaveCard` 只保存用户改的一张，另一张保留原始 DIP；损坏文件读取保留，明确手改允许修复。备份 schema 和现有单矩形接口保持兼容。

实际失败/修复：首次新增 overload 造成四处旧测试目标类型二义性，明确 DesktopRect 后专项68项42失败/26通过，缺实现失败符合预期；实施后两个测试期望有误（垂直区域底边须减卡片高度、任务栏夹具自身将Y设0），核对几何后68/68通过。首次 Native fixture 将 SetParent(child,0) 误要求 GetParent=0，改成有效 HWND 且实际父窗不同；旧Host再次实际发布运行，在脱挂后附着/styles检查失败，证实原误判。临时恢复基线源文件保留旧时间戳使一次增量发布沿用旧Host，触碰源码并重新编译后纠正。DPI Native 诊断确认实际531px/期望531.25px、文件不变，但按钮模板尚未完成布局；改为等待真实 VisualTree 标题出现。以上失败记录保留在 `.tools/desktop-validation/`。

一次有限只读复核发现读取错误提示残留；先强制 signature 失效才能真实触发“最终附着检查后关闭窗口→Render失败”，旧行为实际失败后改为本次读取错误局部提示，成功刷新自然清除，用户操作错误仍保留。另一个实际旧 Thumb 回归证实会保存新卡片的临时回退布局，增加 Alive/真实附着核对后通过；不继续扩展复核范围。

根代理实际串行全量：Core306/306、Infrastructure675/675、Windows390/390，均退出0；布局/消息专项68/68。App Release build 0警告/0错误，自包含发布 `.tools/published/app-desktop-recovery/` 退出0。发布版 `--smoke-test --report .tools/desktop-validation/desktop-recovery-native.json` 退出0，报告确为本次新写入、success=true、synthetic=true、83项通过（原68+桌面15）。独立隔离账号和适配器覆盖有效HWND脱挂、Close重建、宿主丢失/返回、当前新要求/历史/撤销期限/主题/布局、工作区域缩小返回、DPI、跨await环境变化、读取错误清除、旧按钮及旧缩放回调、电源恢复合成消息和主WebView可用。最终原宿主任务7/待确认2，隔离恢复宿主任务1/待确认0，与快照一致；两组parentClass=SHELLDLL_DefView、child/noActivate=true、topmost=false，隔离待确认窗口visible=false。页面、原卡片及独立恢复卡片截图已实际查看：`desktop-recovery-native.png`、`desktop-recovery-native-cards.png`、`desktop-recovery-native-desktop-recovery.png`；卡片图为内容渲染，隐藏待确认窗口的实际状态以Native报告为准。

实际命令：三个 `./.tools/dotnet/dotnet.exe test tests/MessagePurify.{Core,Infrastructure,Windows}.Tests --no-restore --configuration Release` 分别串行运行；`./.tools/dotnet/dotnet.exe build src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release`；同项目 `publish --no-restore --configuration Release --self-contained true --output .tools/published/app-desktop-recovery`；使用 `Start-Process -WindowStyle Hidden -PassThru` 启动发布版合成检查并等待、核对退出和报告新写入。日志 `.tools/desktop-recovery-*.log`；未找到 maintain-context-hygiene 技能，已人工核对需求/交接/源码/报告，不删除文件。`node --test tests/*.test.mjs` 全量139/139（正式页面77、文档43），退出0；最终 App build 0警告/0错误、`git diff --check` 退出0。对应提交说明为 feat: recover desktop cards after host and display changes，标识以 Git 日志为准。

本轮只用隔离合成数据，不重启用户Explorer、不睡眠机器、不改真实屏幕/账号或默认入口。实际多屏、Explorer重启、睡眠及长期稳定性仍未验收，睡眠漏同步政策仍未确认。下一步进入统一真实验收，保留用户已要求延后的实际游戏验收；M3/M8 不因合成通过而标完成。

### 上一段（备份恢复，2026-10-13，本地数据已实现并通过自动/Native 合成验证）

接续基线 `7a9607a`，先读交接及需求并核对实际存储和干净工作区，沿用 `codex/m3-integration-probes`。计划 `docs/superpowers/plans/2026-10-13-backup-restore.md`，依据已确认设计第5.3节。`Infrastructure/Backup/AccountBackupService.cs` 使用四库保留锁及独立只读源 SQLite Backup API 生成同一时点快照；固定 ZIP 包含四库、账号设置、共用自启动/布局/界面偏好，清单记录版本、账号、时刻、长度、SHA256及逐图片引用。不含凭据、其他账号、active-account.txt和游戏目录缓存。当前图片只有QQ标识，not-downloaded且路径/校验为空，有图片时明确“不完整：未包含原图”；没有新增图片下载或宣称完整图片恢复，后期OCR及真实图片验收仍保留。

设置页通过正常注入的 WPF 文件选择器导出、选择预览、取消、明确确认覆盖及打开本地位置。预览展示全部状态数量、历史和共用设置范围。恢复前导出同格式自动备份，暂存校验完整性、schema/必要表列、账号、JSON和必要证据；整体切换及三项共用设置带持久化 journal，失败和进程中断回滚原目录与设置。工作区原实例在 gate 内恢复，已接收消息等锁释放后核对账号继续；generation 使旧页面/卡片请求失效，页面丢弃旧快照/草稿，重载工作区设置和桌面布局，界面偏好重开仍有效。恢复补漏复用现有 Startup 同步请求，受游戏限制；不重置撤销期限，不发送旧提醒或系统通知。最近结果持久化在账号目录外 backup-history.json。

实际失败验证：存储17项及工作区5项首次均因可编译未实现入口失败；桥23项6失败/17拒绝通过；页面新增3项先失败；新增原偏好桥测试先失败。首次存储组合22项21通过、evidence-owner损坏夹具被外键提前拒绝，已显式关闭夹具外键并重跑。检查发现结果记录写入失败可能发生在恢复提交之后：真实文件锁回归先复现数据已切换，再把结果成功写入放到commit标记前，并记录历史文件回滚。必要列损坏回归先错误接受，已补完整列核对；新增seed真实接受审核后，缺证据夹具因重复键失败，已只损坏单行。最终存储/工作区26/26，桥/运行时26/26通过。有限只读复核核对恢复、回滚、宿主、旧请求、页面、卡片及偏好，无新增具体问题。

根代理实际串行全量验证：Core306/306、Infrastructure675/675、Windows344/344，退出0；App Release build 0警告/0错误，发布 `.tools/published/app-backup-restore/` 退出0。最终发布版 `--smoke-test --report .tools/desktop-validation/backup-restore-native.json` 退出0，报告新写入、success=true、synthetic=true、68项通过（原52+备份16）。真实WebView2页面覆盖导出、预览不覆盖/取消、确认整份恢复、恢复前包、原撤销时刻、历史/证据/课程、损坏包拒绝、旧generation、原消息重读去重、重开、布局/偏好和两卡片刷新。两卡片 parentClass=SHELLDLL_DefView、child/noActivate=true、topmost=false，任务7/待确认2与最终快照一致。页面结果、确认预览及卡片截图已实际查看：`backup-restore-native-backup.png`、`backup-restore-native-backup-preview.png`、`backup-restore-native-cards.png`。仅隔离合成数据，真实账号、默认入口与正在运行版本未切换。

Node 两次全量各138项137通过：新增文档回归先发现需求未写隔离合成验证范围，再发现计划缺少SQLite全称；两处正文已同步，最终全量138/138（页面77、文档42）通过、退出0。日志 `.tools/backup-*.log`，Native 报告及截图 `.tools/desktop-validation/`。实际命令：三个 `./.tools/dotnet/dotnet.exe test tests/MessagePurify.{Core,Infrastructure,Windows}.Tests --no-restore --configuration Release`（分别运行）；`./.tools/dotnet/dotnet.exe build src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release`；同项目 `publish --no-restore --configuration Release --self-contained true --output .tools/published/app-backup-restore`；发布版通过 `Start-Process -WindowStyle Hidden -PassThru` 启动合成模式并等待，核对退出和新报告；`node --test tests/*.test.mjs`。最终文档同步后node --test tests/project-rules.test.mjs为42/42、退出0；git diff --check退出0。未找到 maintain-context-hygiene/SKILL.md，已人工核对需求、交接、源码与实际结果，不删除项目文件。下一步为桌面恢复，再统一真实验收；对应提交说明 feat: add account backup and restore，标识以Git日志为准。

### 上一段（消息缓存清理，2026-10-13，已实现并通过自动/Native 合成验证）

接续基线 `97fa794`，工作区核对为干净，沿用 `codex/m3-integration-probes`。依据已确认设计第 5.2 节实施最近 7 天临时消息清理，计划 `docs/superpowers/plans/2026-10-13-cache-cleanup.md`。`AccountCacheCleanup.cs` 按原消息发送时刻严格早于 7 天边界且当前处理版本已路由选择临时消息；全部 task_evidence、全部状态待确认、普通通知、未处理/未路由/版本未匹配消息及保留消息的递归引用链保护。删除及成功结果复用三库同事务，保留路由回执与历史；`EnqueueCore` 对已路由旧消息去重，清理后重读不重新入库。

工作区、快照、历史批次自动入口、`clean-cache` 桥及设置页“清理消息缓存”已接入。设置显示最近成功时间和实际删除条数，不宣称物理磁盘释放量；异常或暂停回滚删除与结果，清理错误独立呈现，不把已完成同步记为失败。成功结果在账号 routing.sqlite3 的 cache_cleanup_result 持久化；原 schema 1、消息/事项/审核/通知和旧提醒历史保留。OCR 图片附件缓存尚未接入，不宣称图片清理完成；本轮只用隔离合成数据，不操作真实账号缓存或切换默认入口。

存储 25 项先全部因未实现失败；工作区/宿主首次可运行 11 项中 7 项因缺少命令/入口失败，4 项参数拒绝通过。桥首次 38 项中正常请求 1 项失败、37 项拒绝通过；页面新增 5 项先因缺少入口失败。实施后组合专项 38/38、桥 38/38、页面 71/71 通过。有限复核发现后续自动成功快照未清除页面旧清理错误：新增 2 项真实 receive 回归先失败，再按账号及成功记录时间修正；页面最终 73/73 通过。旧成功、过期快照及另一账号不能清除新错误，其余指定路径无具体发现。已实际串行运行三个 .NET Release 全量：Core 306/306、Infrastructure 649/649、Windows 318/318，均退出 0；App build 退出 0、0警告/0错误，Node 全量 133/133（页面73、文档41）通过。最终自包含发布 `.tools/published/app-cache-cleanup/` 退出 0，发布版 Native 报告 `.tools/desktop-validation/cache-cleanup-native.json` 进程退出 0、报告新写入、success=true、synthetic=true、52 项通过；通过正式设置页实际删除 1 条旧闲聊、保留 3 条旧证据，正式页面查看任务/待确认/通知原文，重开工作区及隐藏 WebView2 页面恢复同一结果，旧消息重读不重新入库。

Native 测试脚本修正三处前置条件/操作时机：闲聊应允许 Ignored 诊断候选，普通通知通过通知列表打开而非桌面任务/待确认入口，重开页面等待首个账号快照后再切设置。三次失败报告保留在 `.tools/cache-cleanup-validation/native-fixture-failure.json`、`native-navigation-failure.json`、`native-initial-snapshot-failure.json`；均为隔离合成数据，不涉及真实账号。最后截图前显式刷新并核对桌面卡片与最终任务/待确认数量一致，两卡片 parentClass=SHELLDLL_DefView、child/noActivate=true、topmost=false。最终 Native 页面及卡片截图已实际查看。

实际命令（根目录）：

- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release --filter 'FullyQualifiedName~CacheCleanupTests'`：首次 25/25 失败，原因是可编译未实现接口；日志 `.tools/cache-cleanup-store-red.log`。
- 存储/工作区/三个新增宿主专项同过滤器组合：38/38，日志 `.tools/cache-cleanup-integration-green.log`。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release --filter 'FullyQualifiedName~CacheCleanupBridgeTests'`：38/38，日志 `.tools/cache-cleanup-bridge-green.log`。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`：306/306。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release`：649/649。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release`：318/318。
- `./.tools/dotnet/dotnet.exe build src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release`：退出 0、0警告/0错误。
- `./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-cache-cleanup`：退出 0。
- `node --test tests/*.test.mjs`：133/133，退出 0。
- 发布产物以 `Start-Process -WindowStyle Hidden -PassThru` 启动 `--smoke-test --report .tools/desktop-validation/cache-cleanup-native.json` 并等待：进程退出 0、52 项通过，报告确为本次新写入。
- 文档最终同步后 `node --test tests/project-rules.test.mjs`：41/41，退出 0；`git diff --check`：退出 0。

日志 `.tools/cache-cleanup-validation/`、`.tools/cache-cleanup-ui-{red,green}.log` 和 `.tools/cache-cleanup-ui-recovery-{red,green}.log`；最终截图 `.tools/desktop-validation/cache-cleanup-native.png`、`cache-cleanup-native-cards.png`。对应提交说明为 feat: clean expired unlinked message cache，标识以 Git 日志为准。QQ/NapCat 只读，默认入口与运行版本未切换。下一步为备份恢复、桌面恢复，再统一真实验收。未找到 maintain-context-hygiene 技能文件，已按项目规则人工核对需求、交接、代码与实际结果，未删除项目文件。

### 上一段（跨群可能重复建议，2026-10-13，已实现并通过自动/Native 合成验证）

接续基线 `a16ccea`，已核对需求、交接、实际代码及干净工作区，沿用 `codex/m3-integration-probes` 分支。计划为 `docs/superpowers/plans/2026-10-13-cross-group-merge.md`。当前消息模型缺少可核实的跨群原通知身份，故严格匹配完整原文及原始新 Task 候选后提供“可能重复”的人工建议；不自动合并，不放宽跨群延期/取消等关联规则。同课程群须已确认且未排除，保留数字、日期、班级、轮次差异；排除多行动、共享证据、拆分家族，并尊重合并别名上的保持独立决定。

匹配器及工作区快照已写入：`CrossGroupMergeFinder`、`ApplicationItem.MergeSuggestion` 和 `merge-review` 派生项携带双方 ID/版本，复用已实现的合并差异表和保持独立事务；不伪造普通 `PendingReview`。建议已读版本使用双方版本之和，避免较大版本掩盖另一方修改。专项实际执行 `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release --filter 'FullyQualifiedName~CrossGroupMergeWorkspaceTests|FullyQualifiedName~CrossGroupMergeFinderTests'`：**57/57**、退出 0；工作区新增 5 项先全部因缺少建议失败，再通过。匹配专项 52 项覆盖可靠性、严格原文、班级、独立别名及拆分家族；工作区覆盖原文和双版本、未读重现、处理刷新及重开。文档回归 `node --test tests/project-rules.test.mjs` **40/40**、退出 0。

正式页面已接入待确认列表/数量/默认入口、双方正式值与原文、预选合并差异表及桌面打开建议；保持独立持久化后移除建议，合并/拆分/任一方版本变化后重算。双版本冲突显式刷新保留草稿，未知保存确认原请求，账号及迟到响应隔离。建议不使用普通接受/忽略/拒绝命令。桌面待确认卡片包含建议，提醒卡片继续显示当前正式任务；处理后建议移除。卡片新增 11 项先复现失败（旧 31 项通过），页面新增 8 项先复现默认入口/路由等失败，再实施通过。

本轮实际验证：Core Release **306/306**、Infrastructure Release **611/611**（匹配器56、工作区5）、Windows Release **280/280**（卡片42）均退出 0；App Release build 无警告错误。`node --test tests/*.test.mjs` **125/125**（页面66、文档40）、退出 0。自包含发布 `.tools/published/app-cross-group-merge/` 退出 0，无警告错误；发布版 `--smoke-test --report .tools/desktop-validation/cross-group-merge-native.json` 进程退出 0，success=true、synthetic=true、**45 项**通过（原36+新增9）。检查真实 WebView2/本地桥、卡片打开建议、预选双方、当前手改日期差异、保持独立重开、显式合并证据/历史/旧 ID 以及移除建议；两卡片 parentClass=SHELLDLL_DefView、child/noActivate=true、topmost=false。Native 页面/卡片截图已实际查看。

实际命令（根目录）：

- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`：306/306。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release`：611/611。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release`：280/280。
- `./.tools/dotnet/dotnet.exe build src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release`：退出 0，0警告/0错误。
- `node --test tests/*.test.mjs`：125/125，退出 0。
- `./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-cross-group-merge`：退出 0。
- 发布产物以 `Start-Process -WindowStyle Hidden -PassThru` 启动 `--smoke-test --report .tools/desktop-validation/cross-group-merge-native.json` 并等待：退出 0、45 项通过。
- `git diff --check`：退出 0。

同步最新阶段时，旧文档断言仍要求“下一项为跨群合并建议协调”，首次 Node 全量 124/125；更新断言核对“跨群建议已接入、下一项缓存清理”后，全量重跑 125/125、退出 0，失败日志保留 `.tools/cross-group-validation/node-doc-state-failure.log`。引用修复后已重新发布并运行45项Native检查，核对报告确为本次进程新写入、成功且为合成数据。

日志 `.tools/cross-group-validation/`；Native 截图 `cross-group-merge-native.png` 和 `cross-group-merge-native-cards.png`；正式页面建议/差异图 `cross-group-suggestion.png` 和 `cross-group-suggestion-merge.png`，四图均已实际查看。一次有限复核发现引用上下文遗漏：4项真实提取回归先因错误建议失败（原52项通过），再保守排除含Quote的来源，匹配56/56通过；针对该修复复核为ADDRESSED，无新增具体问题。对应提交说明为 feat: suggest cross-group duplicate tasks，标识以Git日志为准；默认入口、正在运行版本和真实 QQ 数据未切换，QQ/NapCat 只读；下一步缓存清理、备份恢复、桌面恢复，再统一真实验收。未找到 `maintain-context-hygiene` 技能文件，已按项目规则人工核对阶段、有效需求、代码与实际结果，未删除项目文件。

### 上一段（手动合并与拆分，2026-10-13，已实现并通过自动/Native 合成验证）

接续基线 `5abb6f9`，沿用已确认设计第 7.2 节及既有 `codex/m3-integration-probes` 分支。实施计划为 `docs/superpowers/plans/2026-10-13-task-structure.md`。`merge-tasks` 同事务核对双方版本，逐字段选择标题/行动、课程、正式时间、优先级及状态；选择的当前优先级持久化为显式覆盖，避免选择另一日期时重新分级改变所选值。来源 ID 保留并跳转，原证据、双方历史及旧计划/台账不删除。`keep-separate` 保存双方独立决定，后续显式合并可替代当前关系，旧审计保留。

`split-task` 保留原项，逐项核对新标题、课程、正式时间、优先级、状态及原证据子集，保存关联去向；有证据但缺日期时进入待确认，无证据手动事项必须日期。不伪造 QQ 消息。待确认保留所选状态和时间类型，补日期确认后仍可通过原关联入口定位正式任务；由普通通知转任务或已收到新要求的原项均可拆出并确认。合并/拆分和保持独立清除失效的完成及关联忽略撤销令牌；重跑原消息回执不重建来源项，共享拆分证据的歧义变更保守待确认。

页面提供差异、逐字段选择、保持独立及拆分证据表单；快照刷新和课程选项更新保留草稿及焦点，双版本冲突先显式刷新核对。保存结果未知或已写入但应答失败时锁定原字段并用原操作/新项 ID 确认重试，迟到或另一账号应答不打断当前编辑。复核发现的优先级与新要求拆分闸口问题均以失败回归复现后修正。

实际验证：Core Release **306/306**、Infrastructure Release **550/550**（结构存储 **39** 项、工作区 **10** 项）、Windows Release **269/269**（结构桥 **33** 项）均退出 0。Node 全量 **116/116**（正式页面 **58/58**、文档 **39/39**），退出 0。自包含发布 `.tools/published/app-task-structure/` 退出 0，无警告错误；`MessagePurify.exe --smoke-test --report .tools/desktop-validation/task-structure-native.json` 进程退出 0，报告 `success=true`、`synthetic=true`、**36 项**通过。两卡片 parentClass=SHELLDLL_DefView、child/noActivate=true、topmost=false；合并只留一项待办，正式拆分及无日期待确认刷新两卡片，重开与关联导航通过。

实际命令（根目录）：

- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`：306/306。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release`：550/550。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release`：269/269。
- `./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-task-structure`：退出 0。
- `node --test tests/*.test.mjs`：116/116，退出 0。
- `git diff --check`：退出 0。
- 发布产物以 `Start-Process -WindowStyle Hidden -PassThru` 启动 `--smoke-test --report .tools/desktop-validation/task-structure-native.json` 并等待：退出 0、36 项通过。

测试日志 `.tools/task-structure-validation/`，页面截图 `.tools/desktop-validation/ui-merge-conflicts-dark.png`、`ui-split-pending-dark.png` 已实际查看（图像是浅色，文件名沿用生成时名称）；Native 页面及卡片图为 `task-structure-native.png` 和 `task-structure-native-cards.png`，两图已实际查看。默认入口和正在运行版本未切换，真实 QQ 事项未操作，QQ/NapCat 继续只读。跨群自动合并建议协调尚未实现；下一步继续此项和其余确定功能、缓存清理、备份恢复、桌面恢复，统一真实验收后置。`maintain-context-hygiene` 技能仍未找到，已按项目规则人工核对有效需求、交接、代码和报告，不删除项目文件。

### 上一段（手动新增任务，2026-10-13，已实现并通过自动/Native 合成验证）

基线 `f2c81e5`，按已确认需求实现补录标题、课程、正式日期及可选钟点；任务页提供“手动新增”，默认优先级按日期自动调整，也可选 P0～P3 和截止/开始类型。`create-task` 使用版本 0、稳定任务及操作 ID，重复重试幂等；失败保留全部草稿，账号切换及迟到响应不覆盖当前账号内容。复用既有课程选项，保存新名称后筛选和编辑立即同步；后台快照更新保留填写内容、焦点和筛选。来源 `Source="manual"` 在任务 JSON 持久化，旧 JSON 缺省为 `qq`；手动时间受保护，证据为空，不生成 QQ 消息，列表、详情、变更历史及桌面显示“手动添加”。计划为 `docs/superpowers/plans/2026-10-13-manual-task.md`。

保存结果不明确时先保留原请求并提供“重试确认保存”，确认前锁定已发送字段，避免已写入后修改草稿导致 ID 冲突或把自动分级转为手动覆盖；可靠失败快照证明尚未保存时允许继续修改。旧失败快照不能解除锁定。用户离开新增表单后，迟到的成功只更新快照和提示，不切走当前页面、不清除另一任务的草稿或版本冲突提示。

本项实际验证：Core Release **306/306**、Infrastructure Release **501/501**（新增手动专项 **38/38**）、Windows Release **236/236**，正式页面 **45/45**，均退出 0。自包含发布 `.tools/published/app-manual-task/` 无警告错误，`MessagePurify.exe --smoke-test --report .tools/desktop-validation/manual-task-native.json` 进程退出 0，报告 `success=true`、`synthetic=true`，Native 合成 **25 项**通过，覆盖页面新增、日期精度、来源历史、重开、卡片刷新及完成。两个卡片仍为 `SHELLDLL_DefView` 子窗口、noActivate=true、topmost=false。默认入口、正在运行版本和真实 QQ 事项未切换，统一真实验收后置；下一项为合并拆分、其余确定功能、备份恢复和桌面恢复。

最终 `node --test tests/*.test.mjs` **102/102**（页面 **45/45**、文档 **38/38**），退出 0；`git diff --check` 退出 0。新增表单截图 `.tools/desktop-validation/manual-task-form-dark.png` 已实际查看。此项仍在既有 `codex/m3-integration-probes` 分支工作，产物和真实数据没有切换。

本项实际命令（根目录）：

- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`：306/306。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release`：501/501。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release`：236/236。
- `node --test tests/*.test.mjs`：102/102。
- `./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-manual-task`：退出 0，无警告错误。
- 发布的 `MessagePurify.exe --smoke-test --report .tools/desktop-validation/manual-task-native.json`：PowerShell `Start-Process -WindowStyle Hidden -PassThru` 启动并等待，进程退出 0，success=true、synthetic=true、25 项通过。

### 上一段（可读变更记录与卡片标记，2026-10-13，已实现并验证）

接续基线 e56b1c7，已读取现有操作前后审计并在详情显示中文字段差异、北京时间、关联原消息证据和历史顺序；延期、取消、新要求和手动编辑产生重要变化标记，纯催交只追加证据；关联待确认建议保留正式值并提供查看入口。任务列表与两张桌面卡片显示变化标签，`mark-read` 按任务版本确认，旧版本不能清除后来变化。实现计划为 docs/superpowers/plans/2026-10-13-task-change-history.md；Core **306/306**、Infrastructure **463/463**、Windows **206/206**、Node 全量 **95/95**（正式页面 **39/39**）均退出 0。自包含发布 `.tools/published/app-change-history-final/` 无警告错误，Native Smoke 报告 `.tools/desktop-validation/change-history-native-final.json` 为 `success=true`、`synthetic=true`、21 项通过。本轮不操作真实 QQ 事项或替换默认入口；OCR、额外安全增强和统一真实验收后置。

### 上一段（完整编辑，2026-10-13，已实现并通过自动/Native 合成验证）

用户已明确同意完整编辑范围：标题、课程、正式日期、钟点、时间类型、优先级。六字段已接入本地任务存储、工作区、桥协议及正式页面；沿用版本化命令、操作幂等、原始证据及操作前后审计、手动时间保护与时间版本。优先级新增可选手动覆盖值，兼容旧任务 JSON 的自动计算。版本冲突拒绝保存并保留界面全部草稿，显式刷新后核对再保存；刷新响应按请求发起时的账号和任务绑定。编辑或清空课程后，同账号同群对原证据消息的明确引用仍可关联；仅标题匹配仍要求课程一致，跨群、跨账号及歧义关联继续拒绝。最新 Core Release **306/306**、Infrastructure Release **455/455**、Windows Release **203/203**、Node 全量 **90/90**（正式页面 35/35）均退出 0；自包含发布 `.tools/published/app-full-edit/` 成功无警告错误，Native 合成报告 `.tools/desktop-validation/full-edit-native.json` 为 `success=true`、`synthetic=true`、17 项通过，覆盖编辑、重开、桌面及版本冲突。本轮未替换默认正式入口和正在运行版本，未操作真实 QQ 事项；真实 QQ 编辑场景统一验收后置。可读变更记录与卡片变化标记已在当前接续实现；下一步完成手动新增及其余确定功能、合并拆分、备份恢复和桌面卡片恢复。QQ/NapCat 保持只读；OCR 和统一验收仍后置。

本轮实际验证命令（项目根目录执行）：

- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`：306/306，退出 0。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release`：463/463，退出 0。
- `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release`：206/206，退出 0。
- `node --test tests/*.test.mjs`：最终 95/95（正式页面 39/39、文档断言已同步），退出 0。
- `./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-change-history-final`：退出 0，无警告错误。
- 最新产物 `MessagePurify.exe --smoke-test --report .tools/desktop-validation/change-history-native-final.json`：以 PowerShell `Start-Process -WindowStyle Hidden -PassThru` 启动并等待，进程退出 0，报告 `success=true`、`synthetic=true`、21 项通过。缩放检查按提示精确定位手柄，避免把滚动条 `Thumb` 当作缩放控件。

### 上一段（普通通知转任务，2026-10-12）

用户确认普通通知转任务按“沿用通知标题和课程、正式日期必填、钟点可选”实现后，本轮已接入正式工作区、桥协议和 WebView2 页面。通知详情新增日期/可选钟点表单和 `convert-notice` 命令；转换后原通知继续保留并标记“已转为任务”，保存关联任务 ID，支持从通知详情打开任务。转换任务沿用通知候选动作和开始/截止类型，保存候选中的全部消息证据；同一通知按通知 ID 幂等，同一条原消息中的不同通知分别建立任务。未填写正式日期时工作区和页面都拒绝提交；QQ/NapCat 仍只读，不新增任何 QQ 或 Windows 通知写入。

本轮新增/更新：`AccountTaskStore.CreateTaskFromNotice`、`AccountCandidateRouter.FindNotice`、`AccountWorkspace` 通知状态投影及 `convert-notice` 工作区命令、`AppBridgeProtocol` 操作注册、正式页面通知详情和模拟桥。回归覆盖：通知日期必填与重复操作、候选动作/开始时间/引用证据保留、同消息多通知独立任务、桥协议参数、页面日期校验/可选钟点/保留通知/查看任务。实际验证：Infrastructure Release **431/431**、Windows Release **171/171**、正式页面 **30/30**、Node 全量 **84/84**、文档回归 **35/35**；均退出码 0。下一项继续完整编辑与变更标记、合并拆分、备份恢复和桌面卡片恢复；普通通知转任务的真实 QQ 场景留到统一验收，不把合成页面回归写成真实接收验收。

上一段实施（基线 `07b6d9c`）：消息提纯自身的桌面与开始菜单便捷入口已完成。首轮指向隐藏 PowerShell 的入口被火绒隔离：桌面于北京时间 18:06:54、开始菜单于 18:06:56，分类 TrojanDownloader/LNK.Agent.g；只读核对 QuarantineEx.db/FilesV3_60 和 log.db/HrLogV3_60 的对应路径与时间，未修改安全设置或隔离内容。现改为普通快捷方式直接指向 MessagePurify.exe、空参数和正常窗口；应用自身读取唯一启用的本机 3001 配置，环境凭据优先，损坏/歧义仅报告安全错误码。

上一段验证：快捷方式/旧启动脚本 7/7、配置读取专项 11/11、Windows Release 164/164、Node 全量 80/80；自包含发布至 `.tools/published/app-launcher/`，默认入口 `-CheckOnly` Ready/AuthenticationReady=true，Native 合成 13 项报告 `.tools/desktop-validation/app-launcher-native.json` success=true。当前安装报告 `.tools/desktop-validation/app-launcher-shortcuts-installed.json` 记录桌面与开始菜单入口已实际安装并检查；桌面入口启动 PID 13596，两个桌面卡片均 visible/child/noActivate=true、parentClass=SHELLDLL_DefView、topmost=false。开始菜单入口再次启动保持单实例，主窗口隐藏后再次启动唤回；真实只读报告 `.tools/desktop-validation/app-launcher-read-only.json` 为 connected/online、历史 complete、失败为空，UIA 显示等待下次同步、18:29 成功时间及 20:00 计划时间。QQ 快捷方式和同步规则保持。

最新工作顺序（2026-10-13）：用户要求先把已确定的功能全部实现，再统一进行验收。Windows 登录自动启动、普通通知转任务、完整编辑、可读变更记录/卡片变化标记和手动新增和手动合并/保持独立/拆分已完成；跨群可能重复建议已实现并验证；消息缓存清理、本地数据备份恢复及桌面卡片恢复已实现并通过自动/Native合成验证，下一阶段为统一真实验收。OCR 图片下载、队列和重分析作为后期独立项目，当前不实现；与本机数据泄露相关的额外安全增强也暂缓。QQ 只读及不得向群聊或私聊发送消息仍是硬约束。自然固定时刻、实际游戏暂停/补同步、长期稳定性和完整真实端到端场景等验收统一后置。关机/睡眠漏同步规则仍是未决定事项，不在未确认前实现。

上一段接续（基线 `b4dc8e9`）：常驻同步状态显示已实现并验证，正常等待、正在同步、游戏延后、未补全及连接异常分别显示。上次成功同步（本次运行）仅在实际完成且没有历史失败的批次结束后记录；重连、游戏暂停及状态刷新保留该时间，下一计划时间复用北京时间调度规则。本轮发布至 `.tools/published/app-sync-status/` 并更新默认入口，正式应用已重开为 PID 40840，主窗口停在设置页；真实界面显示等待下次同步、17:51 成功时间及今晚 20:00 计划时间，两卡片和 QQ/接口保持运行。便捷启动、自启动、OCR 队列、自然固定时刻及实际游戏验收继续保留，整体 M3/M8 不标完成。

当前接续（Windows 登录自动启动，2026-10-12）：自动启动已接入生产代码和设置页。使用当前用户 HKCU\Software\Microsoft\Windows\CurrentVersion\Run，默认注册 MessagePurify.exe --background；--background 隐藏主窗口但保留托盘和桌面卡片，普通入口唤回同一实例后正确恢复主窗口，托盘入口也复用该路径。app-settings.json 原子保存偏好；注册表或偏好文件写入失败时回滚注册表状态并返回可读错误。验证模式不会写入 Run 项。StartupRegistrationTests 与 AppBridgeProtocolTests 53/53、正式页面 29/29、应用 Release build 退出 0 且无警告，git diff --check 退出 0；发布版后台启动、普通入口唤回、Run 项查询和托盘检查均通过。登录后的真实 Windows 自启动验收统一后置；下一项进入完整编辑、变更标记和普通通知转任务。OCR 队列后置，不作为当前阶段阻塞项。

当前验收顺序：用户要求先验证真实 QQ 接入、首次后台同步、批次结束后保持连接及桌面卡片；实际游戏期间暂停/退出后补同步的实机验收延后，游戏禁止同步与自动维护名单的有效需求不变。用户正常退出后，已于约 17:01 重开 Framework；真实首次后台同步与重开去重通过，两张正式卡片及托盘常驻已实际检查。QQ 和正式应用保持运行，主窗口停在新版设置页，显示同步状态，3001/6099 监听且正式应用连接保持。以下上段“未运行/未启动”描述是历史快照，当前结果以本段及本轮记录为准。

**最新有效决定（2026-10-12）：用户选择 A，第一版只使用“提醒事项”和“待确认”两张桌面卡片，取消全部 Windows 系统通知弹窗和声音。** 时间节点、新发现、变更、恢复/历史/迟到批次汇总、断线超过 5 分钟通知、通知点击路由及权限恢复全部取消；自定义时间提醒和安静时段入口移出当前产品范围。普通群通知仍保留在 App“通知”列表，原消息、事项、证据和旧提醒计划/台账不删除。正式两张桌面卡片首段已实现并验证：自动刷新真实 AccountWorkspace 快照、点击查看/完成、左右初始布局、拖动缩放、DIP 布局存盘，仅附着 Explorer 桌面、不设 Topmost。默认入口已指向 `.tools/published/app-sync-status/`，托盘生命周期已修复；当前 QQ/Framework 和正式应用运行，两张卡片可见，主窗口可查看真实任务详情，3001/6099 监听且应用连接保持；首次同步、单条真实任务确认/完成/撤销/重开通过。多屏、Explorer 重启和睡眠恢复尚未验收，M3 真实接收/长期稳定性仍在实施。

本轮已确认：用户选择 A，QQ/NapCat Framework 常驻，固定时间后台同步；替代每次同步切换接入环境及结束后恢复普通 QQ 的旧方案。后台调度与常驻宿主已接入代码，保留连接并在批次之外暂停消息处理；应用启动和后台轮询不打开聊天窗口、不修改桌面 QQ 快捷方式。新版本已自包含发布至 `.tools/published/app-scheduled-sync/`，默认入口已更新，13 项合成 Native 验证通过。用户要求先测真实 QQ 接入、首次后台同步、空闲保持连接及两卡片，实际游戏暂停/恢复实机验收延后；真实冷启动、首次同步及批次后保持连接已通过，三个自然固定时刻、性能与长期稳定性尚未验收。

已确认同步规则：每天 08:00、12:00、20:00（北京时间）各同步一次，共三次；打游戏时禁止同步，游戏期间不得为同步切换或重启 QQ。用户选择 A：游戏导致错过同步时，结束游戏后补同步一次；同一游戏期间错过多个时刻合并为一次补同步，同步中途开始游戏时暂停。用户进一步要求将电脑现有游戏全部加入检测，并定期扫描电脑游戏、同步检测名单，不再要求逐个提供游戏名称；默认应用启动及每日扫描一次，游戏期间延后目录扫描。自动游戏发现及缓存已接入，实际扫描发现 19 个游戏安装目录，Minecraft 整合包合为一项；不能将扫描结果写为所有游戏实际启动/退出识别已验收。关机/睡眠漏同步规则仍待确认。

上一段恢复（历史状态）：用户明确要求恢复普通 QQ，已重新建立桌面 `QQ.lnk`，直接指向 `D:/QQ/QQ.exe`，启动参数为空、使用 QQ 图标及正常窗口。旧备份有 Encrypted 属性，直接复制会报加密失败，因此创建普通快捷方式，不复制加密文件。实际文件持续存在、属性为 Archive，Explorer 桌面命名空间识别“QQ”条目；报告 `.tools/desktop-validation/qq-original-restored.json`。该段未退出或重开 QQ，火绒设置未更改。当时调度尚未实现，旧集成快捷方式方向继续停止；当前常驻调度实现以本轮记录为准。

上一段方向调整（旧按次切换方案，已被本轮常驻方案替代）：用户报告桌面 QQ 消失、火绒发出提醒，并明确要求停止集成快捷方式方向，回到固定时间自动切换与消息同步。当时需求为到点接入、同步置顶群并落盘、结束后恢复普通 QQ，时刻与漏同步规则尚待确认，自动切换尚未实现。原消息存储、提取、工作区和两卡片继续复用；停止快捷方式改造的决定保持有效，旧代码及备份保留历史。

上一段托盘修复：用户找不到托盘图标，已定位为启动回调返回时提前 Dispose NotifyIcon。正式入口改为等待应用监听结束，修复版已发布并打开；真实托盘显示、关闭隐藏、再次启动唤回单实例及菜单退出（ExitCode=0）通过。Windows Release 141/141、Node 七文件 63/63、Native 合成 13 项、独立桌面检查及默认入口检查均退出 0。真实只读复查为 52 个群、9 个置顶群、48 项待确认、0 个正式任务/通知，当次历史 complete；未确认真实课程或日期。Infrastructure/Core/OCR/demo 未改，沿用上一段 375/301/25/108 的实际验证结果。

上一段：已完成[正式桌面卡片首段计划](superpowers/plans/2026-10-12-desktop-only-cards.md)的实现与发布验证。Windows Release 141/141、Infrastructure Release 375/375、Node 七文件 63/63，实际 WPF/WebView2/桌面合成 13 项与独立父窗/style 检查通过；App 及 Windows.Tests locked restore、自包含发布和默认入口检查均退出 0。当时默认 app-desktop，旧 app 窗口仍在运行；本轮已替换为托盘修复版。

上段正式应用：2026-10-12 用户要求继续，已实施[正式应用首段计划](superpowers/plans/2026-10-12-application-host.md)：持久化界面、已有事项操作和只读消息宿主。当时基线为 `6ee0735`；Infrastructure Release 305/305、Core 301/301、Windows 77/77、OCR 25/25、demo 108 项及合成真实 WebView2 闭环通过。用户已退出 QQ 并授权继续，Framework 已重开，正式宿主真实读取 9 个置顶群并保存 40 项待确认，重启仍为 40 项。

历史接续（旧通知方案，现已取消渠道）：用户再次要求继续，基线 `473367e`；[持久化提醒第一段](superpowers/plans/2026-10-12-persisted-reminders.md)已实现并验证：正式任务提醒设置、计划与投递台账、恢复和串行协调。审查复现的失败汇总成员变更漏重试已修复，新增四项真实 SQLite 回归；最终 Infrastructure Release 375/375（服务 46、工作区 24），本段 Windows 96/96、Core 301/301、OCR 25/25、Node 七文件 59/59、demo 108 通过。最终产物再次自包含发布并通过真实合成 WebView2 八项，入口检查退出 0。新产物位于 `.tools/published/app-reminders/`，默认入口已更新；旧正式窗口及 QQ 仍运行，原产物保留。当时 Windows 实际投递及后台循环尚未接入，本段页面明确显示“系统提醒尚未开启”，没有新一轮真实 QQ 到任务/提醒验收，也没有代用户确认课程或日期。

正式应用首段已写入并验证：AccountWorkspace 串行组合消息、任务、路由及新增提醒四库、课程关系及已读，NapCatMessageHost 先落原消息再按原发送时间路由；WPF + WebView2 页面操作写入本地 SQLite。来源正文、完成/撤销、归档恢复、待确认显式日期、忽略/撤销、课程建议确认、搜索筛选及基础托盘可运行。旧方案的持久化计划和可控投递协调作为历史产物保留；当前页面已移除定时提醒表单、节点预览和安静时段入口，正式两卡片已接入，Windows 自动提醒调度与实际通知已取消。普通通知转任务、完整六字段编辑和可读变更记录/卡片变化标记已通过本地代码与专项回归接入；手动新增已接入并通过自动/Native 合成验证；跨群可能重复建议、缓存清理、备份恢复和桌面恢复已接入并通过自动/Native合成验证，OCR 队列及真实 QQ 任务端到端仍在后续。

## 1. 接手先看

- 项目目录：`D:\ai制作的软件\消息提纯`。
- 先读项目根目录的 `AGENTS.md`；恢复工作时先读 `docs/HANDOFF.md`，再读 `docs/requirements.md`，然后核对实际文件。
- 普通 QQ 登录不作为 OneBot 连接成功；本轮正式宿主使用只读接口并持久化真实群消息，合成页面操作和真实接收分别记录。
- 最新规则及结果：只读取 QQ 置顶群。本轮真实只读复查读取 52 个已加入群名单、9 个置顶群历史，当次读取 complete、失败为空，保存 48 项待确认，0 个正式任务/通知；此前两次为 40 项。未自动确认真实课程或日期，没有 QQ 发送或 Windows 通知。Infrastructure 最新完整回归 675/675；动态置顶人工验收、持续实时和长期稳定性未完成，M3 不改为完成。
- 当前阶段：**整体设计已确认，用户反馈 demo 基本满足要求，实施计划已写入并授权持续开发。M1 已完成，M2 已完成，M3 正在实施并保留真实接收/长期稳定性验收。M4 四段本地基础及正式 WPF/WebView2 主界面、AccountWorkspace 和只读宿主已接入；真实 QQ 历史接收/重启去重、合成 WebView2 已通过。旧方案持久化提醒第一段已验证并保留，Windows 全部系统通知与声音已取消；正式两张桌面卡片首段已实现并验证，当前快照、查看/完成、主题、布局存盘和实际桌面层级通过。Windows 登录自动启动已接入代码并完成自动/合成验证，登录后的真实系统验收后置。普通通知转任务、完整编辑、可读变更记录/卡片变化标记及手动新增已通过本地专项与 Native Smoke 接入验证；手动合并/保持独立/拆分已实现并验证，跨群可能重复建议已实现并验证，消息缓存清理、本地数据备份恢复及桌面卡片恢复已实现并通过自动/Native合成验证，下一步为统一真实验收。真实 QQ 编辑/任务端到端、持续真实使用和长期稳定性仍未完成；多屏、Explorer 重启和睡眠恢复尚未验收，M8 不标为完成。**
- 当前可交付产物：文档、原型及六张预览继续保留；新增 `global.json`、`.gitignore`、`src/MessagePurify.Core/` 下的 Time、Reminders、State 和 Rules 核心，`tests/MessagePurify.Core.Tests/` 测试项目，`tests/fixtures/rules/synthetic.json`、`tools/MessagePurify.RuleBaseline/` 及 `docs/verification/rule-baseline.md`。Core、Core.Tests 和 CLI 三个项目各有 `packages.lock.json`；NapCat Shell 与 Framework 的显式路径启动准备位于 `packaging/napcat/`，对应路径回归在 `tests/napcat-launcher.test.mjs` 和 `tests/napcat-framework-launcher.test.mjs`。可重复运行的检查及说明见 `tests/project-rules.test.mjs`、`tests/prototype-qa.mjs`、`tests/README.md`。
- 历史 demo/probe 概述（本轮正式结果见下节）：demo 已写入并运行浏览器验证，双击 `prototype/index.html` 可打开。事项、偏好、自定义课程／分类、撤销时间戳及桌面模拟布局保存在此浏览器的本地存储，刷新保留进度；“重置示例”恢复事项和卡片布局并保留设置及自定义课程／分类。事项时间固定为 2026-10-09 10:00（北京时间），撤销期限使用真实经过时间。用户已确认普通 QQ 在电脑登录；NapCat Shell 与 Framework 官方包均已下载校验。Shell 是无界面路线，同一账号与普通 QQ 并存时会冲突；当前改由 Framework 在 QQ 主进程中加载并自动登录，6099/3001 同时监听。真实探针取得 52 个群及一群一页的 7 条历史消息；当时分页读取 41/52 到达边界、窗口内去重 1098 条，11 群未补全，详见下节。用户确认聊天列表和窗口正常显示。OCR、隐藏 WebView2 桥和自包含 Windows 探针已有实际结果；已收到 1 条实时群文本事件；通知发布会自动从匹配的官方 Windows App SDK 运行时包提取资源，受控 COM 冷启动回调已通过；专用 URI 入口的用户实际点击通过。此前历史版本没有注册正式自启动；本轮已接入代码，登录后的真实系统验收后置。桌面探针基本操作与当时静音提醒呈现/点击通过，长期稳定性及桌面恢复/多屏仍未验收；声音提醒已取消。
- 用户要求持续维护交接文档；这是已授权的工作，以后有变化直接同步，不再反复询问是否记录。

### 本轮同步状态与时间显示（2026-10-12，已发布并重开）

- `MessageHostStatus` 新增可选 Scheduled/LastSuccessfulSyncAt/NextScheduledSyncAt；接入接口未运行实际批次时 history=pending、成功时间为空。历史队列完成且路由结束后才记录成功时间；失败批次不更新，部分历史失败在空闲重连后保留。游戏暂停结束且没有待补请求时及时恢复正常等待，调度/游戏保护及只读范围继续沿用。
- 主界面和两卡片区分正常等待、正在同步、游戏延后、名单未就绪、同步未补全及连接故障；设置显示“上次成功同步（本次运行）”“下次计划同步”，按北京时间格式化。时间没有跨进程持久化，本次运行无成功批次时明确显示空状态；计划时间不是游戏期间的执行承诺。状态更新保留课程草稿、焦点和选择范围，拒绝迟到旧状态。
- 实际 `./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release --filter 'FullyQualifiedName~ScheduledSyncTests|FullyQualifiedName~ApplicationMessageHostTests'` 61/61；完整 Infrastructure Release 428/428、Windows Release 153/153；`node --test tests/*.test.mjs` 最终 75/75（文档 33），均退出 0。App Release build 无警告/错误；`./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-sync-status`、默认入口 `-CheckOnly` 退出 0，Ready/AuthenticationReady=true。Core/OCR/demo 未改，本轮未重跑。
- 新产物 `--smoke-test --report .tools/desktop-validation/sync-status-native.json` 退出 0，13 项真实 WPF/WebView2/桌面合成闭环通过。原托盘脚本两次未找到旧版图标：实际限定到 Shell_TrayWnd 后可以找到隐藏按钮和图标，根桌面的跨提供程序搜索漏项；修正脚本的托盘及菜单搜索范围，`tests/app_tray_test.ps1 -ProcessId 5128 -ExitApplication -Verbose` 正常菜单退出并验证成功。此修正属于验证脚本，不宣称应用托盘重新修复。
- 实际 `.tools/desktop-validation/run-resident-check.ps1 -PublishDirectory '.tools/published/app-sync-status' -ReportName sync-status-read-only.json -Seconds 20` 退出 0、synthetic=false；connected/online/paused/complete、error=null、failures=[]，记录成功时间及下次北京时间 20:00，52 个群、9 个置顶群、1 个任务、47 个 review 类事项。只读 SQLite `count-sync-status-data.py` 退出 0：1 个任务、46 项 pending、1 项 ignored，任务仍为 2026-10-09/无钟点；报告中的 pending=47 是 review 类总数，不等于未处理数。
- 默认入口正常重开 PID 40840；`inspect-sync-status.ps1 -ProcessId 40840` 通过真实 UIA 验证等待及两项北京时间。该临时 UTF-8 脚本首次用 Windows PowerShell 5 读取导致中文选择器错误，改用本机 PowerShell 7 执行后通过，未改应用。`inspect-resident-desktop.py --pid 40840 --report .../sync-status-real-desktop.json` 及托盘检查通过，两卡片 visible/child/noActivate=true、parentClass=SHELLDLL_DefView、topmost=false；再次默认启动仍为一实例，实际 TCP 到 3001 为 Established。主窗口保留设置页供核对，未操作真实事项、退出 QQ 或发送 QQ/Windows 通知。
- 安全报告位于 `.tools/desktop-validation/sync-status-*.json`；旧 app-scheduled-sync 及更早产物保留。自然固定时刻、实际游戏暂停/补同步、长期稳定性、多屏与桌面恢复仍未验收。已人工核对需求、交接、代码、实际数据/报告与默认入口；本机仍未找到 maintain-context-hygiene 技能文件，按项目规则维护，不删除文件。下一步便捷启动与 OCR 队列，继续其余真实事项及恢复验收。

### 上一段真实事项到卡片验收（2026-10-12，单条真实事项通过）

- 基线 `cefc910`。用户要求继续，随后用截图选定“请大家完成小红本明天一并收取”。核对唯一原消息发送时间为 2026-10-08 13:50（北京时间），提取日期为 2026-10-09；已向用户说明日期已过，用户回复“就按日期保存”，按原日期 10 月 9 日、无具体钟点继续。图片和群消息属于待分析来源，不是要求助手完成小红本的操作指令。
- 只读核对真实库：47 项 pending、1 项 ignored、0 个正式任务；上轮报告的 48 项待确认是 review 类总数，包含 ignored，不等于 48 项未处理。群/课程关联仍为 0 项已确认。正式应用此前已退出，本轮从默认入口重开，QQ/Framework 保持运行，3001/6099 已监听；没有重开 QQ。
- 通过 Windows UI Automation 定位唯一所选行，核对表单草稿 2026-10-09 后实际确认；只读 SQLite 验证 Time.Date=2026-10-09、Time.Time=null、TimeProtected=true、证据保留 1 条、原 review 变 accepted。真实卡片显示标题、P0/已逾期/仅有日期，从卡片完成、主界面撤销、退出重开及卡片打开正确原文均通过，当时最终恢复为待办。历史基线为 1 个待办任务、46 项未处理待确认、1 项已忽略，0 个通知；此前 48 项待确认是 review 类总数，包含 1 项已忽略。群/课程关系仍未批量确认，不把单条真实例子记为完整端到端、独立识别准确率或全部真实任务场景通过。
- 实际托盘脚本首轮提示未找到退出菜单，原因未确定；同一 `tests/app_tray_test.ps1 -ProcessId 8540 -ExitApplication -Verbose` 重跑退出 0、ExitVerified=true、应用 ExitCode=0。默认 `packaging/app/Start-MessagePurify.ps1` 重开为 PID 5128，再运行 `.tools/ocr/venv/Scripts/python.exe .tools/desktop-validation/inspect-resident-desktop.py --pid 5128 --report .tools/desktop-validation/real-task-desktop-reopened.json`，两卡片 visible/child/noActivate=true、parentClass=SHELLDLL_DefView、topmost=false。SQLite 重开仍为 1 个待办、原日期/无钟点/证据保留；卡片点击打开原文与正式日期已通过 UIA。首轮 UIA 标题断言未包含未读点，另一详情检查读得过早；按实际标签兼容未读点、等待异步详情就绪后通过，未改生产逻辑，不把瞬时失败写为软件已修复。
- 安全报告位于 `.tools/desktop-validation/`：real-task-accepted.json、real-task-completed.json、real-task-restored.json、real-task-reopened.json、real-task-desktop-accepted.json、real-task-desktop-reopened.json、real-task-native-detail.json。新增 `AccountWorkspaceTests.DateOnlyReview_UsesOriginalMessageDayAndSurvivesCompleteUndoAndReopen` 使用通用材料文本与合成账号，验证原发送日期解析、明确日期确认、P0、完成/撤销/重开与证据；单项 1/1、完整 Infrastructure Release 419/419 退出 0，Node 全量 72/72（文档 32）退出 0。生产代码未改，不重新发布，Core/Windows/OCR/demo 未重跑。
- QQ/Framework 未退出，未发送 QQ 或 Windows 通知，主窗口保留该任务详情供用户核对。下一步补齐同步状态、上次时间与便捷启动，继续 OCR 队列、其余真实课程/事项及桌面恢复验收；自然固定时刻、实际游戏暂停/补同步及长期稳定性仍未验收。人工核对交接、需求、实际库、UIA 报告、测试与默认入口；maintain-context-hygiene 技能此前查找未找到，按项目规则人工维护，没有删除项目文件。

### 上一段真实后台同步验收（2026-10-12，首次同步与常驻通过）

- 基线 `f170dae`。用户明确将实际游戏暂停验收延后，先测试前面的真实 QQ/后台同步与桌面展示；不关闭游戏保护、不更改同步时刻、不调整系统时钟。
- 先读交接和需求、核对源码与发布入口，工作区原本干净。首次复查普通 QQ 已打开且接口未启动，用户回复“已退出”后核对所有 QQ 进程结束，以隐藏 PowerShell 运行 `packaging/napcat/Start-NapCat-Framework.ps1`；约 17:01 QQ 重开，3001/6099 均监听。没有修改桌面 QQ 快捷方式、QQ 安装或火绒设置，没有代用户确认课程或日期。
- 实际以本地临时辅助 `.tools/desktop-validation/run-resident-check.ps1 -ReportName resident-read-only-first.json -Seconds 35` 启动新版 `--read-only-check`，再以 `resident-read-only-repeat.json` 重开一次；两进程 ExitCode=0、success=true、synthetic=false，均为 transport=connected、qq=online、receiving=paused、history=complete、error=null、failures=[]。52 个已加入群、9 个置顶群、48 项待确认、0 个任务/通知，跨进程重开数量未增加。只读 SQLite 核对 messages=359、route_receipts=359、reviews=48，报告 `.tools/desktop-validation/resident-database-counts.json`；未读取或输出消息正文/凭据。首次同步不能替代自然 08:00/12:00/20:00 到点验收，游戏暂停/补同步与长期稳定性仍未验收。
- 运行 `packaging/app/Start-MessagePurify.ps1` 打开正式新版，再用 `.tools/ocr/venv/Scripts/python.exe .tools/desktop-validation/inspect-resident-desktop.py --pid 34036 --close-main --report .tools/desktop-validation/resident-desktop-hidden.json` 实际验证：两张卡片 visible/child/noActivate=true、parentClass=SHELLDLL_DefView、topmost=false；WM_CLOSE 后主窗口隐藏但进程仍在。`tests/app_tray_test.ps1 -ProcessId 34036` 退出 0、Success=true，发现“消息提纯”图标。再次默认启动仍为同一 PID、仅一实例，`resident-desktop-reopen.json` 再次验证可见后正常隐藏、卡片保留；实际 TCP 检查确认该进程到 3001 的 Established 连接保持。最后 QQ/Framework 与正式应用保持运行，未测试托盘退出，未代用户点击确认或完成真实事项。
- 实际 `node --test tests/*.test.mjs` 71/71（文档 31）、Infrastructure Release 专项 `--filter FullyQualifiedName~ScheduledSyncTests|FullyQualifiedName~NapCatMessageHostTests|FullyQualifiedName~InstalledGameCatalogTests` 38/38、宿主 `--filter FullyQualifiedName~ApplicationMessageHostTests` 29/29，均退出 0。第一个 filter 的 NapCatMessageHostTests 名称未匹配实际类，已补跑实际宿主类；合计 67 个本地专项通过。两个入口 `-CheckOnly` Ready=true、正式入口 AuthenticationReady=true、路径为 app-scheduled-sync。未改生产代码，未重跑未改动的 Core/Windows/OCR/demo 或重新发布。人工核对需求、交接、默认入口、报告与存储数量，maintain-context-hygiene 技能仍未找到，不删除项目文件。

- 新增文档断言首轮为 30/31：测试说明有数据和过程，但未明确写出“真实首次后台同步已通过”，已补齐有效结论；最终回归结果如上。该失败属于文档结论缺失，没有修改生产逻辑。

### 上一段确认常驻后台同步与自动游戏名单（2026-10-12）

- 基线 `7f55ded`。用户选择 A，确认 QQ/NapCat Framework 常驻，按北京时间 08:00、12:00、20:00 后台读取置顶群；替代旧按次切换及结束恢复普通 QQ。游戏期间禁止同步，中途开始游戏时暂停，退出后将错过多个时刻合并补一次。随后用户要求将电脑现有游戏全部加入检测，定期扫描并同步名单，不再要求用户逐个提供游戏名称；默认应用启动及每日扫描，游戏期间延后目录扫描。
- 产物：`src/MessagePurify.Infrastructure/Application/ScheduledSync.cs` 提供北京时间调度、游戏延后与保留至确认的请求队列；`NapCatMessageHost.cs` 接入常驻批次，`AppRuntime.cs` 接入后台 runner 与自动游戏名单。`InstalledGameCatalog.cs` 扫描 Steam 库、已登记游戏、WeGame/常见游戏目录、Epic 清单及 Minecraft 版本目录，缓存到 `%LOCALAPPDATA%/MessagePurify/App/game-catalog.json`；每分钟检查扫描是否到期，启动及每 24 小时扫描，游戏中延后；进程约每秒采样。名单未就绪时暂停自动同步，失败保留旧名单并重试。Minecraft 启动标记只在内存核对，不保存命令或认证参数。
- 修复核对发现的启动吞掉未来时点、仅发请求前检测游戏、请求出队后断线丢批次及 BeginHistory gate 外刷新问题。游戏请求前/响应后/逐条落盘/路由事务前检查，未保存的在途页丢弃，保存的原消息和未完成请求保留，退出游戏后从最新页恢复；补同步迟到请求与恢复批次合并，普通新固定时点保留。空闲重连不自动读取，非游戏空闲元数据约每 30 秒核对，游戏期间跳过轮询。
- 实际本机命令 `.tools/dotnet/dotnet.exe run --project .tools/game-catalog-check/GameCatalogCheck.csproj --configuration Release --no-restore -- .tools/game-validation` 退出 0；最终 19 个游戏安装目录、GameRunning=false，报告 `.tools/game-validation/installed-games.json`。包含 12 个 Steam manifest 游戏、2 个仍可运行的库目录、3 个 WeGame 游戏、守望先锋和 Minecraft；卸载空目录、Wallpaper Engine、运行库及平台启动器排除。首次实测发现 Steam 卸载入口误指向平台根目录、系统 Idle 与辅助程序同名误匹配，已修复并加入回归；此扫描不代表逐款游戏启动识别已验收。
- 实际 Infrastructure Release 418/418（调度 23、游戏名单 15、宿主 29）、Windows Release 141/141、Core Release 301/301、OCR unittest 25/25、demo 108 项及 `node --test tests/*.test.mjs` 69/69（文档 29、正式页面 25、启动/桥/打包/快捷方式）通过，均退出 0。验证命令见 `tests/README.md`。已自包含 publish `.tools/published/app-scheduled-sync/`、默认入口 `-CheckOnly` Ready/AuthenticationReady=true、执行新 exe `--smoke-test --report .tools/desktop-validation/scheduled-sync-native.json`，退出 0，13 项 Native 合成通过，父窗 `SHELLDLL_DefView`、非 Topmost。旧发布目录保留。
- 本轮没有启动或关闭真实 QQ，没有修改火绒或桌面 QQ 快捷方式；当前只读进程 QQ/MessagePurify 与 3001 监听计数均为 0。真实 Framework 冷启动、三个固定时间的后台读取、游戏中途暂停/退出合并补同步、游戏性能与长期稳定性尚未验收。常驻不代表首次自动加载 Framework 已实现，既有脚本仍拒绝在运行中的普通 QQ 上重复加载。`maintain-context-hygiene` 技能仍未找到，已按项目规则人工核对有效需求、交接、实际代码、产物和验收边界，没有删除项目文件。

### 上一段核对后台同步可行性（2026-10-12，待确认建议现已确认）

- 基线 `490833f`。用户询问后台同步可行性；只进行文件和官方资料核对、同步讨论记录及测试，没有更改生产代码、桌面 QQ 入口或实际切换 QQ。
- `AppRuntime.Start()` 使用 `Task.Run(RunAsync)`，消息宿主可后台读取、提取及落盘；AppWindow 关闭隐藏、托盘资源存活已有上段实际验证。Framework 启动脚本明确拒绝已有 QQ 上重复加载，当前按次接入仍涉及 QQ 重开，不能承诺仅隐藏窗口就全程无打扰。
- [官方 Framework 说明](https://doc.napneko.icu/guide/boot/Framework) 说明其用于桌面 QQ 并需启动加载；本机已验证接口与聊天窗口一起运行，现有本机加载器保留不变，不据官方新教程修改 QQ 安装。建议接口随 QQ 常驻、仅按三个时刻后台读取，首次接入或接口未加载时仍需重开 QQ；方案未确认，资源占用和游戏影响尚未实测，不把后台同步理解为允许游戏期间同步。
- 实际 `node --test tests/project-rules.test.mjs` 29/29、退出 0；新增常驻建议与有效规则分离检查，并修正设计稿首段仍写“具体时刻待确认”的旧状态。人工核对需求、交接、设计首段、源码和官方资料；只验证文档一致性，不将现有持续后台宿主记为定时同步已实现。

### 上一段确认游戏结束后补同步一次（2026-10-12）

- 基线 `4b4b9f4`。用户选择 A，确认因游戏错过的同步在结束游戏后补一次，并询问检测方法是否麻烦；同一游戏期间错过多个时刻合并为一次补同步，不逐次切换 QQ。
- 需求已确认，调度、游戏检测及退出游戏后的补同步尚未实现。本轮只同步有效需求、交接、相关说明及文档测试，并核对检测方法；不实际切换 QQ。
- 检测方式核对：[微软 Process.GetProcesses](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.getprocesses?view=net-10.0) 支持枚举本机运行程序；[QUERY_USER_NOTIFICATION_STATE](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ne-shellapi-query_user_notification_state) 的游戏相关状态仅说明独占全屏 Direct3D，BUSY 也包含其他全屏应用。因此不能将该接口单独作为所有窗口化/无边框游戏的可靠检测器。现阶段建议匹配用户常玩的游戏进程，游戏仍运行时保持延后，避免短暂切回桌面误触发同步；全屏状态只作辅助。此为检测方案建议，尚未实现或进行真实游戏验证，常玩游戏名称待用户提供。
- 实际 `node --test tests/project-rules.test.mjs` 28/28、退出 0，已更新游戏漏同步规则的相关断言，核对四份说明及当前有效需求/下一步不再将补同步规则写为待确认；人工核对建议与已确认规则分别记录，现有持续接收代码未改。文档与官方资料核对不能作为游戏检测或自动 QQ 切换验收。

### 上一段确认每天三次同步及游戏期间禁止同步（2026-10-12）

- 基线 `1392f96`。用户明确指定早上八点、中午十二点、晚上八点，共三次，并禁止打游戏时同步；承接上一题北京时间，记录为 08:00、12:00、20:00。同步时刻不再作为未决定事项。
- 当前实现核对：`src/MessagePurify.Infrastructure/Application/NapCatMessageHost.cs` 仍为持续连接、历史读取及实时接收循环；`packaging/napcat/Start-NapCat-Framework.ps1` 仍要求已有 QQ 先退出。现有代码没有定时自动切换或游戏检测入口，不能把需求确认写成已实现。
- 游戏导致错过同步后的补同步规则尚未确认；只讨论这一项，明确答复前不默认跳过或补同步。关机/睡眠漏同步、QQ 正在使用及游戏检测方案仍待后续处理。本轮不实际关闭或重开 QQ。
- 实际 `node --test tests/project-rules.test.mjs` 28/28、退出 0，新增时刻、游戏限制及当前状态一致性检查。首次新增用例因按第 1 节截断需求而漏读有效条款，修正测试读取范围后通过；验证仅覆盖文档，未运行 QQ 同步或游戏检测。交接前人工核对有效需求、下一步、接入/应用说明及现有持续连接代码，无新增调度产物。

### 上一段恢复普通 QQ 桌面图标（2026-10-12）

- 基线 `e87d88f`。用户明确说“帮我把 QQ 恢复”，本轮只恢复普通 QQ 的桌面入口，停止集成快捷方式改造的决定保持有效。
- 根因核对：`QQ.original.lnk` 属性为 Archive, Encrypted，桌面目录未加密；此前直接 Copy-Item 报“无法加密指定的文件”。`D:/QQ/QQ.exe` 完整存在。新增 `packaging/napcat/Restore-QQShortcut.ps1`，创建普通 `QQ.lnk`，Target 为 QQ.exe、Arguments 为空、Icon 为 QQ.exe,0、WindowStyle=1；不读取或修改消息接口配置，不复制加密备份，不修改 QQ 程序。
- 实际 `packaging/napcat/Restore-QQShortcut.ps1` 退出 0。8 秒后及后续检查文件仍在；Explorer `Shell.Application.Namespace(0)` 找到名为 QQ 的桌面项，Target=`D:/QQ/QQ.exe`、Arguments 为空、Encrypted=false，报告 `.tools/desktop-validation/qq-original-restored.json`。UIAutomation 未暴露此桌面条目，改由 Explorer 自身桌面命名空间核对；不记录为已经取得图标坐标或截图。
- 新增 `tests/qq-shortcut.test.mjs` 普通恢复用例，验证首次创建、直接指向原 QQ、图标、清除旧参数/最小化配置及重复恢复；实际 `node --test tests/qq-shortcut.test.mjs` 3/3，组合验证 `node --test tests/project-rules.test.mjs tests/qq-shortcut.test.mjs` 30/30，均退出 0。临时目录长短路径通过 realpath 核对，未执行假的 QQ 文件；本轮没有退出或重开真实 QQ，也没有改变火绒设置。
- 需求、交接、接入/应用说明和测试说明已同步，人工核对普通桌面入口与实际报告；后续继续固定时间同步的使用规则确认和实现，不恢复旧集成快捷方式路线。

### 上一段停止集成快捷方式方向，回到定时同步（2026-10-12）

- 基线 `2377443`。用户说明桌面 QQ 不见，随后看到火绒提醒，怀疑其删除了快捷方式；用户明确表示不再沿快捷方式方向开发，接入应在固定时间自动切换与同步。最新明确指示替代原“未决定：定时自动切换”及日常桌面 QQ 改造建议，具体时刻与使用冲突规则仍待确认。
- 用户打断前已核对实际：桌面路径为 `C:/Users/Administrator/Desktop`，隐藏图标设置为 0，QQ.lnk 不存在；`D:/QQ/QQ.exe` 及 `%LOCALAPPDATA%/MessagePurify/Shortcuts/QQ.original.lnk` 仍在。重建后的文件曾短暂存在，8 秒检查时再次缺失，Explorer 桌面项未找到；复制原备份回桌面报“无法加密指定的文件”，恢复未成功。没有拿到火绒具体拦截日志，不写成已独立确定删除机制或已恢复图标。
- 用户停止指示后没有再写桌面、关闭或启动 QQ、修改安全软件设置；只撤去本轮未提交的快捷方式重建测试扩展，原已提交脚本和测试保留历史。新的生产调度代码未实现、没有运行定时切换。
- 后续集中定时同步：沿用已确认 NapCat 与本地读取/存储，先确定具体时刻及 QQ 正在使用时的规则，再实现同步窗口和结束恢复。两张卡片展示本地已有事项，任务/证据/人工操作保护继续有效；不因停止快捷方式方向而删除这些产物。
- 本轮实际文档回归命令 `node --test tests/project-rules.test.mjs`，27/27、退出 0；交接前人工核对有效需求、交接、接入/应用说明和实现状态。固定同步方向与已实现功能分开记录，不使用旧快捷方式测试证明自动切换。

### 历史 QQ 桌面入口简化（2026-10-12，当前方向已停止）

本节记录当时实现和瞬时验证，QQ.lnk 当前实际缺失，不能作为当前可用入口或后续要求；用户已停止此方向。

- 基线 `6f1b648`。用户认为到项目目录找脚本重开 QQ 麻烦；采用原桌面“QQ”图标直接启动带消息接口的 QQ，使用方式仍是双击，未修改 QQ 安装文件、任务栏或开始菜单入口。
- 产物：`packaging/napcat/Install-QQShortcut.ps1`、`tests/qq-shortcut.test.mjs`，Framework 脚本新增 `-OpenExisting`。已有 QQ 时该开关调用原 QQ.exe 激活窗口；未传开关的直接 Framework 启动仍拒绝在已有 QQ 上重复加载。QQ 未运行时仍使用既有已验证 Framework 路线及置顶桥，账号、读取范围和只读边界不变。
- 实际执行 `packaging/napcat/Install-QQShortcut.ps1` 退出 0；更新 `C:/Users/Administrator/Desktop/QQ.lnk`，目标为 Windows PowerShell、参数包含 Hidden 和 OpenExisting，名称为 QQ、图标来自 `D:/QQ/QQ.exe,0`。原入口已备份到 `%LOCALAPPDATA%/MessagePurify/Shortcuts/QQ.original.lnk`；重复安装保留首次备份。测试使用临时中文路径、合成依赖和未执行的 QQ 文件，不依赖本机 NapCat 包。
- 实际用 `Start-Process` 打开桌面快捷方式并等待结束，ExitCode=0；3001 监听所属 QQ PID 前后相同、接口仍监听，报告 `.tools/desktop-validation/qq-shortcut-check.json`。没有退出 QQ、发送消息或重新确认课程；未重测完全退出后的冷启动，既有真实 Framework 冷启动记录继续保留。
- `node --test tests/napcat-framework-launcher.test.mjs tests/qq-shortcut.test.mjs` 3/3、退出 0；最终八文件 Node 命令 65/65、退出 0。新两项覆盖中文/空格路径引用、QQ 图标、隐藏窗口、备份保留、重复安装及已运行激活/普通入口拒绝重复加载。测试起初因临时目录 8.3 短路径与 PowerShell 展开的长路径不同而误报，改为核对 realpath 后通过。正式应用、Core/Infrastructure/OCR/demo 未改，不重复记为本轮重跑。
- 需求、交接、接入说明、应用说明和测试说明已同步，并按实际快捷方式/备份/报告人工核对上下文。后续用户日常直接双击桌面 QQ；登录 Windows 自启动、QQ 内部完全退出后重新启动、真实课程/任务、OCR 和恢复/多屏的剩余验收不变。

### 上一段托盘生命周期修复（2026-10-12）

- 基线 `a2faf28`，分支 `codex/m3-integration-probes`。用户反馈托盘中找不到“消息提纯”；实际旧进程确实没有图标，不是用户未找到隐藏图标。`Program.cs` 的启动异步回调使用 `using var tray`，但正常启动以 `_ = ListenAsync(...)` 立即返回并释放图标及取消令牌；改为 `await ListenAsync(...)`，资源存活到应用退出。
- 新增 `tests/app_tray_test.ps1`（UTF-8 BOM，兼容 Windows PowerShell 5.1），限定 Explorer 托盘容器并检查可见图标，必要时展开隐藏区；`-ExitApplication` 实际右击菜单、调用“退出”并核对 ExitCode=0。旧 app 进程 43172 的启动后图标检查实际失败、退出 1；修复版图标检查通过。测试保留进程句柄以读取退出码，并等待隐藏区展开动画、忽略不可见窗口，避免测试自身误报。
- 实际发布 `.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-tray-fix` 退出 0。默认 `packaging/app/Start-MessagePurify.ps1` 已改为 app-tray-fix，入口回归仍保留三个旧目录；旧进程仅在核对 PID/完整路径后结束，QQ 未退出。
- 实际 `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/app_tray_test.ps1 -ProcessId 24696 -ExitApplication` 退出 0，图标可见、菜单退出成功、应用 ExitCode=0；报告 `.tools/desktop-validation/tray-exit.json`。默认入口已重新打开修复版，当前 PID 26000（后续以实际进程为准）。向正常主窗口发送 WM_CLOSE 后进程和托盘保留；再启动默认入口唤回同一窗口且只剩一个 MessagePurify 进程，五项全部 true，报告 `tray-lifecycle.json` 和 `tray-hidden.json`。
- 实际 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release` 141/141、Node 七文件命令 63/63，均退出 0。新产物 `--smoke-test --report .tools/desktop-validation/tray-fix-native-smoke.json` 进程退出 0、success=true，原生 13 项通过；`.tools/ocr/venv/Scripts/python.exe tests/app_desktop_test.py --executable .tools/published/app-tray-fix/MessagePurify.exe` 退出 0，确认两卡片父窗/style。独立 PowerShell 默认入口 `-CheckOnly` 进程退出 0，Ready/AuthenticationReady=true、发布路径匹配，报告 `tray-fix-launcher-check.json`。Infrastructure/Core/OCR/demo 生产代码未改，沿用上段实际结果，不记为重跑。
- 真实只读复查执行已发布 app-desktop 的 `--read-only-check --seconds 20 --report .tools/desktop-validation/read-only-current.json`，进程退出 0、success=true、synthetic=false；transport=connected、qq=online、receiving=active、history=complete、failures=[]，52 个群、9 个置顶群、48 项待确认、0 个任务/通知。凭据只传子进程环境，没有写入报告、文档或仓库；未自动确认真实课程或日期。
- OCR 下一段仅做只读调查，未实现：图片 AssetId 可由本机 NapCat `get_image` 获取本地路径，现有 worker 支持 UTF-8 JSONL，但附件持久库、队列和 C# worker 宿主仍缺。已有路由回执不会因 ProcessingVersion 提高而重新路由；不得删除回执重跑首轮，以免覆盖完成、忽略、拒绝及人工修正。图片独立消息形成待确认需接续 M6 受保护再分析；附件证据及既有详情可先推进。
- 下一步真实课程/日期由用户在界面核对，验收消息到任务、卡片查看/完成；OCR 队列、M6、自启动及多屏/睡眠/Explorer 恢复仍未完成。本机仍找不到 `maintain-context-hygiene/SKILL.md`，按项目规则人工核对需求、交接、实现、发布路径及实际报告；不删除文件或修改无关文档。

### 上一段正式桌面卡片首段（已实现、发布及限定范围验证）

- 开发基线 `dc3eced`，分支 `codex/m3-integration-probes`。需求、第 3/6 节有效结论、设计正文及总计划已改为两卡片；取消全部 Windows 系统通知弹窗和声音及相关设置、点击/权限/定时/汇总验收，普通 App 通知、原文/证据、取消记录和旧计划/台账继续保留。
- 产物：`src/MessagePurify.Windows/App/DesktopCardHost.cs`、`DesktopCardProjection.cs`、`DesktopLayoutStore.cs`；回归位于 `tests/MessagePurify.Windows.Tests/DesktopCardTests.cs`、`DesktopLayoutTests.cs`、`tests/app_desktop_test.py`，桥、正式页面、原生 SmokeCheck、启动入口及相关测试同步更新。两张独立 WPF 卡片只显示当前账号 active task/pending review，按优先级和有效日期/时刻稳定排序；查看与完成复用核对账号/依赖版本的正式入口。当前主题及减少动画偏好同步，内容刷新保持原布局；提醒为空显示“暂无待办”，待确认为空隐藏。
- 布局保存于正式数据根 `%LOCALAPPDATA%/MessagePurify/App/desktop-layout.json`，schema 1，两个卡片位置/尺寸以 DIP 原子保存；首次左右摆放，读取时按当前 DPI/边界约束，坏文件或未知 schema 保留原文件并提示。修正 DPI 重挂载分支为只 Load/Clamp，不自动 Save，防止默认回退覆盖坏文件；扩展既有五项布局测试，重复改变边界/scale 读取仍保留原内容。标题拖动复用已验证几何，右下角缩放使用实际 WPF Thumb 事件；本轮未做物理鼠标拖动，多屏、Explorer 重启和睡眠恢复尚未验收。重挂载检测代码已写入，未把长期恢复记为通过；本轮变化标记与可读历史已接入，Native Smoke 21 项已通过。
- 单次生产差异审查发现 P2：时钟跨过截止/开始或只有日期的午夜时，日期优先级不变仍为 P0，旧 render signature 未包括 past，标签不刷新。同一 `now` 的 `IsPast` 已纳入 signature 与 Render；新增三项专项先红 0/3、退出 1，修复后 Windows 完整 Release 141/141（原 138 加 3）退出 0。没有重复整体审查循环。
- 实际执行 `.tools/dotnet/dotnet.exe restore src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --locked-mode`、`restore tests/MessagePurify.Windows.Tests --locked-mode` 均退出 0；`test tests/MessagePurify.Windows.Tests --no-restore --configuration Release` 141/141、`test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release` 375/375，均退出 0。Node 七文件命令 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs tests/application-ui.test.mjs tests/application-launcher.test.mjs` 最终 63/63（文档 27、Chrome 24、入口 3、其余 9）退出 0。初轮 61/63 因测试只模拟系统主题而未固定减少动画；当前系统 motion=true，测试 CDP 固定 `no-preference` 后通过。Core/OCR/demo 未改，本轮沿用上段 Core 301/301、OCR 25/25、demo 108 的实际结果，不记为本轮重跑。
- 最终 `.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-desktop` 退出 0，无警告/错误。新产物 `--smoke-test --report .tools/desktop-validation/native-smoke-final.json` 进程退出 0、success=true：snapshot、complete、undo-complete、explicit-date、accept-review、native-bridge、reopen、desktop-data、desktop-theme、desktop-open-item、desktop-complete、desktop-layout、pending-hide 共 13 项通过；`.tools/desktop-validation/native-smoke-final.png` 和 `native-smoke-final-cards.png` 已实际查看。此为临时合成账号，不替代真实 QQ 课程和任务验收。
- 实际 `.tools/ocr/venv/Scripts/python.exe tests/app_desktop_test.py --executable .tools/published/app-desktop/MessagePurify.exe` 退出 0，枚举本次进程两个卡片子窗：parentClass=`SHELLDLL_DefView`，child/noActivate=true、topmost=false，任务可见、空待确认隐藏。对旧 `.tools/published/app-reminders/` 的同项检查曾真实失败，证明旧版无正式卡片。默认入口已改为 app-desktop；独立 PowerShell 执行 `packaging/app/Start-MessagePurify.ps1 -CheckOnly` 后核对实际 ExitCode=0、Ready/AuthenticationReady=true，报告 `.tools/desktop-validation/launcher-check.json`；UTF-8 输出再次核对发布路径完全匹配。旧正式窗口实际仍运行 `.tools/published/app/MessagePurify.exe`，QQ 保持运行；用户从旧版托盘退出后再双击入口加载新版，两个旧发布目录均保留。
- 设计第 12 节仍有 31 项验收，总计划逐项映射；旧通知竞争、投递和点击场景已替换为卡片刷新、当前状态、账号隔离和渠道边界。本轮未重测真实 QQ 数量，沿用上一段 9 个置顶群、40 项待确认两次结果；未自动确认课程或日期。下一步真实课程确认及消息到正式任务/卡片操作验收，再推进 OCR 队列、M6 修复、自启动和剩余完整功能；M3 和 M8 总体不标为完成。
- 本机两个技能目录仍未找到 `maintain-context-hygiene/SKILL.md`，已按项目规则人工对照需求、交接、设计、总计划、实际源文件、发布产物和报告，未删除数据或修改无关文件。七份交付文档同步完成；文档接续順序措辞检查曾为 26/27，补齐有效 HANDOFF → requirements 规则后最终 Node 七文件 63/63、退出 0；diff/staged check 退出 0，已用对应 Git commit 保存并核对工作区干净，最终提交标识以日志为准。

### 暂停接续记录（2026-10-12）

以下至“已确认的真实应用路线”前的接续、实现与探针记录均为当时范围的历史；旧“下一步”不覆盖本轮结果及第 6 节。

- 用户曾要求“先把目前的工作保存好，明天继续”；已保存并暂停。2026-10-12 用户明确要求“继续我们的工作”，暂停已结束，恢复下一段候选路由。
- 实现基线提交：`80274aa`（正式事项与待确认生命周期）；分支：`codex/m3-integration-probes`。保存前已核对工作区干净，验证结果见下方，真实 QQ 和正式界面接入状态不变。
- 当时恢复工作先读 `docs/HANDOFF.md`，再读 `docs/requirements.md`，核对 Git 和实际文件；随后已推进正式卡片首段，当前下一步以第 6 节为准，不重复询问已确认方案。

### 历史：持久化提醒第一段（旧通知方案的设置、计划、可控投递与最终发布通过）

- 产物：`src/MessagePurify.Infrastructure/Reminders/ReminderModels.cs`、`AccountReminderService.cs`；`tests/MessagePurify.Infrastructure.Tests/PersistedReminderTests.cs`、`WorkspaceReminderTests.cs`；工作区、正式桥及页面提醒设置、原生 `SmokeCheck.cs`，实施计划见上方链接。账号目录新增 `reminders.sqlite3` schema 1，原 messages/tasks/routing 三库 schema 1 不变，损坏/未知结构/账号错配不清空重建。
- 正式任务详情保存默认、提前小时、前若干天某时刻和关闭提醒，配置同时核对任务和计划版本，操作 ID 重试幂等。精确时间默认提前 24/2 小时，日期默认前日 12:00；提醒设置不修改正式时间或精度。延期按时间版本重算，内容变化不重建节点；小时规则降为日期时保留原选择、临时默认和 NeedsReview；关闭选择不重新打开。图片建议仍须用户明确确认才能产生正式计划。
- SQLite 保存节点和投递台账，外部调用前事务落盘 `sending`。可控 sink 验证时间节点、错过节点一次恢复汇总、未知结果不逐条重放、明确失败最多三次跨重启重试、权限阻挡不循环提交。未进入外部提交的 `time pending` 恢复时合入一次 recovery；`recovery pending` 接续原 ID 并吸收本次其他到期有效成员。已截止/开始、完成、取消、归档或关闭不投递，只有日期时当天仍有效。
- AccountWorkspace 同一锁跨用户操作、路由、提醒配置及外部提交；变更先发生时不提交旧计划，提交先发生时后续变更清理旧条目。已提交汇总任一成员失效可整条移除，其他成员已发台账保持；Remove 失败保留 ID 和错误供重试，不额外重发摘要。
- 唯一整体审查实际复现 P1：失败 recovery 中 A 完成/延期，使有效 B 永久停在 failed。新增四项真实 SQLite 测试先红 42/46、退出 1；持久化原始未成功成员，按当前有效未成功节点重试，保留原 ID 与三次预算后绿 46/46。完成撤销可恢复原未成功成员，已成功成员不重放；成员变化及重启不重置预算。root 再运行审查窄复现 `.tools/dotnet/dotnet.exe run --project .tools/reminder-validation/review-repro/review-repro.csproj --configuration Release` 退出 0，complete/postpone 两路均只提交有效 B 一次，B 已到期节点为 submitted；不再开启整体审查循环。
- 实际最终回归 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release`：375/375（提醒服务 46、工作区提醒 24），退出 0。本段修复前已执行 Windows Release 96/96（桥新增 19）、Core Release 301/301；OCR `pip check` 通过、unittest 25/25；`node tests/prototype-qa.mjs` 108 项；Node 七文件命令 59/59（文档 24、正式 Chrome 23、入口 3、其余桥/打包 9），均退出 0。除提醒服务外的代码未因 P1 修复变化，不重复无关测试。
- 最终 `.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-reminders` 退出 0、零警告/错误；新产物 `--smoke-test --report .tools/reminder-validation/native-smoke-final.json` 通过真实隐藏 WebView2 的 snapshot、complete、undo-complete、explicit-date、accept-review、reminder-settings、native-bridge、reopen 八项，success=true、进程退出 0。截图 `.tools/reminder-validation/native-smoke-final.png` 已查看；这是临时合成账号，不替代真实 QQ 任务验收。
- 默认双击入口已指向 `.tools/published/app-reminders/`，上一版 `.tools/published/app/` 保留。最终独立 PowerShell 进程运行 `packaging/app/Start-MessagePurify.ps1 -CheckOnly`，WaitForExit 后读取 ExitCode=0，Ready/AuthenticationReady=true；原正式应用进程仍运行旧版，QQ 仍运行。要加载新版，用户从旧版托盘退出后再双击入口。初次验证包装错误地读取旧 `$LASTEXITCODE` 曾退出 1，原生检查本身退出 0；以后 WinExe/脚本进程均核对其真实退出码。
- 此历史版本页面明确显示“系统提醒尚未开启”；当时 Windows 实际投递及后台循环尚未接入，未发送 QQ/Windows 测试通知。当前已取消 Windows 时间节点、批次汇总、新发现/变更、权限恢复、五分钟断线和通知点击；旧 ReminderPlanner、SQLite plans/ledger 保留。后续推进正式桌面、自启动、OCR 队列、账号隔离和真实验收。
- 需求、交接、测试及应用说明已同步；实际 `node --test tests/project-rules.test.mjs` 为 24/24、退出 0，新增提醒进度/存储格式/系统未接入边界检查；`git diff --check` 退出 0。maintain-context-hygiene 两个本机目录仍缺 SKILL.md，已人工对照有效需求、计划、实际文件和报告；没有删除文件或修改无关文档。本段由对应 Git commit 保存，提交标识以日志为准。

### 上段正式应用首段（代码、合成闭环及真实只读宿主通过）

- 产物：`src/MessagePurify.Infrastructure/Application/ApplicationModels.cs`、`AccountWorkspace.cs`、`NapCatMessageHost.cs`；`src/MessagePurify.Windows/App/` 的正式 WPF 项目、桥、窗口、运行时和 `ui/`；双击入口 `packaging/app/Start-MessagePurify.cmd`，自包含产物 `.tools/published/app/MessagePurify.exe`。入口没有样例任务，关闭隐藏托盘，托盘可打开/退出，重复启动唤起已有窗口。
- AccountWorkspace 在同一锁内完成消息、路由、用户操作和快照，来源正文读原消息，课程/群关系与已读版本保存在账号目录 `workspace-settings.json`。原子替换失败保留旧文件；原有三库 schema 1 不变。正式事项操作复用生命周期，带账号、操作 ID、依赖版本、撤销来源 ID及关联事项版本；图片建议只填草稿，显式确认才保存。
- 页面沿用 demo 视觉；任务/待确认/通知/完成记录、搜索/课程/状态筛选、来源正文、日期确认、完成/撤销、归档恢复、忽略/撤销、拒绝建议可操作。初始有待确认则优先显示；群课程建议由取得的名单提出，确认影响新消息，旧普通待确认不重放。完整课程修复仍是 M6，可信发送者和机器人设置未接入，课程确认不授予发送者可信身份。主题/减少动画使用浏览器偏好，事项全部来自 SQLite。
- WebView2 只允许固定本地 HTTPS 页面及资源，桥拒绝外部来源、未知命令、重复字段、数字化 ID、错误版本/日期等；拒绝外部导航、新窗及权限，不把 Token 给页面。AppRuntime 从登录接口确定首个账号，保存后不自动切换，重连账号不符暂停。
- NapCatMessageHost 每个事件和历史页解析正文前核对当前置顶，历史页返回后再核对。实时原消息立即入库，历史期间暂缓路由，读取结束后按原发送时间处理；定期复核 QQ/账号/群名单/置顶，无事件也同步范围。自动重连及跨连接去重有模拟检查；分页按原返回游标、七天边界、100 页上限，无进展/空页/游标错误保留部分失败。同步游标/批次关闭未持久化，首次/重连重新尽力读取，不声称完整 M5。
- 首轮真实检查：当前普通 QQ 在运行但 3001 未监听，`--read-only-check --seconds 5` 退出 1、`connection-unavailable`；用户明确回复“我已退出 QQ，可以继续”后启动已准备 Framework，3001/6099 就绪。新进程未继承 Token 导致 `api-1403`，通过当前本机配置只向进程环境传入凭据后成功。正式启动入口已支持环境变量优先、唯一启用 loopback:3001 配置兜底；配置歧义停止，凭据不输出、不入 Git。
- 真实只读正式宿主：发布程序 `--read-only-check --seconds 20 --report .tools/app-validation/read-only.json` 退出 0，transport connected、QQ online、receiving active、history complete、失败列表为空，群名单 52、事项 40。第二个进程 `--seconds 10 --report .tools/app-validation/read-only-restart.json` 退出 0，9 个置顶群、40 项待确认、0 个正式任务/通知，与上轮相同。课程和图片日期均未擅自确认；没有发送 QQ 或 Windows 通知。本次有边界的历史读取通过，不代表持续实时新消息或独立准确率/长期稳定性验收。
- 合成真实 WPF/WebView2：自包含正式产物 `--smoke-test --report .tools/app-validation/native-smoke.json` 最终退出 0，snapshot、complete、undo-complete、explicit-date、accept-review、native-bridge、reopen 七项通过；截图 `.tools/app-validation/native-smoke.png` 已查看。最初接受测试调用 `review-form.requestSubmit`，页面没有该表单提交入口而超时；页面已补 Enter 同入口，检查也使用固定确认按钮，最终通过。不以合成账号代替真实用户课程和任务验收。
- 审查实际复现：原文“逾期不收”任务当天已为 P0，用户修改为更晚日期后优先级降为 P3，根因是从已饱和的等级反推后果。共享 `PriorityCalculator.HasClearConsequence` 保留匹配行动正文中的明确后果，不改变原提取规则或库格式；先红后绿确认远期改为 P2，否定后果/同消息其他行动不误加级。
- 实际 Release 命令 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release` 为 305/305（工作区 30、宿主 24）；Core 同参数 301/301、Windows 77/77。`.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app` 退出 0，零警告/错误；新项目 lock file 已写入。
- OCR `pip check` 通过、`-m unittest discover -s tests -p ocr_worker_test.py` 25/25；`node tests/prototype-qa.mjs` 108 项通过。新正式页面 `node --test tests/application-ui.test.mjs` 18/18，启动入口 `node --test tests/application-launcher.test.mjs` 2/2，包括中文/空格路径、缺产物拒绝、配置唯一性及不输出凭据。完整命令 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs tests/application-ui.test.mjs tests/application-launcher.test.mjs` 最终 53/53，退出码 0。
- 已通过正式启动入口打开“消息提纯”真实账号窗口，收尾时核对窗口仍运行、OneBot 3001 和 WebUI 6099 仍监听；QQ 保持运行，用户可查看待确认和群课程建议。尚未代用户确认课程或真实任务。
- 收尾同步完整 Node 结果、窗口状态及历史记录范围后，实际执行 `node --test tests/project-rules.test.mjs`，24/24、退出码 0；`git diff --check` 退出码 0。此次收尾仅改文档，不重复运行已通过的 C#/OCR/浏览器全量检查。
- 剩余与下一步：真实课程建议人工确认、连续实时及消息到任务验收；持久化提醒调度/在途协调/Windows 变更通知；正式桌面卡片、自启动、OCR 队列、手动新增/完整编辑、通知转任务、M6 修复、机器人/可信发送者设置、合并拆分/备份恢复和完整账号切换仍未完成。
- 上下文核对：maintain-context-hygiene 两个本机目录仍缺 SKILL.md，按项目协作规则人工对照当前需求、计划、产物与真实运行报告；未删除项目文件或修改无关文档。上述产物由本次 Git 提交保存，标识以提交后日志为准。

### 上段可靠关联变更与保守消息顺序（`6ee0735` 本地 API 与持久化验证通过）

- 产物：更新 `src/MessagePurify.Infrastructure/Processing/AccountCandidateRouter.cs`、`Tasks/TaskLifecycleService.cs`，新增 `tests/MessagePurify.Infrastructure.Tests/LinkedChangeRoutingTests.cs` 并扩充 `CandidateRoutingTests.cs`。新入口 `ProcessAndRoutePending` 从原消息顺序逐条提取与路由；旧 `RoutePending` 可处理预提取候选，但不越过更早未提取消息。
- 每条消息在 tasks 主库附加 inbox/routing 的同一事务中读取最新 KnownTasks、保存提取/处理版本、写正式变更/证据/操作/本地通知及回执；失败全部回滚。KnownTasks 完全替换调用者旧快照，只使用当前群 TrustedSenders 授权高影响变更，ConfirmedPublisherId 不由历史发送者猜测；真实关系获取与持久化仍待正式宿主。
- 可靠 Cancel/Postpone/NewRequirement/Remind 自动更新同一事项，时间手动保护冲突转待确认，取消不受时间保护阻挡。已完成事项的可靠新行动恢复待办并保留完成历史；已归档/已取消的新行动仍待确认。自动新要求保留稳定原标题，完整新要求留在原消息及操作历史。Remind 仅补证据，自动和手动接受均不使用旧候选期限；用户显式传入 confirmedTime 才修改正式时间。
- 仅尚未路由的 Pending/Change 关联行动在课程仍匹配时重新核对；已有普通 Pending/Task/Notice 不升级，课程撤销/变更使旧可靠候选转待确认。已有消息回执阻止更高提取版本重放；完整 M6 关联修复仍未实现。
- 三库 schema 1 不变，rollback journal 限制继续有效。使用 task_evidence LEFT JOIN inbox.messages 的最大原时间保守协调；除本消息外任何证据缺少时间、迟到旧变更或同秒顺序不明都保留关联待确认，不凭 messageId/rowid 判断同秒先后。尚未完整支持 real_seq 或跨群原始转发顺序，最大证据时间也可能使旧有效变更需要人工核对。
- 实际专项命令：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release --filter 'FullyQualifiedName~CandidateRoutingTests|FullyQualifiedName~LinkedChangeRoutingTests|FullyQualifiedName~TaskLifecycleTests'` 为 128/128（候选 36、关联 30、生命周期 62），退出码 0。最小取消行为红测先确认仍 Active，新入口红测确认 CS1061 缺方法；实现后通过。
- 本轮先红后绿的额外修复：通知先前记录提取前版本，raw 与预提取分别实际复现 0/1 而应为 1/2；改为候选处理结束后按最终提取版本保存通知及回执。审查发现并实际复现旧催交建议在新延期后接受会将 10/20 回退 10/13；共用 AcceptLinked 现只在明确确认时间时改变催交正式时间，完成/撤销字段不受影响。
- 完整回归：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release` 最终 251/251；同参数 `tests/MessagePurify.Core.Tests` 296/296、`tests/MessagePurify.Windows.Tests` 57/57。`.tools/ocr/venv/Scripts/python.exe -m pip check` 通过，`-m unittest discover -s tests -p ocr_worker_test.py` 25/25；`node tests/prototype-qa.mjs` 108 项，均退出码 0。
- 文档/打包实际命令：`node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs` 32/32（文档 23、桥 4），退出码 0。首轮旧 Change 仅待确认的正文及断言已更新为当前自动应用，上一段结果只作为历史保留；新增来源授权、顺序及正式宿主边界回归。
- 第四段保存时的剩余项：基础界面和正式只读消息宿主已在本轮接入（见上方）。提醒计划与串行投递及变更一次 Windows 通知、OCR 队列、普通通知转任务、手动新增/完整编辑与非时间字段保护、M6 修复、合并拆分和备份恢复仍在后续；第四段没有 QQ 发送或 Windows 测试提醒。
- 上下文核对：maintain-context-hygiene 两个本机目录此前均缺 SKILL.md；已按项目规则人工对照有效需求、计划、生产文件、测试与真实命令，不删除项目文件或修改无关文档。

### 上段消息候选首轮自动路由（本地 API 与持久化验证通过）

- 已写入 `src/MessagePurify.Infrastructure/Processing/AccountCandidateRouter.cs`、`CandidateRoutingModels.cs` 和 `tests/MessagePurify.Infrastructure.Tests/CandidateRoutingTests.cs`。新增每账号 `routing.sqlite3` schema 1；messages/tasks 结构不变。Task 新建事项，Pending/Change 待确认，Notice 保存完整本地记录，Ignored 仅写处理回执；当前置顶群、账号/群、机器人排除和课程排除再次检查，暂时排除不写回执。普通清楚新任务无需发布者在可信名单，与本地提取器契约一致；高影响变更仍保留来源检查。本段不代表正式 QQ 宿主或系统通知投递已接入。
- 单条消息跨库事务保存全部候选、操作和回执，失败整条回滚；拒绝 WAL/内存/关闭日志模式。消息级回执与完整字符串消息键/候选序号生成的确定性记录身份防止重启、重复消息和更高提取版本重放；通知保留原候选、证据、消息键、处理版本、来源、批次及原发送时刻。生命周期共用原事务内部逻辑，不复制状态变更。
- 最终只读审查发现并修复：提取后课程确认撤销或变更，旧 Task/Notice 均降级为待确认，保留原字段与证据，将课程及对象判断标记为不可靠；不升级原 Pending/Change。四项新增测试先实际运行失败，修正后路由及生命周期专项 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release --filter 'FullyQualifiedName~CandidateRoutingTests|FullyQualifiedName~TaskLifecycleTests'` 为 95/95（路由 32、生命周期 62），退出码 0。
- 实际完整命令：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release` 为 217/217；同参数 `tests/MessagePurify.Core.Tests` 为 296/296，`tests/MessagePurify.Windows.Tests` 为 57/57。`.tools/ocr/venv/Scripts/python.exe -m pip check` 通过，`-m unittest discover -s tests -p ocr_worker_test.py` 为 25/25；`node tests/prototype-qa.mjs` 为 108 项，均退出码 0。
- 文档/打包实际命令：`node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs` 为 31/31（文档 22、桥 4），退出码 0。新增路由交付与当前状态回归，区分本地通知入库、可靠变更待确认及 M6/正式宿主剩余范围。
- 第三段保存时的剩余项：可靠关联变更与保守消息顺序后来已在第四段完成（见上方）；M6 重新分析修复协调、普通通知转任务、手动新增/完整编辑、非时间字段保护、提醒计划与系统投递、OCR 队列、合并拆分、备份恢复和正式应用宿主仍未实现。已完成/忽略等人工处理不会因首轮路由重播而丢失。
- 上下文核对：本机两个 maintain-context-hygiene 目录仍无 SKILL.md；按项目规则人工对照有效需求、计划、源码、测试与实际命令结果，没有删除项目文件或改无关文档。

### 上段正式事项生命周期（本地 API 与持久化验证通过）

- 产物：`src/MessagePurify.Infrastructure/Tasks/TaskStoreModels.cs`、`TaskLifecycleService.cs` 及 `tests/MessagePurify.Infrastructure.Tests/TaskLifecycleTests.cs`。同账号目录新增 `tasks.sqlite3`，既有 `messages.sqlite3` schema 1 不变；账号归属、未知版本及无版本非空库均检查，失败保留数据。
- 已实现：可靠 Task + 正式 Time 新建事项，Pending/Change 保存待确认；接受/拒绝、完成/取消/归档/恢复、完成 1 小时及忽略 24 小时撤销。关联取消/延期/新行动接受时检查 review 和 task 两个版本、更新原事项并追加去重证据；拒绝建议保留原任务，Ignored 须先撤销才能接受。仅保护手动确认时间，不声称完整逐字段编辑已完成。
- 操作 ID 保存命令、依赖版本及参数，同请求重放返回最新记录，不重新执行；同 ID 不同请求冲突。规范化完整候选内容唯一键防止同一候选换 ID 重复建事项，证据重排/重复不改变键；不同内容候选的重新分析协调仍待后续。撤销凭据包含原操作 ID 与截止时间，旧入口不能撤销新一轮操作；当前完成/忽略/归档字段在撤销或恢复时清除，历史由操作前后快照长期保留。
- 图片 SuggestedTime 留在候选，只有显式传入 confirmedTime 才成为受保护正式时间；直接有正式 Time 的候选使用正式值。仅日期不虚构钟点。关联取消无须另填时间，取消不受时间保护阻挡，不把已取消任务恢复为待办。
- 实际命令：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release` 为 184/184（新增生命周期 61）；同配置 Core 296/296、Windows 57/57。`.tools/ocr/venv/Scripts/python.exe -m pip check` 通过，`-m unittest discover -s tests -p ocr_worker_test.py` 25/25；`node tests/prototype-qa.mjs` 108 项，均退出码 0。最初红测仅缺生命周期实现；首次生产编译的泛型推断错误已按 CS0411 修正，随后两项参数错误类型与约定对齐；审查后补齐完成后取消/归档的当前时间戳清除，新增三项检查确认操作历史仍保留。
- 文档/打包验证：实际运行 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs`，30/30、退出码 0。首轮两项失败分别因测试说明未明确宿主未联调及精简总览丢失 QQ 登录与 OneBot 区分，补齐真实边界后通过。
- 本段保存时的剩余项：候选路由及可靠关联变更后来已在第三、四段完成（见上方）；普通通知转任务、手动新增/编辑及非时间字段保护、提醒计划与系统投递、OCR 队列、合并拆分、备份恢复和正式应用宿主仍未实现。
- Git 收尾：本段 11 日开始，提交时间为 12 日凌晨（北京时间）；验证后保存到当前分支，提交标识以 Git 日志为准。暂存 diff 检查退出码 0，提交后工作区为空。
- 上下文核对：两个技能目录的 maintain-context-hygiene 均只有 agents 目录、没有 SKILL.md；按项目规则人工对照需求、计划、源码、测试与实际命令结果，没有删除项目文件或改无关文档。

### 本次置顶群范围调整（代码及真实名单/历史限定验证通过）

- 用户新增明确规则：只读取置顶的群聊消息，替代此前所有群聊范围；实时、历史和补漏统一受限。需求已确认，过滤代码及只读桥已写入并加载真实 QQ；旧全群报告仅是规则调整前的历史结果。当前规则下真实名单与分页/重连限定范围通过，正式自动补漏和长期实时验收仍待完成。
- 本机 NapCat v4.18.37 的 `get_recent_contact` 会读取最近消息但不返回置顶标志，不能据此判断置顶；自定义插件被该版本的官方插件白名单拒绝，未绕过白名单或冒充官方插件。诊断插件未加载，已删除本轮临时文件及启用标记；曾注册空插件管理器，未发送 QQ 消息。改为项目的 Framework 加载入口注册唯一读取 action `messagepurify_get_pinned_groups`，从原生快照只选取群类型/置顶标记/群 ID，不请求最近消息的消息读取接口。
- 产物：`packaging/napcat/pinned-groups.mjs`、`nativeLoaderPinned.cjs`、`Prepare-PinnedGroups.ps1`；Framework 入口在启动时生成带额外导出的 `messagepurify-napcat.mjs` 副本，保留官方 `napcat.mjs` 和 `nativeLoader.cjs`。真实快照 139 项会话、排序列表也为 139，群类型 62 项（包括非当前已加入群的最近会话）；群的 `topFlag` 为数字，实际 1 的 9 项、0 的 53 项。代码只接受 0/1，校验根结果和内部结果、排序/变更列表长度一致及不足 10000 上限；未知标记或快照不完整即失败，不凭顺序/群名推断。首次 `isTop` 假设已由实际 `topFlag` 替代。
- `OneBotClient` 读取桥返回的账号和群 ID 集合；`ProbeRunner` 初始筛群、每页及重试前重新核对，每个实时群事件解析正文前重新核对，重连重建范围。非置顶事件只计排除数量，不解析正文/消息段或存盘；NapCat 通道本身仍可能推送其他群事件。`--history-groups all` 也仅表示所有置顶群。状态读取或格式失败停止本次连接，不回退到全群；不自动清理已有事项或证据。
- 模拟回归：Infrastructure Release 最终 78/78；覆盖混合群/空名单、非置顶坏正文提前排除、首屏/分页/实时处理时取消置顶、重连更换范围、未知状态及账号错配。桥测试 4/4，使用真实版本的 `{get, call}` 结构并检查原接口委托不变。首次接入报 `context.actions.set is not a function`，根因为该版本仅暴露读取包装器，不是 Map；已改为只扩展 `get` 的唯一项目 action，保留原调用方法及其他读取 action。
- 用户明确回复“已退出”后启动项目入口；定位注册问题和原生字段期间，核对路径后重启本轮启动的 QQ 进程继续验证，无需再次扫码。当前 3001/6099 同属于 QQ PID 38900，项目读取桥 ready，QQ 在线。旧进程的新探针先返回 1404、历史尝试及消息均为 0、退出码 1，验证接口不可用不回退；报告 `.tools/m3-results/qq-pinned-bridge-unavailable-20261011.md`。
- 真实执行 `python .tools/m3-results/run-current-framework-probe.py .tools/m3-results/qq-pinned-run-20261011/MessagePurify.QqProbe.dll --history-groups all --history-count 100 --history-days 7 --history-pages 20 --reconnect-rounds 1 --listen-seconds 15 --output .tools/m3-results/qq-pinned-history-reconnect-20261011.md`，22:35:24（北京时间）开始，退出码 0。52 个已加入群中的 9 个置顶群各读两轮，18/18 到达 `date-boundary`，历史计数 686、去重 341、重复 345、账号核对 2/2、重连 1/1、重试 0、实时群事件 0、重连后首次发现 0。其余 43 群未请求历史；没有真实实时群样本，不能把模拟实时排除或受控重连当作长期接收/非预期断网/正式补漏验收。到达日期边界仍不保证 QQ 服务器的历史完整。
- 本轮全部自动回归：Core Release 296/296、Infrastructure Release 78/78、Windows Release 57/57、OCR `pip check` 与 unittest 25/25、Node 五文件 29/29（文档 20 项、桥 4 项）、demo 108 项，均退出码 0。实际命令按 `tests/README.md`，新桥准备/语法检查亦通过。私人配置、运行副本、日志和脱敏诊断/报告均留在 `.tools/m3-results` 或 NapCat 忽略目录，不提交账号、群名或正文。
- 本机未找到 `maintain-context-hygiene` 技能，按项目规则人工核对当前需求、交接、设计/计划、源码、测试及真实报告；没有删除项目文件来缩短上下文。该阶段之后已完成 M4 四段本地能力；下一步正式界面与宿主联调，保留真实动态置顶/长期接收及多屏/恢复等未验收项，不重复静音提醒及桌面基本测试。

### 本次受控重连及跨连接历史去重

- 用户要求继续，沿用 M3 范围推进连接恢复验证；当前 OneBot 3001/6099 正常，不要求再次退出或扫码。只断开探针自己的 WebSocket，QQ 客户端继续运行，没有修改 QQ 或系统设置、发送 QQ 消息或重复 Windows 测试提醒。
- `ProbeArguments` 增加 `--reconnect-rounds 0..2`（默认 0）。每轮读取历史并监听指定秒数，正常结束后等待 2 秒重新连接；重新核对第一次读取的账号，账号变化立即停止。每次从最新页开始，分页游标与页内去重集合重新建立，报告的消息键去重跨连接保留。取消后不继续连接，历史失败在后续成功后仍保留。
- `ProbeReport` 新增连接尝试/账号核对、重连完成/请求、重复消息、重连后首次发现消息键的统计；群结果与失败按连接序号区分，群序号只属于该轮列表。首次发现不证明消息恰好在断开期间发送。报告不包含账号、群名、正文、Token 或游标。
- 新增 `tests/MessagePurify.Infrastructure.Tests/ReconnectProbeTests.cs`，loopback helper 支持连续连接与账号变化。模拟回读新增消息、跨历史/实时/连接去重、每轮新游标、变更账号不读新历史、先前失败不被后续成功覆盖，以及取消后不再连接。初跑 1 项失败源于合成事件缺少 `post_type`（被客户端按响应解析），补齐正确协议字段后 Infrastructure Release 67/67、退出码 0，没有修改协议校验去迁就坏数据。
- 真实执行 `python .tools/m3-results/run-current-framework-probe.py .tools/m3-results/qq-reconnect-run-20261011/MessagePurify.QqProbe.dll --history-groups all --history-count 100 --history-days 7 --history-pages 20 --reconnect-rounds 1 --listen-seconds 15 --output .tools/m3-results/qq-controlled-reconnect-20261011.md`。21:40:37（北京时间）开始，两次连接账号核对 2/2、主动重连 1/1；52 群各读两轮，82/104 到达边界（每轮 41/52），同 11 群每轮持续 1200，各重试一次，合计 22 次重试。历史计数 2206、去重 1098、重复 1108、重连后首次发现 0、实时群 0、其他事件 4。退出码 1 如实反映历史未补全；重连及跨连接去重限定范围通过，没有声称真实断开期间的新消息补回。
- 真实报告和独立 Release 运行副本在 `.tools/m3-results/qq-controlled-reconnect-20261011.md` 及 `qq-reconnect-run-20261011/`，均不提交私人配置或原始消息。模拟新消息补回与真实只读重连分别记录；没有进行 QQ/NapCat 重启、非预期断网自动恢复或跨进程持久化验收。
- 本轮完整自动回归：Core Release 296/296、Infrastructure Release 67/67、Windows Release 57/57、OCR `pip check` 与 unittest 25/25、四文件 Node 24/24（文档 19 项）、demo 108 项，均退出码 0；实际命令沿用 `tests/README.md`，Infrastructure 命令及真实探针命令已列在上方。`git diff --check` 退出码 0。最终只读核对 3001/6099 仍属于 QQ PID 10824。
- M3 继续实施：11 群历史可用范围、长期实时接收/通知稳定性、桌面恢复/多屏及布局持久化仍待验证；正式数据库、自动重连服务、消息转事项与提醒联调尚未完成。下一步准备可独立推进的本地存储和消息处理代码，继续保留未验收项目，不重复已经通过的静音和桌面基本操作。
- 按项目规则人工核对交接、需求、计划、源码、测试与报告；本机仍未找到 `maintain-context-hygiene` 技能，没有删除文件或改无关文档来缩短上下文。

### 本次静音提醒验收与真实历史分页

- 本机静音提醒于 20:33:12（北京时间）提交，通知 ID 36667；用户确认“看到了，点击能打开”。20:33:15 点击记录与发送 Probe ID 一致，协议激活、账号匹配、无错误。发送报告 `.tools/m3-results/silent-presentation-20261011.json`，点击报告 `%LOCALAPPDATA%/MessagePurify/IntegrationProbe/results/protocol-0fb7539916ae42e0b5dd7bfb24a350aa.json`。使用 DLL 与安装相同的 `.tools/windows-probe-desktop-grip-fixed/` 发布副本；不重复提醒或声音验收，长期通知稳定性未验收。
- 用户退出普通 QQ 后，已恢复 `packaging/napcat/Start-NapCat-Framework.ps1`；3001/6099 属于 QQ PID 10824。0 群、0 秒只读基线为在线、账号核对、52 群、NapCat 4.18.37 / v11、退出码 0，报告 `.tools/m3-results/qq-framework-resume-20261011.md`。QQ 保持运行，没有要求再次退出或登录。
- 扩展 `tools/MessagePurify.QqProbe/` 参数、读取及报告：7 天窗口、`all` 群、每页/每群上限、一次失败重试；按发送时间及同秒数值 `real_seq` 选择最早消息的原样游标，短页继续、边界去重、账号/群核对、明确未补全原因，不记录账号/群名/正文/游标/Token。默认一群一页保留。
- 首次全群分页发现 3 群重复页。实际方向对照（状态/账号/群列表及 9 次历史读取）显示相同最早游标 `reverse_order=false` 均 0 新增，`true` 分别 99/96/85 条更早消息；据此修正 `src/MessagePurify.Infrastructure/NapCat/OneBotClient.cs` 的带游标请求方向，首次最新页不变。报告 `.tools/m3-results/qq-history-direction-20261011.json`，不是仅凭参数名推断。
- 最终真实命令：`python .tools/m3-results/run-current-framework-probe.py .tools/m3-results/qq-paged-fixed-run-20261011/MessagePurify.QqProbe.dll --history-groups all --history-count 100 --history-days 7 --history-pages 20 --listen-seconds 60 --output .tools/m3-results/qq-paged-history-fixed-20261011.md`。20:50:00（北京时间）开始，41/52 到达边界（39 日期边界、2 成功空页）、11 群持续 1200、11 次重试；历史计数 1103 含重叠，窗口内去重 1098、实时群 0、排除私聊 1、其他事件 6。退出码 1 如实反映 11 群未补全；原 3 群方向问题解决，无页上限/游标/无进展失败。到达边界不保证 QQ 历史完整，1200 不冒充成功空页。
- 新增 `HistoryPagingTests.cs`，扩展 loopback helper、参数测试及历史方向测试。方向断言先失败、修复后通过；实际重新运行 Core Release 296/296、Infrastructure Release 60/60、Windows Release 57/57、OCR `pip check` 与 unittest 25/25、demo 浏览器 108 项，均退出码 0。最终运行 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs`，23/23、退出码 0；静音验收和分页边界文档检查通过。
- M3 仍在实施：完整历史补漏、断线恢复、长期实时接收/通知稳定性、桌面恢复/多屏和布局持久化仍未验收，正式持久化/任务生成/提醒联调尚未完成。下一步处理真实历史可用范围及剩余集成项，不重复静音提醒和已通过的桌面基本操作。
- 本机仍未找到 `maintain-context-hygiene` 技能，按项目规则人工核对需求、交接、计划、代码、测试及真实报告一致，没有删除文件或为缩短上下文改无关文档。
- `git diff --check` 退出码 0；工作区只包含本轮分页代码/测试和相关文档，私人运行报告与配置仍在忽略目录。

### 本次桌面卡片不可见修复

- 用户反馈按 Win+D 未看到卡片，并提供截图：白色窗口是验证状态控制窗，不能作为卡片显示通过。原版报告 `.tools/m3-results/desktop-current-20261011.json` 和 `desktop-reproduce-20261011.json` 为 `attachedStructureOnly`、父窗 `Progman`、DPI 120，但窗口被桌面图标层遮住。
- 受控实验只重新挂载探针自己的卡片窗口；挂到外层 `Progman` 时捕获壁纸，改挂其 `SHELLDLL_DefView` 子窗后绘制两张卡片。`DesktopHost.cs` 已按此证据修正父窗选择，未修改 Explorer 或 QQ；`attachedStructureOnly` 仍只代表结构检查。
- 新增 `tests/desktop_attachment_test.py`，对真实发布产物按启动 PID 核对父窗、窗口可见标志、子窗/不激活/非置顶样式及物理边界，并正常关闭自己启动的测试窗口。实际运行原版 `.tools/windows-probe-silent`，退出码 1，复现父窗 `Progman`；发布 `.tools/windows-probe-desktop-fixed` 后用同一命令回归，退出码 0。Windows Release 57/57、退出码 0。
- 修复版已通过 `packaging/probe/Install-Probe.ps1 -PublishDirectory .tools/windows-probe-desktop-fixed` 更新当前用户安装，退出码 0，安装 DLL SHA256 与发布一致；没有覆盖正在运行的旧产物。运行 `--interactive-desktop --wait-seconds 600 --report .tools/m3-results/desktop-fixed-20261011.json`，父窗 `SHELLDLL_DefView`、DPI 120、`error=null`，窗口捕获 `.tools/m3-results/desktop-fixed-render-20261011.png` 显示左右两张合成卡片。用户要求“再来一遍我看看”，已重新启动同一修复版，最新报告 `desktop-fixed-resend-20261011.json`。
- 用户确认父窗修复版“看到了，拖动正常”，随后反馈“无法缩放，只能拖动位置”，且白色状态窗伴随卡片显示。实际鼠标诊断发现：在原 18 DIP 小灰块内拖动，尺寸从 850×375 变为 910×405，位置不变；离右下角 30 像素的区域拖动，大小没有变化。扩大手柄至 32 DIP，增加“↘”箭头及缩放提示；成功挂载时不再显示状态窗，失败处理入口保留。
- 扩展真实发布产物回归：成功挂载不能有可见的顶层状态窗；`--mouse` 在扩大后的手柄区域实际拖动，验证宽高分别增加 60/30 物理像素且左上角不变，并恢复指针。原版成功状态窗回归退出码 1；最终 `.tools/windows-probe-desktop-grip-fixed` 的 `python tests/desktop_attachment_test.py --executable .tools/windows-probe-desktop-grip-fixed/MessagePurify.Windows.IntegrationProbe.exe --mouse` 退出码 0。鼠标回归前短暂 `Shell.Application.MinimizeAll()`，结束后 `UndoMinimizeALL()`，未操作 QQ 消息。
- 最终 Release publish 与当前用户安装退出码 0，安装 DLL SHA256 与发布一致。最新运行报告 `.tools/m3-results/desktop-grip-fixed-20261011.json`，捕获 `desktop-grip-fixed-render-20261011.png` 显示箭头。用户确认“缩放正常，也没有白色窗口”，随后确认“正常，不会盖住其他软件”。桌面基本人工项（Win+D 可见、拖动、缩放、普通窗口遮挡及无多余状态窗）通过；程序报告的 `manualChecks` 是自动模板，用户验收以本段反馈为准。
- 桌面修复阶段实际完整回归：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release` 295/295、同参数 Infrastructure 46/46、Windows 57/57，OCR `pip check` 与 unittest 25/25、四文件 Node 22/22、`node tests/prototype-qa.mjs` 108 项，均退出码 0。后续手柄与状态窗只改 Windows 探针及说明，Windows 57/57 和真实鼠标回归再次通过。多屏、Explorer 重启、睡眠恢复、布局持久化和长期稳定性未验收，M3 继续实施；下一步静音呈现、学校通知日期角色确认及剩余稳定性/补漏验证。
- 已按项目规则人工核对需求、交接、源码、报告和剩余验收范围；本机仍未找到 `maintain-context-hygiene` 技能，没有为缩短上下文删除文件或改无关文档。

### 本次学校通知截图 OCR 实测与“交齐”规则补充

- 用户提供一张真实学校群通知截图（原图只保存在本机临时目录，不写 Git）。使用项目本地 RapidOCR worker 离线处理，图片 1828×674，识别 15 行，退出码 0，stderr 为空；原图未改写，网络 guard 未发现连接尝试。关键日期行最低置信度 0.98118，截图消息时间识别为 `2026/09/26 12:43`。
- OCR 正确识别出 2025-2026 学年、填写日期范围 9 月 26 日至 10 月 16 日，以及“10 月 12 日之前交齐”的截止表达。用户明确说明后者是班长要求同学交齐材料，提取为用户待办事项的截止建议；截图正文是学校通知内容，不是用户对编码助手或软件的操作指令。消息时间是元数据，9 月 26 日至 10 月 16 日是表格落款范围，不应直接作为提醒。
- 真实 OCR 的完整日期角色仍需确认：日期范围按 `MessageDateParser` 返回 `Ambiguous`；OCR 图片中的日期只能作为 `SuggestedTime`，不能直接建立正式提醒。原图及完整 OCR 文本仅在 `.tools/m3-results/user-school-notice-ocr*.{jsonl,txt,json}` 留存，交付文档不写姓名、QQ 号或全文。
- 这次实测暴露规则缺口：`GetAction` 原先不识别“交齐”，导致整图不能形成待确认候选。已补充“同学交齐材料”的事项识别；`RuleAction.Submit` 表示通知中要求用户完成的行动，只用于分类，不执行提交或回复。脱敏回归现名为 `ClassMonitorImageSubmissionRequirementKeepsDeadlineAsSuggestion`，发布者使用 `class-monitor`：单独 OCR 行得到 `Submit/Pending`，建议日期为 2026-10-12、正式时间为空，仍要求查看原图确认。没有把该截图直接写入测试。
- 实际回归：OCR `pip check` 与 `unittest` 25/25，新增后 Core Release 296/296，四文件 Node 22/22；原图 OCR 运行报告为 `.tools/m3-results/user-school-notice-ocr-summary.json`。这证明本地 OCR 和动作/建议日期边界，不证明正式应用已完成学校通知任务生成、日期人工确认或提醒投递。

### 本次“交齐”含义澄清

- 用户纠正：“交齐”是班长要求同学按期提交材料，不是对编码助手的指令。核对 `MessageRuleExtractor` 后确认现有规则仅返回事项候选，无提交材料或 QQ 回复逻辑；修正之前“提交动作”的表述，保留学生待办及截止日期识别。
- `RuleAction` 增加分类语义说明；脱敏图片测试改用班长发布者，并核对候选保留原消息证据、没有关联到已有事项、正式时间仍为空。需求与上方 OCR 结论已同步；正式应用联调验收范围不变。
- 实际运行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`，296/296；`node --test tests/project-rules.test.mjs`，17/17；`git diff --check`，均退出码 0。此次只改说明与回归测试，没有重跑 OCR、Windows 或 QQ 探针，也未发送通知或 QQ 消息。
- 本机未找到 `maintain-context-hygiene` 技能，按项目规则人工核对需求、交接、实际规则和剩余验收一致，没有删除文件。

### 本次取消声音提醒

- 2026-10-11 用户表示不需要声音提醒，原因是不一直坐在电脑前；第一版改为统一静音，删除声音开关及声音播放验收。此前按任务类别播放声音和总开关的规则由本条替代。视觉通知、通知中心记录、桌面卡片高亮及恢复运行时的一次汇总仍保留；安静时段的“暂未启用”入口沿用已确认规则。
- demo 已删除声音开关，提醒预览统一静音；加载旧演示存储时丢弃原 `sound` 字段，其他设置及事项保留。Windows 通知载荷固定 `<audio silent="true"/>`，不再提供声音参数或 `notificationSound` 人工验收项；`normal`/`silent` 入口名称兼容保留，实际均静音。
- 实际执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release`（57/57）、`node tests/prototype-qa.mjs`（108 项）、`node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs`（22/22），均退出码 0。浏览器回归覆盖考试/作业/活动静音、移除开关、旧声音偏好不恢复、其他设置与事项保留。
- 实际发布 `.tools/windows-probe-silent` 并通过 `packaging/probe/Install-Probe.ps1 -PublishDirectory .tools/windows-probe-silent` 更新当前用户安装，均退出码 0；安装 DLL 的 SHA256 与发布一致。本轮没有发送新提醒、读取 QQ 或重跑未改动的 Core/Infrastructure/OCR；静音通知呈现仍未进行新的人工验收。
- 需求及设计、计划、源码、测试及剩余验收已人工核对一致，未找到的 `maintain-context-hygiene` 技能继续按项目规则人工核对。正式应用仍尚未接入探针能力，取消声音不代表静音通知呈现或 M3 已验收。

### 本轮接续 M3 真实集成探针

- 用户要求继续，已从 `0c0893a` 进入 `codex/m3-integration-probes`，沿用已确认设计；[M3 详细计划](superpowers/plans/2026-10-10-integration-probes.md)已写入。M1／M2 基线实际重跑 Release 核心 295 项通过。
- 已只读检测普通 QQ 安装及进程（`D:/QQ`、版本目录 `9.9.36-53644`）。官方 Shell 入口依赖注册表而失败，已提供 `packaging/napcat/Start-NapCat.cmd` 直接指定 QQ 路径。用户确认退出普通 QQ 后，本轮启动 Framework 并自动登录；QQ 主进程加载 `napiloader.dll`，WebUI 6099 与 OneBot 3001 已监听。普通 QQ 登录不等于 OneBot 连接，实际只读连接结果单独记录。当前执行会话提权，通知须在支持的非提权环境实测；尚无 M3 完成结论。
- 用户已提供真实 QQ 截图并确认六处红框中的群均为学校相关群；名称通常正规，也有非正式名称，群名只辅助候选，不能据此排除。截图是群列表和私聊界面，没有完整学校通知正文及日期；可用于真实截图 OCR，不作为通知日期提取验收。原图及识别全文仅用于本机，不写 Git。
- 用户反馈担心接入后无法继续使用 QQ。已纠正此前关于 Shell 桌面聊天能力的说明：Shell 是无界面接管路线，同一账号不能与普通 QQ 并行登录。Framework 本轮已实际加载，用户从托盘打开后确认“聊天列表和窗口正常显示”；窗口打开期间同一进程的只读接口复测通过。启动日志曾出现 `updateWindowSizeProperties` 的 TypeError，长期稳定性尚未验证；没有确认的窗口显示故障，不盲目替换加载器或重新登录。
- M3 当前实际结果见[集成探针验证记录](verification/integration-probes.md)：本轮自动回归 Core 296、Infrastructure 46、Windows 45、OCR 25、Node 21、demo 107 均通过。前四次真实只读探针分别监听 30、60、60、300 秒，四次均退出码 0；群 52 个、历史成功 1/1、历史消息 7 条、实时群消息 0 条。第五次 180 秒探针实际收到 1 条实时群消息，退出码 0。不把接口通过替代实时接收、七天补漏、正式任务生成、日期人工确认或 M3 完成。高完整性进程按预期阻止通知；发布时补齐官方资源后，独立中等完整性进程注册并提交测试提醒成功。当时用户点击未观察到激活回调；本次专用 URI 修复后的用户实际点击通过，桌面卡片人工项已基本通过，仍未验收多屏、恢复和长期稳定性。本阶段已有提交 `fbef082`、`42c939a`、`3d87f4f`、`1fd1b1c`、`f980c51` 和 `4128526`。
- 本轮启动入口为 `packaging/napcat/Start-NapCat-Framework.ps1`，实际探针运行 `.tools/dotnet/dotnet.exe tools/MessagePurify.QqProbe/bin/Release/net10.0/MessagePurify.QqProbe.dll --listen-seconds 30 --output .tools/m3-results/qq-framework-current.md`，随后以 60 秒输出 `qq-framework-events.md`、`qq-framework-desktop-open.md`，均退出码 0。原始日志、配置备份和脱敏报告只在 `.tools/m3-results/`；Token 只保存在本机配置和子进程环境，不写入文档或 Git。
- 本轮更新文档回归：初次运行四文件 Node 测试 18/19，通过结果报告缺少明确“实时群事件尚未验收”正文而失败；补齐范围后重跑 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs`，19/19、退出码 0，`git diff --check` 退出码 0。本轮未改产品代码，也未重跑此前 Core/Infrastructure/Windows/OCR/demo 套件。已人工核对需求、设计、交接、报告和本机实际状态；下一步继续实时群事件与 M3 剩余人工验收。
- 上述接入检查已保存为 `4128526`。随后继续 Windows 通知验证，发现原权限检测只判断管理员组，不能识别 Basic User 进程仍保持高完整性；新增 `ProcessIntegrity.cs` 改为查询真实令牌完整性，并新增独立 `whoami` 回归，Windows 45/45、Release publish 和当前用户安装退出码 0。Basic User 复跑正确返回 `blockedElevated`；当时独立进程降到中等完整性后仍返回 `0x80070490`、没有通知 ID。后续已分别定位包身份与资源缺失并成功提交，见下文；未改系统策略或 QQ 进程。
- 通知检查后实际重跑 Core Release 295、Windows Release 45、OCR pip check 与 unittest 25、四文件 Node 19、demo 107，全部退出码 0。Infrastructure 初次构建因本轮仍在运行的 300 秒只读探针锁住 DLL 而退出码 1，代码未改；先执行同项目 `--no-build --no-restore --configuration Release`，46 项通过、退出码 0。监听正常结束后再执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release`，构建与 46 项均通过、退出码 0；没有关闭 QQ。四文件 Node 回归随后扩展为 21/21，新增通知资源提取和打包安装检查。
- 300 秒只读探针报告为 `.tools/m3-results/qq-framework-live-observation.md`，退出码 0、历史 7 条、实时群消息 0、其他事件 11、无失败。用户现已回答“刚收到新的群消息”；第五次探针复制运行产物到独立忽略目录，再实际运行 `.tools/dotnet/dotnet.exe .tools/m3-results/qq-live-run-20261011-feedback/MessagePurify.QqProbe.dll --listen-seconds 180 --output .tools/m3-results/qq-framework-user-feedback.md`，退出码 0，13:42:06（北京时间）开始、实时群消息 1、历史 7、去重后 8、无失败。实时群事件限定范围通过（观察到 1 条），不代表所有群、图片事件或长期稳定性通过。保持 Framework QQ 运行；当前用户实际点击通过，下一步继续静音提醒呈现、桌面人工操作、实时稳定性和历史补漏验收。
- 运行 `python .tools/m3-results/check-recent-group-traffic.py`，每群仅一页 5 条：52 群尝试、41 成功、11 接口失败、历史合计 178 条，近十分钟 2 条分属 2 群，最新时间 13:42:10；退出码 0仅表示诊断完成，不作为全部群历史通过。原统计在 `.tools/m3-results/qq-recent-group-traffic.json`，当时未定位失败原因；后续限定样本诊断见下文。群名、账号、正文及 Token 不输出、不提交；历史诊断不代替实时探针。
- 用户询问是否会往学校群发送消息，已核对源代码及既有 loopback 读取 action 断言。QQ 接入只读，不向 QQ 群或私聊发送消息；学校群通知及测试提醒不通过 QQ 发送，提醒采用本机 Windows 通知。需求及设计同步明确此边界，不修改 NapCat 本身提供的接口能力。
- 本次文档检查初跑 19/20，报告缺少明确的长期稳定性未验收状态；补齐正文并纠正设计、M3 计划及测试说明中的旧实时状态。随后加入通知资源提取和冷启动回归，实际重跑四文件 Node 测试为 21/21、退出码 0；实际运行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-build --no-restore --configuration Release --filter FullyQualifiedName~OneBotTransportTests`，5/5、退出码 0，精确断言探针只调用五个读取 action。本次未改产品源码，未重跑完整 Core/Infrastructure/Windows/OCR/demo 套件；已人工核对需求、交接、设计、计划及本机脱敏报告，不把一条实时事件作为长期或七天补漏验收。

- 本轮修正 Windows App SDK 2.5.1 自包含发布缺少 `Microsoft.WindowsAppRuntime.Insights.Resource.dll` 的问题：`MessagePurify.Windows.IntegrationProbe.csproj` 在 Publish 后从匹配官方运行时 MSIX 提取资源，安装脚本也会拒绝缺少该文件的产物；资源字节来自本机 NuGet 缓存，不写入仓库。打包资源提取、覆盖旧文件、缺资源拒绝及空格/中文路径安装回归均通过。
- `NotificationProbe` 在 `Register()` 后读取 `AppInstance.GetCurrent().GetActivatedEventArgs()`，补上 SDK 首次启动参数消费。受控 COM 冷启动回归报告 `.tools/m3-results/cold-notification-regression-summary.json`：修复前未定位，修复后 `activationObserved=true`、`activationKind=cold`、账号匹配；独立 `tests/notification_cold_activation_test.py` 使用正式发布产物退出码 0。该结果只证明回调路由，不能替代用户实际点击验收。
- 14:28:38 已按用户要求通过真实 Explorer `.lnk` 重发普通测试提醒，报告 `.tools/m3-notification-diagnostic-publish/results/normal-medium-user-resend-20261011.json` 为 `submitted`、ID 36646、`error=null`、`activationObserved=false`；无包身份 15700、完整性 RID 8192。用户此前已看到一条提醒但点击没有反应；当时运行副本尚未包含冷启动修复，点击问题现已通过专用 URI 入口修复，旧声音验收现已取消。直接从 Codex 运行辅助脚本继承包身份 122，会再次出现 `0x80070490`；无包身份后缺资源错误为 `0x8007007E`，两者分别定位。
- 群历史错误诊断实际运行 `python .tools/m3-results/diagnose-group-history-errors-20261011.py`，退出码 0；状态一次、群列表一次、历史两次。报告 `.tools/m3-results/qq-history-errors-diagnosis-20261011.json` 复现一个失败样本 `retcode 1200/data=null`；成功对照返回 5 条消息。失败样本为 NapCat 对底层空历史返回接口错误，未逐群复测全部 11 个失败；仍需在正式同步中逐群展示失败并完成七天补漏策略，不修改 QQ/NapCat 客户端。
- 保存前重新执行 Windows Release 测试，45/45、退出码 0；通过真实 Explorer 辅助进程运行正式发布产物的冷启动回归，`.tools/m3-results/cold-test-result.json` 为退出码 0、PASS。直接从 Codex 运行包装脚本的包身份环境错误已与产品回归区分；无需再次退出或登录 QQ。未找到 `maintain-context-hygiene` 技能，已人工核对交接、需求、实际源码及验证边界。
- 通知修复、测试和文档已保存为 `bdffc7b`。14:28 测试等待结束后，14:40:56 使用包含冷启动修复的正式发布产物再次发送本机提醒；报告 `.tools/windows-probe-publish fixed/results/normal-medium-user-resend-fixed-20261011.json` 为 `submitted`、ID 36648、`error=null`，等待点击 600 秒。用户随后反馈该条提醒多次点击无反应；现已改用专用 URI 入口并取得实际点击通过结果，旧声音验收现已取消，不再重复登录 QQ 或无必要地发送提醒。最新文档边界断言与交付文件检查已更新，实际运行 `node --test tests/project-rules.test.mjs` 为 16/16，`git diff --check` 退出码 0。

### 本次通知点击修复与真实反馈

- 用户反馈多次点击无反应。旧通知 36648 对应 COM 激活出现 DistributedCOM 10010 超时；真实 Explorer 辅助进程的不同完整性测试均出现超时，完整性差异不记为唯一根因。确定的参数缺口是 Windows 会追加 `-Embedding`，旧解析器拒绝该参数；已修复并补回归。
- 新增 `src/MessagePurify.Windows/IntegrationProbe/NotificationProtocol.cs`，以当前用户注册的 `messagepurify-probe` URI 打开合成事项；严格校验字段，点击只显示详情，不注册或发送通知、不读取 QQ。发送与点击报告使用同一 Probe ID，声音意图与静音载荷保留。新增 `tests/MessagePurify.Windows.Tests/NotificationProtocolTests.cs`、`tests/notification_protocol_activation_test.py`；受控 Shell 分发与实际通知点击分开记录。
- 14:52:34 发送的普通测试提醒为 ID 36649，发送报告 `.tools/windows-probe-protocol/results/normal-protocol.json`；用户回答“出现了事项窗口”。14:54:38 的 `%LOCALAPPDATA%/MessagePurify/IntegrationProbe/results/protocol-*.json` 点击记录与发送 Probe ID 一致，`activationObserved=true`、`activationKind=protocol`、账号匹配、`error=null`。用户实际点击通过，不再等待同一项反馈。
- 实际运行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release`，57/57、退出码 0；发布 `.tools/windows-probe-protocol-final` 退出码 0。`python tests/notification_protocol_activation_test.py --executable .tools/windows-probe-protocol/MessagePurify.Windows.IntegrationProbe.exe` 退出码 0，受控报告使用 `controlled-shell-` Probe ID；带 `-Embedding` 的最终产物受控 COM 回归 `.tools/m3-results/cold-protocol-final-test-result.json` 为退出码 0、PASS。
- 实际通知运行副本为 `.tools/windows-probe-protocol/`，最终发布产物为 `.tools/windows-probe-protocol-final/`；执行 `packaging/probe/Install-Probe.ps1 -PublishDirectory .tools/windows-probe-protocol-final` 更新当前用户安装，退出码 0，安装 DLL 的 SHA256 与发布一致，没有覆盖仍打开的事项窗口。当前用户协议注册仍指向实际通知运行副本，后续从新的安装入口发送会重新注册到对应可执行文件。声音验收已取消；静音提醒呈现、桌面人工项、学校通知日期角色人工确认、正式任务生成、长期稳定性及七天补漏仍未验收；M3 继续实施。
- 本次四文件 Node 回归初跑 20/21：文档断言要求同一行明确“受控检查不替代实际点击”，更新后的说明分散在两行；补齐说明后实际重跑 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs`，21/21、退出码 0。本次没有重跑未改动的 Core、Infrastructure、OCR 和 demo 套件；已人工核对当前需求、设计、交接、源码、报告及剩余验收一致，未找到的 `maintain-context-hygiene` 技能继续以项目规定的人工核对替代。

### 上轮 M2 本地规则与样本

- 用户要求继续工作，沿用整体设计及已确认技术路线推进 M2。已核对 M1 源码、锁文件、87 项核心测试及 demo 的二次确认改动，工作从 `8f9acdc` 创建 `codex/m2-local-rules` 分支。
- [M2 详细计划](superpowers/plans/2026-10-10-local-rules.md)已写入，消息／日期、提取／优先级、样本／评估三个任务均已完成；不把合成样本作为真实 QQ 验收。
- Task 1 新增 `src/MessagePurify.Core/Rules/RuleModels.cs`、`MessageNormalizer.cs`、`MessageDateParser.cs`，以及 `tests/MessagePurify.Core.Tests/MessageNormalizerTests.cs`、`MessageDateParserTests.cs`。保留账号／群／消息证据、图片与引用段；日期按原发送时刻及保存时区解释，不制造钟点。支持完整年月日、月日、相对日期、明确星期、下月日期和数字钟点；日期范围、冲突、非法和未覆盖表达返回具体确认原因。
- 实现前过滤测试因缺少类型退出 1；Task 1 提交前实际执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`，退出码 0、183 项通过（原 87 项、标准化 2 项、日期 94 项）。执行 `node --test tests/project-rules.test.mjs`（10 项）、`node tests/prototype-qa.mjs`（107 项）、`git diff --check`，均退出码 0。人工核对需求、阶段和实际产物；未找到的 `maintain-context-hygiene` 技能继续以项目规定的人工核对替代。Task 1 已提交 `998eb0f`。
- Task 2 已写入 `Rules/MessageRuleExtractor.cs`、`PriorityCalculator.cs`，及 `RuleExtractionTests.cs`、`PriorityCalculatorTests.cs`（在既有 Core／测试目录）。普通明确行动按已确认课程和有效时间提取；疑问、否定、转述、限定对象、缺时间分别保留原因。延期／取消／新要求分别检查可信发布者和唯一目标，已归档／已取消新要求为待确认；引用仅提供关联证据，OCR 只提供建议，不实际改变事项或发通知。
- Task 2 两类过滤测试实现前均退出 1；实现后提取过滤初始 67 项通过、优先级初始 12 项通过。补保存时区跨 UTC 日期、条件句、排除目标、混合证据引用、格式要求及自愿表达回归后，root 实际运行 Release 核心全量，退出码 0、289 项通过（提取 85 项、优先级 13 项，含 Task 1 标准化 2 项和日期 102 项）。后果只按明确说明提升一级。
- 独立只读复核发现日期部分匹配、条件拆分后丢失作用域、取消格式要求误取消任务、自愿报名表误建任务等具体问题，已修复并补回归。日期 8 项新增回归实际先失败 6 项（退出 1），修复后过滤 102 项通过（退出 0）；核心全量 289 项包括这些修复，Task 2 已提交 `39fa505`。
- Task 3 新增 `tests/fixtures/rules/synthetic.json`（65 个独立样本、27 类、57 个 gold 候选、47 个需本人行动）以及共享 `RuleFixtureRunner.cs`、6 项 fixture 测试和 `tools/MessagePurify.RuleBaseline/` CLI。测试项目复制 fixture 到输出目录，CLI 只引用 Core 并链接同一 runner。实际执行 fixture 过滤测试通过 6 项；CLI 生成 [rule-baseline.md](verification/rule-baseline.md)：65/65 完整匹配、正式 Task 准确率 23/23、需行动召回 47/47、直接自动覆盖 27/47、待确认占比 23/57、高影响错误 0。CLI 无效参数实际退出码 2；合成结果不替代真实独立 QQ 样本验收。
- CLI 错误 gold 出口通过独立复制运行产物实测：执行 `.tools/dotnet/dotnet.exe .tools/m2-cli-negative/MessagePurify.RuleBaseline.dll --output .tools/m2-cli-negative/failure-report.md`，退出码 1；复制样本只修改一个期待日期，结果为 64/65 匹配、失败 1、高影响错误 1。正式 JSON 已核对未改。共享评估器的全部 Pending、错误日期／目标、零分母及失败出口经独立只读复核无未解决问题。
- 配置 fixture 时首次过滤构建因 SDK 默认包含与显式 `Compile` 重复而退出 1；定位后移除重复项，过滤 6 项及 Release 全量 295 项通过。Core.Tests 和 CLI 的 `restore --locked-mode` 均退出 0；文档阶段措辞检查曾失败，同步有效阶段及断言后 10 项通过。验证范围始终是 C# 核心、合成样本、文档和 demo，不含真实 Windows／QQ／OCR 集成。
- M2 交付前实际执行 `.tools/dotnet/dotnet.exe restore tests/MessagePurify.Core.Tests --locked-mode`、`restore tools/MessagePurify.RuleBaseline --locked-mode`、`test tests/MessagePurify.Core.Tests --no-restore --configuration Release`、`run --project tools/MessagePurify.RuleBaseline --configuration Release --no-restore -- --output docs/verification/rule-baseline.md`，全部退出 0，核心 295 项、CLI 65 条样本通过。执行 `node --test tests/project-rules.test.mjs`（10 项）、`node tests/prototype-qa.mjs`（107 项）、`node --check` 三个现有 JS 文件及 `git diff --check`，均退出 0。人工里程碑核对：M1／M2 已勾选，M3～M9 未勾选，需求、交接、实际源码、基线和验证范围一致；不删除文件来缩短上下文。Task 3 提交说明为 `test: add synthetic rule corpus and baseline report`，标识以 Git 日志为准。

### 上轮 demo 删除日期二次确认

- 用户要求删除截图中的“我已查看原图并确认所填写的日期和时刻”勾选框。demo 改为填写日期后通过现有确认／保存按钮一次提交，不再单独勾选；填入图片建议仍只更新草稿，提交前不写正式时间。
- 已删除勾选框、修改日期后的勾选重置和提交拦截；图片任务通过现有按钮提交后仍记录已确认时间及手动保护。填入建议、修改后保存、刷新、仅日期、图片转通知和自定义课程草稿保留均有浏览器检查；M1 核心和 M2 接续顺序不变。
- 实际执行 `node tests/prototype-qa.mjs --screenshots`，退出码 0、107 项检查通过，无浏览器运行异常；执行 `node --test tests/project-rules.test.mjs`（9 项）、`.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`（87 项）、`node --check prototype/app.js`、`node --check tests/prototype-qa.mjs`、`git diff --check`，均退出码 0。需求、体验说明及测试说明同步更新；对应提交说明为 `fix: remove redundant image date confirmation`，标识以 Git 日志为准。
- 未找到 `maintain-context-hygiene` 技能，已按项目规则人工核对需求、交接、实际代码及验证范围，原图／OCR 建议仍不自动确立正式时间。
- 更新并查看 `prototype/previews/courses.png`，确认日期输入下直接显示优先级，不再出现二次勾选；课程截图在滚动完成后捕获。本轮保留这一处相关预览更新。

### 上轮正式开发启动

- 用户明确授权准备完成后开始编写代码，沿用既有 M1 计划，不重新选择 NapCat、本地运行或课程名单获取方式。
- QQ 已在电脑登录来自用户说明，尚未检查 NapCat 对应 QQ 环境；真实集成仍按 M3 执行，不把普通 QQ 登录写成已接入。
- 当前在 `codex/m1-core-foundation` 分支完成 M1，Task 1 时间、Task 2 提醒和 Task 3 状态操作均已写入并验证。各任务验证后分别提交；真实应用界面、数据库、消息识别和系统集成仍在后续阶段。
- SDK 安装在本项目 `.tools/dotnet`（Git 忽略），没有修改全局 PATH 或已有 dotnet 安装。通过[微软官方发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)获取 Windows x64 ZIP，实际执行 `Invoke-WebRequest`、`Get-FileHash -Algorithm SHA512`、`Expand-Archive`，校验与元数据一致。执行 `.tools/dotnet/dotnet.exe --version`、`--list-sdks`、`--info` 均退出码 0：SDK 10.0.401，RID win-x64，NETCore／ASP.NETCore／WindowsDesktop 运行时 10.0.12；`global.json` 锁定 10.0.401，rollForward 为 latestPatch。
- 已执行 SDK 的 `new globaljson`、`new classlib -f net10.0 --no-restore`、`new xunit -f net10.0 --no-restore`、`add … reference …` 和 `restore tests/MessagePurify.Core.Tests --use-lock-file`，均退出码 0。保留模板稳定依赖 Microsoft.NET.Test.Sdk 17.14.1（MIT）、xunit 2.9.3 与 runner 3.1.4（Apache-2.0），版本与完整传递依赖写入锁文件；未使用覆盖率采集器，不加入 coverlet。许可证来源为对应 NuGet 官方包页，xunit v2 为仅安全更新的旧稳定系列，当前构建未出现警告。
- `TaskTime` 保留 Deadline／Start、日期、可选钟点及时区；仅日期当天仍有效、不生成正式瞬时值；精确时间到时即失效，非法时区及夏令时歧义／不存在钟点返回明确异常。先执行 `test … --no-restore --filter TaskTimeTests`，退出码 1，确认缺少 TimeKind／TaskTime 实现；写入实现后执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore`，退出码 0，10 项通过、0 失败。
- 文档检查首次因顶部需求摘要未记录真实集成边界而失败，读取失败断言后补齐摘要；重跑 `node --test tests/project-rules.test.mjs` 退出码 0、9 项通过。`node tests/prototype-qa.mjs` 退出码 0、102 项通过。`maintain-context-hygiene` 技能在本机技能目录中未找到，按项目规则人工核对当前阶段、有效需求及实际产物。
- Task 1 交付前执行 `.tools/dotnet/dotnet.exe restore tests/MessagePurify.Core.Tests --locked-mode`、`test tests/MessagePurify.Core.Tests --no-restore`、`node --test tests/project-rules.test.mjs`、`node tests/prototype-qa.mjs`、`node --check tests/project-rules.test.mjs`、`git diff --check`，全部退出码 0，分别 10 项核心、9 项文档和 102 项浏览器检查通过。提交说明 `feat: add core time model and test project`，标识以 Git 日志为准。
- Task 2 新增 `src/MessagePurify.Core/Reminders/ReminderPlanner.cs`、`tests/MessagePurify.Core.Tests/ReminderPlannerTests.cs`。默认精确节点提前 24／2 小时，日期节点前一天 12:00；支持自定义小时、日历偏移及关闭。小时规则遇日期精度回退默认并返回 NeedsReview，日历偏移保留，关闭不重启。仅产出节点及待确认决定，不进行系统投递或创建持久化待确认项。
- 先执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --filter ReminderPlannerTests`，退出码 1，确认缺少提醒类型／实现；实现后同命令退出码 0、33 项通过，全部核心测试退出码 0、43 项通过。精确基准的“提前”节点须严格更早，校正计划示例的 `>` 为 `>=`；包括夏令时经过 24 小时、小数小时及日历节点的测试，不改变正式时间。
- Task 2 交付前再次执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore`、`node --test tests/project-rules.test.mjs`、`node tests/prototype-qa.mjs`、`git diff --check`，全部退出码 0，43 项核心、9 项文档、102 项浏览器检查通过。提交说明 `feat: add default and custom reminder planning`，标识以 Git 日志为准。
- Task 3 新增 `src/MessagePurify.Core/State/TaskTransitions.cs`、`tests/MessagePurify.Core.Tests/TaskTransitionsTests.cs`。完成／取消／归档／恢复只对实际变化递增事项版本，时间修改独立递增时间版本；受保护正式时间只输出建议，接受后才更改并保护。取消不被旧完成／恢复／归档覆盖；已完成可信新行动可恢复，已归档及取消不自动恢复。完成撤销 1 小时、忽略资格 24 小时，到期或被后续操作替代时不可撤销；完成后延期再撤销保留最新时间。
- 先执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --filter TaskTransitionsTests`，退出码 1，确认缺少状态类型／实现；实现后同命令退出码 0、44 项通过；全部核心测试退出码 0、87 项通过、0 失败。`OperationSequence` 及 superseded 的实际检查、证据／处理记录保存、忽略的持久化恢复和数据库并发在 M4 落实，核心只消费当前快照和明确输入，不恢复整个旧快照。
- M1 交付前实际执行 `.tools/dotnet/dotnet.exe --version`、`restore tests/MessagePurify.Core.Tests --locked-mode`、`test tests/MessagePurify.Core.Tests --no-restore --configuration Release`，全部退出码 0：SDK 10.0.401、Release 编译成功、87 项测试通过，0 失败／跳过。执行 `node --test tests/project-rules.test.mjs`（9 项）、`node tests/prototype-qa.mjs`（102 项）、`node --check tests/project-rules.test.mjs`、`node --check tests/prototype-qa.mjs`、`node --check prototype/app.js`、`git diff --check`，均退出码 0，无浏览器运行异常；范围为核心、文档及 demo。
- 独立只读代码复核未发现需要修正的问题；人工完成里程碑上下文核对：M1 三任务已勾选且源码／测试／锁文件存在，需求、设计及本文的阶段与实际验证一致，M2～M9 未勾选。`maintain-context-hygiene` 技能仍未找到，沿用人工核对，不删除项目文件。前两次提交为 `f1bdce3`、`0edf77a`；Task 3 提交说明 `feat: add lifecycle and protected time decisions`，标识以 Git 日志为准。

### 上轮 demo 认可与下一步说明

- 用户表示 demo 基本满足要求，并询问下一步。该反馈是对演示交互的基本认可，不代表真实应用验收；有效产品需求和既有技术路线继续保留。
- 下一步沿用已写入的 M1：准备开发环境，完成时间模型、提醒计算、任务状态及撤销规则的真实 C# 核心和测试；随后按 M2、M3 进行本地消息识别与真实 Windows／QQ／OCR／通知的小规模验证，再推进存储、正式界面、后台运行和安装交付。
- 本轮只更新阶段记录和相关文档测试，没有安装 SDK、创建 C# 文件或开始真实集成。技术细节由开发者处理，实际连接时再请用户完成 QQ 登录或提供必要样本。
- 实际执行 `node --test tests/project-rules.test.mjs`、`node tests/prototype-qa.mjs`，均退出码 0，分别 8 项和 102 项通过；`node --check tests/project-rules.test.mjs`、`git diff --check` 均退出码 0。已人工核对演示反馈、有效需求、下一步计划及实际文件一致；`maintain-context-hygiene` 技能此前搜索未找到，沿用项目规定的人工核对。对应提交说明为 `docs: record demo feedback and next development step`，标识以 Git 日志为准。

### 上轮课程／分类选项同步

- 用户要求列表课程筛选与详情的“课程或分类”同步下拉内容并支持自定义。右侧原自由文本输入已替换为下拉框，筛选、待确认、编辑和手动新增任务共用已有课程／分类及自定义名称。
- 各课程下拉框提供“＋ 自定义课程或分类…”入口。右侧新增后自动选中名称，提交事项后才更新所属课程；左侧新增只同步选项，保留当前筛选。取消新增恢复原选择，空白名称显示错误，重复名称复用已有选项并去除首尾空格。同步选项不重建表单，保留未提交的标题、日期、时刻及图片确认。
- 自定义名称保存在原有 `message-purify-demo-v2` 浏览器本地存储的 `customCourses` 字段，兼容未含该字段的旧保存数据；刷新及“重置示例”后保留。本轮支持新增名称，未实现重命名／删除管理或真实课程／群关联修复。
- 实际执行 `node tests/prototype-qa.mjs --screenshots`，退出码 0、102 项检查通过、无 JavaScript 异常；新增 16 项课程交互检查，覆盖两处入口、同步、取消、重复／空白名称、草稿保留、刷新、重置、待确认／手动任务及筛选。实际执行 `node --test tests/project-rules.test.mjs`，退出码 0、8 项通过；`node --check prototype/app.js`、`node --check tests/prototype-qa.mjs`、`git diff --check` 均退出码 0。
- 接续收尾时将课程预览纳入交付文件存在检查，实际重跑 `node tests/prototype-qa.mjs`、`node --test tests/project-rules.test.mjs`，均退出码 0，分别 102 项和 8 项通过；`node --check prototype/app.js`、`node --check tests/prototype-qa.mjs`、`node --check tests/project-rules.test.mjs`、`git diff --check` 均退出码 0。
- 六张预览已生成，新增并查看 `prototype/previews/courses.png`，确认课程下拉框与固定确认按钮布局正常。需求、交接、体验说明、测试说明及实际文件已人工核对一致；本机技能目录仍未找到 `maintain-context-hygiene`，按项目规则完成核对。对应提交说明为 `feat: sync course selectors and support custom courses`，标识以 Git 日志为准。下一步继续收集 demo 体验反馈，真实应用代码尚未开始。

### 上轮 demo 体验修复

- 用户反馈右侧详情被裁切、滚轮不生效，以及无法找到待确认的编辑确认入口。已复现并定位：`.main` 的 Grid 子项默认最小高度被长详情撑大，上层 `.app` 隐藏溢出，详情本身无滚动空间；确认按钮在裁切区域的表单底部。
- 1100×800 复现时详情高 1169px、底部坐标约 1254px、滚动高度等于可视高度。新增高度回归检查首次执行 `node tests/prototype-qa.mjs` 退出码 1，证明原问题；只加入 `.main { min-height:0 }` 后，详情可视高度为 694px、滚动高度 1169px、底部 779px，复跑退出码 0、79 项通过。
- 详情现在由独立滚动内容区与底部固定操作栏组成。待确认的按钮根据事项类型显示“确认并加入任务”或“确认并保留为通知”，文字说明确认后的去向和必填信息。按钮使用原表单提交机制，信息缺失时滚动到错误提示；图片日期的人工确认规则继续有效。
- 最终实际执行 `node tests/prototype-qa.mjs --screenshots`，退出码 0、86 项通过；新增原生鼠标滚轮上下滚动、左栏不受影响、按钮固定可见、按钮文案更新、错误可见，以及原生鼠标点击转为任务／通知的检查。执行 `node --test tests/project-rules.test.mjs`，退出码 0、8 项通过；`node --check prototype/app.js`、`node --check tests/prototype-qa.mjs`、`git diff --check` 均退出码 0。
- 五张预览已生成；新增并查看 `prototype/previews/pending.png`（1100×800），确认右栏滚动条和底部确认按钮可见。需求、体验说明与实际文件已人工核对一致；`maintain-context-hygiene` 技能此前搜索未找到，继续依据项目规则完成核对。真实应用集成边界不变，继续等待用户体验反馈。对应提交说明为 `fix: restore detail scrolling and visible confirmation`，标识以 Git 日志为准。

### 上轮整体确认与实施计划

- 用户回复 A，确认按更新后的完整设计继续制定开发步骤；设计状态由“待整体核对”改为“整体设计已确认”，不重新选择后端机制。
- 已写入[实施总计划](superpowers/plans/2026-10-10-windows-app-implementation.md)及[第一阶段详细计划](superpowers/plans/2026-10-10-core-foundation.md)。总计划分 M1～M9，映射设计第 12 节的全部 31 个验收场景；M1 细分三个可测试并独立提交的任务。计划中的源码路径、代码示例和测试命令是实施说明，不是已经创建或运行的应用功能。
- 独立只读环境核对实际执行 `git --version`、`git status --short`、`node --version`、`Get-Command dotnet -All -ErrorAction SilentlyContinue | Select-Object Name, Source, CommandType | ConvertTo-Json -Compress`、`dotnet --list-sdks`、`dotnet --list-runtimes`，均退出码 0。Git 为 2.54.0.windows.1、Node 为 v24.16.0；默认 dotnet 在 `C:/Program Files/dotnet/dotnet.exe`，SDK 列表为空，NETCore 6/7/8 及 WindowsDesktop 6/7 运行时已安装，不能替代 .NET 10 SDK。
- 执行 PowerShell 浏览器文件及注册表只读查询，使用 `Test-Path`、`Get-Item`、`Get-ChildItem` 和注册表 `GetValue`，两份查询脚本均退出码 0；Chrome 为 154.0.8037.98，WebView2 Runtime 注册版本为 155.0.4283.45。仅检测安装，尚未运行 WPF/WebView2 应用。
- 当轮更新整体确认状态测试、计划链接检查和全部验收场景映射检查；实际执行 `node --test tests/project-rules.test.mjs`，退出码 0、7 项通过；执行 `node tests/prototype-qa.mjs`，退出码 0、27 项通过、无 JavaScript 异常。执行 `node --check tests/project-rules.test.mjs` 与 `git diff --check`，均退出码 0。当轮测试通过，验证范围是文档和早期浏览器原型；M1 所有实施项未执行。其后用户确认 demo 先行，当前顺序以“接手先看”为准。
- 自行核对计划与完整设计：31 项验收均映射到已定义阶段，两份计划中的任务均未勾选；占位词扫描无匹配，未发现 C#/WPF 实现文件。下载副本与上次提交先作正文比对，确认没有额外改动后同步整体确认状态、环境记录及计划绝对链接；再核对与项目设计正文一致。交接、需求、设计和实际文件保持一致，按已有规则人工完成上下文核对。
- 已实际执行 `git diff --cached --check`、`git commit -m 'docs: confirm design and plan Windows implementation'`、`git log -1 --format='%h %s'` 和 `git status --porcelain`，均退出码 0；对应提交已创建，包含本轮 7 个项目文件，工作区状态输出为空。下载副本为项目设计的正文相同副本，正式版本以仓库中的设计为准；最新提交标识见 Git 日志。

### 协作规则、测试与 Git

- 每次改动完成后，都必须创建一个对应的 Git commit，以便后续追踪和回滚。
- 每次改动后，都必须编写或更新相关测试，并在交付给用户前，确保所有测试和验证全部通过。小任务同样适用，不强制测试先行。
- 现有 `AGENTS.md` 已加入“注意事项”，原有接续、文档同步、讨论和阶段规则保留；需求文档同步新增要求。
- 上轮在项目根目录执行 `git init`，建立本地 Git 仓库，并创建首次基线提交 `ff16b4b`，包含现有文档、浏览器原型及规则和测试改动。提交作者仅在本仓库配置为 `Codex <codex@localhost>`，未修改全局作者设置；后续提交见 Git 日志。本轮没有应用实现改动，也未配置远程仓库。
- 已有 `tests/project-rules.test.mjs`、`tests/prototype-qa.mjs` 和 `tests/README.md`。早期 27 项浏览器检查先扩充为 78 项，详情修复后为 86 项，本轮课程功能后为 102 项；文档检查为 8 项。默认测试不覆盖已有预览图，可用 `--screenshots` 主动更新六张预览；测试范围与命令见 `tests/README.md`。
- 上轮实际执行 `node --test tests/project-rules.test.mjs`，退出码 0，6 项通过、0 失败；执行 `node tests/prototype-qa.mjs`，最终退出码 0，27 项通过、无 JavaScript 异常。首次浏览器检查及诊断复跑均在按钮动画断言处退出码 1：诊断显示本机 `prefers-reduced-motion: reduce` 为真、按钮过渡为 `0s`，符合界面现有规则；测试随后显式模拟普通与减少动画两种偏好，不依赖 Windows 设置，未改原型代码。
- 上轮实际执行 `node --check tests/prototype-qa.mjs` 和 `node --check tests/project-rules.test.mjs`，均退出码 0。全部现有测试的命令及环境要求见 `tests/README.md`；验证范围为文档和浏览器原型，真实应用代码尚未开始。本轮更新阶段相关检查并重新验证后记录结果。
- 上轮实际执行 `git diff --cached --check`、`git commit -m 'chore: add project rules and repeatable checks'`、`git log -1 --format='%h %s'` 和 `git status --short`，均退出码 0；首次提交成功、状态输出为空。提交包含 12 个文件，没有个人凭据配置。核对需求、交接与实际产物一致；本机仍未找到 `maintain-context-hygiene` 技能，按项目规则人工完成交接核对。

### 新版 demo 的实现范围与此前验证

- 入口为 `prototype/index.html`，逻辑拆入普通脚本 `prototype/app.js`；双击 HTML 可运行，未增加构建工具或依赖。体验说明见 [demo 说明](../prototype/README.md)。
- 左侧导航、列表与右侧详情沿用早期布局；新增标题／课程／来源群搜索、课程筛选，以及任务页的已归档／已取消筛选、待确认页的最近忽略入口。桌面卡片列表不随主界面搜索而减少。
- 完成后立即移入已完成记录，即时提示和记录详情均提供“撤销”，1 小时内有效；忽略后 24 小时内可撤销。时间戳保存后刷新继续计时，过期保留记录；点击动作再次检查资格。重新收到新行动要求时，先前完成记录仍保留。
- 归档移出待办和桌面模拟卡片，保留原文并停止时间提醒展示；归档筛选内可恢复待办。新增手动任务支持类别、课程、日期、可选时刻和优先级；区分提交截止与考试／活动开始，开始已过不误记为作业逾期，仅日期当天仍有效。
- 单条提醒可选默认、提前小时、前几天某时刻及关闭；仅日期禁用小时模式。延期保留相对间隔，时刻精度丢失时临时采用默认提醒，新增关联待确认项；已关闭提醒不自动开启。上述只计算和展示节点，没有执行系统调度。
- 详情底部可模拟延期、取消和新增行动要求。手动时间冲突保留旧值及有效提醒，接受后更新；取消独立生效并保留记录；已完成新要求恢复待办，已归档新要求先待确认。示例消息及完成／变化记录可查看；没有接入真实消息识别或系统变更通知。
- 图片点击放大、OCR 默认折叠、建议日期可填入或拒绝；填入仅影响表单，勾选确认并提交后才确立正式日期。新内容带未读数量和红点，显示详情后标记已读；在桌面预览操作不会将隐藏的主界面详情误记为已读。
- 保留浅色与纯黑深色、320ms 换肤、按钮按压和卡片悬停动效；设置新增跟随系统和减少动画，以及声音、自动启动、关闭行为的演示偏好。安静时段入口标注“暂未启用”。首次提供三步示例引导；“预览提醒”展示内容及声音意图，点击“打开事项”定位对应详情。没有播放系统声音或修改 Windows 设置。
- 桌面双卡片可拖动／缩放并保存浏览器模拟布局，待确认清空后隐藏其卡片，任务清空后显示“暂无待办”；保留逾期原时间。这里的双卡片仍为一个浏览器预览容器，不是两个独立 Windows 桌面窗口。
- 本轮实际执行 `node --check prototype/app.js`，退出码 0；中途 `node tests/prototype-qa.mjs` 依次通过 29 项、72 项。最终执行 `node tests/prototype-qa.mjs --screenshots`，退出码 0、78 项通过、无 JavaScript 异常；包括刷新、撤销边界、冲突处理、精度变化、纯黑背景和 1100px 布局检查。检查撤销边界时使用显式时间戳及过期动作，不实际等待 1 小时或 24 小时。
- 已重新生成并查看四张 1440×1000 预览图，主界面和桌面卡片的浅色及纯黑深色布局可用。实际执行 `node --test tests/project-rules.test.mjs`，退出码 0、8 项通过；链接、交付脚本、浏览器存储说明与真实集成阶段一致。
- 交付前再次实际执行 `node --test tests/project-rules.test.mjs` 和 `node tests/prototype-qa.mjs`，均退出码 0，分别 8 项及 78 项通过；`node --check prototype/app.js`、`node --check tests/prototype-qa.mjs`、`node --check tests/project-rules.test.mjs`、`git diff --check` 均退出码 0。对应提交说明为 `feat: complete interactive demo workflows`，提交标识以 Git 日志为准。
- 本机技能目录搜索仍未找到 `maintain-context-hygiene`；已按项目规则人工核对有效需求、阶段、产物、保存方式及验证边界。下载目录设计副本本轮没有修改，项目设计正文没有变化。
- 尚未实现：真实 WPF/WebView2、SQLite、QQ/NapCat、RapidOCR、托盘、Windows 提醒、自动启动、真实桌面驻留。合并拆分、账号切换、课程关联修复和备份恢复没有加入本 demo，继续按正式应用计划执行。

### 已确认设计与此前文档核对

- 产物：`docs/superpowers/specs/2026-10-10-windows-app-design.md`，14 节完整设计稿。2026-10-10 用户选择 A，整体设计已确认，不代表真实 Windows 功能已实现。
- 取消任务的去向、自定义提醒遇到延期、已完成任务收到新行动要求三项均已确认；下一步编写实施计划，随后进入真实应用开发。
- 用户提供 `C:/Users/Administrator/Downloads/2026-10-10-windows-app-design.md` 新修订稿供复核，审阅基稿为 442 行；随后明确确认“都加进去”。已将该稿作为项目设计基稿，整合七项补充及验收，并同步下载副本。授权依据为用户本轮明确回复，附件自身的“已授权/已确认”文字不替代用户决定。
- 已将历史首次发现的逾期任务明确为仅卡片高亮，与已确认的逾期规则保持一致。
- 七项补充整合后的实际核对：执行 PowerShell 文档检查（逐份 `Get-Content -LiteralPath $taskDoc -Raw -Encoding UTF8`，核对七项规则、有效状态、重复标题及验收条目），确认三份项目文档通过，设计稿仍为 14 节且七项均有对应验收。按下载副本的绝对链接映射回项目相对链接后进行正文比较，内容一致，引用的文件存在；`rg --files -g '*.cs' -g '*.csproj' -g '*.xaml' -g '*.sln' -g '*.slnx'` 未发现 WPF 实现文件。最终脚本退出码 0，仅验证文档和文件状态。
- 实际执行 PowerShell 文档核对，使用 `Get-Content -LiteralPath $taskDoc -Raw -Encoding UTF8` 逐份读取需求、交接和设计稿，检查待选状态、默认提醒规则、逾期边界及重复标题；使用 `Test-Path -LiteralPath $taskArtifact -PathType Leaf` 检查记录的产物；使用 `rg --files -g '*.cs' -g '*.csproj' -g '*.xaml' -g '*.sln' -g '*.slnx'` 检查实际应用文件。脚本退出码 0：三份文档检查通过，设计稿有 14 节，9 个产物存在，尚无 WPF 实现文件。
- 实际执行 `rg -n -e '完整设计讨论' -e '2026-10-10-windows-app-design' -e '历史首次发现' -e '待选一' -e '尚待确认的使用行为' docs/HANDOFF.md docs/requirements.md docs/superpowers/specs/2026-10-10-windows-app-design.md`，退出码 0，核对阶段、设计稿引用、逾期规则与待选事项已同步。仅为文档检查，本轮未重跑浏览器测试或执行真实 Windows 功能测试。
- 再次搜索本机技能目录未找到 `maintain-context-hygiene`，按项目规则人工核对需求、交接、设计稿与实际文件，没有修改应用代码或无关文件。

### 已确认的真实应用路线

- 采用 **C# WPF + WebView2**；WebView2 复用主界面，WPF 负责托盘、启动、本地持久化、NapCat、工作区刷新和桌面卡片原生窗口层级。RapidOCR 独立本地工作进程继续沿用。
- 第一版只使用左右两张桌面卡片，取消全部 Windows 系统通知弹窗和声音；不提供系统提醒、自定义时间提醒、安静时段、测试通知或通知权限恢复入口。
- 正式两张桌面卡片首段已实现并验证：自动刷新 AccountWorkspace 当前账号快照，显示标题、来源、优先级和有效时间，提供查看/完成；待确认处理完隐藏，空待办显示“暂无待办”。仅附着 Explorer 桌面、不设 Topmost，首次按屏幕左右摆放，拖动/缩放后以 DIP 持久化布局。Native 合成 13 项及独立父窗/style 检查通过；变化标签已在后续变更历史接入中覆盖，Native Smoke 21 项通过；多屏、Explorer 重启和睡眠恢复尚未验收。
- 主界面继续展示连接状态、自动重连、原文、图片和变更记录；断线超过 5 分钟不弹系统通知。普通群通知只保存在 App 通知列表，不增加第三张卡片。
- 归档/取消/完成移出待办与卡片并保留证据；取消记录仍为“已取消”，不记用户完成。可靠新行动恢复已完成任务，保留此前完成记录；手动字段保护和关联待确认继续有效。
- 完成 1 小时/忽略 24 小时撤销、已读版本、搜索筛选、课程/分类同步、纯黑主题及减少动画规则继续有效。
- 自动启动默认开启及关闭行为设置继续作为后续产品范围；首次引导预览桌面卡片，点击卡片详情打开主界面对应事项。
- 手动新增、完整编辑、通知转任务、图片队列、合并拆分、M6 修复和完整账号切换按原计划继续；已接入本地数据备份恢复，原图恢复随图片后期项目；开发者自行处理普通实现细节，不重问 NapCat、本地运行或课程名单来源。
- 以下此前实施和验证记录按当时范围保存；涉及 Windows 通知的“下一步”只属于旧方案历史，当前实施以本节及第 6 节为准。

## 2. 项目目标

制作一个 Windows 桌面软件，从学校 QQ 群消息中整理通知与作业，提取待办、判断优先级并提醒用户。消息处理尽量全部在本机完成。

参考项目是 [偷闲（Touxian）](https://github.com/sssssjw11/touxian)，注意仓库所有者为 `sssssjw11`（五个 s）。借鉴其架构，不直接照搬全部筛选或过期处理规则。

## 3. 已确认需求快照

完整定义见 `docs/requirements.md`。以下为接续时不能遗漏的主要结论。

### 消息获取

- **已决定使用 NapCat**，用户计划用于自己的 QQ 大号。该接入选择已经讨论确认，不需要重新开启选型或账号风险讨论。
- 只读取当前 QQ 中置顶的群聊消息，实时、历史与断线补漏统一限制；连接/重连及运行中重新核对置顶状态，无法取得可靠名单时暂停，不回退到全群。取消置顶后停止新消息读取，已有事项与证据保留；排除私人对话和经用户确认的机器人账号。
- QQ 接入只读，不向 QQ 群或私聊发送消息；第一版通过两张桌面卡片提示待办和待确认，不向学校群发测试消息或 Windows 系统通知。
- 软件按本地规则提出疑似机器人账号，用户确认后才排除；同一账号后续自动忽略，支持撤销。确认前不能因重复内容就排除老师或同学。
- 首次启动尝试回溯最近 7 天；后续断线重连时尝试补漏。历史读取能力取决于 QQ 返回结果，失败时展示对应群和原因。
- 主界面显示连接状态与原因并自动重连；取消断线超过 5 分钟的系统通知。
- 按群及消息 ID 去重，避免历史补漏导致重复任务或重复触发相同提醒。
- 桌面启动与“自动同步”再次读取原消息时跳过已忽略消息，保留已确认事项、正式字段、状态、证据及处理记录；24 小时仅为忽略撤销期限，过期不恢复待确认。新增消息继续正常同步。
- 登录 Windows 后自动启动本软件，在系统托盘后台运行；自动启动默认开启，设置中提供开关。该启动决定针对本软件；目前尚未配置启动项，也未实现自动启动开关。

### 本地识别

- 第一版**不接云端 AI**：本地规则负责通知、作业、相关性、时间和优先级判断。
- 学校场景，只保留通知与作业，不设日常闲聊板块。
- 需要完成的作业、考试、提交等可成为任务；自愿活动、竞赛等作为通知，用户自行加入任务后才按任务处理。
- 支持手动新增任务，用于补录口头布置或漏识别的事项；来源标注为“手动添加”，使用同样的优先级、提醒和完成规则。
- 自动提出课程和群关联候选，由用户确认、修改或排除；复用确认过的关联。**不要回退为首次手填课程名单。**
- 面向全体或明确与本人课程相关的事项自动处理，相关性不确定的进入待确认。
- 综合行动要求、日期上下文、对象限定及群与课程关系，不只依靠一个关键词；不能机械过滤短消息。
- 相对时间以消息原始发送时间为依据。需要行动但缺少或无法确定截止日期时进入待确认，用户补齐并确认后进入正式任务卡片；仅有日期时保留日期精度，不造时刻。

### 图片

- 已同意以 **RapidOCR** 作为本地图片文字识别方案；本机已安装隔离运行环境并完成 25 项回归、用户群列表截图识别和真实学校通知截图识别。通知正文文字、位置和置信度通过；日期范围与“交齐”截止的角色仍需人工确认，正式任务生成和提醒投递未验收。
- 文字消息完整时，原图及 OCR 文字作为辅助信息加入事项详情。
- 文字消息缺少完整时间或其他关键信息时进入待确认；用户查看原图/OCR 后手动填写。
- **图片中的日期经用户确认后才能用于提醒**；OCR 不能自动覆盖或确立正式截止时间。
- 识别失败仍保留原图。目前没有决定支持 PDF 或其他附件内容解析。

### 任务生命周期

- 明确属于同一事项的内容、截止时间和补充说明合并，保留各条消息证据。
- 催交更新原任务；明确的延期、取消、要求修改按字段保护规则更新任务并记录变化，仍在待办中的任务更新卡片变化标记。新时间与手动正式字段冲突时保留旧值及当前提醒、显示待确认差异；取消不被手动时间保护阻挡。
- 能明确关联的延期、取消和提交要求变更更新任务、主界面变更记录和仍在待办的卡片，不发送系统通知。
- 老师明确取消且能关联到原任务时，停止该任务后续提醒，移出待办和桌面卡片，保留“已取消”记录及原始消息；不记为用户已完成。取消状态、可靠关联自动应用及关联建议接受的本地存储已实现，正式卡片只投影 active 任务，取消事项移出；取消变更系统通知已取消。
- 无法明确对应某个任务的变更消息进入待确认，不猜测后直接覆盖。
- **7 天是消息回溯/分析窗口，不是任务寿命。**长期任务跨周保留，原始证据须能继续查看。
- **逾期不自动完成或归档**，继续保持待处理，并通过卡片高亮提示，不重复弹通知。
- 完成由用户手动勾选；完成后进入已完成记录并停止提醒，保留原始证据。用户也可手动归档。
- 任务详情有归档及恢复入口，任务页可筛选已归档记录；归档移出待办和卡片，不记为完成，证据保留。旧证据重读不恢复；归档后新行动要求先作为关联建议待确认。本地归档/恢复及建议持久化已实现，正式主界面及卡片当前状态刷新已接入，Windows 通知清理已取消。
- 已完成任务收到能明确关联的新行动要求时，自动恢复为待办，标出新要求并继续按规则提醒，保留此前完成记录及新旧消息证据；普通补充说明不恢复，关联或有效截止信息不确定时仍进入待确认。可靠新行动自动恢复及历史保留已本地验证，正式卡片变化显示尚未完成，系统提醒调度已取消。

### 优先级与桌面提示

- 本地计算 P0～P3，依据截止时间和明确的事项后果；用户可手动调整。依次匹配：已逾期/今天 P0、未来 3 天内 P1、未来 7 天内 P2、更晚 P3。明确后果可提高一级，否定语义不误提升。
- 实时、手动、历史、补漏及修复发现的任务统一刷新卡片，按最新原消息发送时间倒序，临期和逾期高亮；开始已过单独标记，不误记作业逾期或自动完成，日期当天仍有效。
- 第一版取消全部 Windows 系统通知弹窗和声音；24/2 小时节点、日期前日 12:00、自定义时间提醒、新发现/变更、恢复/历史/迟到批次汇总、通知点击路由、权限恢复和安静时段入口均移出当前范围。
- 重启/睡眠恢复/重连读取当前状态刷新卡片，不补发旧通知；待确认常驻至处理完。原 ReminderPlanner、SQLite plans/ledger 和 probe 验证保留为历史产物，不作为未来 Windows 接入任务。
- 延期按字段保护接受后更新卡片高亮，并按最新原消息时间倒序排列；取消、完成、归档移出卡片，原文及操作历史保留。

### 界面

- 可拖动、调整大小并固定在桌面的组件，统一展示左右排列的两张卡片；用户已选择 B（左右排列）。
- **桌面卡片仅在桌面显示**：切回桌面时可见，不覆盖其他软件窗口；用户已选择 A（仅在桌面显示）。待确认事项仍保留在卡片中，直到用户处理，窗口遮挡不改变其处理状态。
- **提醒事项卡片**：已确认任务按最新原消息发送时间倒序，显示标题、课程/来源群、优先级、截止时间及“详情”“完成”入口。手动新增按创建时间，无时间放最后，临期和逾期高亮；无任务时显示“暂无待办”。主窗口各列表与待确认卡片使用同一排序规则。
- **待确认卡片**：有未处理事项时一直显示，不自动淡出。显示事项标题、来源和待补信息，可查看原文及图片；补齐确认后转入正式任务/通知，无关或误报可忽略。处理一项减少一项，全部处理完后隐藏整张卡片。
- **普通通知仅在软件主界面的通知列表中展示**，不增加第三张桌面通知卡片，也不混入提醒事项卡片。
- **主界面采用左侧导航**：包含“任务、待确认、通知、已完成、回收站、设置”，右侧显示所选页面的内容；已忽略独立进入回收站，原24小时撤销规则保持。用户已选择 A（左侧导航）。
- **事项详情采用主界面右侧详情栏**：点击事项后保留事项列表，在右侧查看原文、图片，并修改截止时间等信息，方便连续处理；用户已选择 A（右侧详情栏）。
- **课程／分类选项同步和自定义**：课程筛选与详情／新增表单共用下拉选项，可从各下拉框新增名称，保存后立即同步并在刷新后保留；同步不丢失未提交表单。筛选选择与事项所属课程独立，编辑课程不自动切换筛选。本轮已在示例数据 demo 实现并验证，真实应用实现仍按正式计划执行。
- **主题切换**：用户已要求支持浅色模式与深色模式自由切换，主界面和桌面卡片统一主题，切换时有柔和的过渡动画。
- **深色主题视觉**：用户进一步明确深色模式使用纯黑背景；面板允许使用接近黑色的层次和边框来保持可读性。

## 4. 已替换的方案

| 旧方案或容易误解的做法 | 当前有效决定 |
| --- | --- |
| 本地规则后由云端 AI 处理不确定消息 | 第一版全部本地处理，不接云端 AI |
| 首次手填课程名单 | 自动提出课程及群关联候选，再由用户确认 |
| 待确认事项每天弹一次汇总通知 | 改为桌面常驻待确认卡片，不设每日汇总通知 |
| 为待确认选择每日提醒时刻 | 该问题已失效，不继续要求用户选择时刻 |
| 普通通知放桌面通知区或增加第三张卡片 | 仅放软件主界面的通知列表 |
| 只保留一周任务，或任务过期后自动归档 | 长期任务跨周保留，过期仍跟踪 |
| OCR 识别出日期后直接用于提醒 | 图片日期必须经用户确认 |
| 时间节点、单条自定义、新发现/变更、恢复/历史/迟到批次、断线等 Windows 通知方案 | 第一版只用两张桌面卡片，取消全部 Windows 系统通知弹窗和声音；保留当前状态、排序及临期/逾期高亮 |
| 逾期后每天中午弹一次通知的建议 | 用户选择仅保留卡片高亮，不重复弹通知 |
| 明确延期一律覆盖手动正式时间 | 与手动字段冲突时保留旧值和当前提醒，接受差异后才替换；取消仍独立生效 |
| 自定义时间提醒和安静时段入口 | 随 Windows 通知渠道取消，旧设置/计划/台账作为历史数据保留 |
| 课程筛选根据事项生成选项，右侧课程用自由文本输入 | 筛选与详情／新增表单共用下拉选项，支持新增自定义名称并立即同步、刷新保留 |

## 5. 调研结论及边界

以下是此前读过官方资料/源码的结论，不代表本项目已经运行验证。

- 偷闲默认基于本地规则、候选聚类、日期解析与优先级计算；它所称的本地 Jev 基线不是本地大语言模型。云端增强是可选项，本项目第一版不采用。
- 已读的偷闲桌面筛选源码会过滤少于 18 个字符的消息，可能漏掉学校群的短作业通知，不能照搬。
- 已读的偷闲桌面优先级逻辑会将逾期事项归档，本项目明确采用继续跟踪的规则。
- RapidOCR 支持中文、Windows、CPU 和本地模型推理，可返回文字、位置与置信度；后续已完成本机群列表及学校通知截图 OCR，日期角色仍需人工确认。离线部署时需提前准备模型等所需文件。
- NapCat 提供 `get_group_msg_history`，参数包含群号、起始消息位置和数量。它使分批补回历史成为可尝试的方案，不能据此保证每个群都有完整 7 天记录。

参考链接：

- [偷闲桌面说明](https://github.com/sssssjw11/touxian/blob/main/desktop/attention-desk/README.md)
- [偷闲本地筛选、日期及优先级源码](https://github.com/sssssjw11/touxian/blob/main/desktop/.agents/skills/attention-announcement-triage/scripts/triage_announcements.py)
- [RapidOCR 输出与使用](https://rapidai.github.io/RapidOCRDocs/main/quickstart/)
- [RapidOCR 本地模型](https://rapidai.github.io/RapidOCRDocs/main/install_usage/rapidocr/how_to_use_offline_model/)
- [NapCat 群历史接口源码](https://github.com/NapNeko/NapCatQQ/blob/main/packages/napcat-onebot/action/go-cqhttp/GetGroupMsgHistory.ts)

## 6. 未决定事项与下一步

以下已有功能和验证保留历史范围；当前部署与进程以首段最新交付记录为准。

已确认方向不重新询问；以下状态均需按真实产物和验证报告更新。

审核粒度已确认并交付（2026-10-15）：用户选择 A，一条原消息一个完整待确认项，需要时手动拆开；新消息与旧未处理拆分项兼容均已实现并验证，正式本机和GitHub beta.3已更新。

1. **整体设计已确认，demo 基本满足要求，持续开发已授权**。M1/M2 已完成，M3 真实接收和长期稳定性仍在实施；真实 QQ、OCR 和桌面宿主各自验证，不用合成数据代替真实验收。
2. **已确认：明确取消任务移出待办和桌面卡片**，保留“已取消”记录和原文，不记完成；不再发取消系统通知。本地取消与旧操作保护已测试，正式卡片 active/pending 状态过滤已通过。
3. **旧自定义提醒方案已取消**。ReminderPlanner、提醒设置/计划/台账及既有验证保留为历史，不删除数据，不作为后续 Windows 自动投递工作。
4. **已确认：已完成任务的可靠新行动恢复待办**，保留完成历史及新旧证据；普通补充不恢复，不可靠关联/时间先待确认。本地状态和路由已测试，正式卡片当前状态刷新及可读变化标记已接入；真实操作验收仍待完成。
5. **正式两张桌面卡片首段已实现并验证**。自动刷新 AccountWorkspace、点击查看/完成、左右初始布局、拖动缩放与 DIP 存盘、仅附着 Explorer 桌面且不设 Topmost；Native 合成 13 项及独立父窗/style 检查通过，默认入口先更新 app-sync-status，后在便捷入口阶段更新为 app-launcher，2026-10-13先更新为app-desktop-recovery，品牌阶段迁移为拾要，当前桌面/开始菜单/默认脚本和已启用Run指向app-card-controls。新版半透明圆角与空待办收紧已生效，真实正常重开及原小数位置恢复通过。托盘生命周期已修复，关闭隐藏、单实例唤回及菜单退出已实际通过；本轮同步状态新版已打开，主窗口保留设置页，QQ/接口保持运行，首次后台同步及重开去重通过。常驻调度与自动游戏名单已通过本地回归及扫描；真实 Framework 冷启动、首次后台读取、空闲保持连接及两卡片常驻已通过；自然固定时间另行核对，实际游戏暂停/合并补同步验收延后，单条真实任务的确认、卡片完成/撤销及重开已通过；同步状态和本次运行成功时间已实现并验证，完整编辑和可读变更记录/卡片变化标记已完成并通过自动/Native 合成验证，手动新增也已完成并通过自动/Native 合成验证，手动合并/保持独立/拆分已实现并验证，跨群可能重复建议也已实现并验证，消息缓存清理、本地数据备份恢复及桌面恢复已实现并通过合成验证，下一步为统一真实验收；OCR 队列及 M6 OCR 重分析后置，多屏、Explorer 重启、睡眠恢复及长期稳定性仍待完成。
6. SQLite 消息/候选/生命周期/路由和旧提醒计划/台账已回归；完整编辑、普通通知转任务、可读变更记录/卡片变化标记已接入并通过本地/页面与 Native Smoke 回归，手动新增已完成并验证，手动合并/保持独立/拆分已完成并验证，跨群可能重复建议已完成并验证，消息缓存清理、本地数据备份恢复及桌面恢复已完成并通过合成验证，下一阶段为统一真实验收。OCR 图片下载、OCR 队列和 M6 OCR 重分析改为后期独立项目，不作为当前阶段阻塞项；与本机数据泄露相关的额外安全增强同样后置。7 天只限制未关联缓存分析窗口，事项证据长期保留；统一验收后置到这些功能完成后。
7. 当前间距/独立任务栏新版PID52336运行，真实两卡片附着和保存位置/尺寸恢复通过，四向面板间距约2 DIP；任务完整记录、布局字节及界面偏好与重开前保护快照相同，四库integrity=ok。独立拾要任务栏实机核对通过，主窗口已唤回，实际桌面截图已查看。16:41历史快照为0个待办、1个已完成任务，75项未处理、1项已接受、1项已忽略。16:40～18:40两小时报告已结束，本轮仅核对completed/success标记及238样本，未逐样本验收，不能据此宣称稳定通过；报告位置见首段。16:23 QQ/应用退出及70未处理为上一段快照，退出原因未定。10月12日的1 个待办任务、46 项未处理待确认、1 项已忽略为历史基线；单条真实日期确认、卡片显示/完成/撤销/重开及原文入口通过。旧52群/9置顶/48个review类（含忽略）及0任务/通知、此前两次40项重开不重复均仅为历史。完整历史一次成功不代表持续实时或未来完整性，真实课程/日期仍待用户核对。旧通知呈现/点击和桌面probe保留历史，不代替正式两卡片验收。
8. 本机 Windows 11 专业版 x64 10.0.26200；项目专用 .NET SDK 10.0.401，开发使用 `.tools/dotnet/dotnet.exe`。Framework 曾加载并通过只读验证，用户确认聊天列表和窗口正常显示；当前实例与按次切换进度以首段最新记录为准；实时群事件限定范围通过（观察到 1 条），长期稳定性尚未验证。
9. **安静时段、系统提醒及自定义时间提醒入口已取消**；手机版尚未确定开发，不保留“暂未启用”的入口。
10. 2026-10-10 七项复核补充中，归档、账号隔离、课程关联修复、手动字段保护继续有效；系统在途发送、迟到/修复汇总及自定义精度默认安排随渠道取消。课程修复保留人工决定和历史台账，结果刷新卡片，不删旧任务或逐条通知。
11. **已确认：点击触发一次同步并在完成后恢复普通 QQ**。用户确认每次点击拾要时自动启动一轮同步（指从桌面快捷方式启动）；从系统托盘打开只显示窗口，不同步，也不切换 QQ；拾要已打开时，点击“自动同步”也启动一轮同步；同步完成后结束项目专用 NapCat Framework，恢复原本的普通 QQ。普通 QQ 已运行时允许自动退出，再在同步后重新打开普通 QQ。取消每天 08:00、12:00、20:00 定时同步，只在点击拾要或“自动同步”时同步；登录 Windows 的后台自启动只恢复本地界面和卡片，不切换 QQ。同步失败、超时或退出应用时也尝试恢复普通 QQ；游戏期间禁止同步，游戏导致错过同步时，结束游戏后补同步一次，重复点击合并为一轮。 旧常驻验证只作为历史证据；按次代码、完整验证、真实QQ接入及部署已完成，范围见当前接续。
12. **已确认：第一版仅使用两张桌面卡片**。2026-10-12 用户选择 A，取消全部 Windows 系统通知弹窗和声音；时间节点、新发现/变更、恢复/历史/迟到批次汇总及连接异常系统通知均取消，通知点击路由、权限恢复、自定义时间提醒和安静时段入口移出当前范围。主界面保留原文、变更记录和连接状态，普通群通知保留在 App“通知”列表；旧 ReminderPlanner、SQLite plans/ledger 和 probe 验证作为历史产物保留。正式桌面卡片首段已实现并验证，不等于整体端到端或长期稳定性完成；取消任务仍为已取消，不记完成。
13. **已确认：将电脑现有游戏全部加入检测，并定期扫描电脑游戏、同步检测名单**。默认应用启动及每日扫描一次，游戏期间延后目录扫描；不再要求用户逐个提供游戏名称。自动发现、缓存和进程检测已写入，实际扫描得到 19 个游戏安装目录；名单报告 `.tools/game-validation/installed-games.json`。按实际游戏进程保持延后，切回桌面不算退出游戏；全屏状态不能单独覆盖游戏，启动器不能直接算作正在玩。逐款实际游戏启动/退出、未知来源遗漏及后续安装场景仍需实机核对。
14. **按次切换已写入代码、验证并部署**。新流程使用 AppRuntime 请求合并、QqSyncSession 和 NapCatMessageHost.RunOnceAsync。完成/未补全后主动断开；异常、取消和五分钟超时尝试恢复普通 QQ。游戏中断取消原因保留，退出游戏后补一次；固定时刻已取消，后台登录启动不触发同步。真实 QQ 切换、正式按钮和入口验证通过；实际游戏与长期运行仍待验收。
15. **本阶段明确后置 OCR 与额外安全增强**。图片下载、附件存储、OCR 队列、OCR 重分析以及与本机数据泄露相关的安全强化暂不实现；现有 OCR worker 和离线调查保留为后期基础。QQ/NapCat 继续只读，软件不得向任何 QQ 群聊或私聊发布消息，这一边界保持为当前硬约束。

16. **已确认：公开源码，提供同学使用的软件版本，并由助手直接上传到用户GitHub供下载**。2026-10-14 用户选择A并明确授权上传；分发首次配置与路径适配已写入，公开仓库shiyao及v0.1.0-beta.1测试版已发布。不能将本机运行验证记为跨电脑分发验证，实际产物、命令与远端结果见首段。

## 7. 接续与同步方式

- 需求变化：更新 `docs/requirements.md` 的有效条款，同时更新本文快照、替换记录和变更记录。
- 实现进展：更新本文“接手先看”中的阶段与产物，记录实际完成内容及尚未完成的部分。
- 验证：写实际执行命令、结果和验证范围；文档检查不能写成 OCR、QQ 连接或提醒功能通过测试。
- 改动交付：每次编写或更新相关测试，全部测试和验证通过后创建对应的 Git commit；小任务不免除这些要求。
- 提出但未确认的新方案只记入“未决定事项”，确认后再更新有效需求。
- 交接前读取文件核对一致性；不能让新结论只留在聊天中。
- 用户希望每次只选择一个问题、选项同时写在正文、等待明确答复；不因等待时间过去就默认选项或进入下一题。

## 8. 变更记录

2026-10-15：用户确认不是手动退出，排查发现工具Job启用KILL_ON_JOB_CLOSE及Codex MSIX AppData虚拟化。退出未捕获直接事件/退出码，不宣称已完全证实根因；当前Explorer独立启动PID23888无包身份、不在工具Job内。真实物理99条审核中84条已处理记录、1条任务和187条其他历史操作完整保留；2笔合并审计核对14条原自动Pending到2主项及12个保留子项，另1条Pending保留，合计15项整理为3项。真实保护是在迁移后建立，四库/14表、4份设置、498个发布文件与卡片显隐均通过核对。收尾新增确认1、完成1、忽略2共4笔操作，当前pending0；原189笔操作、原任务与已处理记录保留，ReadVersions随已读变化，使用后独立快照/审计/卡片再次核对通过。包虚拟profile与物理profile分别保护、未合并；本轮仅更新记录和相关回归，beta.3程序与下载包未变。

2026-10-15：整条原消息审核和旧pending兼容修复已提交bc4e45f，Native重开夹具修正提交b9d109f；Core323、Infrastructure704、Windows456、Node175及119项Native/桌面检查全部通过，68/68规则基线高影响错误0。干净源码构建beta.3，ZIP与合成Setup验证通过，498个程序文件匹配。早先工具启动PID48588及82条review/167条操作/4份设置保持的结果仅属于包虚拟profile，后续物理profile核对见最新排查记录，不能沿用为真实用户数据结论。GitHub main与beta.3源码/119924077字节附件已公开，匿名下载HTTP200；保留另一视觉任务未提交修改，真实QQ未同步、未代用户处理事项。

2026-10-15：用户明确选择 A，替代此前 A/B 待选择状态；按一条原消息一个完整待确认项实施，兼容旧未处理拆分项，保留人工处理与拆分结果。前轮文档提交 77db15c 及定位验证保留为历史。



以下记录保留当时的决定和实际命令；2026-10-12 已取消的 Windows 通知条款、旧按次切换/结束恢复普通 QQ 方案及常驻待确认建议不再作为当前有效规则。

以下日期是记录整理日期，不推断此前各条聊天的实际发生日期。后续新变更按实际日期追加。

| 记录日期 | 变更 | 依据与状态 |
| --- | --- | --- |
| 2026-10-14 | 软件更新持续同步GitHub源码和下载包，beta.2已发布 | 用户明确要求，已有直接上传授权持续有效；规则、需求与新增回归已同步。Core306、Infrastructure695、Windows456、Node173、OCR25、Python打包4与解压版Native115通过；产品及规则提交2657e25推main，beta.2附件119916177字节uploaded、draft=false、prerelease=true，匿名下载HTTP200。保留原本机部署及个人数据 |
| 2026-10-14 | 所有消息列表改按最新原消息时间倒序，替代优先级/截止日期排序 | 主窗口各列表与两桌面卡片统一规则，手动任务用创建时间；Core306、Infrastructure695、Windows456、Node171、OCR25、Python打包4项及113项Native通过，正式原目录部署并后台重开。任务/忽略/历史和布局保留 |
| 2026-10-14 | 明确启动同步跳过已忽略原消息并保留已确认事项 | 复用已有持久化路由回执和事项状态；新增重开后按次同步三种任务状态及忽略撤销过期四项回归，Infrastructure694、Core306、Windows454、Node170、OCR25、Python打包4项通过。需求及交接同步；验证使用隔离合成账号，提交标识以Git日志为准 |
| 2026-10-14 | 用户选择A，公开源码并授权直接上传；首个测试版发布完成 | 新增便携QQ配置、首次Setup、独立下载包、使用说明与完整第三方许可；Core306、Infrastructure690、Windows454、Node170、Python打包4项及解压版113项Native通过。提交c221e66推main，公开仓库shiyao和v0.1.0-beta.1 Release含119915264字节uploaded附件；不含个人材料，本机正式入口与数据保留，跨电脑试用仍待反馈 |
| 2026-10-14 | 新增同学分发与GitHub下载目标 | 只读核对GitHub登录、无远端及本机QQ/NapCat路径依赖；源码公开方式待用户选择，分发包与远端发布尚未完成。同步有效需求、发布准备记录和状态回归，不把建议记为已发布 |
| 2026-10-14 | 确认按次自动接管、同步及恢复普通 QQ，取消定时同步 | 普通 QQ 运行时用户允许自动退出并在同步后恢复；只在点击拾要或“自动同步”时同步，取消 08:00/12:00/20:00。失败/退出尽力恢复，游戏延后合并，代码和验证进度分别见首段 |
| 2026-10-14 | 澄清桌面快捷方式启动范围，移除托盘同步 | 用户明确系统托盘打开不触发同步；两个托盘事件改为只显示窗口，110项Native和正式托盘实机核对通过，已部署到原入口；桌面启动和“自动同步”保持触发一轮 |
| 2026-10-14 | 完成标题栏交付核对并修正桌面验证等待上限 | 完整105项正式Smoke实测60.37秒、退出0，旧40秒等待会过早读报告；上限调整90秒后正式桌面脚本全部通过。PID61372经托盘正常退出，当前PID57432正常后台重开，真实标题栏黑色及独立身份再次核对，任务/布局/界面偏好保留、四库正常。产品提交c6b8a20，验证脚本提交bae11a6。 |
| 2026-10-13 | 主窗口标题栏和边框跟随主题 | 用户指出顶部白色标题栏突兀；DWM同步初始/切换/重开主题并保留原生控制。Windows419、Node152、最终正式Native105项及真实父窗/styles检查通过；原目录发布，两次托盘正常退出后最终PID61372运行，真实标题栏黑色、任务/布局/偏好保留、四库正常。原有Smoke编辑场景补回执等待；当前150%缩放的实际卡片位置按工作区Fit核对，详见当前接续。 |
| 2026-10-13 | 缩小吸附间距并修复独立任务栏分组 | 面板间距10 DIP改为约2 DIP，四向/DPI/紧凑卡片验证通过；进程、主窗口与两入口统一MessagePurify.Shiyao，实际拾要/Codex独立分组通过。Windows415、Node152、正式Native101项通过，原目录发布、数据保护后重开PID52336；布局、任务及界面偏好保留，四库正常。退出脚本NaN失败和进程标识不足的中间结果保留，不记为正常托盘退出；详见最新段。 |
| 2026-10-13 | 增加卡片四向吸附及底色透明度滑块 | 用户选择左右仅顶部平齐并保留高度，上下按可见高度衔接；16 DIP边缘阈值，拖离解除，原schema 1布局持久化。透明度0%～100%只改变底色，重开/备份恢复兼容。Windows410、Infrastructure681、页面79、正式Native100项通过，自包含发布及真实入口迁移/托盘正常退出重开完成；新版PID19968实际布局与任务记录未变，四库完整性正常，详情见最新接续。 |
| 2026-10-13 | 优化桌面卡片外观并修复小数布局恢复 | 用户反馈纯黑大框突兀；改半透明圆角、轻量列表/控件，空待办缩短可见高度。新增7项Native及小数坐标读取/保存回归；Windows391/391、Node151/151、最终代码91项Native及Release发布通过。入口/Run迁移并备份，旧实例正常托盘退出后保护四库及设置，新版PID41884正常后台运行；真实两窗口位置/尺寸与原布局一致，布局字节和任务记录未变、四库完整性正常，最终桌面截图已查看。稳定性及真实登录仍未验收；详见最新接续。 |
| 2026-10-13 | 产品显示名称改为拾要，制作并接入正式图标 | 用户要求有寓意的新名字与图标；采用拾取要事的命名及深青绿/米白/暖金折页消息图标。PNG、九尺寸ICO、提示词已保存，页面/窗口/托盘/卡片/exe及新名入口已接入；17:44迁移真实桌面/开始菜单和已启用Run，原入口与Run已备份、旧发布保留。Node151/151、Windows390/390、Native84项合成及发布通过；最终exe产品名与描述均为拾要，图标可被Windows提取。旧PID40208继续观察，新外观于下次正常退出重开显示，真实登录自启动及稳定性未验收；命名及外观尚未收到用户反馈，详见本文最新段 |
| 2026-10-13 | 更新正式入口到当前最新版 | 用户要求先完成入口更新；默认脚本、桌面/开始菜单及已启用的Run切到app-desktop-recovery，保留原开关、旧产物和QQ入口。实际入口属性、默认预检和两个快捷方式唤回同一实例通过；观察进程继续运行，Windows登录及完整连续观察不标已验收。原入口和Run备份于.tools/entry-upgrade/20261013-165509/。 |
| 2026-10-13 | 接续启动两小时真实只读观察 | 基线d5e0901，新的停机四库保护快照正常；Framework/最新应用重开、两卡片附着及观察初始采样通过。16:40启动现有工具、预计18:40结束，报告持续更新，尚未完成或验收；当前75未处理、人工记录与课程关系未变，默认入口/Run值保留。无自动通知，后续读取完整报告后再判定。 |
| 2026-10-13 | 无人值守首轮真实验收及只读观察工具 | 用户授权约两小时安全操作；停机四库保护快照、Framework冷启动/只读完整历史、正常后台卡片附着及托盘、5秒观察通过，任务记录和课程关系未改。当前0待办/1已完成/70未处理；16:23 QQ与应用已退出且原因未定，两小时连续观察未启动，不标稳定验收完成。默认入口及Run值保留；新增安全观察脚本、失败路径测试和使用说明；Node全量142/142、文档44项通过，详细证据见当前接续。对应提交说明 test: add safe unattended application observation，标识以Git日志为准。 |
| 2026-10-13 | 实施账号本地数据备份恢复 | 四库Backup API快照、设置页预览确认、恢复前自动包、目录/共用设置及启动回滚、旧请求隔离、原撤销时刻保留；Core306、Infrastructure675、Windows344、Node138及68项Native合成通过，未下载原图明确不完整，真实数据和默认入口未切换，下一步桌面恢复 |
| 2026-10-13 | 完成消息缓存清理 | 按原消息发送时刻严格 7 天边界，只删已提取并路由且无保留引用的临时消息；全状态任务/待确认/通知及递归引用保护，三库事务及暂停/异常回滚，保留路由回执和重读去重。历史批次自动清理、设置页入口及持久结果已接入；修复后续成功时页面旧错误残留。Core306/306、Infrastructure649/649、Windows318/318、Node133/133及发布版Native52项通过，产物app-cache-cleanup，截图已查看。真实账号缓存、默认入口和运行版本未操作；下一项备份恢复，再桌面恢复及统一真实验收。 |
| 2026-10-13 | 完成跨群可能重复建议 | 严格原文及原始可靠单Task候选、已确认同课程群关系生成派生建议；人工核对当前差异后合并或保持独立，排除共享证据、多行动、拆分家族及独立别名，引用上下文遗漏已真实提取复现并修正。页面/卡片/双版本/未读/草稿/重开接通；Core306/306、Infrastructure611/611、Windows280/280、Node125/125（页面66、文档40），发布app-cross-group-merge，Native45项success=true、synthetic=true。默认入口和真实QQ未切换；下一步缓存清理、备份恢复和桌面恢复，统一真实验收后置。 |
| 2026-10-13 | 完成手动合并、保持独立及拆分 | 双版本事务、字段/状态选择、优先级显式覆盖、旧 ID 跳转、证据及双方历史、独立决定、缺日期待确认和关联导航已接入。草稿/账号/未知保存重试及旧撤销保护回归通过；Core 306/306、Infrastructure 550/550、Windows 269/269、Node 116/116（页面58、文档39）；发布 app-task-structure，Native 合成 36 项 success=true、synthetic=true、退出 0。跨群自动合并建议协调、默认入口切换和统一真实验收仍后置。 |
| 2026-10-13 | 完成手动新增任务与保存确认保护 | 新增本地 `create-task`、手动来源持久化及旧 JSON 缺省 QQ；标题/课程/日期必填、钟点可选，默认自动分级或显式 P0～P3。共享课程选项、失败/快照草稿保留、账号隔离、幂等重试、保存结果未确认时先确认原请求、迟到响应不打断当前编辑均覆盖。Core 306/306、Infrastructure 501/501、Windows 236/236、Node 全量 102/102（正式页面 45/45、文档 38/38）通过；发布 `.tools/published/app-manual-task/`，Native 合成 25 项报告 `.tools/desktop-validation/manual-task-native.json` success=true、synthetic=true、退出 0。默认入口和真实 QQ 事项未切换，下一项为合并拆分及其余确定功能、备份恢复和桌面恢复，统一真实验收后置。 |
| 2026-10-13 | 完成可读变更记录与桌面卡片变化标记 | 从 `operations` 审计投影中文字段前后值、北京时间、关联原消息证据及历史顺序；延期/取消/新要求/编辑产生重要变化标签，催交补充仅追加证据；待确认变更保留正式值并可打开建议；`mark-read` 按任务版本确认。Core 306/306、Infrastructure 463/463、Windows 206/206、Node 全量 95/95（正式页面 39/39）通过；自包含发布 `.tools/published/app-change-history-final/`，Native 报告 `.tools/desktop-validation/change-history-native-final.json` success=true、synthetic=true、21 项通过。SmokeCheck 另修正了滚动条 Thumb 与缩放手柄的定位歧义。真实 QQ 编辑场景、默认入口替换和统一真实验收后置，下一步为手动新增、合并拆分、备份恢复和桌面恢复。 |
| 2026-10-13 | 完成完整六字段编辑及关联引用保护修正 | 用户确认编辑标题、课程、正式日期、钟点、时间类型和优先级。接入 `edit-task`、版本/操作幂等、原始证据与前后审计、手动时间保护、旧 JSON 优先级兼容、草稿版本冲突保留；刷新按起始账号/任务绑定；编辑或清空课程后同账号同群原证据引用仍可关联，标题匹配仍要求课程一致。Core 306/306、Infrastructure 455/455、Windows 203/203、Node 全量 90/90 及 Native 合成 17 项通过；自包含发布 `.tools/published/app-full-edit/` 无警告错误，报告 `.tools/desktop-validation/full-edit-native.json` success=true、synthetic=true。文档回归新增检查后为 36/36，`git diff --check` 通过。真实 QQ 编辑场景、默认入口替换和正在运行版本未操作，下一步为可读变更记录/卡片变化标记及手动新增。
| 2026-10-12 | 将 OCR 调整为后期独立项目并暂缓额外安全增强 | 用户明确认为 OCR 扫描可后期加入，当前先完成编辑、变更标记、通知转任务、合并拆分、备份恢复和桌面卡片恢复；本机数据不外传，额外安全增强不作为当前门槛。保留 QQ 只读及不得向群聊/私聊发送消息的硬约束；未修改生产逻辑，仅同步需求、交接、应用说明和测试说明。`node --test tests/project-rules.test.mjs` 35/35、`git diff --check` 均退出 0 |
| 2026-10-12 | 调整工作顺序：先完成确定功能，再统一验收 | 用户明确要求所有实现工作完成后再进行验收。后续顺序为自启动、OCR 队列、完整编辑/变更标记/通知转任务、合并拆分、备份恢复、桌面恢复；自然固定时刻、实际游戏暂停/补同步、长期稳定性和完整真实端到端验收后置。关机/睡眠漏同步规则仍未决定 |
| 2026-10-12 | 完成消息提纯自身桌面/开始菜单入口并改为直接 exe 启动 | 首轮隐藏 PowerShell 快捷方式被火绒隔离，分类 `TrojanDownloader/LNK.Agent.g`；只读证据未改安全设置。新增普通 exe 入口、NapCat 本机配置回退和安全错误提示；快捷方式/入口 7/7、配置 11/11、Windows 164/164、Node 80/80；publish `.tools/published/app-launcher/`、Native 13 项、默认入口检查、桌面/开始菜单实际启动、单实例唤回、真实只读与 UIA 时间检查均通过。QQ 快捷方式未改；自然固定时刻、实际游戏暂停/补同步和长期稳定性仍待验收 |
| 2026-10-12 | 完善常驻同步状态、成功时间和下次计划时间，发布并重开新版 | 等待/同步/游戏/未补全/连接故障区分；本次运行真实成功批次更新时间，失败及重连不伪造时间；北京时间下一时刻复用调度器。Infrastructure 428、Windows 153、Node 75、Native 13 通过；真实只读及 UIA 时间、两卡片/托盘通过，默认 app-sync-status、PID 40840 设置页。验证脚本改为限定托盘和应用窗口搜索，旧版正常退出；QQ 未动，固定时刻和实际游戏验收继续保留 |
| 2026-10-12 | 用户选择 A，确认常驻后台同步；要求自动维护本机游戏名单 | 常驻 QQ/NapCat Framework、固定时间后台读取替代按次切换/恢复普通 QQ；08:00/12:00/20:00、游戏禁止同步和退出合并补一次保持有效，中途游戏暂停。要求现有游戏全部纳入检测及定期扫描，不再询问游戏名称；默认启动及每日扫描、游戏时延后。调度、常驻宿主、自动游戏发现与缓存已接入，418/141/301 C#、OCR 25、demo 108 及 Native 13 通过；实机扫描19个游戏目录，已发布 app-scheduled-sync 并更新入口。真实 QQ/逐款游戏/长期验收未完成，本轮未启动/关闭真实 QQ |
| 2026-10-12 | 核对后台同步可行性，接口常驻仅作为待确认建议 | 后台读取已有代码，按次切换仍涉及 QQ 重开；建议接口随 QQ 运行、固定时间后台读取。未确认前保留原接入、游戏限制和补同步规则；未改生产代码或实际切换 QQ，资源占用与游戏影响未实测 |
| 2026-10-12 | 用户选择 A，游戏结束后补同步一次 | 替代游戏漏同步处理待确认；同一游戏期间错过多个时刻合并为一次补同步。同步有效需求、说明及测试，文档 28/28；官方资料支持运行程序查询，全屏状态不能单独覆盖所有游戏，建议检测常玩游戏进程。调度与检测尚未实现，常玩游戏名称待提供 |
| 2026-10-12 | 确认每天三次同步，打游戏时禁止同步 | 用户指定北京时间 08:00、12:00、20:00；替代同步时刻待确认。游戏期间不得为同步切换或重启 QQ；漏同步后的补同步规则待确认。仅同步需求、交接及相关说明/测试，调度和游戏检测尚未实现 |
| 2026-10-12 | 按用户要求恢复普通 QQ 桌面图标 | 加密的原备份不能直接复制，重新创建普通 QQ.lnk：QQ.exe、空参数、QQ 图标及正常窗口；文件持续存在，Explorer 桌面列表含 QQ，Encrypted=false。新增恢复脚本和用例，快捷方式测试 3/3；固定同步方向保留，不继续集成快捷方式改造，未退出 QQ 或修改火绒 |
| 2026-10-12 | 用户停止 QQ 快捷方式方向，回到固定时间自动同步 | 用户看到火绒提醒并怀疑其删除快捷方式；实际 QQ.lnk 缺失，重建及原备份复制恢复未成功。停止此方向，更新有效需求为到点接入、同步落盘、结束恢复普通 QQ；具体时间和使用冲突规则待确认，调度尚未实现。仅同步文档和回归，不实际切换 QQ 或调整火绒 |
| 2026-10-12 | 简化 QQ 重开入口，复用原桌面 QQ 图标 | 原快捷方式先备份，再指向隐藏的 Framework 入口，保留 QQ 名称和图标；已运行时打开已有窗口，不退出 QQ。新增两项快捷方式/激活回归，Node 八文件 65/65；真实快捷方式启动 ExitCode=0、3001 监听仍归原 QQ PID。未重测完全退出后的冷启动，不等于 Windows 自启动已完成 |
| 2026-10-12 | 修复用户找不到托盘图标的问题并切换默认入口 | 启动回调等待 ListenAsync，避免提前释放 NotifyIcon；实际旧版图标检查红、修复版绿。Windows 141/141、Node 63/63、Native 13 项、桌面父窗/style、隐藏/唤回/单实例及菜单退出（ExitCode=0）通过。默认 app-tray-fix，新版已打开、QQ 保持运行；真实只读复查 9 个置顶群/48 项待确认，未确认课程或日期。OCR 仅调查，M3/M8 与恢复/多屏不标完成 |
| 2026-10-12 | 完成正式桌面卡片首段、取消提醒设置入口并发布默认桌面版 | 当前账号任务/待确认、查看/完成、主题、DIP 布局和空待确认隐藏已接入；Windows 141/141、Infrastructure 375/375、Node 63/63、Native 合成 13 项、Python 父窗/style 和入口检查退出 0。单次审查修复时钟 signature 的 past 漏项，DPI 重载不自动覆盖坏布局；最终 publish 无警告/错误。默认 app-desktop，旧 app 窗口/QQ 保持运行；真实 QQ 数量沿用上段，未确认课程/日期，多屏/睡眠/Explorer/长期稳定性仍未验收，M3/M8 不标总体完成，Git 提交由 root 统一执行 |
| 2026-10-12 | 用户选择 A，确认第一版仅用两张桌面卡片 | 取消全部 Windows 系统通知弹窗和声音及相关设置/验收，保留普通 App 通知、原消息与旧计划/台账；同步有效正文、设计和总计划，正式两卡片实施中，验证结果待实际运行后记录 |
| 2026-10-12 | 用户提出仅用两张桌面卡片提醒，询问意见（随后已确认 A） | 声音功能继续取消；是否取消全部 Windows 弹窗待确认，建议只记录于第 6 节第 12 项。当前产品代码及有效需求保持原状态，依赖该选择的 Windows 接入等待明确答复；文档相关检查 25/25、diff 检查通过，本轮以对应 Git commit 保存 |
| 2026-10-12 | 完成持久化提醒第一段及失败汇总成员变更修复 | 新增账号提醒库 schema 1、正式详情设置/计划、节点及投递台账、恢复和串行提交协调。最终 Infrastructure 375/375，服务 46/46、工作区提醒 24/24；本段 Windows 96/96、Core 301/301、OCR 25/25、demo 108、Node 七文件 59/59 通过；最终重新发布与真实合成 WebView2 八项通过。P1 四项先红后绿，窄复现两路有效 B 均仅提交一次。默认入口改为 app-reminders，旧产物/窗口与 QQ 保留；实际 Windows 提醒尚未开启，详细命令及边界见第 1 节 |
| 2026-10-12 | 完成正式应用首段并接入真实 QQ 只读消息宿主 | AccountWorkspace、NapCatMessageHost、WPF/WebView2 页面及双击入口已写入；Infrastructure 305/305、Core 301/301、Windows 77/77、OCR 25/25、demo 108、Node 七文件 53/53、合成真实 WebView2 七项通过。用户退出 QQ 后重开 Framework，真实读取 9 个置顶群、40 项待确认，跨进程重启无重复，正式窗口已打开；P0 饱和后的后果加级修复有回归。真实课程确认后的任务验收、持续实时及提醒/OCR/桌面等仍未完成，详见第 1 节 |
| 2026-10-12 | 用户再次继续，完成 M4 第四段可靠关联变更与保守消息顺序 | 新入口同消息事务内提取/更新/回执，持久化目标及当前群来源授权；迟到旧/同秒/缺来源时间转待确认。新增关联 30 项，候选路由 36、生命周期 62；专项 128/128、Infrastructure 251/251、Core 296/296、Windows 57/57、OCR 25/25 与 pip check、Node 五文件 32/32、demo 108 项实际通过。通知最终版本及旧催交接受回退期限问题均先红测后修正；真实宿主/UI/提醒投递与 M6 仍待完成，完整命令见第 1 节 |
| 2026-10-12 | 用户恢复工作，完成 M4 第三段消息候选首轮路由 | 新增账号路由库及 32 项真实 SQLite 检查，生命周期增至 62；专项 95/95、Infrastructure 217/217、Core 296/296、Windows 57/57、OCR 25/25 与 pip check、Node 五文件 31/31、demo 108 项实际通过。课程确认撤销/改变使旧 Task/Notice 转待确认；当时自动关联变更、M6 重分析协调和真实宿主未完成（自动关联变更后续在第四段完成）；完整产物与实际验证命令见第 1 节 |
| 2026-10-12 | 用户要求保存当前工作，明天继续 | 已核对实现基线 `80274aa` 及干净工作区；补充暂停与恢复顺序、下一步自动路由。仅维护交接及相关文档回归，产品需求不变；当时保存并等待恢复，本次已继续 |
| 2026-10-12 | M4 第二段完成本地正式事项生命周期（11 日开始，12 日凌晨提交） | 新增 AccountTaskStore、正式事项/待确认模型和 61 项真实 SQLite 检查；Infrastructure 184/184、Core 296/296、Windows 57/57、OCR 25/25 与 pip check、Node 五文件 30/30、demo 108 项均通过。完成/忽略撤销、关联建议、证据、版本及操作幂等已落盘；候选自动路由、正式界面、提醒调度和真实 QQ 宿主联调尚未完成。详细产物与命令见第 1 节 |
| 2026-10-11 | M4 第一段写入账号独立 SQLite 消息收件箱与本地规则处理器 | 新增 `AccountMessageStore`、`LocalMessageProcessor` 及真实临时 SQLite 回归；Infrastructure 123/123（存储 23、处理器 22），Core 296/296、Windows 57/57、OCR 25/25、Node 五文件 29/29、demo 108 项和 `git diff --check` 均通过。原消息、来源、规则候选和处理版本可在重启后保留；正式事项、提醒调度、OCR 队列与真实 QQ 接入仍未完成 |
| 2026-10-11 | 用户新增只读取 QQ 置顶群的规则，替代所有群聊范围；写入统一过滤及 Framework 读取桥 | 实时、历史、重连同一范围；未知置顶状态停止。Infrastructure 78/78、桥 4/4；真实 9 个置顶群两轮 18/18 到达日期边界、去重 341、重连 1/1、退出码 0；全部自动回归通过。正式应用、真实动态置顶和长期实时稳定性仍未完成，详见第 1 节 |
| 2026-10-09 | 汇总 NapCat、所有群聊、本地规则、课程/机器人确认、图片辅助、任务生命周期、优先级和补漏方案 | 根据既有对话和需求文档整理；需求已确认，软件未实现 |
| 2026-10-09 | 记录待确认常驻卡片取代定时汇总，普通通知仅进入主界面列表 | 根据用户最新明确选择；旧方案已替换 |
| 2026-10-09 | 新建本交接文档及根目录协作规则，并为需求文档加入同步说明 | 用户要求每次新增内容同步写入，避免长上下文导致遗漏；文档维护机制已建立 |
| 2026-10-09 | 读取并核对三份项目文档，修正根目录规则中的固定阶段描述，避免后续进度变化后留下旧状态 | 执行 `Get-Content -LiteralPath <文档绝对路径> -Encoding UTF8`，三份文件均返回退出码 0；核对需求与交接快照一致。验证范围仅为文档，不包含应用功能 |
| 2026-10-09 | 在新对话中接续项目，核对实际文件，并提出桌面两张卡片上下/左右排列这一项待选问题 | 执行 `Get-Content -LiteralPath 'docs/requirements.md' -Raw -Encoding UTF8`、`Get-Content -LiteralPath 'AGENTS.md' -Raw -Encoding UTF8`、`rg --files -g '!node_modules' -g '!.git'` 和 `Get-ChildItem -LiteralPath . -Force`，均退出码 0；实际仍只有三份项目文档，应用未开始。排列方案尚未确认。未找到 `maintain-context-hygiene` 技能，按项目规则手动核对需求、交接与实际文件一致性 |
| 2026-10-09 | 用户回复 B，确认桌面两张卡片左右排列；同步需求与交接正文，下一项讨论主界面导航布局 | 左右排列是已确认需求，界面尚未实现；此前上下排列只是建议。主界面左侧导航/顶部标签仍待用户选择。执行 `rg -n '左右排列\|左右并排\|当前阶段\|主界面导航\|尚未得到用户选择' docs/requirements.md docs/HANDOFF.md`（表格中的反斜杠用于转义竖线），退出码 0，确认两份文档的当前决定及待选问题一致；验证范围仅为文档 |
| 2026-10-09 | 用户回复 A，确认主界面采用左侧导航；同步需求与交接正文，下一项讨论事项详情的展示方式 | 已确认导航包含任务、待确认、通知、已完成和设置，右侧显示所选页面；界面尚未实现。右侧详情栏/独立弹窗仍待选择。执行 `rg -n -e '左侧导航' -e '详情' docs/requirements.md docs/HANDOFF.md`，退出码 0，核对两份文档的已确认布局与待选问题；验证范围仅为文档 |
| 2026-10-09 | 用户回复 A，确认事项详情采用主界面右侧详情栏；同步需求与交接正文，下一项讨论桌面卡片的显示层级 | 保留事项列表，右侧查看原文、图片及编辑信息；界面尚未实现。卡片仅在桌面显示/始终置顶仍待选择。执行 `rg -n -e '右侧详情栏' -e '显示层级' docs/requirements.md docs/HANDOFF.md`，退出码 0，核对两份文档的已确认详情布局与待选问题；验证范围仅为文档 |
| 2026-10-09 | 用户回复 A，确认桌面卡片仅在桌面显示；界面基础布局已确认，下一项讨论截止提醒发送时刻 | 已同步需求与交接正文，应用尚未实现。执行 `rg -n -e '仅在桌面显示' -e '发送时刻' -e '09:00' docs/requirements.md docs/HANDOFF.md` 和 `rg --files -g '!node_modules' -g '!.git'`，均退出码 0；核对有效决定、待选提醒时刻及实际仍只有三份项目文档。完成本轮界面布局里程碑的文档与实际文件一致性检查；验证范围仅为文档与文件清单 |
| 2026-10-09 | 用户明确按时间精度安排提醒：有具体时刻则提前提醒，仅有日期则前一天中午 12:00 提醒 | 已同步需求与交接正文，替换原来统一提前 3 天/1 天/当天的安排及固定提醒时刻的待选建议；有具体时刻的提前间隔仍未确认。执行 `rg -n -e '12:00' -e '提前间隔' -e '提前提醒间隔' -e '替代原先' -e '无法确定截止日期' docs/requirements.md docs/HANDOFF.md`，退出码 0，核对新规则、日期精度及下一待选问题；验证范围仅为文档，提醒功能尚未实现 |
| 2026-10-09 | 用户回复 A，确认有具体时刻时提前 1 天（24 小时）及 2 小时各提醒一次；下一项讨论逾期后的提醒频率 | 已同步需求、交接快照及替换规则，截止前提醒安排已确认，提醒功能尚未实现。执行 `rg -n -e '24 小时' -e '逾期后的提醒频率' -e '每天中午' -e '当前阶段' docs/requirements.md docs/HANDOFF.md`，退出码 0，核对有效提醒间隔、阶段及待选逾期频率；验证范围仅为文档 |
| 2026-10-09 | 用户回复 B，确认逾期后仅保留卡片高亮，不重复弹 Windows 通知；下一步制作界面原型 | 已同步需求与交接正文，基础布局与提醒安排已确认，实际应用尚未实现。执行 `rg -n -e '不重复弹' -e '界面原型' -e '当前阶段' docs/requirements.md docs/HANDOFF.md`，退出码 0，核对逾期规则与下一步一致；验证范围仅为文档 |
| 2026-10-09 | 新增单文件可点击界面原型与主界面、桌面卡片预览图；补齐逾期卡片的原截止时间显示 | 新增 `prototype/index.html`、`prototype/previews/main.png`、`prototype/previews/desktop.png`。执行 `node C:/Users/Administrator/AppData/Local/Temp/msg-purify-prototype-qa.mjs`，最终退出码 0、19 项检查通过；查看两张预览图，完成原型里程碑的有效需求、交接与实际产物一致性核对。原型已验证，真实 QQ、OCR、Windows 驻留及系统提醒尚未实现 |
| 2026-10-10 | 用户认可原型，并新增浅色/深色自由切换及过渡动画需求 | 已同步需求与交接正文；主题功能已实现并完成验证 |
| 2026-10-10 | 用户要求深色模式改为纯黑背景 | 已同步需求与交接正文；原型深色配色已调整，待重新验证 |
| 2026-10-10 | 完成纯黑深色主题调整并重新验证 | 更新 `prototype/index.html` 的深色配色，生成 `prototype/previews/main-dark.png` 和 `prototype/previews/desktop-dark.png`；执行 `node C:/Users/Administrator/AppData/Local/Temp/msg-purify-prototype-qa.mjs`，退出码 0、23 项检查通过，确认主界面和桌面背景计算颜色均为纯黑；验证范围为浏览器原型 |
| 2026-10-10 | 新增按钮按压过渡和卡片悬停动画需求 | 已同步需求与交接正文；`prototype/index.html` 已加入按钮按压缩放、按钮悬停阴影、任务卡片与桌面卡片悬停抬升、桌面卡片内条目平移动效，并尊重减少动画设置；执行 `node C:/Users/Administrator/AppData/Local/Temp/msg-purify-prototype-qa.mjs`，退出码 0、26 项检查通过 |
| 2026-10-10 | 用户选择 A，确认真实 Windows 应用采用 C# WPF 路线 | 将实现路线确定为 C# WPF + WebView2：复用现有 HTML/CSS/JS 原型，WPF 承担托盘、启动、通知、持久化、NapCat、提醒调度和桌面卡片层级；RapidOCR 规划为独立本地工作进程。路线已确认，尚未开始真实应用实现 |
| 2026-10-10 | 用户要求先完成所有主要部分的设计讨论，再开始写代码 | 后续按设计讨论、设计文档确认、实现计划、实现的顺序推进；当前不新增代码 |
| 2026-10-10 | 用户说明自己是非技术用户，希望只被询问前端操作和提醒等可感知的定制内容 | 后端数据、推送机制、通信和打包方案由开发者自行选择；后续问题聚焦日常使用体验 |
| 2026-10-10 | 用户选择 A，确认首次启动显示简短引导 | 引导完成初始连接、课程/群关联确认和提醒预览；以后登录 Windows 后在后台运行 |
| 2026-10-10 | 用户选择 A，确认点击 Windows 提醒后直接打开对应事项详情 | 提醒点击后进入具体事项的处理页面，减少从列表中再次查找的步骤 |
| 2026-10-10 | 用户选择 A，确认事项较多时提供搜索和简单筛选 | 支持按标题、课程、来源群或状态查找 |
| 2026-10-10 | 用户选择 B，确认主界面默认优先打开待确认页 | 有待确认事项时进入“待确认”，否则进入“任务” |
| 2026-10-10 | 用户选择 A，确认详情栏图片以缩略图显示，点击后放大 | 保持主界面整洁，同时保留查看原图的入口 |
| 2026-10-10 | 用户选择 A，确认 OCR 文字默认折叠 | 需要核对时手动展开，图片日期仍须单独人工确认 |
| 2026-10-10 | 用户选择 A，确认 OCR 日期作为建议显示，人工确认后才生效 | 可修改或拒绝 OCR 建议，不能自动覆盖正式截止时间 |
| 2026-10-10 | 用户选择 A，并将完成后的撤销时间调整为 1 小时 | 任务点击“完成”后立即完成，1 小时内可撤销；该行为尚未实现，属于待开发交互 |
| 2026-10-10 | 用户将完成后的撤销按钮文字改为“撤销” | 即时提示和 1 小时内的已完成记录统一使用“撤销” |
| 2026-10-10 | 用户选择 A，并将忽略待确认事项的撤销时间调整为 1 天 | 误忽略后 1 天内可撤销，撤销后恢复到待确认列表 |
| 2026-10-10 | 用户选择 A，确认新内容显示未读数量和小红点 | 左侧导航、桌面卡片和对应列表可感知新内容 |
| 2026-10-10 | 用户选择 A，确认打开并查看后自动标记已读 | 无需额外点击“标记已读”按钮 |
| 2026-10-10 | 用户选择 A，并要求在设置中保留关闭行为自定义选项 | 默认关闭主窗口后隐藏到托盘；用户可改为直接退出 |
| 2026-10-10 | 用户选择 A，确认首次打开时跟随 Windows 系统主题 | 浅色/纯黑深色主题仍可在设置中自由切换 |
| 2026-10-10 | 用户选择 C，确认桌面卡片首次由软件根据屏幕空间自动摆放 | 后续记住用户拖动和调整大小后的布局 |
| 2026-10-10 | 用户明确作业、考试和提交类任务需要系统声音，其他事项不需要 | 声音规则按事项是否需要完成区分 |
| 2026-10-10 | 用户选择 A，确认声音任务在每个提醒节点各播放一次 | 新任务发现、提前 1 天和提前 2 小时等已安排节点各播放一次 |
| 2026-10-10 | 用户选择 A，确认设置中提供任务声音提醒总开关 | 关闭声音时仍保留视觉通知和桌面卡片提醒 |
| 2026-10-10 | 用户选择 A，确认自动启动默认开启、设置中可以关闭 | 已同步需求与交接正文；自动启动和开关尚未实现。实际验证命令：`rg -n -e '自动启动默认开启' -e '自动启动开关' -e '错过截止前提醒' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档。核对当前文件仍为原型和文档，继续设计讨论，不开始写应用代码 |
| 2026-10-10 | 用户选择 A，确认恢复运行后补发一次汇总提醒 | 仅包括错过截止前提醒、尚未截止且仍待处理的任务；已逾期任务仍仅卡片高亮。需求已确认，功能尚未实现。实际验证命令：`rg -n -e '补发一次汇总提醒' -e '单独调整提醒' docs/requirements.md docs/HANDOFF.md`，退出码 0；仅核对文档同步 |
| 2026-10-10 | 用户选择 A，确认允许单条任务自定义提醒时间 | 任务详情可单独调整提醒，其他任务沿用默认规则；不改变截止时间或其精度。需求已确认，功能尚未实现。实际执行 `rg -n -e '单独调整' -e '单条任务自定义提醒' -e '下一项使用体验讨论' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档 |
| 2026-10-10 | 用户选择 A，确认任务变更后额外发一次系统通知 | 明确关联的延期、取消和提交要求修改更新原任务、标出变化，并发一次系统通知说明变化；同一变更不重复通知。需求已确认，功能尚未实现。实际执行 `rg -n -e '同一变更不重复通知' -e '无法接收 QQ 新消息' -e '用户选择 A，确认任务变更' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档 |
| 2026-10-10 | 用户选择 A，确认 QQ 消息连接持续断开超过 5 分钟时提醒一次 | 界面显示连接状态，自动尝试恢复；同一次持续断线只通知一次，恢复后重新计时。需求已确认，功能尚未实现。实际执行 `rg -n -e '持续断开超过 5 分钟' -e '手动新增任务' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档 |
| 2026-10-10 | 用户选择 A，确认支持手动新增任务 | 可补录口头布置或漏识别的事项，填写标题、课程和截止时间，来源标注为“手动添加”，使用统一的优先级、提醒及完成规则。需求已确认，功能尚未实现。实际执行 `rg -n -e '手动新增任务' -e '下一项使用体验讨论' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档 |
| 2026-10-10 | 用户要求先保留安静时段入口，未来若开发手机版再讨论 | Windows 第一版仅规划保留“暂未启用”的入口，暂不实现按时段自动静音；手机版尚未确定开发。已同步需求和交接，未修改应用代码，入口尚未实现。实际执行 `rg -n -e '安静时段' -e '完整设计供用户核对' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档 |
| 2026-10-10 | 写入 Windows 第一版完整设计草稿并同步需求和交接，替换“下一步整理设计”的旧进度 | 数据、接入、OCR、提醒、桌面、恢复与安装方案已汇总，三项使用行为及整体设计仍待确认；应用代码未开始。修正新 P0/P1 通知与逾期仅高亮的歧义，历史首次发现的逾期任务也不弹新任务通知。实际文档核对命令与结果见第 1 节；仅验证文档和文件状态，下一项只询问取消任务的去向 |
| 2026-10-10 | 用户选择 A，确认明确取消的任务移出待办和桌面卡片 | 停止该任务后续提醒，保留“已取消”记录及原始消息，不记为用户已完成；取消变更仍按既有规则通知一次。需求、交接及设计稿已同步，尚未实现。实际执行 `rg -n -e '取消任务的处理已确认' -e '保留“已取消”记录' -e '紧接着只问待选二' -e '两处行为' -e '自定义提醒' docs/requirements.md docs/HANDOFF.md docs/superpowers/specs/2026-10-10-windows-app-design.md`，退出码 0；仅核对文档。下一项讨论自定义提醒遇到延期，两种方案仍待确认 |
| 2026-10-10 | 用户选择 A，确认延期后自定义提醒保持原来的提前间隔 | 自定义提醒随新截止时间调整，未自定义任务重新计算默认节点，旧截止安排失效。已同步需求、交接及设计稿，尚未实现。实际执行 `rg -n -e '保持原来的提前间隔' -e '旧截止安排失效' -e '紧接着只问待选三' -e '最后一项行为' docs/requirements.md docs/HANDOFF.md docs/superpowers/specs/2026-10-10-windows-app-design.md`，退出码 0；仅核对文档。当前文件清单仍为原型和文档，人工核对实际状态一致；最后一项讨论已完成任务收到明确新行动要求的处理，整体设计尚未确认 |
| 2026-10-10 | 用户选择 A，确认已完成任务收到明确新行动要求后自动恢复待办 | 标出新要求并按规则继续提醒，保留此前完成记录及新旧证据；普通补充不恢复，关联或有效截止信息不确定时仍待确认。三项使用行为均已确认，需求、交接及设计稿已同步；阶段改为等待整体核对，整体设计尚未得到确认，应用代码未开始。实际执行 `rg -n -e '自动恢复为待办' -e '保留此前完成记录' -e '整体设计确认' -e '整体核对' -e '第 13' docs/requirements.md docs/HANDOFF.md docs/superpowers/specs/2026-10-10-windows-app-design.md`，退出码 0；仅核对文档。结合 `rg --files -g '!node_modules/**' -g '!bin/**' -g '!obj/**'` 返回的原型及文档清单，完成本里程碑的人工上下文核对；下一步只请用户确认或修改整体方案 |
| 2026-10-10 | 用户提交下载目录新版设计稿，要求检查是否仍有缺陷；完成文档复核 | 按行号读取 442 行附件并与有效需求及实际文件核对，两项独立只读检查分别覆盖提醒/状态及产品一致性；复核收敛为第 6 节的 5 处流程缺口和 2 处新增提醒行为差异，均未记为新增已确认需求。未修改附件、有效需求、项目内设计稿或应用代码，只按持续维护要求更新交接。实际执行 `rg -n -e '归档' -e '账号.*通知' -e '通知.*账号' -e '重新分析' -e '重新检查' -e '关联.*修改' docs/requirements.md 'C:/Users/Administrator/Downloads/2026-10-10-windows-app-design.md'`，退出码 0；结合带行号读取核对缺口。通过 agent-reach 网页阅读和 web 工具查看微软通知发送/管理及自包含部署官方说明，未运行真实通知。实际执行 `rg -n -e '完整设计复核' -e '442 行' -e '附件复核发现' -e '并发缺口' -e '迟到消息' -e '行为差异一' -e '行为差异二' docs/HANDOFF.md`，退出码 0；交接记录核对通过。人工核对上下文与文件状态一致，整体设计仍等待确认 |
| 2026-10-10 | 用户确认“都加进去”，整合七项复核补充 | 已更新 `docs/requirements.md`、项目设计稿、本文及下载目录设计稿；设计基稿采用用户提供的新版，七项补充落实到流程、数据、调度和验收。两条提醒策略由建议改为有效规则，旧概括由字段保护和精度兼容条件限定；普通技术参数由开发者确定。实际执行 PowerShell 文档核对，命令与最终结果见第 1 节：三份项目文档、14 节设计及七项验收通过，项目与下载副本正文一致，链接有效，最终退出码 0。最初两次检查分别因“不兼容/小时规则失效”和“持久化小批次/持久化迟到小批次”的匹配表述不同退出码 1；读取对应正文确认规则已存在后修正检查条件，未用改产品规则掩盖核对问题。人工完成里程碑上下文核对；应用代码未开始，整体设计仍待核对 |
| 2026-10-10 | 用户要求每次改动更新测试、全部验证通过并创建对应 Git commit | 在现有 `AGENTS.md` 加入“注意事项”，保留原协作规则，并明确新要求优先于小任务测试豁免；同步有效需求及交接。保存可重复运行的浏览器检查，新增文档检查及减少动画偏好覆盖：6 项文档检查、27 项浏览器检查全部通过，两份测试脚本语法检查通过。初始化本地 Git 并保存首个项目基线，实际命令与诊断记录见第 1 节；未改原型应用代码。下一步仍为整体设计确认，之后才编写实现计划和真实应用代码 |
| 2026-10-10 | 用户选择 A，整体设计已确认，制定实施计划 | 更新需求、项目设计、下载副本和交接的有效状态；写入 M1～M9 总路线及 M1 三任务详细计划，31 项设计验收场景均有映射。只读复核未检测到 .NET SDK、已检测到 Chrome/WebView2，未安装依赖或接入 QQ。更新并实际运行 7 项文档检查、27 项浏览器检查，均退出码 0；脚本语法及 diff 检查通过，范围为文档和原型。真实应用代码未开始，下一步执行 M1；不再把整体确认写成待选事项 |
| 2026-10-10 | 用户询问是否应先看 demo | 核对现有原型后建议先补齐后续交互再供体验；只记为待决定建议，整体设计确认不回退，不把旧原型写成新版完整 demo。强化当前阶段检查，要求需求、交接与设计均明确应用实现尚未开始。实际执行 `node --test tests/project-rules.test.mjs`，退出码 0、7 项通过；`node tests/prototype-qa.mjs` 退出码 0、27 项通过；`node --check tests/project-rules.test.mjs`、`git diff --check` 均退出码 0。本轮不修改应用或原型代码，验证仅为文档和既有浏览器原型 |
| 2026-10-10 | 用户选择 A，补齐新版 demo 供体验 | demo 先行由建议转为有效顺序；新增普通脚本、体验说明及撤销／归档／提醒／变更／搜索／新增／图片／设置等示例交互，演示进度改为浏览器本地保存，刷新不重置撤销计时。更新实际测试为 8 项文档与 78 项浏览器检查，全部通过，四张预览已生成并查看；具体命令及范围见第 1 节。真实应用代码尚未开始，下一步先收集体验反馈，再接续 M1 |
| 2026-10-10 | 用户反馈右栏无法滚动、找不到待确认提交入口 | 复现并修复 Grid 最小高度造成的裁切，独立滚动详情内容，固定底部确认按钮并明确任务／通知去向，验证失败时提示可见。最终 86 项浏览器与 8 项文档检查通过，脚本语法及 diff 检查通过；新增待确认预览并查看。普通刷新可加载修复且保留已有演示数据，真实 QQ／Windows 功能尚未接入 |
| 2026-10-10 | 用户要求两处课程下拉内容同步并支持自定义 | 筛选、待确认、编辑及手动新增任务共用课程／分类选项，新增后同步、刷新及重置保留，取消和同步不丢失草稿。新增 16 项相关浏览器检查及课程预览，最终 102 项浏览器与 8 项文档检查通过，脚本语法及 diff 检查通过；需求、交接和说明已同步。真实应用代码尚未开始，继续供用户体验 |
| 2026-10-10 | 用户反馈 demo 基本满足要求，询问下一步 | 同步演示基本认可状态，说明按既有计划先实施 M1 核心基础，再推进识别与真实集成。更新文档状态检查；本轮仅维护文档及测试，真实应用代码尚未开始 |
| 2026-10-10 | 用户确认 QQ 已在电脑登录并授权准备完成后开始编写代码；完成 M1 Task 1 | 在 codex/m1-core-foundation 分支安装项目专用 SDK 10.0.401、锁定依赖并编写日期／时刻／时区模型；10 项核心、9 项文档和 102 项浏览器检查通过，实际命令见第 1 节。提醒／状态核心继续实施，真实 QQ 接入尚未验证 |
| 2026-10-10 | 完成 M1 Task 2 默认及自定义提醒计算 | 新增提醒纯函数与 33 项测试，合计 43 项核心检查通过；日期精度不造时刻，小时失效返回默认及 NeedsReview，关闭状态保留。校正计划示例以保证提前节点严格早于精确基准；系统调度／投递尚未实现 |
| 2026-10-10 | 完成 M1 Task 3 状态与字段保护；M1 核心基础完成 | 新增状态纯函数与 44 项测试，合计 87 项核心检查通过；取消优先、延期后撤销保留最新值、完成 1 小时／忽略 24 小时边界均验证。SDK、三个核心模块及测试已写入，真实 NapCat／OCR／WPF／SQLite／系统通知仍在后续阶段；下一步 M2 |
| 2026-10-10 | 用户要求删除 demo 图片日期的独立二次确认勾选 | 填入／修改日期后直接通过确认或保存按钮提交，建议填入仍仅更新草稿。新增和更新浏览器回归，107 项浏览器、9 项文档及 87 项核心测试通过，需求与交接同步 |
| 2026-10-10 | 接续 M2，完成 Task 1 消息契约、标准化及发送上下文日期 | 原发送日期、跨年、范围／冲突、非法数值及时区边界已验证，183 项核心、10 项文档及 107 项浏览器检查通过；行动与样本尚在实施，真实 QQ／OCR 尚未接入 |
| 2026-10-10 | 完成 M2 Task 2 与 Task 3 本地规则和合成基线 | Release 核心 289 项、fixture 6 项、文档 10 项、浏览器 107 项通过；CLI 生成 65 条样本报告（23/23 正式 Task 准确、47/47 需行动召回、27/47 直接自动覆盖、0 高影响错误），无效参数退出 2。结果不代表真实 QQ 验收；下一步 M3 |
| 2026-10-10 | 用户提供真实 QQ 截图，确认红框均为学校相关群 | 同步正规群名辅助候选及非正式名称例外；截断名称待真实列表核对。仅验证截图 OCR，不把群列表视作完整通知日期样本；M3 正在实施 |
| 2026-10-11 | 用户反馈普通 QQ 与 NapCat 同账号冲突，担心无法继续使用 QQ | 纠正此前关于 Shell 聊天窗口的错误说明：Shell 是无界面接管路线，同账号不能与普通 QQ 并行；官方 Framework 桌面模式仅作为待验证候选。更新需求、设计和接入说明；真实 OneBot 尚未通过 |
| 2026-10-11 | M3 探针实现与自动验证推进 | Infrastructure 46 项、Windows 44 项、OCR 25 项和 Node 18 项通过；隐藏 WebView2 桥及自包含发布实测通过；真实 QQ 3001 未就绪，通知受提权阻止，桌面人工项未测。见 `docs/verification/integration-probes.md` |
| 2026-10-11 | 纠正 NapCat Shell 聊天能力说明并准备 Framework 显式入口 | Shell 明确记录为无界面接管路线；官方 Framework v4.18.37 包与本机 QQ `9.9.36-53644` 版本映射已核对，SHA256 与发布摘要一致，新增 `Start-NapCat-Framework.ps1/.cmd` 及路径回归。未向 QQ 注入或重启，桌面聊天与 OneBot 共存仍未验证。实际验证：Framework `-CheckOnly` 通过，Node 18/18、Core 295/295、Infrastructure 46/46、Windows 44/44、OCR 25/25、demo 107 及 `git diff --check` 通过 |
| 2026-10-11 | 用户询问是否可让普通 QQ 与 NapCat 定时自动切换 | 仅记录为未决定方案：可在空闲时切换并恢复普通 QQ，但仍非并行运行；尚未确认开发，不进入有效需求 |
| 2026-10-11 | Framework 真实只读接入及桌面窗口反馈 | 用户确认已退出普通 QQ 后实际启动 Framework 并自动登录；三个限定范围探针退出码 0，群 52 个、一群一页历史 7 条、实时群消息 0 条。用户确认聊天列表和窗口正常显示，窗口打开期间接口复测通过；启动窗口初始化 TypeError 保留记录，实时群事件、长期稳定性、完整历史补漏及 M3 剩余人工项未验收。同步当前正文与相关回归，Node 19/19、diff 检查通过，不提交私人日志或配置 |
| 2026-10-11 | 修正 Windows 权限检测并继续真实验证 | 新增令牌完整性查询和独立 whoami 回归，Windows 45/45；Basic User 高完整性误判已修正。中等完整性测试实际运行，但通知接口 `0x80070490`、未提交提醒；服务均运行，原因仍待定位。第四次 QQ 探针监听 300 秒退出码 0，未观察到实时群事件。完整回归 Core 295、Infrastructure 46、Windows 45、OCR 25、Node 19、demo 107 及 Release publish/安装通过；Infrastructure 初次构建 DLL 锁问题在探针结束后复跑通过，QQ 保持运行 |
| 2026-10-11 | 收到真实群事件并明确 QQ 只读边界 | 用户反馈新群消息后，第五次 180 秒探针收到 1 条、历史 7 条、退出码 0；历史诊断 52 群中 41 成功、11 失败，原因待查。用户询问是否会发群消息，已核对没有发送接口调用，需求明确只读及本机 Windows 提醒。相关文档和测试同步，Node 20/20、OneBotTransportTests 5/5 通过；长期稳定性、七天补漏及 M3 人工项仍未验收 |
| 2026-10-11 | 通知资源发布、冷启动回调修复及用户要求重发 | 发布补齐匹配 SDK 官方资源并拒绝缺资源安装；冷启动读取 SDK 首次参数，受控 COM 正式产物回归退出 0，Windows 45/45、四文件 Node 21/21。14:28:38 本机提醒提交 ID 36646，实际点击和声音仍待用户反馈；QQ 只读。历史只复测一个失败样本及一个对照，定位空历史错误，不代表七天补漏通过 |
| 2026-10-11 | 修复通知点击入口，用户确认出现事项窗口 | 探针改用专用 URI 打开合成事项，并补齐 Windows COM 的 `-Embedding` 参数处理；14:52:34 提醒 ID 36649 的实际点击记录与发送 Probe ID 一致，用户实际点击通过。Windows 57/57、Node 21/21、Release 发布、当前用户安装及受控 Shell/COM 回归通过；声音、静音呈现、桌面人工项、长期稳定性及七天补漏未验收，M3 继续实施 |
| 2026-10-11 | 完成真实学校通知截图 OCR 实测并补齐“交齐”规则 | 本地 RapidOCR 离线识别真实通知截图 15 行、退出码 0、stderr 为空，正文文字、位置和置信度通过；识别出的填写日期范围进入人工确认，“10月12日之前交齐”作为提交候选，图片日期只保存为 `SuggestedTime`。新增脱敏 Core 回归，Core Release 296/296、OCR 25/25、四文件 Node 22/22 通过；正式任务生成、日期人工确认及提醒投递仍未验收，原图和全文未入 Git |
| 2026-10-11 | 用户取消声音提醒，第一版统一静音 | 替代此前按任务类别播放声音及声音总开关的规则；demo 删除开关并清理旧声音偏好，通知载荷固定静音，声音验收项移除。Windows 57/57、Node 22/22、浏览器 108 项及 Release 发布/当前用户安装通过；静音呈现和 M3 其余人工项仍未验收 |
| 2026-10-11 | 修复桌面卡片不可见、缩放手柄不明显及多余状态窗 | 父窗改为 `SHELLDLL_DefView`，扩大右下角箭头手柄，成功时隐藏状态窗；真实产物/鼠标回归通过。用户确认可见、拖动和缩放正常、无白色窗口、不覆盖其他软件，基本桌面人工项通过；完整自动回归及最终 Windows 57/57、发布/安装通过。多屏、Explorer 重启、睡眠恢复和布局持久化未验收，M3 继续实施 |
| 2026-10-11 | 用户澄清“交齐”是班长对同学的要求 | 纠正“提交动作”的表述，明确截图正文属于学校通知内容；软件识别学生待办，不执行材料提交或 QQ 回复。更新分类说明和班长发布者回归，Core 296/296、文档 17/17、diff 检查通过；正式应用联调仍待验收 |

| 2026-10-11 | 静音提醒呈现与点击通过，恢复 Framework 并扩展真实历史分页 | ID 36667 用户确认，点击 Probe ID 匹配；修复带游标读取方向，Infrastructure 60/60。全群复测 41/52 到达边界、窗口内去重 1098，11 群持续 1200 保留未补全；真实 CLI 退出 1，不宣称完整补漏。详细命令、自动回归和范围见第 1 节及集成验证文档；M3 继续实施 |

| 2026-10-11 | 受控重连与跨连接去重限定范围通过 | 新增主动重连探针及回归，Infrastructure 67/67；真实两次账号核对 2/2、重连 1/1，历史 2206、去重 1098、重复 1108。11 群每轮持续 1200，CLI 退出 1；模拟新消息补回与真实无新消息分开记录，正式自动恢复/持久化仍未完成 |
| 2026-10-12 | 接入 Windows 登录自动启动 | 新增当前用户 Run 注册、`--background` 后台入口、设置页开关和原子偏好文件；验证模式不写入 Run 项，注册表/偏好写入失败回滚注册表状态；修复后台实例被普通入口唤回后主窗口仍透明的问题。`StartupRegistrationTests` 与 `AppBridgeProtocolTests` 53/53、正式页面 29/29、应用 Release build 无警告错误、Native 13 项及默认入口检查通过；发布并重开 `.tools/published/app-launcher/`，后台启动/普通入口唤回/Run 项/托盘检查通过，真实登录 Windows 自启动验收后置，下一项为 OCR 队列 |

| 2026-10-13 | 完成桌面卡片恢复与多屏可见布局 | 实际父窗/styles检查、宿主丢失/返回、系统事件与跨await/旧回调隔离、单卡片偏好保存已接入。Core306/306、Infrastructure675/675、Windows390/390、Node139/139、App build/publish及新写入83项Native合成检查通过；截图已查看。真实Explorer重启、物理多屏、睡眠/长期稳定性仍未验收，睡眠漏同步政策未确认，默认入口/真实账号未切换；下一阶段统一真实验收 |
