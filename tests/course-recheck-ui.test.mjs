import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import test from 'node:test';

const root = fileURLToPath(new URL('../', import.meta.url));
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const task = { id: 'task1', kind: 'task', state: 'active', title: '原任务', course: '物理', group: '课程群', source: 'qq', due: '2026-12-20', time: null, timeKind: 'deadline', priority: 3, version: 1, unread: false, evidence: [], reviewReasons: [], action: 'submit' };
const conflict = { ...task, id: 'course-conflict', kind: 'review', state: 'pending', title: '课程关联需要核对', targetTaskId: 'task1', targetTaskVersion: 1, canAccept: false, canAcceptNotice: false, isCourseConflict: true, reviewReasons: ['课程关联已修改，原任务和人工处理已保留。'] };
const fixture = { accountId: '12345', generation: 0, generatedAt: '2026-10-28T01:00:00Z', items: [task, conflict], reminders: [], groups: [{ groupId: '100', name: '课程群', suggestedCourse: '物理', course: '物理', excluded: false, pinned: true }] };
const result = { accountId: '12345', groupId: '100', course: '物理', excluded: false, completedAt: '2026-10-28T02:00:00Z', checkedMessages: 12, checkedTasks: 2, addedTasks: 1, addedNotices: 2, addedReviews: 3, preservedMessages: 6, firstMessageAt: '2026-10-20T01:00:00Z', lastMessageAt: '2026-10-27T02:00:00Z', missingEvidenceMessages: 2, skippedUnpinned: false };
const bridgeScript = `(() => {
  const listeners = new Set();
  const backend = window.backend = { snapshot: ${JSON.stringify(fixture)}, messages: [], clock: Date.now() };
  backend.emit = data => listeners.forEach(listener => listener({data: JSON.parse(JSON.stringify(data))}));
  backend.push = () => { backend.snapshot.generatedAt = new Date(++backend.clock).toISOString(); backend.emit({type:'state',snapshot:backend.snapshot}); };
  window.chrome ||= {};
  window.chrome.webview = {
    addEventListener: (type, listener) => { if (type === 'message') listeners.add(listener); },
    postMessage: command => {
      backend.messages.push(JSON.parse(JSON.stringify(command)));
      queueMicrotask(() => {
        const p = command.parameters || {}, item = backend.snapshot.items.find(item => item.id === p.id);
        if (command.operation === 'mark-read' && item) item.unread = false;
        if (['reject','ignore'].includes(command.operation) && item) { item.state = command.operation === 'reject' ? 'rejected' : 'ignored'; item.version++; }
        if (command.operation === 'confirm-group') { const group=backend.snapshot.groups.find(group=>group.groupId===p.id); group.course=p.course; group.excluded=p.excluded; }
        backend.snapshot.generatedAt = new Date(++backend.clock).toISOString();
        backend.emit({id:command.id,success:true,snapshot:backend.snapshot});
      });
    }
  };
})();`;

