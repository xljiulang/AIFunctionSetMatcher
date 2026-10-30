using AIFunctionSetMatcher.Internal;
using Xunit;

namespace AIFunctionSetMatcher.Tests;

/// <summary>
/// 验证名称矩阵唯一最大值分配快捷路径。
/// </summary>
public sealed class AssignmentShortcutTests
{
    /// <summary>
    /// 验证每行都有不冲突的唯一最高分时直接分配。
    /// </summary>
    [Fact]
    public void TryAssign_unique_row_maxima_assigns_columns()
    {
        double[] scores =
        [
            1.0, 0.2,
            0.1, 0.9,
        ];
        Span<int> rightByLeft = stackalloc int[2];

        var assigned = AssignmentShortcut.TryAssign(
            scores,
            rowCount: 2,
            columnCount: 2,
            rightByLeft);

        Assert.True(assigned);
        Assert.Equal([0, 1], rightByLeft.ToArray());
    }

    /// <summary>
    /// 验证最高分并列时拒绝快捷分配。
    /// </summary>
    [Fact]
    public void TryAssign_rejects_tied_row_maxima()
    {
        double[] scores =
        [
            1.0, 1.0,
            0.1, 0.9,
        ];
        Span<int> rightByLeft = stackalloc int[2];

        var assigned = AssignmentShortcut.TryAssign(
            scores,
            rowCount: 2,
            columnCount: 2,
            rightByLeft);

        Assert.False(assigned);
    }

    /// <summary>
    /// 验证最高分目标冲突时拒绝快捷分配。
    /// </summary>
    [Fact]
    public void TryAssign_rejects_conflicting_row_maxima()
    {
        double[] scores =
        [
            1.0, 0.2,
            0.9, 0.1,
        ];
        Span<int> rightByLeft = stackalloc int[2];

        var assigned = AssignmentShortcut.TryAssign(
            scores,
            rowCount: 2,
            columnCount: 2,
            rightByLeft);

        Assert.False(assigned);
    }

    /// <summary>
    /// 验证右侧较少时使用每列唯一最高分左侧。
    /// </summary>
    [Fact]
    public void TryAssign_assigns_unique_column_maxima_when_columns_are_fewer()
    {
        double[] scores =
        [
            0.2,
            0.8,
        ];
        Span<int> rightByLeft = stackalloc int[2];

        var assigned = AssignmentShortcut.TryAssign(
            scores,
            rowCount: 2,
            columnCount: 1,
            rightByLeft);

        Assert.True(assigned);
        Assert.Equal([-1, 0], rightByLeft.ToArray());
    }
}
