import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { actionName, extractPinnedGroups, registerPinnedGroups } from '../packaging/napcat/pinned-groups.mjs';

const group = (peerUin, topFlag, chatType = 2) => ({ peerUin, topFlag, chatType, peerName: 'private-name', msgId: 'private-id' });
const snapshot = (...contacts) => ({ result: 0, info: { errCode: 0, changedList: contacts, sortedContactList: contacts.map((_, index) => index) } });

test('置顶桥只返回置顶群 ID，不读取消息正文，不包含私聊或群名', () => {
  const result = extractPinnedGroups(snapshot(group('123', 1), group('124', 0), group('125', 1, 1), group('123', 1)), '456');
  assert.deepEqual(result, { self_id: '456', group_ids: [{ group_id: '123' }] });
  assert.deepEqual(extractPinnedGroups(snapshot(group('124', 0)), '456').group_ids, []);
});

test('未知置顶标记、失败或达到快照上限均暂停，不回退到全群', () => {
  for (const data of [null, { info: { errCode: 1, changedList: [] } }, snapshot(group('123', undefined)),
    snapshot(group('123', true)), snapshot(group('123', 2)), snapshot(group('bad-id', 1)),
    { result: 0, info: { errCode: 0, changedList: [], sortedContactList: [1] } },
    snapshot(...Array.from({ length: 10000 }, () => group('123', 0)))]) {
    assert.throws(() => extractPinnedGroups(data, '456'), /pinned-state-unavailable/);
  }
});

test('只注册一个读取 action，重复调用重新获取置顶状态', async () => {
  let calls = 0;
  const existing = { actionName: 'get_status' };
  const context = { actions: { get: key => key === 'get_status' ? existing : undefined, call: () => 'original-call' }, core: { selfInfo: { uin: '456' }, context: { session: {
    getRecentContactService: () => ({ getRecentContactListSnapShot: async count => {
      assert.equal(count, 10000); calls++; return snapshot(group('123', calls === 1 ? 1 : 0));
    } })
  } } } };
  class Action { constructor(context, core) { this.core = core; } }
  registerPinnedGroups(context, Action);
  assert.equal(context.actions.get('get_status'), existing);
  assert.equal(context.actions.call(), 'original-call');
  assert.equal(context.actions.get('send_group_msg'), undefined);
  const action = context.actions.get(actionName);
  assert.equal((await action._handle({})).group_ids.length, 1);
  assert.equal((await action._handle({})).group_ids.length, 0);
  const diagnosis = JSON.stringify(await action._handle({ schema_only: true }));
  for (const privateValue of ['private-name', 'private-id', '123', '456']) assert.ok(!diagnosis.includes(privateValue));
});

test('准备脚本保留官方文件并生成独立副本，加载器使用同一副本', t => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'message-purify-pinned-'));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(temporary)), path.resolve(os.tmpdir()));
    assert.ok(path.basename(temporary).startsWith('message-purify-pinned-'));
    fs.rmSync(temporary, { recursive: true, force: true });
  });
  const official = 'class OneBotAction {}\nconst WebUiDataRuntime = {};\nexport function NCoreInitFramework() {}\n';
  fs.writeFileSync(path.join(temporary, 'napcat.mjs'), official);
  const prepare = fileURLToPath(new URL('../packaging/napcat/Prepare-PinnedGroups.ps1', import.meta.url));
  const run = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', prepare, '-PackageDirectory', temporary], { encoding: 'utf8' });
  assert.equal(run.status, 0, run.stderr);
  assert.equal(fs.readFileSync(path.join(temporary, 'napcat.mjs'), 'utf8'), official);
  assert.match(fs.readFileSync(path.join(temporary, 'messagepurify-napcat.mjs'), 'utf8'), /export \{ OneBotAction, WebUiDataRuntime \}/);
  const loader = fs.readFileSync(path.join(temporary, 'nativeLoaderPinned.cjs'), 'utf8');
  assert.match(loader, /messagepurify-napcat\.mjs/);
  assert.match(loader, /registerPinnedGroups/);
  const syntax = spawnSync(process.execPath, ['--check', path.join(temporary, 'messagepurify-napcat.mjs')], { encoding: 'utf8' });
  assert.equal(syntax.status, 0, syntax.stderr);
});
