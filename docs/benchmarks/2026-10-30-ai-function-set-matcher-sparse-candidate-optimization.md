# AIFunctionSetMatcher 稀疏候选优化结果

## 环境

- 日期：2026-10-30，Asia/Shanghai
- BenchmarkDotNet：0.15.8
- CPU：12th Gen Intel Core i5-12450H 2.00 GHz，1 CPU，12 逻辑核 / 8 物理核
- 操作系统：Windows 11 10.0.26300.9711
- .NET SDK：10.0.400
- 运行时：.NET 10.0.14，X64 RyuJIT x86-64-v3
- GC：Concurrent Workstation
- 诊断器：MemoryDiagnoser

## 修改前

上一轮全链路分配优化后的基线：

| Scenario | Count | Mean | Allocated | Job |
|---|---:|---:|---:|---|
| Intersect_Exact | 128 | 432,414.4 ns | 1,408 B | Default |
| Intersect_RequestSet | 128 | 742.910 us | 356.2 KB | ShortRun |
| Intersect_HungarianFallback | 128 | 578.142 us | 1,408 B | ShortRun |

## 唯一描述稀疏候选

最终 `Intersect_Exact` 使用标准 Job：

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release --no-build -- --filter '*Intersect_Exact*'
```

| Count | 修改前 Mean | 修改后 Mean | 修改后 Allocated |
|---:|---:|---:|---:|
| 1 | 427.2 ns | 199.2 ns | 328 B |
| 8 | 1,987.8 ns | 1,644.6 ns | 384 B |
| 32 | 26,786.3 ns | 4,665.4 ns | 2,384 B |
| 128 | 432,414.4 ns | 17,364.1 ns | 8,528 B |

128 项耗时降低 `96.0%`，达到 `200 us` 以下的阶段目标。

稀疏候选工作区使缓存匹配的稳态 Allocated 从 `1,408 B` 增加到 `8,528 B`。这部分不再包含 LOH 分数矩阵，但稀疏候选和结果集合需要额外小数组。优化重点应继续放在计时和保留容量，而不是把该数字压回 1 KB。

## 稀疏场景

使用 ShortRun：

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release --no-build -- --filter '*SparseCandidateBenchmarks*' --job short
```

| Method | Count | Mean | Allocated |
|---|---:|---:|---:|
| UniqueDescription | 16 | 2.064 us | 1,360 B |
| UniqueDescription | 128 | 18.036 us | 8,528 B |
| UniqueDescription | 256 | 34.143 us | 16,720 B |
| NameOnly | 16 | 205.97 ns | 336 B |
| NameOnly | 128 | 1.131 us | 336 B |
| NameOnly | 256 | 2.066 us | 336 B |
| NoSharedSchema | 16 | 82.31 ns | 272 B |
| NoSharedSchema | 128 | 176.48 ns | 272 B |
| NoSharedSchema | 256 | 295.48 ns | 272 B |
| RequestWithExtraSchema | 16 | 63.457 us | 61.736 KB |
| RequestWithExtraSchema | 128 | 277.242 us | 262.952 KB |
| RequestWithExtraSchema | 256 | 509.072 us | 480.536 KB |

`NoSharedSchema` 不构建非共同 Schema 组的 token 档案，256 项仍低于 `300 ns`。

## 稠密回退

| Count | Mean | Allocated |
|---:|---:|---:|
| 16 | 9.537 us | 608 B |
| 128 | 509.279 us | 1,504 B |
| 256 | 2.084 ms | 2,528 B |

128 项稠密回退从上一轮约 `578.142 us` 降至 `509.279 us`，未出现超过 10% 的性能退化。

## 请求集合路径

复用服务端集合缓存并每次创建请求集合：

| Count | 修改前 Mean | 修改后 Mean | 修改前 Allocated | 修改后 Allocated |
|---:|---:|---:|---:|---:|
| 1 | 3.781 us | 2.493 us | 10.16 KB | 4.17 KB |
| 8 | 30.469 us | 18.671 us | 66.82 KB | 20.34 KB |
| 32 | 173.801 us | 85.601 us | 269.79 KB | 89.38 KB |
| 128 | 1,915.135 us | 314.983 us | 1,268.89 KB | 345.22 KB |

128 项 Mean 降低 `83.6%`，满足 `500 us` 目标。Allocated 降低 `72.8%`，但 `345.22 KB` 未达到 `250 KB` 目标。

请求同时携带额外 Schema 时，128 项为 `277.242 us / 262.952 KB`，说明惰性 Schema 分组已避免为非共同分组构建 token 档案。当前主要分配来自共同分组的 token 字符串、profile、postings 和持久数组，而不是临时矩阵。

## 结论

- 唯一描述候选已从 N² 扫描转为稀有 token posting 查询。
- 128 项标准匹配从 `432.414 us` 降至 `17.364 us`，降低 `96.0%`。
- 128 项请求路径从 `1.915 ms` 降至约 `315 us`，降低 `83.6%`。
- 名称唯一和无共同 Schema 场景已接近常数级耗时。
- 稠密 Hungarian 回退保持正确且没有性能退化。
- 请求侧 `250 KB` 分配目标未达到；报告保留 `345.22 KB` 的实测值，后续若要求继续降低，需要进一步把 token 从字符串数组改为组内 token id 或稳定 token 指纹。
