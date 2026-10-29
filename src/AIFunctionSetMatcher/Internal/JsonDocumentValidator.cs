using System.Text.Json;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 验证 JSON 文档中的对象不包含重复属性。
/// </summary>
internal static class JsonDocumentValidator
{
    /// <summary>
    /// 递归检查 JSON 元素及其子元素中的重复属性名。
    /// </summary>
    /// <param name="element">要检查的 JSON 元素。</param>
    /// <exception cref="AIFunctionDeclarationSetParseException">对象包含重复属性名。</exception>
    public static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new AIFunctionDeclarationSetParseException(
                        $"JSON 对象包含重复属性“{property.Name}”。");
                }

                EnsureNoDuplicateProperties(property.Value);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureNoDuplicateProperties(item);
            }
        }
    }
}
