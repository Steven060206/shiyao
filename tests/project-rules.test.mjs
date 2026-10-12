import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const documents = ['AGENTS.md', 'docs/requirements.md', 'docs/HANDOFF.md'];

test('同步状态记录真实批次时间和本次运行范围，固定时刻与游戏实测仍另行验收', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'src/MessagePurify.Windows/App/README.md', 'tests/README.md']) {
    const content = read(file);
    assert.match(content, /同步状态显示已实现并验证/, file);
    assert.match(content, /上次成功同步（本次运行）/, file);
    assert.match(content, /下次计划同步/, file);
    assert.match(content, /不跨进程持久化|没有跨进程持久化/, file);
    assert.match(content, /真实只读.*(?:通过|退出 0)/s, file);
    assert.match(content, /自然固定时刻.*实际游戏.*(?:未完成|未验收)/, file);
  }
});

test('用户 A 已确认两卡片渠道，全部 Windows 系统弹窗和声音取消', () => {
  const next = read('docs/HANDOFF.md').split('## 6. 未决定事项与下一步')[1].split('## 7.')[0];
  assert.match(next, /已确认：第一版仅使用两张桌面卡片/);
  assert.match(next, /2026-10-12 用户选择 A/);
  assert.doesNotMatch(next, /未决定：仅使用桌面卡片提醒|弹窗尚待确认|Windows 投递接入须先等/);
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md',
    'docs/superpowers/specs/2026-10-10-windows-app-design.md',
    'docs/superpowers/plans/2026-10-10-windows-app-implementation.md']) {
    const content = read(file);
    assert.match(content, /取消全部 Windows 系统通知弹窗和声音/, file);
    for (const term of ['自定义', '安静时段', '权限恢复', '历史', '迟到', '恢复', '变更']) {
      assert.ok(content.includes(term), `${file} 缺少已取消渠道范围：${term}`);
    }
    assert.match(content, /普通.*(?:App|通知列表)|App.*(?:普通|通知)/, `${file} 保留普通群通知列表`);
  }
  const activeRules = read('docs/requirements.md').split('\n').filter(line => line.startsWith('- ')).join('\n');
  assert.doesNotMatch(activeRules, /卡片高亮和 Windows 通知提醒|额外发一次 Windows 系统通知|补发一次汇总提醒|每次持续断开超过 5 分钟后发一次系统通知/);
  assert.doesNotMatch(activeRules, /设置中保留“安静时段”入口|允许单独调整该任务的提醒时间|提前 1 天（24 小时）和 2 小时各提醒一次/);
});

test('当前桌面方向保留历史产物和真实验证边界', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md',
    'docs/superpowers/specs/2026-10-10-windows-app-design.md']) {
    const content = read(file);
    assert.match(content, /ReminderPlanner.*SQLite plans\/ledger|ReminderPlanner、SQLite plans\/ledger/, file);
    assert.match(content, /历史产物|历史保留/, file);
    assert.match(content, /不删除.*(?:数据|消息)|不删.*数据|数据.*保留/, file);
    assert.match(content, /正式.*(?:卡片|桌面).*(?:首段|第一段).*(?:已实现|已完成)/, file);
    assert.match(content, /多屏.*(?:未验收|尚未验收|仍.*验收)/, `${file} 保留后续真实桌面验收范围`);
    assert.match(content, /M3.*正在实施|M3.*仍在实施/, file);
    assert.match(content, /不记为.*完成|不记完成/, `${file} 取消不能记为完成`);
  }
});

test('设计和总计划将通知验收替换为卡片和当前状态验收', () => {
  const spec = read('docs/superpowers/specs/2026-10-10-windows-app-design.md');
  const acceptance = spec.split('## 12. 后续真实验收')[1].split('## 13.')[0];
  const plan = read('docs/superpowers/plans/2026-10-10-windows-app-implementation.md');
  for (const label of ['首次导入与补漏卡片刷新', '卡片时间排序与高亮', '取消与卡片操作', '账号切换与卡片操作', '提示渠道与普通通知列表', '崩溃与工作区恢复']) {
    assert.ok(acceptance.includes(`| ${label} |`), `设计缺少当前卡片验收：${label}`);
    assert.ok(plan.includes(`| ${label} |`), `计划缺少当前卡片映射：${label}`);
  }
  assert.doesNotMatch(acceptance, /\| 默认与自定义提醒 \||\| 取消与在途通知提交 \||\| 通知状态与旧通知清理 \||\| 崩溃与未知投递结果 \|/);
  assert.match(spec, /AccountWorkspace.*当前账号快照/);
  assert.match(spec, /Topmost/);
  assert.match(plan, /M3.*真实接收.*长期稳定性.*仍在实施/);
});

for (const file of documents) {
  test(`${file} 保留测试、交付与提交要求`, () => {
    const content = read(file);
    assert.match(content, /每次改动完成后，都必须创建一个对应的 Git commit/);
    assert.match(content, /每次改动后，都必须编写或更新相关测试/);
    assert.match(content, /在交付给用户前，确保所有测试和验证全部通过/);
  });
}

