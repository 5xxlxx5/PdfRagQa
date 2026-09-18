# PdfRagQa

工业产品 PDF 智能问答系统。对**使用手册**与**宣传手册**采用不同的解析与检索链路，
答案可溯源到原文页码与坐标框。

需求与验收标准见 [`docs/需求分析.md`](docs/需求分析.md)。

---

## 一、运行前置

| 依赖 | 要求 | 说明 |
| --- | --- | --- |
| .NET SDK | 10.0+ | 项目目标框架 net10.0 |
| SQL Server | **2025 及以上** | 向量检索依赖原生 `VECTOR` 类型与 `VECTOR_DISTANCE` 函数。开发用 LocalDB 即可 |
| 模型服务 | OpenAI 兼容接口 | 对话 / 视觉 / 向量化三项，可来自不同厂商 |

检查环境：

```bash
dotnet --version      # 应输出 10.x
sqllocaldb info       # 应列出 MSSQLLocalDB
```

---

## 二、配置模型服务

密钥**不要**写进 `src/PdfRagQa.Api/appsettings.json`（该文件纳入版本控制），
写到 `src/PdfRagQa.Api/appsettings.Development.json`（已在 `.gitignore` 中）：

```json
{
  "Logging": { "LogLevel": { "Default": "Information" } },
  "Ai": {
    "Chat":      { "ApiKey": "sk-..." },
    "Vision":    { "ApiKey": "sk-..." },
    "Embedding": { "ApiKey": "sk-..." }
  }
}
```

这里**只覆盖密钥**。端点地址与模型名在 `appsettings.json` 中预置，避免两处配置漂移：

```json
"Ai": {
  "Chat":      { "BaseUrl": "https://dashscope.aliyuncs.com/compatible-mode/v1", "Model": "deepseek-v4.1-flash" },
  "Vision":    { "BaseUrl": "https://dashscope.aliyuncs.com/compatible-mode/v1", "Model": "deepseek-v4.1-flash" },
  "Embedding": { "BaseUrl": "https://dashscope.aliyuncs.com/compatible-mode/v1", "Model": "text-embedding-v4" },
  "EmbeddingDimensions": 1024,
  "EmbeddingBatchSize": 10,
  "RenderDpi": 120
}
```

三项能力独立配置的原因：实际部署中它们常来自不同厂商，地址与密钥都不相同。
默认值全部指向阿里百炼的 OpenAI 兼容端点，**一个密钥即可跑通三项**。

> **注意**：DeepSeek 官方 API **不提供 embeddings 接口**，向量化必须另配一家。

---

## 三、启动

```bash
dotnet run --project src/PdfRagQa.Api/PdfRagQa.Api.csproj --launch-profile http
```

服务地址 <http://localhost:5286>。

首次启动会自动完成：建库 → 建表 → 幂等补齐历史数据 → **校验向量列维度与配置是否一致**。
维度不一致时会直接拒绝启动并给出重建指引（见下文「更换向量模型」）。

> **环境变量坑**：`--launch-profile` 会读取 `Properties/launchSettings.json` 里的
> `ASPNETCORE_ENVIRONMENT=Development`。若**直接运行 DLL**（如部署场景），
> 必须自己设 `ASPNETCORE_ENVIRONMENT=Development`，否则 `appsettings.Development.json`
> 不会被加载，表现为「模型未配置」。

---

## 四、使用

### 1. 导入文档

```bash
curl -X POST http://localhost:5286/api/documents/import \
  -H "Content-Type: application/json" \
  -d '{"filePath":"C:/docs/manual.pdf","documentType":0,"version":"1.0"}'
```

| 字段 | 说明 |
| --- | --- |
| `filePath` | PDF 路径。用正斜杠，避免 JSON 反斜杠转义 |
| `documentType` | **必填建议**：`0`=使用手册（文本链路）、`1`=宣传手册（渲染+视觉链路） |
| `version` | 文档版本，同 `documentId` 可有多个版本，检索默认命中最近导入的版本 |
| `language` | `0`=中文 `1`=英文 `2`=中英混合，缺省中文 |

响应含 `parseNotes`（解析明细）与 `warnings`（如向量化失败、文字层为空等）。

**解析链路由声明的类型决定，不做自动判定**：

- 使用手册 → PdfPig 抽取原文与词项坐标（保留 bbox 供溯源高亮）
- 宣传手册 → PDFium 逐页渲染成图 → 视觉模型识别（不受 PDF 文字层编码问题影响）

### 2. 提问

```bash
curl -X POST http://localhost:5286/api/questions \
  -H "Content-Type: application/json" \
  -d '{"question":"安全注意事项"}'
```

可加 `documentId` / `version` / `language` 缩小范围。

响应中的 `citations` 只包含**模型在答案里标注引用了的**片段，携带
`documentId + pageNo + bbox + chunkId`，可用于在前端 PDF 上高亮定位。
`confidence` 是**引用覆盖率**（被引用的片段数 / 提供给模型的片段数），
不是校准概率，仅作参考。

