using System.Text.Json;
using SqlInfoStreamer;

namespace SqlInfoStreamer.Tests;

public class StreamingTests
{
    [Fact]
    public void ResultSetStartData_SerializesToCorrectJson()
    {
        // Arrange
        var data = new ResultSetStartData
        {
            Timestamp = "2023-01-01T12:00:00.000Z",
            Type = "result_set_start",
            ResultSetIndex = 0,
            Columns = new List<string> { "id", "name", "email" }
        };

        // Act
        var json = JsonSerializer.Serialize(data, EventDataContext.Default.ResultSetStartData);
        var deserialized = JsonSerializer.Deserialize<ResultSetStartData>(json, EventDataContext.Default.ResultSetStartData);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("2023-01-01T12:00:00.000Z", deserialized.Timestamp);
        Assert.Equal("result_set_start", deserialized.Type);
        Assert.Equal(0, deserialized.ResultSetIndex);
        Assert.Equal(3, deserialized.Columns.Count);
        Assert.Contains("id", deserialized.Columns);
        Assert.Contains("name", deserialized.Columns);
        Assert.Contains("email", deserialized.Columns);
    }

    [Fact]
    public void RowData_SerializesToCorrectJson()
    {
        // Arrange
        var data = new RowData
        {
            Timestamp = "2023-01-01T12:00:00.000Z",
            Type = "row",
            ResultSetIndex = 0,
            RowIndex = 0,
            Data = new Dictionary<string, string?>
            {
                { "id", "1" },
                { "name", "John Doe" },
                { "email", null }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(data, EventDataContext.Default.RowData);
        var deserialized = JsonSerializer.Deserialize<RowData>(json, EventDataContext.Default.RowData);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("2023-01-01T12:00:00.000Z", deserialized.Timestamp);
        Assert.Equal("row", deserialized.Type);
        Assert.Equal(0, deserialized.ResultSetIndex);
        Assert.Equal(0, deserialized.RowIndex);
        Assert.Equal(3, deserialized.Data.Count);
        Assert.Equal("1", deserialized.Data["id"]);
        Assert.Equal("John Doe", deserialized.Data["name"]);
        Assert.Null(deserialized.Data["email"]);
    }

    [Fact]
    public void ResultSetEndData_SerializesToCorrectJson()
    {
        // Arrange
        var data = new ResultSetEndData
        {
            Timestamp = "2023-01-01T12:00:00.000Z",
            Type = "result_set_end",
            ResultSetIndex = 0,
            TotalRows = 100
        };

        // Act
        var json = JsonSerializer.Serialize(data, EventDataContext.Default.ResultSetEndData);
        var deserialized = JsonSerializer.Deserialize<ResultSetEndData>(json, EventDataContext.Default.ResultSetEndData);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("2023-01-01T12:00:00.000Z", deserialized.Timestamp);
        Assert.Equal("result_set_end", deserialized.Type);
        Assert.Equal(0, deserialized.ResultSetIndex);
        Assert.Equal(100, deserialized.TotalRows);
    }

    [Fact]
    public void OutputParametersData_SerializesToCorrectJson()
    {
        // Arrange
        var data = new OutputParametersData
        {
            Timestamp = "2023-01-01T12:00:00.000Z",
            Type = "output_parameters",
            OutputParameters = new Dictionary<string, string?>
            {
                { "@result", "success" },
                { "@count", "42" },
                { "@error", null }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(data, EventDataContext.Default.OutputParametersData);
        var deserialized = JsonSerializer.Deserialize<OutputParametersData>(json, EventDataContext.Default.OutputParametersData);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("2023-01-01T12:00:00.000Z", deserialized.Timestamp);
        Assert.Equal("output_parameters", deserialized.Type);
        Assert.Equal(3, deserialized.OutputParameters.Count);
        Assert.Equal("success", deserialized.OutputParameters["@result"]);
        Assert.Equal("42", deserialized.OutputParameters["@count"]);
        Assert.Null(deserialized.OutputParameters["@error"]);
    }

    [Fact]
    public void StreamingEventData_ContainsRequiredFields()
    {
        // Test that all streaming event data types have timestamp and type fields
        var resultSetStart = new ResultSetStartData
        {
            Timestamp = "test",
            Type = "result_set_start"
        };

        var row = new RowData
        {
            Timestamp = "test",
            Type = "row"
        };

        var resultSetEnd = new ResultSetEndData
        {
            Timestamp = "test",
            Type = "result_set_end"
        };

        var outputParams = new OutputParametersData
        {
            Timestamp = "test",
            Type = "output_parameters"
        };

        // All should serialize without errors
        Assert.NotNull(JsonSerializer.Serialize(resultSetStart, EventDataContext.Default.ResultSetStartData));
        Assert.NotNull(JsonSerializer.Serialize(row, EventDataContext.Default.RowData));
        Assert.NotNull(JsonSerializer.Serialize(resultSetEnd, EventDataContext.Default.ResultSetEndData));
        Assert.NotNull(JsonSerializer.Serialize(outputParams, EventDataContext.Default.OutputParametersData));
    }
}