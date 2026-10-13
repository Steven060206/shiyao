import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import test from 'node:test';

const script = fileURLToPath(new URL('./watch_application.ps1', import.meta.url));
test('无人值守观察拒绝其他进程，并保存无私人内容的失败报告', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'messagepurify-watch-'));
  try {
    const reportPath = path.join(directory, 'observation.json');
    const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
      '-File', script, '-ProcessId', String(process.pid), '-Report', reportPath, '-Seconds', '1'],
      {encoding:'utf8', windowsHide:true, timeout:15000});
    assert.equal(result.status, 1, result.stderr);
    const report = JSON.parse(fs.readFileSync(reportPath, 'utf8').replace(/^\uFEFF/, ''));
    assert.equal(report.completed, true);
    assert.equal(report.success, false);
    assert.equal(report.failure, 'unexpected-process');
    assert.equal(report.sampleCount, 0);
    assert.deepEqual(report.samples, []);
    assert.equal(process.kill(process.pid, 0), true, '观察脚本不能终止被观察进程');
    assert.equal(fs.existsSync(reportPath + '.tmp'), false);
  } finally { fs.rmSync(directory, {recursive:true, force:true}); }
});

test('无人值守观察遇到已退出的进程时保存失败报告', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'messagepurify-watch-'));
  try {
    const reportPath = path.join(directory, 'observation.json');
    const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
      '-File', script, '-ProcessId', '2147483647', '-Report', reportPath, '-Seconds', '1'],
      {encoding:'utf8', windowsHide:true, timeout:15000});
    assert.equal(result.status, 1, result.stderr);
    const report = JSON.parse(fs.readFileSync(reportPath, 'utf8').replace(/^\uFEFF/, ''));
    assert.equal(report.completed, true);
    assert.equal(report.success, false);
    assert.equal(report.failure, 'observation-unavailable');
    assert.equal(report.sampleCount, 0);
    assert.deepEqual(report.samples, []);
    assert.equal(fs.existsSync(reportPath + '.tmp'), false);
  } finally { fs.rmSync(directory, {recursive:true, force:true}); }
});
