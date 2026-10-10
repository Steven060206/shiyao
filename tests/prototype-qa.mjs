import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {spawn} from 'node:child_process';
import {fileURLToPath, pathToFileURL} from 'node:url';

const workspace = fileURLToPath(new URL('../', import.meta.url));
const captureScreenshots = process.argv.includes('--screenshots');
const output = path.join(workspace,'prototype/previews');
if (captureScreenshots) fs.mkdirSync(output,{recursive:true});
const profile = fs.mkdtempSync(path.join(os.tmpdir(),'msg-purify-browser-'));
const browser = spawn(process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe',[
  '--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check',
  '--remote-debugging-port=0','--user-data-dir='+profile,
  pathToFileURL(path.join(workspace,'prototype/index.html')).href
],{windowsHide:true,stdio:['ignore','ignore','pipe']});
let socket, stderr = '', counter = 0;
const pending = new Map(), errors = [], checks = [];
browser.stderr.on('data',data => stderr += data.toString());
const delay = ms => new Promise(resolve => setTimeout(resolve,ms));
function assert(condition,message) { if (!condition) throw new Error(message); checks.push(message); }
try {
  const activePortFile = path.join(profile,'DevToolsActivePort');
  for (let i=0;i<120 && !fs.existsSync(activePortFile);i++) await delay(100);
  if (!fs.existsSync(activePortFile)) throw new Error('Browser did not start: '+stderr.slice(-1000));
  const port = fs.readFileSync(activePortFile,'utf8').split('\n')[0];
  let target;
  for (let i=0;i<40 && !target;i++) {
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    target = targets.find(item => item.type === 'page');
    if (!target) await delay(100);
  }
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve,reject) => { socket.addEventListener('open',resolve,{once:true}); socket.addEventListener('error',reject,{once:true}); });
  socket.addEventListener('message',event => {
    const message = JSON.parse(event.data);
    if (message.id) {
      const handlers = pending.get(message.id); pending.delete(message.id);
      if (message.error) handlers?.reject(new Error(JSON.stringify(message.error))); else handlers?.resolve(message.result);
    } else if (message.method === 'Runtime.exceptionThrown') errors.push(message.params.exceptionDetails.text);
  });
  const call = (method,params={}) => new Promise((resolve,reject) => {
    const id = ++counter; pending.set(id,{resolve,reject}); socket.send(JSON.stringify({id,method,params}));
  });
  const evaluate = async expression => {
    const result = await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
    return result.result.value;
  };
  const click = selector => evaluate(`document.querySelector(${JSON.stringify(selector)}).click()`);
  const screenshot = async filename => {
    if (!captureScreenshots) return;
    const result = await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
    fs.writeFileSync(path.join(output,filename),Buffer.from(result.data,'base64'));
  };
  await call('Runtime.enable'); await call('Page.enable');
  await call('Emulation.setEmulatedMedia',{features:[{name:'prefers-reduced-motion',value:'no-preference'}]});
  await call('Emulation.setDeviceMetricsOverride',{width:1440,height:1000,deviceScaleFactor:1,mobile:false});
  for (let i=0;i<50 && !await evaluate('document.readyState === "complete" && document.querySelectorAll(".task-row").length === 4');i++) await delay(100);
  assert(await evaluate('JSON.stringify([...document.querySelectorAll(".task-row")].map(row=>row.dataset.id)) === JSON.stringify(["t4","t1","t2","t3"])'),'Task order preserves overdue and today tasks, followed by priority and date');
  assert(await evaluate('document.querySelector(".task-row[data-id=t4]").classList.contains("overdue") && items.find(i=>i.id === "t4").state === "active"'),'Overdue task remains active with a highlight');
  assert(await evaluate('reminderLabel(items.find(i=>i.id === "t1")) === "10/8 18:00、10/9 16:00" && reminderLabel(items.find(i=>i.id === "t2")) === "10/10 12:00 提醒"'),'Exact-time reminders use 24h and 2h; date-only reminder uses prior-day noon');
  const buttonDebug = await evaluate('(()=>{const s=getComputedStyle(document.querySelector(".button"));return {duration:s.transitionDuration,transform:s.transform,shadow:s.boxShadow}})()');
  assert((buttonDebug.duration.includes('0.16s') || buttonDebug.duration.includes('0.32s')) && buttonDebug.transform === 'none','Buttons have a transition and neutral resting state');
  const hoverCard = await evaluate('(()=>{const r=document.querySelector(".task-row").getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()');
  await call('Input.dispatchMouseEvent',{type:'mouseMoved',x:hoverCard.x,y:hoverCard.y});
  await delay(250);
  assert(await evaluate('getComputedStyle(document.querySelector(".task-row")).transform !== "none" && getComputedStyle(document.querySelector(".task-row")).boxShadow !== "none"'),'Task cards animate with lift and shadow on hover');
  await call('Input.dispatchMouseEvent',{type:'mouseMoved',x:5,y:5});
  await screenshot('main.png');
  assert(await evaluate('document.documentElement.dataset.theme === "light" && document.querySelector("[data-action=theme]").textContent === "深色模式"'),'Theme control starts in light mode and offers dark mode');
  await click('[data-action="theme"]');
  await delay(400);
  const darkDebug = await evaluate('({theme:document.documentElement.dataset.theme,label:document.querySelector("[data-action=theme]").textContent,bodyBackground:getComputedStyle(document.body).backgroundColor,appBackground:getComputedStyle(document.querySelector(".app")).backgroundColor})');
  assert(darkDebug.theme === 'dark' && darkDebug.label === '浅色模式' && darkDebug.bodyBackground === 'rgb(0, 0, 0)','Theme control switches the main interface to pure black');
  assert(await evaluate('getComputedStyle(document.querySelector(".app")).transitionDuration.includes("0.32s")'),'Theme surfaces have a 320ms transition');
  await screenshot('main-dark.png');
  await click('[data-action="theme"]');
  await delay(400);
  await call('Emulation.setDeviceMetricsOverride',{width:1100,height:800,deviceScaleFactor:1,mobile:false});
  assert(await evaluate('document.body.scrollWidth <= innerWidth && document.querySelector(".detail-pane").getBoundingClientRect().right <= innerWidth'),'Main layout fits an 1100px desktop viewport');
  await call('Emulation.setDeviceMetricsOverride',{width:1440,height:1000,deviceScaleFactor:1,mobile:false});
  await click('[data-action="desktop"]');
  assert(await evaluate('!document.querySelector("#desktop").hidden && document.querySelectorAll(".desktop-card").length === 2 && document.querySelectorAll(".mini-row").length === 6'),'Desktop preview presents two cards and excludes ordinary notices');
  assert(await evaluate('document.querySelector(".mini-row.overdue .mini-foot").textContent.includes("10月8日 23:59")'),'Overdue desktop task continues to display its original deadline');
  const desktopCard = await evaluate('(()=>{const r=document.querySelector(".desktop-card").getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()');
  await call('Input.dispatchMouseEvent',{type:'mouseMoved',x:desktopCard.x,y:desktopCard.y});
  await delay(250);
  assert(await evaluate('getComputedStyle(document.querySelector(".desktop-card")).transform !== "none"'),'Desktop cards animate with lift on hover');
  await call('Input.dispatchMouseEvent',{type:'mouseMoved',x:5,y:5});
  await screenshot('desktop.png');
  await click('[data-action="theme"]');
  await delay(400);
  assert(await evaluate('document.documentElement.dataset.theme === "dark" && getComputedStyle(document.querySelector(".desktop")).backgroundColor === "rgb(0, 0, 0)"'),'Desktop preview follows the pure black theme');
  await screenshot('desktop-dark.png');
  await click('[data-action="theme"]');
  await delay(400);
  const handle = await evaluate('(()=>{const r=document.querySelector("#widget-handle").getBoundingClientRect();return {x:r.x+100,y:r.y+18,left:document.querySelector("#widget").offsetLeft}})()');
  await call('Input.dispatchMouseEvent',{type:'mousePressed',x:handle.x,y:handle.y,button:'left',buttons:1,clickCount:1});
  await call('Input.dispatchMouseEvent',{type:'mouseMoved',x:handle.x+40,y:handle.y+20,button:'left',buttons:1});
  await call('Input.dispatchMouseEvent',{type:'mouseReleased',x:handle.x+40,y:handle.y+20,button:'left',buttons:0,clickCount:1});
  assert(await evaluate(`document.querySelector('#widget').offsetLeft === ${handle.left+40}`),'Desktop component can be dragged');
  const grip = await evaluate('(()=>{const r=document.querySelector("#widget").getBoundingClientRect();return {x:r.right-3,y:r.bottom-3,width:r.width}})()');
  await call('Input.dispatchMouseEvent',{type:'mousePressed',x:grip.x,y:grip.y,button:'left',buttons:1,clickCount:1});
  await call('Input.dispatchMouseEvent',{type:'mouseMoved',x:grip.x+60,y:grip.y+35,button:'left',buttons:1});
  await call('Input.dispatchMouseEvent',{type:'mouseReleased',x:grip.x+60,y:grip.y+35,button:'left',buttons:0,clickCount:1});
  assert(await evaluate(`document.querySelector('#widget').getBoundingClientRect().width > ${grip.width}`),'Desktop component can be resized');
  await click('[data-action="back"]');
  await click('[data-action="complete"][data-id="t1"]');
  await click('[data-action="nav"][data-page="completed"]');
  assert(await evaluate('document.querySelectorAll(".task-row").length === 1 && document.querySelector(".message").textContent.includes("10月9日18:00") && document.querySelector("#detail-pane").textContent.includes("停止提醒")'),'Completing a task retains its source evidence in the completed view');
  await click('[data-action="nav"][data-page="pending"]');
  assert(await evaluate('document.querySelector("input[name=due]").value === "" && document.querySelector("input[name=time]").value === ""'),'Image text does not automatically establish a deadline');
  await evaluate('document.querySelector("form").requestSubmit()');
  assert(await evaluate('document.querySelector(".error").textContent.includes("截止日期")'),'Confirming a task with no deadline shows an inline error');
  await evaluate('document.querySelector("input[name=due]").value="2026-10-12";document.querySelector("input[name=time]").value="18:00";document.querySelector("form").requestSubmit()');
  assert(await evaluate('document.querySelector(".error").textContent.includes("勾选") && pageItems("pending").length === 2'),'Image deadline requires explicit human confirmation');
  await evaluate('document.querySelector("input[name=imageConfirmed]").checked=true;document.querySelector("form").requestSubmit()');
  assert(await evaluate('page === "tasks" && pageItems("pending").length === 1 && items.find(i=>i.id === "p1").due === "2026-10-12"'),'Confirmed item moves into tasks');
  await click('[data-action="nav"][data-page="pending"]');
  await click('[data-action="ignore"][data-id="p2"]');
  await click('[data-action="desktop"]');
  assert(await evaluate('document.querySelectorAll(".desktop-card").length === 1 && document.querySelector("#widget").classList.contains("only-tasks")'),'Pending card disappears after all pending items are processed');
  await click('[data-action="back"]');
  await click('[data-action="nav"][data-page="notices"]');
  assert(await evaluate('document.querySelectorAll(".task-row").length === 2'),'Ordinary notifications stay in the main notice list');
  await click('[data-action="select"][data-id="n2"]');
  await click('[data-action="convert"][data-id="n2"]');
  await evaluate('document.querySelector("input[name=due]").value="2026-10-20";document.querySelector("form").requestSubmit()');
  assert(await evaluate('page === "tasks" && items.find(i=>i.id === "n2").kind === "task" && reminderLabel(items.find(i=>i.id === "n2")) === "10/19 12:00 提醒"'),'User can add a notice as a task with a date-only deadline');
  await click('[data-action="desktop"]');
  while (await evaluate('pageItems("tasks").length > 0')) await click('.mini-foot button[data-action="complete"]');
  assert(await evaluate('document.querySelector("#widget-grid").textContent.includes("暂无待办") && pageItems("completed").length === 6'),'Empty desktop state says no tasks and keeps all six completed records');
  await click('[data-action="back"]');
  await click('[data-action="reset"]');
  assert(await evaluate('pageItems("tasks").length === 4 && pageItems("pending").length === 2 && pageItems("completed").length === 0'),'Reset restores only the demonstration data');
  await call('Emulation.setEmulatedMedia',{features:[{name:'prefers-reduced-motion',value:'reduce'}]});
  const reducedMotionCard = await evaluate('(()=>{const r=document.querySelector(".task-row").getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()');
  await call('Input.dispatchMouseEvent',{type:'mouseMoved',x:reducedMotionCard.x,y:reducedMotionCard.y});
  assert(await evaluate('getComputedStyle(document.querySelector(".task-row")).transitionDuration === "0s" && getComputedStyle(document.querySelector(".task-row")).transform === "none" && getComputedStyle(document.querySelector(".task-row")).boxShadow === "none" && getComputedStyle(document.querySelector(".button")).transitionDuration === "0s" && getComputedStyle(document.querySelector(".app")).transitionDuration === "0s"'),'Reduced-motion preference disables transitions and hover animation');
  assert(errors.length === 0,'No JavaScript runtime exceptions');
  console.log(JSON.stringify({passed:checks.length,checks,screenshots:captureScreenshots ? ['prototype/previews/main.png','prototype/previews/main-dark.png','prototype/previews/desktop.png','prototype/previews/desktop-dark.png'] : []},null,2));
} finally {
  if (socket?.readyState === WebSocket.OPEN) socket.close();
  if (browser.pid) spawn('taskkill',['/PID',String(browser.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'});
}
