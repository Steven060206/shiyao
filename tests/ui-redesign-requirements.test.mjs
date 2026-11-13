import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

const read = relative => fs.readFileSync(new URL('../' + relative, import.meta.url), 'utf8');
test('正式版界面改版的已确认边界、计划与接续记录一致', () => {
  const plan = read('docs/superpowers/plans/2026-11-13-shiyao-ui-redesign.md');
  for (const document of ['docs/requirements.md', 'docs/HANDOFF.md']) {
    assert.match(read(document), /superpowers\/plans\/2026-11-13-shiyao-ui-redesign\.md/);
  }
  assert.match(plan, /codex\/m3-integration-probes/);
  assert.match(plan, /原消息时间倒序/);
  assert.match(plan, /QQ 只读/);
  assert.match(plan, /不增加公共API、桥命令、数据库迁移或备份格式/);
  assert.match(plan, /页面URL稳定/);
  assert.match(plan, /Native使用隔离合成数据/);
  assert.match(plan, /待确认原文默认展开/);
});
