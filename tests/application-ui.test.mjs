import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import test from 'node:test';

const workspace = fileURLToPath(new URL('../', import.meta.url));
const pageUrl = pathToFileURL(path.join(workspace, 'src/MessagePurify.Windows/App/ui/index.html')).href;
const browserPath = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const evidence = [{ groupId: '100', groupName: '课程群', senderId: '200', sentAt: '2026-10-12T09:00:00+08:00', text: '请提交报告。\n' + '原消息正文 '.repeat(180), imageCount: 1 }];
const itemDefaults = { due: null, time: null, timeKind: 'deadline', priority: null, targetTaskId: null, targetTaskVersion: null, undoOperationId: null, undoExpiresAt: null, reviewReasons: [], suggestedDue: null, suggestedTime: null, action: 'submit', canAccept: false };
const fixture = {
  accountId: '12345', generatedAt: '2026-10-12T10:00:00+08:00',
  items: [
    { id: 'task1', kind: 'task', title: '实验报告', course: '物理', group: '课程群', state: 'active', due: '2026-12-20', time: '', timeKind: 'deadline', priority: 3, version: 1, unread: true, evidence },
    { id: 'start1', kind: 'task', title: '考试', course: '数学', group: '数学群', state: 'active', due: '2020-01-01', time: '', timeKind: 'start', priority: 0, version: 1, unread: false, evidence: [] },
    { id: 'review1', kind: 'review', title: '图片预习要求', course: '物理', group: '课程群', state: 'pending', due: null, time: null, timeKind: 'deadline', suggestedDue: '2026-12-21', suggestedTime: '18:00', version: 1, unread: true, canAccept: true, action: 'submit', reviewReasons: ['图片日期须确认'], evidence },
    { id: 'blocked', kind: 'review', title: '对象不清楚', course: '', group: '课程群', state: 'pending', due: null, version: 1, unread: false, canAccept: false, reviewReasons: ['适用课程尚未确认'], evidence: [] },
    { id: 'cancel1', kind: 'review', title: '取消实验报告', course: '物理', group: '课程群', state: 'pending', due: null, version: 1, targetTaskId: 'task1', targetTaskVersion: 1, canAccept: true, action: 'cancel', unread: false, evidence: [] },
    { id: 'remind1', kind: 'review', title: '实验报告催交', course: '物理', group: '课程群', state: 'pending', due: '2020-01-01', time: '09:00', version: 1, targetTaskId: 'task1', targetTaskVersion: 1, canAccept: true, action: 'remind', unread: false, evidence: [] },
    { id: 'notice1', kind: 'notice', title: '图书馆开放', course: '', group: '校园群', state: 'active', version: 1, unread: true, evidence: [] }
  ],
  reminders: [
    { taskId: 'task1', timeVersion: 1, planVersion: 1, preference: { mode: 'default', hours: null, days: null, clock: null }, needsReview: false, active: true, nodes: [{ id: 'task1-default', scheduledAt: '2026-12-19T12:00:00+08:00', state: 'pending' }] },
    { taskId: 'start1', timeVersion: 1, planVersion: 1, preference: { mode: 'default', hours: null, days: null, clock: null }, needsReview: false, active: false, nodes: [] }
  ],
  groups: [
    { groupId: '100', name: '课程群', suggestedCourse: '物理课程', course: null, excluded: false, pinned: true },
    { groupId: '101', name: '非置顶群', suggestedCourse: '非置顶课程', course: null, excluded: false, pinned: false }
  ]
};
fixture.items = fixture.items.map(item => ({ ...itemDefaults, ...item }));
const status = { transport: 'connected', qq: 'online', receiving: 'active', history: 'partial', error: null, updatedAt: '2026-10-12T10:00:00+08:00', historyFailures: [{ groupId: '100', reason: 'history_timeout' }] };
const mockScript = `(() => {
  const listeners = new Set();
  const clone = value => JSON.parse(JSON.stringify(value));
  const backend = window.backend = { snapshot: ${JSON.stringify(fixture)}, status: ${JSON.stringify(status)}, startupEnabled: true, messages: [], hold: true, held: [], failNext: null };
  backend.emit = data => listeners.forEach(listener => listener({data:clone(data)}));
  backend.push = () => backend.emit({type:'state',snapshot:backend.snapshot,status:backend.status,startupEnabled:backend.startupEnabled});
  backend.release = () => { backend.hold=false; for (const command of backend.held.splice(0)) respond(command); };
  function respond(command) {
    const parameters=command.parameters || {};
    if(command.operation==='set-appearance') { backend.appearance=clone(parameters);backend.emit({id:command.id,success:true});return; }
    if(command.operation==='set-autostart') { backend.startupEnabled=parameters.enabled;backend.emit({id:command.id,success:true,startupEnabled:backend.startupEnabled});return; }
    if (backend.failNext && command.operation !== 'snapshot' && command.operation !== 'mark-read') {
      const error=backend.failNext;backend.failNext=null;backend.emit({id:command.id,success:false,error,snapshot:backend.snapshot,status:backend.status});return;
    }
    const item=backend.snapshot?.items.find(item=>item.id===parameters.id);
    if(command.operation==='confirm-group') {
      const group=backend.snapshot.groups.find(group=>group.groupId===parameters.id);group.course=parameters.course || null;group.excluded=parameters.excluded;
    } else if(command.operation !== 'snapshot') {
      if(!item || item.version !== parameters.version) { backend.emit({id:command.id,success:false,error:'version_conflict',snapshot:backend.snapshot,status:backend.status});return; }
      if(command.operation==='mark-read') { item.unread=false;item.changeLabel=item.pendingChanges?.length ? '待确认变更' : null; }
      else {
        const transitions={'complete':'completed','undo-complete':'active','archive':'archived','restore':'active','ignore':'ignored','undo-ignore':'pending','reject':'rejected'};
        if(transitions[command.operation])item.state=transitions[command.operation];
        if(command.operation==='complete' || command.operation==='ignore') { item.undoOperationId=parameters.operationId;item.undoExpiresAt=new Date(Date.now()+3600000).toISOString(); }
        if(command.operation==='accept-time'){item.due=parameters.date;item.time=parameters.time || null;item.timeKind=parameters.timeKind;}
        if(command.operation==='edit-task'){item.title=parameters.title;item.course=parameters.course || null;item.due=parameters.date;item.time=parameters.time || null;item.timeKind=parameters.timeKind;item.priority=parameters.priority;}
        if(command.operation==='accept-review') {
          if(item.action==='cancel')backend.snapshot.items.find(target=>target.id===item.targetTaskId).state='cancelled';
          item.kind='task';item.state='active';item.due=parameters.date || null;item.time=parameters.time || null;item.timeKind=parameters.timeKind || 'deadline';
          backend.snapshot.reminders.push({taskId:item.id,timeVersion:1,planVersion:1,preference:{mode:'default',hours:null,days:null,clock:null},needsReview:false,active:true,nodes:[]});
        }
        if(command.operation==='convert-notice') {
          const taskId=item.targetTaskId || 'notice-task-'+item.id;
          item.state='converted';item.targetTaskId=taskId;item.targetTaskVersion=1;
          if(!backend.snapshot.items.some(candidate=>candidate.id===taskId)) backend.snapshot.items.push(Object.assign({}, item, {id:taskId,kind:'task',state:'active',due:parameters.date,time:parameters.time || null,timeKind:parameters.timeKind || 'deadline',version:1,targetTaskId:null,targetTaskVersion:null,unread:false}));
        } else item.version++;
      }
    }
    if(backend.snapshot)backend.snapshot.generatedAt=new Date().toISOString();
    backend.emit({id:command.id,success:true,snapshot:backend.snapshot,status:backend.status});
  }
  window.chrome = window.chrome || {};
  window.chrome.webview = { addEventListener:(type,listener)=>{if(type==='message')listeners.add(listener);},postMessage:command=>{
    backend.messages.push(clone(command));if(backend.hold){backend.held.push(command);return;}queueMicrotask(()=>respond(command));
  }};
})();`;

