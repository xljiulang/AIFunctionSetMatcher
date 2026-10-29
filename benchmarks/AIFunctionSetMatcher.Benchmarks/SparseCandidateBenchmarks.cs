using AIFunctionSetMatcher;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace AIFunctionSetMatcher.Benchmarks;

/// <summary>
/// 测量稀疏候选、名称回退和 Schema 分组路径。
/// </summary>
[MemoryDiagnoser]
public class SparseCandidateBenchmarks
{
    private readonly Consumer consumer = new();
    private AIFunctionDeclaration[] requestDeclarations = null!;
    private AIFunctionDeclaration[] requestWithExtraDeclarations = null!;
    private AIFunctionDeclarationSet left = null!;
    private AIFunctionDeclarationSet uniqueRight = null!;
    private AIFunctionDeclarationSet nameOnlyRight = null!;
    private AIFunctionDeclarationSet ambiguousLeft = null!;
    private AIFunctionDeclarationSet ambiguousRight = null!;
    private AIFunctionDeclarationSet differentSchemaRight = null!;

    /// <summary>
    /// 获取或设置每个集合中的函数数量。
    /// </summary>
    [Params(16, 128, 256)]
    public int Count { get; set; }

    /// <summary>
    /// 准备确定性稀疏候选输入。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        using var schema = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "query": { "type": "string" }
              }
            }
            """);
        using var differentSchema = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "id": { "type": "integer" }
              }
            }
            """);
        var schemaElement = schema.RootElement.Clone();
        var differentSchemaElement = differentSchema.RootElement.Clone();
        requestDeclarations = CreateDeclarations(
            index => CreateDeclaration(
                $"tool_{index:D3}",
                $"Process benchmark records for item {index:D3}.",
                schemaElement));
        var extraCount = Math.Min(32, 256 - Count);
        requestWithExtraDeclarations = new AIFunctionDeclaration[Count + extraCount];
        requestDeclarations.CopyTo(requestWithExtraDeclarations, 0);
        for (var index = 0; index < extraCount; index++)
        {
            requestWithExtraDeclarations[Count + index] = CreateDeclaration(
                $"extra_{index:D3}",
                $"Extra operation {index:D3}.",
                differentSchemaElement);
        }

        left = AIFunctionDeclarationSet.From(requestDeclarations);
        uniqueRight = AIFunctionDeclarationSet.From(requestDeclarations);
        nameOnlyRight = AIFunctionDeclarationSet.From(
            CreateDeclarations(
                index => CreateDeclaration(
                    $"mcp__tool_{index:D3}",
                    string.Empty,
                    schemaElement)));
        ambiguousLeft = AIFunctionDeclarationSet.From(
            CreateDeclarations(
                index => CreateDeclaration(
                    $"tool_alpha_{index:D3}",
                    "Process benchmark records.",
                    schemaElement)));
        ambiguousRight = AIFunctionDeclarationSet.From(
            CreateDeclarations(
                index => CreateDeclaration(
                    $"mcp__tool_alpha_{index:D3}",
                    "Process benchmark records.",
                    schemaElement)));
        differentSchemaRight = AIFunctionDeclarationSet.From(
            CreateDeclarations(
                index => CreateDeclaration(
                    $"different_{index:D3}",
                    "Different operation.",
                    differentSchemaElement)));
    }

    /// <summary>
    /// 测量描述唯一时的稀疏候选交集。
    /// </summary>
    [Benchmark]
    public void Intersect_UniqueDescription()
    {
        consumer.Consume(left.Intersect(uniqueRight));
    }

    /// <summary>
    /// 测量无描述工具使用名称 token 的稀疏交集。
    /// </summary>
    [Benchmark]
    public void Intersect_NameOnly()
    {
        consumer.Consume(left.Intersect(nameOnlyRight));
    }

    /// <summary>
    /// 测量共享描述导致候选冲突时回退 Hungarian 的交集。
    /// </summary>
    [Benchmark]
    public void Intersect_AmbiguousFallback()
    {
        consumer.Consume(ambiguousLeft.Intersect(ambiguousRight));
    }

    /// <summary>
    /// 测量左右 Schema 完全不相交时的交集。
    /// </summary>
    [Benchmark]
    public void Intersect_NoSharedSchema()
    {
        consumer.Consume(left.Intersect(differentSchemaRight));
    }

    /// <summary>
    /// 测量请求同时包含匹配 Schema 和额外 Schema 时的交集。
    /// </summary>
    [Benchmark]
    public void Intersect_RequestWithExtraSchema()
    {
        consumer.Consume(
            left.Intersect(
                AIFunctionDeclarationSet.From(requestWithExtraDeclarations)));
    }

    /// <summary>
    /// 创建指定数量的确定性声明。
    /// </summary>
    /// <param name="createDeclaration">按索引创建声明。</param>
    /// <returns>声明数组。</returns>
    private AIFunctionDeclaration[] CreateDeclarations(
        Func<int, AIFunctionDeclaration> createDeclaration)
    {
        return Enumerable.Range(0, Count)
            .Select(createDeclaration)
            .ToArray();
    }

    /// <summary>
    /// 创建单个函数声明。
    /// </summary>
    /// <param name="name">函数名称。</param>
    /// <param name="description">函数描述。</param>
    /// <param name="schema">输入 Schema。</param>
    /// <returns>函数声明。</returns>
    private static AIFunctionDeclaration CreateDeclaration(
        string name,
        string description,
        JsonElement schema)
    {
        return AIFunctionFactory.CreateDeclaration(name, description, schema);
    }
}
