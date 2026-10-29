# AIFunctionSetMatcher 全链路分配优化结果

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

## 修改前基线

修改前使用标准 Job 重新采集 128 项 `Intersect_Exact` 基线：

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release -- --filter '*Intersect_Exact*'
```

| Count | Mean | Allocated |
|---:|---:|---:|
| 1 | 572.1 ns | 1.52 KB |
| 8 | 9,472.3 ns | 4.06 KB |
| 32 | 131,659.4 ns | 22.37 KB |
| 128 | 2,179,799.7 ns | 280.36 KB |

## 匹配热路径

最终使用标准 Job 同时测量 `Intersect_Exact` 和 `Except_Exact`：

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release --no-build -- --filter '*Intersect_Exact*' '*Except_Exact*'
```

| Method | Count | Mean | Gen0 | Allocated |
|---|---:|---:|---:|---:|
| Intersect_Exact | 1 | 427.2 ns | 0.0625 | 392 B |
| Except_Exact | 1 | 419.1 ns | 0.0610 | 384 B |
| Intersect_Exact | 8 | 1,987.8 ns | 0.0687 | 448 B |
| Except_Exact | 8 | 2,001.0 ns | 0.0610 | 384 B |
| Intersect_Exact | 32 | 26,786.3 ns | 0.0916 | 640 B |
| Except_Exact | 32 | 30,344.5 ns | 0.0610 | 384 B |
| Intersect_Exact | 128 | 432,414.4 ns | - | 1,408 B |
| Except_Exact | 128 | 450,288.1 ns | - | 384 B |

128 项 `Intersect_Exact` 相比本次基线：

- Mean 从 `2,179,799.7 ns` 降至 `432,414.4 ns`，降低 `80.2%`。
- Allocated 从 `280.36 KB` 降至 `1,408 B`，降低 `99.5%`。
- Gen2 从每个工作负载产生 LOH 压力降至无 Gen2 记录。

池化数组在 BenchmarkDotNet 预热后由共享池复用，因此稳态 Allocated 不包含已经由池持有的矩阵容量。首次进程调用仍承担一次池扩容成本。

## 请求集合路径

该场景复用服务端集合缓存，并在每次调用中通过
`AIFunctionDeclarationSet.From(...)` 新建请求集合。使用 ShortRun：

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release --no-build -- --filter '*RequestSet*' --job short
```

| Method | Count | Mean | Gen0 | Gen1 | Allocated |
|---|---:|---:|---:|---:|---:|
| Intersect_RequestSet | 1 | 2.688 us | 0.6256 | - | 3.85 KB |
| Except_RequestSet | 1 | 2.734 us | 0.6256 | - | 3.84 KB |
| Intersect_RequestSet | 8 | 18.728 us | 3.8147 | 0.0610 | 23.39 KB |
| Except_RequestSet | 8 | 18.679 us | 3.7842 | 0.0610 | 23.33 KB |
| Intersect_RequestSet | 32 | 94.063 us | 14.5264 | 0.8545 | 89.46 KB |
| Except_RequestSet | 32 | 93.125 us | 14.5264 | 0.8545 | 89.21 KB |
| Intersect_RequestSet | 128 | 742.910 us | 57.6172 | 10.7422 | 356.2 KB |
| Except_RequestSet | 128 | 740.453 us | 57.6172 | 9.7656 | 355.2 KB |

与上一份缓存优化报告的请求路径相比：

| Count | 修改前 Mean | 修改后 Mean | 修改前 Allocated | 修改后 Allocated |
|---:|---:|---:|---:|---:|
| 1 | 3.781 us | 2.688 us | 10.16 KB | 3.85 KB |
| 8 | 30.469 us | 18.728 us | 66.82 KB | 23.39 KB |
| 32 | 173.801 us | 94.063 us | 269.79 KB | 89.46 KB |
| 128 | 1,915.135 us | 742.910 us | 1,268.89 KB | 356.2 KB |

128 项请求路径 Mean 降低 `61.2%`，Allocated 降低 `71.9%`，超过计划中的 `1.4 ms` 和 `900 KB` 目标。

## 回退与建档

强制 Hungarian 全局分配的 ShortRun 结果：

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release --no-build -- --filter '*HungarianFallback*' --job short
```

| Count | Mean | Allocated |
|---:|---:|---:|
| 1 | 419.1 ns | 392 B |
| 8 | 2.525 us | 448 B |
| 32 | 35.628 us | 640 B |
| 128 | 607.403 us | 1,408 B |

仅创建请求声明集合、不构建匹配索引的 ShortRun 结果：

| Count | Mean | Allocated |
|---:|---:|---:|
| 1 | 51.72 ns | 376 B |
| 8 | 159.55 ns | 952 B |
| 32 | 407.25 ns | 1,936 B |
| 128 | 1,568.34 ns | 8,528 B |

`Union_Disjoint` 使用标准 Job 复测后为 `147.3 ns / 600 B`、
`463.1 ns / 1232 B`、`1.301 us / 4240 B`、`4.820 us / 18440 B`。
消除 LINQ 二次复制后的 ShortRun 分配进一步降至
`416 B`、`1048 B`、`4056 B`、`18256 B`，没有超过计划的 10% 回退边界。

`From_OpenAi` 和 `From_Mcp` 的解析路径没有出现超过 10% 的稳定分配回退；128 项分别为约 `323.5 KB` 和 `280.57 KB`，与原有 `323.41 KB` 和 `280.48 KB` 基本一致。

## 结论

- Hungarian 不再创建补边成本矩阵，分数和算法工作数组均由共享数组池承载。
- 唯一候选路径无需执行 Hungarian；重叠候选仍使用原全局一对一分配。
- 匹配结果使用栈上位图、精确大小数组和内部已验证子集构造，不再重复名称校验。
- 请求侧 Schema 使用池化 canonical UTF-8 和四段指纹，不再保留十六进制签名字符串。
- 名称与描述 token 使用排序紧凑数组和双指针交集，避免每个文本的 HashSet 桶结构。
- 结果仅代表本次机器、运行时和后台负载，不作为跨机器 CI 阈值。