test('正式页面通过 WebView 桥显示与修改持久化事项', { timeout: 60000 }, async t => {
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'messagepurify-app-qa-'));
  const browser = spawn(browserPath, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] });
  let socket, stderr = '', counter = 0;
  const pending = new Map(), browserErrors = [];
  browser.stderr.on('data', data => stderr += data.toString());
  try {
    const activePortFile = path.join(profile, 'DevToolsActivePort');
    for (let i = 0; i < 120 && !fs.existsSync(activePortFile); i++) await delay(100);
    assert.ok(fs.existsSync(activePortFile), 'Chrome 未启动：' + stderr.slice(-1000));
    const port = fs.readFileSync(activePortFile, 'utf8').split('\n')[0];
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    socket = new WebSocket(targets.find(item => item.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    socket.addEventListener('message', event => {
      const message = JSON.parse(event.data);
      if (message.id) { const handlers = pending.get(message.id); pending.delete(message.id); message.error ? handlers?.reject(new Error(JSON.stringify(message.error))) : handlers?.resolve(message.result); }
      else if (message.method === 'Runtime.exceptionThrown') browserErrors.push(message.params.exceptionDetails.exception?.description || message.params.exceptionDetails.text);
    });
    const call = (method, parameters = {}) => new Promise((resolve, reject) => { const id = ++counter; pending.set(id, { resolve, reject }); socket.send(JSON.stringify({ id, method, params: parameters })); });
    const evaluate = async expression => { const result = await call('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }); if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text); return result.result.value; };
    const click = selector => evaluate(`document.querySelector(${JSON.stringify(selector)}).click()`);
    const change = (selector, value) => evaluate(`(()=>{const element=document.querySelector(${JSON.stringify(selector)});element.value=${JSON.stringify(value)};element.dispatchEvent(new Event('input',{bubbles:true}));element.dispatchEvent(new Event('change',{bubbles:true}));})()`);
    const nav = page => click(`[data-action="nav"][data-page="${page}"]`);
    const select = id => click(`.task-row[data-id="${id}"]`);
    const push = snapshot => evaluate(`backend.snapshot=${JSON.stringify({ ...snapshot, generatedAt: new Date().toISOString() })};backend.push()`);
    const body = () => evaluate('document.body.textContent');
    const capture = async name => {
      await evaluate('document.querySelector("#toast").hidden=true;if(!document.querySelector("#settings-panel").hidden)document.querySelector("#desktop-status")?.scrollIntoView({block:"center"})');
      await delay(350);
      await evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
      const result = await call('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const directory = path.join(workspace, '.tools/desktop-validation'); fs.mkdirSync(directory, { recursive: true });
      fs.writeFileSync(path.join(directory, name), Buffer.from(result.data, 'base64'));
    };
    const waitFor = async expression => { for (let i = 0; i < 50; i++) { if (await evaluate(expression)) return; await delay(50); } assert.fail('页面条件未满足：' + expression); };
    await call('Runtime.enable'); await call('Page.enable');
    await call('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'dark' }, { name: 'prefers-reduced-motion', value: 'no-preference' }] });
    await call('Emulation.setTimezoneOverride', { timezoneId: 'America/New_York' });
    await call('Emulation.setDeviceMetricsOverride', { width: 1200, height: 800, deviceScaleFactor: 1, mobile: false });
    await call('Page.addScriptToEvaluateOnNewDocument', { source: mockScript });
    await call('Page.navigate', { url: pageUrl });
    await waitFor('document.readyState === "complete"');

    await t.test('初次请求真实快照与卡片外观，等待时无样例或虚假在线状态', async () => {
      assert.ok(await evaluate('!!document.querySelector("#app")'), '正式页面尚未实现');
      assert.match(await body(), /正在读取|等待连接/);
      assert.doesNotMatch(await body(), /QQ 在线|实验报告|Demo/);
      assert.equal(await evaluate('document.querySelectorAll(".task-row").length'), 0);
      assert.deepEqual(await evaluate('backend.messages[0]'), { id: await evaluate('backend.messages[0].id'), operation: 'snapshot', parameters: {} });
      assert.deepEqual(await evaluate('backend.messages.map(message=>message.operation)'), ['snapshot', 'set-appearance']);
      assert.deepEqual(await evaluate('backend.messages[1].parameters'), { theme: 'dark', reducedMotion: false });
      assert.equal(await evaluate('document.documentElement.dataset.theme'), 'dark');
      assert.equal(await evaluate('getComputedStyle(document.body).backgroundColor'), 'rgb(0, 0, 0)');
    });
    await t.test('离线且尚无账号快照时继续显示等待，不生成空账号事项', async () => {
      await evaluate('backend.snapshot=null;backend.status={...backend.status,transport:"disconnected",qq:"offline",receiving:"paused"};backend.release()');
      assert.equal(await evaluate('document.querySelectorAll(".task-row").length'), 0);
      assert.match(await body(), /连接已断开/); assert.doesNotMatch(await body(), /QQ 在线|实验报告/);
      assert.match(await body(), /QQ.*离线/);
      await evaluate('backend.emit({type:"open-item",accountId:"12345",id:"cancel1"})');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="mark-read").length'), 0);
      await evaluate(`backend.snapshot=${JSON.stringify(fixture)};backend.status=${JSON.stringify(status)};backend.push()`);
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'), 'cancel1', '快照之后兑现桌面打开请求');
    });
    await waitFor('!!document.querySelector(".task-row")');
    await t.test('常驻同步区分空闲、读取、未补全和连接故障', async () => {
      await evaluate(`backend.status={...${JSON.stringify(status)},scheduled:true,receiving:"paused",history:"complete",historyFailures:[]};backend.push()`);
      assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), '等待下次同步');
      assert.equal(await evaluate('document.querySelector("#history-status").textContent'), '等待下次同步');
      assert.equal(await evaluate('document.querySelector("#host-error").hidden'), true);
      await evaluate('backend.status.history="pending";backend.push()');
      assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), '等待下次同步');
      await evaluate('backend.status.history="reading";backend.status.receiving="active";backend.push()');
      assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), '正在同步置顶群消息');
      await evaluate(`backend.status.history="partial";backend.status.receiving="paused";backend.status.historyFailures=${JSON.stringify(status.historyFailures)};backend.push()`);
      assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), '同步未补全');
      await nav('settings');
      assert.match(await evaluate('document.querySelector("#settings-panel").textContent'), /课程群.*历史读取超时/s);
      for (const fields of [
        { transport: 'connecting', qq: 'unknown', error: null },
        { transport: 'disconnected', qq: 'unknown', error: 'connection-failed' },
        { transport: 'connected', qq: 'offline', error: 'qq-offline' },
        { transport: 'connected', qq: 'online', error: 'pinned-state-unavailable' }
      ]) {
        await evaluate(`backend.status={...backend.status,...${JSON.stringify(fields)},history:"complete",historyFailures:[]};backend.push()`);
        assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), '同步已暂停');
        assert.doesNotMatch(await evaluate('document.querySelector("#settings-panel").textContent'), /等待下次同步/);
      }
      await evaluate(`backend.status=${JSON.stringify(status)};backend.push()`); await nav('pending');
    });
    await t.test('直接启动的连接配置错误显示可读原因并保留本地事项', async () => {
      for (const [error, message] of [
        ['napcat-configuration-invalid', 'QQ 连接配置无法识别，请核对本地接口配置。'],
        ['napcat-configuration-unavailable', 'QQ 连接配置暂时无法读取，连接已暂停，请检查文件访问权限。'],
        ['napcat-configuration-ambiguous', '检测到多个启用的 QQ 连接配置，连接已暂停，请核对当前接口。']
      ]) {
        await evaluate(`backend.status={...backend.status,transport:"disconnected",qq:"unknown",receiving:"paused",error:${JSON.stringify(error)}};backend.push()`);
        assert.equal(await evaluate('document.querySelector("#host-error").textContent'), message);
        assert.equal(await evaluate('document.querySelector("#host-error").hidden'), false);
        assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), '消息接收已暂停');
        assert.ok(await evaluate('document.querySelectorAll(".task-row").length') > 0);
      }
      await evaluate(`backend.status=${JSON.stringify(status)};backend.push()`);
    });
    await t.test('游戏期间明确显示延后同步且保留本地事项', async () => {
      await evaluate('backend.status={...backend.status,scheduled:true,receiving:"paused",error:"game-active"};backend.push()');
      assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), '游戏中，同步已延后');
      assert.equal(await evaluate('document.querySelector("#history-status").textContent'), '游戏中，同步已延后');
      assert.match(await evaluate('document.querySelector("#host-error").textContent'), /游戏运行中.*退出游戏后.*一次/);
      assert.ok(await evaluate('document.querySelectorAll(".task-row").length') > 0);
      await evaluate('backend.status={...backend.status,error:"game-catalog-unavailable"};backend.push()');
      assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), '检测名单未就绪，同步已暂停');
      assert.match(await evaluate('document.querySelector("#host-error").textContent'), /游戏名单.*暂停/);
      await evaluate(`backend.status=${JSON.stringify(status)};backend.push()`);
    });
    await t.test('同步时间使用北京时间、注明本次运行且状态推送保留课程草稿焦点', async () => {
      await evaluate(`backend.status={...${JSON.stringify(status)},scheduled:true,receiving:"paused",history:"complete",historyFailures:[],lastSuccessfulSyncAt:null,nextScheduledSyncAt:"2026-10-13T00:00:00Z"};backend.push()`);
      await nav('settings');
      assert.equal(await evaluate('document.querySelector("#last-successful-sync").textContent'), '本次运行尚无成功同步');
      assert.equal(await evaluate('document.querySelector("#next-scheduled-sync").textContent'), '2026-10-13 08:00（北京时间）');
      assert.match(await evaluate('document.querySelector("#settings-panel").textContent'), /上次成功同步（本次运行）/);
      await change('[data-group-id="100"] input[name=course]', '尚未提交的课程');
      await evaluate('document.querySelector("[data-group-id=\\"100\\"] input").focus();document.activeElement.setSelectionRange(2,5);backend.status.lastSuccessfulSyncAt="2026-10-12T00:05:00Z";backend.status.nextScheduledSyncAt="2026-10-12T04:00:00Z";backend.push()');
      assert.equal(await evaluate('document.querySelector("#last-successful-sync").textContent'), '2026-10-12 08:05（北京时间）');
      assert.equal(await evaluate('document.querySelector("#next-scheduled-sync").textContent'), '2026-10-12 12:00（北京时间）');
      assert.equal(await evaluate('document.activeElement.name'), 'course');
      assert.equal(await evaluate('document.activeElement.value'), '尚未提交的课程');
      assert.deepEqual(await evaluate('[document.activeElement.selectionStart,document.activeElement.selectionEnd]'), [2, 5]);
      await evaluate('backend.emit({type:"state",status:{...backend.status,updatedAt:"2000-01-01T00:00:00Z",lastSuccessfulSyncAt:"2000-01-01T00:00:00Z",nextScheduledSyncAt:null,transport:"disconnected"}})');
      assert.equal(await evaluate('document.querySelector("#last-successful-sync").textContent'), '2026-10-12 08:05（北京时间）');
      assert.equal(await evaluate('document.querySelector("#next-scheduled-sync").textContent'), '2026-10-12 12:00（北京时间）');
      assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), '等待下次同步');
      await change('[data-group-id="100"] input[name=course]', '物理课程');
      await evaluate(`backend.status=${JSON.stringify(status)};backend.push()`); await nav('pending');
    });
    await t.test('首次有待确认默认进入待确认，来源正文安全且图片不发请求', async () => {
      assert.equal(await evaluate('document.querySelector("[aria-current=page]").dataset.page'), 'pending');
      await select('review1');
      assert.match(await body(), /原消息正文/);
      assert.match(await body(), /1 张图片/);
      assert.equal(await evaluate('document.querySelectorAll("img,iframe").length'), 0);
      assert.equal(await evaluate('document.querySelector("[name=date]").value'), '');
      assert.equal(await evaluate('document.querySelector("[name=time]").value'), '');
      assert.match(await body(), /历史.*部分|部分.*历史/);
    });
    await t.test('待确认日期为空时不能提交，显式填入建议只改变草稿', async () => {
      const before = await evaluate('backend.messages.filter(message=>message.operation==="accept-review").length');
      await click('#pending-confirm-button');
      assert.match(await evaluate('document.querySelector("#detail-error").textContent'), /日期/);
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="accept-review").length'), before);
      await click('[data-action="use-suggestion"]');
      assert.equal(await evaluate('document.querySelector("[name=date]").value'), '2026-12-21');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="accept-review").length'), before);
      await change('[name=date]', '2026-12-25'); await change('[name=time]', '');
    });
    await t.test('宿主报错不丢草稿，推送保留焦点与未提交日期', async () => {
      await evaluate('backend.failNext="version_conflict"'); await click('#pending-confirm-button');
      assert.match(await evaluate('document.querySelector("#detail-error").textContent'), /变化|更新|版本/);
      assert.equal(await evaluate('document.querySelector("[name=date]").value'), '2026-12-25');
      await evaluate('document.querySelector("[name=date]").focus();backend.snapshot.items.find(item=>item.id==="review1").version++;backend.push()');
      assert.equal(await evaluate('document.activeElement.name'), 'date');
      assert.equal(await evaluate('document.querySelector("[name=date]").value'), '2026-12-25');
      await evaluate('document.querySelector("#review-form").requestSubmit()');
      const command = await evaluate('backend.messages.filter(message=>message.operation==="accept-review").at(-1)');
      assert.equal(command.parameters.version, 2); assert.equal(command.parameters.date, '2026-12-25');
      assert.equal(command.parameters.time, ''); assert.equal(command.parameters.accountId, '12345');
      assert.match(command.parameters.operationId, /^[\da-f-]{36}$/i);
    });
    await t.test('迟到旧快照不会覆盖当前内容或日期草稿', async () => {
      await nav('pending'); await select('cancel1');
      const before = await evaluate('document.querySelector(".detail-title").textContent');
      await evaluate('(()=>{const old=structuredClone(backend.snapshot);old.generatedAt="2000-01-01T00:00:00Z";old.items.find(item=>item.id==="cancel1").title="旧标题";backend.emit({type:"state",snapshot:old,status:{...backend.status,updatedAt:"2000-01-01T00:00:00Z",transport:"disconnected"}});})()');
      assert.equal(await evaluate('document.querySelector(".detail-title").textContent'), before);
      assert.match(await evaluate('document.querySelector("#connection-status").textContent'), /QQ 在线/);
    });
    await t.test('不可接受的待确认显示解释；关联取消不要求日期', async () => {
      await nav('pending'); await select('blocked');
      assert.equal(await evaluate('document.querySelector("#pending-confirm-button").disabled'), true);
      assert.match(await body(), /适用课程尚未确认/);
      await select('cancel1'); await click('#pending-confirm-button');
      const command = await evaluate('backend.messages.filter(message=>message.operation==="accept-review").at(-1)');
      assert.equal(command.parameters.id, 'cancel1'); assert.equal(command.parameters.targetTaskVersion, 1);
      assert.equal(command.parameters.date, undefined);
    });
    await push(fixture);
    await t.test('关联催交不默认采用旧日期，直接确认保留任务当前时间', async () => {
      await nav('pending'); await select('remind1');
      assert.equal(await evaluate('document.querySelector("[name=date]").value'), '');
      assert.equal(await evaluate('document.querySelector("[name=time]").value'), '');
      await evaluate('window.previousReviewForm=document.querySelector("#review-form");backend.snapshot.items.find(item=>item.id==="remind1").targetTaskVersion=2;backend.snapshot.items.find(item=>item.id==="task1").version=2;backend.snapshot.generatedAt=new Date().toISOString();backend.push()');
      assert.ok(await evaluate('window.previousReviewForm===document.querySelector("#review-form")'));
      await click('#pending-confirm-button');
      const command = await evaluate('backend.messages.filter(message=>message.operation==="accept-review").at(-1)');
      assert.equal(command.parameters.id, 'remind1'); assert.equal(command.parameters.targetTaskVersion, 2);
      assert.equal(command.parameters.date, undefined); assert.equal(command.parameters.time, undefined); assert.equal(command.parameters.timeKind, undefined);
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="task1").due'), '2026-12-20');
    });
    await push(fixture);
    await t.test('完成、撤销、归档和恢复全部通过版本化宿主命令', async () => {
      await nav('tasks'); await select('task1'); await click('[data-action="complete"]');
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="task1").state'), 'completed');
      await nav('completed'); await select('task1'); await click('[data-action="undo-complete"]');
      const undo = await evaluate('backend.messages.filter(message=>message.operation==="undo-complete").at(-1)');
      assert.ok(undo.parameters.undoOperationId); assert.equal(undo.parameters.version, 2);
      await nav('tasks'); await select('task1'); await click('[data-action="archive"]');
      await change('#state-filter', 'archived'); await select('task1'); await click('[data-action="restore"]');
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="task1").state'), 'active');
    });
    await t.test('任务日期编辑保留日期精度，开始时间过后标签正确', async () => {
      await change('#state-filter', 'active'); await select('task1');
      await change('[name=date]', '2026-12-30'); await change('[name=time]', '');
      await click('[data-action="edit-task"]');
      const command = await evaluate('backend.messages.filter(message=>message.operation==="edit-task").at(-1)');
      assert.equal(command.parameters.date, '2026-12-30'); assert.equal(command.parameters.time, '');
      await select('start1'); assert.match(await body(), /开始时间已过/);
      assert.doesNotMatch(await evaluate('document.querySelector("#detail-pane").textContent'), /已逾期/);
    });
    await t.test('任务完整编辑六字段通过版本化命令保存，课程选择复用筛选选项', async () => {
      const editing = structuredClone(fixture); editing.items.push({ ...editing.items[0], id: 'edit1', unread: false });
      await push(editing); await nav('tasks'); await select('edit1');
      assert.deepEqual(await evaluate('[...document.querySelector("#time-form").elements].filter(element=>element.name).map(element=>element.name)'), ['title', 'course', 'date', 'time', 'timeKind', 'priority']);
      assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'), '实验报告');
      assert.equal(await evaluate('document.querySelector("#time-form [name=course]").value'), '物理');
      assert.equal(await evaluate('document.querySelector("#time-form [name=priority]").value'), '3');
      assert.deepEqual(await evaluate('[...document.querySelectorAll("#task-course-options option")].map(option=>option.value)'), await evaluate('[...document.querySelector("#course-filter").options].map(option=>option.value).filter(Boolean)'));
      await change('#time-form [name=title]', '  实验报告修订版  '); await change('#time-form [name=course]', '数学');
      await change('#time-form [name=date]', '2026-12-31'); await change('#time-form [name=time]', '18:30');
      await change('#time-form [name=timeKind]', 'start'); await change('#time-form [name=priority]', '1');
      await click('[data-action="edit-task"]');
      const command = await evaluate('backend.messages.filter(message=>message.operation==="edit-task").at(-1)');
      assert.deepEqual(command.parameters, { accountId: '12345', id: 'edit1', operationId: command.parameters.operationId, version: 1, title: '实验报告修订版', course: '数学', date: '2026-12-31', time: '18:30', timeKind: 'start', priority: 1 });
      assert.equal(await evaluate('document.querySelector(".detail-title").textContent'), '实验报告修订版');
      assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'), '实验报告修订版');
      await evaluate('document.querySelector("#time-form").scrollIntoView({block:"start"})');
      await capture('ui-full-edit-task-dark.png');
    });
    await t.test('完整编辑拒绝空标题、超长标题、缺日期及无效优先级，可清除课程和钟点', async () => {
      await select('edit1');
      const before = await evaluate('backend.messages.filter(message=>message.operation==="edit-task").length');
      for (const [name, value, message] of [['title', '   ', /标题/], ['title', '字'.repeat(257), /256/], ['date', '', /正式日期/], ['priority', '', /优先级/]]) {
        await change('#time-form [name=title]', '实验报告修订版'); await change('#time-form [name=date]', '2026-12-31'); await change('#time-form [name=priority]', '1');
        await change(`#time-form [name=${name}]`, value); await click('[data-action="edit-task"]');
        assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="edit-task").length'), before);
        assert.match(await evaluate('document.querySelector("#detail-error").textContent'), message);
      }
      await change('#time-form [name=priority]', '2'); await change('#time-form [name=course]', ''); await change('#time-form [name=time]', '');
      await change('#course-filter', '数学'); await click('[data-action="edit-task"]');
      const command = await evaluate('backend.messages.filter(message=>message.operation==="edit-task").at(-1)');
      assert.equal(command.parameters.course, ''); assert.equal(command.parameters.time, '');
      assert.equal(await evaluate('document.querySelector("#course-filter").value'), '数学');
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="edit1").course'), null);
      await change('#course-filter', '');
    });
    await t.test('并发任务更新保留全部编辑草稿，旧版本拒绝后刷新核对才能保存新版', async () => {
      await select('edit1');
      const fields = { title: '我的标题草稿', course: '新课程', date: '2026-12-29', time: '19:00', timeKind: 'deadline', priority: '0' };
      for (const [name, value] of Object.entries(fields)) await change(`#time-form [name=${name}]`, value);
      const oldVersion = await evaluate('backend.snapshot.items.find(item=>item.id==="edit1").version');
      await evaluate('Object.assign(backend.snapshot.items.find(item=>item.id==="edit1"),{title:"刚更新的任务标题",course:"其他课程",due:"2027-01-01",time:"10:00",timeKind:"start",priority:3,version:99});backend.snapshot.generatedAt=new Date().toISOString();backend.push()');
      assert.equal(await evaluate('document.querySelector(".detail-title").textContent'), '刚更新的任务标题');
      for (const [name, value] of Object.entries(fields)) assert.equal(await evaluate(`document.querySelector('#time-form [name=${name}]').value`), value);
      await click('[data-action="edit-task"]');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="edit-task").at(-1).parameters.version'), oldVersion);
      assert.match(await evaluate('document.querySelector("#detail-error").textContent'), /草稿.*保留.*刷新.*核对/);
      for (const [name, value] of Object.entries(fields)) assert.equal(await evaluate(`document.querySelector('#time-form [name=${name}]').value`), value);
      await click('[data-action="refresh-edit"]');
      for (const [name, value] of Object.entries(fields)) assert.equal(await evaluate(`document.querySelector('#time-form [name=${name}]').value`), value);
      await click('[data-action="edit-task"]');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="edit-task").at(-1).parameters.version'), 99);
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="edit1").title'), fields.title);
      assert.equal(await evaluate('document.querySelector("#course-filter").value'), '');
    });
    await t.test('任务刷新响应延迟时只核对发起任务，切换后的另一任务仍保留旧版本保护', async () => {
      const editing = structuredClone(fixture);
      editing.items.push({ ...editing.items[0], id: 'edit-a', unread: false }, { ...editing.items[0], id: 'edit-b', unread: false });
      await push(editing); await nav('tasks'); await select('edit-a'); await change('#time-form [name=title]', 'A 的草稿');
      await select('edit-b'); await change('#time-form [name=title]', 'B 的草稿');
      await evaluate('for(const id of ["edit-a","edit-b"])Object.assign(backend.snapshot.items.find(item=>item.id===id),{title:id+"的新标题",version:2});backend.snapshot.generatedAt=new Date().toISOString();backend.push()');
      await select('edit-a'); await click('[data-action="edit-task"]');
      assert.match(await evaluate('document.querySelector("#detail-error").textContent'), /草稿.*保留/);
      await evaluate('backend.hold=true'); await click('[data-action="refresh-edit"]');
      await select('edit-b'); await evaluate('backend.release()'); await click('[data-action="edit-task"]');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="edit-task" && message.parameters.id==="edit-b").at(-1).parameters.version'), 1);
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="edit-b").title'), 'edit-b的新标题');
      await select('edit-a');
      assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'), 'A 的草稿');
      await click('[data-action="edit-task"]');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="edit-task" && message.parameters.id==="edit-a").at(-1).parameters.version'), 2);
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="edit-a").title'), 'A 的草稿');
    });
    await t.test('另一个任务的迟到刷新响应保留当前任务的冲突提示和全部草稿', async () => {
      await select('edit-a'); await evaluate('backend.hold=true'); await click('[data-action="refresh"]');
      await select('edit-b'); await click('[data-action="edit-task"]');
      await evaluate('(()=>{const command=backend.held.find(command=>command.operation==="edit-task");backend.held=backend.held.filter(candidate=>candidate!==command);backend.emit({id:command.id,success:false,error:"version_conflict",snapshot:backend.snapshot,status:backend.status});})()');
      const error = await evaluate('document.querySelector("#detail-error").textContent');
      assert.match(error, /草稿.*保留.*刷新.*核对/);
      await evaluate('backend.release()');
      assert.equal(await evaluate('document.querySelector("#detail-error").textContent'), error);
      assert.equal(await evaluate('document.querySelector("#detail-error").hidden'), false);
      assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'), 'B 的草稿');
      await click('[data-action="edit-task"]');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="edit-task" && message.parameters.id==="edit-b").at(-1).parameters.version'), 1);
    });
    await push(fixture);
    await t.test('正式详情只保留日期与卡片渠道，不显示时钟提醒设置或旧待确认提示', async () => {
      const legacy = structuredClone(fixture);
      Object.assign(legacy.reminders[0], { preference: { mode: 'hours', hours: 1, days: null, clock: null }, needsReview: true });
      await push(legacy); await nav('tasks'); await select('task1');
      assert.equal(await evaluate('document.querySelectorAll("#reminder-form,#reminder-plan,#reminder-review,[name=reminderMode],[data-action=set-reminder]").length'), 0);
      assert.doesNotMatch(await evaluate('document.querySelector("#detail-pane").textContent'), /提醒设置|系统提醒尚未开启|原自定义提醒不再适用|24 小时|12:00/);
      assert.match(await evaluate('document.querySelector(".deadline-box").textContent'), /2026-12-20.*仅有日期/);
      assert.equal(await evaluate('document.querySelector("#time-form [name=date]").value'), '2026-12-30', '快照刷新保留已有日期草稿');
      assert.equal(await evaluate('document.querySelector("#time-form [name=time]").value'), '');
      await capture('ui-desktop-task-dark.png');
      await nav('pending'); await select('review1');
      assert.equal(await evaluate('!!document.querySelector("#review-form")'), true);
      assert.equal(await evaluate('!!document.querySelector("#reminder-form")'), false);
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="set-reminder").length'), 0);
    });
    await t.test('设置说明两张桌面卡片静音渠道，无安静时段或系统通知待接入入口', async () => {
      await nav('settings');
      assert.match(await body(), /通过桌面卡片提醒，不播放声音或发送系统通知/);
      assert.doesNotMatch(await body(), /安静时段|系统提醒尚未开启|任务声音提醒|提醒时间|提前小时|通知中心/);
      assert.match(await evaluate('document.querySelector("#desktop-status").textContent'), /等待挂载/);
      await evaluate('backend.emit({type:"state",desktop:{state:"attached",error:null}})');
      assert.match(await evaluate('document.querySelector("#desktop-status").textContent'), /两张.*已挂载/);
      await evaluate('backend.emit({type:"state",desktop:{state:"unavailable",error:"desktop-unavailable"}})');
      assert.match(await evaluate('document.querySelector("#desktop-status").textContent'), /暂不可用.*主界面.*查看/);
      assert.doesNotMatch(await body(), /desktop-unavailable/);
      await capture('ui-desktop-settings-unavailable.png');
      await evaluate('backend.emit({type:"state",desktop:{state:"attached",error:null}})');
    });
    await t.test('设置可关闭并重新开启 Windows 登录自动启动', async () => {
      assert.equal(await evaluate('document.querySelector("#autostart-setting").value'), 'on');
      await change('#autostart-setting', 'off');
      assert.deepEqual(await evaluate('backend.messages.filter(message=>message.operation==="set-autostart").at(-1).parameters'), { enabled: false });
      assert.equal(await evaluate('document.querySelector("#autostart-setting").value'), 'off');
      await change('#autostart-setting', 'on');
      assert.deepEqual(await evaluate('backend.messages.filter(message=>message.operation==="set-autostart").at(-1).parameters'), { enabled: true });
      assert.equal(await evaluate('document.querySelector("#autostart-setting").value'), 'on');
    });
    await t.test('外观桥使用有效主题与布尔运动偏好，用户选择和系统变化即时同步', async () => {
      const appearance = () => evaluate('backend.messages.filter(message=>message.operation==="set-appearance").at(-1)');
      await change('#theme-setting', 'light'); await change('#motion-setting', 'reduce');
      let command = await appearance();
      assert.deepEqual(command.parameters, { theme: 'light', reducedMotion: true });
      assert.match(command.id, /^[\da-f-]{36}$/i);
      assert.deepEqual(Object.keys(command).sort(), ['id', 'operation', 'parameters']);
      await change('#theme-setting', 'system'); await change('#motion-setting', 'system');
      await call('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'light' }, { name: 'prefers-reduced-motion', value: 'reduce' }] });
      await waitFor('backend.appearance?.theme==="light" && backend.appearance?.reducedMotion===true');
      assert.equal(await evaluate('document.documentElement.dataset.theme'), 'light');
      await call('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'dark' }, { name: 'prefers-reduced-motion', value: 'no-preference' }] });
      await waitFor('backend.appearance?.theme==="dark" && backend.appearance?.reducedMotion===false');
      await capture('ui-desktop-settings-dark.png');
    });
    await t.test('外观独立应答不当作事项保存，不清草稿、焦点或既有错误', async () => {
      await evaluate('backend.hold=true'); await change('#theme-setting', 'light');
      await nav('tasks'); await select('task1'); await change('#time-form [name=date]', '2026-12-28');
      await evaluate('document.querySelector("#time-form [name=date]").focus();document.querySelector("#toast").hidden=true;backend.release()');
      assert.equal(await evaluate('document.querySelector("#time-form [name=date]").value'), '2026-12-28');
      assert.equal(await evaluate('document.activeElement.name'), 'date');
      assert.equal(await evaluate('document.querySelector("#toast").hidden'), true);
      assert.equal(await evaluate('backend.snapshot.items[0].due'), '2026-12-20');
      await evaluate('backend.failNext="version_conflict"'); await click('[data-action="edit-task"]');
      const error = await evaluate('document.querySelector("#detail-error").textContent');
      const before = await evaluate('backend.messages.filter(message=>message.operation==="set-appearance").length');
      await call('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'light' }, { name: 'prefers-reduced-motion', value: 'no-preference' }] });
      await waitFor(`backend.messages.filter(message=>message.operation==="set-appearance").length>${before}`);
      assert.equal(await evaluate('document.querySelector("#time-form [name=date]").value'), '2026-12-28');
      assert.match(error, /变化|版本/);
      assert.equal(await evaluate('document.querySelector("#detail-error").textContent'), error);
    });
    await t.test('桌面打开同账号事项清除筛选、定位任务与待确认并保存已读，保留原草稿', async () => {
      const opened = structuredClone(fixture); opened.items[0].version = 40; opened.items[2].version = 40;
      await push(opened); await nav('pending'); await select('review1'); await change('#review-form [name=date]', '2026-12-29');
      await nav('tasks'); await change('#course-filter', '数学'); await change('#search', '不存在'); await change('#state-filter', 'archived');
      await evaluate('backend.emit({type:"open-item",accountId:"12345",id:"task1"})');
      assert.equal(await evaluate('document.querySelector("[aria-current=page]").dataset.page'), 'tasks');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'), 'task1');
      assert.equal(await evaluate('document.querySelector("#search").value'), '');
      assert.equal(await evaluate('document.querySelector("#course-filter").value'), '');
      assert.equal(await evaluate('document.querySelector("#state-filter").value'), 'active');
      const taskRead = await evaluate('backend.messages.filter(message=>message.operation==="mark-read" && message.parameters.id==="task1" && message.parameters.version===40)');
      assert.equal(taskRead.length, 1); assert.equal(taskRead[0].parameters.accountId, '12345');
      await evaluate('backend.emit({type:"open-item",accountId:"12345",id:"review1"})');
      assert.equal(await evaluate('document.querySelector("[aria-current=page]").dataset.page'), 'pending');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'), 'review1');
      assert.equal(await evaluate('document.querySelector("#review-form [name=date]").value'), '2026-12-29');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="mark-read" && message.parameters.id==="review1" && message.parameters.version===40).length'), 1);
      await capture('ui-desktop-open-pending.png');
      await evaluate('backend.snapshot.items[0].state="completed";backend.snapshot.generatedAt=new Date().toISOString();backend.push();backend.emit({type:"open-item",accountId:"12345",id:"task1"})');
      assert.equal(await evaluate('document.querySelector("[aria-current=page]").dataset.page'), 'completed');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'), 'task1');
    });
    await t.test('桌面打开错误账号、未知事项或无效标识只提示中文，不查询其他账号或丢草稿', async () => {
      await nav('pending'); await select('review1');
      const before = await evaluate('backend.messages.length');
      await evaluate('backend.emit({type:"open-item",accountId:"other-account",id:"review1"})');
      assert.match(await evaluate('document.querySelector("#host-error").textContent'), /账号.*不一致|其他.*账号/);
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'), 'review1');
      assert.equal(await evaluate('backend.messages.length'), before);
      await evaluate('backend.emit({type:"open-item",accountId:"12345",id:"missing"})');
      assert.match(await evaluate('document.querySelector("#host-error").textContent'), /不存在|未找到|无法找到/);
      await evaluate('backend.emit({type:"open-item",accountId:"12345",id:7})');
      assert.match(await evaluate('document.querySelector("#host-error").textContent'), /无效|无法打开/);
      assert.equal(await evaluate('document.querySelector("#review-form [name=date]").value'), '2026-12-29');
      assert.equal(await evaluate('backend.messages.length'), before);
      await evaluate('backend.emit({type:"open-item",accountId:"12345",id:"review1"})');
      assert.equal(await evaluate('document.querySelector("#host-error").hidden'), true);
    });
    await push(fixture);
    await t.test('系统时区不同时仍按北京时间判断精确截止', async () => {
      await nav('tasks');
      const instant = new Date(Date.now() - 3600000);
      const due = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit' }).format(instant);
      const time = new Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Shanghai', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(instant);
      await evaluate(`backend.snapshot.items.push({...${JSON.stringify(itemDefaults)},id:'timezone1',kind:'task',title:'北京时间截止',state:'active',course:'',group:'课程群',due:${JSON.stringify(due)},time:${JSON.stringify(time)},timeKind:'deadline',priority:0,version:1,unread:false,evidence:[]});backend.snapshot.reminders.push({taskId:'timezone1',timeVersion:1,planVersion:1,preference:{mode:'default',hours:null,days:null,clock:null},needsReview:false,active:false,nodes:[]});backend.snapshot.generatedAt=new Date().toISOString();backend.push()`);
      await select('timezone1');
      assert.equal(await evaluate('document.querySelector(".task-row[data-id=timezone1]").classList.contains("overdue")'), true);
      assert.match(await evaluate('document.querySelector("#detail-pane").textContent'), /已逾期/);
    });
    await t.test('忽略与撤销、拒绝建议有真实操作', async () => {
      await nav('pending'); await select('review1'); await click('[data-action="ignore"]');
      await change('#state-filter', 'ignored'); await select('review1'); await click('[data-action="undo-ignore"]');
      await change('#state-filter', 'pending'); await select('blocked'); await click('[data-action="reject"]');
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="blocked").state'), 'rejected');
    });
    await push(fixture);
    await t.test('课程设置使用真实置顶群并确认或排除，保留原待确认', async () => {
      await nav('settings'); assert.match(await body(), /物理课程/); assert.doesNotMatch(await body(), /非置顶课程/);
      await change('[data-group-id="100"] input[name=course]', '物理实验');
      await evaluate('backend.failNext="version_conflict"'); await click('[data-group-id="100"] [data-action="confirm-group"]');
      assert.match(await evaluate('document.querySelector("#host-error").textContent'), /变化/);
      assert.equal(await evaluate(`document.querySelector('[data-group-id="100"] input').value`), '物理实验');
      await evaluate(`document.querySelector('[data-group-id="100"] input').focus();backend.status.updatedAt=new Date().toISOString();backend.push()`);
      assert.equal(await evaluate('document.activeElement.name'), 'course');
      await click('[data-group-id="100"] [data-action="confirm-group"]');
      let command = await evaluate('backend.messages.filter(message=>message.operation==="confirm-group").at(-1)');
      assert.equal(command.parameters.course, '物理实验'); assert.equal(command.parameters.excluded, false);
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="review1").state'), 'pending');
      assert.match(await body(), /已有待确认.*核对|已有.*仍需.*确认/);
      await click('[data-group-id="100"] [data-action="exclude-group"]');
      command = await evaluate('backend.messages.filter(message=>message.operation==="confirm-group").at(-1)');
      assert.equal(command.parameters.excluded, true);
    });
    await push(fixture);
    await t.test('普通通知可补正式日期转为任务并保留通知入口', async () => {
      await nav('notices'); await select('notice1');
      assert.equal(await evaluate('!!document.querySelector("#notice-form")'), true);
      assert.equal(await evaluate('document.querySelector("#notice-form [name=date]").required'), true);
      const before = await evaluate('backend.messages.filter(message=>message.operation==="convert-notice").length');
      await click('[data-action="convert-notice"]');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="convert-notice").length'), before);
      assert.match(await evaluate('document.querySelector("#detail-error").textContent'), /正式日期/);
      await change('#notice-form [name=date]', '2026-12-30');
      await click('[data-action="convert-notice"]');
      await waitFor('backend.snapshot.items.find(item=>item.id==="notice1").state==="converted"');
      const command = await evaluate('backend.messages.filter(message=>message.operation==="convert-notice").at(-1)');
      assert.equal(command.parameters.id, 'notice1'); assert.equal(command.parameters.date, '2026-12-30');
      assert.equal(command.parameters.time, ''); assert.equal(command.parameters.timeKind, 'deadline');
      assert.equal(await evaluate('document.querySelectorAll(".task-row[data-id=notice1]").length'), 1);
      assert.match(await body(), /已转为任务/);
      assert.equal(await evaluate('document.querySelector("[data-action=open-target]").textContent'), '查看任务');
      await click('[data-action="open-target"]');
      assert.equal(await evaluate('document.querySelector("[aria-current=page]").dataset.page'), 'tasks');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'), 'notice-task-notice1');
    });
    await t.test('通知未读只标记一次，状态刷新不会无限请求或重置筛选', async () => {
      await nav('notices'); await select('notice1');
      const count = await evaluate('backend.messages.filter(message=>message.operation==="mark-read" && message.parameters.id==="notice1").length');
      await evaluate('backend.push();backend.push();backend.push()');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="mark-read" && message.parameters.id==="notice1").length'), count);
      await nav('tasks'); await change('#course-filter', '物理'); await change('#search', '实验');
      await evaluate('backend.status.transport="disconnected";backend.status.qq="unknown";backend.status.receiving="paused";backend.push()');
      assert.equal(await evaluate('document.querySelector("#course-filter").value'), '物理');
      assert.equal(await evaluate('document.querySelector("#search").value'), '实验');
      assert.match(await body(), /连接已断开/); assert.doesNotMatch(await evaluate('document.querySelector("#connection-status").textContent'), /QQ 在线/);
      assert.equal(await evaluate('document.querySelector("[aria-current=page]").dataset.page'), 'tasks');
      await evaluate('backend.status.error="account-mismatch";backend.push()');
      assert.match(await evaluate('document.querySelector("#host-error").textContent'), /账号不一致/);
      assert.doesNotMatch(await evaluate('document.querySelector("#host-error").textContent'), /account-mismatch/);
      await evaluate('backend.status.error=null;backend.push()');
    });
    await t.test('右详情独立滚动且确认栏在窗口内固定可见', async () => {
      await nav('pending'); await change('#state-filter', 'pending'); await change('#search', ''); await change('#course-filter', ''); await select('review1');
      let result = await call('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const screenshotPath = path.join(os.tmpdir(), 'messagepurify-application-ui-dark.png');
      fs.writeFileSync(screenshotPath, Buffer.from(result.data, 'base64'));
      const point = await evaluate('(()=>{const r=document.querySelector(".detail-scroll").getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+100};})()');
      await call('Input.dispatchMouseEvent', { type: 'mouseWheel', ...point, deltaX: 0, deltaY: 900 });
      await waitFor('document.querySelector(".detail-scroll").scrollTop>0');
      assert.ok(await evaluate('document.querySelector(".detail-scroll").scrollTop>0'));
      assert.equal(await evaluate('document.querySelector("#list-pane").scrollTop'), 0);
      assert.ok(await evaluate('(()=>{const r=document.querySelector("#pending-confirm-button").getBoundingClientRect();return r.top>0 && r.bottom<=innerHeight})()'));
      result = await call('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      fs.writeFileSync(path.join(os.tmpdir(), 'messagepurify-application-ui-dark-scroll.png'), Buffer.from(result.data, 'base64'));
      t.diagnostic('视觉检查截图：' + screenshotPath);
    });
    await t.test('未查看变更显示中文标记，实际打开详情后清标记并保留记录', async () => {
      const changed = structuredClone(fixture);
      Object.assign(changed.items[0], { id: 'history-task', version: 200, changeLabel: '已延期', changes: [{ label: '已延期', at: '2026-10-13T01:23:45Z', version: 200, fields: [{ field: '正式日期', before: '2026-12-19', after: '2026-12-20' }], evidence: [] }] });
      await evaluate('backend.hold=true');
      try {
        await push(changed); await nav('tasks'); await select('start1');
        assert.equal(await evaluate('document.querySelector(".task-row[data-id=history-task] .change-badge")?.textContent'), '已延期');
        assert.equal(await evaluate('backend.held.some(message=>message.operation==="mark-read" && message.parameters.id==="history-task" && message.parameters.version===200)'), false, '只打开列表不确认其他事项的变更');
        await select('history-task');
        assert.equal(await evaluate('document.querySelector(".detail-change-label")?.textContent'), '已延期');
        assert.match(await evaluate('document.querySelector(".change-history").textContent'), /已延期.*正式日期.*2026-12-19.*2026-12-20/s);
      } finally { await evaluate('backend.release()'); }
      assert.equal(await evaluate('document.querySelector(".detail-change-label")'), null);
      assert.equal(await evaluate('document.querySelector(".task-row[data-id=history-task] .change-badge")'), null);
      assert.match(await evaluate('document.querySelector(".change-history").textContent'), /已延期/);
      await nav('settings'); await change('#theme-setting', 'dark'); await nav('tasks'); await select('history-task');
      await evaluate('document.querySelector(".change-history").scrollIntoView({block:"start"})');
      await capture('ui-change-history-dark.png');
    });
    await t.test('变更记录保留后端顺序、北京时间、日期精度及独立来源并转义全部字段', async () => {
      const changed = structuredClone(fixture);
      changed.items[0].id = 'escaped-history-task';
      const source = { ...evidence[0], text: '<script>window.compromised=true</script>', groupName: '<img src=x onerror="window.compromised=true">', senderId: '<iframe>', imageCount: 0 };
      changed.items[0].changes = [
        { label: '已修改<img src=x>', at: '2026-10-13T01:23:45Z', version: 202, fields: [{ field: '<课程>', before: '数学<iframe>', after: '（未填写）' }, { field: '正式时间', before: '2026-12-20 18:30', after: '2026-12-21 · 仅有日期' }], evidence: [source] },
        { label: '已创建', at: '2026-10-12T01:00:00Z', version: 1, fields: [], evidence: [] }
      ];
      await push(changed); await nav('tasks'); await select('escaped-history-task');
      const history = await evaluate('document.querySelector(".change-history")?.textContent');
      assert.match(history, /已修改<img src=x>.*2026.*10.*13.*09:23:45/s);
      assert.match(history, /<课程>.*数学<iframe>.*（未填写）.*2026-12-21 · 仅有日期/s);
      assert.match(history, /<script>window.compromised=true<\/script>/);
      assert.ok(history.indexOf('已修改') < history.indexOf('已创建'));
      assert.equal(await evaluate('!!window.compromised || !!document.querySelector(".change-history img,.change-history iframe,.change-history script")'), false);
      assert.equal(await evaluate('document.querySelector(".change-history").compareDocumentPosition(document.querySelector(".source-section")) & Node.DOCUMENT_POSITION_FOLLOWING'), 4);
    });
    await t.test('待确认变更保留正式值和日期精度，查看建议定位原建议并保留编辑草稿', async () => {
      const changed = structuredClone(fixture);
      changed.items[0].id = 'pending-history-task';
      changed.items[0].changeLabel = '待确认变更'; changed.items[0].unread = false;
      changed.items[0].pendingChanges = [{ id: 'remind1', label: '催交待确认', due: '2026-12-25', time: null, timeKind: 'deadline', reasons: ['与手动日期冲突'] }, { id: 'cancel1', label: '取消待确认', due: null, time: null, timeKind: 'deadline', reasons: ['来源待核对'] }];
      await push(changed); await nav('tasks'); await select('pending-history-task');
      await change('#time-form [name=title]', '保留中的编辑标题');
      assert.match(await evaluate('document.querySelector(".pending-changes")?.textContent'), /催交待确认.*2026-12-25.*仅有日期.*与手动日期冲突.*取消待确认.*来源待核对/s);
      assert.equal(await evaluate('document.querySelector("#time-form [name=date]").value'), '2026-12-20');
      assert.equal(await evaluate('document.querySelector(".detail-change-label").textContent'), '待确认变更');
      await capture('ui-pending-changes-dark.png');
      await click('[data-action="open-suggestion"][data-id="cancel1"]');
      assert.equal(await evaluate('document.querySelector("[aria-current=page]").dataset.page'), 'pending');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'), 'cancel1');
      await nav('tasks'); await select('pending-history-task');
      assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'), '保留中的编辑标题');
    });
    await t.test('查看期间到达的新变更独立确认版本，旧响应不会清掉未展示的标记', async () => {
      const changed = structuredClone(fixture);
      Object.assign(changed.items[0], { id: 'raced-history-task', version: 301, unread: true, changeLabel: '已延期', changes: [{ label: '已延期', at: '2026-10-13T01:00:00Z', version: 301, fields: [], evidence: [] }] });
      await evaluate('backend.hold=true');
      try {
        await push(changed); await nav('tasks'); await select('raced-history-task');
        const oldRead = await evaluate('backend.held.find(message=>message.operation==="mark-read" && message.parameters.id==="raced-history-task" && message.parameters.version===301)');
        assert.ok(oldRead);
        await evaluate('Object.assign(backend.snapshot.items.find(item=>item.id==="raced-history-task"),{version:302,changeLabel:"新要求",changes:[{label:"新要求",at:"2026-10-13T02:00:00Z",version:302,fields:[],evidence:[]}]});backend.snapshot.generatedAt=new Date().toISOString();backend.push()');
        await evaluate(`backend.emit({id:${JSON.stringify(oldRead.id)},success:false,error:"version_conflict",snapshot:backend.snapshot,status:backend.status})`);
        assert.equal(await evaluate('document.querySelector(".detail-change-label")?.textContent'), '新要求');
        assert.match(await evaluate('document.querySelector(".change-history").textContent'), /新要求/);
        assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="mark-read" && message.parameters.id==="raced-history-task" && message.parameters.version===302).length'), 1);
      } finally { await evaluate('backend.release()'); }
      assert.equal(await evaluate('document.querySelector(".detail-change-label")'), null);
      assert.match(await evaluate('document.querySelector(".change-history").textContent'), /新要求/);
    });
    await t.test('来源、标题、群名中的 HTML 全部按纯文本展示', async () => {
      const malicious = structuredClone(fixture); malicious.items[2].title = '<img src=x onerror="window.compromised=true">'; malicious.items[2].evidence[0].text = '<script>window.compromised=true</script>'; malicious.items[2].evidence[0].groupName = '<iframe src=https://example.com>';
      await push(malicious); await nav('pending'); await select('review1');
      assert.match(await body(), /<script>window.compromised=true<\/script>/);
      assert.equal(await evaluate('!!window.compromised || !!document.querySelector("img,iframe")'), false);
    });
    await t.test('主题与减少动画可存偏好，空快照没有演示事项', async () => {
      await nav('settings'); await change('#theme-setting', 'light'); await change('#motion-setting', 'reduce');
      assert.equal(await evaluate('document.documentElement.dataset.theme'), 'light');
      assert.equal(await evaluate('document.documentElement.dataset.motion'), 'reduce');
      assert.ok(await evaluate('Object.keys(JSON.parse(localStorage.getItem("message-purify-preferences"))).every(key=>["theme","motion"].includes(key))'));
      assert.deepEqual(await evaluate('Object.keys(localStorage)'), ['message-purify-preferences']);
      await push({ accountId: '12345', generatedAt: new Date().toISOString(), items: [], groups: [], reminders: [] }); await nav('tasks');
      assert.equal(await evaluate('document.querySelectorAll(".task-row").length'), 0); assert.match(await body(), /暂无待办/);
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="snapshot").length'), 4, '初始读取、两次冲突刷新及一次手动全局刷新');
      assert.deepEqual(browserErrors, []);
    });
  } finally {
    socket?.close(); browser.kill();
    await new Promise(resolve => { if (browser.exitCode !== null) resolve(); else { const timer = setTimeout(resolve, 3000); browser.once('exit', () => { clearTimeout(timer); resolve(); }); } });
    // Chrome can hold profile files briefly after exit. The temporary profile is safe to leave on failure.
    if (path.dirname(path.resolve(profile)) === path.resolve(os.tmpdir()) && path.basename(profile).startsWith('messagepurify-app-qa-')) {
      try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 2, retryDelay: 100 }); } catch {}
    }
  }
});
