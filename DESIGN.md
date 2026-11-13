---
name: 拾要
description: WPF 与 WebView2 正式应用的温润个人助理视觉规范。
colors:
  paper: "#F5F3EC"
  paper-raised: "#FFFEFA"
  ink: "#25332D"
  muted: "#5F6B63"
  moss: "#315B46"
  selected-bg: "#E6EDE3"
  line-soft: "#DBDED5"
  border-control: "#788578"
  warning: "#815A14"
  warning-bg: "#F8ECD6"
  danger: "#AB3838"
  danger-bg: "#F7E5E2"
  paper-dark: "#000000"
  paper-raised-dark: "#171D19"
  ink-dark: "#EAECE3"
  muted-dark: "#A7B0A4"
  moss-dark: "#A5C8AC"
  selected-bg-dark: "#24382B"
  line-soft-dark: "#2E3931"
  border-control-dark: "#718575"
  warning-dark: "#E0BC74"
  warning-bg-dark: "#433719"
  danger-dark: "#EF9990"
  danger-bg-dark: "#482420"
typography:
  page-title:
    fontFamily: '"Microsoft YaHei UI", "Segoe UI", sans-serif'
    fontSize: "24px"
    fontWeight: 650
    lineHeight: 1.3
  detail-title:
    fontFamily: '"Microsoft YaHei UI", "Segoe UI", sans-serif'
    fontSize: "20px"
    fontWeight: 650
    lineHeight: 1.5
  item-title:
    fontFamily: '"Microsoft YaHei UI", "Segoe UI", sans-serif'
    fontSize: "16px"
    fontWeight: 600
    lineHeight: 1.6
  body:
    fontFamily: '"Microsoft YaHei UI", "Segoe UI", sans-serif'
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.6
  label:
    fontFamily: '"Microsoft YaHei UI", "Segoe UI", sans-serif'
    fontSize: "12px"
    fontWeight: 400
    lineHeight: 1.6
rounded:
  tag: "4px"
  control: "6px"
  group: "8px"
spacing:
  step-1: "4px"
  step-2: "8px"
  step-3: "12px"
  step-4: "16px"
  step-6: "24px"
  step-8: "32px"
---

# Design System: 拾要

## Overview

**Creative North Star: "安静的个人助理"**

米白纸张、墨绿主色与少量暖金服务于日常事项处理。标题、时间、来源、待核对问题和下一步操作形成稳定层级；沿用拾要名称和图标。

本规范提取自正式应用的 [app.css](src/MessagePurify.Windows/App/ui/app.css)、[index.html](src/MessagePurify.Windows/App/ui/index.html)、[DesktopCardHost.cs](src/MessagePurify.Windows/App/DesktopCardHost.cs) 与 [WindowTheme.cs](src/MessagePurify.Windows/App/WindowTheme.cs)。产品约束以 [requirements.md](docs/requirements.md) 和[批准方案](docs/superpowers/plans/2026-11-13-shiyao-ui-redesign.md)为准；验证、部署和发布状态见 [HANDOFF.md](docs/HANDOFF.md)。

**Key Characteristics:**

- 温润而安静的色彩与系统字体。
- 标题、时间、来源和主要动作有稳定层级。
- 列表与详情独立滚动，长流程固定提交。
- 桌面卡片保留半透明底色和不透明文字。

## Colors

前置 token 为数值依据；无后缀为浅色，`-dark` 为同角色深色值。

### Primary

墨绿 `moss` 用于主要动作、当前导航和焦点；`selected-bg` 承载选中层。

### Secondary

暖金 `warning` 与 `warning-bg` 用于注意、待核对和建议。

### Tertiary

`danger` 与 `danger-bg` 用于错误、逾期和高优先级提示；未读点沿用错误强调色。

### Neutral

`paper` 为主背景，`paper-raised` 为面板，`ink` 与 `muted` 区分正文和辅助文字，`line-soft` 与 `border-control` 区分分隔线和控件边界。深色主背景必须保留纯黑，面板使用暖黑，选中层使用深墨绿。

**The 语义配对 Rule.** 浅深主题按相同角色切换，状态前景与底色成对使用，状态同时提供文字依据。

正文和占位文字的验收目标至少为 4.5:1，控件和焦点至少为 3:1。

## Typography

