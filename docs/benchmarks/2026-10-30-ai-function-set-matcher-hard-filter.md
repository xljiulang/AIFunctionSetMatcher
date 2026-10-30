# AIFunctionSetMatcher Schema 与描述硬过滤基准

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

CPU 与前几轮基准不同，因此本报告只用于验证当前硬过滤实现，不与更早的纳秒、微秒和分配数字做直接倍率比较。

## 运行命令

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release --no-build -- --filter '*SetOperationBenchmarks*' --job short
```

## 缓存集合操作

| Method | Count | Mean | Gen0 | Allocated |
|---|---:|---:|---:|---:|
| Intersect_Exact | 1 | 257.5 ns | 0.0858 | 360 B |
| Intersect_Exact | 8 | 520.9 ns | 0.0992 | 416 B |
| Intersect_Exact | 32 | 1.328 us | 0.1450 | 608 B |
| Intersect_Exact | 128 | 4.310 us | 0.3281 | 1,376 B |
| Intersect_NamespaceRenamed | 128 | 4.354 us | 0.3281 | 1,376 B |
| Except_Exact | 128 | 4.056 us | 0.0839 | 352 B |
| Except_DescriptionMismatch | 128 | 2.967 us | 0.3281 | 1,376 B |
| Intersect_DuplicateDescriptionAssignment | 128 | 685.376 us | - | 1,607 B |

Schema 和描述唯一时直接建立匹配，不读取名称 token。名称不相似不再导致拒绝；共享描述桶内所有配对均合法，Hungarian 分配只用于优先最大化匹配数量，再最大化名称相似度总和。

## 请求集合路径

| Method | Count | Mean | Gen0 | Gen1 | Allocated |
|---|---:|---:|---:|---:|---:|
| Intersect_RequestSet | 1 | 5.649 us | 1.0452 | - | 4,376 B |
| Intersect_RequestSet | 8 | 40.455 us | 4.7607 | - | 20,072 B |
| Intersect_RequestSet | 32 | 175.220 us | 16.6016 | - | 70,361 B |
| Intersect_RequestSet | 128 | 572.837 us | 66.4063 | - | 280,958 B |
| Except_RequestSet | 128 | 563.581 us | 66.4063 | 0.9766 | 279,934 B |

当前请求路径仍需要为每个新请求计算 Schema 指纹和描述指纹，并建立 Schema 分组及描述桶。名称 token 只在同 `(Schema, 描述)` 桶存在多个左右声明时构建。

## 结论

- Schema 和描述唯一缓存路径在 128 项时为 `4.310 us / 1,376 B`，名称 profile 不再参与。
- 共享描述的 128 项名称全局分配为 `685.376 us / 1,607 B`，耗时明显高于直接匹配，但没有逐候选矩阵分配。
- 请求集合 128 项仍需约 `281 KB` 分配，主要成本转移到请求侧 Schema 指纹、描述指纹和分组结构，而不是描述 token postings。
- 描述硬过滤会拒绝任何增加、删除或替换 token 的文案变更；生产使用前必须用真实工具声明验证召回率。

基准结果仅代表本次 CPU、运行时和后台负载，不作为跨机器 CI 阈值。
