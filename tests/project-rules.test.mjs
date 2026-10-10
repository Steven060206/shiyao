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

test('项目文档的本地链接与已交付原型文件存在', () => {
  const files = [...documents, 'docs/superpowers/specs/2026-10-10-windows-app-design.md'];
  for (const file of files) {
    for (const match of read(file).matchAll(/\[[^\]]+\]\(([^)]+)\)/g)) {
      const target = match[1];
      if (/^[a-z]+:/i.test(target) || target.startsWith('#')) continue;
      const resolved = path.resolve(root, path.dirname(file), target.split('#')[0]);
      assert.ok(fs.existsSync(resolved), `${file} 链接目标不存在：${target}`);
    }
  }
  for (const file of ['prototype/index.html', ...['main', 'main-dark', 'desktop', 'desktop-dark'].map(name => `prototype/previews/${name}.png`), 'tests/prototype-qa.mjs']) {
    assert.ok(fs.statSync(path.join(root, file)).isFile(), `交付文件缺失：${file}`);
  }
});

test('需求、交接及设计仍一致记录整体设计待核对', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md', 'docs/superpowers/specs/2026-10-10-windows-app-design.md']) {
    assert.match(read(file), /(?:整体设计|完整设计)[^。；\n]*(?:未确认|待.*核对)/, file);
  }
});