test('小任务规则明确服从新的测试和提交要求', () => {
  const content = read('AGENTS.md');
  assert.match(content, /小任务快速通道不免除测试、验证和 Git commit/);
  assert.match(content, /测试豁免由本文件“注意事项”的最新要求替代/);
  for (const section of ['接续项目时', '文档同步：每次有变化都更新', '与用户讨论', '小任务偏好', '阶段与进度']) {
    assert.ok(content.includes(`## ${section}`), `原有协作章节丢失：${section}`);
  }
});

test('正式应用交付记录区分合成闭环、只读宿主与剩余系统功能', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'tests/README.md']) {
    const content = read(file);
    assert.match(content, /正式应用首段/, file);
    assert.match(content, /AccountWorkspace/, file);
    assert.match(content, /NapCatMessageHost/, file);
    assert.match(content, /合成.*(?:WebView2|闭环)|WebView2.*合成/, file);
    assert.match(content, /(?:提醒调度|OCR 队列).*未完成|(?:提醒调度|OCR 队列).*后续/, file);
    assert.match(content, /真实 QQ.*(?:验证|接口|联调)/, file);
    assert.match(content, /持久化提醒第一段/, `${file} 同步本段实现进度`);
    assert.match(content, /reminders\.sqlite3.*schema 1/, `${file} 记录新增提醒库`);
    assert.match(content, /原 messages\/tasks\/routing 三库 schema 1 不变/, `${file} 保留原存储格式`);
    assert.match(content, /系统提醒尚未开启/, `${file} 保留旧版页面的实际状态`);
    assert.match(content, /Windows 实际投递及后台循环尚未接入/, `${file} 保留旧版系统投递验证边界`);
    if (file !== 'tests/README.md') {
      assert.match(content, /旧通知方案|旧方案/, `${file} 旧提醒产物不能成为后续 Windows 接入要求`);
      assert.match(content, /系统通知.*(?:取消|已取消)|取消全部 Windows 系统通知/, file);
    }
  }
  for (const artifact of ['src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj',
    'src/MessagePurify.Windows/App/packages.lock.json', 'src/MessagePurify.Windows/App/ui/app.js',
    'src/MessagePurify.Windows/App/DesktopCardHost.cs', 'src/MessagePurify.Windows/App/DesktopCardProjection.cs',
    'src/MessagePurify.Windows/App/DesktopLayoutStore.cs', 'tests/MessagePurify.Windows.Tests/DesktopCardTests.cs',
    'tests/MessagePurify.Windows.Tests/DesktopLayoutTests.cs', 'tests/app_desktop_test.py',
    'docs/superpowers/plans/2026-10-12-desktop-only-cards.md',
    'src/MessagePurify.Windows/App/README.md', 'packaging/app/Start-MessagePurify.cmd',
    'tests/application-ui.test.mjs', 'tests/application-launcher.test.mjs']) {
    assert.ok(fs.statSync(path.join(root, artifact)).isFile(), `正式应用产物缺失：${artifact}`);
  }
});

