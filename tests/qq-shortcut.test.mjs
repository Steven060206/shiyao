import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const psQuote = value => `'${value.replaceAll("'", "''")}'`;
const run = command => spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-EncodedCommand',
  Buffer.from(`[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); $ErrorActionPreference = 'Stop'; ${command}`, 'utf16le').toString('base64')],
  {encoding: 'utf8', windowsHide: true});
const temporary = t => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'messagepurify-qq-shortcut-'));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
    fs.rmSync(directory, {recursive: true, force: true});
  });
  return directory;
};
const fixture = t => {
  const directory = temporary(t);
  const project = path.join(directory, '项目 根');
  const scripts = path.join(project, 'packaging', 'napcat');
  const packageDirectory = path.join(project, '.tools/napcat/framework/v4.18.37/package');
  fs.mkdirSync(scripts, {recursive: true});
  fs.mkdirSync(packageDirectory, {recursive: true});
  for (const name of ['Install-QQShortcut.ps1', 'Start-NapCat-Framework.ps1'])
    fs.copyFileSync(path.join(root, 'packaging/napcat', name), path.join(scripts, name));
  for (const name of ['napimain.exe', 'napiloader.dll', 'nativeLoader.cjs', 'napcat.mjs'])
    fs.writeFileSync(path.join(packageDirectory, name), 'not-executed');
  return {directory, scripts};
};

test('QQ 桌面快捷方式保留图标、正确引用中文路径并备份原入口，重复安装不覆盖备份', t => {
  const {directory, scripts} = fixture(t);
  const qq = path.join(directory, 'QQ 程序.exe');
  const shortcut = path.join(directory, 'QQ.lnk');
  const backup = path.join(directory, '原始备份');
  fs.writeFileSync(qq, 'not-executed');
  const create = run(`$shell = New-Object -ComObject WScript.Shell; $link = $shell.CreateShortcut(${psQuote(shortcut)}); $link.TargetPath = ${psQuote(qq)}; $link.Arguments = '--original'; $link.Save()`);
  assert.equal(create.status, 0, create.stderr);
  const original = fs.readFileSync(shortcut);
  const install = () => run(`& ${psQuote(path.join(scripts, 'Install-QQShortcut.ps1'))} -ShortcutPath ${psQuote(shortcut)} -QqExecutablePath ${psQuote(qq)} -BackupDirectory ${psQuote(backup)}`);
  const installed = install();
  assert.equal(installed.status, 0, installed.stderr);
  assert.equal(JSON.parse(installed.stdout).Shortcut, shortcut);
  assert.deepEqual(fs.readFileSync(path.join(backup, 'QQ.original.lnk')), original);
  const inspect = run(`$shell = New-Object -ComObject WScript.Shell; $link = $shell.CreateShortcut(${psQuote(shortcut)}); @{Target=$link.TargetPath; Arguments=$link.Arguments; Icon=$link.IconLocation; Style=$link.WindowStyle} | ConvertTo-Json`);
  assert.equal(inspect.status, 0, inspect.stderr);
  const link = JSON.parse(inspect.stdout);
  assert.equal(path.basename(link.Target).toLowerCase(), 'powershell.exe');
  const entry = link.Arguments.match(/-File "([^"]+)"/)?.[1];
  assert.ok(entry, 'Shortcut must quote the launcher script path');
  assert.equal(fs.realpathSync.native(entry), fs.realpathSync.native(path.join(scripts, 'Start-NapCat-Framework.ps1')));
  assert.ok(link.Arguments.includes(`-QqExecutablePath "${qq}"`));
  assert.match(link.Arguments, /-WindowStyle Hidden.*-OpenExisting$/);
  assert.equal(link.Icon, `${qq},0`);
  assert.equal(link.Style, 7);
  assert.equal(install().status, 0);
  assert.deepEqual(fs.readFileSync(path.join(backup, 'QQ.original.lnk')), original);
});

test('QQ 已运行时桌面入口只调用 QQ 激活窗口，普通 Framework 启动仍拒绝重复加载', t => {
  const {directory, scripts} = fixture(t);
  const qq = path.join(directory, 'QQ.exe');
  fs.writeFileSync(qq, 'not-executed');
  const invoke = open => run(`function Get-Process { param($Name, $ErrorAction) @{Id=123} }; function Start-Process { param($FilePath, $WorkingDirectory) @{FilePath=$FilePath; WorkingDirectory=$WorkingDirectory} | ConvertTo-Json }; & ${psQuote(path.join(scripts, 'Start-NapCat-Framework.ps1'))} -QqExecutablePath ${psQuote(qq)} ${open ? '-OpenExisting' : ''}`);
  const opened = invoke(true);
  assert.equal(opened.status, 0, opened.stderr);
  assert.deepEqual(JSON.parse(opened.stdout), {WorkingDirectory: directory, FilePath: qq});
  const refused = invoke(false);
  assert.notEqual(refused.status, 0);
  assert.match(refused.stderr, /already running/);
});
