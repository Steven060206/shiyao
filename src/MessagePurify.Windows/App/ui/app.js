'use strict';

const $ = id => document.getElementById(id);
const html = value => String(value ?? '').replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character]));
const pages = { tasks: '任务', pending: '待确认', notices: '通知', completed: '已完成', settings: '设置' };
const states = { active: '待办', completed: '已完成', archived: '已归档', cancelled: '已取消', pending: '待确认', ignored: '已忽略', rejected: '已拒绝', accepted: '已确认' };
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

let snapshot = null, status = null, page = 'tasks', selectedId = null, initialized = false;
let desktop = { state: 'waiting', error: null }, pendingOpenItem = null;
let filters = { search: '', course: '', state: 'active' }, detailSignature = '', settingsSignature = '', lastFilterPage = null;
let detailError = null, hostError = '', toastTimer, undoTimer, undoScheduledFor;
const requests = new Map(), busy = new Set(), drafts = new Map(), groupDrafts = new Map(), readAttempts = new Set();
const bridge = window.chrome?.webview;
const uuid = () => crypto.randomUUID();
const items = () => snapshot?.items || [];
const selected = () => items().find(item => item.id === selectedId);
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
    pinned_groups_unavailable: '暂时无法读取置顶群范围',
    pinned_state_unavailable: '暂时无法读取置顶群范围，消息接收已暂停。',
    connection_unavailable: '暂未连接 QQ，请确认电脑上的 QQ 已登录。',
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
  if (itemId) busy.add(itemId);
  const timer = setTimeout(() => {
    const pending = requests.get(id); if (!pending) return;
    requests.delete(id); if (pending.itemId) busy.delete(pending.itemId);
    if (pending.itemId && pending.operation !== 'confirm-group') detailError = { id: pending.itemId, message: errorLabel('command_timeout') };
    else hostError = errorLabel('command_timeout');
    render();
  }, 15000);
  requests.set(id, { operation, itemId, timer });
  try { bridge.postMessage({ id, operation, parameters }); }
  catch {
    clearTimeout(timer); requests.delete(id); if (itemId) busy.delete(itemId);
    hostError = errorLabel('bridge_unavailable');
  }
  updateDetailControls(); renderStatus();
}
function command(operation, item, extra = {}) {
  if (!snapshot || !item || busy.has(item.id)) return;
  detailError = null;
  request(operation, { accountId: snapshot.accountId, id: item.id, operationId: uuid(), version: item.version, ...extra }, item.id);
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
  const item = items().find(item => item.id === message.id && ['task', 'review'].includes(item.kind));
  if (!item) { hostError = '未找到此事项，请刷新后重试。'; render(); return; }
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
    if (pending.itemId) busy.delete(pending.itemId);
    if (pending.operation === 'set-appearance') {
      if (!message.success) { hostError = errorLabel(message.error); renderStatus(); }
      return;
    }
    if (!message.success) {
      if (pending.operation === 'mark-read') { /* Retry only when another item version is viewed. */ }
      else if (pending.itemId && pending.operation !== 'confirm-group') detailError = { id: pending.itemId, message: errorLabel(message.error) };
      else hostError = errorLabel(message.error);
    } else if (pending.operation !== 'mark-read') {
      hostError = ''; detailError = null;
      if (pending.itemId) {
        drafts.delete(pending.itemId);
        if (pending.operation === 'confirm-group') groupDrafts.delete(pending.itemId);
        toast({ complete: '已完成，可在已完成记录中撤销', ignore: '已忽略，可在已忽略记录中撤销', 'accept-review': '已确认', 'accept-time': '正式时间已保存', 'confirm-group': '群课程关联已保存' }[pending.operation] || '已保存');
      }
    }
  } else if (message.type !== 'state') return;
  if (message.type === 'state' && message.error) hostError = errorLabel(message.error);
  if (['attached', 'waiting', 'unavailable'].includes(message.desktop?.state)) desktop = message.desktop;
  if (message.status && (!status || Date.parse(message.status.updatedAt) >= Date.parse(status.updatedAt) || !status.updatedAt)) status = message.status;
  if (message.snapshot) {
    const incoming = message.snapshot;
    const older = snapshot && Date.parse(incoming.generatedAt) < Date.parse(snapshot.generatedAt);
    if (!older) {
      if (snapshot && snapshot.accountId !== incoming.accountId) {
        drafts.clear(); groupDrafts.clear(); readAttempts.clear(); selectedId = null; initialized = false;
      }
      snapshot = incoming;
      if (!initialized) {
        page = items().some(item => item.kind === 'review' && item.state === 'pending') ? 'pending' : 'tasks';
        filters.state = page === 'pending' ? 'pending' : 'active'; initialized = true;
      }
    }
  }
  if (pendingOpenItem && snapshot) { openItem(pendingOpenItem); return; }
  render();
}
bridge?.addEventListener('message', event => receive(event.data));

