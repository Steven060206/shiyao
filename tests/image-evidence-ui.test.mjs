import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = fileURLToPath(new URL('../src/MessagePurify.Windows/App/ui/', import.meta.url));
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const ids = ['a', 'b', 'c', 'd', 'e', 'f'].map(letter => letter.repeat(64));
const url = (id, generation = 4, account = '12345') => `/evidence/${account}/${generation}/${id}`;
const images = [
  { id: ids[0], status: 'recognized', url: url(ids[0]), ocrText: '<img data-ocr-injected="yes">明天18点前提交作业', canRetry: false },
  { id: ids[1], status: 'ocr-failed', url: url(ids[1]), ocrText: null, error: 'image-ocr-failed', canRetry: true },
  { id: ids[2], status: 'download-failed', url: null, ocrText: null, error: 'image-download-failed', canRetry: true },
  { id: ids[3], status: 'pending', url: null, ocrText: null, canRetry: false },
  { id: ids[4], status: 'downloaded', url: url(ids[4]), ocrText: null, canRetry: false },
  { id: ids[5], status: 'recognized', url: url(ids[5]), ocrText: '', canRetry: false }
];
const fixture = { accountId: '12345', generation: 4, generatedAt: '2026-10-28T12:00:00Z', groups: [], reminders: [], items: [{
  id: 'image-review', kind: 'review', state: 'pending', title: '图片作业', course: '数学', group: '数学群', version: 1,
  source: 'qq', timeKind: 'deadline', reviewReasons: ['图片中的日期须确认'], action: 'submit', canAccept: true,
  evidence: [{ groupId: '100', groupName: '数学群', senderId: '456', sentAt: '2026-10-28T10:00:00Z', text: '<原消息正文>', imageCount: 6, images }]
}] };
const bridgeScript = `(() => {
  const listeners = new Set();
  const backend = window.backend = { snapshot: ${JSON.stringify(fixture)}, messages: [], clock: Date.now(), holdRetry: false };
  backend.emit = data => listeners.forEach(listener => listener({data:JSON.parse(JSON.stringify(data))}));
  backend.push = (extra = {}) => { backend.snapshot.generatedAt = new Date(++backend.clock).toISOString(); backend.emit({type:'state',snapshot:backend.snapshot,...extra}); };
  backend.respondRetry = success => {
    const command = backend.retryCommand; backend.retryCommand = null;
    if (success) for (const source of [...(backend.snapshot.imageQueue || []), ...backend.snapshot.items.flatMap(item=>item.evidence || [])]) for (const image of source.images || [])
      if (image.id === command.parameters.id) { image.status = image.url ? 'downloaded' : 'pending'; image.canRetry = false; }
    backend.snapshot.generatedAt = new Date(++backend.clock).toISOString();
    backend.emit({id:command.id,success,error:success?null:'图片重试失败',snapshot:backend.snapshot});
  };
  window.chrome ||= {};
  window.chrome.webview = {
    addEventListener: (type, listener) => { if(type === 'message') listeners.add(listener); },
    postMessage: command => {
      backend.messages.push(JSON.parse(JSON.stringify(command)));
      if(command.operation === 'retry-image') { backend.retryCommand=command; if(!backend.holdRetry) queueMicrotask(()=>backend.respondRetry(true)); return; }
      queueMicrotask(()=> { backend.snapshot.generatedAt=new Date(++backend.clock).toISOString(); backend.emit({id:command.id,success:true,snapshot:backend.snapshot}); });
    }
  };
})();`;