Web 使用前置系统字体栈，不加载在线字体；WPF 使用 Microsoft YaHei UI。正文与表单采用 body；说明文字（13px），小按钮至少（13px），辅助文字至少（12px）。日期与数量使用等宽数字特性。

列表标题最多两行，详情保留完整标题。原文保留换行并允许长 URL 换行；待确认原文默认展开，其余长原文可收为六行后展开。

WPF 标题与事项标题（16 DIP、半粗体）、正文（13 DIP）、来源和时间（12 DIP）。CSS px 与 WPF DIP 按各自平台解释。

## Layout

应用填满宿主内容区，不设外围大留白。原生最小窗口（1000×620 DIP），Web 按扣除标题栏后的实际内容区布局。

| Web 可用宽度 | 导航宽度 | 详情宽度 | 横向内边距 |
|---|---|---|---|
| 至少 1200px | 208px | 420px | 24px |
| 不超过 1199px | 168px | 360px | 20px |

列表占剩余空间，顶栏最小高度（64px）。搜索默认单独一行，宽度至少（1500px）时筛选合为一行；高度不超过（660px）时收紧纵向留白。

列表工具区固定，事项与详情独立滚动，长流程提交固定底部。设置占满工作区，四组为外观与桌面、消息与课程、数据与备份、启动；分组和定位导航最大宽度（1000px），组内说明区最大（760px）。定位仅在容器内滚动并聚焦标题，URL 和 fragment 不变。

## Elevation & Depth

普通分区依靠留白、分隔线和背景层次。按钮悬停阴影为 `0 4px 10px var(--shadow-soft)`，事项悬停为 `0 2px 6px var(--shadow-soft)`，页面反馈条为 `0 6px 22px var(--shadow-hover)`。主窗口不使用整窗悬浮阴影。

悬停位移约（1px），操作反馈（160ms），换肤过渡（320ms），使用 ease。系统或应用减少动画时移除过渡、悬停位移和悬停阴影；不使用逐项进场动画。

## Shapes

控件、分组和标签采用前置 rounded 的对应层级，普通内容不重复套圆角框。WPF 桌面外框保留（16 DIP）圆角、（1 DIP）边框、（5 DIP）透明外缘；内部事项行（8 DIP），完成按钮（6 DIP）。卡片外缘 DropShadowEffect 参数为 BlurRadius（10）、ShadowDepth（2）、Opacity（0.16）。

## Components

主要按钮使用主色实底，普通按钮使用面板底与控件边线，安静按钮使用透明底。普通按钮与输入框最小高度（36px），小按钮（32px），确认按钮（40px）；焦点描边（2px）、偏移（3px），禁用透明度（0.55）。失败保留输入，错误靠近表单。

导航按处理、记录分组，设置置底；当前页采用选中底、主色和加重文字，保留 aria-current。计数与未读含义分开。普通事项行使用底部分隔线，选中行使用选中底色；标题和状态优先，随后呈现课程/来源及正式时间。

详情组织标题、来源、状态、正式时间或核对问题、事项操作、原文与图片、编辑/确认、关联与历史。原文保留来源和发送时间，OCR 独立折叠。固定确认区包含去向说明和准确操作名称。图片对话框保持在窗口内，关闭后恢复焦点。

桌面两卡共享文字与状态角色，浅色底采用纸张色，深色底采用暖黑面板色。底色透明度（0～100%），默认浅色（13%）、深色（20%），文字不透明。保留拖动、缩放、约（2 DIP）吸附、不置顶、不抢焦点及无对应事项时隐藏。原生标题栏跟随主题，深色为黑色，保留原生窗口控制。

## Do's and Don'ts

### Do:

- **Do** 保留米白、墨绿、少量暖金与纯黑深色主背景。
- **Do** 保持清晰文字层级、独立滚动、固定提交和可见焦点。
- **Do** 保留桌面圆角、透明外缘和不透明文字。

### Don't:

- **Don't** 更换拾要名称或图标，引入在线字体或另造品牌表达。
- **Don't** 用重复面包屑、卡片套卡片或整窗阴影增加负担。
- **Don't** 仅靠颜色解释状态，或继续缩小辅助字号。
- **Don't** 让设置定位改变 URL，或让换肤、导航、图片查看触发 QQ 同步。