test('项目文档的本地链接、原型与核心交付文件存在', () => {
  const files = [...documents, 'docs/superpowers/specs/2026-10-10-windows-app-design.md', 'docs/superpowers/plans/2026-10-10-windows-app-implementation.md', 'docs/superpowers/plans/2026-10-10-core-foundation.md', 'docs/superpowers/plans/2026-10-10-local-rules.md', 'docs/superpowers/plans/2026-10-10-integration-probes.md', 'tests/README.md'];
  for (const file of files) {
    for (const match of read(file).matchAll(/\[[^\]]+\]\(([^)]+)\)/g)) {
      const target = match[1];
      if (/^[a-z]+:/i.test(target) || target.startsWith('#')) continue;
      const resolved = path.resolve(root, path.dirname(file), target.split('#')[0]);
      assert.ok(fs.existsSync(resolved), `${file} 链接目标不存在：${target}`);
    }
  }
  const artifacts = [
    'prototype/index.html', 'prototype/app.js', 'prototype/README.md',
    ...['courses', 'pending', 'main', 'main-dark', 'desktop', 'desktop-dark'].map(name => `prototype/previews/${name}.png`),
    'tests/prototype-qa.mjs', 'global.json', '.gitignore',
    'src/MessagePurify.Core/MessagePurify.Core.csproj',
    'src/MessagePurify.Core/Time/TaskTime.cs',
    'src/MessagePurify.Core/Reminders/ReminderPlanner.cs',
    'src/MessagePurify.Core/State/TaskTransitions.cs',
    ...['RuleModels', 'MessageNormalizer', 'MessageDateParser', 'MessageRuleExtractor', 'PriorityCalculator'].map(name => `src/MessagePurify.Core/Rules/${name}.cs`),
    'tests/MessagePurify.Core.Tests/MessagePurify.Core.Tests.csproj',
    ...['TaskTime', 'ReminderPlanner', 'TaskTransitions', 'MessageNormalizer', 'MessageDateParser', 'RuleExtraction', 'PriorityCalculator', 'RuleFixture'].map(name => `tests/MessagePurify.Core.Tests/${name}Tests.cs`),
    'tests/MessagePurify.Core.Tests/RuleFixtureRunner.cs', 'tests/fixtures/rules/synthetic.json',
    'tools/MessagePurify.RuleBaseline/MessagePurify.RuleBaseline.csproj', 'tools/MessagePurify.RuleBaseline/Program.cs',
    'tools/MessagePurify.RuleBaseline/packages.lock.json', 'docs/verification/rule-baseline.md',
    ...['src/MessagePurify.Core', 'tests/MessagePurify.Core.Tests'].map(folder => `${folder}/packages.lock.json`),
    'docs/verification/integration-probes.md',
    'docs/integrations/napcat-setup.md',
    'packaging/probe/README.md', 'packaging/probe/Install-Probe.ps1', 'packaging/probe/Run-Probe.ps1',
    'packaging/napcat/Start-NapCat.ps1', 'packaging/napcat/Start-NapCat.cmd',
    'packaging/napcat/Start-NapCat-Framework.ps1', 'packaging/napcat/Start-NapCat-Framework.cmd',
    'tests/napcat-framework-launcher.test.mjs',
    'src/MessagePurify.Windows/IntegrationProbe/ProcessIntegrity.cs',
    'tests/MessagePurify.Windows.Tests/ProcessIntegrityTests.cs',
    'packaging/probe/Copy-NotificationResource.ps1',
    'tests/notification_cold_activation_test.py',
    'src/MessagePurify.Windows/IntegrationProbe/NotificationProtocol.cs',
    'tests/MessagePurify.Windows.Tests/NotificationProtocolTests.cs',
    'tests/notification_protocol_activation_test.py',
    'tests/desktop_attachment_test.py',
    'docs/superpowers/plans/2026-10-11-task-lifecycle.md',
    'src/MessagePurify.Infrastructure/Tasks/TaskStoreModels.cs',
    'src/MessagePurify.Infrastructure/Tasks/TaskLifecycleService.cs',
    'tests/MessagePurify.Infrastructure.Tests/TaskLifecycleTests.cs',
    'docs/superpowers/plans/2026-10-12-candidate-routing.md',
    'src/MessagePurify.Infrastructure/Processing/AccountCandidateRouter.cs',
    'src/MessagePurify.Infrastructure/Processing/CandidateRoutingModels.cs',
    'tests/MessagePurify.Infrastructure.Tests/CandidateRoutingTests.cs',
    'docs/superpowers/plans/2026-10-12-linked-changes.md',
    'tests/MessagePurify.Infrastructure.Tests/LinkedChangeRoutingTests.cs',
    'docs/superpowers/plans/2026-10-12-persisted-reminders.md',
    'src/MessagePurify.Infrastructure/Reminders/ReminderModels.cs',
    'src/MessagePurify.Infrastructure/Reminders/AccountReminderService.cs',
    'tests/MessagePurify.Infrastructure.Tests/PersistedReminderTests.cs',
    'tests/MessagePurify.Infrastructure.Tests/WorkspaceReminderTests.cs'
  ];
  for (const file of artifacts) {
    assert.ok(fs.statSync(path.join(root, file)).isFile(), `交付文件缺失：${file}`);
  }
});

test('M4 生命周期进度明确区分存储验证和消息宿主联调', () => {
  const planFile = 'docs/superpowers/plans/2026-10-11-task-lifecycle.md';
  for (const match of read(planFile).matchAll(/\[[^\]]+\]\(([^)]+)\)/g)) {
    assert.ok(fs.existsSync(path.resolve(root, path.dirname(planFile), match[1])), `生命周期计划链接缺失：${match[1]}`);
  }
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'tests/README.md']) {
    const current = read(file).split('## 2.')[0];
    assert.match(current, /生命周期/, file);
    assert.match(current, /Infrastructure(?: Release)? \d+\/\d+/, file);
    assert.match(current, /自动路由|自动转事项/, file);
    assert.match(current, /(?:真实 QQ|正式只读 QQ).*(?:宿主|接收|验证)/, file);
    assert.match(current, /(?:提醒|OCR).*(?:未完成|后续)/, file);
  }
  assert.match(read(planFile), /SuggestedTime.*(?:永远|不能)/);
  assert.match(read(planFile), /expectedTaskVersion/);
  assert.match(read(planFile), /普通通知转换.*后续|普通通知转换.*不在本段/);
  assert.match(read('docs/HANDOFF.md'), /恢复工作时先读.*docs\/HANDOFF\.md.*再读.*docs\/requirements\.md/);
});