test('图片证据在正式页面中安全展示、放大、折叠识别文字和重试', { timeout: 45000 }, async t => {
  const original = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXs8AAAAASUVORK5CYII=', 'base64');
  const server = http.createServer((request, response) => {
    if (/^\/evidence\/\d+\/\d+\/[a-f0-9]{64}$/.test(request.url)) { response.writeHead(200, { 'Content-Type': 'image/png' }); response.end(original); return; }
    const relative = request.url === '/' ? 'index.html' : request.url.slice(1);
    const file = path.resolve(root, relative);
    if (!file.startsWith(path.resolve(root) + path.sep) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { response.writeHead(404); response.end(); return; }
    response.writeHead(200, { 'Content-Type': ({ '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.ico': 'image/x-icon' })[path.extname(file)] || 'application/octet-stream' });
    response.end(fs.readFileSync(file));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'messagepurify-image-qa-'));
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
    const text = selector => evaluate(`document.querySelector(${JSON.stringify(selector)}).textContent`);
    const push = (snapshot, extra = {}) => evaluate(`backend.snapshot=${JSON.stringify(snapshot)};backend.push(${JSON.stringify(extra)})`);
    const waitFor = async expression => { for (let i = 0; i < 100; i++) { if (await evaluate(expression)) return; await delay(50); } assert.fail('页面条件未满足：' + expression); };
    await call('Runtime.enable'); await call('Page.enable');
    await call('Page.addScriptToEvaluateOnNewDocument', { source: bridgeScript });
    await call('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/index.html` });
    await waitFor('document.readyState === "complete" && !!window.backend?.messages.length');
    await click('[data-action=nav][data-page=pending]');
    await click('[data-action=select][data-id=image-review]');

    await t.test('原图缩略图真实加载，OCR默认折叠且按文字转义', async () => {
      await waitFor('[...document.querySelectorAll(".evidence-thumbnail")].every(image=>image.complete&&image.naturalWidth>0)');
      assert.equal(await evaluate('document.querySelectorAll(".evidence-thumbnail").length'), 4);
      assert.equal(await evaluate('document.querySelector("details.image-ocr").open'), false);
      assert.match(await text('details.image-ocr pre'), /<img data-ocr-injected="yes">明天18点前提交作业/);
      assert.equal(await evaluate('document.querySelector("[data-ocr-injected]")'), null);
      assert.match(await text('#detail-pane'), /图片中的日期仅作建议，请核对后确认/);
      await click('details.image-ocr summary');
      assert.equal(await evaluate('document.querySelector("details.image-ocr").open'), true);
    });
    await t.test('点击放大显示原图，按钮和Escape均能关闭并释放图片源', async () => {
      const selector = `[data-action=preview-image][data-image-id="${ids[0]}"]`;
      await click(selector);
      await waitFor('document.querySelector("#image-preview-original").naturalWidth>0');
      assert.equal(await evaluate('document.querySelector("#image-preview-dialog").open'), true);
      await click('[data-action=close-image-preview]');
      assert.equal(await evaluate('document.querySelector("#image-preview-dialog").open'), false);
      assert.equal(await evaluate('document.querySelector("#image-preview-original").hasAttribute("src")'), false);
      await click(selector);
      await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
      await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
      await waitFor('!document.querySelector("#image-preview-dialog").open');
    });
    await t.test('下载和识别状态分别显示，空识别无空文字折叠，失败才允许重试', async () => {
      const content = await text('#detail-pane');
      for (const expected of ['等待下载原图', '原图已保存，等待本地识别', '未识别到文字', '原图下载失败，可重试', '文字识别失败，原图已保留']) assert.ok(content.includes(expected));
      assert.equal(await evaluate('document.querySelectorAll("[data-action=retry-image]").length'), 2);
      assert.equal(await evaluate('document.querySelectorAll("details.image-ocr").length'), 1);
      assert.doesNotMatch(content, /图片读取与 OCR 尚未接入/);
    });
    await t.test('重试带账号和代次、阻止重复点击并保留正在填写的正式日期', async () => {
      await evaluate('backend.holdRetry=true;const input=document.querySelector("#review-form input[name=date]");input.value="2026-11-08";input.dispatchEvent(new Event("input",{bubbles:true}))');
      const selector = `[data-action=retry-image][data-image-id="${ids[1]}"]`;
      await click(selector); await click(selector);
      const commands = await evaluate('backend.messages.filter(command=>command.operation==="retry-image")');
      assert.equal(commands.length, 1);
      assert.deepEqual({ ...commands[0].parameters, operationId: 'id' }, { accountId: '12345', id: ids[1], operationId: 'id', version: 0, generation: 4 });
      assert.equal(await evaluate(`document.querySelector(${JSON.stringify(selector)}).disabled`), true);
      await evaluate('backend.respondRetry(true)');
      assert.equal(await evaluate('document.querySelector("#review-form input[name=date]").value'), '2026-11-08');
      assert.equal(await evaluate(`document.querySelector(${JSON.stringify(selector)})`), null);
      assert.match(await text('#toast'), /已加入图片处理队列/);
    });
    await t.test('外部、本地文件、其他账号、旧代次及带查询的图片源都不加载', async () => {
      for (const unsafe of ['https://external.test/image.png', 'file:///C:/secret.png', '//external.test/image.png', url(ids[0], 3), url(ids[0], 4, '999'), url(ids[0]) + '?path=other']) {
        const data = structuredClone(fixture); data.items[0].evidence[0].images = [{ ...images[0], url: unsafe }]; data.items[0].evidence[0].imageCount = 1;
        await push(data);
        assert.equal(await evaluate('document.querySelectorAll(".evidence-thumbnail,[data-action=preview-image]").length'), 0);
      }
      const data = structuredClone(fixture); delete data.items[0].evidence[0].images;
      await push(data);
      assert.match(await text('#detail-pane'), /尚有 6 张图片待下载原图/);
    });
    await t.test('备份部分缺图只说明缺失图片，包含原图的范围明确', async () => {
      await push(fixture, { backupResult: { accountId: '12345', path: 'C:/backup.zip', completedAt: '2026-10-28T12:30:00Z', isComplete: false, missingImageCount: 2 } });
      await click('[data-action=nav][data-page=settings]');
      const content = await text('#settings-panel');
      assert.match(content, /未包含 2 张原图；这些图片仅保留引用，已保存的原图随备份包含/);
      assert.match(content, /已保存的原图和识别结果会随备份导出/);
      assert.doesNotMatch(content, /未包含原图文件，仅保留图片引用/);
    });
    await t.test('纯图片尚无事项时可从设置查看失败并重试，等待回执期间按钮禁用', async () => {
      const data = { ...structuredClone(fixture), items: [], imageQueue: [{ ...structuredClone(fixture.items[0].evidence[0]), imageCount: 1, images: [structuredClone(images[1])] }] };
      await push(data); await click('[data-action=nav][data-page=settings]');
      assert.match(await text('#image-queue'), /文字识别失败，原图已保留/);
      const selector = `#image-queue [data-action=retry-image][data-image-id="${ids[1]}"]`;
      await click('#image-queue [data-action=preview-image]');
      assert.equal(await evaluate('document.querySelector("#image-preview-dialog").open'), true);
      await click('[data-action=close-image-preview]');
      await evaluate('backend.holdRetry=true'); await click(selector); await click(selector);
      assert.equal(await evaluate(`document.querySelector(${JSON.stringify(selector)}).disabled`), true);
      await evaluate('backend.respondRetry(false)');
      assert.equal(await evaluate(`document.querySelector(${JSON.stringify(selector)}).disabled`), false);
      await click(selector); await evaluate('backend.respondRetry(true)');
      assert.match(await text('#image-queue'), /原图已保存，等待本地识别/);
      assert.equal(await evaluate('document.querySelector("#image-queue [data-action=retry-image]")'), null);
    });
    await t.test('恢复和账号或代次变化立即关闭旧原图', async () => {
      await push(fixture); await click('[data-action=nav][data-page=pending]');
      await click(`[data-action=preview-image][data-image-id="${ids[0]}"]`);
      await push(fixture, { isRestoring: true });
      assert.equal(await evaluate('document.querySelector("#image-preview-dialog").open'), false);
      await push(fixture, { isRestoring: false });
      await click(`[data-action=preview-image][data-image-id="${ids[0]}"]`);
      const next = structuredClone(fixture); next.generation = 5; next.items[0].evidence[0].images.forEach(image => { if (image.url) image.url=url(image.id,5); });
      await push(next);
      assert.equal(await evaluate('document.querySelector("#image-preview-dialog").open'), false);
      await click(`[data-action=preview-image][data-image-id="${ids[0]}"]`);
      await push({ ...next, accountId: '67890' });
      assert.equal(await evaluate('document.querySelector("#image-preview-dialog").open'), false);
      assert.equal(await evaluate('document.querySelector("#image-preview-original").hasAttribute("src")'), false);
    });
    assert.deepEqual(errors, []);
  } finally {
    socket?.close(); browser.kill(); server.closeAllConnections(); await new Promise(resolve => server.close(resolve));
    await new Promise(resolve => { if (browser.exitCode !== null) resolve(); else { const timer=setTimeout(resolve,3000); browser.once('exit',()=>{clearTimeout(timer);resolve();}); } });
    if (path.dirname(path.resolve(profile)) === path.resolve(os.tmpdir()) && path.basename(profile).startsWith('messagepurify-image-qa-')) {
      try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 2, retryDelay: 100 }); } catch {}
    }
  }
});