function pageItems() {
  const search = filters.search.trim().toLocaleLowerCase();
  return items().filter(item => {
    const belongs = page === 'tasks' ? item.kind === 'task' && item.state !== 'completed'
      : page === 'pending' ? item.kind === 'review'
        : page === 'completed' ? item.kind === 'task' && item.state === 'completed'
          : page === 'notices' ? item.kind === 'notice' : false;
    return belongs && (page === 'completed' || filters.state === 'all' || item.state === filters.state)
      && (!filters.course || item.course === filters.course)
      && (!search || [item.title, item.course, item.group, states[item.state]].some(value => String(value || '').toLocaleLowerCase().includes(search)));
  }).sort((a, b) => (a.priority ?? 4) - (b.priority ?? 4) || String(a.due || '9999').localeCompare(String(b.due || '9999')) || String(a.time || '').localeCompare(String(b.time || '')) || String(a.title).localeCompare(String(b.title), 'zh-CN'));
}
function renderStatus() {
  const connected = status?.transport === 'connected';
  $('connection-status').textContent = !status ? '等待连接' : status.transport === 'disconnected' ? '连接已断开' : status.transport === 'connecting' ? '正在连接 QQ' : status.qq === 'online' ? 'QQ 在线' : status.qq === 'offline' ? 'QQ 离线' : 'QQ 状态待确认';
  $('receiving-status').textContent = !status ? '正在读取本地事项' : connected && status.qq === 'online' && status.receiving === 'active' ? '正在接收置顶群消息' : '消息接收已暂停';
  $('history-status').textContent = ({ pending: '等待读取历史', reading: '正在读取历史', complete: '本次历史读取完成', partial: '历史读取部分未完成' })[status?.history] || '历史读取状态待确认';
  const error = hostError || (status?.error ? errorLabel(status.error) : '');
  $('host-error').textContent = error; $('host-error').hidden = !error;
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
    const options = page === 'pending' ? ['pending', 'ignored', 'rejected', 'all'] : page === 'tasks' ? ['active', 'archived', 'cancelled', 'all'] : ['active', 'all'];
    $('state-filter').innerHTML = options.map(value => `<option value="${value}">${states[value] || '全部状态'}</option>`).join('');
    if (!options.includes(filters.state)) filters.state = options[0];
    lastFilterPage = page;
  }
  const courses = [...new Set([...items().map(item => item.course), ...(snapshot?.groups || []).map(group => group.course)].filter(Boolean))].sort((a, b) => a.localeCompare(b, 'zh-CN'));
  if (filters.course && !courses.includes(filters.course)) courses.push(filters.course);
  const courseHTML = '<option value="">全部课程</option>' + courses.map(course => `<option value="${html(course)}">${html(course)}</option>`).join('');
  if ($('course-filter').innerHTML !== courseHTML) $('course-filter').innerHTML = courseHTML;
  $('course-filter').value = filters.course; $('state-filter').value = filters.state;
  if ($('search').value !== filters.search) $('search').value = filters.search;
  $('state-filter').hidden = page === 'completed';
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
  $('filters').hidden = isSettings; $('list-summary').hidden = isSettings; $('rows').hidden = isSettings; $('settings-panel').hidden = !isSettings;
  if (isSettings) { renderSettings(); return; }
  renderFilters();
  const visible = pageItems();
  if (!visible.some(item => item.id === selectedId)) selectedId = visible[0]?.id || null;
  $('list-summary').innerHTML = `<span>${visible.length} 项${snapshot ? '' : ' · 等待本地应用'}</span><b>${pages[page]}</b>`;
  $('rows').innerHTML = visible.length ? visible.map(item => `<button class="task-row ${item.id === selectedId ? 'selected' : ''} ${item.kind === 'task' && past(item) && item.timeKind !== 'start' ? 'overdue' : item.due === localDate() ? 'today' : ''}" data-action="select" data-id="${html(item.id)}">
    <div class="row-top"><h2>${html(item.title)}${item.unread ? '<i class="unread-dot" aria-label="未读"></i>' : ''}</h2><span class="tag ${item.kind === 'review' ? 'pending' : Number.isInteger(item.priority) ? 'p' + item.priority : ''}">${item.kind === 'review' ? html(states[item.state]) : Number.isInteger(item.priority) ? 'P' + item.priority : html(states[item.state])}</span></div>
    <div class="row-meta"><span>${html(item.course || item.group || '来源待核对')}</span><span>${item.kind === 'task' ? html(dateLabel(item)) : html(states[item.state])}</span></div>
    ${item.kind === 'review' && item.reviewReasons?.length ? `<p class="row-note">${html(item.reviewReasons.join(' · '))}</p>` : ''}</button>`).join('') : emptyMessage();
}
function draftFor(item) {
  if (!drafts.has(item.id)) drafts.set(item.id, {
    date: item.action === 'remind' && item.targetTaskId ? '' : item.due || '',
    time: item.action === 'remind' && item.targetTaskId ? '' : item.time || '',
    timeKind: item.timeKind || 'deadline'
  });
  return drafts.get(item.id);
}
function formHTML(item) {
  const draft = draftFor(item);
  return `<form class="form" id="${item.kind === 'review' ? 'review-form' : 'time-form'}" data-id="${html(item.id)}" novalidate>
    <label class="field">${linkedWithoutDate(item) ? '日期（可留空，保留原任务时间）' : '正式日期'}<input type="date" name="date" value="${html(draft.date)}"></label>
    <div class="form-grid"><label class="field">时间（可不填）<input type="time" name="time" value="${html(draft.time)}"></label><label class="field">时间类型<select name="timeKind"><option value="deadline" ${draft.timeKind !== 'start' ? 'selected' : ''}>截止时间</option><option value="start" ${draft.timeKind === 'start' ? 'selected' : ''}>开始时间</option></select></label></div>
    <p class="hint">只知道日期时请留空时间，不会补写具体钟点。</p>
    ${item.suggestedDue ? `<div class="change-box">建议日期：${html(item.suggestedDue)}${item.suggestedTime ? ' ' + html(item.suggestedTime) : ''}<p>建议尚未成为正式时间，核对后可填入表单。</p><button type="button" class="button small" data-action="use-suggestion">填入建议日期</button></div>` : ''}
    ${item.kind === 'task' ? '<button type="submit" class="button" data-action="accept-time">保存正式时间</button>' : ''}</form>`;
}
function sourceHTML(item) {
  return '<h3 class="section-label">原始消息</h3>' + (item.evidence?.length ? item.evidence.map(source => `<div class="source-meta">${html(source.groupName || source.groupId || '来源群')} · ${html(source.senderId || '发送者待核对')} · ${html(source.sentAt ? new Date(source.sentAt).toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai', hour12: false }) : '发送时间暂缺')}</div><div class="message">${html(source.text)}</div>${source.imageCount > 0 ? `<div class="image-placeholder">原消息含 ${html(source.imageCount)} 张图片 · 图片读取与 OCR 尚未接入，请在 QQ 核对原图。</div>` : ''}`).join('') : '<p class="hint">暂无可显示的原消息正文。</p>');
}
function actionsHTML(item) {
  const button = (action, label, primary = false) => `<button class="button ${primary ? 'primary' : ''}" data-action="${action}">${label}</button>`;
  if (item.kind === 'review') return item.state === 'pending' ? button('ignore', '忽略') + button('reject', '拒绝建议') : item.state === 'ignored' && canUndo(item) ? button('undo-ignore', '撤销') : '';
  if (item.kind !== 'task') return '';
  if (item.state === 'active') return button('complete', '完成', true) + button('archive', '归档');
  if (item.state === 'completed') return (canUndo(item) ? button('undo-complete', '撤销') : '') + button('archive', '归档');
  if (item.state === 'archived') return button('restore', '恢复为待办');
  return '';
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
  const item = page === 'settings' ? null : selected();
  const signature = item ? JSON.stringify({ ...item, version: undefined, targetTaskVersion: undefined, unread: undefined }) : `empty/${page}/${Boolean(snapshot)}/${status?.transport}/${status?.qq}`;
  const scroll = $('detail-pane').querySelector('.detail-scroll'), footer = $('detail-pane').querySelector('.confirmation-bar');
  if (signature !== detailSignature) {
    const sameItem = scroll.querySelector('form')?.dataset.id === item?.id;
    retainFocus(scroll, () => {
      if (!item) scroll.innerHTML = `<div class="empty"><h2>${page === 'settings' ? '仅接收置顶群' : snapshot ? '查看事项详情' : initialMessage()[0]}</h2><p>${page === 'settings' ? '确认群课程后用于新消息。已有待确认仍需逐项核对。' : snapshot ? '选择左侧事项查看原文与操作。' : initialMessage()[1]}</p></div>`;
      else scroll.innerHTML = `<p class="eyebrow">${html(states[item.state] || pages[page])}</p><h2 class="detail-title">${html(item.title)}</h2><p class="detail-sub">${html(item.course || '课程待核对')} · ${html(item.group || '来源待核对')}</p>
        ${item.kind === 'task' ? `<div class="deadline-box ${item.state === 'active' && past(item) && item.timeKind !== 'start' ? 'overdue' : ''}"><small>${timeLabel(item)}</small><b>${html(dateLabel(item))}</b><small>${item.state !== 'active' ? html(states[item.state]) : past(item) ? item.timeKind === 'start' ? '开始时间已过' : '已逾期，仍保留待处理' : '按正式日期和时间精度保存'}</small></div>` : ''}
        ${item.kind === 'review' && item.reviewReasons?.length ? `<div class="change-box">需要核对<p>${html(item.reviewReasons.join(' · '))}</p></div>` : ''}
        <div class="detail-actions">${actionsHTML(item)}</div>
        ${item.kind === 'review' && item.state === 'pending' && item.canAccept ? (item.action === 'cancel' ? '<p class="hint">确认后取消关联任务，保留原文和操作记录。</p>' : formHTML(item)) : item.kind === 'task' && item.state === 'active' ? `<h3 class="section-label">调整正式时间</h3>${formHTML(item)}` : ''}
        ${item.kind === 'review' && item.state === 'pending' && !item.canAccept ? '<p class="hint">当前建议无法直接接受，请核对上方原因。可忽略或拒绝建议。</p>' : ''}
        ${item.state === 'ignored' && !canUndo(item) || item.state === 'completed' && !canUndo(item) ? '<p class="hint">撤销期限已过。</p>' : ''}${sourceHTML(item)}`;
      if (!sameItem) scroll.scrollTop = 0;
    });
    footer.hidden = !item || item.kind !== 'review' || item.state !== 'pending';
    footer.innerHTML = item ? `<p class="error" id="detail-error" role="alert" hidden></p>${!footer.hidden ? `<p id="pending-confirm-help">${item.action === 'cancel' ? '确认后取消关联任务。' : item.targetTaskId ? '确认后更新关联任务；已有正式记录仍保留。' : '补齐正式日期后加入任务，保留原消息来源。'}</p><button class="button primary" id="pending-confirm-button" data-action="accept-review" ${!item.canAccept ? 'disabled' : ''}>${item.action === 'cancel' ? '确认取消关联任务' : item.targetTaskId ? '确认并更新任务' : '确认并加入任务'}</button>` : ''}` : '';
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
  $('detail-pane').querySelectorAll('button').forEach(button => { button.disabled = Boolean(item && busy.has(item.id)) || (button.id === 'pending-confirm-button' && !item?.canAccept); });
  document.querySelectorAll('.group-setting button').forEach(button => { button.disabled = busy.has(button.closest('[data-group-id]').dataset.groupId); });
}
function renderSettings() {
  const pinnedGroups = (snapshot?.groups || []).filter(group => group.pinned);
  const signature = JSON.stringify([pinnedGroups, status && { ...status, updatedAt: undefined }, preferences, desktop]);
  if (signature === settingsSignature) return;
  retainFocus($('settings-panel'), () => {
    $('settings-panel').innerHTML = `<section class="settings-section"><h2>界面</h2><div class="setting-row"><span>主题</span><select id="theme-setting"><option value="system">跟随 Windows</option><option value="light">浅色</option><option value="dark">深色</option></select></div><div class="setting-row"><span>动画</span><select id="motion-setting"><option value="system">跟随系统</option><option value="reduce">减少动画</option></select></div></section>
      <section class="settings-section"><h2>课程与置顶群</h2><p>按真实置顶群名称确认课程；无需先填写课程名单。关联变更用于新消息，已有待确认仍需逐项核对。</p>
      ${pinnedGroups.length ? pinnedGroups.map(group => {
        if (!groupDrafts.has(group.groupId)) groupDrafts.set(group.groupId, group.course || group.suggestedCourse || '');
        return `<div class="group-setting" data-group-id="${html(group.groupId)}"><h3>${html(group.name)}</h3><p>建议课程：${html(group.suggestedCourse || '请核对群名称')}${group.excluded ? ' · 已排除' : group.course ? ' · 已确认' : ' · 待确认'}</p><label class="field">课程或分类<input name="course" maxlength="80" value="${html(groupDrafts.get(group.groupId))}"></label><div class="detail-actions"><button class="button primary small" data-action="confirm-group">确认关联</button><button class="button small" data-action="exclude-group">${group.excluded ? '恢复接收并确认' : '排除此群'}</button></div></div>`;
      }).join('') : '<p class="hint">尚无置顶群名单。连接 QQ 后会显示可确认的真实群。</p>'}</section>
      <section class="settings-section"><h2>接收状态</h2><p>${html($('connection-status').textContent)} · ${html($('receiving-status').textContent)}</p><p>${html($('history-status').textContent)}</p>${status?.historyFailures?.length ? '<p>未完成历史读取：</p>' + status.historyFailures.map(failure => `<p>${html(pinnedGroups.find(group => group.groupId === failure.groupId)?.name || failure.groupId)} · ${html(errorLabel(failure.reason))}</p>`).join('') : ''}</section>
      <section class="settings-section"><h2>桌面卡片提醒</h2><p>通过桌面卡片提醒，不播放声音或发送系统通知。</p><p>任务与待确认使用两张常驻桌面卡片，普通群通知仍在通知列表查看。</p><p id="desktop-status">${desktop.state === 'attached' ? '两张桌面卡片已挂载。' : desktop.state === 'unavailable' ? '桌面卡片暂不可用，仍可在主界面查看任务和待确认。' : '桌面卡片正在等待挂载。'}</p></section>`;
    $('theme-setting').value = preferences.theme; $('motion-setting').value = preferences.motion;
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
function acceptReview(item) {
  if (!item?.canAccept || item.state !== 'pending') return;
  const time = item.action === 'cancel' && item.targetTaskId ? {} : validateTime(item);
  if (time) command('accept-review', item, { ...time, ...(item.targetTaskId ? { targetTaskVersion: item.targetTaskVersion } : {}) });
}
document.addEventListener('input', event => {
  const element = event.target;
  if (element.id === 'search') { filters.search = element.value; render(); }
  const form = element.closest('form[data-id]');
  if (form && ['date', 'time', 'timeKind'].includes(element.name)) {
    const item = items().find(item => item.id === form.dataset.id);
    if (item) draftFor(item)[element.name] = element.value;
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
  }
  const form = element.closest('form[data-id]');
  if (form && element.name === 'timeKind') { const item = items().find(item => item.id === form.dataset.id); if (item) draftFor(item).timeKind = element.value; }
});
document.addEventListener('submit', event => {
  event.preventDefault();
  const item = selected();
  if (event.target.id === 'time-form' && item) { const time = validateTime(item); if (time) command('accept-time', item, time); }
  else if (event.target.id === 'review-form') acceptReview(item);
});
document.addEventListener('click', event => {
  const button = event.target.closest('button[data-action]');
  if (!button || button.disabled) return;
  const action = button.dataset.action;
  if (action === 'nav') {
    page = button.dataset.page; filters.state = page === 'pending' ? 'pending' : 'active'; selectedId = null; detailError = null; render(); return;
  }
  if (action === 'select') { selectedId = button.dataset.id; detailError = null; render(); return; }
  if (action === 'refresh') { hostError = ''; request('snapshot', {}); return; }
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
  if (action === 'use-suggestion') {
    const draft = draftFor(item); draft.date = item.suggestedDue || ''; draft.time = item.suggestedTime || '';
    const form = $('review-form') || $('time-form');
    form.elements.date.value = draft.date; form.elements.time.value = draft.time;
    detailError = null; updateDetailControls(); return;
  }
  if (action === 'accept-review') {
    acceptReview(item);
  } else if (action === 'accept-time') { /* The form submit handler validates and sends this command. */ }
  else if (['undo-complete', 'undo-ignore'].includes(action)) {
    if (!canUndo(item)) { detailError = { id: item.id, message: errorLabel('undo_expired') }; updateDetailControls(); return; }
    command(action, item, { undoOperationId: item.undoOperationId });
  } else if (['complete', 'archive', 'restore', 'ignore', 'reject'].includes(action)) command(action, item);
});

render();
request('snapshot', {});
syncAppearance();
