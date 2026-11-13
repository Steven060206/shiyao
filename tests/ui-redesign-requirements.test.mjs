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

test('设计规范的浅深色tokens与正式CSS一致，扩展资料可解析且预览存在', () => {
  const design = read('DESIGN.md'), css = read('src/MessagePurify.Windows/App/ui/app.css');
  const [light, dark] = css.split(':root[data-theme="dark"]');
  const colors = [...design.matchAll(/^  ([\w-]+): "(#[A-Fa-f0-9]{6})"$/gm)];
  assert.equal(colors.length, 24);
  for (const [, name, value] of colors) {
    const isDark = name.endsWith('-dark'), token = name.replace(/-dark$/, '');
    assert.match(isDark ? dark : light, new RegExp(`--${token}: ${value};`, 'i'));
  }
  const extensions = JSON.parse(read('.impeccable/design.json'));
  assert.equal(extensions.schemaVersion, 2);
  assert.equal(extensions.narrative.northStar, '安静的个人助理');
  for (const page of ['tasks','pending','settings','desktop']) for (const theme of ['light','dark']) {
    assert.ok(fs.statSync(new URL(`../docs/verification/ui-redesign/${page}-${theme}.png`, import.meta.url)).size > 1000);
  }
});

test('界面交付记录区分已发布版本、保留数据与尚未完成的真实验收', () => {
  const report = read('docs/verification/2026-11-13-ui-redesign.md');
  const requirements = read('docs/requirements.md');
  const current = read('docs/HANDOFF.md').split('### 本机同步端口调整')[0];
  for (const document of [report, requirements, current]) {
    assert.match(document, /beta\.14/);
    assert.match(document, /0786b40/);
    assert.match(document, /四库16表全行及5/);
    assert.match(document, /ui-redesign\/rollback\/program/);
    assert.match(document, /ui-redesign\/beta14-before-deploy/);
    assert.match(document, /100%\/150%/);
    assert.match(document, /真实QQ图片/);
  }
  assert.doesNotMatch(current, /尚未替换正式程序或发布|下一步干净打包/);
  assert.match(report, /releases\/download\/v0\.1\.0-beta\.14\/Shiyao-0\.1\.0-beta\.14-win-x64\.zip/);
  assert.match(report, /本次没有数据迁移/);
  assert.match(report, /未完成|仍待/);
  assert.match(read('README.md'), /ui-redesign\/README\.md/);
});