test('M4 首轮候选路由状态明确本地分流、课程确认和后续实现边界', () => {
  for (const file of ['docs/requirements.md', 'tests/README.md']) {
    const content = read(file);
    assert.match(content, /AccountCandidateRouter.*首轮.*(?:自动路由已实现|自动路由.*已实现)/, file);
    for (const disposition of ['Task', 'Pending', 'Change', 'Notice', 'Ignored']) {
      assert.ok(content.includes(disposition), `${file} 缺少 ${disposition} 分流说明`);
    }
    assert.match(content, /可靠关联 Change 自动更新同一事项/, `${file} 当前可靠变更自动应用`);
    assert.match(content, /课程确认.*(?:撤销|变更).*Task\/Notice.*降级.*待确认/, file);
    assert.match(content, file === 'docs/requirements.md'
      ? /普通通知列表表示学校群中的普通通知，与已取消的 Windows 系统通知无关/
      : /普通通知列表入库不等于 Windows 系统通知投递/, file);
    assert.match(content, /普通通知转任务.*(?:待完成|尚未完成|后续阶段)/, file);
    assert.match(content, /自动可靠关联变更/, file);
    assert.match(content, /M6.*重新分析协调.*(?:待完成|尚未完成|后续阶段)/, file);
    assert.match(content, /正式应用首段.*(?:界面|WebView2|WPF)/, file);
    assert.match(content, /(?:提醒调度|OCR 队列).*(?:未完成|后续)/, file);
    assert.match(content, /Infrastructure Release 184\/184/, `${file} 保留上轮实际基线`);
    assert.doesNotMatch(content.split('## 2.')[0], /收件箱自动转事项.*尚未完成|消息收件箱到事项的自动路由.*未完成/,
      `${file} 当前状态不能保留已替换的路由结论`);
  }
  assert.match(read('docs/requirements.md'), /更高 ProcessingVersion 不自动重放/);
  const readme = read('tests/README.md');
  assert.match(readme, /FullyQualifiedName~CandidateRoutingTests\|FullyQualifiedName~LinkedChangeRoutingTests\|FullyQualifiedName~TaskLifecycleTests/);
  assert.match(readme, /回执失败整条消息回滚/);
  assert.match(readme, /WAL 拒绝且不写结果/);
  assert.match(readme, /第三段当时 Pending\/Change 仅保存待确认.*第四段已替代/);
});

test('M4 可靠关联变更状态保留持久化目标、来源授权、保守顺序和正式宿主边界', () => {
  const planFile = 'docs/superpowers/plans/2026-10-12-linked-changes.md';
  for (const match of read(planFile).matchAll(/\[[^\]]+\]\(([^)]+)\)/g)) {
    const target = match[1];
    if (/^[a-z]+:/i.test(target) || target.startsWith('#')) continue;
    assert.ok(fs.existsSync(path.resolve(root, path.dirname(planFile), target.split('#')[0])),
      `关联变更计划链接缺失：${target}`);
  }
  for (const file of ['docs/requirements.md', 'tests/README.md']) {
    const content = read(file);
    assert.match(content, /自动可靠关联变更与保守消息顺序协调已实现/, file);
    assert.match(content, /ProcessAndRoutePending.*单条消息事务.*提取.*路由/, file);
    assert.match(content, /RoutePending.*更早未提取消息先等待/, file);
    assert.match(content, /KnownTasks 完全由当前持久化事项投影替换调用者旧快照/, file);
    assert.match(content, /只有当前群 TrustedSenders 授权高影响变更/, file);
    assert.match(content, /原发送者不自动升级为可信发布者/, file);
    assert.match(content, /普通原 Pending\/Task\/Notice 不升级/, file);
    assert.match(content, /Cancel\/Postpone\/NewRequirement\/Remind/, file);
    assert.match(content, /Remind 只追加证据，不覆盖正式时间或恢复事项/, file);
    assert.match(content, /Remind 未传 confirmedTime 时不使用候选 Time.*显式确认新时间仍可修改/, file);
    assert.match(content, /保留完成历史/, file);
    assert.match(content, /保留稳定标题/, file);
    assert.match(content, /LEFT JOIN.*任一来源缺失时间.*同秒顺序冲突.*待确认/, file);
    assert.match(content, /尚未完整支持 real_seq 或跨群原转发顺序/, file);
    assert.match(content, file === 'docs/requirements.md'
      ? /变更系统通知及系统提醒联调已取消/
      : /变更一次 Windows 通知.*(?:未完成|尚未完成|后续阶段)/, file);
    assert.match(content, /M6.*重新分析协调.*(?:待完成|尚未完成|后续阶段)/, file);
  }
  const current = read('docs/HANDOFF.md').split('### 暂停接续记录')[0];
  assert.match(read('docs/HANDOFF.md'), /第四段|可靠关联变更/);
  assert.match(current, /(?:真实|正式只读) QQ.*(?:接收|宿主)|真实正式宿主/);
});

