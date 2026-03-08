原则

尽量避免界面出现的emoji，也避免这种字符：✓ 

优先使用库（如Agent Framework/AntdUI）等提供的能力，而不是自己手写相关的功能空间

视觉保持一致性设计，色调以蓝色及其相容色为主基调

---

功能设计：
（1）taskpane是我们福星插件AI原生设计的入口，承载了整个插件的所有功能；
（2）当插件第一次打开的时候，应该弹出用户使用告示（类似于免责说明）
（3）当每个新的session打开的时候，应该请求大模型，获取用户的问候语并显示；
（4）当每个新的session打开的时候，都应该自动进行快速文档感知，并更新到对话状态
---
下面直接给你一套 **Word + LLM 插件 / 自动化** 的稳妥架构，目标是：

* **Word 不假死**
* **COM 不乱线程**
* **LLM 慢也不拖 UI**
* **可取消、可重试、可增量更新**
* **后面能扩展到批注、改写、润色、审校**

---

# 一、总原则

**一句话：**

> **所有 Word COM 访问，只放在 STA 主线程；所有 LLM/文本分析，都放后台线程。**

不要让后台线程直接碰：

* `Application`
* `Document`
* `Range`
* `Selection`
* `Paragraphs`
* `Comments`
* `Revisions`

这些全都只允许在 **Word 所在 STA 线程** 操作。

---

# 二、推荐分层

建议拆成 5 层：

```text
[1] Word COM Adapter（STA）
[2] Document Snapshot / Diff Layer
[3] Task Orchestrator
[4] LLM Worker Pool
[5] Apply-back Writer（STA）
```

---

## 1. Word COM Adapter（STA 层）

职责：

* 从 Word 读文档
* 获取选区 / 段落 / 样式 / 批注位置
* 把结果写回 Word
* 处理 Word 的对象生命周期

这一层必须满足：

* **单线程**
* **串行执行**
* **所有 COM 调用都在同一个 STA 线程**

### 这一层不要做的事

不要在这里做：

* LLM 调用
* 大文本处理
* 正则大扫描
* embedding / rerank
* diff 算法
* json 清洗

这一层只做 Word IO。

---

## 2. Document Snapshot / Diff Layer

这是关键层。

从 Word 读出来后，不要把 `Range`、`Paragraph` 这些 COM 对象传到后台。
只传 **纯内存数据结构**。

例如：

```json
{
  "documentId": "doc-123",
  "version": 17,
  "selection": {
    "start": 1024,
    "end": 2088
  },
  "blocks": [
    {
      "blockId": "p1",
      "type": "paragraph",
      "start": 0,
      "end": 120,
      "text": "第一段内容...",
      "style": "Normal"
    },
    {
      "blockId": "p2",
      "type": "paragraph",
      "start": 121,
      "end": 260,
      "text": "第二段内容...",
      "style": "Heading 1"
    }
  ]
}
```

### 快照里放什么

建议放：

* 文档唯一标识
* 版本号
* 选区起止
* 段落列表
* 纯文本
* 块级边界
* 样式名
* 表格单元格坐标（如果要支持表格）
* 批注锚点范围
* 修订模式状态

### 不要放什么

不要把这些带出去：

* `Range COM对象`
* `Document COM对象`
* `Selection COM对象`

后台只认快照，不认 Word 对象。

---

## 3. Task Orchestrator（任务编排层）

这是中控。

职责：

* 接收用户动作
* 创建任务
* 排队
* 取消旧任务
* 管理版本
* 收集后台结果
* 决定是否允许写回

### 建议任务模型

```text
用户点击“润色选中内容”
→ 生成 Job
→ 读取当前文档快照 version=17
→ 后台跑 LLM
→ 返回 patch
→ 检查当前文档是否还是 version=17
    - 是：允许写回
    - 否：拒绝直接写回，提示“文档已变化，请重新应用”
```

### 任务状态建议

```text
Created
ReadingSnapshot
Queued
RunningLLM
PostProcessing
ReadyToApply
Applying
Completed
Cancelled
Failed
```

