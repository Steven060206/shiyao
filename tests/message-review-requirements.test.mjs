import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

test('用户已选择一条原消息一个完整待确认项，保留人工结果与日期核对', () => {
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
