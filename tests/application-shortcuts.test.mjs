import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import test from 'node:test';

const root = fileURLToPath(new URL('../', import.meta.url));
const quote = value => `'${value.replaceAll("'", "''")}'`;
const run = script => spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-EncodedCommand',
  Buffer.from(`[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';${script}`, 'utf16le').toString('base64')],
  {encoding:'utf8',windowsHide:true});
function fixture(t) {
  const directory = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), '消息提纯 shortcut ')));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(directory)), fs.realpathSync.native(os.tmpdir()));
    fs.rmSync(directory,{recursive:true,force:true});
  });
  const scripts = path.join(directory,'项目 根','packaging','app');
  const published = path.join(directory,'项目 根','.tools','published','app-launcher');
  fs.mkdirSync(scripts,{recursive:true}); fs.mkdirSync(path.join(published,'ui'),{recursive:true});
  for (const name of ['Install-MessagePurifyShortcuts.ps1','Start-MessagePurify.ps1'])
    fs.copyFileSync(path.join(root,'packaging/app',name),path.join(scripts,name));
  fs.writeFileSync(path.join(published,'MessagePurify.exe'),'not executed');
  fs.writeFileSync(path.join(published,'ui/index.html'),'<html></html>');
  const destinations = [path.join(directory,'桌面'),path.join(directory,'开始 菜单')];
  const install = extra => run(`& ${quote(path.join(scripts,'Install-MessagePurifyShortcuts.ps1'))} -ShortcutDirectories @(${destinations.map(quote).join(',')}) ${extra || ''}`);
  const inspect = shortcut => {
    const result = run(`$shell=New-Object -ComObject WScript.Shell;$link=$shell.CreateShortcut(${quote(shortcut)});@{Target=$link.TargetPath;Arguments=$link.Arguments;Icon=$link.IconLocation;WorkingDirectory=$link.WorkingDirectory;Style=$link.WindowStyle;Description=$link.Description}|ConvertTo-Json`);
    assert.equal(result.status,0,result.stderr);
    return JSON.parse(result.stdout);
  };
  return {scripts,published,destinations,install,inspect};
}

test('消息提纯桌面与开始菜单入口直接打开应用，正确引用中文路径且保留 QQ 图标', t => {
  const {scripts,published,destinations,install,inspect} = fixture(t);
  fs.mkdirSync(destinations[0]);
  const qqPath = path.join(destinations[0],'QQ.lnk');
  fs.writeFileSync(qqPath,'ordinary QQ shortcut remains untouched');
  const before = fs.readFileSync(qqPath);
  for (let attempt=0;attempt<2;attempt++) {
    const result = install(); assert.equal(result.status,0,result.stderr);
    const installed = JSON.parse(result.stdout);
    assert.deepEqual(installed.Shortcuts,destinations.map(dir=>path.join(dir,'消息提纯.lnk')));
    for (const shortcut of installed.Shortcuts) {
      const link = inspect(shortcut);
      assert.equal(link.Target,path.join(published,'MessagePurify.exe'));
      assert.equal(link.Arguments,'');
      assert.equal(link.Icon,`${path.join(published,'MessagePurify.exe')},0`);
      assert.equal(link.WorkingDirectory,published);
      assert.equal(link.Style,1);
      assert.equal(link.Description,'消息提纯');
    }
    assert.deepEqual(fs.readFileSync(qqPath),before);
    assert.doesNotMatch(result.stdout,/token|QQ\.exe|Framework/);
  }
});

test('消息提纯快捷入口保留指定发布目录，启动不写入凭据', t => {
  const {destinations,published,install,inspect} = fixture(t);
  const result = install(`-PublishDirectory ${quote(published)}`);
  assert.equal(result.status,0,result.stderr);
  assert.equal(inspect(path.join(destinations[0],'消息提纯.lnk')).Target,path.join(published,'MessagePurify.exe'));
  assert.doesNotMatch(inspect(path.join(destinations[0],'消息提纯.lnk')).Arguments,/token|NapCat/);
});

test('发布缺失时停止创建两个入口，保留已有文件', t => {
  const {published,destinations,install} = fixture(t);
  fs.unlinkSync(path.join(published,'ui/index.html'));
  const result = install();
  assert.notEqual(result.status,0);
  assert.match(result.stderr,/UI is missing/);
  for (const dir of destinations) assert.equal(fs.existsSync(path.join(dir,'消息提纯.lnk')),false);
});

test('同名入口属于其他程序时停止，保留原快捷方式且不创建另一入口', t => {
  const {destinations,install} = fixture(t);
  fs.mkdirSync(destinations[1],{recursive:true});
  const conflicting = path.join(destinations[1],'消息提纯.lnk');
  const created = run(`$shell=New-Object -ComObject WScript.Shell;$link=$shell.CreateShortcut(${quote(conflicting)});$link.TargetPath=Join-Path $env:SystemRoot 'System32/notepad.exe';$link.Save()`);
  assert.equal(created.status,0,created.stderr);
  const original = fs.readFileSync(conflicting);
  const result = install();
  assert.notEqual(result.status,0);
  assert.match(result.stderr,/Existing shortcut was preserved/);
  assert.deepEqual(fs.readFileSync(conflicting),original);
  assert.equal(fs.existsSync(path.join(destinations[0],'消息提纯.lnk')),false);
});