### 这里一定要有“版本控制”

因为用户在 LLM 处理期间可能继续改 Word。

所以每次任务必须绑定：

* `documentId`
* `snapshotVersion`
* `selectionRange`
* `requestId`

写回前必须再比对一次。

---

## 4. LLM Worker Pool（后台工作层）

职责：

* 调 LLM
* 分块
* prompt 构造
* 重试
* 结果清洗
* 合并 patch
* 生成结构化输出

这一层完全不要碰 COM。

### 推荐设计

后台线程只吃这种输入：

```json
{
  "jobId": "job-001",
  "action": "polish",
  "snapshot": {...},
  "options": {
    "tone": "professional",
    "trackChanges": true
  }
}
```

输出这种结果：

```json
{
  "jobId": "job-001",
  "baseVersion": 17,
  "patches": [
    {
      "type": "replace",
      "start": 1050,
      "end": 1180,
      "newText": "修改后的内容"
    }
  ],
  "explanations": [
    {
      "range": [1050, 1180],
      "reason": "语句更简洁"
    }
  ]
}
```

### 推荐不要让 LLM 直接输出整篇文档

最好输出：

* `replace(start, end, newText)`
* `insert(pos, text)`
* `comment(start, end, text)`

而不是：

* “这是完整新文档，请全部替换”

因为整篇替换很容易：

* 丢格式
* 丢批注
* 丢域代码
* 丢目录
* 丢修订信息

---

## 5. Apply-back Writer（写回层，STA）

这层负责把后台结果安全落回 Word。

### 写回原则

写回时：

1. 切回 STA
2. 重新获取当前文档对象
3. 校验文档版本/选区
4. 把 patch 应用到 `Range`
5. 必要时开启/关闭 Track Changes
6. 释放短生命周期 COM 对象

### 推荐写回方式

优先级通常是：

#### A. 精准 Range 替换

适合纯文本改写。

```text
range.Start = x
range.End = y
range.Text = newText
```

#### B. 批注写入

适合“建议但不直接改”。

```text
Comments.Add(Range, "建议内容")
```

#### C. 修订模式下替换

适合让用户看改动痕迹。

先控制 Track Changes，再替换 Range。

---

# 三、线程模型

推荐这个线程模型：

```text
Thread 1: Word UI / COM STA Thread
- 读文档
- 写文档
- 处理 Ribbon / Task Pane UI 事件

Thread 2..N: Background Worker Threads
- LLM 请求
- 文本清洗
- diff
- 规则检查
```

### 最关键的边界

后台线程和 STA 线程之间只传：

* string
* int
* array
* json
* immutable DTO

不要传：

* COM 对象
* 动态代理
* Word interop 引用

---

# 四、推荐数据流

```text
用户点击按钮
→ UI线程发起命令
→ STA线程读取选区文本 + 上下文
→ 生成 Snapshot DTO
→ 丢给 Orchestrator
→ 后台线程调用 LLM
→ 返回结构化 Patch
→ Orchestrator 校验版本
→ 将 Apply 操作 post 回 STA
→ STA线程写回 Word
→ UI提示完成
```

可以写成伪代码：

```csharp
async Task RunRewriteCommand()
{
    var snapshot = await staDispatcher.InvokeAsync(() =>
        wordAdapter.CaptureSelectionSnapshot());

    var job = orchestrator.CreateJob(snapshot);

    var result = await llmService.ProcessAsync(job);

    if (!orchestrator.CanApply(result))
        return;

    await staDispatcher.InvokeAsync(() =>
        wordAdapter.ApplyPatches(result));
}
```

---

# 五、组件建议

---

## 1. StaDispatcher

专门封装“回 STA 执行”。

### 作用

后台想操作 Word 时，不直接调，而是：

```text
staDispatcher.InvokeAsync(() => adapter.ApplyPatch(...))
```

### 你可以把它理解为

Word 世界的唯一入口。

---

## 2. WordAdapter

封装所有 Word Interop。

接口可以这样：

