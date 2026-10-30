# AIFunctionSetMatcher

`AIFunctionSetMatcher` 是面向 .NET 10 和 Microsoft Extensions for AI 的函数声明集合语义匹配库。它用于比较 MCP 导出的工具声明与 OpenAI Chat Completions 请求中的 `tools[]`，并返回左侧集合中未匹配的函数声明。

## 安装与引用

项目面向 `net10.0`，引用：

```xml
<PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.11.0" />
```

`AIFunctionDeclarationSet` 实现 `IReadOnlyCollection<AIFunctionDeclaration>`。集合直接保留调用方传入的声明实例；声明加入集合后，调用方必须保证 Name、Description、JsonSchema 和 ReturnJsonSchema 不再变化。

集合首次执行 `Except` 或 `Intersect` 时会线程安全地构建声明匹配索引，后续操作复用该索引。进程级服务端集合适合长期持有，请求级集合则只为本次请求构建一次索引。

匹配过程使用调用级池化工作数组；当同一条 Schema 和描述下每组最多只有一个左右声明时，会直接匹配并跳过名称分词和 Hungarian 全局分配。请求侧索引使用 canonical UTF-8 Schema 指纹和池化描述指纹，避免持久保留描述 token 集合与 `HashSet` 桶结构。

匹配器先按 Schema 语义指纹分组，再在分组内按描述语义指纹建立紧凑描述桶。只有同一 `(Schema, 描述)` 桶中的声明才能建立候选；描述桶出现多个左右声明时，才构建名称 token 并执行 Hungarian 全局一对一分配。名称不设置最低分或最小分差。

请求侧描述会先进行 NFKC 规范化，再忽略大小写、空白、标点和词序，对规范化 token 集合计算 SHA-256 描述指纹。描述指纹只保存固定大小的摘要，不保留描述 token 或描述倒排索引。名称 token 只在重复描述桶需要比较名称相似度时按需构建。

## 基本用法

```csharp
using System.Text.Json;
using AIFunctionSetMatcher;

using var mcpDocument = JsonDocument.Parse(await File.ReadAllTextAsync("mcp-tools.json"));
using var clientDocument = JsonDocument.Parse(await File.ReadAllTextAsync("client-tools.json"));
using var requestDocument = JsonDocument.Parse(await File.ReadAllTextAsync("chat-request.json"));

var expected = AIFunctionDeclarationSet
    .From(mcpDocument.RootElement)
    .Union(AIFunctionDeclarationSet.From(clientDocument.RootElement));

var actual = AIFunctionDeclarationSet.From(requestDocument.RootElement);
var intersection = expected.Intersect(actual);
var missing = expected.Except(actual);

if (missing.Count > 0)
{
    // 由 MAF 根据 missing 中的声明决定拒绝请求或记录问题。
}
```

`Intersect` 返回左侧集合中成功匹配的原始声明。`Intersect` 与 `Except` 使用完全相同的匹配规则，两者的结果互斥，并且共同覆盖左侧集合。

`Except` 与 LINQ 的左差集返回形式一致：只返回左侧无法与右侧建立一对一匹配的声明，右侧额外声明不会出现在返回值中。需要双向差异时分别计算：

```csharp
var missingFromActual = expected.Except(actual);
var extraOnActual = actual.Except(expected);
```

## 支持的 JSON

`From(JsonElement)` 自动识别：

- MCP `tools/list` JSON-RPC 响应。
- MCP `result` 对象。
- 裸 MCP tools 数组。
- 完整 OpenAI Chat Completions 请求及其 `tools[]`。
- 裸 OpenAI function tools 数组。

`From(IReadOnlyList<JsonElement>)` 用于 MCP 分页响应。除最后一页外，每页都必须包含 `nextCursor`；最后一页不能继续携带 `nextCursor`。

## 匹配规则

- MCP `inputSchema` 与 Chat `function.parameters` 先进行语义规范化，再要求 SHA-256 签名相等。
- Schema 规范化会排序对象键和无顺序数组，并忽略 `title`、参数内部的 `description`、`examples` 注解。
- 类型、参数名、required、默认值、枚举、约束、组合关系和 `$ref/$defs` 属于匹配契约。
- 名称使用多语言 Jaccard/containment，以支持 MCP 命名空间前后缀。
- 顶层工具描述使用硬过滤：NFKC 规范化后忽略大小写、空白、标点和词序，要求规范化 token 集合完全相等。
- 两侧描述均为空或均只包含标点时视为相同；一侧没有描述 token、另一侧存在描述 token 时拒绝。
- 同一 `(Schema, 描述)` 桶内的名称相似度不设最低门槛，只用于一对一分配合。
- 使用全局一对一分配，优先最大化匹配数量，再最大化名称相似度总和。

默认限制：

- 256 个函数声明。
- 单个 JSON Schema 256 KiB。
- 64 个 MCP 分页。
- 单个 JSON 文档 4 MiB。

## 重要限制

本库验证的是客户端声明的兼容性，不能证明工具实际由指定 MCP 服务器提供。标准 Chat Completions `tools[]` 不包含可验证的来源信息；如需来源真实性，需要额外设计签名、受信通道或私有元数据协议。

参数 Schema 内部的 `description` 不参与匹配，但它也可能进入模型上下文，因此本库不会消除该独立提示注入风险。

顶层工具描述现在是强匹配契约。服务端文案、翻译或客户端元数据更新后，即使参数 Schema 和名称兼容，描述 token 集合不同也会导致不匹配。升级到本版本时不能继续配置 `MinimumMatchScore`、`MinimumScoreGap` 或 `MinimumDescriptionSimilarity`。

## 构建与测试

```powershell
dotnet build AIFunctionSetMatcher.slnx --configuration Release --warnaserror
dotnet test AIFunctionSetMatcher.slnx --configuration Release
```

## 性能基准

BenchmarkDotNet 项目位于 `benchmarks/AIFunctionSetMatcher.Benchmarks`，覆盖 `From`、`Union`、`Except` 和 `Intersect` 的公开 API 路径。

SetOperation 基准同时包含双方集合均已缓存，以及服务端集合复用缓存、请求集合每次新建的两种场景。

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release -- --filter '*'
```

查看可运行的基准：

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release --no-build -- --list flat
```

本机原始基线和历史优化结果保存在 `docs/benchmarks`。Schema 与描述硬过滤基准记录见 [`docs/benchmarks/2026-10-30-ai-function-set-matcher-hard-filter.md`](docs/benchmarks/2026-10-30-ai-function-set-matcher-hard-filter.md)。基准结果依赖 CPU、运行时和后台负载，不用于 CI 硬性性能断言。