test('新版演示交付链接可用，保存说明与真实集成阶段一致', () => {
  for (const match of read('prototype/index.html').matchAll(/<script src="([^"]+)"/g)) {
    assert.ok(fs.statSync(path.resolve(root, 'prototype', match[1])).isFile(), `演示脚本缺失：${match[1]}`);
  }
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'prototype/README.md']) {
    const content = read(file);
    assert.match(content, /本地存储/, `${file} 需说明演示保存方式`);
    assert.match(content, /撤销.*真实经过时间|真实经过时间.*撤销/, `${file} 需说明撤销计时与演示日期区分`);
    assert.match(content, /真实应用(?:实现|代码)尚未开始|Windows.*尚未接入|(?:正式提醒|OCR 队列|桌面卡片).*(?:未完成|后续)/, `${file} 需说明真实集成边界`);
  }
});

test('需求、交接及设计的当前状态一致记录整体设计已确认', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md']) {
    assert.match(read(file).split('## 2.')[0], /demo 基本满足要求/, `${file} 需同步用户的演示反馈`);
    assert.match(read(file).split('## 2.')[0], /M1/, `${file} 需保留核心基础完成状态`);
  }
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'docs/superpowers/specs/2026-10-10-windows-app-design.md']) {
    const current = read(file).split('## 1.')[0] + (file.endsWith('HANDOFF.md') ? read(file).match(/^- 当前阶段：.*$/m)?.[0] : '');
    assert.match(current, /整体设计已确认/, file);
    assert.match(current, /实施(?:总)?计划/, file);
    assert.match(current, /M1.*正在实施|M1 已完成/, `${file} 需准确记录核心开发阶段`);
    assert.match(current, /尚未接入|(?:正式提醒|完整端到端).*(?:未完成|后续)/, `${file} 需保留真实集成边界`);
    assert.doesNotMatch(current, /整体设计尚未确认|完整设计仍待整体核对/, file);
  }
});

test('M2 计划与当前规则开发阶段同步', () => {
  const plan = read('docs/superpowers/plans/2026-10-10-local-rules.md');
  assert.match(plan, /原发送时刻/);
  assert.match(plan, /合成样本与真实独立验收分开/);
  assert.match(plan, /Task 1/);
  assert.match(plan, /Task 2/);
  assert.match(plan, /Task 3/);
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'docs/superpowers/specs/2026-10-10-windows-app-design.md']) {
    assert.match(read(file).split('## 2.')[0], /M2.*(?:正在实施|已完成)/, `${file} 需同步本地规则阶段`);
  }
});

test('QQ 登录与实际连接状态分别记录', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md']) {
    const content = read(file);
    assert.match(content, /用户.*QQ.*(?:电脑|本机)登录/, `${file} 需记录用户确认的登录`);
    assert.match(content, /尚未验证 OneBot 连接|不作为 OneBot 连接成功/, `${file} 需区分登录和接入`);
  }
});

test('NapCat 两种模式、真实接口与桌面反馈的验证范围一致', () => {
  const files = [
    'docs/requirements.md',
    'docs/HANDOFF.md',
    'docs/integrations/napcat-setup.md',
    'docs/verification/integration-probes.md',
    'docs/superpowers/plans/2026-10-10-integration-probes.md',
    'docs/superpowers/specs/2026-10-10-windows-app-design.md'
  ];
  for (const file of files) {
    const content = read(file);
    assert.match(content, /Shell[^\n]*无界面|无界面[^\n]*Shell/, `${file} 需说明 Shell 无界面边界`);
    assert.match(content, /Framework.*已(?:加载|实际加载|通过)|通过 Framework/, `${file} 需同步 Framework 已运行`);
    assert.match(content, /只读(?:接口|连接|探针).*通过/, `${file} 需同步真实只读结果`);
    assert.match(content, /用户确认聊天列表和窗口正常显示|用户.*确认.*聊天列表和窗口正常显示/, `${file} 需区分用户桌面反馈与接口验证`);
    assert.match(content, /实时群事件限定范围通过（观察到 1 条）/, `${file} 需同步真实群事件的限定验证结果`);
    assert.match(content, /长期稳定性.*尚未(?:验证|验收)/, `${file} 不得以单条事件代替长期稳定性验收`);
    const current = content.split('## 2.')[0];
    assert.doesNotMatch(current, /OneBot 尚未接入|OneBot 尚未真实连接|尚未向 QQ 注入|本机尚未(?:执行注入|注入或重启)/, `${file} 当前状态不能保留启动前结论`);
    assert.doesNotMatch(content, /Shell[^\n]*正常聊天|NapCat 启动的 QQ 窗口仍可正常聊天|NapCat 窗口就是可正常使用/, `${file} 不能承诺 Shell 有可聊天窗口`);
  }
});

test('QQ 接入保持只读，学校群不承载测试或任务提醒', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'docs/superpowers/specs/2026-10-10-windows-app-design.md', 'docs/integrations/napcat-setup.md']) {
    const content = read(file);
    assert.match(content, /QQ 接入只读，不向 QQ 群或私聊发送消息/, file);
    assert.match(content, /Windows 通知|Windows 测试提醒/, file);
  }
});