test('课程历史重查结果和冲突入口在正式浏览器页面中可核对', { timeout: 45000 }, async t => {
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'messagepurify-course-qa-'));
  const browser = spawn(process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] });
  let socket, stderr = '', sequence = 0;
  const pending = new Map(), errors = [];
  browser.stderr.on('data', data => stderr += data.toString());
  try {
    const portFile = path.join(profile, 'DevToolsActivePort');
    for (let i = 0; i < 120 && !fs.existsSync(portFile); i++) await delay(100);
    assert.ok(fs.existsSync(portFile), 'Chrome 未启动：' + stderr.slice(-1000));
    const port = fs.readFileSync(portFile, 'utf8').split('\n')[0];
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    socket = new WebSocket(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    socket.addEventListener('message', event => {
      const message = JSON.parse(event.data);
      if (message.id) { const handler = pending.get(message.id); pending.delete(message.id); message.error ? handler?.reject(new Error(JSON.stringify(message.error))) : handler?.resolve(message.result); }
      else if (message.method === 'Runtime.exceptionThrown') errors.push(message.params.exceptionDetails.exception?.description || message.params.exceptionDetails.text);
    });
    const call = (method, params = {}) => new Promise((resolve, reject) => { const id = ++sequence; pending.set(id, { resolve, reject }); socket.send(JSON.stringify({ id, method, params })); });
    const evaluate = async expression => { const value = await call('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }); if (value.exceptionDetails) throw new Error(value.exceptionDetails.exception?.description || value.exceptionDetails.text); return value.result.value; };
    const click = selector => evaluate(`document.querySelector(${JSON.stringify(selector)}).click()`);
    const nav = page => click(`[data-action=nav][data-page=${page}]`);
    const push = snapshot => evaluate(`backend.snapshot=${JSON.stringify(snapshot)};backend.push()`);
    const text = selector => evaluate(`document.querySelector(${JSON.stringify(selector)}).textContent`);
    const change = (selector, value) => evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.focus();e.value=${JSON.stringify(value)};e.dispatchEvent(new Event('input',{bubbles:true}));})()`);
    const selectConflict = async () => { await nav('pending'); await click('[data-action=select][data-id=course-conflict]'); };
    await call('Runtime.enable'); await call('Page.enable');
    await call('Emulation.setTimezoneOverride', { timezoneId: 'America/New_York' });
    await call('Page.addScriptToEvaluateOnNewDocument', { source: bridgeScript });
    await call('Page.navigate', { url: pathToFileURL(path.join(root, 'src/MessagePurify.Windows/App/ui/index.html')).href });
    for (let i = 0; i < 100; i++) { if (await evaluate('document.readyState === "complete" && !!window.backend?.messages.length && !!document.querySelector(".task-row")')) break; await delay(50); }

    await t.test('无重查记录时显示空状态，并说明保存关联会检查保留消息', async () => {
      await nav('settings');
      assert.match(await text('#course-recheck-result'), /尚无课程关联重查记录/);
      assert.match(await text('#settings-panel'), /保存关联后重查仍保留的本地消息/);
      assert.doesNotMatch(await text('#settings-panel'), /关联变更用于新消息/);
    });
    await t.test('显示真实数量、北京时间范围和不可用证据', async () => {
      await push({ ...fixture, courseRecheck: result });
      const content = await text('#course-recheck-result');
      assert.match(content, /实际检查 12 条本地消息、2 项任务/);
      assert.match(content, /新增任务 1 项、通知 2 项、待确认 3 项/);
      assert.match(content, /保留已有处理 6 条/);
      assert.match(content, /2026-10-20 09:00（北京时间） 至 2026-10-27 10:00（北京时间）/);
      assert.match(content, /2 条任务证据原消息暂不可用/);
    });
    await t.test('结果刷新保留正在编辑的课程草稿和焦点，重查错误可见且转义', async () => {
      await change('[data-group-id="100"] input[name=course]', '尚未保存的课程');
      await push({ ...fixture, courseRecheck: { ...result, checkedMessages: 18 }, courseRecheckError: '<重查失败> 请重新确认关联重试' });
      assert.match(await text('#course-recheck-result'), /实际检查 18 条/);
      assert.equal(await evaluate('document.querySelector("[data-group-id=\\"100\\"] input").value'), '尚未保存的课程');
      assert.equal(await evaluate('document.activeElement.name'), 'course');
      assert.equal(await evaluate('document.querySelector("#course-recheck-error").hidden'), false);
      assert.match(await text('#course-recheck-error'), /<重查失败>/);
      assert.equal(await evaluate('document.querySelector("#course-recheck-error").children.length'), 0);
    });
    await t.test('其他账号、其他群和旧关联的结果均不显示', async () => {
      for (const other of [{ ...result, accountId: '67890' }, { ...result, groupId: '101' }, { ...result, course: '旧课程' }, { ...result, excluded: true }]) {
        await push({ ...fixture, courseRecheck: other });
        assert.match(await text('#course-recheck-result'), /尚无课程关联重查记录/);
      }
      await push({ ...fixture, accountId: '67890', courseRecheck: result });
      assert.match(await text('#course-recheck-result'), /尚无课程关联重查记录/);
      await push(fixture); await nav('settings');
    });
    await t.test('群名按文字呈现，非置顶跳过和无保留消息分别说明', async () => {
      const name = '<img data-recheck-injection="yes" src="bad">';
      await push({ ...fixture, groups: [{ ...fixture.groups[0], name }], courseRecheck: { ...result, checkedMessages: 0, firstMessageAt: null, lastMessageAt: null, missingEvidenceMessages: 0 } });
      assert.match(await text('#course-recheck-result'), /<img data-recheck-injection="yes" src="bad">/);
      assert.match(await text('#course-recheck-result'), /本群暂无保留消息/);
      assert.equal(await evaluate('document.querySelector("img[data-recheck-injection]")'), null);
      await push({ ...fixture, groups: [{ ...fixture.groups[0], pinned: false }], courseRecheck: { ...result, skippedUnpinned: true } });
      assert.match(await text('#course-recheck-result'), /此群当前未置顶，本次未重查消息/);
      assert.doesNotMatch(await text('#course-recheck-result'), /实际检查 12/);
    });
    await t.test('课程冲突无批量勾选、接受表单或任务和通知确认按钮', async () => {
      await push({ ...fixture, items: [task, { ...conflict, canAccept: true, canAcceptNotice: true }] });
      await selectConflict();
      assert.equal(await evaluate('document.querySelector("[data-batch-id=course-conflict]")'), null);
      assert.equal(await evaluate('document.querySelector("#review-form")'), null);
      assert.equal(await evaluate('document.querySelector("#pending-confirm-button")'), null);
      assert.match(await text('#detail-pane'), /原任务内容和状态已保留/);
      assert.equal(await text('[data-action=open-target]'), '查看原任务');
      assert.equal(await text('[data-action=reject]'), '保留原任务');
      assert.equal(await text('[data-action=ignore]'), '忽略');
      await click('[data-action=open-target]');
      assert.equal(await text('.detail-title'), '原任务');
      assert.equal(await evaluate('document.querySelector("#time-form input[name=course]").value'), '物理');
      assert.deepEqual(await evaluate('backend.snapshot.items.find(item=>item.id==="task1")'), task);
    });
    await t.test('忽略和保留原任务只处理建议，不修改原任务或发送接受操作', async () => {
      for (const action of ['ignore', 'reject']) {
        await push(fixture); await selectConflict(); await click(`[data-action=${action}]`);
        assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="course-conflict").state'), action === 'ignore' ? 'ignored' : 'rejected');
        assert.deepEqual(await evaluate('backend.snapshot.items.find(item=>item.id==="task1")'), task);
      }
      assert.equal(await evaluate('backend.messages.filter(message=>["accept-review","accept-notice","edit-task"].includes(message.operation)).length'), 0);
    });
    await t.test('确认关联沿用现有命令，只提交当前课程草稿', async () => {
      await push(fixture); await nav('settings');
      await change('[data-group-id="100"] input[name=course]', '新课程');
      await click('[data-group-id="100"] [data-action=confirm-group]');
      const command = await evaluate('backend.messages.findLast(message=>message.operation==="confirm-group")');
      assert.deepEqual({ ...command.parameters, operationId: 'id' }, { accountId: '12345', id: '100', operationId: 'id', version: 0, course: '新课程', excluded: false, generation: 0 });
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="sync-now").length'), 0);
    });
    assert.deepEqual(errors, []);
  } finally {
    socket?.close(); browser.kill();
    await new Promise(resolve => { if (browser.exitCode !== null) resolve(); else { const timer = setTimeout(resolve, 3000); browser.once('exit', () => { clearTimeout(timer); resolve(); }); } });
    if (path.dirname(path.resolve(profile)) === path.resolve(os.tmpdir()) && path.basename(profile).startsWith('messagepurify-course-qa-')) {
      try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 2, retryDelay: 100 }); } catch {}
    }
  }
});
