# AIFunctionSetMatcher 性能基准基线

> 本报告记录的是移除 `PromptRiskDetector` 并调整顶层描述阈值之前的代码版本。性能结论仍可作为优化前参考，但当前版本需要重新运行基准。

## 环境

- 运行开始：2026-10-29，完成于 2026-10-30（Asia/Shanghai）
- BenchmarkDotNet：0.15.8
- CPU：12th Gen Intel Core i5-12450H 2.00 GHz，1 CPU，12 逻辑核 / 8 物理核
- 操作系统：Windows 11 10.0.26300.9711
- .NET SDK：10.0.400
- 运行时：.NET 10.0.14，X64 RyuJIT x86-64-v3
- GC：Concurrent Workstation
- Job：DefaultJob
- 诊断器：MemoryDiagnoser
- 参数：Count = 1、8、32、128

运行命令：

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release --no-build -- --filter '*'
```

## JSON 解析

| Method | Count | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|
| From_OpenAi | 1 | 871.7 ns | 17.15 ns | 24.04 ns | 0.4740 | 0.0010 | 2.91 KB |
| From_Mcp | 1 | 1,007.1 ns | 19.60 ns | 31.09 ns | 0.5112 | - | 3.14 KB |
| From_OpenAi | 8 | 6,144.3 ns | 118.18 ns | 300.82 ns | 3.3722 | 0.0076 | 20.7 KB |
| From_Mcp | 8 | 5,366.4 ns | 107.01 ns | 223.37 ns | 3.0212 | 0.0229 | 18.55 KB |
| From_OpenAi | 32 | 22,298.1 ns | 367.59 ns | 343.85 ns | 13.1531 | 0.0305 | 80.72 KB |
| From_Mcp | 32 | 19,554.2 ns | 368.75 ns | 326.89 ns | 11.4746 | 0.0305 | 70.41 KB |
| From_OpenAi | 128 | 89,505.1 ns | 1,662.31 ns | 2,588.02 ns | 52.6123 | 0.1221 | 323.41 KB |
| From_Mcp | 128 | 77,108.9 ns | 983.96 ns | 872.25 ns | 45.4102 | 0.1221 | 280.48 KB |

## 集合操作

| Method | Count | Mean | Error | StdDev | Median | Gen0 | Gen1 | Gen2 | Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Union_Disjoint | 1 | 150.8 ns | 2.91 ns | 2.99 ns | 150.4 ns | 0.0865 | - | - | 544 B |
| Intersect_Exact | 1 | 8,885.1 ns | 161.58 ns | 151.14 ns | 8,842.7 ns | 3.7537 | 0.0153 | - | 23,552 B |
| Intersect_NamespaceRenamed | 1 | 9,026.9 ns | 160.46 ns | 150.10 ns | 9,054.1 ns | 3.8757 | 0.0153 | - | 24,392 B |
| Except_Exact | 1 | 8,907.9 ns | 164.89 ns | 161.94 ns | 8,908.3 ns | 3.7079 | 0.0153 | - | 23,328 B |
| Except_DescriptionMismatch | 1 | 6,393.1 ns | 127.61 ns | 293.21 ns | 6,447.3 ns | 2.5482 | 0.0076 | - | 16,000 B |
| Union_Disjoint | 8 | 463.4 ns | 9.26 ns | 24.39 ns | 455.9 ns | 0.1874 | - | - | 1,176 B |
| Intersect_Exact | 8 | 144,265.1 ns | 3,749.61 ns | 10,996.96 ns | 140,335.6 ns | 76.9043 | 0.9766 | - | 483,176 B |
| Intersect_NamespaceRenamed | 8 | 141,050.5 ns | 2,770.59 ns | 5,596.73 ns | 139,543.7 ns | 77.8809 | 0.9766 | - | 489,896 B |
| Except_Exact | 8 | 136,515.9 ns | 2,145.55 ns | 1,901.97 ns | 135,806.4 ns | 76.6602 | 0.9766 | - | 482,320 B |
| Except_DescriptionMismatch | 8 | 163,801.4 ns | 13,361.20 ns | 39,186.09 ns | 189,119.8 ns | 70.1904 | 0.9766 | - | 440,904 B |
| Union_Disjoint | 32 | 2,176.0 ns | 185.93 ns | 548.21 ns | 2,472.6 ns | 0.6657 | 0.0019 | - | 4,184 B |
| Intersect_Exact | 32 | 1,709,495.9 ns | 55,910.68 ns | 151,157.78 ns | 1,659,892.2 ns | 968.7500 | 62.5000 | - | 6,088,304 B |
| Intersect_NamespaceRenamed | 32 | 2,436,062.9 ns | 130,216.82 ns | 383,947.27 ns | 2,619,365.9 ns | 974.6094 | 62.5000 | - | 6,115,184 B |
| Except_Exact | 32 | 1,904,428.9 ns | 36,153.27 ns | 48,263.59 ns | 1,908,583.6 ns | 968.7500 | 62.5000 | - | 6,086,272 B |
| Except_DescriptionMismatch | 32 | 2,569,979.5 ns | 88,486.39 ns | 259,515.29 ns | 2,615,283.4 ns | 982.4219 | 64.4531 | - | 6,165,024 B |
| Union_Disjoint | 128 | 6,134.3 ns | 75.37 ns | 62.93 ns | 6,122.5 ns | 2.9297 | 0.0381 | - | 18,384 B |
| Intersect_Exact | 128 | 36,101,342.2 ns | 1,372,650.00 ns | 4,047,289.77 ns | 36,885,246.9 ns | 14,437.5000 | 187.5000 | - | 90,915,168 B |
| Intersect_NamespaceRenamed | 128 | 39,865,493.9 ns | 759,581.68 ns | 1,481,505.83 ns | 39,587,053.3 ns | 14,466.6667 | 200.0000 | - | 91,022,688 B |
| Except_Exact | 128 | 38,601,044.4 ns | 754,763.81 ns | 1,058,073.54 ns | 38,361,357.1 ns | 14,428.5714 | 142.8571 | - | 90,905,712 B |
| Except_DescriptionMismatch | 128 | 25,451,177.0 ns | 583,038.25 ns | 1,596,057.92 ns | 25,040,192.9 ns | 15,142.8571 | 214.2857 | 71.4286 | 95,154,520 B |

## 结论

- JSON 解析在 128 项时仍低于 100 微秒，不是主要瓶颈。
- `Union` 保持线性增长，128 项约 6 微秒。
- `Except` 和 `Intersect` 的全局匹配增长明显：32 项约 1.7–2.6 ms，128 项约 25–40 ms。
- 128 项匹配的单次分配约 91–95 MB，说明 O(n²) 候选比较和 O(n³) Hungarian 分配之外，还存在显著临时对象开销。
- 当前基准支持将 32 项以内视为毫秒级、128 项视为高成本场景；是否需要优化应结合实际生产工具数量。
- 该结果只代表本次机器和运行时，不作为跨机器 CI 性能门槛。
