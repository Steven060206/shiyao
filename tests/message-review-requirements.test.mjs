import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

test('用户已选择一条原消息一个完整待确认项，保留人工结果与日期核对', () => {
  const readme = fs.readFileSync(new URL('../README.md', import.meta.url), 'utf8');
  assert.match(readme, /一条 QQ 原消息只生成一个完整待确认项/);
  assert.match(readme, /手动拆分/);
  assert.match(readme, /保留已确认、已忽略和手动拆分的结果/);
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md']) {
    const content = fs.readFileSync(new URL(`../${file}`, import.meta.url), 'utf8');
    const feedback = content.split('审核粒度反馈（2026-10-15')[1]?.split('\n### ')[0];
    assert.ok(feedback, `${file}: missing message review feedback`);
    assert.match(feedback, /已确认需要减少同一原消息的重复审核/);
    assert.match(feedback, /MessageRuleExtractor\.SplitActions/);
    assert.match(feedback, /AccountCandidateRouter/);
    assert.match(feedback, /用户已选择 A/);
    assert.match(feedback, /一条 QQ 原消息只生成一个完整待确认项/);
    assert.match(feedback, /手动拆开/);
    assert.match(feedback, /日期.*不.*任选/s);
    assert.match(feedback, /已确认、已忽略、已拒绝/);
    assert.match(feedback, /不同群.*消息 ID/s);
  }
});

test('最新交付按真实物理数据记账，保留退出证据边界', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md']) {
    const content = fs.readFileSync(new URL(`../${file}`, import.meta.url), 'utf8');
    const current = file.endsWith('requirements.md')
      ? content.match(/^最新交付状态.*$/m)?.[0]
      : content.split('### 当前接续')[1]?.split('### 上一段')[0];
    assert.ok(current, `${file}: missing current delivery section`);
    assert.match(current, /MSIX/);
    assert.match(current, /两套profile分别保护、未合并/);
    assert.match(current, /99条审核/);
    assert.match(current, /84条已处理记录/);
    assert.match(current, /187条其他历史操作/);
    assert.match(current, /12个子项/);
    assert.match(current, /整理后pending=3/);
    assert.match(current, /提醒卡隐藏、待确认卡显示/);
    assert.match(current, /迁移后.*快照/);
    assert.doesNotMatch(current, /无需合并|无需迁移/);
    assert.match(current, /accept-review1、complete1、ignore2/);
    assert.match(current, /ReadVersions/);
    assert.match(current, /当前activeTasks=0、pending=0，两卡均隐藏/);
    assert.match(current, /Explorer.*--background/);
    assert.match(current, /不在工具Job内/);
    assert.match(current, /未捕获直接退出码/);
    assert.match(current, /退出根因尚不能完全证实/);
  }
});