### 3. 检索预览（不调用生成模型）

```bash
curl -X POST http://localhost:5286/api/retrieval/preview \
  -H "Content-Type: application/json" \
  -d '{"query":"安全注意事项","topK":5}'
```

用于调参与排障：`citations` 只含模型标注的片段，生成模型不可用时无法从问答接口
观察检索结果，本端点直接暴露检索输出。

### 4. 反馈

```bash
curl -X POST http://localhost:5286/api/feedback \
  -H "Content-Type: application/json" \
  -d '{"conversationId":"c-001","question":"...","answer":"...","isUpvote":false,"rejectionReason":"来源不相关"}'
```

### 5. 冒烟测试

```bash
scripts/smoke.sh <手册PDF> [宣传册PDF]
```

一条命令完成：构建 → 拉起服务 → 导入手册/宣传册 → 问答 → 反馈 → 逐项断言 → 清理进程。
只做端到端存活检查，**不替代单元测试**。

---

## 五、接口一览

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| POST | `/api/documents/import` | 文档接入：解析 → 向量化 → 写入 chunk |
| POST | `/api/questions` | 问答：混合检索 → 生成 → 引用溯源 |
| POST | `/api/retrieval/preview` | 检索预览，不调用生成模型 |
| POST | `/api/feedback` | 点赞 / 点踩 / 人工修正 |

---

## 六、架构

```
Api              控制器 · 启动装配
Infrastructure   实现内层端口 · 持久化 · 依赖注入
Application      用例：文档导入 · 问答 · 反馈
Domain           实体 · 端口 · 枚举（零外部依赖）
```

引用方向单向向内。**端口定义在内层、实现落在外层**，因此更换向量库或模型服务
只需改依赖注入的注册，不触及业务逻辑。

### 检索链路

```
关键词 BM25（中文 bigram 分词）─┐
                                ├─→ RRF 融合 → 生成模型 → 引用
向量余弦（原生 VECTOR_DISTANCE）┘
```

两路各自的价值：关键词擅长型号、故障码、参数这类精确串；向量擅长同义改写与跨语言检索。
向量路是「尽力而为」的——向量化失败时退化为关键词单路，不拖垮整个检索。

### 数据表

| 表 | 用途 |
| --- | --- |
| `document` | 文档元数据、版本、类型来源（管理员声明 / 自动判定） |
| `chunk` | 检索最小单元：文本、bbox、向量、图片引用 |
| `qa_feedback` | 点赞 / 点踩 / 人工修正 |

数据库名为 `PdfRagQa`，连接串见 `appsettings.json`。

---

## 七、运维

### 更换向量模型

`chunk.embedding` 是**固定维度**的 `VECTOR(n)` 列，启动时会校验其与
`Ai:EmbeddingDimensions` 是否一致，不一致直接拒绝启动。

更换步骤：

1. 修改 `appsettings.json` 的 `Embedding:Model` 与 `EmbeddingDimensions`
2. 调整列维度（需先删除旧列，SQL Server 不支持直接改 VECTOR 维度）
3. 清空 `chunk.embedding`
4. 重新导入全部文档

### 常见问题

| 现象 | 原因与处理 |
| --- | --- |
| 启动报「chunk.embedding 列维度为 X，但配置为 Y」 | 配置与表结构不一致，按提示重建 |
| 导入后 `warnings` 提示「向量化失败」 | 密钥缺失或额度不足。文本已入库，向量可后补 |
| 问答返回「【生成模型未配置】」 | `appsettings.Development.json` 未被加载，检查 `ASPNETCORE_ENVIRONMENT` |
| 构建失败且报 DLL 被占用 | 有旧服务实例在运行，先停止它 |
| 中文 JSON 请求体导致 400 | Git Bash 会把非 ASCII 命令行参数转成系统 ANSI 编码，改用文件传参（`--data-binary @file`） |
| 部署到反向代理后出现重定向死循环 | TLS 在代理层终止时，应用看到的是 http 会反复跳转。需配置 `ForwardedHeaders` 并限定可信代理（`KnownProxies` / `KnownNetworks`），**不能无条件信任请求头** |

---

## 八、已知限制

- **chunk 为页级粒度**，`bbox` 使用整页范围。按章节/段落细分后坐标溯源才够精确
- **`DocumentChunk` 无语言字段**，写入时 `[language]` 硬编码为中文，影响中英混合文档的过滤
- **多轮对话未实现**，`QuestionService` 传入的历史为 `null`
- **渲染出的页面图片未持久化**，`image_ref` 为空，多模态证据目前只有文字
- **宣传册的视觉识别受成本约束**：1000 页文档全量调用视觉模型约需 50~80 分钟，
  超出需求文档「≤30 分钟」的索引构建指标，大文档需评估采样策略
- **数据库变更靠 `schema.sql` 里的幂等语句**，尚无版本化迁移机制
