import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import test from 'node:test';

const assets=fileURLToPath(new URL('../src/MessagePurify.Windows/App/ui/assets/',import.meta.url));

test('拾要原图可被 Windows 解码，圆角外透明且中心接近不透明', () => {
  const png=path.join(assets,'shiyao.png').replaceAll("'","''");
  const script=`Add-Type -AssemblyName System.Drawing;$image=[Drawing.Bitmap]::new('${png}');try{@{Width=$image.Width;Height=$image.Height;CornerAlpha=$image.GetPixel(0,0).A;CenterAlpha=$image.GetPixel([int]($image.Width/2),[int]($image.Height/2)).A}|ConvertTo-Json}finally{$image.Dispose()}`;
  const result=spawnSync('powershell.exe',['-NoProfile','-NonInteractive','-EncodedCommand',Buffer.from(script,'utf16le').toString('base64')],{encoding:'utf8',windowsHide:true});
  assert.equal(result.status,0,result.stderr);
  const image=JSON.parse(result.stdout);
  assert.equal(image.Width,image.Height); assert.ok(image.Width>=1024);
  assert.equal(image.CornerAlpha,0); assert.ok(image.CenterAlpha>=250);
});

test('Windows 图标含全部九档尺寸和有效的 PNG 图像帧', () => {
  const icon=fs.readFileSync(path.join(assets,'shiyao.ico'));
  assert.equal(icon.readUInt16LE(0),0); assert.equal(icon.readUInt16LE(2),1);
  assert.equal(icon.readUInt16LE(4),9);
  const sizes=[];
  for (let i=0;i<9;i++) {
    const entry=6+i*16, size=icon[entry]||256;
    sizes.push(size); assert.equal(icon[entry+1]||256,size);
    const length=icon.readUInt32LE(entry+8),offset=icon.readUInt32LE(entry+12);
    assert.ok(offset>=150 && offset+length<=icon.length);
    assert.deepEqual(icon.subarray(offset,offset+8),Buffer.from([137,80,78,71,13,10,26,10]));
    assert.equal(icon.readUInt32BE(offset+16),size); assert.equal(icon.readUInt32BE(offset+20),size);
  }
  assert.deepEqual(sizes,[16,20,24,32,40,48,64,128,256]);
});
