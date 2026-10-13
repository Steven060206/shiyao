'use strict';

const $ = id => document.getElementById(id);
const html = value => String(value ?? '').replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character]));
const pages = { tasks: '任务', pending: '待确认', notices: '通知', completed: '已完成', settings: '设置' };
const states = { active: '待办', converted: '已转为任务', completed: '已完成', archived: '已归档', cancelled: '已取消', pending: '待确认', ignored: '已忽略', rejected: '已拒绝', accepted: '已确认', merged: '已合并' };
const descriptions = { tasks: '保留待办，直到完成或归档', pending: '核对原文，补齐信息后再确认', notices: '无需完成的群消息', completed: '保留完成记录和消息来源', settings: '连接状态、课程关联与界面偏好' };
const preferencesKey = 'message-purify-preferences';
const preferences = { theme: 'system', motion: 'system' };
try {
  const saved = JSON.parse(localStorage.getItem(preferencesKey));
  if (['system', 'light', 'dark'].includes(saved?.theme)) preferences.theme = saved.theme;
  if (['system', 'reduce'].includes(saved?.motion)) preferences.motion = saved.motion;
} catch {}
const systemTheme = matchMedia('(prefers-color-scheme:dark)');
const systemMotion = matchMedia('(prefers-reduced-motion:reduce)');
function applyPreferences() {
  document.documentElement.dataset.theme = preferences.theme === 'system' ? (systemTheme.matches ? 'dark' : 'light') : preferences.theme;
  document.documentElement.dataset.motion = preferences.motion;
}
applyPreferences();
requestAnimationFrame(() => requestAnimationFrame(() => { document.documentElement.dataset.ready = 'true'; }));
function syncAppearance() {
  applyPreferences();
  request('set-appearance', { theme: document.documentElement.dataset.theme, reducedMotion: preferences.motion === 'reduce' || systemMotion.matches });
}
systemTheme.addEventListener('change', syncAppearance);
systemMotion.addEventListener('change', syncAppearance);

