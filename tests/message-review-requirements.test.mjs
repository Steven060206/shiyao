import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

test('原消息重复审核反馈记录定位依据，并等待用户选择审核粒度', () => {
  for (const file of ['docs/requirements.md', 'docs/HANDOFF.md']) {
    const content = fs.readFileSync(new URL(`../${file}`, import.meta.url), 'utf8');
    const feedback = content.split('审核粒度反馈（2026-10-15')[1]?.split('\n### ')[0];
    assert.ok(feedback, `${file}: missing message review feedback`);
    assert.match(feedback, /已确认需要减少同一原消息的重复审核/);
    assert.match(feedback, /MessageRuleExtractor\.SplitActions/);
    assert.match(feedback, /AccountCandidateRouter/);
    assert.match(feedback, /未决定事项/);
    assert.match(feedback, /A 默认一条原消息一个完整待确认项/);
    assert.match(feedback, /B 保留多个事项但集中在一张审核卡处理/);
    assert.match(feedback, /等待用户明确选择后实施/);
    assert.match(feedback, /不能把任一方案记为已确认或已修复/);
  }
});
