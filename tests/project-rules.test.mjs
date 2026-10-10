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
  const files = [...documents, 'docs/superpowers/specs/2026-10-10-windows-app-design.md', 'docs/superpowers/plans/2026-10-10-windows-app-implementation.md', 'docs/superpowers/plans/2026-10-10-core-foundation.md'];
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
    'tests/MessagePurify.Core.Tests/MessagePurify.Core.Tests.csproj',
    ...['TaskTime', 'ReminderPlanner', 'TaskTransitions'].map(name => `tests/MessagePurify.Core.Tests/${name}Tests.cs`),
    ...['src/MessagePurify.Core', 'tests/MessagePurify.Core.Tests'].map(folder => `${folder}/packages.lock.json`)
  ];
  for (const file of artifacts) {
    assert.ok(fs.statSync(path.join(root, file)).isFile(), `交付文件缺失：${file}`);
  }
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
    assert.match(read(file).split('## 2.')[0], /M2.*正在实施|M2 已完成/, `${file} 需同步本地规则阶段`);
  }
});

test('QQ 登录与实际连接状态分别记录', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md']) {
    const content = read(file);
    assert.match(content, /用户.*QQ.*(?:电脑|本机)登录/, `${file} 需记录用户确认的登录`);
    assert.match(content, /尚未验证 OneBot 连接|不作为 OneBot 连接成功/, `${file} 需区分登录和接入`);
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
