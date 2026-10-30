# AIFunctionSetMatcher 双向流式匹配优化结果

## 环境

- 日期：2026-10-30，Asia/Shanghai
- BenchmarkDotNet：0.15.8
- CPU：Intel Core i7-8565U 1.80 GHz，1 CPU，8 逻辑核 / 4 物理核
- 操作系统：Windows 11 10.0.26300.9711
- .NET SDK：10.0.402
- 运行时：.NET 10.0.14，X64 RyuJIT x86-64-v3
- GC：Concurrent Workstation
- 诊断器：MemoryDiagnoser
- Job：ShortRun

## 修改内容

- JSON 文档大小检查直接读取原始 UTF-8 字节跨度，不再创建完整字符串。
- 单次索引构建复用原始 Schema 字节完全相同的语义签名。
- 匹配器自动选择已经缓存索引的一侧作为目标，另一侧流式探测。
- 两侧均已缓存时使用双边索引，避免重复构建名称 profile。
- 名称矩阵存在无冲突唯一最大值时直接分配，无需执行 Hungarian。

## 解析路径

| Method | Count | Mean | Allocated |
|---|---:|---:|---:|
| From_OpenAi | 1 | 3.800 us | 2.53 KB |
| From_Mcp | 1 | 4.037 us | 2.73 KB |
| From_OpenAi | 8 | 25.709 us | 17.20 KB |
| From_Mcp | 8 | 12.657 us | 15.44 KB |
| From_OpenAi | 32 | 58.887 us | 66.54 KB |
| From_Mcp | 32 | 53.101 us | 58.02 KB |
| From_OpenAi | 128 | 249.323 us | 266.48 KB |
| From_Mcp | 128 | 265.493 us | 230.96 KB |

与历史基线的 128 项解析分配相比：

- OpenAI：`323.41 KB -> 266.48 KB`，降低约 `17.6%`。
- MCP：`280.48 KB -> 230.96 KB`，降低约 `17.7%`。

## 双向请求路径

| Method | Count | Mean | Allocated |
|---|---:|---:|---:|
| Intersect_RequestSet | 1 | 5.512 us | 3.04 KB |
| Intersect_RequestSet | 8 | 15.796 us | 6.17 KB |
| Intersect_RequestSet | 32 | 50.758 us | 15.95 KB |
| Intersect_RequestSet | 128 | 187.401 us | 57.63 KB |
| Except_RequestSet | 128 | 185.512 us | 56.63 KB |
| Intersect_CachedRightRequestSet | 128 | 260.475 us | 57.63 KB |
| Except_CachedRightRequestSet | 128 | 234.284 us | 56.64 KB |

128 项正向请求路径从 `572.837 us / 280,958 B` 降至 `187.401 us / 57.63 KB`，耗时降低约 `67%`，分配降低约 `79%`。反向请求路径也保持相同的 `57.63 KB` 分配。

两侧缓存且 Schema、描述唯一的 128 项交集复测为 `4.290 us / 1,376 B`，与优化前的 `4.310 us / 1,376 B` 基本持平。

## 名称分配

| Count | Mean | Allocated |
|---:|---:|---:|
| 16 | 10.05 us | 568 B |
| 128 | 545.87 us | 1,595 B |
| 256 | 2,157.65 us | 3,525 B |

128 项共享描述从 `685.376 us / 1,607 B` 降至 `545.87 us / 1,595 B`，耗时降低约 `20.4%`，分配基本持平。

## 结论

- 正向和反向请求调用都能自动利用服务端缓存侧索引。
- 请求集合不再构建完整匹配索引，128 项分配显著下降。
- 唯一名称最大值快速路径避免了多数重复描述场景的 Hungarian 计算。
- 基准结果只代表当前机器和运行时，不作为跨机器 CI 硬阈值。
