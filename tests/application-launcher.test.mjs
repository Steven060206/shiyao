import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import test from 'node:test';

const root = fileURLToPath(new URL('../', import.meta.url));
test('默认入口选择新发布目录，保留仍在运行的上一版产物', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'messagepurify-release-'));
  try {
    const entry = path.join(directory, 'packaging/app/Start-MessagePurify.ps1');
    fs.mkdirSync(path.dirname(entry), {recursive:true});
    fs.copyFileSync(path.join(root, 'packaging/app/Start-MessagePurify.ps1'), entry);
    for (const release of ['app', 'app-reminders', 'app-desktop', 'app-tray-fix', 'app-scheduled-sync', 'app-sync-status', 'app-launcher', 'app-desktop-recovery']) {
      const published = path.join(directory, '.tools/published', release);
      fs.mkdirSync(path.join(published, 'ui'), {recursive:true});
      fs.writeFileSync(path.join(published, 'MessagePurify.exe'), release);
      fs.writeFileSync(path.join(published, 'ui/index.html'), '<html></html>');
    }
    const result = spawnSync('powershell.exe', ['-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File', entry, '-CheckOnly'], {encoding:'utf8',windowsHide:true});
    assert.equal(result.status, 0, result.stderr);
    assert.equal(fs.realpathSync.native(JSON.parse(result.stdout).Executable),
      fs.realpathSync.native(path.join(directory, '.tools/published/app-desktop-recovery/MessagePurify.exe')));
    assert.equal(fs.readFileSync(path.join(directory, '.tools/published/app/MessagePurify.exe'), 'utf8'), 'app');
    assert.equal(fs.readFileSync(path.join(directory, '.tools/published/app-reminders/MessagePurify.exe'), 'utf8'), 'app-reminders');
    assert.equal(fs.readFileSync(path.join(directory, '.tools/published/app-desktop/MessagePurify.exe'), 'utf8'), 'app-desktop');
    assert.equal(fs.readFileSync(path.join(directory, '.tools/published/app-tray-fix/MessagePurify.exe'), 'utf8'), 'app-tray-fix');
    assert.equal(fs.readFileSync(path.join(directory, '.tools/published/app-scheduled-sync/MessagePurify.exe'), 'utf8'), 'app-scheduled-sync');
    assert.equal(fs.readFileSync(path.join(directory, '.tools/published/app-sync-status/MessagePurify.exe'), 'utf8'), 'app-sync-status');
    assert.equal(fs.readFileSync(path.join(directory, '.tools/published/app-launcher/MessagePurify.exe'), 'utf8'), 'app-launcher');
  } finally {
    assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
    fs.rmSync(directory, {recursive:true,force:true});
  }
});
test('正式应用入口支持中文空格路径，缺界面文件时停止', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), '消息提纯 app '));
  try {
    fs.writeFileSync(path.join(directory, 'MessagePurify.exe'), 'synthetic check-only executable');
    fs.mkdirSync(path.join(directory, 'ui'));
    fs.writeFileSync(path.join(directory, 'ui/index.html'), '<html></html>');
    const invoke = () => spawnSync('powershell.exe', ['-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
      path.join(root,'packaging/app/Start-MessagePurify.ps1'),'-PublishDirectory',directory,'-CheckOnly'], {encoding:'utf8',windowsHide:true});
    const ready = invoke();
    assert.equal(ready.status,0,ready.stderr);
    assert.equal(JSON.parse(ready.stdout).Ready,true);
    fs.unlinkSync(path.join(directory,'ui/index.html'));
    const missing = invoke();
    assert.notEqual(missing.status,0);
    assert.match(missing.stderr,/UI is missing/);
  } finally {
    assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
    fs.rmSync(directory,{recursive:true,force:true});
  }
});

test('启动入口从唯一启用的本机配置取得鉴权，不输出凭据', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'messagepurify-local-auth-'));
  try {
    fs.writeFileSync(path.join(directory,'MessagePurify.exe'),'check only');
    fs.mkdirSync(path.join(directory,'ui'));
    fs.writeFileSync(path.join(directory,'ui/index.html'),'<html></html>');
    const config = path.join(directory,'config'); fs.mkdirSync(config);
    const secret = 'synthetic-test-auth-value';
    fs.writeFileSync(path.join(config,'onebot11_123.json'),JSON.stringify({network:{websocketServers:[{enable:true,host:'127.0.0.1',port:3001,token:secret}]}}));
    const env = {...process.env}; delete env.MESSAGEPURIFY_NAPCAT_TOKEN;
    const ready = spawnSync('powershell.exe',['-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
      path.join(root,'packaging/app/Start-MessagePurify.ps1'),'-PublishDirectory',directory,'-NapCatConfigurationDirectory',config,'-CheckOnly'],{env,encoding:'utf8',windowsHide:true});
    assert.equal(ready.status,0,ready.stderr);
    assert.equal(JSON.parse(ready.stdout).AuthenticationReady,true);
    assert.equal((ready.stdout+ready.stderr).includes(secret),false);
    fs.writeFileSync(path.join(config,'onebot11_456.json'),fs.readFileSync(path.join(config,'onebot11_123.json')));
    const ambiguous = spawnSync('powershell.exe',['-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
      path.join(root,'packaging/app/Start-MessagePurify.ps1'),'-PublishDirectory',directory,'-NapCatConfigurationDirectory',config,'-CheckOnly'],{env,encoding:'utf8',windowsHide:true});
    assert.notEqual(ambiguous.status,0);
    assert.equal((ambiguous.stdout+ambiguous.stderr).includes(secret),false);
  } finally {
    assert.equal(path.dirname(path.resolve(directory)),path.resolve(os.tmpdir()));
    fs.rmSync(directory,{recursive:true,force:true});
  }
});