test('置顶群规则同步至需求、交接、设计、计划与探针，未知状态不能回退全群', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'docs/superpowers/specs/2026-10-10-windows-app-design.md',
    'docs/superpowers/plans/2026-10-10-windows-app-implementation.md', 'docs/superpowers/plans/2026-10-10-integration-probes.md',
    'tools/MessagePurify.QqProbe/README.md']) {
    const content = read(file);
    assert.match(content, /只读取(?:当前 QQ (?:中|账号中))?置顶的群聊(?:消息)?|只读取当前 QQ 账号中置顶的群聊消息/, file);
    assert.match(content, /不回退(?:到)?全群|不回退到所有群/, file);
  }
  assert.doesNotMatch(read('docs/requirements.md'), /使用 NapCat，监听所有群聊/);
  assert.doesNotMatch(read('docs/HANDOFF.md'), /范围为所有群聊/);
  assert.match(read('docs/HANDOFF.md'), /真实名单\/历史限定验证通过/);
  assert.match(read('docs/HANDOFF.md'), /没有真实实时群样本/);
});

test('真实 QQ 接入报告保留历史范围、窗口异常和剩余验收', () => {
  const report = read('docs/verification/integration-probes.md');
  assert.match(report, /群 52 个|52 个群/);
  assert.match(report, /历史消息 7 条|7 条历史消息/);
  assert.match(report, /一群一页/);
  assert.match(report, /updateWindowSizeProperties/);
  assert.match(report, /七天历史分页、断线补漏、正式持久化或任务生成/);
  assert.match(report, /M3 仍为“正在实施”/);
});

test('已确认常驻后台同步替代按次切换，停止快捷方式方向', () => {
  const handoff = read('docs/HANDOFF.md');
  const requirements = read('docs/requirements.md');
  assert.match(handoff, /已确认：QQ\/NapCat Framework 常驻，固定时间后台同步/);
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'docs/integrations/napcat-setup.md', 'src/MessagePurify.Windows/App/README.md']) {
    const content = read(file);
    assert.match(content, /固定时间/);
    assert.match(content, /常驻.*固定时间后台同步|常驻，固定时间后台同步/);
    assert.match(content, /停止.*快捷方式|快捷方式改造已取消|快捷方式改造.*停止/);
  }
  assert.match(requirements, /替代每次同步切换接入环境及结束后恢复普通 QQ/);
  assert.match(handoff, /用户选择 A：游戏导致错过同步时，结束游戏后补同步一次/);
  assert.doesNotMatch(requirements, /日常重新打开 QQ 直接双击桌面原有/);
});

test('每天三次同步、游戏期间禁止同步及退出后合并补一次保持有效', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'docs/integrations/napcat-setup.md', 'src/MessagePurify.Windows/App/README.md']) {
    const end = file === 'docs/requirements.md' ? '## 3.'
      : file === 'docs/HANDOFF.md' ? '上一段恢复（'
      : file === 'docs/integrations/napcat-setup.md' ? '以下 Shell' : '## 当前可操作';
    const current = read(file).split(end)[0];
    assert.match(current, /每天 08:00、12:00、20:00（北京时间）各同步一次，共三次/, file);
    assert.match(current, /打游戏时禁止同步/, file);
    assert.match(current, /常驻/, file);
    assert.match(current, /中途开始游戏时暂停|中途游戏暂停/, file);
    assert.match(current, /真实.*尚未验收|真实.*验收尚未完成/, file);
    assert.match(current, /游戏导致错过同步时，结束游戏后补同步一次/, file);
    assert.doesNotMatch(current, /游戏导致错过同步(?:后的补同步规则|时的补同步规则).*待确认/, file);
    assert.doesNotMatch(current, /具体同步时刻.*待确认|具体时刻及.*待确认/, file);
  }
  const rules = read('docs/requirements.md').split('\n').filter(line => line.startsWith('- ')).join('\n');
  assert.match(rules, /固定时间 08:00、12:00、20:00/, '有效需求保留三个时刻');
  assert.match(rules, /游戏期间不得为同步切换或重启 QQ/, '游戏限制覆盖 QQ 切换');
  assert.match(rules, /游戏导致错过同步时，结束游戏后补同步一次/);
  assert.match(rules, /同一游戏期间错过多个固定时间，合并为一次补同步/);
  assert.doesNotMatch(rules, /结束游戏后补一次还是等待下个固定时间，尚待用户确认/);
  assert.doesNotMatch(rules, /具体同步时刻.*待确认/);
  const next = read('docs/HANDOFF.md').split('## 6. 未决定事项与下一步')[1].split('## 7.')[0];
  assert.match(next, /不重新询问.*同步时间/);
  assert.match(next, /游戏漏同步处理已确认，不重新询问/);
  assert.doesNotMatch(next, /游戏导致错过同步时的补同步规则.*待确认|下一步优先确认游戏导致错过同步/);
  assert.doesNotMatch(next, /具体同步时刻.*待确认/);
});

