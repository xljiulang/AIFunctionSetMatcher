# AIFunctionSetMatcher

`AIFunctionSetMatcher` 是面向 .NET 10 和 Microsoft Extensions for AI 的函数声明集合语义匹配库。它用于比较 MCP 导出的工具声明与 OpenAI Chat Completions 请求中的 `tools[]`，并返回左侧集合中未匹配的函数声明。

## 安装与引用

项目面向 `net10.0`，引用：

```xml
<PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.11.0" />
```

`AIFunctionDeclarationSet` 实现 `IReadOnlyCollection<AIFunctionDeclaration>`。集合直接保留调用方传入的声明实例；声明加入集合后，调用方必须保证 Name、Description、JsonSchema 和 ReturnJsonSchema 不再变化。

集合首次执行 `Except` 或 `Intersect` 时会线程安全地构建声明匹配索引，后续操作复用该索引。进程级服务端集合适合长期持有，请求级集合则只为本次请求构建一次索引。

匹配过程使用调用级池化工作数组；当每个左侧声明最多只有一个无歧义候选时，会跳过 Hungarian 全局分配。请求侧索引使用 canonical UTF-8 Schema 指纹和紧凑 token 集合，避免保留十六进制签名字符串与每个文本的 `HashSet` 桶结构。

达到 16 项的 Schema 分组会建立描述或名称 token postings。匹配器先使用稀有 token 生成安全候选超集，再执行精确分数、描述门禁和歧义检查；出现多个真实候选时自动回退到完整 Hungarian 全局分配。Schema 指纹先建立轻量索引，名称和描述档案只在左右共同 Schema 分组首次使用时创建。

请求侧分词会先用规范化字符跨度查询组内 token 驻留表，只有首次出现的 token 才创建字符串。Postings 使用连续数组保存所有声明下标，Schema 分组档案按需初始化；同一分组的名称和描述 token 引用集中在单个 TokenArena 中。

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
- 顶层工具描述使用独立门禁：两侧都为空或都有描述且 Jaccard 不低于 `0.95`；只有一侧为空时拒绝。
- 顶层描述均非空时，综合分为 `0.75 × 名称相似度 + 0.25 × 描述 Jaccard`，最低匹配分为 `0.72`。
- 最佳与次优候选的默认最小分差为 `0.08`；无法唯一区分的声明不会参与匹配。
- 使用全局一对一分配，优先最大化匹配数量，再最大化总相似度。

默认限制：

- 256 个函数声明。
- 单个 JSON Schema 256 KiB。
- 64 个 MCP 分页。
- 单个 JSON 文档 4 MiB。

## 重要限制

本库验证的是客户端声明的兼容性，不能证明工具实际由指定 MCP 服务器提供。标准 Chat Completions `tools[]` 不包含可验证的来源信息；如需来源真实性，需要额外设计签名、受信通道或私有元数据协议。

参数 Schema 内部的 `description` 不参与匹配，但它也可能进入模型上下文，因此本库不会消除该独立提示注入风险。

`0.72`、`0.08` 和 `0.95` 是保守默认值，生产使用前应使用脱敏的合法与异常工具样本进行校准。

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

本机原始基线见 [`docs/benchmarks/2026-10-29-ai-function-set-matcher.md`](docs/benchmarks/2026-10-29-ai-function-set-matcher.md)，匹配索引缓存优化结果见 [`docs/benchmarks/2026-10-30-ai-function-set-matcher-optimization.md`](docs/benchmarks/2026-10-30-ai-function-set-matcher-optimization.md)，全链路分配优化结果见 [`docs/benchmarks/2026-10-30-ai-function-set-matcher-allocation-optimization.md`](docs/benchmarks/2026-10-30-ai-function-set-matcher-allocation-optimization.md)，稀疏候选优化结果见 [`docs/benchmarks/2026-10-30-ai-function-set-matcher-sparse-candidate-optimization.md`](docs/benchmarks/2026-10-30-ai-function-set-matcher-sparse-candidate-optimization.md)，请求侧 token/postings 优化结果见 [`docs/benchmarks/2026-10-30-ai-function-set-matcher-request-profile-optimization.md`](docs/benchmarks/2026-10-30-ai-function-set-matcher-request-profile-optimization.md)。基准结果依赖 CPU、运行时和后台负载，不用于 CI 硬性性能断言。
