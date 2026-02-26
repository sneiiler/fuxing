---
name: fuxing-project
description: FuXing 项目的代码导航技能。本项目同时包含前端 C# Word 办公插件和 Python 后端智能体服务两个完全独立的子项目。当用户谈论"前端插件"、"C# 插件"、"Office 插件"、"Word 插件"、"Ribbon"、"任务面板"、"COM 插件"、"AntdUI"等时，This skill should be used to navigate to `fronted_word_tools/`。当用户谈论"智能体"、"Agent"、"记忆"、"RAG"、"文件管理"、"向量嵌入"、"LangGraph"、"Python API"等时，This skill should be used to navigate to `backend_agents/`。
---

# FuXing 项目导航

FuXing 是一个全栈项目，包含两个完全独立的子项目，分别位于不同目录中。在生成或修改代码前，必须先确定用户意图属于哪个子项目，然后在对应目录下操作。

## 子项目速查表

| 关键词 / 话题 | 所属子项目 | 根目录 |
|---|---|---|
| C# 插件、Office 插件、Word 插件、COM 插件 | 前端 Word 插件 | `fronted_word_tools/` |
| Ribbon、任务面板、右键菜单、AntdUI | 前端 Word 插件 | `fronted_word_tools/` |
| 纠错、批注、表格格式化、选中文本操作 | 前端 Word 插件 | `fronted_word_tools/` |
| NetOffice、NetWorkHelper、ConfigLoader | 前端 Word 插件 | `fronted_word_tools/` |
| 智能体、Agent、LangGraph、图（Graph） | 后端智能体 | `backend_agents/` |
| RAG、向量嵌入、文件管理、知识库 | 后端智能体 | `backend_agents/` |
| 记忆（Memory）、会话（Session） | 后端智能体 | `backend_agents/` |
| Python API、FastAPI、流式输出 | 后端智能体 | `backend_agents/` |
| 校对、润色、续写、问答（QA） | 后端智能体（graphs 节点） | `backend_agents/app/graphs/` |

## 前端子项目：`fronted_word_tools/`

**技术栈**：C# / .NET Framework、NetOffice、AntdUI、Newtonsoft.Json

### 关键文件

```
fronted_word_tools/
├── FuXing.cs                # 主插件入口，NetOffice COM 加载项
├── ConfigLoader.cs          # 配置管理（JSON 读写）
├── NetWorkHelper.cs         # HTTP 通信（调用后端 API）
├── RibbonUI.xml             # Ribbon 功能区 XML 定义
├── TaskPaneControl.cs       # 任务面板 UserControl
├── TaskPaneWindow.cs        # 任务面板窗口宿主
├── SettingForm.cs           # 设置界面
├── AboutDialog.cs           # 关于对话框
├── ResourceManager.cs       # 资源（图标）管理
├── Properties/
│   └── AssemblyInfo.cs      # COM 注册信息
└── Resources/               # 图标等资源文件
```

### 开发约定

- 所有 AI 功能通过 `NetWorkHelper` 调用后端 HTTP 接口，**不在前端实现任何 AI 逻辑**
- 配置文件保存为文档目录下的 `office_tools_config.json`
- UI 组件优先使用 AntdUI 库中的控件
- COM 注册脚本：`RegisterPlugin.bat` / `UnregisterPlugin.bat`
- 编译后自动注册：`PostBuild.bat`

### 编译验证流程

修改前端 C# 代码后，**只需编译确认无错误即可**，不要自动启动 Word 或注册 COM：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "fronted_word_tools\build.ps1" -Configuration Debug
```

- 编译成功标志：输出 `FuXing -> ...\FuXing.dll` 且无 `error` 行
- 若 DLL 被 Word 锁定导致复制失败（`MSB3026` warning），属于正常现象——说明编译本身已通过，代码无错误
- **不要主动关闭 Word、不要主动启动 Word、不要主动注册 COM**，用户会自行处理运行环节

---

## 后端子项目：`backend_agents/`

**技术栈**：Python、FastAPI、LangGraph、LangChain、向量数据库

### 关键目录

```
backend_agents/
├── app/
│   ├── main.py              # FastAPI 应用入口
│   ├── api/v1/              # REST API 路由层
│   ├── core/
│   │   ├── config.py        # 全局配置（从环境变量加载）
│   │   ├── errors.py        # 自定义异常
│   │   └── util.py          # 工具函数
│   ├── graphs/              # LangGraph 智能体编排
│   │   ├── router.py        # 主图：路由 → 节点 → review
│   │   ├── state.py         # AgentState 定义
│   │   ├── proofread_agent.py   # 校对节点
│   │   ├── polish_agent.py      # 润色节点
│   │   ├── continue_agent.py    # 续写节点
│   │   ├── qa_agent.py          # 文件问答节点
│   │   ├── review_agent.py      # 结果审核节点
│   │   └── tools/               # 智能体工具函数
│   ├── models/              # Pydantic 数据模型
│   │   ├── chat.py          # 聊天请求/响应
│   │   ├── documents.py     # 文档模型
│   │   ├── sessions.py      # 会话模型
│   │   ├── tasks.py         # 任务模型
│   │   ├── enums.py         # Mode 等枚举值
│   │   └── embedding/       # 向量嵌入相关模型
│   ├── services/
│   │   ├── embedding.py     # 向量嵌入服务
│   │   ├── storage.py       # 文件存储服务
│   │   ├── streaming.py     # SSE 流式输出
│   │   └── formatter.py     # 输出格式化
│   ├── db/                  # 数据库层（向量库等）
│   ├── config/
│   │   └── rag_workflow.yaml  # RAG 工作流配置
│   └── uploads/             # 用户上传文件存储目录
└── tests/                   # 测试文件
```

### LangGraph 工作流

图的执行路径：`router → (proofread | polish | continue_writing | file_qa) → review`

新增智能体节点时：
1. 在 `graphs/` 下新建 `<name>_agent.py`，实现节点函数
2. 在 `graphs/state.py` 中扩展 `AgentState` 字段（如需要）
3. 在 `graphs/router.py` 中注册新节点并连接边
4. 在 `models/enums.py` 中添加对应的 `Mode` 枚举值

### 开发约定

- 所有 API 接口定义在 `api/v1/` 目录下，按资源分文件
- 数据模型统一使用 Pydantic，放在 `models/` 目录
- 配置通过 `core/config.py` 统一管理，支持 `.env` 文件注入
- 流式输出使用 `services/streaming.py` 中的 SSE 实现
- 文件上传后存储在 `uploads/` 目录，文件名格式：`f_<timestamp>__<原始名>`

## 判断流程

遇到代码生成或修改请求时，按以下顺序判断：

1. **关键词匹配**：对照上方速查表，判断话题属于哪个子项目
2. **默认推断**：
   - 涉及 `.cs`、`.xml`、`.csproj` 文件 → `fronted_word_tools/`
   - 涉及 `.py`、`.yaml` 文件（项目相关）→ `backend_agents/`
3. **歧义处理**：若无法判断（如"添加一个新功能"），询问用户："请问是前端 Word 插件（C#）还是后端智能体服务（Python）？"

详细的项目结构说明请参考 `references/project-structure.md`。
