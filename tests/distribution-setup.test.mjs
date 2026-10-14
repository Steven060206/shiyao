import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import test from 'node:test';

const project = fileURLToPath(new URL('../', import.meta.url));
const quote = value => `'${value.replaceAll("'", "''")}'`;
const run = script => spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-STA', '-ExecutionPolicy', 'Bypass', '-EncodedCommand',
  Buffer.from(`[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';${script}`, 'utf16le').toString('base64')],
{encoding: 'utf8', windowsHide: true});
const dependencies = ['napimain.exe', 'napiloader.dll', 'nativeLoader.cjs', 'napcat.mjs'];
const napcatSource = 'class OneBotAction {}\nconst WebUiDataRuntime = {};\nexport function NCoreInitFramework() {}\n';

function snapshot(directory) {
  const entries = [];
  function visit(current) {
    for (const entry of fs.readdirSync(current, {withFileTypes: true}).sort((a, b) => a.name.localeCompare(b.name))) {
      const file = path.join(current, entry.name);
      entries.push([path.relative(directory, file), entry.isDirectory() ? null : fs.readFileSync(file).toString('base64')]);
      if (entry.isDirectory()) visit(file);
    }
  }
  visit(directory);
  return entries;
}

function fixture(t, {omitDependency, archivedAccount} = {}) {
  const temporary = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), '拾要 配置回归 ')));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(temporary)), fs.realpathSync.native(os.tmpdir()));
    assert.ok(path.basename(temporary).startsWith('拾要 配置回归 '));
    fs.rmSync(temporary, {recursive: true, force: true});
  });
  const directory = path.join(temporary, '拾要 分发目录');
  const packageDirectory = path.join(directory, 'napcat', 'package');
  const qq = path.join(temporary, 'QQ 安装目录', 'QQ.exe');
  const archiveSource = path.join(temporary, '官方 包原始内容');
  const archive = path.join(directory, 'vendor', 'NapCat.Framework-v4.18.37.zip');
  for (const folder of ['app/ui', 'app-tools', 'bridge', 'vendor']) fs.mkdirSync(path.join(directory, folder), {recursive: true});
  fs.mkdirSync(path.dirname(qq));
  fs.writeFileSync(qq, 'fixture QQ must never be executed');
  fs.writeFileSync(path.join(directory, 'app', 'MessagePurify.exe'), 'fixture app must never be executed during Setup');
  fs.writeFileSync(path.join(directory, 'app', 'ui', 'index.html'), '<html></html>');
  for (const file of ['Setup.ps1', 'Setup.cmd', 'Start.cmd'])
    fs.copyFileSync(path.join(project, 'packaging', 'release', file), path.join(directory, file));
  for (const file of ['Prepare-PinnedGroups.ps1', 'nativeLoaderPinned.cjs', 'pinned-groups.mjs'])
    fs.copyFileSync(path.join(project, 'packaging', 'napcat', file), path.join(directory, 'bridge', file));
  fs.writeFileSync(path.join(directory, 'app-tools', 'Install-MessagePurifyShortcuts.ps1'),
    "param([string]$PublishDirectory)\n[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'installer-called.txt'), $PublishDirectory)\n");
  fs.mkdirSync(path.join(archiveSource, 'config'), {recursive: true});
  fs.writeFileSync(path.join(archiveSource, 'config', 'napcat.json'), '{}');
  for (const file of dependencies) {
    if (file !== omitDependency) fs.writeFileSync(path.join(archiveSource, file), file === 'napcat.mjs' ? napcatSource : 'official fixture');
  }
  if (archivedAccount) fs.writeFileSync(path.join(archiveSource, 'config', `onebot11_${archivedAccount}.json`), '{}');
  const zipped = run(`Compress-Archive -LiteralPath @(${fs.readdirSync(archiveSource).map(file => quote(path.join(archiveSource, file))).join(',')}) -DestinationPath ${quote(archive)}`);
  assert.equal(zipped.status, 0, zipped.stderr);
  const setup = (extra = '', {qqPath = qq, account = '12345678', shortcuts = false} = {}) =>
    run(`& ${quote(path.join(directory, 'Setup.ps1'))} -QqExecutable ${quote(qqPath)} -AccountId ${quote(account)} ${shortcuts ? '' : '-SkipShortcuts'} ${extra}`);
  const settings = path.join(directory, 'app', 'qq-sync.json');
  const config = path.join(packageDirectory, 'config', 'onebot11_12345678.json');
  return {temporary, directory, qq, archive, packageDirectory, settings, config, setup};
}

