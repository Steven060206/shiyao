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
const itemDefaults = { source: 'qq', due: null, time: null, timeKind: 'deadline', priority: null, targetTaskId: null, targetTaskVersion: null, undoOperationId: null, undoExpiresAt: null, reviewReasons: [], suggestedDue: null, suggestedTime: null, action: 'submit', canAccept: false };
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
function mergeSuggestionSnapshot(prefix, accountId = '12345') {
  const first = { ...structuredClone(fixture.items[0]), id: prefix + '-first', title: '人工改名的实验报告', course: '人工分类', due: '2026-12-28', time: '18:30', priority: 2, version: 110, unread: false, evidence: [{ ...evidence[0], groupName: '实验群', text: '实验群：周一提交报告' }] };
  const second = { ...structuredClone(fixture.items[1]), id: prefix + '-second', title: '实验报告', course: '物理', due: '2026-12-29', time: null, timeKind: 'start', priority: 1, state: 'completed', version: 120, unread: false, evidence: [{ ...evidence[0], groupId: '101', groupName: '班级群', text: '班级群：周一提交报告' }] };
  const suggestion = { ...itemDefaults, id: prefix + '-suggestion', kind: 'merge-review', title: '可能重复：人工改名的实验报告', course: '人工分类', group: '实验群 / 班级群', state: 'pending', version: 230, unread: true, reviewReasons: ['同课程的跨群相似事项，需人工核对'], evidence: [...first.evidence, ...second.evidence], mergeSuggestion: { firstTaskId: first.id, firstTaskVersion: 110, secondTaskId: second.id, secondTaskVersion: 120 } };
  return { ...structuredClone(fixture), accountId, items: [first, second, suggestion] };
}
const status = { transport: 'connected', qq: 'online', receiving: 'active', history: 'partial', error: null, updatedAt: '2026-10-12T10:00:00+08:00', historyFailures: [{ groupId: '100', reason: 'history_timeout' }] };
const mockScript = `(() => {
  const listeners = new Set();
  const clone = value => JSON.parse(JSON.stringify(value));
  const backend = window.backend = { snapshot: ${JSON.stringify(fixture)}, status: ${JSON.stringify(status)}, startupEnabled: true, messages: [], hold: true, held: [], failNext: null, creationRequests: {}, structureRequests: {}, separatePairs: {} };
  backend.emit = data => listeners.forEach(listener => listener({data:clone(data)}));
  const pairKey = (accountId, firstId, secondId) => JSON.stringify([accountId, ...[firstId, secondId].sort()]);
  // Mirror the workspace's derived snapshot items; commands still target real tasks.
  backend.projectMergeSuggestions = () => {
    if (!backend.snapshot) return;
    backend.snapshot.items = backend.snapshot.items.filter(suggestion => {
      if (suggestion.kind !== 'merge-review') return true;
      const metadata = suggestion.mergeSuggestion;
      const first = backend.snapshot.items.find(item => item.id === metadata.firstTaskId);
      const second = backend.snapshot.items.find(item => item.id === metadata.secondTaskId);
      if (!first || !second || first.state === 'merged' || second.state === 'merged' || backend.separatePairs[pairKey(backend.snapshot.accountId, first.id, second.id)]) return false;
      metadata.firstTaskVersion = first.version; metadata.secondTaskVersion = second.version;
      suggestion.version = first.version + second.version;
      return true;
    });
  };
  backend.push = () => { backend.projectMergeSuggestions(); backend.emit({type:'state',snapshot:backend.snapshot,status:backend.status,startupEnabled:backend.startupEnabled}); };
  backend.release = () => { backend.hold=false; for (const command of backend.held.splice(0)) respond(command); };
  function respond(command) {
    const parameters=command.parameters || {};
    if(command.operation==='set-appearance') { backend.appearance=clone(parameters);backend.emit({id:command.id,success:true});return; }
    if(command.operation==='set-autostart') { backend.startupEnabled=parameters.enabled;backend.emit({id:command.id,success:true,startupEnabled:backend.startupEnabled});return; }
    if (backend.failNext && command.operation !== 'snapshot' && command.operation !== 'mark-read') {
      const error=backend.failNext;backend.failNext=null;backend.emit({id:command.id,success:false,error,snapshot:backend.snapshot,status:backend.status});return;
    }
    if(command.operation==='export-backup') {
      backend.exportedSnapshot=clone(backend.snapshot);
      backend.backupResult={accountId:parameters.accountId,path:'C:/backups/current.zip',createdAt:'2026-10-13T03:00:00Z',isComplete:false,missingImageCount:2,taskCount:2,reviewCount:3,noticeCount:1};
      backend.emit({id:command.id,success:true,backupResult:backend.backupResult,snapshot:backend.snapshot});return;
    }
    if(command.operation==='inspect-backup') {
      if(backend.cancelFile){backend.cancelFile=false;backend.emit({id:command.id,success:true,cancelled:true});return;}
      backend.backupPreview={id:'preview-1',generation:parameters.generation,preview:backend.backupResult,applicationSettings:{autoStartEnabled:false,theme:'light',motion:'reduce',desktopLayoutJson:null}};
      backend.emit({id:command.id,success:true,backupPreview:backend.backupPreview});return;
    }
    if(command.operation==='cancel-backup') {backend.backupPreview=null;backend.emit({id:command.id,success:true});return;}
    if(command.operation==='open-backup-location'){backend.emit({id:command.id,success:true});return;}
    if(command.operation==='restore-backup') {
      backend.snapshot={...clone(backend.exportedSnapshot),generation:(backend.snapshot.generation||0)+1,generatedAt:new Date().toISOString()};
      backend.backupPreview=null;backend.backupResult={...backend.backupResult,preRestoreBackupPath:'C:/backups/before.zip'};
      backend.emit({id:command.id,success:true,snapshot:backend.snapshot,backupResult:backend.backupResult,preferences:{theme:'light',motion:'reduce'},startupEnabled:false,isRestoring:false});return;
    }
    if(command.operation==='clean-cache') {
      backend.snapshot.cacheCleanup={accountId:parameters.accountId,completedAt:'2026-10-13T02:30:00Z',cutoff:'2026-10-06T02:30:00Z',deletedMessages:backend.cleanupDeleted ?? 12,retainedOldMessages:3,remainingMessages:15};
      backend.snapshot.cacheCleanupError=null;backend.snapshot.generatedAt=new Date().toISOString();
      backend.emit({id:command.id,success:true,snapshot:backend.snapshot,status:backend.status});return;
    }
    if(command.operation==='sync-now') { backend.emit({id:command.id,success:true,syncRequested:true,status:{...backend.status,syncPhase:'queued'}});return; }
    const item=backend.snapshot?.items.find(item=>item.id===parameters.id);
    if(['merge-tasks','split-task','keep-separate'].includes(command.operation)) {
      const saved=backend.structureRequests[parameters.operationId];
      if(saved) {
        backend.emit({id:command.id,success:saved===JSON.stringify(parameters),error:'operation_conflict',snapshot:backend.snapshot,status:backend.status});return;
      }
      const other=backend.snapshot.items.find(candidate=>candidate.id===parameters.mergeWithId);
      if(!item || item.version!==parameters.version || command.operation!=='split-task' && (!other || other.version!==parameters.mergeWithVersion)) { backend.emit({id:command.id,success:false,error:'version_conflict',snapshot:backend.snapshot,status:backend.status});return; }
      if(command.operation==='merge-tasks') {
        const values=clone(item), choices=parameters.mergeSelection;
        for(const [field,key] of Object.entries({title:'titleTaskId',course:'courseTaskId',state:'stateTaskId'})) item[field]=(choices[key]===item.id ? values : other)[field];
        item.priority=parameters.priority;
        const selectedTime=choices.timeTaskId===item.id ? values : other;
        for(const field of ['due','time','timeKind'])item[field]=selectedTime[field];
        item.evidence=[...(item.evidence||[]),...(other.evidence||[])];
        item.relatedItemIds=[...(item.relatedItemIds||[]),...(other.relatedItemIds||[])];
        other.state='merged';other.mergedIntoId=item.id;other.version++;
        item.changes=[{label:'已合并',at:new Date().toISOString(),fields:[],evidence:[]},...(item.changes||[])];
      } else if(command.operation==='split-task') {
        const newItem={...clone(item),id:parameters.newItemId,title:parameters.title,course:parameters.course,kind:parameters.date ? 'task' : 'review',state:parameters.date ? parameters.state : 'pending',due:parameters.date,time:parameters.time,timeKind:parameters.timeKind,priority:parameters.priority??3,version:1,unread:false,canAccept:!parameters.date,evidence:parameters.evidenceIndexes.map(index=>item.evidence[index]),relatedItemIds:[item.id],changes:[{label:'已拆分',at:new Date().toISOString(),fields:[],evidence:[]}]};
        backend.snapshot.items.push(newItem);item.relatedItemIds=[...(item.relatedItemIds||[]),newItem.id];
      } else if(command.operation==='keep-separate') backend.separatePairs[pairKey(backend.snapshot.accountId,item.id,other.id)]=true;
      item.version++;backend.structureRequests[parameters.operationId]=JSON.stringify(parameters);
    } else if(command.operation==='create-task') {
      if(item && backend.creationRequests[parameters.id] !== JSON.stringify(parameters)) { backend.emit({id:command.id,success:false,error:'任务 ID 已被其他事项使用。',snapshot:backend.snapshot,status:backend.status});return; }
      backend.creationRequests[parameters.id]=JSON.stringify(parameters);
      if (!item) backend.snapshot.items.push({...${JSON.stringify(itemDefaults)},id:parameters.id,kind:'task',source:'manual',title:parameters.title,course:parameters.course,group:'手动添加',state:'active',due:parameters.date,time:parameters.time || null,timeKind:parameters.timeKind,priority:parameters.priority ?? 3,version:1,unread:false,evidence:[]});
      if(backend.failAfterCreateOnce) { backend.failAfterCreateOnce=false;backend.emit({id:command.id,success:false,error:'command_timeout',snapshot:backend.snapshot,status:backend.status});return; }
    } else if(command.operation==='confirm-group') {
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
    backend.projectMergeSuggestions();
    if(backend.snapshot)backend.snapshot.generatedAt=new Date().toISOString();
    if(backend.failAfterStructureOnce && ['merge-tasks','split-task','keep-separate'].includes(command.operation)){backend.failAfterStructureOnce=false;backend.emit({id:command.id,success:false,error:'本地数据操作失败，原数据已保留。',snapshot:backend.snapshot,status:backend.status});return;}
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

    await t.test('拾要名称及本地图标实际加载，浅深主题均保持可见', async () => {
      const check = await evaluate(`(async () => {
        const icon = document.querySelector('img.brand-mark');
        await icon.decode();
        const originalTheme = document.documentElement.dataset.theme;
        const themes = ['light', 'dark'].map(theme => {
          document.documentElement.dataset.theme = theme;
          const rect = icon.getBoundingClientRect();
          return rect.width >= 24 && rect.height >= 24 && getComputedStyle(icon).display !== 'none';
        });
        document.documentElement.dataset.theme = originalTheme;
        return { title: document.title, brand: document.querySelector('.brand').textContent.trim(),
          loaded: icon.complete && icon.naturalWidth > 0, local: new URL(icon.src).protocol === 'file:', themes };
      })()`);
      assert.deepEqual(check,{title:'拾要',brand:'拾要',loaded:true,local:true,themes:[true,true]});
    });

    await t.test('初次请求真实快照与卡片外观，等待时无样例或虚假在线状态', async () => {
      assert.ok(await evaluate('!!document.querySelector("#app")'), '正式页面尚未实现');
      assert.match(await body(), /正在读取|等待连接/);
      assert.doesNotMatch(await body(), /QQ 在线|实验报告|Demo/);
      assert.equal(await evaluate('document.querySelectorAll(".task-row").length'), 0);
      assert.deepEqual(await evaluate('backend.messages[0]'), { id: await evaluate('backend.messages[0].id'), operation: 'snapshot', parameters: {} });
      assert.deepEqual(await evaluate('backend.messages.map(message=>message.operation)'), ['snapshot', 'set-appearance']);
      assert.deepEqual(await evaluate('backend.messages[1].parameters'), { theme: 'dark', reducedMotion: false, preferenceTheme:'system',preferenceMotion:'system' });
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
    await t.test('自动同步按钮请求一轮，切回 QQ 后正常空闲且不显示重连', async () => {
      await evaluate(`backend.status={...${JSON.stringify(status)},transport:"disconnected",qq:"unknown",receiving:"paused",scheduled:false,syncPhase:"idle",history:"complete",historyFailures:[],lastSuccessfulSyncAt:"2026-10-14T02:10:00Z",nextScheduledSyncAt:null};backend.push()`);
      assert.equal(await evaluate('document.querySelector("#connection-status").textContent'), '普通 QQ 模式');
      assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), '同步完成，已切回 QQ');
      assert.equal(await evaluate('document.querySelector("[data-action=sync-now]").disabled'), false);
      await nav('settings');
      assert.equal(await evaluate('document.querySelector("#last-successful-sync").textContent'), '2026-10-14 10:10（北京时间）');
      assert.match(await evaluate('document.querySelector("#settings-panel").textContent'), /点击.*自动同步.*切回/);
      assert.equal(await evaluate('document.querySelector("#next-scheduled-sync")'), null);
      const before = await evaluate('backend.messages.filter(m=>m.operation==="sync-now").length');
      await click('[data-action=sync-now]');
      assert.equal(await evaluate('backend.messages.filter(m=>m.operation==="sync-now").length'), before + 1);
      assert.deepEqual(await evaluate('backend.messages.findLast(m=>m.operation==="sync-now").parameters'), {});
      for (const [syncPhase, label] of [['queued','等待同步'],['starting','正在切换 QQ'],['syncing','正在同步置顶群消息'],['restoring','正在切回普通 QQ']]) {
        await evaluate(`backend.status.syncPhase=${JSON.stringify(syncPhase)};backend.push()`);
        assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), label);
        assert.equal(await evaluate('document.querySelector("[data-action=sync-now]").disabled'), true);
      }
      await evaluate('backend.status.syncPhase="idle";backend.push()');
      assert.equal(await evaluate('document.querySelector("[data-action=sync-now]").disabled'), false);
      await evaluate('backend.status.syncPhase="failed";backend.status.error="sync-timeout";backend.push()');
      assert.equal(await evaluate('document.querySelector("#receiving-status").textContent'), '同步失败，可重试');
      assert.match(await evaluate('document.querySelector("#host-error").textContent'), /超时.*普通 QQ/);
      await evaluate(`backend.status=${JSON.stringify(status)};backend.push()`); await nav('pending');
    });
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
      assert.equal(await evaluate('document.querySelectorAll("#detail-pane img,#detail-pane iframe").length'), 0);
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
    await t.test('手动新增要求标题、课程、正式日期，日期独立保存并打开手动来源详情', async () => {
      await push(fixture); await nav('tasks'); await click('[data-action="new-task"]');
      assert.equal(await evaluate('document.querySelector("#manual-task-form [name=priority]").value'), 'auto');
      assert.deepEqual(await evaluate('[...document.querySelectorAll("#task-course-options option")].map(option=>option.value)'), await evaluate('[...document.querySelector("#course-filter").options].map(option=>option.value).filter(Boolean)'));
      const before = await evaluate('backend.messages.filter(message=>message.operation==="create-task").length');
      for (const [name, value, message] of [['title', '   ', /标题/], ['course', ' ', /课程/], ['date', '', /正式日期/], ['date', '2026-02-30', /日期/]]) {
        await change('#manual-task-form [name=title]', '口头报告'); await change('#manual-task-form [name=course]', '物理'); await change('#manual-task-form [name=date]', '2026-12-22');
        await change(`#manual-task-form [name=${name}]`, value); await click('[data-action="create-task"]');
        assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="create-task").length'), before);
        assert.match(await evaluate('document.querySelector("#manual-task-error").textContent'), message);
      }
      await change('#manual-task-form [name=title]', '  口头报告  '); await change('#manual-task-form [name=course]', '  自定义课程  '); await change('#manual-task-form [name=date]', '2026-12-22');
      await capture('manual-task-form-dark.png');
      await click('[data-action="create-task"]');
      const command = await evaluate('backend.messages.filter(message=>message.operation==="create-task").at(-1)');
      assert.deepEqual(command.parameters, {accountId:'12345',generation:0,id:command.parameters.id,operationId:command.parameters.operationId,version:0,title:'口头报告',course:'自定义课程',date:'2026-12-22',time:null,timeKind:'deadline',priority:null});
      assert.ok(command.parameters.id); assert.ok(command.parameters.operationId);
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'), command.parameters.id);
      assert.match(await evaluate('document.querySelector(".task-row.selected").textContent'), /自定义课程.*手动添加/s);
      assert.match(await evaluate('document.querySelector(".source-section").textContent'), /手动添加/);
      assert.doesNotMatch(await evaluate('document.querySelector(".source-section").textContent'), /原始消息|发送者|QQ|暂无.*原消息/);
      assert.equal(await evaluate('document.querySelector("#detail-pane .message")'), null);
      assert.match(await evaluate('document.querySelector(".deadline-box").textContent'), /2026-12-22.*仅有日期/);
      assert.ok(await evaluate('[...document.querySelector("#course-filter").options].some(option=>option.value==="自定义课程")'));
      await click('[data-action="complete"]'); await nav('completed'); await select(command.parameters.id); await click('[data-action="undo-complete"]');
      assert.equal(await evaluate(`backend.snapshot.items.find(item=>item.id===${JSON.stringify(command.parameters.id)}).state`), 'active');
    });
    await t.test('手动新增失败保留草稿，重试沿用任务和操作 ID 并阻止重复提交', async () => {
      await push(fixture); await nav('tasks'); await click('[data-action="new-task"]');
      const fields = {title:'重试新增',course:'数学',date:'2026-12-23',time:'08:30',timeKind:'start',priority:'1'};
      for (const [name, value] of Object.entries(fields)) await change(`#manual-task-form [name=${name}]`, value);
      await evaluate('backend.failAfterCreateOnce=true'); await click('[data-action="create-task"]');
      const first = await evaluate('backend.messages.filter(message=>message.operation==="create-task").at(-1)');
      assert.match(await evaluate('document.querySelector("#manual-task-error").textContent'), /草稿.*保留/);
      for (const [name, value] of Object.entries(fields)) assert.equal(await evaluate(`document.querySelector('#manual-task-form [name=${name}]').value`), value);
      assert.equal(await evaluate('document.querySelector("#manual-task-form [name=title]").disabled'), true, '保存结果未确认时先重试，确认后再编辑');
      assert.equal(await evaluate('document.querySelector("[data-action=create-task]").disabled'), false);
      await evaluate('backend.hold=true'); await click('[data-action="create-task"]');
      assert.equal(await evaluate('document.querySelector("[data-action=create-task]").disabled'), true);
      await evaluate('document.querySelector("#manual-task-form").dispatchEvent(new Event("submit",{bubbles:true,cancelable:true}))');
      const retries = await evaluate(`backend.messages.filter(message=>message.operation==="create-task" && message.parameters.id===${JSON.stringify(first.parameters.id)})`);
      assert.equal(retries.length, 2); assert.deepEqual(retries[1].parameters, first.parameters);
      await evaluate('backend.release()');
      assert.equal(await evaluate(`backend.snapshot.items.filter(item=>item.id===${JSON.stringify(first.parameters.id)}).length`), 1);
      assert.match(await evaluate('document.querySelector(".deadline-box").textContent'), /开始时间.*2026-12-23 08:30/s);
      assert.equal(await evaluate(`backend.snapshot.items.find(item=>item.id===${JSON.stringify(first.parameters.id)}).priority`), 1);
    });
    await t.test('新增本地超时先用原请求确认，确定未保存的失败允许修改草稿', async () => {
      await push(fixture); await nav('tasks'); await click('[data-action="new-task"]');
      for (const [name, value] of Object.entries({title:'超时新增',course:'物理',date:'2026-12-23'})) await change(`#manual-task-form [name=${name}]`, value);
      await evaluate('window.originalManualSetTimeout=window.setTimeout;window.setTimeout=(handler,ms,...args)=>{const id=window.originalManualSetTimeout(handler,ms,...args);if(ms===15000)window.manualTimeout={handler,id};return id;};backend.hold=true');
      try {
        await click('[data-action="create-task"]');
        const first = await evaluate('backend.messages.filter(message=>message.operation==="create-task").at(-1)');
        await evaluate(`(()=>{const emit=backend.emit;backend.emit=message=>{if(message.id!==${JSON.stringify(first.id)})emit(message);};backend.release();queueMicrotask(()=>{backend.emit=emit;clearTimeout(window.manualTimeout.id);window.manualTimeout.handler();});})()`);
        await waitFor('document.querySelector("#manual-task-error").textContent.includes("尚未确认")');
        assert.equal(await evaluate('document.querySelector("#manual-task-form [name=title]").disabled'), true);
        await change('#manual-task-form [name=title]', '不应重写未确认请求');
        await evaluate('backend.push();backend.hold=true'); await click('[data-action="create-task"]');
        const staleFailure = await evaluate('backend.messages.filter(message=>message.operation==="create-task").at(-1)');
        await evaluate(`backend.held=backend.held.filter(message=>message.id!==${JSON.stringify(staleFailure.id)});backend.emit({id:${JSON.stringify(staleFailure.id)},success:false,error:'command_timeout',snapshot:${JSON.stringify({...fixture,generatedAt:'2000-01-01T00:00:00Z'})},status:backend.status});backend.release()`);
        assert.equal(await evaluate('document.querySelector("#manual-task-form [name=title]").disabled'), true, '旧失败快照不能解锁已保存的请求');
        await click('[data-action="create-task"]');
        const retry = await evaluate('backend.messages.filter(message=>message.operation==="create-task").at(-1)');
        assert.deepEqual(retry.parameters, first.parameters);
        assert.equal(await evaluate(`backend.snapshot.items.find(item=>item.id===${JSON.stringify(first.parameters.id)}).title`), '超时新增');
        assert.equal(await evaluate(`backend.snapshot.items.filter(item=>item.id===${JSON.stringify(first.parameters.id)}).length`), 1);
      } finally { await evaluate('window.setTimeout=window.originalManualSetTimeout;backend.release()'); }
      await click('[data-action="new-task"]');
      for (const [name, value] of Object.entries({title:'未保存草稿',course:'数学',date:'2026-12-24'})) await change(`#manual-task-form [name=${name}]`, value);
      await evaluate('backend.failNext="本地数据操作失败，原数据已保留。"'); await click('[data-action="create-task"]');
      const failed = await evaluate('backend.messages.filter(message=>message.operation==="create-task").at(-1)');
      assert.equal(await evaluate('document.querySelector("#manual-task-form [name=title]").disabled'), false);
      await change('#manual-task-form [name=title]', '修改后新增'); await click('[data-action="create-task"]');
      const changed = await evaluate('backend.messages.filter(message=>message.operation==="create-task").at(-1)');
      assert.equal(changed.parameters.id, failed.parameters.id); assert.notEqual(changed.parameters.operationId, failed.parameters.operationId);
      assert.equal(await evaluate(`backend.snapshot.items.find(item=>item.id===${JSON.stringify(changed.parameters.id)}).title`), '修改后新增');
    });
    await t.test('保存中离开新增表单，迟到的成功响应保留当前页面、任务草稿和焦点', async () => {
      await push(fixture); await nav('tasks'); await click('[data-action="new-task"]');
      for (const [name, value] of Object.entries({title:'延迟保存',course:'物理',date:'2026-12-23'})) await change(`#manual-task-form [name=${name}]`, value);
      await evaluate('backend.hold=true'); await click('[data-action="create-task"]');
      const command = await evaluate('backend.messages.filter(message=>message.operation==="create-task").at(-1)');
      await nav('tasks'); await select('task1'); await change('#time-form [name=title]', '当前编辑草稿');
      await click('[data-action="edit-task"]');
      const edit = await evaluate('backend.messages.filter(message=>message.operation==="edit-task").at(-1)');
      await evaluate(`backend.held=backend.held.filter(message=>message.id!==${JSON.stringify(edit.id)});backend.emit({id:${JSON.stringify(edit.id)},success:false,error:'version_conflict',snapshot:backend.snapshot,status:backend.status})`);
      await evaluate('document.querySelector("#time-form [name=title]").focus();document.querySelector("#time-form [name=title]").setSelectionRange(1,3);backend.release()');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'), 'task1');
      assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'), '当前编辑草稿');
      assert.equal(await evaluate('document.activeElement.name'), 'title');
      assert.equal(await evaluate('document.activeElement.selectionStart'), 1);
      assert.match(await evaluate('document.querySelector("#detail-error").textContent'), /事项已有变化.*草稿已保留/);
      assert.equal(await evaluate('document.querySelector("[data-action=refresh-edit]").hidden'), false);
      assert.equal(await evaluate(`backend.snapshot.items.filter(item=>item.id===${JSON.stringify(command.parameters.id)}).length`), 1);
      await click('[data-action="new-task"]');
      assert.equal(await evaluate('document.querySelector("#manual-task-form [name=title]").value'), '');
      await click('[data-action="cancel-new-task"]');
    });
    await t.test('新增草稿在快照、课程选项更新和页面切换期间保持字段、焦点及筛选', async () => {
      await push(fixture); await nav('tasks'); await change('#course-filter', '物理'); await click('[data-action="new-task"]');
      await change('#manual-task-form [name=title]', '尚未提交的标题'); await change('#manual-task-form [name=course]', '尚未提交的新课程'); await change('#manual-task-form [name=date]', '2026-12-24');
      await evaluate('document.querySelector("#manual-task-form [name=title]").focus();document.querySelector("#manual-task-form [name=title]").setSelectionRange(2,4);backend.snapshot.items.push({...backend.snapshot.items[0],id:"new-course-option",course:"历史"});backend.snapshot.generatedAt=new Date().toISOString();backend.push()');
      assert.equal(await evaluate('document.activeElement.name'), 'title'); assert.equal(await evaluate('document.activeElement.selectionStart'), 2);
      assert.equal(await evaluate('document.querySelector("#manual-task-form [name=course]").value'), '尚未提交的新课程');
      assert.ok(await evaluate('[...document.querySelectorAll("#task-course-options option")].some(option=>option.value==="历史")'));
      assert.equal(await evaluate('document.querySelector("#course-filter").value'), '物理');
      await nav('notices'); await nav('tasks'); await click('[data-action="new-task"]');
      assert.equal(await evaluate('document.querySelector("#manual-task-form [name=title]").value'), '尚未提交的标题');
      await click('[data-action="cancel-new-task"]');
      assert.equal(await evaluate('document.querySelector("#manual-task-form")'), null);
      await click('[data-action="new-task"]');
      assert.equal(await evaluate('document.querySelector("#manual-task-form [name=title]").value'), '');
      await click('[data-action="cancel-new-task"]'); await change('#course-filter', '');
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
      assert.deepEqual(command.parameters, { accountId: '12345', generation: 0, id: 'edit1', operationId: command.parameters.operationId, version: 1, title: '实验报告修订版', course: '数学', date: '2026-12-31', time: '18:30', timeKind: 'start', priority: 1 });
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
      assert.deepEqual(command.parameters, { theme: 'light', reducedMotion: true, preferenceTheme:'light',preferenceMotion:'reduce' });
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
      assert.equal(await evaluate('!!window.compromised || !!document.querySelector("img:not(.brand-mark),iframe")'), false);
    });
    await t.test('新增草稿按账号隔离，旧账号的迟到保存响应不切换当前账号或覆盖新草稿', async () => {
      await push(fixture); await nav('tasks'); await click('[data-action="new-task"]');
      for (const [name,value] of Object.entries({title:'账号 A 的草稿',course:'数学',date:'2026-12-25'})) await change(`#manual-task-form [name=${name}]`,value);
      const accountB = {...structuredClone(fixture),accountId:'67890'};
      await push(accountB); await nav('tasks'); await click('[data-action="new-task"]');
      assert.equal(await evaluate('document.querySelector("#manual-task-form [name=title]").value'), '');
      await change('#manual-task-form [name=title]', '账号 B 的草稿');
      await push(fixture); await nav('tasks'); await click('[data-action="new-task"]');
      assert.equal(await evaluate('document.querySelector("#manual-task-form [name=title]").value'), '账号 A 的草稿');
      await evaluate('backend.hold=true'); await click('[data-action="create-task"]');
      const command = await evaluate('backend.messages.filter(message=>message.operation==="create-task").at(-1)');
      await push(accountB); await nav('tasks'); await click('[data-action="new-task"]');
      await evaluate(`backend.held=backend.held.filter(message=>message.id!==${JSON.stringify(command.id)});backend.emit({id:${JSON.stringify(command.id)},success:true,snapshot:${JSON.stringify({...fixture,generatedAt:'2099-01-01T00:00:00Z'})},status:backend.status});backend.release()`);
      assert.equal(await evaluate('backend.snapshot.accountId'), '67890');
      assert.equal(await evaluate('document.querySelector("#manual-task-form [name=title]").value'), '账号 B 的草稿');
      await change('#manual-task-form [name=course]', '物理'); await change('#manual-task-form [name=date]', '2026-12-26'); await click('[data-action="create-task"]');
      const accountBCommand = await evaluate('backend.messages.filter(message=>message.operation==="create-task").at(-1)');
      assert.equal(accountBCommand.parameters.accountId, '67890'); assert.notEqual(accountBCommand.parameters.id, command.parameters.id);
      await push(fixture); await nav('tasks');
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
    await t.test('合并展示五字段差异并要求明确选择，保留任务ID及全部来源历史', async () => {
      const merged=structuredClone(fixture);
      merged.items=merged.items.filter(item=>item.kind==='task').map((item,index)=>({...item,id:index?'merge-other':'merge-current',unread:false,version:10,evidence:[{...evidence[0],text:index?'另一来源':'当前来源'}]}));
      await push(merged);await nav('tasks');await select('merge-current');
      await click('[data-action="open-merge"]');
      await change('#merge-task-form [name=mergeWithId]','merge-other');
      assert.equal(await evaluate('document.querySelectorAll("#merge-task-form .merge-field").length'),5);
      assert.match(await evaluate('document.querySelector("#merge-task-form").textContent'),/存在差异/);
      const before=await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").length');
      await click('[data-action="merge-tasks"]');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").length'),before);
      assert.match(await evaluate('document.querySelector("#structure-error").textContent'),/选择.*字段|逐项.*选择/);
      const selection={titleTaskId:'merge-current',courseTaskId:'merge-other',timeTaskId:'merge-current',priorityTaskId:'merge-other',stateTaskId:'merge-current'};
      for(const [name,value] of Object.entries(selection)) await change(`#merge-task-form [name=${name}]`,value);
      await capture('ui-merge-conflicts-dark.png');
      await click('[data-action="merge-tasks"]');
      const command=await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").at(-1)');
      assert.deepEqual(command.parameters,{accountId:'12345',generation:0,id:'merge-current',version:10,operationId:command.parameters.operationId,mergeWithId:'merge-other',mergeWithVersion:10,mergeSelection:selection,priority:0});
      assert.equal(await evaluate('document.querySelectorAll(".task-row[data-id=merge-other]").length'),0);
      assert.match(await evaluate('document.querySelector(".source-section").textContent'),/当前来源.*另一来源/s);
      assert.match(await evaluate('document.querySelector(".merged-sources").textContent'),/考试/);
      await click('[data-action="view-merged-source"][data-id="merge-other"]');
      assert.match(await evaluate('document.querySelector(".merged-source-detail").textContent'),/另一来源/);
      await evaluate('backend.emit({type:"open-item",accountId:"12345",id:"merge-other"})');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'),'merge-current');
    });
    await t.test('拆分逐项选择证据及状态，缺日期进入待确认且关联事项可打开',async()=>{
      const split=structuredClone(fixture);split.items=[{...split.items[0],id:'split-current',unread:false,version:20,evidence:[{...evidence[0],text:'报告证据'},{...evidence[0],text:'论文证据'}]}];
      await push(split);await nav('tasks');await select('split-current');await click('[data-action="open-split"]');
      await change('#split-task-form [name=title]','课程论文');await change('#split-task-form [name=date]','');
      await evaluate('const boxes=[...document.querySelectorAll("#split-task-form [name=evidenceIndex]")];boxes[0].checked=false;boxes[0].dispatchEvent(new Event("change",{bubbles:true}));');
      await capture('ui-split-pending-dark.png');await click('[data-action="split-task"]');
      const command=await evaluate('backend.messages.filter(message=>message.operation==="split-task").at(-1)');
      assert.equal(command.parameters.date,null);assert.equal(command.parameters.priority,null);assert.deepEqual(command.parameters.evidenceIndexes,[1]);
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="split-current").state'),'active');
      assert.equal(await evaluate(`backend.snapshot.items.find(item=>item.id===${JSON.stringify(command.parameters.newItemId)}).state`),'pending');
      await click(`[data-action="open-related"][data-id="${command.parameters.newItemId}"]`);
      assert.equal(await evaluate('document.querySelector("[aria-current=page]").dataset.page'),'pending');
      assert.match(await evaluate('document.querySelector(".source-section").textContent'),/论文证据/);
      await click('[data-action="open-related"][data-id="split-current"]');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'),'split-current');
    });
    await t.test('合并自动P0来源及另一项P3时间时，保留用户所见优先级为显式值',async()=>{
      const automatic=structuredClone(fixture);automatic.items=automatic.items.filter(item=>item.kind==='task').map((item,index)=>({...item,id:index?'auto-merge-other':'auto-merge-current',unread:false,version:25,due:index?'2030-12-20':'2020-01-01',time:null,timeKind:'deadline',priority:index?3:0,priorityOverride:null}));
      await push(automatic);await nav('tasks');await select('auto-merge-current');await click('[data-action="open-merge"]');await change('#merge-task-form [name=mergeWithId]','auto-merge-other');
      const selection={titleTaskId:'auto-merge-current',courseTaskId:'auto-merge-current',timeTaskId:'auto-merge-other',priorityTaskId:'auto-merge-current',stateTaskId:'auto-merge-current'};
      for(const [name,value] of Object.entries(selection))await change(`#merge-task-form [name=${name}]`,value);
      await click('[data-action="merge-tasks"]');const command=await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").at(-1)');
      assert.equal(command.parameters.priority,0);assert.equal(command.parameters.mergeSelection.timeTaskId,'auto-merge-other');
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="auto-merge-current").due'),'2030-12-20');
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="auto-merge-current").priority'),0);
      assert.equal(await evaluate('document.querySelector("#time-form [name=priority]").value'),'0');
    });
    await t.test('保持独立按双方版本保存，完成状态差异不会自动传播',async()=>{
      const separate=structuredClone(fixture);separate.items=separate.items.filter(item=>item.kind==='task').map((item,index)=>({...item,id:index?'separate-other':'separate-current',state:index?'completed':'active',unread:false,version:30}));
      await push(separate);await nav('tasks');await select('separate-current');await click('[data-action="open-merge"]');
      await change('#merge-task-form [name=mergeWithId]','separate-other');
      assert.equal(await evaluate('document.querySelectorAll("#merge-task-form .merge-field.conflict").length'),5);
      await click('[data-action="keep-separate"]');
      const command=await evaluate('backend.messages.filter(message=>message.operation==="keep-separate").at(-1)');
      assert.deepEqual(command.parameters,{accountId:'12345',generation:0,id:'separate-current',version:30,operationId:command.parameters.operationId,mergeWithId:'separate-other',mergeWithVersion:30});
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="separate-current").state'),'active');
      await nav('completed');assert.equal(await evaluate('document.querySelector(".task-row[data-id=separate-other]").dataset.id'),'separate-other');
      await nav('tasks');await select('separate-current');await click('[data-action="open-merge"]');await change('#merge-task-form [name=mergeWithId]','separate-other');
      for(const name of ['titleTaskId','courseTaskId','timeTaskId','priorityTaskId','stateTaskId'])await change(`#merge-task-form [name=${name}]`,'separate-current');
      await click('[data-action="merge-tasks"]');
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="separate-other").state'),'merged');
      await change('#state-filter','all');assert.equal(await evaluate('document.querySelectorAll(".task-row").length'),1);
      await nav('completed');assert.equal(await evaluate('document.querySelectorAll(".task-row").length'),0);
    });
    await t.test('拆分校验标题、日期、证据和状态，正式项允许空课程并保留原任务',async()=>{
      const split=structuredClone(fixture);split.items=[{...split.items[0],id:'split-validation',version:40,unread:false}];
      await push(split);await nav('tasks');await select('split-validation');await click('[data-action="open-split"]');
      const before=await evaluate('backend.messages.filter(message=>message.operation==="split-task").length');
      for(const [name,value,message] of [['title',' ',/标题/],['title','字'.repeat(257),/256/],['priority','',/优先级/],['state','',/状态/]]){
        for(const [field,input] of Object.entries({title:'独立数据表',priority:'auto',state:'active'}))await change(`#split-task-form [name=${field}]`,input);
        await change(`#split-task-form [name=${name}]`,value);await click('[data-action="split-task"]');
        assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="split-task").length'),before);
        assert.match(await evaluate('document.querySelector("#structure-error").textContent'),message);
      }
      await change('#split-task-form [name=state]','cancelled');await change('#split-task-form [name=date]','');
      await evaluate('const box=document.querySelector("#split-task-form [name=evidenceIndex]");box.checked=false;box.dispatchEvent(new Event("change",{bubbles:true}));');
      await click('[data-action="split-task"]');assert.match(await evaluate('document.querySelector("#structure-error").textContent'),/证据.*日期/);
      await change('#split-task-form [name=date]','2026-12-28');await change('#split-task-form [name=course]','');await change('#split-task-form [name=time]','18:30');await change('#split-task-form [name=timeKind]','start');await change('#split-task-form [name=priority]','1');
      await click('[data-action="split-task"]');
      const command=await evaluate('backend.messages.filter(message=>message.operation==="split-task").at(-1)');
      assert.deepEqual(command.parameters,{accountId:'12345',generation:0,id:'split-validation',version:40,operationId:command.parameters.operationId,newItemId:command.parameters.newItemId,title:'独立数据表',course:'',date:'2026-12-28',time:'18:30',timeKind:'start',priority:1,state:'cancelled',evidenceIndexes:[]});
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="split-validation").state'),'active');
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="split-validation").evidence.length'),1);
      await click(`[data-action="open-related"][data-id="${command.parameters.newItemId}"]`);
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'),command.parameters.newItemId);
      assert.match(await evaluate('document.querySelector(".deadline-box").textContent'),/开始时间.*18:30.*已取消/s);
      const manual={...structuredClone(fixture),items:[{...fixture.items[0],id:'manual-split',source:'manual',group:'手动添加',unread:false,evidence:[]}]};
      await push(manual);await nav('tasks');await select('manual-split');await click('[data-action="open-split"]');await change('#split-task-form [name=date]','');await click('[data-action="split-task"]');
      assert.match(await evaluate('document.querySelector("#structure-error").textContent'),/无原消息证据.*正式日期/);
    });
    await t.test('合并冲突保留字段选择与编辑草稿，刷新核对双方最新版本再提交',async()=>{
      const merging=structuredClone(fixture);merging.items=merging.items.filter(item=>item.kind==='task').map((item,index)=>({...item,id:index?'merge-conflict-other':'merge-conflict-current',unread:false,version:50}));
      await push(merging);await nav('tasks');await select('merge-conflict-current');await change('#time-form [name=title]','尚未保存的编辑草稿');await click('[data-action="open-merge"]');await change('#merge-task-form [name=mergeWithId]','merge-conflict-other');
      const selection={titleTaskId:'merge-conflict-current',courseTaskId:'merge-conflict-other',timeTaskId:'merge-conflict-current',priorityTaskId:'merge-conflict-other',stateTaskId:'merge-conflict-current'};
      for(const [name,value] of Object.entries(selection))await change(`#merge-task-form [name=${name}]`,value);
      await evaluate('for(const item of backend.snapshot.items)item.version=51;backend.snapshot.items[0].title="后台更新后的标题";backend.snapshot.generatedAt=new Date().toISOString();backend.push()');
      await click('[data-action="merge-tasks"]');
      const failed=await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").at(-1)');
      assert.equal(failed.parameters.version,50);assert.equal(failed.parameters.mergeWithVersion,50);
      assert.match(await evaluate('document.querySelector("#structure-error").textContent'),/草稿已保留.*刷新/);
      for(const [name,value] of Object.entries(selection))assert.equal(await evaluate(`document.querySelector('#merge-task-form [name=${name}]').value`),value);
      await click('[data-action="refresh-structure"]');await click('[data-action="merge-tasks"]');
      const saved=await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").at(-1)');
      assert.equal(saved.parameters.version,51);assert.equal(saved.parameters.mergeWithVersion,51);assert.notEqual(saved.parameters.operationId,failed.parameters.operationId);
      assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'),'尚未保存的编辑草稿');
    });
    await t.test('拆分草稿随快照保留焦点筛选，刷新冲突后按原证据身份重新定位索引',async()=>{
      const split=structuredClone(fixture);split.items=[{...split.items[0],id:'split-conflict',unread:false,version:60,evidence:[{...evidence[0],text:'A证据'},{...evidence[0],text:'B证据'}]}];
      await push(split);await nav('tasks');await change('#course-filter','物理');await select('split-conflict');await click('[data-action="open-split"]');
      await change('#split-task-form [name=title]','保留中的拆分草稿');await change('#split-task-form [name=course]','新的拆分课程');
      await evaluate('(()=>{const box=document.querySelectorAll("#split-task-form [name=evidenceIndex]")[0];box.checked=false;box.dispatchEvent(new Event("change",{bubbles:true}));document.querySelector("#split-task-form [name=title]").focus();document.querySelector("#split-task-form [name=title]").setSelectionRange(1,3);backend.snapshot.items[0].version=61;backend.snapshot.items[0].evidence.reverse();backend.snapshot.generatedAt=new Date().toISOString();backend.push()})()');
      assert.equal(await evaluate('document.activeElement.name'),'title');assert.equal(await evaluate('document.activeElement.selectionStart'),1);
      assert.equal(await evaluate('document.querySelector("#course-filter").value'),'物理');
      await nav('notices');await nav('tasks');await select('split-conflict');await click('[data-action="open-split"]');
      assert.equal(await evaluate('document.querySelector("#split-task-form [name=title]").value'),'保留中的拆分草稿');
      await click('[data-action="split-task"]');assert.match(await evaluate('document.querySelector("#structure-error").textContent'),/草稿已保留/);
      const failed=await evaluate('backend.messages.filter(message=>message.operation==="split-task").at(-1)');assert.equal(failed.parameters.version,60);assert.deepEqual(failed.parameters.evidenceIndexes,[1]);
      await click('[data-action="refresh-structure"]');
      assert.equal(await evaluate('document.querySelector("#split-task-form [name=course]").value'),'新的拆分课程');
      assert.deepEqual(await evaluate('[...document.querySelectorAll("#split-task-form [name=evidenceIndex]:checked")].map(element=>Number(element.value))'),[0]);
      await click('[data-action="split-task"]');const saved=await evaluate('backend.messages.filter(message=>message.operation==="split-task").at(-1)');
      assert.equal(saved.parameters.version,61);assert.deepEqual(saved.parameters.evidenceIndexes,[0]);assert.equal(saved.parameters.newItemId,failed.parameters.newItemId);
      assert.equal(await evaluate(`backend.snapshot.items.find(item=>item.id===${JSON.stringify(saved.parameters.newItemId)}).evidence[0].text`),'B证据');
    });
    await t.test('拆分超时保留稳定操作与新事项ID，重复提交和迟到响应只创建一项',async()=>{
      const split=structuredClone(fixture);split.items=[{...split.items[0],id:'split-timeout',unread:false,version:70}];
      await push(split);await nav('tasks');await select('split-timeout');await click('[data-action="open-split"]');await change('#split-task-form [name=title]','超时后的独立分项');
      await evaluate('window.originalStructureSetTimeout=window.setTimeout;window.setTimeout=(handler,ms,...args)=>{const id=window.originalStructureSetTimeout(handler,ms,...args);if(ms===15000)window.structureTimeout={handler,id};return id;};backend.hold=true');
      try{
        await click('[data-action="split-task"]');const first=await evaluate('backend.messages.filter(message=>message.operation==="split-task").at(-1)');
        const before=await evaluate('backend.messages.length');await evaluate('document.querySelector("#split-task-form").dispatchEvent(new Event("submit",{bubbles:true,cancelable:true}))');assert.equal(await evaluate('backend.messages.length'),before);
        await evaluate('clearTimeout(window.structureTimeout.id);window.structureTimeout.handler()');
        assert.match(await evaluate('document.querySelector("#structure-error").textContent'),/尚未确认.*草稿已保留/);assert.equal(await evaluate('document.querySelector("#split-task-form [name=title]").disabled'),true);
        await change('#split-task-form [name=title]','不能重写未确认操作');await click('[data-action="split-task"]');
        const retry=await evaluate('backend.messages.filter(message=>message.operation==="split-task").at(-1)');assert.deepEqual(retry.parameters,first.parameters);
        await evaluate('backend.release()');assert.equal(await evaluate(`backend.snapshot.items.filter(item=>item.id===${JSON.stringify(first.parameters.newItemId)}).length`),1);
        await evaluate(`backend.emit({id:${JSON.stringify(first.id)},success:true,snapshot:${JSON.stringify({...split,generatedAt:'2099-01-01T00:00:00Z'})}})`);
        assert.equal(await evaluate('document.querySelectorAll(".related-items button").length'),1,'重复应答不覆盖已确认快照');
      }finally{await evaluate('window.setTimeout=window.originalStructureSetTimeout;backend.release()');}
    });
    await t.test('拆分或合并已写入后错误应答仍锁原请求，重试不创建第二项或修改字段',async()=>{
      const splitting=structuredClone(fixture);splitting.items=[{...splitting.items[0],id:'committed-split',unread:false,version:75}];
      await push(splitting);await nav('tasks');await select('committed-split');await click('[data-action="open-split"]');await change('#split-task-form [name=title]','已写入的分项');
      await evaluate('backend.failAfterStructureOnce=true');await click('[data-action="split-task"]');
      const split=await evaluate('backend.messages.filter(message=>message.operation==="split-task").at(-1)');
      assert.equal(await evaluate('document.querySelector("#split-task-form [name=title]").disabled'),true);assert.match(await evaluate('document.querySelector("#structure-error").textContent'),/尚未确认/);
      await change('#split-task-form [name=title]','不能修改已写入分项');await click('[data-action="split-task"]');
      assert.deepEqual(await evaluate('backend.messages.filter(message=>message.operation==="split-task").at(-1).parameters'),split.parameters);
      assert.equal(await evaluate(`backend.snapshot.items.filter(item=>item.id===${JSON.stringify(split.parameters.newItemId)}).length`),1);
      const merging=structuredClone(fixture);merging.items=merging.items.filter(item=>item.kind==='task').map((item,index)=>({...item,id:index?'committed-other':'committed-current',unread:false,version:76}));
      await push(merging);await nav('tasks');await select('committed-current');await click('[data-action="open-merge"]');await change('#merge-task-form [name=mergeWithId]','committed-other');
      for(const name of ['titleTaskId','courseTaskId','timeTaskId','priorityTaskId','stateTaskId'])await change(`#merge-task-form [name=${name}]`,'committed-current');
      await evaluate('backend.failAfterStructureOnce=true');await click('[data-action="merge-tasks"]');const merge=await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").at(-1)');
      assert.equal(merge.parameters.priority,3);
      assert.equal(await evaluate('document.querySelector("#merge-task-form [name=titleTaskId]").disabled'),true);assert.match(await evaluate('document.querySelector("#structure-error").textContent'),/尚未确认/);
      await evaluate('backend.snapshot.items.find(item=>item.id==="committed-current").priority=0;backend.snapshot.generatedAt=new Date().toISOString();backend.push()');
      await click('[data-action="merge-tasks"]');assert.deepEqual(await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").at(-1).parameters'),merge.parameters);
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'),'committed-current');
    });
    await t.test('迟到结构保存不切走另一任务，不清其编辑草稿及冲突提示',async()=>{
      const split=structuredClone(fixture);split.items=split.items.filter(item=>item.kind==='task').map((item,index)=>({...item,id:index?'late-edit':'late-split',unread:false,version:80}));
      await push(split);await nav('tasks');await select('late-split');await click('[data-action="open-split"]');await change('#split-task-form [name=title]','迟到的分项');await evaluate('backend.hold=true');await click('[data-action="split-task"]');
      await nav('tasks');await select('late-edit');await change('#time-form [name=title]','另一任务的编辑草稿');await click('[data-action="edit-task"]');
      await evaluate('const edit=backend.held.find(message=>message.operation==="edit-task");backend.held=backend.held.filter(message=>message!==edit);backend.emit({id:edit.id,success:false,error:"version_conflict",snapshot:backend.snapshot});document.querySelector("#time-form [name=title]").focus();document.querySelector("#time-form [name=title]").setSelectionRange(1,3);backend.release()');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'),'late-edit');assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'),'另一任务的编辑草稿');
      assert.equal(await evaluate('document.activeElement.name'),'title');assert.equal(await evaluate('document.activeElement.selectionStart'),1);assert.match(await evaluate('document.querySelector("#detail-error").textContent'),/草稿已保留/);
    });
    await t.test('合并拆分草稿按账号隔离，旧账号迟到响应不覆盖同ID的新账号草稿',async()=>{
      const accountA={...structuredClone(fixture),items:[{...fixture.items[0],id:'account-split',unread:false,version:90}]};
      const accountB={...structuredClone(accountA),accountId:'67890'};
      await push(accountA);await nav('tasks');await select('account-split');await click('[data-action="open-split"]');await change('#split-task-form [name=title]','账号A拆分');
      await push(accountB);await nav('tasks');await select('account-split');await click('[data-action="open-split"]');await change('#split-task-form [name=title]','账号B拆分');
      await push(accountA);await nav('tasks');await select('account-split');await click('[data-action="open-split"]');assert.equal(await evaluate('document.querySelector("#split-task-form [name=title]").value'),'账号A拆分');
      await evaluate('backend.hold=true');await click('[data-action="split-task"]');const first=await evaluate('backend.messages.filter(message=>message.operation==="split-task").at(-1)');
      await push(accountB);await nav('tasks');await select('account-split');await click('[data-action="open-split"]');
      await evaluate(`backend.held=backend.held.filter(message=>message.id!==${JSON.stringify(first.id)});backend.emit({id:${JSON.stringify(first.id)},success:true,snapshot:${JSON.stringify({...accountA,generatedAt:'2099-01-01T00:00:00Z'})}});backend.release()`);
      assert.equal(await evaluate('document.querySelector("#split-task-form [name=title]").value'),'账号B拆分');assert.equal(await evaluate('document.querySelector("#split-task-form [name=title]").disabled'),false);
      await click('[data-action="split-task"]');const saved=await evaluate('backend.messages.filter(message=>message.operation==="split-task").at(-1)');assert.equal(saved.parameters.accountId,'67890');assert.notEqual(saved.parameters.newItemId,first.parameters.newItemId);
    });
    await t.test('已接受拆分建议的关联入口跳转正式任务，接受记录不混入待确认列表',async()=>{
      const accepted=structuredClone(fixture);accepted.items=[{...fixture.items[0],id:'accepted-parent',unread:false,relatedItemIds:['accepted-child']},{...fixture.items[2],id:'accepted-child',state:'accepted',targetTaskId:'accepted-task',unread:false},{...fixture.items[0],id:'accepted-task',unread:false,relatedItemIds:['accepted-parent']}];
      await push(accepted);await nav('tasks');await select('accepted-parent');await click('[data-action="open-related"][data-id="accepted-child"]');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'),'accepted-task');
      await nav('pending');await change('#state-filter','all');assert.equal(await evaluate('document.querySelectorAll(".task-row").length'),0);
    });
    await t.test('迟到冲突刷新绑定发起账号，同ID另一账号的拆分草稿不被确认版本',async()=>{
      const accountA={...structuredClone(fixture),items:[{...fixture.items[0],id:'refresh-account',unread:false,version:100}]};
      const accountB={...structuredClone(accountA),accountId:'67890'};
      await push(accountA);await nav('tasks');await select('refresh-account');await click('[data-action="open-split"]');await change('#split-task-form [name=title]','A账号刷新草稿');
      await evaluate('backend.snapshot.items[0].version=101;backend.snapshot.generatedAt=new Date().toISOString();backend.push()');await click('[data-action="split-task"]');await evaluate('backend.hold=true');await click('[data-action="refresh-structure"]');
      const refresh=await evaluate('backend.messages.filter(message=>message.operation==="snapshot").at(-1)');
      await push(accountB);await nav('tasks');await select('refresh-account');await click('[data-action="open-split"]');await change('#split-task-form [name=title]','B账号刷新草稿');
      await evaluate(`backend.held=backend.held.filter(message=>message.id!==${JSON.stringify(refresh.id)});backend.emit({id:${JSON.stringify(refresh.id)},success:true,snapshot:${JSON.stringify({...accountA,generatedAt:'2099-01-01T00:00:00Z'})}});backend.release()`);
      assert.equal(await evaluate('document.querySelector("#split-task-form [name=title]").value'),'B账号刷新草稿');
      await click('[data-action="split-task"]');const saved=await evaluate('backend.messages.filter(message=>message.operation==="split-task").at(-1)');assert.equal(saved.parameters.accountId,'67890');assert.equal(saved.parameters.version,100);
    });
    await t.test('跨群可能重复建议进入待确认计数与新账号默认入口',async()=>{
      await push(mergeSuggestionSnapshot('merge-default', '67891'));
      assert.equal(await evaluate('document.querySelector("#page-title").textContent'),'待确认');
      assert.equal(await evaluate('document.querySelectorAll(".task-row").length'),1);
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'),'merge-default-suggestion');
      assert.match(await evaluate('document.querySelector("[data-page=pending] .count").title'),/共 1 项/);
      assert.match(await evaluate('document.querySelector(".task-row .row-note").textContent'),/跨群相似/);
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="mark-read" && message.parameters.id==="merge-default-suggestion").at(-1).parameters.version'),230);
      await nav('tasks');assert.equal(await evaluate('document.querySelectorAll(".task-row").length'),1);
    });
    await t.test('建议详情展示双方当前正式字段和原文，不提供普通确认忽略拒绝',async()=>{
      await push(mergeSuggestionSnapshot('merge-detail'));await nav('pending');await select('merge-detail-suggestion');
      const detail=await evaluate('document.querySelector(".detail-scroll").textContent');
      assert.match(detail,/跨群相似/);assert.match(detail,/人工改名的实验报告/);assert.match(detail,/实验报告/);
      assert.match(detail,/人工分类/);assert.match(detail,/物理/);assert.match(detail,/2026-12-28 18:30/);assert.match(detail,/2026-12-29/);
      assert.match(detail,/P2/);assert.match(detail,/P1/);assert.match(detail,/待办/);assert.match(detail,/已完成/);
      assert.match(detail,/实验群：周一提交报告/);assert.match(detail,/班级群：周一提交报告/);
      assert.equal(await evaluate('document.querySelectorAll("[data-action=accept-review],[data-action=ignore],[data-action=reject],#review-form").length'),0);
      assert.equal(await evaluate('document.querySelectorAll("[data-action=open-merge-suggestion]").length'),1);
      await call('Emulation.setDeviceMetricsOverride',{width:1200,height:1600,deviceScaleFactor:1,mobile:false});
      try { await capture('cross-group-suggestion.png'); }
      finally { await call('Emulation.setDeviceMetricsOverride',{width:1200,height:800,deviceScaleFactor:1,mobile:false}); }
    });
    await t.test('建议打开预选双方，保留手改正式差异和原任务编辑草稿',async()=>{
      await push(mergeSuggestionSnapshot('merge-preselect'));await nav('tasks');await select('merge-preselect-first');
      await change('#time-form [name=title]','尚未保存的任务编辑');await nav('pending');await select('merge-preselect-suggestion');await click('[data-action=open-merge-suggestion]');
      assert.equal(await evaluate('document.querySelector("#merge-task-form").dataset.id'),'merge-preselect-first');
      assert.equal(await evaluate('document.querySelector("#merge-task-form [name=mergeWithId]").value'),'merge-preselect-second');
      assert.equal(await evaluate('document.querySelectorAll("#merge-task-form .merge-field.conflict").length'),5);
      assert.match(await evaluate('document.querySelector(".detail-scroll").textContent'),/跨群相似/);
      await call('Emulation.setDeviceMetricsOverride',{width:1200,height:1600,deviceScaleFactor:1,mobile:false});
      try { await capture('cross-group-suggestion-merge.png'); }
      finally { await call('Emulation.setDeviceMetricsOverride',{width:1200,height:800,deviceScaleFactor:1,mobile:false}); }
      await click('[data-action=close-structure]');await nav('tasks');await select('merge-preselect-first');
      assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'),'尚未保存的任务编辑');
    });
    await t.test('建议保持独立发送双方版本，刷新和重开不再提示该建议',async()=>{
      const original=mergeSuggestionSnapshot('merge-separate');await push(original);await nav('pending');await select('merge-separate-suggestion');await click('[data-action=open-merge-suggestion]');await click('[data-action=keep-separate]');
      const command=await evaluate('backend.messages.filter(message=>message.operation==="keep-separate").at(-1)');
      assert.deepEqual(command.parameters,{accountId:'12345',generation:0,id:'merge-separate-first',version:110,operationId:command.parameters.operationId,mergeWithId:'merge-separate-second',mergeWithVersion:120});
      await nav('pending');assert.equal(await evaluate('document.querySelectorAll(".task-row").length'),0);
      await click('[data-action=refresh]');await push(original);await nav('pending');
      assert.equal(await evaluate('document.querySelectorAll(".task-row").length'),0);
      assert.equal(await evaluate('document.querySelector("[data-page=pending] .count")'),null);
    });
    await t.test('建议双版本冲突须显式刷新，刷新保留字段选择与编辑草稿',async()=>{
      await push(mergeSuggestionSnapshot('merge-refresh'));await nav('tasks');await select('merge-refresh-first');await change('#time-form [name=title]','冲突时保留的编辑草稿');
      await nav('pending');await select('merge-refresh-suggestion');await click('[data-action=open-merge-suggestion]');
      const selection={titleTaskId:'merge-refresh-first',courseTaskId:'merge-refresh-second',timeTaskId:'merge-refresh-first',priorityTaskId:'merge-refresh-second',stateTaskId:'merge-refresh-first'};
      for(const [name,value] of Object.entries(selection))await change('#merge-task-form [name='+name+']',value);
      await evaluate('backend.snapshot.items.find(item=>item.id==="merge-refresh-first").version=111;backend.snapshot.items.find(item=>item.id==="merge-refresh-second").version=121;backend.snapshot.items[0].title="后台的新正式标题";backend.snapshot.generatedAt=new Date().toISOString();backend.push()');
      await click('[data-action=merge-tasks]');const failed=await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").at(-1)');
      assert.equal(failed.parameters.version,110);assert.equal(failed.parameters.mergeWithVersion,120);
      assert.match(await evaluate('document.querySelector("#structure-error").textContent'),/草稿已保留.*刷新/);
      for(const [name,value] of Object.entries(selection))assert.equal(await evaluate('document.querySelector("#merge-task-form [name='+name+']").value'),value);
      await click('[data-action=refresh-structure]');
      assert.match(await evaluate('document.querySelector("#merge-task-form").textContent'),/后台的新正式标题/);
      for(const [name,value] of Object.entries(selection))assert.equal(await evaluate('document.querySelector("#merge-task-form [name='+name+']").value'),value);
      await click('[data-action=merge-tasks]');const saved=await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").at(-1)');
      assert.equal(saved.parameters.version,111);assert.equal(saved.parameters.mergeWithVersion,121);assert.notEqual(saved.parameters.operationId,failed.parameters.operationId);
      assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'),'冲突时保留的编辑草稿');
      await nav('pending');assert.equal(await evaluate('document.querySelectorAll(".task-row").length'),0);
    });
    await t.test('桌面 open-item 打开跨群建议并阻止另一账号路由',async()=>{
      await push(mergeSuggestionSnapshot('merge-desktop'));await nav('tasks');await evaluate('backend.emit({type:"open-item",accountId:"12345",id:"merge-desktop-suggestion"})');
      assert.equal(await evaluate('document.querySelector("#page-title").textContent'),'待确认');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'),'merge-desktop-suggestion');
      assert.match(await evaluate('document.querySelector(".detail-scroll").textContent'),/班级群：周一提交报告/);
      await evaluate('backend.emit({type:"open-item",accountId:"67890",id:"merge-desktop-suggestion"})');
      assert.equal(await evaluate('document.querySelector(".task-row.selected").dataset.id'),'merge-desktop-suggestion');
      assert.match(await body(),/账号.*不一致/);
    });
    await t.test('建议保存超时锁定原请求，重复提交确认不切换字段',async()=>{
      await push(mergeSuggestionSnapshot('merge-unknown'));await nav('pending');await select('merge-unknown-suggestion');await click('[data-action=open-merge-suggestion]');
      for(const name of ['titleTaskId','courseTaskId','timeTaskId','priorityTaskId','stateTaskId'])await change('#merge-task-form [name='+name+']','merge-unknown-first');
      await evaluate('window.originalSuggestionSetTimeout=window.setTimeout;window.setTimeout=(handler,ms,...args)=>{const id=window.originalSuggestionSetTimeout(handler,ms,...args);if(ms===15000)window.suggestionTimeout={handler,id};return id;};backend.hold=true');
      try{
        await click('[data-action=merge-tasks]');const first=await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").at(-1)');
        await evaluate('clearTimeout(window.suggestionTimeout.id);window.suggestionTimeout.handler()');
        assert.match(await evaluate('document.querySelector("#structure-error").textContent'),/尚未确认/);
        assert.equal(await evaluate('document.querySelector("#merge-task-form [name=titleTaskId]").disabled'),true);
        await change('#merge-task-form [name=titleTaskId]','merge-unknown-second');await click('[data-action=merge-tasks]');
        assert.deepEqual(await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").at(-1).parameters'),first.parameters);
        await evaluate('backend.release()');await nav('pending');assert.equal(await evaluate('document.querySelectorAll(".task-row").length'),0);
      }finally{await evaluate('window.setTimeout=window.originalSuggestionSetTimeout;backend.release()');}
    });
    await t.test('建议合并选择按账号隔离，旧账号迟到保存不清新账号草稿',async()=>{
      const accountA=mergeSuggestionSnapshot('merge-account'),accountB=mergeSuggestionSnapshot('merge-account','67890');
      await push(accountA);await nav('pending');await select('merge-account-suggestion');await click('[data-action=open-merge-suggestion]');
      for(const name of ['titleTaskId','courseTaskId','timeTaskId','priorityTaskId','stateTaskId'])await change('#merge-task-form [name='+name+']','merge-account-first');
      await evaluate('backend.hold=true');await click('[data-action=merge-tasks]');const first=await evaluate('backend.messages.filter(message=>message.operation==="merge-tasks").at(-1)');
      await push(accountB);await nav('pending');await select('merge-account-suggestion');await click('[data-action=open-merge-suggestion]');await change('#merge-task-form [name=titleTaskId]','merge-account-second');
      await evaluate('backend.held=backend.held.filter(message=>message.id!=='+JSON.stringify(first.id)+');backend.emit({id:'+JSON.stringify(first.id)+',success:true,snapshot:'+JSON.stringify({...accountA,generatedAt:'2099-01-01T00:00:00Z'})+'});backend.release()');
      assert.equal(await evaluate('document.querySelector("#merge-task-form [name=titleTaskId]").value'),'merge-account-second');
      assert.equal(await evaluate('document.querySelector("#merge-task-form [name=titleTaskId]").disabled'),false);
      await click('[data-action=keep-separate]');const saved=await evaluate('backend.messages.filter(message=>message.operation==="keep-separate").at(-1)');
      assert.equal(saved.parameters.accountId,'67890');assert.notEqual(saved.parameters.operationId,first.parameters.operationId);
    });
    await t.test('消息缓存设置说明最近七天窗口、长期证据及处理记录，按实际结果显示北京时间',async()=>{
      const cleanup={accountId:'12345',completedAt:'2026-10-13T02:30:00Z',cutoff:'2026-10-06T02:30:00Z',deletedMessages:12,retainedOldMessages:3,remainingMessages:15};
      await push({...structuredClone(fixture),cacheCleanup:cleanup,cacheCleanupError:null});await nav('settings');
      assert.equal(await evaluate('!!document.querySelector("[data-action=clean-cache]")'),true);
      assert.match(await evaluate('document.querySelector("#settings-panel").textContent'),/最近\s*7\s*天/);
      assert.match(await evaluate('document.querySelector("#settings-panel").textContent'),/证据.*处理记录.*长期保留/);
      assert.match(await evaluate('document.querySelector("#cache-cleanup-result").textContent'),/2026-10-13 10:30/);
      assert.match(await evaluate('document.querySelector("#cache-cleanup-result").textContent'),/实际删除.*12.*条/);
      assert.doesNotMatch(await evaluate('document.querySelector("#cache-cleanup-result").textContent'),/释放.*磁盘|图片已清理/);
    });
    await t.test('清理入口在没有账号时禁用，使用当前账号和固定缓存ID，忙碌时阻止重复提交',async()=>{
      await push({...structuredClone(fixture),accountId:'',cacheCleanup:null,cacheCleanupError:null});await nav('settings');
      assert.equal(await evaluate('document.querySelector("[data-action=clean-cache]").disabled'),true);
      await push({...structuredClone(fixture),cacheCleanup:null,cacheCleanupError:null});await nav('settings');
      assert.match(await evaluate('document.querySelector("#cache-cleanup-result").textContent'),/尚无成功清理/);
      await evaluate('backend.hold=true');await click('[data-action=clean-cache]');
      const command=await evaluate('backend.messages.filter(message=>message.operation==="clean-cache").at(-1)');
      assert.deepEqual(command.parameters,{accountId:'12345',generation:0,id:'cache',operationId:command.parameters.operationId,version:0});
      assert.match(command.parameters.operationId,/\S/);
      assert.equal(await evaluate('document.querySelector("[data-action=clean-cache]").disabled'),true);
      assert.match(await evaluate('document.querySelector("[data-action=clean-cache]").textContent'),/正在清理/);
      const count=await evaluate('backend.messages.filter(message=>message.operation==="clean-cache").length');await click('[data-action=clean-cache]');
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="clean-cache").length'),count);
      await evaluate('backend.release()');
      assert.equal(await evaluate('document.querySelector("[data-action=clean-cache]").disabled'),false);
      assert.match(await evaluate('document.querySelector("#cache-cleanup-result").textContent'),/实际删除.*12.*条/);
    });
    await t.test('清理失败保留上次成功记录、课程草稿及焦点，重试成功清除独立错误',async()=>{
      await nav('settings');await change('[data-group-id="100"] input[name=course]','清理时的课程草稿');
      await evaluate('document.querySelector("[data-group-id=\\"100\\"] input[name=course]").focus();backend.failNext="本地消息缓存清理失败，原数据已保留。"');
      await click('[data-action=clean-cache]');
      assert.equal(await evaluate('document.querySelector("#cache-cleanup-error").hidden'),false);
      assert.match(await evaluate('document.querySelector("#cache-cleanup-error").textContent'),/清理失败/);
      assert.match(await evaluate('document.querySelector("#cache-cleanup-result").textContent'),/实际删除.*12.*条/);
      assert.equal(await evaluate('document.querySelector("[data-group-id=\\"100\\"] input[name=course]").value'),'清理时的课程草稿');
      assert.equal(await evaluate('document.activeElement.name'),'course');
      await click('[data-action=clean-cache]');assert.equal(await evaluate('document.querySelector("#cache-cleanup-error").hidden'),true);
    });
    await t.test('清理超时释放按钮并保留结果，迟到响应不覆盖后续成功清理',async()=>{
      await evaluate('window.originalCacheSetTimeout=window.setTimeout;window.setTimeout=(handler,ms,...args)=>{const id=window.originalCacheSetTimeout(handler,ms,...args);if(ms===15000)window.cacheTimeout={handler,id};return id;};backend.hold=true');
      try {
        await click('[data-action=clean-cache]');const late=await evaluate('backend.messages.filter(message=>message.operation==="clean-cache").at(-1)');
        await evaluate('clearTimeout(window.cacheTimeout.id);window.cacheTimeout.handler()');
        assert.equal(await evaluate('document.querySelector("[data-action=clean-cache]").disabled'),false);
        assert.match(await evaluate('document.querySelector("#cache-cleanup-error").textContent'),/尚未确认|暂未响应/);
        assert.match(await evaluate('document.querySelector("#cache-cleanup-result").textContent'),/实际删除.*12.*条/);
        await evaluate('backend.held=[];backend.hold=false;backend.cleanupDeleted=0');await click('[data-action=clean-cache]');
        assert.match(await evaluate('document.querySelector("#cache-cleanup-result").textContent'),/实际删除.*0.*条/);
        const stale={...structuredClone(fixture),generatedAt:'2099-01-01T00:00:00Z',cacheCleanup:{accountId:'12345',completedAt:'2099-01-01T00:00:00Z',deletedMessages:999}};
        await evaluate(`backend.emit({id:${JSON.stringify(late.id)},success:true,snapshot:${JSON.stringify(stale)}})`);
        assert.match(await evaluate('document.querySelector("#cache-cleanup-result").textContent'),/实际删除.*0.*条/);
        assert.equal(await evaluate('document.querySelector("#cache-cleanup-error").hidden'),true);
      }finally{await evaluate('window.setTimeout=window.originalCacheSetTimeout;backend.cleanupDeleted=12;backend.release()');}
    });
    await t.test('旧账号清理响应不切换新账号或占用按钮，清理结果不清除当前任务编辑草稿',async()=>{
      await push({...structuredClone(fixture),cacheCleanup:null,cacheCleanupError:null});await nav('tasks');await select('task1');await change('#time-form [name=title]','清理后保留的编辑草稿');await nav('settings');
      await evaluate('backend.hold=true');await click('[data-action=clean-cache]');await nav('tasks');await select('task1');
      await evaluate('document.querySelector("#time-form [name=title]").focus();document.querySelector("#time-form [name=title]").setSelectionRange(1,3);backend.release()');
      assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'),'清理后保留的编辑草稿');
      assert.equal(await evaluate('document.activeElement.name'),'title');assert.equal(await evaluate('document.activeElement.selectionStart'),1);
      await nav('settings');await evaluate('backend.hold=true');await click('[data-action=clean-cache]');
      const late=await evaluate('backend.messages.filter(message=>message.operation==="clean-cache").at(-1)');
      const accountB={...structuredClone(fixture),accountId:'67890',cacheCleanup:null,cacheCleanupError:'本次自动清理失败，原数据已保留。'};
      await push(accountB);await nav('settings');assert.equal(await evaluate('document.querySelector("[data-action=clean-cache]").disabled'),false);
      await change('[data-group-id="100"] input[name=course]','新账号课程草稿');
      await evaluate(`backend.held=backend.held.filter(message=>message.id!==${JSON.stringify(late.id)});backend.emit({id:${JSON.stringify(late.id)},success:true,snapshot:${JSON.stringify({...fixture,generatedAt:'2099-01-01T00:00:00Z',cacheCleanup:{accountId:'12345',deletedMessages:888}})}});backend.release()`);
      assert.match(await evaluate('document.querySelector("#cache-cleanup-result").textContent'),/尚无成功清理/);
      assert.match(await evaluate('document.querySelector("#cache-cleanup-error").textContent'),/自动清理失败/);
      assert.equal(await evaluate('document.querySelector("[data-group-id=\\"100\\"] input[name=course]").value'),'新账号课程草稿');
      await click('[data-action=clean-cache]');assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="clean-cache").at(-1).parameters.accountId'),'67890');
    });
    await t.test('手动清理失败后新的自动成功清除本地错误，重复上次成功记录保留新错误',async()=>{
      const previous={accountId:'12345',completedAt:new Date(Date.now()-60000).toISOString(),deletedMessages:4};
      const current={...structuredClone(fixture),cacheCleanup:previous,cacheCleanupError:null};
      await push(current);await nav('settings');await evaluate('backend.failNext="本地消息缓存清理失败，原数据已保留。"');await click('[data-action=clean-cache]');
      assert.match(await evaluate('document.querySelector("#cache-cleanup-error").textContent'),/清理失败/);
      await push(current);assert.equal(await evaluate('document.querySelector("#cache-cleanup-error").hidden'),false,'重复旧成功结果不能清除新错误');
      const completed={...current,cacheCleanup:{...previous,completedAt:new Date(Date.now()+1000).toISOString(),deletedMessages:5}};
      await push(completed);
      assert.equal(await evaluate('document.querySelector("#cache-cleanup-error").hidden'),true,'新的自动成功应清除对应账号本地错误');
      assert.match(await evaluate('document.querySelector("#cache-cleanup-result").textContent'),/实际删除.*5.*条/);
    });
    await t.test('清理超时错误只由本账号请求之后的新成功解除，过期快照和另一账号结果不能解除',async()=>{
      const current={...structuredClone(fixture),cacheCleanup:null,cacheCleanupError:null};
      await push(current);await nav('settings');
      await evaluate('window.originalCacheRecoverySetTimeout=window.setTimeout;window.setTimeout=(handler,ms,...args)=>{const id=window.originalCacheRecoverySetTimeout(handler,ms,...args);if(ms===15000)window.cacheRecoveryTimeout={handler,id};return id;};backend.hold=true');
      try {
        await click('[data-action=clean-cache]');await evaluate('clearTimeout(window.cacheRecoveryTimeout.id);window.cacheRecoveryTimeout.handler();backend.held=[];backend.hold=false');
        const newer={accountId:'12345',completedAt:new Date(Date.now()+2000).toISOString(),deletedMessages:6};
        await evaluate(`backend.emit({type:'state',snapshot:${JSON.stringify({...current,generatedAt:'2000-01-01T00:00:00Z',cacheCleanup:newer})}})`);
        assert.equal(await evaluate('document.querySelector("#cache-cleanup-error").hidden'),false,'过期快照不能清除错误');
        await push({...current,cacheCleanup:{...newer,accountId:'67890'}});
        assert.equal(await evaluate('document.querySelector("#cache-cleanup-error").hidden'),false,'另一账号的清理结果不能清除错误');
        await push({...current,accountId:'67890',cacheCleanup:{...newer,accountId:'67890'}});await nav('settings');
        assert.equal(await evaluate('document.querySelector("#cache-cleanup-error").hidden'),true);
        await push(current);await nav('settings');
        assert.equal(await evaluate('document.querySelector("#cache-cleanup-error").hidden'),false,'账号切换不会删除原账号错误');
        await push({...current,cacheCleanup:{...newer,completedAt:new Date(Date.now()-60000).toISOString()}});
        assert.equal(await evaluate('document.querySelector("#cache-cleanup-error").hidden'),false,'请求之前的成功不能解除结果未知');
        await push({...current,cacheCleanup:newer});
        assert.equal(await evaluate('document.querySelector("#cache-cleanup-error").hidden'),true);
      }finally{await evaluate('window.setTimeout=window.originalCacheRecoverySetTimeout;backend.release()');}
    });
    await t.test('备份设置导出并展示不完整原图范围，预览必须显式确认整份覆盖',async()=>{
      await push({...structuredClone(fixture),generation:0});await nav('settings');
      await change('#theme-setting','light');await change('#motion-setting','reduce');
      assert.ok(await evaluate('!!document.querySelector("[data-action=export-backup]")'));
      await click('[data-action=export-backup]');
      assert.match(await evaluate('document.querySelector("#backup-result").textContent'),/不完整.*2.*原图/);
      const command=await evaluate('backend.messages.filter(message=>message.operation==="export-backup").at(-1)');
      assert.deepEqual(command.parameters,{accountId:'12345',generation:0,theme:'light',motion:'reduce'});
      await click('[data-action=inspect-backup]');
      assert.match(await evaluate('document.querySelector("#backup-preview").textContent'),/12345.*全部.*历史.*自动启动.*布局.*主题/s);
      assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="restore-backup").length'),0);
      await click('[data-action=cancel-backup]');
      assert.equal(await evaluate('!!document.querySelector("[data-action=restore-backup]")'),false);
      await evaluate('backend.cancelFile=true');await click('[data-action=inspect-backup]');
      assert.equal(await evaluate('!!document.querySelector("[data-action=restore-backup]")'),false);
    });
    await t.test('恢复超时保持覆盖锁，成功刷新清除旧草稿并恢复偏好，迟到旧快照不能倒回',async()=>{
      await nav('tasks');await select('task1');await change('#time-form [name=title]','恢复前草稿');
      const oldSnapshot=await evaluate('structuredClone(backend.snapshot)');
      await nav('settings');await click('[data-action=inspect-backup]');await evaluate('backend.hold=true');
      await evaluate('window.originalBackupSetTimeout=window.setTimeout;window.setTimeout=(handler,ms,...args)=>{const id=window.originalBackupSetTimeout(handler,ms,...args);if(ms===15000)window.backupTimeout={id,handler};return id}');
      try{
        await click('[data-action=restore-backup]');await evaluate('clearTimeout(window.backupTimeout.id);window.backupTimeout.handler()');
        assert.equal(await evaluate('document.querySelector("[data-action=restore-backup]").disabled'),true);
        assert.match(await evaluate('document.querySelector("#backup-error").textContent'),/尚未确认.*等待/);
        await nav('tasks');await select('task1');assert.equal(await evaluate('document.querySelector("[data-action=complete]").disabled'),true);
        await evaluate('backend.release()');await nav('tasks');await select('task1');
        assert.equal(await evaluate('document.querySelector("#time-form [name=title]").value'),'实验报告');
        assert.equal(await evaluate('JSON.parse(localStorage.getItem("message-purify-preferences")).theme'),'light');
        await evaluate(`backend.emit({type:'state',snapshot:${JSON.stringify({...oldSnapshot,generatedAt:'2099-01-01T00:00:00Z'})}})`);
        await click('[data-action=complete]');
        assert.equal(await evaluate('backend.messages.filter(message=>message.operation==="complete").at(-1).parameters.generation'),1);
        await nav('settings');assert.match(await evaluate('document.querySelector("#backup-result").textContent'),/恢复前备份/);
      }finally{await evaluate('window.setTimeout=window.originalBackupSetTimeout;backend.release()');}
    });
    await t.test('旧账号备份预览应答不能显示到新账号，恢复失败保留原数据及独立错误',async()=>{
      await push({...structuredClone(fixture),generation:1});await nav('settings');await evaluate('backend.hold=true');await click('[data-action=inspect-backup]');
      const late=await evaluate('backend.held.at(-1)');await evaluate('backend.held=[];backend.hold=false');
      await push({...structuredClone(fixture),accountId:'67890',generation:0});await nav('settings');
      await evaluate(`backend.emit({id:${JSON.stringify(late.id)},success:true,backupPreview:{id:'old',generation:1,preview:{...backend.backupResult,accountId:'12345'}}})`);
      assert.equal(await evaluate('!!document.querySelector("[data-action=restore-backup]")'),false);
      await push({...structuredClone(fixture),generation:1});await nav('settings');await click('[data-action=inspect-backup]');
      await evaluate('backend.failNext="备份校验失败，原数据已保留。"');await click('[data-action=restore-backup]');
      assert.match(await evaluate('document.querySelector("#backup-error").textContent'),/校验失败.*原数据已保留/);
      assert.equal(await evaluate('backend.snapshot.items.find(item=>item.id==="task1").title'),'实验报告');
    });
    await t.test('宿主一次性恢复的界面偏好写入本地，后续推送保留用户新选择',async()=>{
      await nav('settings');await evaluate('backend.emit({type:"state",preferences:{theme:"dark",motion:"reduce"},snapshot:backend.snapshot})');
      assert.equal(await evaluate('document.documentElement.dataset.theme'),'dark');
      assert.deepEqual(await evaluate('JSON.parse(localStorage.getItem("message-purify-preferences"))'),{theme:'dark',motion:'reduce'});
      await change('#theme-setting','system');await change('#motion-setting','system');
      const appearance=await evaluate('backend.messages.filter(message=>message.operation==="set-appearance").at(-1).parameters');
      assert.equal(appearance.preferenceTheme,'system');assert.equal(appearance.preferenceMotion,'system');
      await evaluate('backend.push()');
      assert.equal(await evaluate('document.querySelector("#theme-setting").value'),'system');
      assert.equal(await evaluate('document.querySelector("#motion-setting").value'),'system');
    });
    await t.test('透明度滑块即时同步、保存偏好，恢复旧备份时回到主题默认值', async () => {
      await nav('settings');
      assert.equal(await evaluate('document.querySelector("#desktop-transparency-setting").min'), '0');
      assert.equal(await evaluate('document.querySelector("#desktop-transparency-setting").max'), '100');
      await evaluate('document.querySelector("#desktop-transparency-setting").value=60;document.querySelector("#desktop-transparency-setting").dispatchEvent(new Event("input",{bubbles:true}))');
      await waitFor('backend.appearance?.desktopTransparency===60');
      assert.equal(await evaluate('document.querySelector("#desktop-transparency-value").value'), '60%');
      assert.equal(await evaluate('JSON.parse(localStorage.getItem("message-purify-preferences")).desktopTransparency'), 60);
      await evaluate('document.querySelector("#desktop-transparency-setting").scrollIntoView({block:"center"})');
      const slider = await evaluate('(() => { const slider=document.querySelector("#desktop-transparency-setting");slider.dataset.dragCheck="active";const rect=slider.getBoundingClientRect();return {x:rect.x,y:rect.y,width:rect.width,height:rect.height}; })()');
      await call('Input.dispatchMouseEvent', { type:'mousePressed', x:slider.x+slider.width*0.6, y:slider.y+slider.height/2, button:'left', clickCount:1 });
      await call('Input.dispatchMouseEvent', { type:'mouseMoved', x:slider.x+slider.width*0.8, y:slider.y+slider.height/2, button:'left', buttons:1 });
      await waitFor('backend.appearance.desktopTransparency > 70');
      assert.equal(await evaluate('document.querySelector("#desktop-transparency-setting").dataset.dragCheck'), 'active');
      const dragged = await evaluate('Number(document.querySelector("#desktop-transparency-setting").value)');
      await call('Input.dispatchMouseEvent', { type:'mouseReleased', x:slider.x+slider.width*0.8, y:slider.y+slider.height/2, button:'left', clickCount:1 });
      await change('#theme-setting', 'light');
      assert.equal(await evaluate('backend.appearance.desktopTransparency'), dragged);
      await evaluate('backend.emit({type:"state",preferences:{theme:"dark",motion:"reduce",desktopTransparency:35},snapshot:backend.snapshot})');
      assert.equal(await evaluate('document.querySelector("#desktop-transparency-setting").value'), '35');
      await evaluate('backend.emit({type:"state",preferences:{theme:"dark",motion:"reduce",desktopTransparency:null},snapshot:backend.snapshot})');
      assert.equal(await evaluate('document.querySelector("#desktop-transparency-setting").value'), '20');
      assert.equal(await evaluate('JSON.parse(localStorage.getItem("message-purify-preferences")).desktopTransparency ?? null'), null);
    });
    assert.deepEqual(browserErrors, []);
  } finally {
    socket?.close(); browser.kill();
    await new Promise(resolve => { if (browser.exitCode !== null) resolve(); else { const timer = setTimeout(resolve, 3000); browser.once('exit', () => { clearTimeout(timer); resolve(); }); } });
    // Chrome can hold profile files briefly after exit. The temporary profile is safe to leave on failure.
    if (path.dirname(path.resolve(profile)) === path.resolve(os.tmpdir()) && path.basename(profile).startsWith('messagepurify-app-qa-')) {
      try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 2, retryDelay: 100 }); } catch {}
    }
  }
});
