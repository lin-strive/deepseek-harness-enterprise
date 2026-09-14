# 超智能 Harness 设计 Tokens

## 色彩

- `brand-logo`: `#F26A21`，Logo 图形标中的高识别机动橙；
- `brand-700`: `#C14E12`，主按钮、焦点和活动状态；
- `brand-800`: `#A93E0A`，主按钮悬停；
- `brand-050`: `#FFF4EC`，品牌浅色可信面板；
- `neutral-950`: `#17202A`，主文本；
- `neutral-700`: `#465463`，次级文本；
- `neutral-500`: `#718096`，辅助文本；
- `neutral-200`: `#DCE3EA`，输入框与分隔线；
- `neutral-100`: `#EEF2F6`，次级背景；
- `neutral-050`: `#F7F9FB`，窗口背景；
- `surface`: `#FFFFFF`；
- `danger-700`: `#B42318`；
- `danger-050`: `#FEF3F2`；
- `success-700`: `#027A48`；
- `success-050`: `#ECFDF3`。

全屏范围内只使用机动橙作为强调色；红绿仅用于错误和成功语义，并同时提供图标或文字。

## 字体

- UI：`Segoe UI, Microsoft YaHei UI, Microsoft YaHei, sans-serif`；
- 正文：15px / 1.55；
- 辅助文字：13px / 1.45；
- 页面标题：30px / 1.2，SemiBold，字距 `-0.02em`；
- 分区标题：18px / 1.35，SemiBold；
- 数字与状态：启用等宽数字。

## 间距

- 基础单位：4px；
- 刻度：4、8、12、16、24、32、48、64；
- 表单列宽：384px；
- 窗口最小尺寸：960 × 640；
- 控件最小高度：44px。

## 圆角与阴影

- 输入框：6px；
- 按钮：8px；
- 信息块：10px；
- 对话确认面板：14px；
- 主面板阴影：`0 16px 48px rgba(23,32,42,.08)` 与 `0 2px 8px rgba(23,32,42,.06)`。

## 动效

- 悬停颜色：120ms ease-out；
- 步骤切换：200ms ease-out；
- 加载超过 200ms 才显示；
- 尊重 Windows“显示动画”与“减少动态效果”设置；
- 禁止弹跳、持续漂浮和装饰性动画。