let snapshot = null, status = null, startupEnabled = true, page = 'tasks', selectedId = null, initialized = false;
let desktop = { state: 'waiting', error: null }, pendingOpenItem = null;
let filters = { search: '', course: '', state: 'active' }, detailSignature = '', settingsSignature = '', lastFilterPage = null;
let detailError = null, hostError = '', toastTimer, undoTimer, undoScheduledFor;
let manualOpen = false, structureOpen = null, mergedSourceId = null;
const requests = new Map(), busy = new Set(), drafts = new Map(), manualDrafts = new Map(), structureDrafts = new Map(), groupDrafts = new Map(), readAttempts = new Set();
const bridge = window.chrome?.webview;
const uuid = () => crypto.randomUUID();
const items = () => snapshot?.items || [];
const selected = () => items().find(item => item.id === selectedId);
const loadedAccount = () => typeof snapshot?.accountId === 'string' && Boolean(snapshot.accountId.trim());
const manualDraft = () => manualDrafts.get(snapshot?.accountId);
const structureKey = (accountId, id, mode) => `${accountId}/${id}/${mode}`;
const structureDraft = () => structureOpen && structureOpen.accountId === snapshot?.accountId ? structureDrafts.get(structureKey(structureOpen.accountId, structureOpen.id, structureOpen.mode)) : null;
const structureBusyKey = (accountId, id) => `${accountId}/${id}/structure`;
const structureOperations = ['merge-tasks', 'split-task', 'keep-separate'];
const linkedWithoutDate = item => Boolean(item.targetTaskId && ['cancel', 'remind'].includes(item.action));
const localDate = () => new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
const past = item => Boolean(item.due && (item.time ? new Date(`${item.due}T${item.time}:00+08:00`).getTime() < Date.now() : item.due < localDate()));
const timeLabel = item => item.timeKind === 'start' ? '开始时间' : '截止时间';
const dateLabel = item => item.due ? `${item.due}${item.time ? ' ' + item.time : ' · 仅有日期'}` : '日期待确认';
const canUndo = item => item.undoOperationId && item.undoExpiresAt && new Date(item.undoExpiresAt).getTime() > Date.now();
function errorLabel(error) {
  const code = typeof error === 'string' ? error : error?.code;
  const known = {
    version_conflict: '事项已有变化，请核对最新内容后重新确认。',
    stale_version: '事项已有变化，请核对最新内容后重新确认。',
    account_mismatch: 'QQ 账号不一致，接收已暂停。请核对当前登录账号。',
    undo_expired: '撤销期限已过，当前状态已保留。',
    operation_conflict: '这次操作与之前的请求冲突，请刷新后重试。',
    invalid_time: '请填写有效日期和时间。',
    command_timeout: '本地应用暂未响应，草稿已保留，请稍后刷新。',
    bridge_unavailable: '页面尚未连接本地应用。请从消息提纯应用打开。',
    history_timeout: '历史读取超时',
    connection_failed: '连接失败',
    qq_offline: 'QQ 离线',
    game_active: '游戏运行中，同步已延后；退出游戏后合并补同步一次。',
    game_catalog_unavailable: '游戏名单尚未准备好，自动同步已暂停。',
    pinned_groups_unavailable: '暂时无法读取置顶群范围',
    pinned_state_unavailable: '暂时无法读取置顶群范围，消息接收已暂停。',
    connection_unavailable: '暂未连接 QQ，请确认电脑上的 QQ 已登录。',
    napcat_configuration_invalid: 'QQ 连接配置无法识别，请核对本地接口配置。',
    napcat_configuration_unavailable: 'QQ 连接配置暂时无法读取，连接已暂停，请检查文件访问权限。',
    napcat_configuration_ambiguous: '检测到多个启用的 QQ 连接配置，连接已暂停，请核对当前接口。',
    startup_registration_unavailable: '自动启动设置无法保存，请检查当前用户的注册表权限。',
    startup_settings_unavailable: '自动启动设置无法保存，本地设置文件未能写入。',
    history_empty_page: '历史消息未继续返回，本群尚未完成补漏。',
    history_no_progress: '历史读取未取得新消息，本群尚未完成补漏。'
  };
  if (typeof code === 'string' && /[\u3400-\u9fff]/.test(code)) return code;
  if (typeof code === 'string' && code.startsWith('api-failed-')) return 'QQ 接口暂未完成请求，请稍后重试。';
  return known[code] || known[code?.replace?.(/-/g, '_')] || '操作未完成，请核对后重试。';
}
function toast(message) {
  clearTimeout(toastTimer); $('toast').textContent = message; $('toast').hidden = false;
  toastTimer = setTimeout(() => { $('toast').hidden = true; }, 3500);
}
function request(operation, parameters, itemId = null) {
  if (!bridge) { hostError = errorLabel('bridge_unavailable'); renderStatus(); return; }
  const id = uuid();
  const structural = structureOperations.includes(operation);
  const busyKey = structural ? structureBusyKey(parameters.accountId, itemId) : itemId;
  if (busyKey) busy.add(busyKey);
  const structureDraftKey = structural ? structureKey(parameters.accountId, itemId, operation === 'split-task' ? 'split' : 'merge') : null;
  const timer = setTimeout(() => {
    const pending = requests.get(id); if (!pending) return;
    if (pending.structural) {
      pending.timedOut = true;
      const draft = structureDrafts.get(pending.structureDraftKey);
      if (draft?.operationId === pending.structureOperationId) { draft.unconfirmed = true; draft.error = '保存结果尚未确认，草稿已保留。请先重试确认保存，再修改内容。'; }
    } else requests.delete(id);
    releaseBusy(pending);
    if (pending.structural) { /* Retain the request so a late response can confirm this operation. */ }
    else if (pending.operation === 'create-task') {
      const draft = manualDrafts.get(pending.accountId);
      if (draft?.id === pending.itemId) { draft.unconfirmed = true; draft.error = '保存结果尚未确认，草稿已保留。请先重试确认保存，再修改内容。'; }
    } else if (pending.itemId && pending.operation !== 'confirm-group') detailError = { id: pending.itemId, message: errorLabel('command_timeout') };
    else hostError = errorLabel('command_timeout');
    render();
  }, 15000);
  const refreshTarget = operation === 'snapshot' && selected()?.kind === 'task' ? { accountId: snapshot.accountId, id: selectedId, structureDraftKey: structureOpen && structureKey(structureOpen.accountId, structureOpen.id, structureOpen.mode) } : null;
  requests.set(id, { operation, itemId, busyKey, timer, refreshTarget, accountId: parameters.accountId, structural, structureDraftKey, structureOperationId: parameters.operationId });
  try { bridge.postMessage({ id, operation, parameters }); }
  catch {
    clearTimeout(timer); requests.delete(id); if (busyKey) busy.delete(busyKey);
    if (structural && structureDrafts.get(structureDraftKey)) structureDrafts.get(structureDraftKey).error = errorLabel('bridge_unavailable');
    else if (operation === 'create-task' && manualDraft()?.id === itemId) manualDraft().error = errorLabel('bridge_unavailable');
    else hostError = errorLabel('bridge_unavailable');
  }
  updateDetailControls(); renderStatus();
}
function command(operation, item, extra = {}) {
  if (!snapshot || !item || busy.has(item.id)) return;
  detailError = null;
  request(operation, { accountId: snapshot.accountId, id: item.id, operationId: uuid(), version: item.version, ...extra }, item.id);
}
function releaseBusy(pending) {
  if (pending.busyKey && ![...requests.values()].some(request => request.busyKey === pending.busyKey && !request.timedOut)) busy.delete(pending.busyKey);
}
function markRead(item) {
  if (!item?.unread || !snapshot) return;
  const key = `${snapshot.accountId}/${item.id}/${item.version}`;
  if (readAttempts.has(key)) return;
  readAttempts.add(key);
  request('mark-read', { accountId: snapshot.accountId, id: item.id, operationId: uuid(), version: item.version });
}
function openItem(message) {
  if (typeof message.accountId !== 'string' || !message.accountId.trim() || typeof message.id !== 'string' || !message.id.trim()) {
    hostError = '无法打开事项：标识无效。'; render(); return;
  }
  if (!snapshot) { pendingOpenItem = { accountId: message.accountId, id: message.id }; return; }
  pendingOpenItem = null;
  if (snapshot.accountId !== message.accountId) {
    hostError = '此事项所属 QQ 账号与当前账号不一致，请核对账号后再打开。'; render(); return;
  }
  let item = items().find(item => item.id === message.id && ['task', 'review'].includes(item.kind));
  const visited = new Set();
  while (item && (item.state === 'merged' && item.mergedIntoId || item.kind === 'review' && item.state === 'accepted' && item.targetTaskId)) {
    if (visited.has(item.id)) { item = null; break; }
    visited.add(item.id);
    item = items().find(candidate => candidate.id === (item.mergedIntoId || item.targetTaskId));
  }
  if (!item) { hostError = '未找到此事项，请刷新后重试。'; render(); return; }
  manualOpen = false; structureOpen = null; mergedSourceId = null;
  page = item.kind === 'review' ? 'pending' : item.state === 'completed' ? 'completed' : 'tasks';
  filters = { search: '', course: '', state: item.state };
  selectedId = item.id; detailError = null; hostError = '';
  render();
  document.querySelector('.task-row.selected')?.scrollIntoView({ block: 'nearest' });
}
function receive(message) {
  if (!message || typeof message !== 'object') return;
  if (message.type === 'open-item') { openItem(message); return; }
  const pending = requests.get(message.id);
  if (pending) {
    clearTimeout(pending.timer); requests.delete(message.id);
    releaseBusy(pending);
    if (pending.operation === 'set-appearance') {
      if (!message.success) { hostError = errorLabel(message.error); renderStatus(); }
      return;
    }
    if (pending.operation === 'set-autostart') {
      if (!message.success) hostError = errorLabel(message.error);
      else startupEnabled = Boolean(message.startupEnabled);
      render();
      return;
    }
    if (pending.structural) {
      const draft = structureDrafts.get(pending.structureDraftKey);
      pending.openStructure = structureOpen && pending.structureDraftKey === structureKey(structureOpen.accountId, structureOpen.id, structureOpen.mode) && selectedId === pending.itemId;
      if (draft?.operationId === pending.structureOperationId) {
        if (message.success) {
          structureDrafts.delete(pending.structureDraftKey);
          for (const [id, retry] of requests) if (retry.structural && retry.accountId === pending.accountId && retry.structureOperationId === pending.structureOperationId) { clearTimeout(retry.timer); requests.delete(id); }
          busy.delete(pending.busyKey);
        } else {
          const freshFailure = message.snapshot?.accountId === pending.accountId && Date.parse(message.snapshot.generatedAt) >= Date.parse(snapshot?.generatedAt || message.snapshot.generatedAt);
          const payload = JSON.parse(draft.payloadSignature);
          const possiblySaved = Array.isArray(message.snapshot?.items) && (pending.operation === 'split-task' ? message.snapshot.items.some(item => item.id === payload.newItemId)
            : pending.operation === 'merge-tasks' ? message.snapshot.items.some(item => item.id === payload.mergeWithId && item.state === 'merged' && item.mergedIntoId === pending.itemId)
              : !['version_conflict', 'stale_version'].includes(message.error) && message.snapshot.items.some(item => item.id === pending.itemId && item.version > payload.version));
          draft.unconfirmed = !freshFailure || possiblySaved || ['command_timeout', 'operation_conflict'].includes(message.error);
          draft.refresh = !draft.unconfirmed && ['version_conflict', 'stale_version'].includes(message.error);
          draft.error = draft.unconfirmed ? '保存结果尚未确认，草稿已保留。请先重试确认保存，再修改内容。' : draft.refresh ? '事项已有变化，合并或拆分草稿已保留。请刷新并核对最新内容后再保存。' : errorLabel(message.error);
        }
      }
      if (pending.accountId !== snapshot?.accountId) { render(); return; }
    }
    if (pending.operation === 'snapshot' && pending.refreshTarget && pending.refreshTarget.accountId !== snapshot?.accountId) { render(); return; }
    if (pending.operation === 'create-task') {
      const draft = manualDrafts.get(pending.accountId);
      pending.openCreation = manualOpen && page === 'tasks' && pending.accountId === snapshot?.accountId && draft?.id === pending.itemId;
      if (draft?.id === pending.itemId) {
        if (message.success) manualDrafts.delete(pending.accountId);
        else {
          const freshFailure = message.snapshot?.accountId === pending.accountId && Array.isArray(message.snapshot.items)
            && Date.parse(message.snapshot.generatedAt) >= Date.parse(snapshot?.generatedAt || message.snapshot.generatedAt);
          draft.unconfirmed = !freshFailure || message.snapshot.items.some(item => item.id === pending.itemId);
          draft.error = draft.unconfirmed ? '保存结果尚未确认，草稿已保留。请先重试确认保存，再修改内容。' : errorLabel(message.error);
        }
      }
      if (pending.accountId !== snapshot?.accountId) { render(); return; }
    }
    if (pending.structural) { /* The operation's own draft carries its error and retry state. */ }
    else if (!message.success) {
      if (pending.operation === 'mark-read') { /* Retry only when another item version is viewed. */ }
      else if (pending.operation === 'create-task') { /* Keep the account's manual draft for retry. */ }
      else if (pending.itemId && pending.operation !== 'confirm-group') detailError = { id: pending.itemId, message: pending.operation === 'edit-task' && message.error === 'version_conflict' ? '事项已有变化，编辑草稿已保留。请刷新并核对最新内容后再保存。' : errorLabel(message.error), refreshEdit: pending.operation === 'edit-task' && message.error === 'version_conflict' };
      else hostError = errorLabel(message.error);
    } else if (pending.operation !== 'mark-read') {
      if (pending.operation !== 'create-task' || pending.openCreation) {
        hostError = '';
        if (pending.operation !== 'snapshot') detailError = null;
      }
      if (pending.itemId && pending.operation !== 'create-task') {
        drafts.delete(pending.itemId);
        if (pending.operation === 'confirm-group') groupDrafts.delete(pending.itemId);
        toast({ complete: '已完成，可在已完成记录中撤销', ignore: '已忽略，可在已忽略记录中撤销', 'accept-review': '已确认', 'accept-time': '正式时间已保存', 'edit-task': '任务修改已保存', 'convert-notice': '已转为任务，可在待办中查看', 'confirm-group': '群课程关联已保存' }[pending.operation] || '已保存');
      }
    }
  } else if (message.type !== 'state') return;
  if (message.type === 'state' && message.error) hostError = errorLabel(message.error);
  if (['attached', 'waiting', 'unavailable'].includes(message.desktop?.state)) desktop = message.desktop;
  if (message.status && (!status || Date.parse(message.status.updatedAt) >= Date.parse(status.updatedAt) || !status.updatedAt)) status = message.status;
  if (typeof message.startupEnabled === 'boolean') startupEnabled = message.startupEnabled;
  if (message.snapshot) {
    const incoming = message.snapshot;
    const older = snapshot && Date.parse(incoming.generatedAt) < Date.parse(snapshot.generatedAt);
    if (!older) {
      if (snapshot && snapshot.accountId !== incoming.accountId) {
        drafts.clear(); groupDrafts.clear(); readAttempts.clear(); selectedId = null; initialized = false;
        manualOpen = false; structureOpen = null; mergedSourceId = null; detailError = null;
      }
      snapshot = incoming;
      if (!initialized) {
        page = items().some(item => item.kind === 'review' && item.state === 'pending') ? 'pending' : 'tasks';
        filters.state = page === 'pending' ? 'pending' : 'active'; initialized = true;
      }
    }
  }
  if (pending?.operation === 'create-task' && message.success && pending.accountId === snapshot?.accountId) {
    if (pending.openCreation) { manualOpen = false; page = 'tasks'; filters = { search: '', course: '', state: 'active' }; selectedId = pending.itemId; }
    toast('手动任务已添加');
  }
  if (pending?.structural && message.success && pending.accountId === snapshot?.accountId) {
    toast({ 'merge-tasks': '重复事项已合并', 'split-task': '已拆出关联事项', 'keep-separate': '已保留为独立事项' }[pending.operation]);
    if (pending.openStructure) { openItem({ accountId: pending.accountId, id: pending.itemId }); return; }
  }
  if (pending?.operation === 'snapshot' && message.success && pending.refreshTarget && pending.refreshTarget.accountId === snapshot?.accountId) {
    const item = items().find(item => item.id === pending.refreshTarget.id), draft = item && drafts.get(item.id);
    if (item?.kind === 'task' && draft) draft.version = item.version;
    const structuralDraft = structureDrafts.get(pending.refreshTarget.structureDraftKey);
    if (item?.kind === 'task' && structuralDraft && !structuralDraft.unconfirmed) {
      structuralDraft.version = item.version;
      const other = items().find(candidate => candidate.id === structuralDraft.mergeWithId);
      if (other?.kind === 'task' && other.state !== 'merged') structuralDraft.mergeWithVersion = other.version;
      if (structuralDraft.evidence) {
        const selectedEvidence = structuralDraft.evidenceIndexes.map(index => JSON.stringify(structuralDraft.evidence[index]));
        structuralDraft.evidence = item.evidence || [];
        structuralDraft.evidenceIndexes = structuralDraft.evidence.map((source, index) => selectedEvidence.includes(JSON.stringify(source)) ? index : -1).filter(index => index >= 0);
      }
      structuralDraft.refresh = false; structuralDraft.error = ''; detailSignature = '';
    }
    if (detailError?.id === item?.id) detailError = null;
  }
  if (pendingOpenItem && snapshot) { openItem(pendingOpenItem); return; }
  render();
}
bridge?.addEventListener('message', event => receive(event.data));

