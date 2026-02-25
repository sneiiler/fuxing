# FuXing 项目结构详细参考

## 仓库根目录

```
d:\code\fuxing\
├── .github/
│   └── skill/
│       └── fuxing-project/       # 本项目 Copilot 技能
├── backend_agents/               # 后端 Python 智能体服务
├── fronted_word_tools/           # 前端 C# Word 插件
└── .gitignore
```

---

## 前端：`fronted_word_tools/`

### 完整文件树

```
fronted_word_tools/
├── FuXing.sln                    # Visual Studio 解决方案
├── FuXing.csproj                 # 项目文件（.NET Framework）
├── packages.config               # NuGet 包配置
│
├── FuXing.cs                     # 主插件类（NetOffice IExcelAddin）
├── ConfigLoader.cs               # JSON 配置读写
├── NetWorkHelper.cs              # HTTP 请求封装（调用后端）
├── RibbonUI.xml                  # Word Ribbon 功能区定义
├── TaskPaneControl.cs            # 任务面板 WinForms UserControl
├── TaskPaneWindow.cs             # 任务面板 ICTPFactory 宿主
├── SettingForm.cs                # 设置窗体
├── SettingForm.resx              # 设置窗体资源
├── AboutDialog.cs                # 关于对话框
├── IconTestForm.cs               # 图标预览测试窗体
├── ResourceManager.cs            # 图标/资源加载管理
│
├── Properties/
│   ├── AssemblyInfo.cs           # COM 注册信息、GUID、版本号
│   ├── Resources.cs              # 资源访问包装类
│   ├── Resources.Designer.cs     # 自动生成
│   └── Resources.resx            # 嵌入资源定义
│
├── Resources/                    # PNG 图标等二进制资源
│
├── packages/                     # NuGet 本地缓存
│   ├── AntdUI.2.1.1/             # 现代化 UI 组件库
│   ├── NetOfficeFw.Core.1.9.7/   # NetOffice 核心
│   ├── NetOfficeFw.Office.1.9.7/ # NetOffice Office 层
│   ├── NetOfficeFw.Word.1.9.7/   # NetOffice Word 对象模型
│   └── Newtonsoft.Json.13.0.3/   # JSON 序列化
│
├── RegisterPlugin.bat            # 手动注册 COM（管理员运行）
├── UnregisterPlugin.bat          # 注销 COM
├── QuickReregister.bat           # 快速重新注册
├── PostBuild.bat                 # 编译后自动注册脚本
└── PostBuild-Setup.md            # PostBuild 配置说明
```

### 核心类说明

| 类名 | 职责 |
|---|---|
| `FuXing` | 插件主入口，实现 `IWordAddin`，挂载 Ribbon 和上下文菜单事件 |
| `ConfigLoader` | 读写 `office_tools_config.json`，管理服务器地址等配置 |
| `NetWorkHelper` | 封装 `HttpClient`，向后端发起同步/异步 POST 请求 |
| `TaskPaneControl` | 主任务面板 UI，包含 AI 功能入口按钮 |
| `SettingForm` | 配置界面，使用 AntdUI 组件 |
| `ResourceManager` | 从 `Resources/` 目录加载图标资源 |

---

## 后端：`backend_agents/`

### 完整文件树

```
backend_agents/
├── pyproject.toml                # Python 项目元数据
├── requirements.txt              # 依赖列表
├── README.md
│
├── app/
│   ├── main.py                   # FastAPI app 实例、lifespan、挂载路由
│   ├── __init__.py
│   │
│   ├── api/
│   │   ├── __init__.py
│   │   └── v1/                   # v1 版本路由（按资源分文件）
│   │
│   ├── config/
│   │   └── rag_workflow.yaml     # RAG 检索工作流配置参数
│   │
│   ├── core/
│   │   ├── config.py             # 全局配置（Pydantic Settings，读取 .env）
│   │   ├── errors.py             # 自定义异常类
│   │   └── util.py               # 通用工具函数
│   │
│   ├── db/
│   │   └── __init__.py           # 数据库连接/向量库初始化
│   │
│   ├── graphs/                   # LangGraph 智能体编排层
│   │   ├── state.py              # AgentState TypedDict 定义
│   │   ├── router.py             # 主图构建（StateGraph），orchestrator 对象
│   │   ├── proofread_agent.py    # 校对节点
│   │   ├── polish_agent.py       # 润色节点
│   │   ├── continue_agent.py     # 续写节点
│   │   ├── qa_agent.py           # 文件问答（RAG）节点
│   │   ├── review_agent.py       # 结果审核/格式化节点
│   │   ├── tools/                # 节点使用的工具函数
│   │   └── README.md
│   │
│   ├── models/                   # Pydantic 数据模型层
│   │   ├── chat.py               # ChatRequest / ChatResponse
│   │   ├── common.py             # DocRef 等共享模型
│   │   ├── documents.py          # 文档相关模型
│   │   ├── sessions.py           # 会话模型
│   │   ├── tasks.py              # 异步任务模型
│   │   ├── files.py              # 文件上传模型
│   │   ├── qa.py                 # 问答请求/响应模型
│   │   ├── enums.py              # Mode 枚举（chat/proofread/polish/continue/qa）
│   │   ├── errors.py             # 错误响应模型
│   │   ├── tools.py              # 工具调用模型
│   │   └── embedding/            # 向量嵌入相关模型
│   │
│   ├── services/                 # 业务服务层
│   │   ├── embedding.py          # 向量化、相似度检索
│   │   ├── storage.py            # 文件存储（上传/读取/删除）
│   │   ├── streaming.py          # SSE 流式响应生成器
│   │   └── formatter.py          # 输出格式化（Markdown 等）
│   │
│   ├── workers/
│   │   └── __init__.py           # 后台任务 Worker
│   │
│   └── uploads/                  # 运行时文件上传目录
│       └── f_<timestamp>__<name> # 文件命名规范
│
└── tests/
    ├── test_health.py
    ├── test_upload.py
    └── test_embedding_simple.py
```

### AgentState 核心字段

`graphs/state.py` 中的 `AgentState` 在图的所有节点间传递，主要字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `mode` | `Mode` | 任务模式（路由依据） |
| `doc` | `DocRef` | 文档引用（doc_id + doc_version_id） |
| `selection_range` | `Optional[...]` | 选中文本范围（为 None 则全文操作） |
| `user_message` | `str` | 用户输入内容 |
| `tool_choice` | `Optional[str]` | 强制指定工具 |
| `text_reply` | `str` | 最终输出给前端的文本 |

### API 接口约定

- 基础路径：`/api/v1/`
- 流式接口：返回 `text/event-stream`（SSE），使用 `services/streaming.py`
- 文件上传：`multipart/form-data`，存入 `app/uploads/`
- 错误格式：统一使用 `models/errors.py` 中的错误响应结构

### 新增功能检查清单

**新增 Agent 节点**：
- [ ] `graphs/<name>_agent.py` — 节点实现
- [ ] `graphs/state.py` — 扩展 AgentState（如需新字段）
- [ ] `graphs/router.py` — 注册节点 + 连接图边
- [ ] `models/enums.py` — 添加 Mode 枚举值
- [ ] `api/v1/` — 添加对应 API 端点（如需要）

**新增 API 端点**：
- [ ] `api/v1/<resource>.py` — 路由定义
- [ ] `models/` — 请求/响应 Pydantic 模型
- [ ] `app/main.py` — 挂载新路由器
