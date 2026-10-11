import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const documents = ['AGENTS.md', 'docs/requirements.md', 'docs/HANDOFF.md'];

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
    'tests/MessagePurify.Infrastructure.Tests/TaskLifecycleTests.cs'
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
    assert.match(current, /真实 QQ.*(?:未完成|未.*联调|未接入|宿主联调)/, file);
  }
  assert.match(read(planFile), /SuggestedTime.*(?:永远|不能)/);
  assert.match(read(planFile), /expectedTaskVersion/);
  assert.match(read(planFile), /普通通知转换.*后续|普通通知转换.*不在本段/);
});

test('新版演示交付链接可用，保存说明与真实集成阶段一致', () => {
  for (const match of read('prototype/index.html').matchAll(/<script src="([^"]+)"/g)) {
    assert.ok(fs.statSync(path.resolve(root, 'prototype', match[1])).isFile(), `演示脚本缺失：${match[1]}`);
  }
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'prototype/README.md']) {
    const content = read(file);
    assert.match(content, /本地存储/, `${file} 需说明演示保存方式`);
    assert.match(content, /撤销.*真实经过时间|真实经过时间.*撤销/, `${file} 需说明撤销计时与演示日期区分`);
    assert.match(content, /真实应用(?:实现|代码)尚未开始|Windows.*尚未接入/, `${file} 需说明真实集成边界`);
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
    assert.match(current, /尚未接入/, `${file} 需保留真实集成状态`);
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

test('未确认的自动切换方案只保留在交接事项中', () => {
  const handoff = read('docs/HANDOFF.md');
  const requirements = read('docs/requirements.md');
  assert.match(handoff, /未决定：定时自动切换普通 QQ 与 NapCat/);
  assert.match(handoff, /尚未确认要开发/);
  assert.doesNotMatch(requirements, /定时自动切换普通 QQ 与 NapCat/);
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

test('第一版统一静音，取消声音开关及声音播放验收', () => {
  const requirements = read('docs/requirements.md');
  assert.match(requirements, /第一版取消声音提醒，所有 Windows 通知均静音/);
  assert.match(requirements, /不提供“任务声音提醒”开关，也不进行声音播放验收/);
  assert.doesNotMatch(requirements, /播放系统声音|请求一次系统声音/);
  for (const file of ['docs/HANDOFF.md', 'docs/superpowers/specs/2026-10-10-windows-app-design.md', 'docs/superpowers/plans/2026-10-10-windows-app-implementation.md', 'docs/verification/integration-probes.md']) {
    assert.match(read(file), /第一版取消声音提醒，所有 Windows 通知均静音|用户取消声音提醒，第一版所有 Windows 通知均静音/, file);
  }
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
