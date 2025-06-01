// ReSharper disable ConvertToConstant.Local
namespace SqlInfoStreamer.Tests;

public class OutputParameterExtractionTests
{
    [Fact]
    public void ExtractOutputParameters_BasicPattern_ShouldExtract()
    {
        // Arrange
        var sql = "@param1 OUTPUT";

        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Single(result);
        Assert.Contains("@param1", result);
    }

    [Fact]
    public void ExtractOutputParameters_MultipleParameters_ShouldExtractAll()
    {
        // Arrange
        var sql = @"
            @param1 = @value1 OUTPUT,
            @param2 = @value2 OUTPUT,
            @param3 = @value3 OUTPUT";

        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("@value1", result);
        Assert.Contains("@value2", result);
        Assert.Contains("@value3", result);
    }

    [Fact]
    public void ExtractOutputParameters_VariousSpacing_ShouldExtract()
    {
        // Arrange
        var sql = @"
            @param1OUTPUT,
            @param2 OUTPUT,
            @param3   OUTPUT,
            @param4	OUTPUT"; // Tab character

        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Equal(4, result.Count);
        Assert.Contains("@param1", result);
        Assert.Contains("@param2", result);
        Assert.Contains("@param3", result);
        Assert.Contains("@param4", result);
    }

    [Fact]
    public void ExtractOutputParameters_CaseInsensitive_ShouldExtract()
    {
        // Arrange
        var sql = @"
            @param1 OUTPUT,
            @param2 output,
            @param3 Output,
            @param4 OUT";

        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Equal(3, result.Count); // OUT should not match due to word boundary
        Assert.Contains("@param1", result);
        Assert.Contains("@param2", result);
        Assert.Contains("@param3", result);
    }

    [Fact]
    public void ExtractOutputParameters_StoredProcedureCall_ShouldExtract()
    {
        // Arrange
        var sql = @"
            EXEC spTestProcedure
                @input1 = 123,
                @output1 = @result1 OUTPUT,
                @output2 = @result2 OUTPUT,
                @input2 = 'test'";

        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("@result1", result);
        Assert.Contains("@result2", result);
    }

    [Fact]
    public void ExtractOutputParameters_ComplexParameterNames_ShouldExtract()
    {
        // Arrange
        var sql = @"
            @param_with_underscores OUTPUT,
            @param123 OUTPUT,
            @paramWithNumbers456 OUTPUT,
            @P OUTPUT";

        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Equal(4, result.Count);
        Assert.Contains("@param_with_underscores", result);
        Assert.Contains("@param123", result);
        Assert.Contains("@paramWithNumbers456", result);
        Assert.Contains("@P", result);
    }

    [Fact]
    public void ExtractOutputParameters_WithComments_ShouldExtract()
    {
        // Arrange
        var sql = @"
            @param1 = @value1 OUTPUT, -- This is a comment
            @param2 = @value2 OUTPUT /* Block comment */";

        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("@value1", result);
        Assert.Contains("@value2", result);
    }

    [Fact]
    public void ExtractOutputParameters_FalsePositives_ShouldNotExtract()
    {
        // Arrange
        var sql = @"
            SELECT 'OUTPUT' as column_name,
            @param1 = 'OUTPUTFILE',
            'Some text with @param OUTPUT in quotes',
            -- @commented_param OUTPUT
            OUTPUT_TABLE_NAME = 'test'";

        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Empty(result); // Should not extract any false positives
    }

    [Fact]
    public void ExtractOutputParameters_EmptyString_ShouldReturnEmpty()
    {
        // Arrange
        var sql = "";

        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractOutputParameters_OnlyOutputKeyword_ShouldReturnEmpty()
    {
        // Arrange
        var sql = "OUTPUT";

        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("@param OUTPUT")]
    [InlineData("@param   OUTPUT")]
    [InlineData("@param\tOUTPUT")]
    [InlineData("@param\nOUTPUT")]
    [InlineData("@paramOUTPUT")]
    public void ExtractOutputParameters_VariousWhitespace_ShouldExtract(string sql)
    {
        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Single(result);
        Assert.Contains("@param", result);
    }

    [Fact]
    public void ExtractOutputParameters_DuplicateParameters_ShouldReturnUnique()
    {
        // Arrange
        var sql = @"
            @param1 OUTPUT,
            @param1 OUTPUT,
            @param2 OUTPUT";

        // Act
        var result = Program.ExtractOutputParameters(sql);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("@param1", result);
        Assert.Contains("@param2", result);
    }
}