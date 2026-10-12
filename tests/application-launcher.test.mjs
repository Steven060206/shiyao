import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import test from 'node:test';

const root = fileURLToPath(new URL('../', import.meta.url));
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
