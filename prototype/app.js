// 仅演示界面和规则：不连接 QQ、不识别图片、不调度 Windows 通知。
// 事项时间使用固定演示日期；撤销期限使用真实经过时间，刷新后继续计时。
const TODAY = '2026-10-09';
const NOW = new Date('2026-10-09T10:00:00+08:00');
const STORAGE_KEY = 'message-purify-demo-v2';
const pages = {tasks:'任务',pending:'待确认',notices:'通知',completed:'已完成',settings:'设置'};
const seed = [
  {id:'t1',kind:'task',state:'active',title:'数据结构 · 实验二报告',course:'数据结构',group:'数据结构课程群',due:'2026-10-09',time:'18:00',sender:'课程老师',sent:'10月8日 19:40',text:'请同学们在10月9日18:00前提交实验二报告。\n提交到课程平台，包含代码、运行截图和结果分析。'},
  {id:'t2',kind:'task',state:'active',title:'高等数学 · 第三章习题',course:'高等数学',group:'高数学习群',due:'2026-10-11',time:'',sender:'课程老师',sent:'10月8日 15:12',text:'本周作业：第三章习题第1、3、5题。\n10月11日提交，写清解题过程，拍照上传到课程平台。'},
  {id:'t3',kind:'task',state:'active',title:'大学英语 · 单元听力练习',course:'大学英语',group:'大学英语课程群',due:'2026-10-13',time:'20:00',sender:'课程老师',sent:'10月9日 08:35',text:'请完成第三单元听力练习，10月13日20:00前提交。\n练习入口在课程平台的第三单元下。'},
  {id:'t4',kind:'task',state:'active',title:'程序设计 · 实验一',course:'程序设计',group:'程序设计课程群',due:'2026-10-08',time:'23:59',sender:'课程老师',sent:'10月6日 16:20',text:'实验一请在10月8日23:59前提交。\n内容包括基础输入输出、条件分支和循环练习。'},
  {id:'p1',kind:'task',state:'pending',title:'大学物理 · 实验预习',course:'大学物理',group:'大学物理实验群',due:'',time:'',reason:'截止日期需从图片中确认',sender:'实验老师',sent:'10月9日 09:18',text:'下次实验前请完成预习，提交时间见图片。',image:true,ocr:'实验预习要求：10月12日18:00前提交预习报告。报告包含实验原理和步骤。'},
  {id:'p2',kind:'notice',state:'pending',title:'教务通知 · 专业讲座',course:'',group:'学院通知群',due:'',time:'',reason:'适用对象待确认',sender:'教务老师',sent:'10月9日 09:05',text:'10月14日举办专业发展讲座，面向电子信息类专业同学。\n有兴趣的同学可以报名参加。'},
  {id:'n1',kind:'notice',state:'active',title:'图书馆开放时间调整',course:'校园通知',group:'学校综合群',due:'',time:'',sender:'图书馆老师',sent:'10月9日 08:10',text:'本周六图书馆开放时间调整为09:00—17:00。\n请同学们合理安排借阅时间。'},
  {id:'n2',kind:'notice',state:'active',title:'校园摄影比赛报名',course:'校园活动',group:'学校综合群',due:'',time:'',sender:'学生事务老师',sent:'10月8日 12:30',text:'校园摄影比赛开始报名，主题是“我眼中的校园”。\n自愿参加，报名截止日期为10月20日。'}
];
const defaultSettings = {theme:'system',sound:true,motion:'system',autostart:true,close:'tray',guideDone:false};
const $ = id => document.getElementById(id);
const escapeHTML = value => String(value ?? '').replace(/[&<>"']/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
const freshItems = () => structuredClone(seed).map(item => ({...item,unread:true,timeType:'deadline',taskType:item.kind === 'task' ? 'homework' : 'other',reminder:{mode:'default'},history:[]}));
let items = freshItems(), settings = {...defaultSettings}, widgetLayout = null;
try {
  const saved = JSON.parse(localStorage.getItem(STORAGE_KEY));
  if (saved?.version === 2 && Array.isArray(saved.items)) {
    items = saved.items;
    settings = {...defaultSettings,...saved.settings};
    widgetLayout = saved.widgetLayout;
  }
} catch {}
let page = items.some(i => i.state === 'pending') ? 'pending' : 'tasks';
let selectedId = null, convertingId = null, toastTimer, guideStep = 0;
let filters = {search:'',course:'',state:'active'};
const systemTheme = matchMedia('(prefers-color-scheme:dark)');
const dayOffset = date => (Date.parse(date+'T00:00:00Z')-Date.parse(TODAY+'T00:00:00Z'))/86400000;
const timePast = item => Boolean(item.due && (item.time ? Date.parse(item.due+'T'+item.time+':00+08:00') < NOW.getTime() : item.due < TODAY));
const overdue = item => item.timeType !== 'start' && timePast(item);
const timeName = item => item.timeType === 'start' ? '开始时间' : '截止时间';
function persist() {
  try { localStorage.setItem(STORAGE_KEY,JSON.stringify({version:2,items,settings,widgetLayout})); }
  catch { $('storage-status').textContent = '浏览器无法保存，刷新后进度可能丢失'; }
}
function priority(item) {
  if (item.manualPriority) return item.manualPriority;
  const days = item.due ? dayOffset(item.due) : Infinity;
  return days <= 0 ? 'P0' : days <= 3 ? 'P1' : days <= 7 ? 'P2' : 'P3';
}
function dateLabel(item) {
  if (!item.due) return '日期待补齐';
  const day = item.due === TODAY ? '今天' : `${Number(item.due.slice(5,7))}月${Number(item.due.slice(8))}日`;
  return day + (item.time ? ' '+item.time : ' · 仅有日期');
}
function reminderTimes(item) {
  if (!item.due || item.reminder?.mode === 'off') return [];
  const rule = item.reminder || {mode:'default'};
  const exact = item.time ? Date.parse(item.due+'T'+item.time+':00+08:00') : null;
  if (rule.mode === 'hours' && exact) return [exact - rule.hours*3600000];
  if (rule.mode === 'days') return [Date.parse(item.due+'T'+rule.clock+':00+08:00')-rule.days*86400000];
  return exact ? [exact-86400000,exact-7200000] : [Date.parse(item.due+'T12:00:00+08:00')-86400000];
}
function reminderLabel(item) {
  if (item.state === 'completed') return '已完成，停止提醒';
  if (item.state === 'archived') return '已归档，停止提醒';
  if (item.state === 'cancelled') return '已取消，停止提醒';
  if (item.state !== 'active') return '确认后再安排提醒';
  if (item.kind === 'notice') return '普通通知，可自行加入任务';
  if (item.reminder?.mode === 'off') return '此任务的时间提醒已关闭';
  if (timePast(item)) return item.timeType === 'start' ? '开始时间已过，保留待办' : '仅保留卡片高亮，不重复弹通知';
  const format = value => new Intl.DateTimeFormat('zh-CN',{timeZone:'Asia/Shanghai',month:'numeric',day:'numeric',hour:'2-digit',minute:'2-digit',hour12:false}).format(new Date(value));
  const times = reminderTimes(item);
  return times.length ? times.map(format).join('、') + (times.length === 1 ? ' 提醒' : '') : '补齐日期后安排提醒';
}
function canUndo(item, now = Date.now()) {
  const at = item.state === 'completed' ? item.completedAt : item.state === 'ignored' ? item.ignoredAt : null;
  const duration = item.state === 'completed' ? 3600000 : 86400000;
  return Number.isFinite(at) && now >= at && now < at + duration;
}
function pageItems(target = page) {
  const list = items.filter(item => target === 'pending' ? item.state === 'pending' : target === 'completed' ? item.state === 'completed' || item.history?.some(e => e.action === 'completed' && !e.undone) : item.state === 'active' && item.kind === (target === 'tasks' ? 'task' : 'notice'));
  return target === 'tasks' ? list.sort((a,b) => priority(a).localeCompare(priority(b)) || (a.due+(a.time || '23:59')).localeCompare(b.due+(b.time || '23:59'))) : list;
}
function visibleItems() {
  let list = page === 'tasks' && filters.state !== 'active' ? items.filter(i => i.kind === 'task' && i.state === filters.state) : page === 'pending' && filters.state === 'ignored' ? items.filter(i => i.state === 'ignored') : pageItems();
  const query = filters.search.trim().toLocaleLowerCase();
  return list.filter(i => (!filters.course || i.course === filters.course) && (!query || [i.title,i.course,i.group].join(' ').toLocaleLowerCase().includes(query)));
}
function badge(item) {
  const label = {pending:'待确认',completed:'已完成',ignored:'已忽略',archived:'已归档',cancelled:'已取消'}[item.state];
  return label ? `<span class="tag ${item.state === 'pending' ? 'pending' : ''}">${label}</span>` : item.kind === 'notice' ? '<span class="tag">通知</span>' : `<span class="tag ${priority(item).toLowerCase()}">${priority(item)}</span>`;
}
const dot = item => item.unread ? '<span class="unread-dot" aria-label="未读"></span>' : '';
function renderNav() {
  $('nav').innerHTML = Object.entries(pages).map(([key,label]) => {
    const list = pageItems(key), unread = ['tasks','pending','notices'].includes(key) ? list.filter(i => i.unread).length : 0;
    return `<button data-action="nav" data-page="${key}" ${key === page ? 'aria-current="page"' : ''}><span>${label}${unread ? '<span class="unread-dot" aria-label="未读"></span>' : ''}</span>${key === 'settings' ? '' : `<span class="count" aria-label="${unread ? unread+' 项未读' : list.length+' 项'}">${unread || list.length}</span>`}</button>`;
  }).join('');
}
function filterHTML() {
  const courses = [...new Set(items.map(i => i.course).filter(Boolean))];
  const states = page === 'tasks' ? {active:'待办',archived:'已归档',cancelled:'已取消'} : page === 'pending' ? {active:'待确认',ignored:'最近忽略'} : {active:'全部状态'};
  return `<div class="filters"><input type="search" id="search" aria-label="搜索事项" placeholder="搜索标题、课程或来源群" value="${escapeHTML(filters.search)}"><select id="course-filter" aria-label="筛选课程"><option value="">全部课程</option>${courses.map(c => `<option ${c === filters.course ? 'selected' : ''}>${escapeHTML(c)}</option>`).join('')}</select><select id="state-filter" aria-label="筛选状态">${Object.entries(states).map(([key,label]) => `<option value="${key}" ${key === filters.state ? 'selected' : ''}>${label}</option>`).join('')}</select></div>`;
}
function renderList() {
  if (page === 'settings') { renderSettings(); return; }
  const list = visibleItems();
  const descriptions = {tasks:`${pageItems('tasks').length} 项待办 · ${pageItems('tasks').filter(overdue).length} 项逾期`,pending:'补齐信息，确认与自己相关的事项',notices:'学校通知与自愿活动',completed:'保留完成记录与原始消息'};
  $('list-pane').innerHTML = `<div class="page-head"><div><h1>${pages[page]}</h1><p class="subline">${descriptions[page]}</p></div><div class="date-stamp">10月9日 10:00<br>固定演示时间</div></div><div class="guide-banner"><span>未连接 QQ · 所有消息均为示例</span><button class="button small quiet" data-action="guide">体验引导</button></div>${filterHTML()}<div class="list-summary"><b>显示 ${list.length} 项</b><span>${page === 'tasks' ? '按优先级与时间排序' : page === 'pending' && filters.state === 'ignored' ? '忽略后 24 小时内可撤销' : '打开详情后标记已读'}</span></div><div class="rows">${list.map(item => `<button class="task-row ${item.id === selectedId ? 'selected' : ''} ${item.state === 'active' && item.kind === 'task' ? overdue(item) ? 'overdue' : item.due === TODAY ? 'today' : '' : ''}" data-action="select" data-id="${item.id}" aria-pressed="${item.id === selectedId}"><div class="row-top">${badge(item)}<h2>${escapeHTML(item.title)}</h2>${dot(item)}</div><div class="row-meta"><span>${escapeHTML(item.course || item.group)}</span><span>${item.kind === 'task' ? dateLabel(item) : escapeHTML(item.sent)}</span></div>${item.state === 'pending' ? `<p class="row-note pending">${escapeHTML(item.reason)}</p>` : item.state === 'active' && item.kind === 'task' && timePast(item) ? `<p class="row-note">${item.timeType === 'start' ? '开始时间已过' : '已逾期'} · 等待手动处理</p>` : item.changeLabel ? `<p class="row-note pending">${escapeHTML(item.changeLabel)}</p>` : ''}</button>`).join('') || '<div class="empty"><h2>暂无符合条件的事项</h2><p>可以调整筛选，或添加一个任务。</p></div>'}</div>`;
}
function editForm(item, mode) {
  const task = mode === 'convert' || item.kind === 'task';
  return `<form class="form" data-id="${item.id || ''}" data-mode="${mode}" novalidate>${mode === 'pending' ? `<label class="field">事项类型<select name="kind"><option value="task" ${task ? 'selected' : ''}>任务</option><option value="notice" ${!task ? 'selected' : ''}>通知</option></select></label>` : ''}<label class="field">事项标题<input name="title" value="${escapeHTML(item.title)}" required maxlength="150"></label><label class="field">课程或分类<input name="course" value="${escapeHTML(item.course)}" placeholder="例如：大学物理" required maxlength="80"></label><label class="field">事项类别<select name="taskType">${Object.entries({homework:'作业',exam:'考试',submission:'需要提交的任务',activity:'活动',other:'其他'}).map(([key,label]) => `<option value="${key}" ${key === (item.taskType || 'homework') ? 'selected' : ''}>${label}</option>`).join('')}</select></label><label class="field">时间用途<select name="timeType"><option value="deadline">提交截止</option><option value="start" ${item.timeType === 'start' ? 'selected' : ''}>考试 / 活动开始</option></select></label><div class="form-grid"><label class="field">日期<input type="date" name="due" value="${item.due || ''}"></label><label class="field">具体时刻（可不填）<input type="time" name="time" value="${item.time || ''}"></label></div><p class="hint">只明确日期时，前一天中午 12:00 提醒。普通通知无需填写日期。</p>${item.image ? `<label class="check"><input type="checkbox" name="imageConfirmed" ${item.imageConfirmed ? 'checked' : ''}><span>我已查看原图并确认所填写的日期和时刻</span></label>` : ''}<label class="field">优先级<select name="priority"><option value="">自动计算</option>${['P0','P1','P2','P3'].map(p => `<option ${item.manualPriority === p ? 'selected' : ''}>${p}</option>`).join('')}</select></label><p class="error" role="alert" hidden></p><button class="button primary" type="submit">${mode === 'pending' ? '确认事项' : mode === 'convert' ? '加入任务' : mode === 'new' ? '创建任务' : '保存修改'}</button></form>`;
}
function reminderForm(item) {
  const rule = item.reminder || {mode:'default'};
  return `<details id="reminder-settings"><summary>自定义此任务的提醒</summary><form class="form" data-mode="reminder" data-id="${item.id}" novalidate><label class="field">提醒方式<select name="reminderMode"><option value="default">默认提醒</option><option value="hours" ${rule.mode === 'hours' ? 'selected' : ''} ${!item.time ? 'disabled' : ''}>提前若干小时</option><option value="days" ${rule.mode === 'days' ? 'selected' : ''}>前若干天的某时刻</option><option value="off" ${rule.mode === 'off' ? 'selected' : ''}>关闭时间提醒</option></select></label><div class="form-grid" data-rule="hours" ${rule.mode !== 'hours' ? 'hidden' : ''}><label class="field">提前小时数<input name="hours" type="number" min="0.5" max="8760" step="0.5" value="${rule.hours || 2}"></label></div><div class="form-grid" data-rule="days" ${rule.mode !== 'days' ? 'hidden' : ''}><label class="field">提前天数<input name="days" type="number" min="1" max="365" value="${rule.days || 1}"></label><label class="field">提醒钟点<input name="clock" type="time" value="${rule.clock || '12:00'}"></label></div><p class="hint">${item.time ? '提醒以正式时间为基准，不改变截止或开始时间。' : '只有日期，不能使用小时倒推。'}<br>默认：有时刻提前 24 小时、2 小时；仅日期前一天 12:00。</p><p class="error" role="alert" hidden></p><button class="button small primary" type="submit">保存提醒</button></form></details>`;
}
function imagePreview() {
  const svg = '<svg xmlns="http://www.w3.org/2000/svg" width="720" height="440" viewBox="0 0 720 440"><rect width="720" height="440" fill="#f8f8f3"/><rect x="30" y="30" width="660" height="380" fill="white" stroke="#ddd"/><text x="65" y="88" font-size="18" fill="#777" font-family="Microsoft YaHei,sans-serif">原图示例 · 实验安排</text><text x="65" y="148" font-size="30" fill="#252d24" font-family="Microsoft YaHei,sans-serif">大学物理实验预习</text><path d="M65 172H650" stroke="#ddd"/><text x="65" y="225" font-size="24" fill="#343f35" font-family="Microsoft YaHei,sans-serif">10月12日 18:00 前提交预习报告</text><text x="65" y="285" font-size="22" fill="#525e53" font-family="Microsoft YaHei,sans-serif">报告包含实验原理和步骤</text><text x="65" y="358" font-size="17" fill="#7e867d" font-family="Microsoft YaHei,sans-serif">课程老师 · 大学物理实验群</text></svg>';
  return `<img class="source-image" alt="原图示例：大学物理实验预习，10月12日18:00前提交预习报告" src="data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}">`;
}
function suggestionHTML(item) {
  if (item.kind === 'reminder') return `<div class="change-box"><b>原自定义提醒不再适用，当前使用默认提醒</b><p>此任务的新时间只有日期，原“提前小时”规则无法继续使用。</p><button class="button small" data-action="open-reminder" data-id="${item.targetId}">重新设置提醒</button><button class="button small" data-action="default-reminder" data-id="${item.id}">保留默认提醒</button><button class="button small" data-action="off-reminder" data-id="${item.id}">关闭时间提醒</button></div>`;
  return `<div class="change-box"><b>${item.restoreArchived ? '已归档任务收到新行动要求' : '新时间与手动填写的时间不同'}</b><p>当前：${escapeHTML(dateLabel(item.oldTime))}<br>建议：${escapeHTML(dateLabel(item.suggestion))}<br>接受前保留当前任务状态与有效提醒。</p><button class="button primary small" data-action="accept-change" data-id="${item.id}">${item.restoreArchived ? '接受并恢复待办' : '接受新时间'}</button></div>`;
}
function renderDetail() {
  const item = items.find(i => i.id === selectedId);
  if (page === 'settings') { $('detail-pane').innerHTML = '<p class="eyebrow">演示范围</p><h2 class="detail-title">先体验日常操作</h2><p class="detail-sub">这些开关保存的是此浏览器的演示偏好。系统声音、自动启动、托盘和 QQ 连接将在真实 Windows 应用中接入。</p><div class="deadline-box"><small>体验路线</small><b>确认图片日期 → 完成并撤销</b><small>也可以归档恢复、添加考试、调整提醒，或在详情底部模拟老师的新消息。</small></div><button class="button" data-action="guide">重新体验引导</button>'; return; }
  if (!item) { $('detail-pane').innerHTML = '<div class="empty"><h2>选择一个事项</h2><p>这里显示原文、图片和可编辑信息。</p></div>'; return; }
  const pending = item.state === 'pending', active = item.state === 'active', task = item.kind === 'task';
  const suggestion = ['change','reminder'].includes(item.kind);
  const expired = ['completed','ignored'].includes(item.state) && !canUndo(item);
  $('detail-pane').innerHTML = `<p class="eyebrow">${pending ? '核对并补齐信息' : '事项详情'}</p><h2 class="detail-title">${escapeHTML(item.title)}</h2><p class="detail-sub">${badge(item)} <span style="margin-left:7px">${escapeHTML(item.course || '课程或分类待确认')}</span></p>${item.changeLabel ? `<div class="change-box">${escapeHTML(item.changeLabel)}</div>` : ''}${suggestion ? suggestionHTML(item) : `<div class="deadline-box ${active && task && overdue(item) ? 'overdue' : ''}"><small>${pending ? '待补信息' : task ? timeName(item) : '事项类型'}</small><b>${pending ? escapeHTML(item.reason) : task ? dateLabel(item) : '普通通知'}</b><small>${reminderLabel(item)}</small></div>`}<div class="detail-actions">${pending ? `<button class="button small" data-action="ignore" data-id="${item.id}">忽略此项</button>` : canUndo(item) ? `<button class="button" data-action="undo" data-id="${item.id}">撤销</button><span class="hint">${item.state === 'completed' ? '完成后 1 小时内' : '忽略后 24 小时内'}可撤销</span>` : expired ? '<span class="tag">撤销期限已过，记录保留</span>' : ''}${active && task ? `<button class="button primary" data-action="complete" data-id="${item.id}">标记完成</button><button class="button" data-action="archive" data-id="${item.id}">归档</button><button class="button small" data-action="preview-reminder" data-id="${item.id}">预览提醒</button>` : active && item.kind === 'notice' ? `<button class="button" data-action="convert" data-id="${item.id}">加入任务</button>` : item.state === 'archived' ? `<button class="button" data-action="restore" data-id="${item.id}">恢复待办</button>` : ''}</div>${item.reminderFallback ? '<div class="change-box">原自定义提醒不再适用，当前使用默认提醒。请重新设置或保留默认。</div>' : ''}<h3 class="section-label">${item.manual ? '任务来源' : '原始消息'}</h3><p class="source-meta">${escapeHTML(item.group)} · ${escapeHTML(item.sender)}<br>${escapeHTML(item.sent)}</p><div class="message">${escapeHTML(item.text)}</div>${item.image ? `<button class="source-image-button" data-action="image" aria-label="放大原图">${imagePreview()}</button><details class="ocr-details"><summary>查看识别文字</summary><p class="ocr">图片文字（示例）：${escapeHTML(item.ocr)}</p></details>${pending && !item.imageSuggestionRejected ? '<div class="change-box"><b>建议日期：10月12日 18:00</b><p>核对原图后确认，才能用于提醒。</p><button class="button small" data-action="use-image-date">填入建议</button><button class="button small quiet" data-action="reject-image-date" data-id="'+item.id+'">拒绝建议</button></div>' : ''}` : ''}${item.history?.length ? `<details><summary>变更与完成记录（${item.history.length}）</summary>${item.history.map(e => `<p class="ocr">${escapeHTML(e.text)}${e.undone ? '（已撤销）' : ''}</p>`).join('')}</details>` : ''}${suggestion || !active && !pending ? '' : pending ? `<h3 class="section-label">补齐并确认</h3>${editForm(item,'pending')}` : convertingId === item.id ? `<h3 class="section-label">加入任务</h3>${editForm(item,'convert')}` : task ? `<details><summary>修改事项信息</summary>${editForm(item,'save')}</details>${reminderForm(item)}` : ''}${task && !pending && item.state !== 'cancelled' ? `<details class="simulation"><summary>体验老师的新消息（示例）</summary><p class="ocr">模拟已能明确关联到此任务的消息，保留原文。</p><div class="detail-actions">${active ? `<button class="button small" data-action="simulate-delay" data-id="${item.id}">延期两天</button><button class="button small" data-action="simulate-date" data-id="${item.id}">新时间只有日期</button><button class="button small" data-action="simulate-cancel" data-id="${item.id}">老师取消任务</button>` : ''}<button class="button small" data-action="simulate-new" data-id="${item.id}">新增提交要求</button></div></details>` : ''}`;
}
function renderDesktop() {
  const tasks = pageItems('tasks'), pending = pageItems('pending');
  $('widget').classList.toggle('only-tasks',pending.length === 0);
  const rows = (list,isPending) => list.map(item => `<article class="mini-row ${!isPending ? overdue(item) ? 'overdue' : item.due === TODAY ? 'today' : '' : ''}"><button class="mini-open" data-action="open" data-id="${item.id}">${escapeHTML(item.title)}${dot(item)}</button><p class="mini-meta">${escapeHTML(item.group)}${isPending ? '<br>'+escapeHTML(item.reason) : item.changeLabel ? '<br>'+escapeHTML(item.changeLabel) : ''}</p><div class="mini-foot">${badge(item)}<span style="color:var(--muted);font-size:10px">${isPending ? '' : (timePast(item) ? item.timeType === 'start' ? '开始已过 · ' : '已逾期 · ' : '')+dateLabel(item)}</span><button class="button small" data-action="${isPending ? 'open' : 'complete'}" data-id="${item.id}">${isPending ? '处理' : '完成'}</button></div></article>`).join('');
  $('widget-grid').innerHTML = `<section class="desktop-card" aria-label="提醒事项卡片"><header class="card-heading"><h2>提醒事项</h2><span>${tasks.length} 项 · ${tasks.filter(i=>i.unread).length} 未读</span></header><div class="card-body">${rows(tasks,false) || '<div class="empty"><h2>暂无待办</h2></div>'}</div></section>${pending.length ? `<section class="desktop-card" aria-label="待确认卡片"><header class="card-heading"><h2>待确认</h2><span>${pending.length} 项 · ${pending.filter(i=>i.unread).length} 未读</span></header><div class="card-body">${rows(pending,true)}</div></section>` : ''}`;
}
function chooseVisible() {
  const list = page === 'settings' ? [] : visibleItems();
  if (!list.some(i => i.id === selectedId)) selectedId = list[0]?.id || null;
  if (!$('guide-dialog').open && !$('app').hidden) {
    const item = items.find(i => i.id === selectedId);
    if (item) item.unread = false;
  }
}
function render() {
  chooseVisible();
  $('breadcrumb').textContent = pages[page];
  renderNav(); renderList(); renderDetail(); renderDesktop(); persist();
}
function navigate(target,id = null,state = 'active') {
  page = target; selectedId = id; convertingId = null; filters = {search:'',course:'',state}; render();
}
function notify(message, undoItem) {
  clearTimeout(toastTimer);
  $('toast').innerHTML = `<span>${escapeHTML(message)}</span>${undoItem && canUndo(undoItem) ? `<button data-action="undo" data-id="${undoItem.id}">撤销</button>` : ''}`;
  $('toast').hidden = false;
  toastTimer = setTimeout(() => $('toast').hidden = true,6000);
}
function applyPreferences() {
  document.documentElement.dataset.theme = settings.theme === 'system' ? systemTheme.matches ? 'dark' : 'light' : settings.theme;
  document.documentElement.dataset.motion = settings.motion;
  const label = document.documentElement.dataset.theme === 'dark' ? '浅色模式' : '深色模式';
  document.querySelectorAll('[data-action="theme"]').forEach(button => { button.textContent = label; button.setAttribute('aria-label','切换到'+label); });
}
systemTheme.addEventListener('change',() => { if (settings.theme === 'system') applyPreferences(); });
function renderSettings() {
  const toggle = (name,label) => `<label class="check"><input type="checkbox" data-setting="${name}" ${settings[name] ? 'checked' : ''}><span>${label}</span></label>`;
  $('list-pane').innerHTML = `<div class="page-head"><div><h1>设置</h1><p class="subline">偏好保存在此浏览器，系统功能仅演示</p></div></div><section class="settings-section"><h2>外观</h2><label class="setting-row">主题<select data-setting="theme">${Object.entries({system:'跟随系统',light:'浅色',dark:'纯黑深色'}).map(([key,label]) => `<option value="${key}" ${settings.theme === key ? 'selected' : ''}>${label}</option>`).join('')}</select></label><label class="setting-row">动画<select data-setting="motion"><option value="system">跟随系统</option><option value="reduce" ${settings.motion === 'reduce' ? 'selected' : ''}>减少动画</option></select></label></section><section class="settings-section"><h2>提醒</h2><div class="setting-row">${toggle('sound','任务声音提醒')}</div><p>作业、考试和提交类任务每个提醒节点请求一次系统声音；普通通知静音。demo 只展示通知内容。</p><div class="setting-row"><span>安静时段</span><span class="tag">暂未启用</span></div><p>有时刻：提前 24 小时和 2 小时。<br>仅日期：前一天中午 12:00。<br>单条任务可在详情中自定义或关闭时间提醒。</p></section><section class="settings-section"><h2>启动与关闭</h2><div class="setting-row">${toggle('autostart','登录 Windows 后自动启动')}</div><label class="setting-row">关闭窗口时<select data-setting="close"><option value="tray">隐藏到托盘</option><option value="exit" ${settings.close === 'exit' ? 'selected' : ''}>直接退出</option></select></label><p>此处不会修改 Windows 启动项或关闭浏览器。</p></section><section class="settings-section"><h2>桌面与演示</h2><p>左右卡片可拖动、缩放，演示布局会保存。<br>QQ、OCR、托盘与系统通知尚未接入。</p><div class="detail-actions"><button class="button small" data-action="desktop">预览桌面卡片</button><button class="button small" data-action="guide">体验引导</button><button class="button small" data-action="reset">重置示例</button></div></section>`;
}
function showDesktop(show) {
  $('desktop').hidden = !show; $('app').hidden = show;
  if (show && widgetLayout) {
    Object.assign($('widget').style,{width:widgetLayout.width+'px',height:widgetLayout.height+'px'});
    $('widget').style.left = Math.max(10,Math.min(innerWidth-$('widget').offsetWidth-10,widgetLayout.left))+'px';
    $('widget').style.top = Math.max(82,Math.min(innerHeight-$('widget').offsetHeight-10,widgetLayout.top))+'px';
  }
}
function log(item,action,text) { (item.history ||= []).push({action,text,at:Date.now()}); }
function complete(item) {
  if (item?.kind !== 'task' || item.state !== 'active') return;
  item.state = 'completed'; item.completedAt = Date.now(); log(item,'completed','标记完成 · '+dateLabel(item));
  resolveReminderSuggestion(item); render(); notify('已完成，可在 1 小时内撤销',item);
}
function undo(item) {
  if (!item || !canUndo(item)) { render(); notify('撤销期限已过'); return; }
  const wasCompleted = item.state === 'completed';
  item.state = wasCompleted ? 'active' : 'pending';
  if (wasCompleted) {
    const record = item.history.findLast(e => e.action === 'completed' && !e.undone);
    if (record) record.undone = true;
    ensureReminderCompatibility(item);
  }
  log(item,'undo','已撤销'+(wasCompleted ? '完成' : '忽略'));
  showDesktop(false); navigate(wasCompleted ? 'tasks' : 'pending',item.id); notify('已撤销');
}
function resolveReminderSuggestion(item) {
  items.filter(i => i.kind === 'reminder' && i.targetId === item.id && ['pending','ignored'].includes(i.state)).forEach(i => i.state = 'resolved');
}
function ensureReminderCompatibility(item) {
  const incompatible = item.reminder?.mode === 'hours' && !item.time;
  item.reminderFallback = incompatible;
  if (incompatible && !items.some(i => i.kind === 'reminder' && i.targetId === item.id && ['pending','ignored'].includes(i.state))) {
    items.push({id:crypto.randomUUID(),kind:'reminder',state:'pending',targetId:item.id,title:'调整提醒 · '+item.title,course:item.course,group:item.group,reason:'原小时提醒不再适用',text:'新正式时间只有日期。当前临时使用前一天中午 12:00 的默认提醒。',sent:'演示变更',sender:'提醒设置',unread:true,history:[]});
  }
  if (!incompatible) resolveReminderSuggestion(item);
}
function acceptTime(item,suggestion,label) {
  Object.assign(item,{due:suggestion.due,time:suggestion.time,changeLabel:label,unread:true});
  log(item,'change',label+' · '+dateLabel(item)); ensureReminderCompatibility(item);
}
function simulate(item,action) {
  if (!item || item.kind !== 'task' || item.state === 'cancelled') return;
  if (action === 'simulate-cancel') {
    item.state = 'cancelled'; item.changeLabel = '老师已取消此任务';
    log(item,'cancelled','示例消息：老师明确取消，停止提醒'); resolveReminderSuggestion(item);
    items.filter(i => i.kind === 'change' && i.targetId === item.id && ['pending','ignored'].includes(i.state)).forEach(i => i.state = 'resolved');
    navigate('tasks',item.id,'cancelled'); notify('示例变更通知：老师已取消任务'); return;
  }
  const base = item.due < TODAY ? TODAY : item.due;
  const due = new Date(Date.parse(base+'T00:00:00Z')+2*86400000).toISOString().slice(0,10);
  const suggestion = {due,time:action === 'simulate-date' ? '' : item.time};
  const label = action === 'simulate-new' ? '新增要求：补交一份结果分析' : action === 'simulate-date' ? '老师更新了日期，未明确时刻' : '老师将时间延后两天';
  const restoreArchived = item.state === 'archived';
  log(item,'message','示例消息：'+label+' · '+dateLabel(suggestion));
  if (restoreArchived || item.protectedTime && (suggestion.due !== item.due || suggestion.time !== item.time)) {
    const existing = items.find(i => i.kind === 'change' && i.targetId === item.id && ['pending','ignored'].includes(i.state));
    if (existing) { notify('此任务已有待核对的变更建议'); render(); return; }
    const candidate = {id:crypto.randomUUID(),kind:'change',state:'pending',targetId:item.id,title:'核对新要求 · '+item.title,course:item.course,group:item.group,reason:restoreArchived ? '是否恢复已归档任务' : '新时间与手动值冲突',oldTime:{due:item.due,time:item.time},suggestion,restoreArchived,restoreCompleted:item.state === 'completed',text:'示例消息：'+label,sent:'演示变更',sender:'课程老师',unread:true,history:[]};
    items.push(candidate); navigate('pending',candidate.id); notify('保留原任务，等待你确认变更'); return;
  }
  item.state = 'active'; acceptTime(item,suggestion,label);
  navigate('tasks',item.id); notify('示例变更通知：'+label);
}
function showGuide() {
  guideStep = 0; renderGuide(); $('guide-dialog').showModal();
}
function renderGuide() {
  const steps = [
    ['连接 QQ','正式应用会引导你连接已经选好的 NapCat。本 demo 不需要登录，直接用示例消息体验。'],
    ['核对课程和群','示例关联：大学物理 ↔ 大学物理实验群；数据结构 ↔ 数据结构课程群。正式应用会提出候选，供你确认或修改。'],
    ['看看提醒与待确认','图片日期先核对再确认。作业有时刻时提前 24 小时和 2 小时提醒；只有日期时前一天 12:00 提醒。点击卡片就能打开详情。']
  ];
  $('guide-dialog').innerHTML = `<p class="eyebrow">首次使用 · ${guideStep+1} / 3 · 示例引导</p><h2>${steps[guideStep][0]}</h2><p>${steps[guideStep][1]}</p><div class="modal-actions"><button class="button" data-action="guide-skip">直接体验</button><button class="button primary" data-action="guide-next">${guideStep === 2 ? '开始体验' : '下一步'}</button></div>`;
}
function finishGuide() { settings.guideDone = true; $('guide-dialog').close(); render(); }
document.addEventListener('click',event => {
  const button = event.target.closest('button[data-action]'); if (!button) return;
  const action = button.dataset.action, item = items.find(i => i.id === button.dataset.id);
  if (action === 'theme') { settings.theme = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark'; applyPreferences(); persist(); if (page === 'settings') renderSettings(); return; }
  if (action === 'desktop') { showDesktop(true); return; }
  if (action === 'back') { showDesktop(false); render(); return; }
  if (action === 'guide') { showGuide(); return; }
  if (action === 'guide-skip') { finishGuide(); return; }
  if (action === 'guide-next') { if (++guideStep < 3) renderGuide(); else finishGuide(); return; }
  if (action === 'close-modal') { button.closest('dialog').close(); return; }
  if (action === 'new') { $('new-dialog').innerHTML = '<h2>新增任务</h2><p>补录口头布置或漏识别的事项，来源标注为“手动添加”。</p>'+editForm({kind:'task',title:'',course:''},'new')+'<div class="modal-actions"><button class="button" data-action="close-modal">取消</button></div>'; $('new-dialog').showModal(); return; }
  if (action === 'image') { $('image-dialog').innerHTML = '<h2>原图示例</h2>'+imagePreview()+'<div class="modal-actions"><button class="button" data-action="close-modal">关闭</button></div>'; $('image-dialog').showModal(); return; }
  if (action === 'preview-reminder') {
    const sound = settings.sound && ['homework','exam','submission'].includes(item.taskType || 'homework');
    $('reminder-dialog').innerHTML = `<p class="eyebrow">提醒通知预览 · 不发送系统通知</p><h2>${escapeHTML(item.title)}</h2><p>${timeName(item)}：${dateLabel(item)}<br>${escapeHTML(item.course)} · 演示账号<br><span id="preview-sound">${sound ? '正式应用请求系统声音' : '静音提醒'}</span> · demo 不播放声音</p><div class="modal-actions"><button class="button" data-action="close-modal">关闭</button><button class="button primary" data-action="notification-open" data-id="${item.id}">打开事项</button></div>`;
    $('reminder-dialog').showModal(); return;
  }
  if (action === 'notification-open') { $('reminder-dialog').close(); showDesktop(false); navigate('tasks',item.id); return; }
  if (action === 'reset') { items = freshItems(); widgetLayout = null; $('widget').removeAttribute('style'); $('toast').hidden = true; navigate('pending'); notify('示例事项与卡片布局已重置，保留你的设置'); return; }
  if (action === 'nav') { navigate(button.dataset.page); return; }
  if (action === 'select') { selectedId = item.id; convertingId = null; render(); return; }
  if (action === 'open') { showDesktop(false); navigate(item.state === 'pending' ? 'pending' : 'tasks',item.id); return; }
  if (action === 'convert') { convertingId = item.id; renderDetail(); return; }
  if (action === 'complete') { complete(item); return; }
  if (action === 'undo') { undo(item); return; }
  if (action === 'ignore') { if (item.state !== 'pending') return; item.state = 'ignored'; item.ignoredAt = Date.now(); render(); notify('已忽略，24 小时内可在“最近忽略”撤销',item); return; }
  if (action === 'archive') { if (item.state !== 'active') return; item.state = 'archived'; log(item,'archived','已归档，停止时间提醒'); resolveReminderSuggestion(item); render(); notify('已归档，可在任务页的“已归档”筛选中恢复'); return; }
  if (action === 'restore') { if (item.state !== 'archived') return; item.state = 'active'; log(item,'restored','手动恢复待办'); ensureReminderCompatibility(item); navigate('tasks',item.id); notify('已恢复待办'); return; }
  if (action === 'use-image-date') { const form = $('detail-pane').querySelector('form[data-mode=pending]'); form.elements.due.value = '2026-10-12'; form.elements.time.value = '18:00'; form.elements.imageConfirmed.checked = false; form.elements.due.focus(); return; }
  if (action === 'reject-image-date') { item.imageSuggestionRejected = true; button.closest('.change-box').remove(); persist(); return; }
  if (action === 'open-reminder') { showDesktop(false); navigate('tasks',item.id); $('reminder-settings').open = true; $('reminder-settings').scrollIntoView({block:'nearest'}); return; }
  if (action === 'default-reminder' || action === 'off-reminder') { const target = items.find(i=>i.id === item.targetId); target.reminder = {mode:action === 'off-reminder' ? 'off' : 'default'}; target.reminderFallback = false; resolveReminderSuggestion(target); render(); notify('提醒设置已更新'); return; }
  if (action === 'accept-change') {
    const target = items.find(i => i.id === item.targetId);
    if (target.state === 'cancelled') { item.state = 'resolved'; render(); notify('原任务已取消'); return; }
    if (item.restoreArchived || item.restoreCompleted) target.state = 'active';
    acceptTime(target,item.suggestion,'已确认老师的新要求'); item.state = 'resolved';
    navigate(target.state === 'active' ? 'tasks' : 'completed',target.id); notify('已接受新时间并更新提醒'); return;
  }
  if (action.startsWith('simulate-')) simulate(item,action);
});
document.addEventListener('input',event => {
  if (event.target.id === 'search') {
    filters.search = event.target.value; chooseVisible();
    // 只替换列表，避免输入每个字时丢失焦点与输入法状态。
    const value = event.target.value, position = event.target.selectionStart;
    renderList(); const search = $('search'); search.focus(); search.value = value; search.setSelectionRange(position,position);
    renderDetail(); renderNav(); renderDesktop(); persist();
  }
  if (['due','time'].includes(event.target.name)) {
    const checkbox = event.target.form?.elements.imageConfirmed;
    if (checkbox) checkbox.checked = false;
  }
});
document.addEventListener('change',event => {
  const input = event.target;
  if (input.id === 'course-filter' || input.id === 'state-filter') { filters[input.id === 'course-filter' ? 'course' : 'state'] = input.value; render(); return; }
  if (input.dataset.setting) { settings[input.dataset.setting] = input.type === 'checkbox' ? input.checked : input.value; applyPreferences(); persist(); return; }
  if (input.name === 'reminderMode') input.form.querySelectorAll('[data-rule]').forEach(el => el.hidden = el.dataset.rule !== input.value);
  if (input.name === 'taskType') input.form.elements.timeType.value = ['exam','activity'].includes(input.value) ? 'start' : 'deadline';
});
document.addEventListener('submit',event => {
  const form = event.target.closest('form[data-mode]'); if (!form) return; event.preventDefault();
  const data = new FormData(form), mode = form.dataset.mode;
  const item = items.find(i => i.id === form.dataset.id);
  const error = message => { form.querySelector('.error').textContent = message; form.querySelector('.error').hidden = false; };
  if (mode === 'reminder') {
    const rule = {mode:data.get('reminderMode')}, hours = Number(data.get('hours')), days = Number(data.get('days')), clock = data.get('clock');
    if (rule.mode === 'hours' && (!item.time || !Number.isFinite(hours) || hours < 0.5 || hours > 8760)) { error('只有明确时刻才能使用小时提醒，请填写 0.5～8760 小时。'); return; }
    if (rule.mode === 'days' && (!Number.isInteger(days) || days < 1 || days > 365 || !/^\d{2}:\d{2}$/.test(clock))) { error('请填写 1～365 的整数天数和有效钟点。'); return; }
    if (rule.mode === 'hours') rule.hours = hours;
    if (rule.mode === 'days') Object.assign(rule,{days,clock});
    item.reminder = rule; item.reminderFallback = false; resolveReminderSuggestion(item); render(); notify('已保存此任务的提醒设置'); return;
  }
  const kind = mode === 'new' || mode === 'convert' ? 'task' : mode === 'pending' ? data.get('kind') : item.kind;
  const title = data.get('title').trim(), course = data.get('course').trim(), due = data.get('due'), time = data.get('time'), timeType = data.get('timeType');
  const message = !title ? '请填写事项标题。' : !course ? '请确认课程或分类。' : kind === 'task' && !due ? '请填写并确认截止日期或开始日期。' : time && !due ? '请同时填写日期。' : item?.image && kind === 'task' && !data.get('imageConfirmed') ? '请查看原图并勾选日期和时刻确认。' : '';
  if (message) { error(message); return; }
  const target = mode === 'new' ? {id:crypto.randomUUID(),group:'手动添加',sender:'我',sent:'演示日期 · 手动录入',text:'手动添加的任务。',manual:true,unread:false,reminder:{mode:'default'},history:[]} : item;
  const protectedTime = mode === 'new' || mode !== 'save' || due !== target.due || time !== target.time || timeType !== target.timeType;
  Object.assign(target,{title,course,due:kind === 'task' ? due : '',time:kind === 'task' ? time : '',timeType,taskType:data.get('taskType'),kind,state:'active',manualPriority:data.get('priority'),imageConfirmed:Boolean(data.get('imageConfirmed'))});
  if (protectedTime) target.protectedTime = true;
  if (mode === 'new') { items.push(target); $('new-dialog').close(); }
  if (kind === 'task') ensureReminderCompatibility(target);
  navigate(kind === 'task' ? 'tasks' : 'notices',target.id); notify(mode === 'pending' ? '已确认事项' : mode === 'new' ? '已添加任务' : '已保存');
});
$('guide-dialog').addEventListener('cancel',() => { settings.guideDone = true; setTimeout(render,0); });
$('widget-handle').addEventListener('pointerdown',event => {
  if (event.button !== 0) return;
  const widget = $('widget'), startX = event.clientX, startY = event.clientY, startLeft = widget.offsetLeft, startTop = widget.offsetTop;
  const handle = event.currentTarget; handle.setPointerCapture(event.pointerId);
  const move = e => { widget.style.left = Math.max(10,Math.min(innerWidth-widget.offsetWidth-10,startLeft+e.clientX-startX))+'px'; widget.style.top = Math.max(82,Math.min(innerHeight-widget.offsetHeight-10,startTop+e.clientY-startY))+'px'; };
  const stop = () => { handle.removeEventListener('pointermove',move); handle.removeEventListener('pointerup',stop); handle.removeEventListener('pointercancel',stop); saveLayout(); };
  handle.addEventListener('pointermove',move); handle.addEventListener('pointerup',stop); handle.addEventListener('pointercancel',stop);
});
function saveLayout() {
  if ($('desktop').hidden) return;
  widgetLayout = {left:$('widget').offsetLeft,top:$('widget').offsetTop,width:$('widget').offsetWidth,height:$('widget').offsetHeight}; persist();
}
new ResizeObserver(saveLayout).observe($('widget'));
window.addEventListener('resize',() => { if (!$('desktop').hidden) showDesktop(true); });
// 更新限时撤销按钮，动作处理仍在点击时检查真实期限。
setInterval(() => {
  const item = items.find(i => i.id === selectedId);
  if (item && !canUndo(item) && $('detail-pane').querySelector('[data-action=undo]')) renderDetail();
},1000);
applyPreferences();
if (!settings.guideDone) showGuide();
render();
