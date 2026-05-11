using Xunit;
using BogDb.Samples.SqlToCypher.Blazor.Services;

namespace BogDb.Tests.Main;

/// <summary>
/// Exercises every Cypher query in the SQL-to-Cypher lesson catalogue against its
/// seeded in-memory graph and verifies the expected column names and row count.
///
/// These are the ground-truth metrics shown in the UI's "Expected (canonical)" hint —
/// keeping tests and the UI in sync.
/// </summary>
public class SqlToCypherSampleCypherQueriesTest
{
    private static readonly SqlToCypherGraphService Service = new();

    [Theory]
    [MemberData(nameof(LessonData))]
    public void LessonCypherQuery_SucceedsWithExpectedColumnsAndRowCount(
        int lessonNumber, string title, string cypher,
        string[] expectedColumns, int expectedRowCount)
    {
        var result = Service.Execute(cypher);

        Assert.True(result.IsSuccess,
            $"Lesson {lessonNumber} ({title}) failed: {result.Error}\nQuery: {cypher}");

        Assert.Equal(expectedColumns.Length, result.Columns.Count);
        for (var i = 0; i < expectedColumns.Length; i++)
            Assert.Equal(expectedColumns[i], result.Columns[i]);

        Assert.Equal(expectedRowCount, result.Rows.Count);
    }

    public static IEnumerable<object[]> LessonData()
    {
        var lessons = SqlToCypherGraphService.GetLessons();
        foreach (var l in lessons)
            yield return [l.Number, l.Title, l.Cypher, l.ExpectedColumns ?? [], l.ExpectedRowCount];
    }
}