test('常驻 A 方案、自动维护本机游戏名单及真实验收边界分别记录', () => {
  const requirements = read('docs/requirements.md');
  assert.match(requirements, /用户选择 A.*(?:常驻方案|常驻.*后台)/);
  assert.match(requirements, /不再每次同步前后切换或重开 QQ/);
  assert.match(requirements, /将电脑现有游戏全部加入检测，并定期扫描电脑游戏、同步检测名单/);
  assert.match(requirements, /默认应用启动及每日扫描一次，游戏期间延后目录扫描/);
  const next = read('docs/HANDOFF.md').split('## 6. 未决定事项与下一步')[1].split('## 7.')[0];
  assert.match(next, /已确认：QQ\/NapCat Framework 常驻，固定时间后台同步/);
  assert.match(next, /不取消游戏禁止同步和补一次规则/);
  assert.match(next, /真实常驻验收尚未完成/);
  assert.match(next, /游戏性能及长期稳定性仍需实测/);
  assert.doesNotMatch(next, /常玩游戏名称待用户提供|未决定：消息接口随 QQ 常驻/);
  const spec = read('docs/superpowers/specs/2026-10-10-windows-app-design.md').split('**状态：')[0];
  assert.match(spec, /每天 08:00、12:00、20:00（北京时间）/);
  assert.match(spec, /打游戏时禁止同步，游戏结束后合并补同步一次/);
  assert.match(spec, /替代每次同步切换接入环境及结束后恢复普通 QQ/);
  assert.match(spec, /现有游戏全部加入检测.*定期扫描/);
  assert.doesNotMatch(spec, /具体时刻.*待确认/);
  const active = requirements.split('\n').filter(line => line.startsWith('- ')).join('\n');
  assert.doesNotMatch(active, /自动切换普通 QQ 与消息接入环境|同步结束后恢复普通 QQ|游戏检测和中途开始游戏时的停止处理尚未实现/);
});

test('先完成确定功能，再统一进行真实验收', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md']) {
    const current = read(file).split('## 2.')[0];
    assert.match(current, /(?:当前开发顺序|最新工作顺序).*先.*(?:功能|已确定).*统一.*验收/s, file);
    assert.match(current, /Windows 登录自动启动.*OCR.*完整编辑.*合并拆分.*备份恢复.*桌面卡片恢复/s, file);
    assert.match(current, /(?:统一|真实).*验收.*(?:后置|完成后)/s, file);
    assert.match(current, /游戏禁止同步.*(?:保持有效|有效需求不变)/, file);
    assert.match(current, /关机\/睡眠漏同步规则仍(?:是)?未决定事项|关机\/睡眠漏同步规则待确认/, file);
  }
});

test('真实首次同步和常驻通过，固定时刻与游戏实测仍待验收', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'src/MessagePurify.Windows/App/README.md', 'tests/README.md']) {
    const content = read(file);
    assert.match(content, /真实.*首次.*(?:后台同步|同步).*已通过|真实.*首次.*后台同步.*通过|真实首次同步.*常驻.*验收/, file);
    assert.match(content, /自然.*固定.*(?:尚未验收|未验收|未完成)|自然.*到点验收/, file);
    assert.match(content, /实际游戏.*(?:验收延后|统一.*后置|仍需实测|尚未验收)/s, file);
    assert.match(content, /48 项待确认/, file);
    assert.match(content, /0 个.*任务\/通知/, file);
    assert.match(content, /(?:QQ\/正式应用|QQ\/Framework 与正式应用|QQ\/接口与正式应用|QQ 和正式应用).*(?:保持运行|继续运行)|当前 QQ\/Framework 和正式应用运行/, file);
  }
});

test('单条真实任务按原消息日期确认，完成撤销重开与全量验收分别记录', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'src/MessagePurify.Windows/App/README.md', 'tests/README.md']) {
    const content = read(file);
    assert.match(content, /用户.*就按日期保存|用户.*确认按日期保存/, file);
    assert.match(content, /2026-10-09/, file);
    assert.match(content, /无钟点|无具体钟点/, file);
    assert.match(content, /从卡片完成.*(?:撤销|主界面撤销).*退出重开/, file);
    assert.match(content, /1 个待办任务、46 项未处理待确认、1 项已忽略/, file);
    assert.match(content, /48 项待确认是 review 类总数，包含 1 项已忽略/, file);
    assert.match(content, /不.*(?:完整端到端|全部真实任务场景)/, file);
  }
});

