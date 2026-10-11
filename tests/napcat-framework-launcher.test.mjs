import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const launcher = fileURLToPath(new URL('../packaging/napcat/Start-NapCat-Framework.ps1', import.meta.url));

test('Framework 入口支持显式 QQ 路径并拒绝缺少运行时依赖', t => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'message-purify-napcat-framework-'));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(temporary)), path.resolve(os.tmpdir()));
    assert.ok(path.basename(temporary).startsWith('message-purify-napcat-framework-'));
    fs.rmSync(temporary, { recursive: true, force: true });
  });
  const packageDirectory = path.join(temporary, 'Framework 包');
  fs.mkdirSync(packageDirectory);
  const qq = path.join(temporary, 'QQ 程序.exe');
  fs.writeFileSync(qq, 'fixture-not-executed');
  for (const file of ['napimain.exe', 'napiloader.dll', 'nativeLoader.cjs', 'napcat.mjs']) {
    fs.writeFileSync(path.join(packageDirectory, file), 'fixture-not-executed');
  }
  const check = () => spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', launcher,
    '-PackageDirectory', packageDirectory, '-QqExecutablePath', qq, '-CheckOnly'], { encoding: 'utf8' });
  const result = check();
  assert.equal(result.status, 0, result.stderr);
  const report = JSON.parse(result.stdout);
  assert.equal(report.Ready, true);
  assert.equal(report.Mode, 'Framework');
  assert.equal(report.RegistryRequired, false);
  assert.equal(report.QqExecutable, qq);

  const defaultRoot = path.join(temporary, '项目 根');
  const defaultScript = path.join(defaultRoot, 'packaging/napcat/Start-NapCat-Framework.ps1');
  const defaultPackage = path.join(defaultRoot, '.tools/napcat/framework/v4.18.37/package');
  fs.mkdirSync(path.dirname(defaultScript), { recursive: true });
  fs.copyFileSync(launcher, defaultScript);
  fs.mkdirSync(defaultPackage, { recursive: true });
  for (const file of ['napimain.exe', 'napiloader.dll', 'nativeLoader.cjs', 'napcat.mjs']) {
    fs.copyFileSync(path.join(packageDirectory, file), path.join(defaultPackage, file));
  }
  const defaultRun = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', defaultScript,
    '-QqExecutablePath', qq, '-CheckOnly'], { encoding: 'utf8', cwd: process.env.SystemRoot });
  assert.equal(defaultRun.status, 0, defaultRun.stderr);
  assert.equal(JSON.parse(defaultRun.stdout).Ready, true, 'Framework 默认目录必须脱离启动 cwd 解析');

  fs.unlinkSync(path.join(packageDirectory, 'napiloader.dll'));
  assert.notEqual(check().status, 0);
  const script = fs.readFileSync(launcher, 'utf8');
  assert.match(script, /Get-Process -Name 'QQ'/, '真实启动前必须检查已有 QQ 进程');
  assert.match(script, /Exit the current QQ before starting NapCat Framework/, '已有 QQ 时必须给出退出提示');
});