test('干净分发包在中文空格路径配置本机只读接口，不启动 QQ 或应用、不输出凭据', t => {
  const f = fixture(t);
  const result = f.setup();
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(JSON.parse(fs.readFileSync(f.settings, 'utf8')), {QqExecutable: f.qq});
  assert.notDeepEqual([...fs.readFileSync(f.settings).subarray(0, 3)], [0xef, 0xbb, 0xbf]);
  assert.deepEqual([...fs.readFileSync(path.join(f.directory, 'Setup.ps1')).subarray(0, 3)], [0xef, 0xbb, 0xbf]);
  const configuration = JSON.parse(fs.readFileSync(f.config, 'utf8'));
  const network = configuration.network;
  for (const field of ['httpServers', 'httpSseServers', 'httpClients', 'websocketClients', 'plugins']) assert.deepEqual(network[field], []);
  assert.equal(network.websocketServers.length, 1);
  const endpoint = network.websocketServers[0];
  assert.equal(endpoint.host, '127.0.0.1');
  assert.equal(endpoint.port, 3001);
  assert.equal(endpoint.enable, true);
  assert.equal(endpoint.messagePostFormat, 'array');
  assert.equal(endpoint.debug, false);
  assert.equal(endpoint.reportSelfMessage, false);
  assert.equal(Buffer.from(endpoint.token, 'base64').length, 32);
  assert.equal(configuration.enableLocalFile2Url, false);
  assert.equal(configuration.musicSignUrl, '');
  assert.equal(configuration.imageDownloadProxy, '');
  assert.ok(!result.stdout.includes(endpoint.token));
  assert.ok(!result.stderr.includes(endpoint.token));
  assert.doesNotMatch(result.stdout + result.stderr, /send_group_msg|send_private_msg|\b(?:access_token|token)\b/i);
  assert.equal(fs.readFileSync(path.join(f.packageDirectory, 'napcat.mjs'), 'utf8'), napcatSource);
  assert.match(fs.readFileSync(path.join(f.packageDirectory, 'messagepurify-napcat.mjs'), 'utf8'), /export \{ OneBotAction, WebUiDataRuntime \};/);
  for (const file of ['nativeLoaderPinned.cjs', 'pinned-groups.mjs'])
    assert.deepEqual(fs.readFileSync(path.join(f.packageDirectory, file)), fs.readFileSync(path.join(f.directory, 'bridge', file)));
  assert.equal(fs.existsSync(path.join(f.directory, 'app-tools', 'installer-called.txt')), false);
  assert.equal(fs.readdirSync(path.join(f.directory, 'app')).some(file => file.endsWith('.tmp')), false);
});

test('同账号重复配置逐字节保留账号配置与 token，已有完整包不重新解压', t => {
  const f = fixture(t);
  assert.equal(f.setup().status, 0);
  const config = JSON.parse(fs.readFileSync(f.config, 'utf8'));
  config.personalPreference = '保留自定义配置';
  const preserved = Buffer.from(JSON.stringify(config, null, 3) + '\n', 'utf8');
  fs.writeFileSync(f.config, preserved);
  fs.writeFileSync(f.archive, 'intentionally invalid ZIP: existing package must be reused');
  const marker = path.join(f.packageDirectory, 'existing-package.txt');
  fs.writeFileSync(marker, 'keep');
  const result = f.setup();
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(fs.readFileSync(f.config), preserved);
  assert.equal(fs.readFileSync(marker, 'utf8'), 'keep');
  assert.ok(!result.stdout.includes(config.network.websocketServers[0].token));
  const before = snapshot(f.directory);
  const check = f.setup('-CheckOnly');
  assert.equal(check.status, 0, check.stderr);
  assert.equal(JSON.parse(check.stdout).ConfigurationExists, true);
  assert.deepEqual(snapshot(f.directory), before);
  const savedPath = run(`& ${quote(path.join(f.directory, 'Setup.ps1'))} -AccountId '12345678' -SkipShortcuts -CheckOnly`);
  assert.equal(savedPath.status, 0, savedPath.stderr);
  assert.equal(JSON.parse(savedPath.stdout).QqExecutable, f.qq);
  assert.deepEqual(snapshot(f.directory), before);
});

