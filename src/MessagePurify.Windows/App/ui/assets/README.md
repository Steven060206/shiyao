# 拾要 · 图标

“拾”：从消息中拾取；“要”：重要且需要处理的事项。读作 shí yào。
深青绿表示安静和秩序，米白消息气泡表示原始消息，暖金折页表示从中拾出的重点。

- `shiyao.png`：内置 image_gen 生成的 RGBA 原图（1254 × 1254），外侧透明。
- `shiyao.ico`：同一图像编码为 Windows 多尺寸图标，包含 16、20、24、32、40、48、64、128、256 像素，供 exe、窗口、托盘和快捷方式使用。
- 正式页面使用本地 PNG；窗口与托盘使用嵌入 WPF 的 ICO 资源。
- 名称是本轮按用户要求提供的设计，尚未收到用户的外观反馈。

## 最终生成提示词

生成方式：内置 image_gen，transparent_background=true；随后用 Pillow 编码 ICO，未使用生成 API 或 CLI。

```text
Use case: logo-brand
Asset type: final Windows desktop software app icon, square 1024 by 1024.
Primary request: Design a beautiful, distinctive, refined icon for a Chinese local message-organizing productivity app named 拾要 (Shiyao), meaning gently collecting what matters from a sea of messages. It extracts school announcements and actionable to-dos from noisy group messages and quietly presents them on the desktop.
Subject: one bold, beautifully balanced ivory folded sheet/message glyph. The silhouette subtly fuses a chat bubble with a gently lifted folded page; an elegant warm golden folded corner represents the important piece that has been picked out. A single unified mark, memorable and legible at 24 pixels, generous shapes and no thin outlines.
Style/medium: polished contemporary app icon, restrained dimensional paper form with subtle soft depth and very smooth edges, graphic logo quality, quiet and premium rather than cartoonish.
Composition/framing: square icon tile centered, large rounded corners, rich deep petrol green / dark teal tile, glyph occupying about 62% of tile, a little breathing room around it. Tile fills 94% of the canvas. Exterior of rounded tile genuinely transparent.
Color palette: deep teal #173E3B to #285C55 with a very subtle tonal gradient, warm ivory #FAF6EA glyph, muted warm gold #D9B475 folded corner. Strong contrast and a crisp silhouette.
Lighting/mood: quiet, reassuring, intelligent, purposeful. Extremely restrained highlight/shadow confined inside icon.
Text: none, no letters, no Chinese characters, no wordmark.
Constraints: one finished usable app icon only; no mockups, no presentation sheet, no device, no background scene, no captions, no watermarks, no stars or sparkles, no checklist or generic checkmark, no many small details. Genuine alpha transparency outside the rounded tile; retain opaque richly colored tile and opaque glyph.
```
