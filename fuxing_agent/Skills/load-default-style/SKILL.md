---
name: load-default-style
description: 文档默认样式应用技能。当用户提出"套用默认样式""统一格式""按规范排版""标题正文表图题注统一"等需求时使用。通过 format_content、format_table、insert_image、insert_caption 等工具，依据 default_style_profile.json 配置应用标准格式。
metadata:
  author: fuxing
  version: "2.0"
---

# Load Default Style Skill

## When To Use
- 用户要求统一文档样式、规范化排版、应用默认模板。
- 用户提到标题层级（1~6级）、正文、表格、图片、题注的格式统一。
- 用户没有给出细节但明确要求"按默认标准处理"。

## Scope
本技能覆盖以下对象的默认样式落地：
- 标题 1~6
- 正文
- 表格
- 图片
- 题注（图/表/公式）

## Source Of Truth
- 配置文件：`references/default_style_profile.json`
- 配置优先级：
  1) 用户在当前请求中的明确参数
  2) `default_style_profile.json`
  3) 工具默认值（仅在配置无该字段时）

## Required Behavior
- 插件启动时会自动创建专属样式（如 `福星_标题1`、`福星_正文`、`福星_题注`、`福星_表格`）。
- 样式名称必须从配置读取，不要硬编码新名称。
- 若文档缺少配置中指定样式：直接报错并请求用户确认，不做兼容回退。
- 能调用工具就直接调用，不只给建议。
- 范围不明确时，先用 `ask_user` 确认范围（全文/选区/节点）。

## Profile Schema (Summary)
- `heading_styles`：1~6 级标题样式名
- `body_style`：正文样式名
- `body_font`：正文字体、字号、对齐、行距
- `table`：表格样式/字体/边框/表头
- `image`：默认对齐与宽度（cm）
- `caption`：图/表/公式题注标签与字体规则

## Tool Mapping
1) 标题/正文
  - 工具：`format_content`
  - 用法：`action="format"` + `target` + `style_name`
  - `style_name` 来自配置（`heading_styles.h1~h6` / `body_style`）
  - 格式化全部正文（含列表项）：使用 `target.type="body_text"` + `style_name` 来自配置的 `body_style`
  - 格式化全部 N 级标题：使用 `target.type="heading_level"` + `target.value="N"` + `style_name` 来自配置的 `heading_styles.hN`
  - 所有格式化操作必须使用福星系列样式（`福星_正文`、`福星_标题1` 等），不要使用 Word 内置样式名

2) 表格
  - 插入：`insert_table`
  - 统一：`format_table`
  - 参数来源：`table`（样式、字体、对齐、表头、边框）

3) 图片
  - 工具：`insert_image`
  - 默认参数：`image.alignment`、`image.default_width_cm`
