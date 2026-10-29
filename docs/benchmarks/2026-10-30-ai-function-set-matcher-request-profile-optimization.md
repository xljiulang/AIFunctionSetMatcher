# AIFunctionSetMatcher 请求侧 Token 与 Postings 优化结果

## 环境

- 日期：2026-10-30，Asia/Shanghai
- BenchmarkDotNet：0.15.8
- CPU：12th Gen Intel Core i5-12450H 2.00 GHz，1 CPU，12 逻辑核 / 8 物理核
- 操作系统：Windows 11 10.0.26300.9711
- .NET SDK：10.0.400
- 运行时：.NET 10.0.14，X64 RyuJIT x86-64-v3
- GC：Concurrent Workstation
- 诊断器：MemoryDiagnoser

## 基线

本轮开始前：

| Method | Count | Mean | Allocated | Job |
|---|---:|---:|---:|---|
| Intersect_RequestSet | 128 | 314.983 us | 345.22 KB | ShortRun |
| Intersect_RequestWithExtraSchema | 128 | 277.242 us | 262.952 KB | ShortRun |
| Intersect_RequestWithExtraSchema | 256 | 509.072 us | 480.536 KB | ShortRun |

## 各阶段请求路径

### 阶段一：Token 提前驻留

| Method | Count | Mean | Allocated |
|---|---:|---:|---:|
| Intersect_RequestSet | 1 | 2.640 us | 4.13 KB |
| Intersect_RequestSet | 8 | 17.821 us | 20.03 KB |
| Intersect_RequestSet | 32 | 81.724 us | 80.60 KB |
| Intersect_RequestSet | 128 | 314.585 us | 309.44 KB |

128 项分配从 `345.22 KB` 降至 `309.44 KB`，达到 `320 KB` 以下目标。

### 阶段二：扁平 Postings

| Method | Count | Mean | Allocated |
|---|---:|---:|---:|
| Intersect_RequestSet | 1 | 2.546 us | 4.13 KB |
| Intersect_RequestSet | 8 | 18.584 us | 20.03 KB |
| Intersect_RequestSet | 32 | 77.970 us | 76.98 KB |
| Intersect_RequestSet | 128 | 301.279 us | 293.38 KB |

Posting 构建已不再为每个 token 保留独立 `int[]`。空名称索引额外通过共享 Empty 跳过。

### 阶段三：惰性 Schema 分组

| Method | Count | Mean | Allocated |
|---|---:|---:|---:|
| Intersect_RequestWithExtraSchema | 16 | 61.654 us | 51.95 KB |
| Intersect_RequestWithExtraSchema | 128 | 266.422 us | 203.96 KB |
| Intersect_RequestWithExtraSchema | 256 | 488.304 us | 363.84 KB |

128 项额外 Schema 请求分配从 `262.952 KB` 降至 `203.96 KB`，未使用 Schema 组不再构建 token 档案和 postings。

### 阶段四：TokenArena

最终请求路径：

```powershell
dotnet run --project benchmarks/AIFunctionSetMatcher.Benchmarks/AIFunctionSetMatcher.Benchmarks.csproj --configuration Release --no-build -- --filter '*RequestSet*' --job short
```

| Method | Count | Mean | Gen0 | Gen1 | Allocated |
|---|---:|---:|---:|---:|---:|
| Intersect_RequestSet | 1 | 2.662 us | 0.6332 | 0.0038 | 3.90 KB |
| Except_RequestSet | 1 | 2.722 us | 0.6332 | 0.0038 | 3.89 KB |
| Intersect_RequestSet | 8 | 19.843 us | 3.1738 | 0.0610 | 19.58 KB |
| Except_RequestSet | 8 | 19.608 us | 3.1738 | 0.0610 | 19.52 KB |
| Intersect_RequestSet | 32 | 80.670 us | 12.2070 | 0.4883 | 75.33 KB |
| Except_RequestSet | 32 | 76.007 us | 12.2070 | 0.4883 | 75.08 KB |
| Intersect_RequestSet | 128 | 298.684 us | 46.8750 | 5.8594 | 288.72 KB |
| Except_RequestSet | 128 | 294.749 us | 46.8750 | 6.3477 | 287.72 KB |

最终额外 Schema 场景：

| Count | Mean | Gen0 | Gen1 | Allocated |
|---:|---:|---:|---:|---:|
| 16 | 61.236 us | 8.3618 | 0.3052 | 51.52 KB |
| 128 | 268.025 us | 31.2500 | 3.9063 | 200.02 KB |
| 256 | 496.171 us | 54.6875 | 11.7188 | 355.91 KB |

## 结论

- `Intersect_RequestSet` 128 项耗时从 `314.983 us` 降至 `298.684 us`。
- 主请求路径分配从 `345.22 KB` 降至 `288.72 KB`，降低 `16.4%`，但未达到 `250 KB` 目标。
- 带额外 Schema 的 128 项请求分配为 `200.02 KB`，说明惰性分组已经有效避免无关档案构建。
- 256 项额外 Schema 分配从 `480.536 KB` 降至 `355.91 KB`。
- 空名称/描述索引复用共享 Empty，不再创建不必要的字典和 posting 数组。
- 剩余分配主要来自规范 token 字符串、TokenInterner、Profile 对象、CompactPostingIndex 字典和必要的结果集合。
- 本轮没有引入 token hash/id；若继续要求显著低于 `250 KB`，应先进行类型级分配剖析，再决定是否采用带碰撞回退的稳定 token key。

## 缓存匹配护栏

最终 `Intersect_Exact` 使用标准 Job：

| Count | Mean | Allocated |
|---:|---:|---:|
| 1 | 203.6 ns | 328 B |
| 8 | 1.662 us | 384 B |
| 32 | 4.638 us | 21.128 KB |
| 128 | 17.165 us | 7.504 KB |

128 项仍低于 `20 us`，请求侧优化没有破坏进程级稀疏候选路径。