test('M3 正在实施，实测条件不被协议模拟替代', () => {
  const plan = read('docs/superpowers/plans/2026-10-10-integration-probes.md');
  for (const label of ['Task 1', 'Task 2', 'Task 3', 'get_group_msg_history', 'JSON Lines', 'activationObserved']) {
    assert.ok(plan.includes(label), `M3 契约缺失：${label}`);
  }
  assert.match(plan, /M3 所有真实验收项完成前保持“正在实施”/);
  const report = read('docs/verification/integration-probes.md');
  assert.match(report, /受控.*不替代用户实际.*点击验收/, '受控回调不能替代用户点击');
  assert.match(report, /包含冷启动修复的正式发布产物重发/, '保留此前运行产物范围');
  assert.match(report, /用户实际点击通过/, '同步用户已经确认的点击结果');
  assert.match(report, /点击记录与发送 Probe ID 一致/, '真实点击须与对应提醒核对');
  assert.match(report, /静音呈现及用户实际点击通过/, '同步静音提醒的真实验收');
  assert.match(report, /长期通知稳定性仍未验收/, '一次提醒不替代长期稳定性');
  assert.match(report, /controlled-shell-/, '受控 Shell 分发应与真实点击区分');
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'docs/superpowers/specs/2026-10-10-windows-app-design.md']) {
    const current = read(file).split('## 2.')[0];
    assert.match(current, /M3.*正在实施/, file);
    assert.match(current, /用户实际点击通过/, file);
    const overview = current.split("###")[0];
    assert.match(overview, /静音.*(显示|呈现).*点击.*通过/, `${file} 同步静音验收`);
    assert.doesNotMatch(overview, /用户实际通知点击.*仍未验收|实际点击仍未观察到激活回调|实际点击.*仍待反馈/, `${file} 当前总览不保留过时点击结论`);
  }
});

test('历史分页说明区分读取范围和未补全原因', () => {
  const readme = read('tools/MessagePurify.QqProbe/README.md');
  for (const term of ['--history-days', '--history-pages', 'all', 'reverse_order=true', 'real_seq', 'message_seq', 'history-no-progress', 'history-page-limit', '1200 不等同于空页']) {
    assert.ok(readme.includes(term), `历史分页说明缺失：${term}`);
  }
  assert.match(readme, /不保证完整七天记录/);
  assert.match(readme, /不输出.*历史游标/);
  assert.match(read('docs/verification/integration-probes.md'), /历史分页.*真实/);
});

test('受控重连说明保留账号核对、去重和正式补漏边界', () => {
  const readme = read('tools/MessagePurify.QqProbe/README.md');
  for (const term of ['--reconnect-rounds', '不复用旧游标', '账号不符立即停止', '取消后不再连接', '跨连接保留', '非预期断网的自动恢复', '跨进程持久化']) {
    assert.ok(readme.includes(term), `受控重连说明缺失：${term}`);
  }
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'docs/verification/integration-probes.md']) {
    const current = read(file).split('### 本次静音提醒验收')[0];
    assert.match(current, /受控.*重连/, `${file} 记录本轮重连范围`);
    assert.match(current, /正式.*(未完成|尚未|未验收)/, `${file} 不替代正式应用进度`);
  }
});

test('第一版取消系统通知及声音入口，旧静音 probe 记录保留', () => {
  const requirements = read('docs/requirements.md');
  assert.match(requirements, /第一版取消全部 Windows 系统通知弹窗和声音/);
  assert.match(requirements, /不提供声音、系统提醒、自定义提醒时间或安静时段入口/);
  assert.match(requirements, /不进行通知权限恢复、通知点击或声音播放验收/);
  assert.doesNotMatch(requirements, /播放系统声音|请求一次系统声音/);
  for (const file of ['docs/HANDOFF.md', 'docs/superpowers/specs/2026-10-10-windows-app-design.md', 'docs/superpowers/plans/2026-10-10-windows-app-implementation.md']) {
    assert.match(read(file), /取消全部 Windows 系统通知弹窗和声音/, file);
  }
  assert.match(read('docs/verification/integration-probes.md'), /用户取消声音提醒，第一版所有 Windows 通知均静音/);
  assert.match(read('docs/verification/integration-probes.md'), /静音呈现及用户实际点击通过/);
});

test('学校群名仅辅助候选，保留用户确认及非正式名称例外', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'docs/superpowers/specs/2026-10-10-windows-app-design.md']) {
    const content = read(file);
    assert.match(content, /红框.*学校相关群/, file);
    assert.match(content, /非正式名称/, file);
    assert.match(content, /群名.*辅助|名称.*辅助/, file);
  }
});

test('实施总计划覆盖设计中的全部真实验收场景', () => {
  const spec = read('docs/superpowers/specs/2026-10-10-windows-app-design.md');
  const section = spec.split('## 12. 后续真实验收')[1].split('## 13.')[0];
  const scenarios = section.split('\n').map(line => line.trim()).filter(line => line.startsWith('| ') && !line.startsWith('| ---')).map(line => line.split('|')[1].trim()).filter(label => label !== '场景');
  assert.ok(scenarios.length > 0, '设计验收表为空或格式变化');
  const plan = read('docs/superpowers/plans/2026-10-10-windows-app-implementation.md');
  const rows = new Map(plan.split('\n').filter(line => line.startsWith('| ')).map(line => line.split('|').slice(1, 3).map(value => value.trim())));
  for (const scenario of scenarios) {
    assert.ok(rows.has(scenario), `计划遗漏验收场景：${scenario}`);
    for (const stage of rows.get(scenario).split('、')) {
      assert.match(stage, /^M[1-9]$/, `无效阶段：${stage}`);
      assert.ok(plan.includes(`### ${stage}：`), `阶段未定义：${stage}`);
    }
  }
});
