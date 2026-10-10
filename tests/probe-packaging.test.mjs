import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const installer = fileURLToPath(new URL('../packaging/probe/Install-Probe.ps1', import.meta.url));
const runner = fileURLToPath(new URL('../packaging/probe/Run-Probe.ps1', import.meta.url));

test('当前用户探针安装支持空格和中文路径，重复复制且不改源文件', t => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'message-purify-packaging-'));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(temporary)), path.resolve(os.tmpdir()));
    assert.ok(path.basename(temporary).startsWith('message-purify-packaging-'));
    fs.rmSync(temporary, { recursive: true, force: true });
  });
  const source = path.join(temporary, '发布 源');
  const destination = path.join(temporary, '安装 目录');
  fs.mkdirSync(path.join(source, 'ui'), { recursive: true });
  fs.writeFileSync(path.join(source, 'MessagePurify.Windows.IntegrationProbe.exe'), 'fixture');
  fs.writeFileSync(path.join(source, 'ui/index.html'), 'fixture-ui');
  const install = extra => spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', installer,
    '-PublishDirectory', source, '-InstallDirectory', destination, ...extra], { encoding: 'utf8' });
  const dryRun = install(['-WhatIf']);
  assert.equal(dryRun.status, 0, dryRun.stderr);
  assert.equal(fs.existsSync(destination), false);
  for (let index = 0; index < 2; index++) {
    const result = install([]);
    assert.equal(result.status, 0, result.stderr);
    assert.equal(fs.readFileSync(path.join(destination, 'ui/index.html'), 'utf8'), 'fixture-ui');
    assert.equal(fs.readFileSync(path.join(source, 'MessagePurify.Windows.IntegrationProbe.exe'), 'utf8'), 'fixture');
    for (const entry of ['Run-Probe.ps1', 'Bridge.cmd', 'Desktop.cmd', 'Notification.cmd', 'SilentNotification.cmd', 'ColdNotification.cmd']) {
      assert.ok(fs.statSync(path.join(destination, entry)).isFile(), entry);
    }
  }
  fs.unlinkSync(path.join(source, 'ui/index.html'));
  assert.notEqual(install([]).status, 0, '不完整发布产物必须拒绝安装');
});

test('双击运行入口向真实子进程传递各模式的独立参数和非零退出码', t => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'message-purify-runner-'));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(temporary)), path.resolve(os.tmpdir()));
    assert.ok(path.basename(temporary).startsWith('message-purify-runner-'));
    fs.rmSync(temporary, { recursive: true, force: true });
  });
  const directory = path.join(temporary, '运行 目录');
  fs.mkdirSync(directory);
  fs.copyFileSync(runner, path.join(directory, 'Run-Probe.ps1'));
  const compiler = path.join(directory, 'compile.ps1');
  fs.writeFileSync(compiler, `Add-Type -OutputType ConsoleApplication -OutputAssembly (Join-Path $PSScriptRoot 'MessagePurify.Windows.IntegrationProbe.exe') -TypeDefinition @'
using System;
using System.IO;
public static class Fixture {
  public static int Main(string[] args) {
    File.WriteAllLines(Environment.GetEnvironmentVariable("MESSAGEPURIFY_TEST_ARGUMENTS"), args);
    return int.Parse(Environment.GetEnvironmentVariable("MESSAGEPURIFY_TEST_EXIT"));
  }
}
'@\n`);
  const build = spawnSync('powershell.exe', ['-NoProfile', '-File', compiler], { encoding: 'utf8' });
  assert.equal(build.status, 0, build.stderr);
  const captured = path.join(directory, 'args.txt');
  for (const [mode, expected] of [
    ['Bridge', ['--bridge']], ['Desktop', ['--interactive-desktop']],
    ['NormalNotification', ['--notification', 'normal']],
    ['SilentNotification', ['--notification', 'silent']],
    ['ColdNotification', ['--notification', 'normal', '--exit-after-send']]
  ]) {
    const result = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
      path.join(directory, 'Run-Probe.ps1'), '-Mode', mode, '-WaitSeconds', '2'], {
      cwd: process.env.SystemRoot, encoding: 'utf8',
      env: { ...process.env, MESSAGEPURIFY_TEST_ARGUMENTS: captured, MESSAGEPURIFY_TEST_EXIT: '3' }
    });
    assert.equal(result.status, 3, result.stderr);
    const args = fs.readFileSync(captured, 'utf8').trim().split(/\r?\n/);
    assert.deepEqual(args.slice(0, expected.length + 3), [...expected, '--wait-seconds', '2', '--report']);
    assert.equal(args.length, expected.length + 4);
    assert.equal(fs.realpathSync.native(path.dirname(args.at(-1))).toLowerCase(),
      fs.realpathSync.native(path.join(directory, 'results')).toLowerCase());
  }
});
