import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const launcher = fileURLToPath(new URL('../packaging/napcat/Start-NapCat.ps1', import.meta.url));

test('NapCat 启动准备可使用无注册表的 QQ 路径，并拒绝缺依赖', t => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'message-purify-napcat-'));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(temporary)), path.resolve(os.tmpdir()));
    assert.ok(path.basename(temporary).startsWith('message-purify-napcat-'));
    fs.rmSync(temporary, { recursive: true, force: true });
  });
  const packageDirectory = path.join(temporary, '启动 包');
  fs.mkdirSync(packageDirectory);
  const qq = path.join(temporary, 'QQ 程序.exe');
  fs.writeFileSync(qq, 'fixture-not-executed');
  for (const file of ['qqnt.json', 'loadNapCat.js', 'NapCatWinBootHook.dll', 'NapCatWinBootMain.exe', 'napcat.mjs']) {
    fs.writeFileSync(path.join(packageDirectory, file), 'fixture-not-executed');
  }
  const check = () => spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', launcher,
    '-PackageDirectory', packageDirectory, '-QqExecutablePath', qq, '-CheckOnly'], { encoding: 'utf8' });
  const result = check();
  assert.equal(result.status, 0, result.stderr);
  const report = JSON.parse(result.stdout);
  assert.equal(report.Ready, true);
  assert.equal(report.RegistryRequired, false);
  assert.equal(report.QqExecutable, qq);
  const defaultRoot = path.join(temporary, '项目 根');
  const defaultScript = path.join(defaultRoot, 'packaging/napcat/Start-NapCat.ps1');
  fs.mkdirSync(path.dirname(defaultScript), { recursive: true });
  fs.copyFileSync(launcher, defaultScript);
  fs.cpSync(packageDirectory, path.join(defaultRoot, '.tools/napcat/v4.18.37'), { recursive: true });
  const defaultRun = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', defaultScript,
    '-QqExecutablePath', qq, '-CheckOnly'], { encoding: 'utf8', cwd: process.env.SystemRoot });
  assert.equal(defaultRun.status, 0, defaultRun.stderr);
  assert.equal(JSON.parse(defaultRun.stdout).Ready, true, 'Windows PowerShell 默认目录必须脱离启动cwd解析');
  fs.unlinkSync(path.join(packageDirectory, 'NapCatWinBootHook.dll'));
  assert.notEqual(check().status, 0);
});
