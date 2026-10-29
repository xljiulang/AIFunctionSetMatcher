# AIFunctionSetMatcher 匹配缓存优化结果

## 环境

- 日期：2026-10-30，Asia/Shanghai
- BenchmarkDotNet：0.15.8
- CPU：12th Gen Intel Core i5-12450H 2.00 GHz，1 CPU，12 逻辑核 / 8 物理核
- 操作系统：Windows 11 10.0.26300.9711
- .NET SDK：10.0.400
- 运行时：.NET 10.0.14，X64 RyuJIT x86-64-v3
- GC：Concurrent Workstation
- 诊断器：MemoryDiagnoser
- 参数：Count = 1、8、32、128

## Intersect_Exact 标准 Job 对比

优化前后均使用 `Intersect_Exact` 标准 Job。优化前数据来自
[`2026-10-29-ai-function-set-matcher.md`](2026-10-29-ai-function-set-matcher.md)。
优化前基线早于 `PromptRiskDetector` 移除和 `0.95` 描述阈值调整，因此下方差异同时包含索引缓存、预计算复用、Schema 分组、Hungarian 分配复用以及风险规则移除。

| Count | 优化前 Mean | 优化后 Mean | 耗时降低 | 优化前 Allocated | 优化后 Allocated | 分配降低 |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 8,885.1 ns | 718.6 ns | 91.9% | 23,552 B | 1.52 KB | 93.5% |
| 8 | 144,265.1 ns | 5,647.5 ns | 96.1% | 483,176 B | 4.06 KB | 99.2% |
| 32 | 1,709,495.9 ns | 71,823.6 ns | 95.8% | 6,088,304 B | 22.37 KB | 99.6% |
| 128 | 36,101,342.2 ns | 1,316,141.8 ns | 96.4% | 90,915,168 B | 280.36 KB | 99.7% |

运行命令：

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release --no-build -- --filter '*Intersect_Exact*'
```

## 真实请求集合场景

该场景复用进程级左侧集合的匹配缓存，并在每次调用中通过
`AIFunctionDeclarationSet.From(...)` 创建新的请求集合。使用 ShortRun，
统计稳定性低于标准 Job，但更接近实际请求路径。

| Method | Count | Mean | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Intersect_RequestSet | 1 | 3.781 us | 0.6124 us | 0.0336 us | 1.6556 | 0.0076 | - | 10.16 KB |
| Intersect_RequestSet | 8 | 30.469 us | 7.0900 us | 0.3886 us | 10.8948 | 0.3052 | - | 66.82 KB |
| Intersect_RequestSet | 32 | 173.801 us | 3.7525 us | 0.2057 us | 43.9453 | 4.1504 | - | 269.79 KB |
| Intersect_RequestSet | 128 | 1,915.135 us | 888.9326 us | 48.7254 us | 166.0156 | 82.0313 | 82.0313 | 1,268.89 KB |

运行命令：

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release --no-build -- --filter '*Intersect_RequestSet*' --job short
```

## 结论

- 128 项稳定匹配路径从约 `36.1 ms / 90.9 MB` 降至约 `1.32 ms / 280 KB`。
- 耗时降低 `96.4%`，单次分配降低 `99.7%`，超过 50% 和 80% 的预定目标。
- 即使请求集合每次新建，128 项交集的完整请求路径仍约为 `1.92 ms / 1.27 MB`。
- 缓存把名称、描述和 Schema 签名的重复计算移出了 N² 候选循环；Hungarian 工作数组也不再逐行分配。
- 结果仅代表本次机器与运行时，不作为跨机器 CI 性能阈值。