```csharp
public interface IWordAdapter
{
    DocumentSnapshot CaptureDocumentSnapshot();
    SelectionSnapshot CaptureSelectionSnapshot();
    void ApplyPatches(PatchSet patches);
    void AddComments(CommentSet comments);
    int GetDocumentVersion();
}
```

这样以后替换实现、测试 mock 都方便。

---

## 3. SnapshotBuilder

负责把 Word 里的结构转成内存对象：

* 段落切分
* 表格切分
* 选区映射
* 坐标记录

---

## 4. PatchEngine

职责：

* 比对旧文本和新文本
* 生成最小 patch
* 防止整段全替换

建议尽量自己控制 patch 格式，不要完全相信模型自由输出。

---

## 5. ApplyGuard

写回前安全检查：

* 文档还是不是同一个
* 版本号是否变化
* 选区是否漂移
* 当前是否正在编辑表格/页眉/批注窗格
* 文档是否只读 / 受保护

---

# 六、版本控制怎么做最稳

最简单可行的方式：

### 方案 A：快照版本号

每次读文档时：

* 给当前快照一个递增版本号
* 用户每次编辑后 version++（可由事件触发）

写回前比对 `baseVersion == currentVersion`

优点：简单。
缺点：用户哪怕改了别处，也会冲掉结果。

### 方案 B：局部哈希

记录：

* 当前选区文本 hash
* 前后文 hash

写回前重新算一次。只有局部没变才写。

这个更实用。

例如：

```json
{
  "selectionTextHash": "abc123",
  "prefixHash": "p001",
  "suffixHash": "s001"
}
```

只要选区附近没变，就允许应用。

### 最推荐

**版本号 + 局部哈希** 一起用。

---

# 七、Word 里最容易踩坑的点

---

## 1. 不要长期持有 Range

`Range` 是活对象，文档一改，位置可能漂。

正确做法：

* 读取时立刻转成 `start/end`
* 写回时重新获取新 `Range`

---

## 2. 不要依赖 Selection

`Selection` 会被用户乱动。

正确做法：

* 任务启动时把 `Selection.Start/End` 拿出来
* 后面全用固定坐标或锚点

---

## 3. 大文档不要一次全丢给 LLM

建议：

* 选区任务：只传选区 + 少量上下文
* 全文任务：按章节 / 段落分块

---

## 4. 不要直接全文替换

全文替换会毁很多 Word 特性：

* 样式边界
* 表格
* 域
* 交叉引用
* 批注锚点
* 修订

---

## 5. COM 对象要及时释放

如果你是 .NET + Office Interop，尤其要注意：

* 不要在循环里乱拿 COM 子对象不释放
* 不要链式调用一长串 COM 属性
* 用完尽量显式释放短生命周期对象

否则 Word 进程容易残留。

---

# 八、推荐三种工作模式

---

## 模式 1：只读分析模式

适合：

* 摘要
* 风险扫描
* 术语检查
* 逻辑检查

流程：

* 读 Word
* 后台分析
* 返回侧边栏结果
* 不写文档

这是最稳的。

---

## 模式 2：建议模式

适合：

* 给改写建议
* 生成批注
* 审校提示

流程：

* 读 Word
* 后台分析
* 回 STA
* 用批注写建议

风险比直接替换低很多。

---

## 模式 3：自动改写模式

适合：

* 润色
* 改语气
* 缩写扩写
* 纠错

流程：

* 读选区
* 后台处理
* 结构化 patch
* 回 STA 写回

这是最强但也最容易出问题的模式，建议最后做。

---

# 九、一个比较靠谱的最小 MVP

先别做太全，按这个最小版本来：

### 功能

* 只支持“对选中文本进行润色”
* 不支持表格
* 不支持页眉页脚
* 不支持跨故事区域
* 只处理纯文本段落
* 写回方式为：替换当前 Range
* 可选开启修订模式

### 流程

1. 用户选中文本
2. STA 读取：

   * start/end
   * text
   * 前后各 300 字上下文
   * 当前文档 id
3. 后台调用 LLM
4. 返回新文本
5. STA 再校验选区原文是否还一致
6. 一致则替换，不一致则提示冲突