function pageItems() {
  const search = filters.search.trim().toLocaleLowerCase();
  return items().filter(item => {
    if (item.state === 'merged' || item.kind === 'review' && item.state === 'accepted') return false;
    const belongs = page === 'tasks' ? item.kind === 'task' && item.state !== 'completed'
      : page === 'pending' ? item.kind === 'review'
        : page === 'completed' ? item.kind === 'task' && item.state === 'completed'
          : page === 'notices' ? item.kind === 'notice' : false;
    const stateMatch = page === 'completed' || filters.state === 'all' || item.state === filters.state || page === 'notices' && filters.state === 'active' && item.state === 'converted';
    return belongs && stateMatch
      && (!filters.course || item.course === filters.course)
      && (!search || [item.title, item.course, item.group, states[item.state]].some(value => String(value || '').toLocaleLowerCase().includes(search)));
  }).sort((a, b) => (a.priority ?? 4) - (b.priority ?? 4) || String(a.due || '9999').localeCompare(String(b.due || '9999')) || String(a.time || '').localeCompare(String(b.time || '')) || String(a.title).localeCompare(String(b.title), 'zh-CN'));
}
function renderStatus() {
  const connected = status?.transport === 'connected';
  $('connection-status').textContent = !status ? '等待连接' : status.transport === 'disconnected' ? '连接已断开' : status.transport === 'connecting' ? '正在连接 QQ' : status.qq === 'online' ? 'QQ 在线' : status.qq === 'offline' ? 'QQ 离线' : 'QQ 状态待确认';
  const scheduledText = scheduledSyncLabel();
  $('receiving-status').textContent = scheduledText || (!status ? '正在读取本地事项' : connected && status.qq === 'online' && status.receiving === 'active' ? '正在接收置顶群消息' : '消息接收已暂停');
  $('history-status').textContent = scheduledText || ({ pending: '等待读取历史', reading: '正在读取历史', complete: '本次历史读取完成', partial: '历史读取部分未完成' })[status?.history] || '历史读取状态待确认';
  const error = hostError || (status?.error ? errorLabel(status.error) : '');
  $('host-error').textContent = error; $('host-error').hidden = !error;
}
function scheduledSyncLabel() {
  if (!status?.scheduled) return null;
  if (status.transport !== 'connected' || status.qq !== 'online') return '同步已暂停';
  const error = status.error?.replace?.(/_/g, '-');
  if (error === 'game-active') return '游戏中，同步已延后';
  if (error === 'game-catalog-unavailable') return '检测名单未就绪，同步已暂停';
  if (error) return '同步已暂停';
  if (status.history === 'reading') return '正在同步置顶群消息';
  if (status.history === 'partial' || status.historyFailures?.length) return '同步未补全';
  if (status.receiving === 'active') return '正在同步置顶群消息';
  return status.receiving === 'paused' && ['complete', 'pending'].includes(status.history) ? '等待下次同步' : '同步状态待确认';
}
function syncTimeLabel(value, empty) {
  if (!value) return empty;
  const instant = new Date(value);
  if (Number.isNaN(instant.getTime())) return '时间待确认';
  const parts = Object.fromEntries(new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(instant).map(part => [part.type, part.value]));
  return `${parts.year}-${parts.month}-${parts.day} ${parts.hour}:${parts.minute}（北京时间）`;
}
function renderNav() {
  $('nav').innerHTML = Object.entries(pages).map(([key, label]) => {
    const relevant = items().filter(item => key === 'pending' ? item.kind === 'review' && item.state === 'pending' : key === 'tasks' ? item.kind === 'task' && item.state === 'active' : key === 'notices' ? item.kind === 'notice' : key === 'completed' ? item.state === 'completed' : false);
    const unread = relevant.filter(item => item.unread).length;
    return `<button data-action="nav" data-page="${key}" ${key === page ? 'aria-current="page"' : ''}><span>${label}${unread ? '<i class="unread-dot" aria-label="有未读事项"></i>' : ''}</span>${relevant.length ? `<span class="count" title="共 ${relevant.length} 项，${unread} 项未读">${unread ? `${unread} 未读` : relevant.length}</span>` : ''}</button>`;
  }).join('');
}
function renderFilters() {
  if (lastFilterPage !== page) {
    const options = page === 'pending' ? ['pending', 'ignored', 'rejected', 'all'] : page === 'tasks' ? ['active', 'archived', 'cancelled', 'all'] : page === 'notices' ? ['active', 'converted', 'all'] : ['active', 'all'];
    $('state-filter').innerHTML = options.map(value => `<option value="${value}">${states[value] || '全部状态'}</option>`).join('');
    if (!options.includes(filters.state)) filters.state = options[0];
    lastFilterPage = page;
  }
  const courses = courseOptions();
  if (filters.course && !courses.includes(filters.course)) courses.push(filters.course);
  const courseHTML = '<option value="">全部课程</option>' + courses.map(course => `<option value="${html(course)}">${html(course)}</option>`).join('');
  if ($('course-filter').innerHTML !== courseHTML) $('course-filter').innerHTML = courseHTML;
  $('course-filter').value = filters.course; $('state-filter').value = filters.state;
  if ($('search').value !== filters.search) $('search').value = filters.search;
  $('state-filter').hidden = page === 'completed';
}
function courseOptions() {
  return [...new Set([...items().map(item => item.course), ...(snapshot?.groups || []).map(group => group.course)].filter(Boolean))].sort((a, b) => a.localeCompare(b, 'zh-CN'));
}
function initialMessage() {
  if (status?.qq === 'offline') return ['等待 QQ 登录', 'QQ 当前离线，请确认电脑上的 QQ 已登录。'];
  if (status && status.transport !== 'connected') return ['等待 QQ 连接', '连接成功后显示本地事项。'];
  if (status?.qq === 'unknown') return ['等待确认 QQ 状态', '消息接收暂停，已有数据会保留。'];
  return ['正在读取本地事项', '等待本地应用提供事项。'];
}
function emptyMessage() {
  if (!snapshot) { const [title, message] = initialMessage(); return `<div class="empty"><h2>${title}</h2><p>${message}</p></div>`; }
  if (filters.search || filters.course || !['active', 'pending'].includes(filters.state)) return '<div class="empty"><h2>没有符合筛选的事项</h2><p>可调整搜索、课程或状态筛选。</p></div>';
  return `<div class="empty"><h2>${({ tasks: '暂无待办', pending: '暂无待确认', notices: '暂无通知', completed: '暂无完成记录' })[page]}</h2><p>${page === 'tasks' ? '置顶群中识别出的任务会显示在这里。' : '已有事项保存在本地应用中。'}</p></div>`;
}
function renderList() {
  $('page-title').textContent = pages[page]; $('breadcrumb').textContent = pages[page]; $('page-subline').textContent = descriptions[page];
  $('date-stamp').textContent = new Intl.DateTimeFormat('zh-CN', { timeZone: 'Asia/Shanghai', month: 'long', day: 'numeric', weekday: 'short' }).format(new Date());
  const isSettings = page === 'settings';
  const createButton = document.querySelector('[data-action="new-task"]');
  createButton.hidden = page !== 'tasks'; createButton.disabled = !loadedAccount() || Boolean(manualOpen);
  $('filters').hidden = isSettings; $('list-summary').hidden = isSettings; $('rows').hidden = isSettings; $('settings-panel').hidden = !isSettings;
  if (isSettings) { renderSettings(); return; }
  renderFilters();
  const visible = pageItems();
  const viewingStructure = structureOpen?.accountId === snapshot?.accountId && structureOpen?.id === selectedId && selected()?.kind === 'task';
  if (!visible.some(item => item.id === selectedId) && !viewingStructure) selectedId = visible[0]?.id || null;
  $('list-summary').innerHTML = `<span>${visible.length} 项${snapshot ? '' : ' · 等待本地应用'}</span><b>${pages[page]}</b>`;
  $('rows').innerHTML = visible.length ? visible.map(item => `<button class="task-row ${item.id === selectedId ? 'selected' : ''} ${item.kind === 'task' && past(item) && item.timeKind !== 'start' ? 'overdue' : item.due === localDate() ? 'today' : ''}" data-action="select" data-id="${html(item.id)}">
    <div class="row-top"><h2>${html(item.title)}${item.unread ? '<i class="unread-dot" aria-label="未读"></i>' : ''}${item.changeLabel ? `<span class="change-badge">${html(item.changeLabel)}</span>` : ''}</h2><span class="tag ${item.kind === 'review' ? 'pending' : Number.isInteger(item.priority) ? 'p' + item.priority : ''}">${item.kind === 'review' ? html(states[item.state]) : Number.isInteger(item.priority) ? 'P' + item.priority : html(states[item.state])}</span></div>
    <div class="row-meta"><span>${html(item.course || item.group || '来源待核对')}${item.source === 'manual' ? '<span class="manual-source">手动添加</span>' : ''}</span><span>${item.kind === 'task' ? html(dateLabel(item)) : html(states[item.state])}</span></div>
    ${item.kind === 'review' && item.reviewReasons?.length ? `<p class="row-note">${html(item.reviewReasons.join(' · '))}</p>` : ''}</button>`).join('') : emptyMessage();
}
function draftFor(item) {
  if (!drafts.has(item.id)) drafts.set(item.id, {
    title: item.title || '', course: item.course || '', priority: String(item.priority ?? 3), version: item.version,
    date: item.action === 'remind' && item.targetTaskId ? '' : item.due || '',
    time: item.action === 'remind' && item.targetTaskId ? '' : item.time || '',
    timeKind: item.timeKind || 'deadline'
  });
  return drafts.get(item.id);
}
function formHTML(item) {
  const draft = draftFor(item);
  const notice = item.kind === 'notice';
  const formId = item.kind === 'review' ? 'review-form' : notice ? 'notice-form' : 'time-form';
  return `<form class="form" id="${formId}" data-id="${html(item.id)}" novalidate>
    ${item.kind === 'task' ? `<label class="field">标题（必填）<input type="text" name="title" value="${html(draft.title)}" maxlength="256" required></label><label class="field">课程或分类（可清除或填写新名称）<input type="text" name="course" value="${html(draft.course)}" maxlength="256" list="task-course-options"><datalist id="task-course-options">${courseOptions().map(course => `<option value="${html(course)}"></option>`).join('')}</datalist></label>` : ''}
    <label class="field">${notice || item.kind === 'task' ? '正式日期（必填）' : linkedWithoutDate(item) ? '日期（可留空，保留原任务时间）' : '正式日期'}<input type="date" name="date" value="${html(draft.date)}"${notice || item.kind === 'task' ? ' required' : ''}></label>
    <div class="form-grid"><label class="field">时间（可不填）<input type="time" name="time" value="${html(draft.time)}"></label><label class="field">时间类型<select name="timeKind"><option value="deadline" ${draft.timeKind !== 'start' ? 'selected' : ''}>截止时间</option><option value="start" ${draft.timeKind === 'start' ? 'selected' : ''}>开始时间</option></select></label></div>
    <p class="hint">只知道日期时请留空时间，不会补写具体钟点。</p>
    ${item.kind === 'task' ? `<label class="field">优先级<select name="priority" required>${[0, 1, 2, 3].map(priority => `<option value="${priority}" ${String(priority) === draft.priority ? 'selected' : ''}>P${priority}</option>`).join('')}</select></label>` : ''}
    ${item.suggestedDue ? `<div class="change-box">建议日期：${html(item.suggestedDue)}${item.suggestedTime ? ' ' + html(item.suggestedTime) : ''}<p>建议尚未成为正式时间，核对后可填入表单。</p><button type="button" class="button small" data-action="use-suggestion">填入建议日期</button></div>` : ''}
    ${item.kind === 'task' ? '<button type="submit" class="button" data-action="edit-task">保存修改</button>' : notice ? '<button type="submit" class="button primary" data-action="convert-notice">加入任务</button>' : ''}</form>`;
}
function manualFormHTML(draft) {
  return `<p class="eyebrow">手动添加</p><h2 class="detail-title">新增任务</h2><p class="hint">补录口头布置或未识别出的事项。</p><form class="form" id="manual-task-form" data-id="${html(draft.id)}" novalidate>
    <label class="field">标题（必填）<input name="title" type="text" maxlength="256" value="${html(draft.title)}" required></label>
    <label class="field">课程或分类（必填，可填写新名称）<input name="course" type="text" maxlength="256" value="${html(draft.course)}" list="task-course-options" required><datalist id="task-course-options">${courseOptions().map(course => `<option value="${html(course)}"></option>`).join('')}</datalist></label>
    <label class="field">正式日期（必填）<input name="date" type="date" value="${html(draft.date)}" required></label>
    <div class="form-grid"><label class="field">时间（可不填）<input name="time" type="time" value="${html(draft.time)}"></label><label class="field">时间类型<select name="timeKind"><option value="deadline" ${draft.timeKind !== 'start' ? 'selected' : ''}>截止时间</option><option value="start" ${draft.timeKind === 'start' ? 'selected' : ''}>开始时间</option></select></label></div>
    <p class="hint">只知道日期时请留空时间，不会补写具体钟点。</p>
    <label class="field">优先级<select name="priority"><option value="auto" ${draft.priority === 'auto' ? 'selected' : ''}>自动（按日期调整）</option>${[0, 1, 2, 3].map(priority => `<option value="${priority}" ${String(priority) === draft.priority ? 'selected' : ''}>P${priority}</option>`).join('')}</select></label>
    <p class="error" id="manual-task-error" role="alert" hidden></p><div class="detail-actions"><button type="submit" class="button primary" data-action="create-task">添加任务</button><button type="button" class="button" data-action="cancel-new-task">取消</button></div></form>`;
}
function evidenceHTML(evidence) {
  return evidence.map(source => `<div class="source-meta">${html(source.groupName || source.groupId || '来源群')} · ${html(source.senderId || '发送者待核对')} · ${html(source.sentAt ? new Date(source.sentAt).toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai', hour12: false }) : '发送时间暂缺')}</div><div class="message">${html(source.text)}</div>${source.imageCount > 0 ? `<div class="image-placeholder">原消息含 ${html(source.imageCount)} 张图片 · 图片读取与 OCR 尚未接入，请在 QQ 核对原图。</div>` : ''}`).join('');
}
function sourceHTML(item) {
  if (item.source === 'manual' && !item.evidence?.length) return '<section class="source-section"><h3 class="section-label">任务来源</h3><p class="hint">手动添加，使用任务的优先级和完成规则。</p></section>';
  return '<section class="source-section"><h3 class="section-label">原始消息</h3>' + (item.evidence?.length ? evidenceHTML(item.evidence) : '<p class="hint">暂无可显示的原消息正文。</p>') + '</section>';
}
function historyHTML(item) {
  if (!item.changes?.length) return '';
  return `<section class="change-history"><h3 class="section-label">变更记录 <span>北京时间</span></h3><ol class="history-list">${item.changes.map(change => `<li class="history-entry"><div class="history-heading"><b>${html(change.label)}</b><time datetime="${html(change.at)}">${html(new Date(change.at).toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai', hour12: false }))}</time></div>${change.fields?.length ? `<dl class="history-fields">${change.fields.map(field => `<dt>${html(field.field)}</dt><dd><span>${html(field.before)}</span><span class="history-arrow" aria-label="改为">→</span><strong>${html(field.after)}</strong></dd>`).join('')}</dl>` : ''}${change.evidence?.length ? `<div class="history-evidence"><p class="hint">关联消息</p>${evidenceHTML(change.evidence)}</div>` : ''}</li>`).join('')}</ol></section>`;
}
function pendingChangesHTML(item) {
  if (!item.pendingChanges?.length) return '';
  return `<section class="pending-changes"><h3 class="section-label">待确认变更</h3><p class="hint">核对并接受建议前，保留当前正式信息。</p>${item.pendingChanges.map(change => `<article class="change-box"><b>${html(change.label)}</b>${change.due ? `<p>拟定${change.timeKind === 'start' ? '开始' : '截止'}：${html(dateLabel(change))}</p>` : ''}${change.reasons?.length ? `<p>${html(change.reasons.join(' · '))}</p>` : ''}<button type="button" class="button small" data-action="open-suggestion" data-id="${html(change.id)}">查看建议</button></article>`).join('')}</section>`;
}
function relatedHTML(item) {
  const related = (item.relatedItemIds || []).map(id => items().find(candidate => candidate.id === id)).filter(Boolean);
  const sources = items().filter(candidate => candidate.state === 'merged' && candidate.mergedIntoId === item.id);
  const source = sources.find(candidate => candidate.id === mergedSourceId);
  return `${related.length ? `<section class="related-items"><h3 class="section-label">关联事项</h3>${related.map(candidate => `<button type="button" class="button small" data-action="open-related" data-id="${html(candidate.id)}">${html(candidate.title)} · ${html(states[candidate.state] || '已合并')}</button>`).join('')}</section>` : ''}${sources.length ? `<section class="merged-sources"><h3 class="section-label">合并来源</h3><p class="hint">保留原事项的证据和变更记录。</p>${sources.map(candidate => `<button type="button" class="button small" data-action="view-merged-source" data-id="${html(candidate.id)}">${html(candidate.title)}</button>`).join('')}${source ? `<div class="merged-source-detail"><h3>${html(source.title)}</h3>${historyHTML(source)}${sourceHTML(source)}</div>` : ''}</section>` : ''}`;
}
function openStructure(item, mode) {
  if (item.kind !== 'task' || item.state === 'merged' || !loadedAccount()) return;
  const key = structureKey(snapshot.accountId, item.id, mode);
  if (!structureDrafts.has(key)) structureDrafts.set(key, mode === 'merge'
    ? { version: item.version, mergeWithId: '', mergeWithVersion: null, selection: {}, error: '' }
    : { version: item.version, newItemId: uuid(), title: item.title || '', course: item.course || '', date: item.due || '', time: item.time || '', timeKind: item.timeKind || 'deadline', priority: 'auto', state: ['active', 'completed', 'cancelled', 'archived'].includes(item.state) ? item.state : 'active', evidence: item.evidence || [], evidenceIndexes: (item.evidence || []).map((_, index) => index), error: '' });
  structureOpen = { accountId: snapshot.accountId, id: item.id, mode }; manualOpen = false;
  detailSignature = ''; render();
}
const mergeFields = [
  ['titleTaskId', '标题', item => item.title || '（未填写）'],
  ['courseTaskId', '课程或分类', item => item.course || '（未填写）'],
  ['timeTaskId', '正式时间', item => `${timeLabel(item)} · ${dateLabel(item)}`],
  ['priorityTaskId', '优先级', item => Number.isInteger(item.priority) ? `P${item.priority}` : '自动'],
  ['stateTaskId', '状态', item => states[item.state] || item.state]
];
function structureFormHTML(item, draft) {
  const merge = structureOpen.mode === 'merge';
  const other = items().find(candidate => candidate.id === draft.mergeWithId && candidate.kind === 'task' && (candidate.state !== 'merged' || draft.unconfirmed));
  return `<p class="eyebrow">${merge ? '合并重复事项' : '拆开错误合并'}</p><h2 class="detail-title">${html(item.title)}</h2><p class="hint">${merge ? '当前事项将作为保留项；逐字段选择正式信息，全部证据和历史记录继续保留。' : '保留原事项，另建可独立处理的关联事项。核对行动、时间、证据及状态。'}</p><form class="form structure-form" id="${merge ? 'merge-task-form' : 'split-task-form'}" data-id="${html(item.id)}" novalidate>
    ${merge ? `<label class="field">选择另一项任务<select name="mergeWithId"><option value="">请选择要合并的任务</option>${items().filter(candidate => candidate.kind === 'task' && candidate.id !== item.id && (candidate.state !== 'merged' || draft.unconfirmed && candidate.id === draft.mergeWithId)).map(candidate => `<option value="${html(candidate.id)}" ${candidate.id === draft.mergeWithId ? 'selected' : ''}>${html(candidate.title)} · ${html(candidate.course || '未填写课程')} · ${html(states[candidate.state])}</option>`).join('')}</select></label>${other ? mergeFields.map(([key, label, value]) => `<div class="merge-field ${value(item) !== value(other) ? 'conflict' : ''}"><h3>${label}${value(item) !== value(other) ? '<span>存在差异</span>' : ''}</h3><dl><dt>当前事项</dt><dd>${html(value(item))}</dd><dt>另一事项</dt><dd>${html(value(other))}</dd></dl><label class="field">保留${label}<select name="${key}"><option value="">请选择保留哪项</option><option value="${html(item.id)}" ${draft.selection[key] === item.id ? 'selected' : ''}>当前事项 · ${html(value(item))}</option><option value="${html(other.id)}" ${draft.selection[key] === other.id ? 'selected' : ''}>另一事项 · ${html(value(other))}</option></select></label></div>`).join('') : ''}` : `
      <label class="field">新事项标题（必填）<input name="title" maxlength="256" value="${html(draft.title)}" required></label><label class="field">课程或分类（可留空）<input name="course" maxlength="256" value="${html(draft.course)}" list="task-course-options"><datalist id="task-course-options">${courseOptions().map(course => `<option value="${html(course)}"></option>`).join('')}</datalist></label>
      <label class="field">正式日期（有原消息证据时可留空）<input name="date" type="date" value="${html(draft.date)}"></label><div class="form-grid"><label class="field">时间（可不填）<input name="time" type="time" value="${html(draft.time)}"></label><label class="field">时间类型<select name="timeKind">${['deadline', 'start'].map(kind => `<option value="${kind}" ${draft.timeKind === kind ? 'selected' : ''}>${kind === 'start' ? '开始时间' : '截止时间'}</option>`).join('')}</select></label></div><p class="hint">缺少正式日期且有选中证据时，新事项进入待确认；无原消息的手动事项须补齐日期。仅有日期不会补写具体钟点。</p>
      <div class="form-grid"><label class="field">优先级<select name="priority"><option value="auto" ${draft.priority === 'auto' ? 'selected' : ''}>自动（按日期调整）</option>${[0, 1, 2, 3].map(priority => `<option value="${priority}" ${String(priority) === draft.priority ? 'selected' : ''}>P${priority}</option>`).join('')}</select></label><label class="field">新任务状态<select name="state">${['active', 'completed', 'cancelled', 'archived'].map(state => `<option value="${state}" ${draft.state === state ? 'selected' : ''}>${states[state]}</option>`).join('')}</select></label></div>
      <fieldset class="split-evidence"><legend>新事项使用的原消息证据</legend><p class="hint">原事项继续保留全部证据，可让两项共享同一条原消息。</p>${draft.evidence.length ? draft.evidence.map((source, index) => `<label class="evidence-choice"><input type="checkbox" name="evidenceIndex" value="${index}" ${draft.evidenceIndexes.includes(index) ? 'checked' : ''}><span>证据 ${index + 1} · ${html(source.groupName || source.groupId || '来源待核对')}<small>${html(source.text)}</small></span></label>`).join('') : '<p class="hint">此任务没有原消息证据。</p>'}</fieldset>`}
    <p class="error" id="structure-error" role="alert" hidden></p><button type="button" class="button" data-action="refresh-structure" hidden>刷新并核对最新内容</button><div class="detail-actions"><button type="submit" class="button primary" data-action="${merge ? 'merge-tasks' : 'split-task'}">${merge ? '确认合并' : '拆出关联事项'}</button>${merge ? '<button type="button" class="button" data-action="keep-separate">保持独立</button>' : ''}<button type="button" class="button" data-action="close-structure">返回详情</button></div></form>`;
}
function saveStructure(operation) {
  const item = selected(), draft = structureDraft();
  if (!item || !draft || busy.has(item.id) || busy.has(structureBusyKey(snapshot.accountId, item.id))) return;
  if (draft.unconfirmed) { request(draft.operation, { ...JSON.parse(draft.payloadSignature), operationId: draft.operationId }, item.id); return; }
  let extra;
  if (operation === 'split-task') {
    const title = draft.title.trim(), course = draft.course.trim();
    const validDate = !draft.date || /^\d{4}-\d{2}-\d{2}$/.test(draft.date) && Number(draft.date.slice(0, 4)) > 0 && !Number.isNaN(Date.parse(draft.date + 'T00:00:00Z')) && new Date(draft.date + 'T00:00:00Z').toISOString().slice(0, 10) === draft.date;
    draft.error = !title ? '请填写新事项标题。' : draft.title.length > 256 || draft.course.length > 256 ? '标题和课程不能超过 256 个字符。' : /[\u0000-\u001f\u007f]/.test(draft.title + draft.course) ? '标题和课程不能包含控制字符。' : !draft.date && !draft.evidenceIndexes.length ? '无原消息证据时，请补齐正式日期。' : !validDate ? '请填写有效日期。' : draft.time && (!draft.date || !/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(draft.time)) ? '填写时间时也需要有效日期。' : !['deadline', 'start'].includes(draft.timeKind) ? '请选择截止时间或开始时间。' : draft.priority !== 'auto' && !/^[0-3]$/.test(draft.priority) ? '请选择自动或 P0 至 P3 的优先级。' : !['active', 'completed', 'cancelled', 'archived'].includes(draft.state) ? '请选择新任务状态。' : '';
    extra = { newItemId: draft.newItemId, title, course, date: draft.date || null, time: draft.time || null, timeKind: draft.timeKind, priority: draft.priority === 'auto' ? null : Number(draft.priority), state: draft.state, evidenceIndexes: [...draft.evidenceIndexes].sort((a, b) => a - b) };
  } else {
    const other = items().find(candidate => candidate.id === draft.mergeWithId && candidate.kind === 'task' && candidate.state !== 'merged' && candidate.id !== item.id);
    const priority = (draft.selection.priorityTaskId === item.id ? item : other)?.priority;
    draft.error = !other ? '请选择另一项有效任务。' : operation === 'merge-tasks' && mergeFields.some(([key]) => ![item.id, other.id].includes(draft.selection[key])) ? '请逐项选择保留哪个字段和状态。' : operation === 'merge-tasks' && (!Number.isInteger(priority) || priority < 0 || priority > 3) ? '请核对要保留的 P0 至 P3 优先级。' : '';
    extra = { mergeWithId: draft.mergeWithId, mergeWithVersion: draft.mergeWithVersion, ...(operation === 'merge-tasks' ? { mergeSelection: { ...draft.selection }, priority } : {}) };
  }
  if (draft.error) { updateDetailControls(); return; }
  const payload = { accountId: snapshot.accountId, id: item.id, version: draft.version, ...extra };
  const signature = JSON.stringify([operation, payload]);
  if (signature !== draft.requestSignature) { draft.operationId = uuid(); draft.requestSignature = signature; draft.payloadSignature = JSON.stringify(payload); draft.operation = operation; }
  request(operation, { ...payload, operationId: draft.operationId }, item.id);
}
function actionsHTML(item) {
  const button = (action, label, primary = false) => `<button class="button ${primary ? 'primary' : ''}" data-action="${action}">${label}</button>`;
  if (item.kind === 'review') return item.state === 'pending' ? button('ignore', '忽略') + button('reject', '拒绝建议') : item.state === 'ignored' && canUndo(item) ? button('undo-ignore', '撤销') : '';
  if (item.kind === 'notice') return item.state === 'converted' && item.targetTaskId ? button('open-target', '查看任务', true) : '';
  if (item.kind !== 'task') return '';
  const structure = item.state !== 'merged' ? button('open-merge', '合并重复事项') + button('open-split', '拆开错误合并') : '';
  if (item.state === 'active') return button('complete', '完成', true) + button('archive', '归档') + structure;
  if (item.state === 'completed') return (canUndo(item) ? button('undo-complete', '撤销') : '') + button('archive', '归档') + structure;
  if (item.state === 'archived') return button('restore', '恢复为待办') + structure;
  return structure;
}
function retainFocus(container, replace) {
  const active = document.activeElement;
  const focusName = container.contains(active) ? active.name : null;
  const focusGroup = active.closest?.('[data-group-id]')?.dataset.groupId;
  const selectionStart = active.selectionStart, selectionEnd = active.selectionEnd;
  const scrollTop = container.scrollTop;
  replace();
  container.scrollTop = scrollTop;
  if (focusName) {
    const candidates = container.querySelectorAll('[name]');
    const input = [...candidates].find(element => element.name === focusName && (!focusGroup || element.closest('[data-group-id]')?.dataset.groupId === focusGroup));
    input?.focus({ preventScroll: true });
    if (input && selectionStart !== null && typeof input.setSelectionRange === 'function') try { input.setSelectionRange(selectionStart, selectionEnd); } catch {}
  }
}
function renderDetail() {
  const structuralDraft = structureDraft(), structuralItem = selected();
  if (structuralDraft && structuralItem?.id === structureOpen.id && page !== 'settings') {
    const scroll = $('detail-pane').querySelector('.detail-scroll');
    const other = items().find(item => item.id === structuralDraft.mergeWithId);
    const signature = JSON.stringify(['structure', structureOpen, structuralItem, other, structuralDraft.evidence]);
    if (signature !== detailSignature) { retainFocus(scroll, () => { scroll.innerHTML = structureFormHTML(structuralItem, structuralDraft); }); detailSignature = signature; }
    $('detail-pane').querySelector('.confirmation-bar').hidden = true;
    clearTimeout(undoTimer); undoScheduledFor = null;
    updateDetailControls(); markRead(structuralItem); return;
  }
  if (manualOpen && page === 'tasks' && loadedAccount() && manualDraft()) {
    const draft = manualDraft(), signature = `manual/${snapshot.accountId}/${draft.id}`;
    const scroll = $('detail-pane').querySelector('.detail-scroll');
    if (signature !== detailSignature) { scroll.innerHTML = manualFormHTML(draft); scroll.scrollTop = 0; detailSignature = signature; }
    $('detail-pane').querySelector('.confirmation-bar').hidden = true;
    clearTimeout(undoTimer); undoScheduledFor = null;
    updateDetailControls(); return;
  }
  const item = page === 'settings' ? null : selected();
  const signature = item ? JSON.stringify([{ ...item, version: undefined, targetTaskVersion: undefined, unread: undefined }, mergedSourceId, (item.relatedItemIds || []).map(id => items().find(candidate => candidate.id === id)), items().filter(candidate => candidate.state === 'merged' && candidate.mergedIntoId === item.id)]) : `empty/${page}/${Boolean(snapshot)}/${status?.transport}/${status?.qq}`;
  const scroll = $('detail-pane').querySelector('.detail-scroll'), footer = $('detail-pane').querySelector('.confirmation-bar');
  if (signature !== detailSignature) {
    const sameItem = scroll.querySelector('form')?.dataset.id === item?.id;
    retainFocus(scroll, () => {
      if (!item) scroll.innerHTML = `<div class="empty"><h2>${page === 'settings' ? '仅接收置顶群' : snapshot ? '查看事项详情' : initialMessage()[0]}</h2><p>${page === 'settings' ? '确认群课程后用于新消息。已有待确认仍需逐项核对。' : snapshot ? '选择左侧事项查看原文与操作。' : initialMessage()[1]}</p></div>`;
      else scroll.innerHTML = `<p class="eyebrow">${html(states[item.state] || pages[page])}</p><h2 class="detail-title">${html(item.title)}</h2>${item.changeLabel ? `<p class="detail-change-label change-badge">${html(item.changeLabel)}</p>` : ''}<p class="detail-sub">${html(item.course || '课程待核对')} · ${html(item.group || '来源待核对')}</p>
        ${item.kind === 'task' ? `<div class="deadline-box ${item.state === 'active' && past(item) && item.timeKind !== 'start' ? 'overdue' : ''}"><small>${timeLabel(item)}</small><b>${html(dateLabel(item))}</b><small>${item.state !== 'active' ? html(states[item.state]) : past(item) ? item.timeKind === 'start' ? '开始时间已过' : '已逾期，仍保留待处理' : '按正式日期和时间精度保存'}</small></div>` : ''}
        ${pendingChangesHTML(item)}
        ${item.kind === 'review' && item.reviewReasons?.length ? `<div class="change-box">需要核对<p>${html(item.reviewReasons.join(' · '))}</p></div>` : ''}
        <div class="detail-actions">${actionsHTML(item)}</div>
        ${item.kind === 'review' && item.state === 'pending' && item.canAccept ? (item.action === 'cancel' ? '<p class="hint">确认后取消关联任务，保留原文和操作记录。</p>' : formHTML(item)) : item.kind === 'task' && item.state === 'active' ? `<h3 class="section-label">编辑任务</h3>${formHTML(item)}` : item.kind === 'notice' && item.state === 'active' ? `<h3 class="section-label">加入任务</h3><p class="hint">普通通知默认只保留在通知列表；确认正式日期后，可将它加入待办。</p>${formHTML(item)}` : item.kind === 'notice' && item.state === 'converted' ? '<p class="hint">这条通知已保留原消息和转换记录，可从上方打开关联任务。</p>' : ''}
        ${item.kind === 'review' && item.state === 'pending' && !item.canAccept ? '<p class="hint">当前建议无法直接接受，请核对上方原因。可忽略或拒绝建议。</p>' : ''}
        ${item.state === 'ignored' && !canUndo(item) || item.state === 'completed' && !canUndo(item) ? '<p class="hint">撤销期限已过。</p>' : ''}${relatedHTML(item)}${historyHTML(item)}${sourceHTML(item)}`;
      if (!sameItem) scroll.scrollTop = 0;
    });
    footer.hidden = !item || item.kind !== 'review' || item.state !== 'pending';
    footer.innerHTML = item ? `<p class="error" id="detail-error" role="alert" hidden></p>${item.kind === 'task' ? '<button class="button" data-action="refresh-edit" hidden>刷新并核对最新内容</button>' : ''}${!footer.hidden ? `<p id="pending-confirm-help">${item.action === 'cancel' ? '确认后取消关联任务。' : item.targetTaskId ? '确认后更新关联任务；已有正式记录仍保留。' : '补齐正式日期后加入任务，保留原消息来源。'}</p><button class="button primary" id="pending-confirm-button" data-action="accept-review" ${!item.canAccept ? 'disabled' : ''}>${item.action === 'cancel' ? '确认取消关联任务' : item.targetTaskId ? '确认并更新任务' : '确认并加入任务'}</button>` : ''}` : '';
    if (item && footer.hidden) { footer.hidden = false; footer.classList.add('error-only'); }
    else footer.classList.remove('error-only');
    detailSignature = signature;
  }
  updateDetailControls();
  const expires = item && canUndo(item) ? `${item.id}/${item.undoExpiresAt}` : null;
  if (expires !== undoScheduledFor) {
    clearTimeout(undoTimer); undoScheduledFor = expires;
    if (expires) undoTimer = setTimeout(() => { detailSignature = ''; undoScheduledFor = null; render(); }, Math.max(1, new Date(item.undoExpiresAt).getTime() - Date.now()));
  }
  if (item) markRead(item);
}
function updateDetailControls() {
  const item = selected();
  const error = $('detail-error');
  if (error) { const message = detailError?.id === item?.id ? detailError.message : ''; error.textContent = message; error.hidden = !message; if ($('detail-pane').querySelector('.confirmation-bar').classList.contains('error-only')) $('detail-pane').querySelector('.confirmation-bar').hidden = !message; }
  const refresh = $('detail-pane').querySelector('[data-action="refresh-edit"]');
  if (refresh) refresh.hidden = !(detailError?.id === item?.id && detailError.refreshEdit);
  const courseList = $('task-course-options');
  if (courseList) {
    const options = courseOptions().map(course => `<option value="${html(course)}"></option>`).join('');
    if (courseList.innerHTML !== options) courseList.innerHTML = options;
  }
  $('detail-pane').querySelectorAll('button').forEach(button => { button.disabled = Boolean(item && (busy.has(item.id) || busy.has(structureBusyKey(snapshot?.accountId, item.id)))) || (button.id === 'pending-confirm-button' && !item?.canAccept); });
  const structuralForm = $('merge-task-form') || $('split-task-form'), structuralDraft = structureDraft();
  if (structuralForm && structuralDraft) {
    const saving = busy.has(structureBusyKey(snapshot.accountId, item.id)) || busy.has(item.id);
    structuralForm.querySelectorAll('input,select').forEach(element => { element.disabled = saving || Boolean(structuralDraft.unconfirmed); });
    const submit = structuralForm.querySelector('[type=submit]');
    submit.textContent = structuralDraft.unconfirmed ? '重试确认保存' : structureOpen.mode === 'merge' ? '确认合并' : '拆出关联事项';
    structuralForm.querySelector('[data-action=refresh-structure]').hidden = !structuralDraft.refresh;
    const separate = structuralForm.querySelector('[data-action=keep-separate]');
    if (separate) separate.disabled = saving || Boolean(structuralDraft.unconfirmed);
    $('structure-error').textContent = structuralDraft.error || ''; $('structure-error').hidden = !structuralDraft.error;
  }
  const manualForm = $('manual-task-form');
  if (manualForm) {
    const draft = manualDraft(), saving = busy.has(draft?.id);
    manualForm.querySelectorAll('button').forEach(element => { element.disabled = saving; });
    manualForm.querySelectorAll('input,select').forEach(element => { element.disabled = saving || Boolean(draft?.unconfirmed); });
    manualForm.querySelector('[data-action="create-task"]').textContent = draft?.unconfirmed ? '重试确认保存' : '添加任务';
    $('manual-task-error').textContent = draft?.error || ''; $('manual-task-error').hidden = !draft?.error;
  }
  document.querySelectorAll('.group-setting button').forEach(button => { button.disabled = busy.has(button.closest('[data-group-id]').dataset.groupId); });
}
function renderSettings() {
  const pinnedGroups = (snapshot?.groups || []).filter(group => group.pinned);
  const signature = JSON.stringify([pinnedGroups, status && { ...status, updatedAt: undefined }, preferences, startupEnabled, desktop]);
  if (signature === settingsSignature) return;
  retainFocus($('settings-panel'), () => {
    $('settings-panel').innerHTML = `<section class="settings-section"><h2>界面</h2><div class="setting-row"><span>主题</span><select id="theme-setting"><option value="system">跟随 Windows</option><option value="light">浅色</option><option value="dark">深色</option></select></div><div class="setting-row"><span>动画</span><select id="motion-setting"><option value="system">跟随系统</option><option value="reduce">减少动画</option></select></div></section>
      <section class="settings-section"><h2>启动</h2><div class="setting-row"><span>登录 Windows 后自动启动</span><select id="autostart-setting"><option value="on">开启</option><option value="off">关闭</option></select></div><p class="hint">开启后登录 Windows 时在托盘后台运行，桌面卡片继续显示。</p></section>
      <section class="settings-section"><h2>课程与置顶群</h2><p>按真实置顶群名称确认课程；无需先填写课程名单。关联变更用于新消息，已有待确认仍需逐项核对。</p>
      ${pinnedGroups.length ? pinnedGroups.map(group => {
        if (!groupDrafts.has(group.groupId)) groupDrafts.set(group.groupId, group.course || group.suggestedCourse || '');
        return `<div class="group-setting" data-group-id="${html(group.groupId)}"><h3>${html(group.name)}</h3><p>建议课程：${html(group.suggestedCourse || '请核对群名称')}${group.excluded ? ' · 已排除' : group.course ? ' · 已确认' : ' · 待确认'}</p><label class="field">课程或分类<input name="course" maxlength="80" value="${html(groupDrafts.get(group.groupId))}"></label><div class="detail-actions"><button class="button primary small" data-action="confirm-group">确认关联</button><button class="button small" data-action="exclude-group">${group.excluded ? '恢复接收并确认' : '排除此群'}</button></div></div>`;
      }).join('') : '<p class="hint">尚无置顶群名单。连接 QQ 后会显示可确认的真实群。</p>'}</section>
      <section class="settings-section"><h2>${status?.scheduled ? '同步状态' : '接收状态'}</h2><p>${html($('connection-status').textContent)} · ${html($('receiving-status').textContent)}</p>${status?.scheduled ? `<p>上次成功同步（本次运行）：<span id="last-successful-sync">${html(syncTimeLabel(status.lastSuccessfulSyncAt, '本次运行尚无成功同步'))}</span></p><p>下次计划同步：<span id="next-scheduled-sync">${html(syncTimeLabel(status.nextScheduledSyncAt, '计划时间待确认'))}</span></p><p class="hint">每日 08:00、12:00、20:00（北京时间）；游戏期间延后同步。</p>` : `<p>${html($('history-status').textContent)}</p>`}${status?.historyFailures?.length ? '<p>未完成历史读取：</p>' + status.historyFailures.map(failure => `<p>${html(pinnedGroups.find(group => group.groupId === failure.groupId)?.name || failure.groupId)} · ${html(errorLabel(failure.reason))}</p>`).join('') : ''}</section>
      <section class="settings-section"><h2>桌面卡片提醒</h2><p>通过桌面卡片提醒，不播放声音或发送系统通知。</p><p>任务与待确认使用两张常驻桌面卡片，普通群通知仍在通知列表查看。</p><p id="desktop-status">${desktop.state === 'attached' ? '两张桌面卡片已挂载。' : desktop.state === 'unavailable' ? '桌面卡片暂不可用，仍可在主界面查看任务和待确认。' : '桌面卡片正在等待挂载。'}</p></section>`;
    $('theme-setting').value = preferences.theme; $('motion-setting').value = preferences.motion; $('autostart-setting').value = startupEnabled ? 'on' : 'off';
  });
  settingsSignature = signature;
}
function render() { renderStatus(); renderNav(); renderList(); renderDetail(); }
function validateTime(item) {
  const draft = draftFor(item);
  let message = '';
  if (!draft.date && !linkedWithoutDate(item)) message = '请补齐正式日期，核对后再确认。';
  else if (draft.date && (!/^\d{4}-\d{2}-\d{2}$/.test(draft.date) || Number(draft.date.slice(0, 4)) < 1 || new Date(draft.date + 'T00:00:00Z').toISOString().slice(0, 10) !== draft.date)) message = '请填写有效日期。';
  else if (draft.time && (!draft.date || !/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(draft.time))) message = '填写时间时也需要有效日期。';
  if (message) { detailError = { id: item.id, message }; updateDetailControls(); return null; }
  return draft.date ? { date: draft.date, time: draft.time || '', timeKind: draft.timeKind } : {};
}
function editTask(item) {
  const draft = draftFor(item);
  const title = draft.title.trim(), course = draft.course.trim();
  const message = !title ? '请填写任务标题。' : draft.title.length > 256 ? '任务标题不能超过 256 个字符。' : draft.course.length > 256 ? '课程或分类不能超过 256 个字符。' : /[\u0000-\u001f\u007f]/.test(draft.title + draft.course) ? '标题和课程不能包含控制字符。' : !/^[0-3]$/.test(draft.priority) ? '请选择 P0 至 P3 的优先级。' : !['deadline', 'start'].includes(draft.timeKind) ? '请选择截止时间或开始时间。' : '';
  if (message) { detailError = { id: item.id, message }; updateDetailControls(); return; }
  const time = validateTime(item);
  if (time) command('edit-task', item, { ...time, title, course, priority: Number(draft.priority), version: draft.version });
}
function createTask() {
  const draft = manualDraft();
  if (!loadedAccount() || !manualOpen || !draft || busy.has(draft.id)) return;
  if (draft.unconfirmed) { request('create-task', { ...JSON.parse(draft.payloadSignature), operationId: draft.operationId }, draft.id); return; }
  const title = draft.title.trim(), course = draft.course.trim();
  const validDate = /^\d{4}-\d{2}-\d{2}$/.test(draft.date) && Number(draft.date.slice(0, 4)) > 0 && !Number.isNaN(Date.parse(draft.date + 'T00:00:00Z')) && new Date(draft.date + 'T00:00:00Z').toISOString().slice(0, 10) === draft.date;
  draft.error = !title ? '请填写任务标题。' : !course ? '请填写课程或分类。' : draft.title.length > 256 || draft.course.length > 256 ? '标题和课程不能超过 256 个字符。' : /[\u0000-\u001f\u007f]/.test(draft.title + draft.course) ? '标题和课程不能包含控制字符。' : !draft.date ? '请补齐正式日期。' : !validDate ? '请填写有效日期。' : draft.time && !/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(draft.time) ? '请填写有效时间。' : !['deadline', 'start'].includes(draft.timeKind) ? '请选择截止时间或开始时间。' : draft.priority !== 'auto' && !/^[0-3]$/.test(draft.priority) ? '请选择自动或 P0 至 P3 的优先级。' : '';
  if (draft.error) { updateDetailControls(); return; }
  const payload = { accountId: snapshot.accountId, id: draft.id, version: 0, title, course, date: draft.date, time: draft.time || null, timeKind: draft.timeKind, priority: draft.priority === 'auto' ? null : Number(draft.priority) };
  const signature = JSON.stringify(payload);
  if (signature !== draft.payloadSignature) { draft.operationId = uuid(); draft.payloadSignature = signature; }
  request('create-task', { ...payload, operationId: draft.operationId }, draft.id);
}
function acceptReview(item) {
  if (!item?.canAccept || item.state !== 'pending') return;
  const time = item.action === 'cancel' && item.targetTaskId ? {} : validateTime(item);
  if (time) command('accept-review', item, { ...time, ...(item.targetTaskId ? { targetTaskVersion: item.targetTaskVersion } : {}) });
}
function changeStructureField(element) {
  const draft = structureDraft();
  if (!draft || draft.unconfirmed || busy.has(structureBusyKey(snapshot.accountId, structureOpen.id))) return;
  if (element.name === 'mergeWithId') {
    if (draft.mergeWithId === element.value) return;
    const item = selected(), other = items().find(candidate => candidate.id === element.value && candidate.kind === 'task' && candidate.state !== 'merged');
    draft.mergeWithId = element.value; draft.mergeWithVersion = other?.version ?? null;
    draft.selection = Object.fromEntries(mergeFields.map(([key, , value]) => [key, other && value(item) === value(other) ? item.id : '']));
    draft.error = ''; detailSignature = ''; render();
  } else if (mergeFields.some(([key]) => key === element.name)) draft.selection[element.name] = element.value;
  else if (element.name === 'evidenceIndex') {
    const index = Number(element.value);
    draft.evidenceIndexes = element.checked ? [...new Set([...draft.evidenceIndexes, index])] : draft.evidenceIndexes.filter(value => value !== index);
  } else if (['title', 'course', 'date', 'time', 'timeKind', 'priority', 'state'].includes(element.name)) draft[element.name] = element.value;
}

document.addEventListener('input', event => {
  const element = event.target;
  if (element.id === 'search') { filters.search = element.value; render(); }
  const form = element.closest('form[data-id]');
  if (form && ['merge-task-form', 'split-task-form'].includes(form.id)) { changeStructureField(element); return; }
  if (form && ['title', 'course', 'date', 'time', 'timeKind', 'priority'].includes(element.name)) {
    if (form.id === 'manual-task-form' && manualDraft()?.id === form.dataset.id) { if (!manualDraft().unconfirmed) manualDraft()[element.name] = element.value; }
    else { const item = items().find(item => item.id === form.dataset.id); if (item) draftFor(item)[element.name] = element.value; }
  }
  const group = element.closest('[data-group-id]');
  if (group && element.name === 'course') groupDrafts.set(group.dataset.groupId, element.value);
});
document.addEventListener('change', event => {
  const element = event.target;
  if (element.id === 'course-filter') { filters.course = element.value; render(); }
  else if (element.id === 'state-filter') { filters.state = element.value; render(); }
  else if (element.id === 'theme-setting' || element.id === 'motion-setting') {
    preferences[element.id === 'theme-setting' ? 'theme' : 'motion'] = element.value;
    syncAppearance(); try { localStorage.setItem(preferencesKey, JSON.stringify(preferences)); } catch { toast('界面偏好暂时无法保存'); }
    settingsSignature = '';
  } else if (element.id === 'autostart-setting') {
    request('set-autostart', { enabled: element.value === 'on' });
  }
  const form = element.closest('form[data-id]');
  if (form && ['merge-task-form', 'split-task-form'].includes(form.id)) { changeStructureField(element); return; }
  if (form && ['timeKind', 'priority'].includes(element.name)) {
    if (form.id === 'manual-task-form' && manualDraft()?.id === form.dataset.id) { if (!manualDraft().unconfirmed) manualDraft()[element.name] = element.value; }
    else { const item = items().find(item => item.id === form.dataset.id); if (item) draftFor(item)[element.name] = element.value; }
  }
});
document.addEventListener('submit', event => {
  event.preventDefault();
  const item = selected();
  if (event.target.id === 'merge-task-form') saveStructure('merge-tasks');
  else if (event.target.id === 'split-task-form') saveStructure('split-task');
  else if (event.target.id === 'manual-task-form') createTask();
  else if (event.target.id === 'time-form' && item) editTask(item);
  else if (event.target.id === 'notice-form' && item) { const time = validateTime(item); if (time) command('convert-notice', item, time); }
  else if (event.target.id === 'review-form') acceptReview(item);
});
document.addEventListener('click', event => {
  const button = event.target.closest('button[data-action]');
  if (!button || button.disabled) return;
  const action = button.dataset.action;
  if (action === 'new-task') {
    if (!loadedAccount()) return;
    if (!manualDraft()) manualDrafts.set(snapshot.accountId, { id: uuid(), title: '', course: '', date: '', time: '', timeKind: 'deadline', priority: 'auto', error: '' });
    structureOpen = null; manualOpen = true; detailError = null; render(); $('manual-task-form').elements.title.focus(); return;
  }
  if (action === 'cancel-new-task') { manualDrafts.delete(snapshot.accountId); manualOpen = false; render(); return; }
  if (action === 'nav') {
    manualOpen = false; structureOpen = null; mergedSourceId = null; page = button.dataset.page; filters.state = page === 'pending' ? 'pending' : 'active'; selectedId = null; detailError = null; render(); return;
  }
  if (action === 'select') { manualOpen = false; structureOpen = null; mergedSourceId = null; selectedId = button.dataset.id; detailError = null; render(); return; }
  if (action === 'refresh' || action === 'refresh-edit' || action === 'refresh-structure') { hostError = ''; request('snapshot', {}); return; }
  if (action === 'open-related') { openItem({ accountId: snapshot.accountId, id: button.dataset.id }); return; }
  if (action === 'view-merged-source') { mergedSourceId = button.dataset.id; render(); return; }
  if (action === 'close-structure') { structureOpen = null; detailSignature = ''; render(); return; }
  if (action === 'open-suggestion') {
    const suggestion = items().find(item => item.id === button.dataset.id && item.kind === 'review' && item.state === 'pending');
    if (!suggestion || !snapshot) { hostError = '此建议已处理或暂不可用，请刷新后核对。'; render(); return; }
    openItem({ accountId: snapshot.accountId, id: suggestion.id }); return;
  }
  if (action === 'confirm-group' || action === 'exclude-group') {
    const groupId = button.closest('[data-group-id]').dataset.groupId;
    const group = snapshot?.groups.find(group => group.groupId === groupId);
    if (!group || busy.has(groupId)) return;
    const excluded = action === 'exclude-group' ? !group.excluded : false;
    const course = (groupDrafts.get(groupId) || '').trim();
    if (!excluded && !course) { hostError = '请填写此群对应的课程或分类。'; renderStatus(); return; }
    request('confirm-group', { accountId: snapshot.accountId, id: groupId, operationId: uuid(), version: 0, course, excluded }, groupId); return;
  }
  const item = selected(); if (!item) return;
  if (action === 'open-merge' || action === 'open-split') { openStructure(item, action === 'open-merge' ? 'merge' : 'split'); return; }
  if (action === 'keep-separate') { saveStructure('keep-separate'); return; }
  if (action === 'use-suggestion') {
    const draft = draftFor(item); draft.date = item.suggestedDue || ''; draft.time = item.suggestedTime || '';
    const form = $('review-form') || $('time-form');
    form.elements.date.value = draft.date; form.elements.time.value = draft.time;
    detailError = null; updateDetailControls(); return;
  }
  if (action === 'accept-review') {
    acceptReview(item);
  } else if (action === 'edit-task') { /* The form submit handler validates and sends this command. */ }
  else if (action === 'convert-notice') { /* The form submit handler validates and sends this command. */ }
  else if (action === 'open-target') {
    const target = items().find(candidate => candidate.id === item.targetTaskId && candidate.kind === 'task');
    if (!target) { hostError = '关联任务暂未出现在快照，请刷新后重试。'; render(); return; }
    openItem({ accountId: snapshot.accountId, id: target.id }); return;
  }
  else if (['undo-complete', 'undo-ignore'].includes(action)) {
    if (!canUndo(item)) { detailError = { id: item.id, message: errorLabel('undo_expired') }; updateDetailControls(); return; }
    command(action, item, { undoOperationId: item.undoOperationId });
  } else if (['complete', 'archive', 'restore', 'ignore', 'reject'].includes(action)) command(action, item);
});

render();
request('snapshot', {});
syncAppearance();