test('CheckOnly 核对尚未解压的包，文件和进程入口均不写入或运行', t => {
  const f = fixture(t);
  const before = snapshot(f.directory);
  const result = f.setup('-CheckOnly', {shortcuts: true});
  assert.equal(result.status, 0, result.stderr);
  const report = JSON.parse(result.stdout);
  assert.equal(report.Ready, true);
  assert.equal(report.QqExecutable, f.qq);
  assert.equal(report.AccountId, '12345678');
  assert.equal(report.PackageAlreadyExists, false);
  assert.equal(report.ConfigurationExists, false);
  assert.deepEqual(snapshot(f.directory), before);
  assert.equal(fs.existsSync(f.packageDirectory), false);
  const missingAccount = run(`& ${quote(path.join(f.directory, 'Setup.ps1'))} -QqExecutable ${quote(f.qq)} -SkipShortcuts -CheckOnly`);
  assert.notEqual(missingAccount.status, 0);
  assert.match(missingAccount.stderr, /-AccountId/);
  assert.deepEqual(snapshot(f.directory), before);
});

test('相对路径、其他 exe、不存在的 QQ 与非法账号都在写入前拒绝', t => {
  const f = fixture(t);
  const otherExecutable = path.join(path.dirname(f.qq), 'Other.exe');
  fs.writeFileSync(otherExecutable, 'fixture');
  const before = snapshot(f.directory);
  for (const qqPath of [path.relative(f.directory, f.qq), otherExecutable, path.join(f.temporary, 'missing', 'QQ.exe'), '\\QQ.exe']) {
    const result = f.setup('', {qqPath});
    assert.notEqual(result.status, 0, qqPath);
    assert.match(result.stderr, /QQ\.exe.*绝对路径/);
    assert.deepEqual(snapshot(f.directory), before);
  }
  for (const account of ['1234', '1234567890123456', 'abc12345', '../123456', '12345 ', '12345\n']) {
    const result = f.setup('', {account});
    assert.notEqual(result.status, 0, account);
    assert.match(result.stderr, /5 至 15 位数字/);
    assert.deepEqual(snapshot(f.directory), before);
  }
});

test('其他账号配置与已有不完整 package 都被保留，拒绝混用或覆盖', t => {
  const f = fixture(t);
  assert.equal(f.setup().status, 0);
  let before = snapshot(f.directory);
  const otherAccount = f.setup('', {account: '87654321'});
  assert.notEqual(otherAccount.status, 0);
  assert.match(otherAccount.stderr, /其他 QQ 账号.*新文件夹/s);
  assert.deepEqual(snapshot(f.directory), before);
  fs.unlinkSync(path.join(f.packageDirectory, 'napiloader.dll'));
  before = snapshot(f.directory);
  const incomplete = f.setup();
  assert.notEqual(incomplete.status, 0);
  assert.match(incomplete.stderr, /已有 NapCat 目录不完整/);
  assert.deepEqual(snapshot(f.directory), before);
});

test('不完整或带账号配置的 ZIP 在解压前拒绝', t => {
  for (const options of [{omitDependency: 'napiloader.dll'}, {archivedAccount: '99887766'}]) {
    const f = fixture(t, options);
    const before = snapshot(f.directory);
    const result = f.setup();
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /压缩包不完整|压缩包包含账号配置/);
    assert.deepEqual(snapshot(f.directory), before);
  }
});