这个版本就已经很好用了。

---

# 十、推荐目录结构

如果你是 C# / VSTO / Add-in Express 这类，可以这样分：

```text
/WordAddin
  /UI
    RibbonController.cs
    TaskPaneView.cs

  /Interop
    WordAdapter.cs
    StaDispatcher.cs
    ComUtils.cs

  /DocumentModel
    DocumentSnapshot.cs
    SelectionSnapshot.cs
    BlockDto.cs
    PatchDto.cs

  /Orchestration
    JobManager.cs
    ApplyGuard.cs
    CancellationRegistry.cs

  /LLM
    LlmClient.cs
    PromptBuilder.cs
    ResponseParser.cs
    PatchEngine.cs

  /Infrastructure
    Logger.cs
    RetryPolicy.cs
    JsonUtil.cs
    Config.cs
```

---

# 十一、异常处理建议

必须区分这几类错误：

### 1. COM 错误

比如：

* RPC_E_CALL_REJECTED
* 文档关闭
* Word 忙碌
* 对象失效

处理方式：

* 只在 STA 捕获
* 短重试
* 不行就提示用户重试

### 2. LLM 错误

比如：

* 超时
* 限流
* 返回格式坏了

处理方式：

* 后台重试
* 保留原文
* 不影响 Word

### 3. 版本冲突

用户已经改了文档。

处理方式：

* 不强写
* 给“重新生成”或“查看建议”选项

---

# 十二、性能优化建议

### 1. 读一次，算很久，写一次

你前面那句话就是这个思想，完全对。

### 2. 尽量只读选区，不读全文

尤其是大文档。

### 3. 上下文裁剪

只给模型必要上下文，不要整个文档。

### 4. Patch 小而精

写回次数越少越稳。

### 5. UI 和任务解耦

按钮点下去后，UI 只显示状态，不要卡在同步调用里。

---

# 十三、你可以直接照抄的核心伪代码

```csharp
public async Task RewriteSelectionAsync()
{
    CancellationTokenSource cts = new();

    // 1. STA读取
    var snapshot = await _sta.InvokeAsync(() =>
    {
        return _wordAdapter.CaptureSelectionSnapshot();
    });

    // 2. 后台处理
    var result = await Task.Run(async () =>
    {
        var llmOutput = await _llm.RewriteAsync(snapshot.Text, snapshot.Context, cts.Token);
        var patch = _patchEngine.BuildReplacePatch(snapshot, llmOutput);
        return new RewriteResult(snapshot.DocumentId, snapshot.Version, patch);
    }, cts.Token);

    // 3. 写回前校验
    var canApply = await _sta.InvokeAsync(() =>
    {
        return _applyGuard.Validate(result);
    });

    if (!canApply)
        return;

    // 4. STA写回
    await _sta.InvokeAsync(() =>
    {
        _wordAdapter.ApplyPatches(result.PatchSet);
    });
}
```

---

# 十四、最终推荐方案

如果你要的是 **稳定优先**，我建议你按这个路线做：

### 第一阶段

* 只做选区级处理
* 只支持纯文本段落
* 所有 Word 操作都在 STA
* 后台只处理字符串
* 写回前做文本一致性校验

### 第二阶段

* 支持批注模式
* 支持修订模式
* 支持分块 patch
* 支持取消任务

### 第三阶段

* 支持表格
* 支持整章处理
* 支持术语库 / 风格库
* 支持多轮建议和人工确认

---

# 十五、一句话版架构图

```text
Word(STA)
  └─ WordAdapter
       ├─ Capture Snapshot
       └─ Apply Patch

Snapshot DTO
  └─ JobManager / Orchestrator
       └─ Background LLM Workers
            ├─ Prompt
            ├─ LLM Call
            ├─ Parse
            └─ Patch Build

ApplyGuard
  └─ marshal back to STA
       └─ Apply to Word safely
```

---

你要是愿意，我下一条可以直接给你一份 **C# 可落地的类设计 + 接口定义**，按 VSTO/COM 插件风格写。
