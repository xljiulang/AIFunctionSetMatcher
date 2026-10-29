namespace AIFunctionSetMatcher;

/// <summary>
/// 表示解析或构造 AI 函数声明集合时发生的错误。
/// </summary>
public sealed class AIFunctionDeclarationSetParseException : Exception
{
    /// <summary>
    /// 初始化 <see cref="AIFunctionDeclarationSetParseException"/> 类的新实例。
    /// </summary>
    /// <param name="message">错误消息。</param>
    public AIFunctionDeclarationSetParseException(string message)
        : base(message)
    {
    }
}