test('每个新软件文件夹生成独立随机凭据，默认配置调用分发快捷方式入口', t => {
  const first = fixture(t);
  const second = fixture(t);
  for (const f of [first, second]) {
    const result = f.setup('', {shortcuts: true});
    assert.equal(result.status, 0, result.stderr);
    assert.equal(fs.readFileSync(path.join(f.directory, 'app-tools', 'installer-called.txt'), 'utf8'), path.join(f.directory, 'app'));
  }
  const firstToken = JSON.parse(fs.readFileSync(first.config, 'utf8')).network.websocketServers[0].token;
  const secondToken = JSON.parse(fs.readFileSync(second.config, 'utf8')).network.websocketServers[0].token;
  assert.notEqual(firstToken, secondToken);
});

test('Setup.cmd 保持独立路径运行与错误可见，Start.cmd 未配置时停止', t => {
  const f = fixture(t);
  const before = snapshot(f.directory);
  const check = spawnSync('cmd.exe', ['/d', '/c', 'Setup.cmd', '-QqExecutable', f.qq, '-AccountId', '12345678', '-SkipShortcuts', '-CheckOnly'],
    {cwd: f.directory, encoding: 'utf8', input: '\r\n', windowsHide: true});
  assert.equal(check.status, 0, check.stderr);
  assert.match(check.stdout, /"Ready":\s*true/);
  assert.deepEqual(snapshot(f.directory), before);
  const start = spawnSync('cmd.exe', ['/d', '/c', 'Start.cmd'], {cwd: f.directory, encoding: 'utf8', input: '\r\n', windowsHide: true});
  assert.equal(start.status, 1, start.stderr);
  assert.match(start.stdout, /run Setup\.cmd/);
  assert.deepEqual(snapshot(f.directory), before);
  const failed = spawnSync('cmd.exe', ['/d', '/c', 'Setup.cmd', '-QqExecutable', f.qq, '-AccountId', '1234', '-SkipShortcuts'],
    {cwd: f.directory, encoding: 'utf8', input: '\r\n', windowsHide: true});
  assert.notEqual(failed.status, 0);
  assert.match(failed.stderr, /5 至 15 位数字/);
  assert.deepEqual(snapshot(f.directory), before);
  const script = fs.readFileSync(path.join(f.directory, 'Setup.cmd'), 'utf8');
  assert.match(script, /-NoProfile -STA -ExecutionPolicy Bypass -File/);
  assert.match(script, /pause/);
});

test('Start.cmd 直接启动应用且将中文空格 app 目录设为工作目录', async t => {
  const f = fixture(t);
  fs.unlinkSync(path.join(f.directory, 'app', 'MessagePurify.exe'));
  const compiled = run(`Add-Type -TypeDefinition 'public class StartFixture { public static void Main() { System.IO.File.WriteAllText("start-called.txt", System.Environment.CurrentDirectory); } }' -OutputAssembly ${quote(path.join(f.directory, 'app', 'MessagePurify.exe'))} -OutputType WindowsApplication`);
  assert.equal(compiled.status, 0, compiled.stderr);
  fs.writeFileSync(f.settings, JSON.stringify({QqExecutable: f.qq}));
  const started = spawnSync('cmd.exe', ['/d', '/c', 'Start.cmd'], {cwd: f.directory, encoding: 'utf8', windowsHide: true});
  assert.equal(started.status, 0, started.stderr);
  const marker = path.join(f.directory, 'app', 'start-called.txt');
  for (let i = 0; i < 40 && !fs.existsSync(marker); i++) await new Promise(resolve => setTimeout(resolve, 50));
  assert.equal(fs.readFileSync(marker, 'utf8'), path.join(f.directory, 'app'));
  assert.equal(fs.existsSync(f.packageDirectory), false);
  assert.doesNotMatch(fs.readFileSync(path.join(f.directory, 'Start.cmd'), 'utf8'), /powershell|QQ\.exe|NapCat/i);
});
