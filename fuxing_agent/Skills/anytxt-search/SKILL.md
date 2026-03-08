---
name: anytxt-search
description: 基于 AnyTxt 本地全文检索引擎的文档搜索能力。当用户需要搜索本地文件内容、查找包含特定关键词的文档、获取文件匹配片段、或对图片进行 OCR 文字识别时使用本技能。通过 web_request 工具调用 AnyTxt 的 HTTP JSON-RPC 2.0 API。
metadata:
  author: fuxing
  version: "1.0"
---

# AnyTxt 本地文档搜索

## 概述

AnyTxt 是一个本地全文检索引擎，通过 ATGUI.exe 在 `http://127.0.0.1:9920` 提供 JSON-RPC 2.0 API。
本技能为福星插件提供基于 AnyTxt 的文档检索能力。

## 前置条件

- AnyTxt 已安装并运行（ATGUI.exe 进程存在）
- 默认端口 9920 未被占用

## 可用 API

所有请求均为 POST，Content-Type 为 `application/json`，遵循 JSON-RPC 2.0 协议。
详细参数定义见 `references/anytxt_api.json`。

### 1. Search — 获取匹配文件数量

快速确认是否有匹配结果及数量，适合在 GetResult 前做预检。

### 2. GetResult — 获取匹配文件列表

返回匹配文件的基本信息列表，支持分页和排序。

### 3. GetFragment — 获取匹配片段

根据文件 ID（从 GetResult 结果中获取）提取关键词命中的上下文片段。

### 4. OCR — 图片文字识别

传入图片绝对路径，返回识别出的文本。

## 调用方式

使用 `web_request` 工具，参数示例：

```json
{
  "url": "http://127.0.0.1:9920",
  "method": "POST",
  "body": {
    "id": 1,
    "jsonrpc": "2.0",
    "method": "ATRpcServer.Searcher.V1.GetResult",
    "params": {
      "input": {
        "pattern": "关键词",
        "filterDir": "C:\\Documents",
        "filterExt": "*.docx",
        "lastModifyBegin": 0,
        "lastModifyEnd": 2147483647,
        "limit": "50",
        "offset": 0,
        "order": 2
      }
    }
  }
}
```

## 典型工作流

1. 用 Search 确认匹配数量
2. 用 GetResult 获取文件列表（注意合理设置 limit）
3. 对感兴趣的文件用 GetFragment 获取匹配上下文
4. 将检索结果呈现给用户或用于后续处理
