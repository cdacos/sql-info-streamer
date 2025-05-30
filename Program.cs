#region

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

#endregion

namespace SqlInfoStreamer;

// ReSharper disable once ClassNeverInstantiated.Global
internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Setup cancellation for Ctrl+C
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // Prevent immediate termination
            WriteEvent("info", "Cancellation requested, shutting down gracefully...");
            cts.Cancel();
        };

        try
        {
            var options = ParseArguments(args);

            string sql;
            if (!string.IsNullOrWhiteSpace(options.SqlInput))
            {
                // SQL provided via --sql argument
                if (File.Exists(options.SqlInput))
                    sql = await File.ReadAllTextAsync(options.SqlInput, cts.Token);
                else
                    sql = options.SqlInput;
            }
            else
            {
                // SQL provided via stdin
                sql = await Console.In.ReadToEndAsync(cts.Token);
            }

            if (string.IsNullOrWhiteSpace(sql))
            {
                WriteEvent("error", "No SQL provided via --sql argument or stdin");
                return 1;
            }

            await ExecuteSqlWithInfoMessages(options.ConnectionString, sql, options.Timeout, cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            WriteEvent("info", "Operation cancelled by user");
            return 130; // Standard exit code for Ctrl+C
        }
        catch (Exception ex)
        {
            WriteEvent("error", ex.Message);
            return 1;
        }
    }

    private static async Task ExecuteSqlWithInfoMessages(string connectionString, string sql, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);

        // Capture InfoMessage events for real-time progress
        connection.InfoMessage += (_, e) =>
        {
            foreach (SqlError error in e.Errors) WriteEvent("info", error.Message.Trim(), error.Class, error.Number);
        };

        try
        {
            await connection.OpenAsync(cancellationToken);
            WriteEvent("started", "SQL execution began");

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = timeoutSeconds;

            // Register cancellation to cancel the SQL command on the server
            await using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    command.Cancel(); // Send cancellation to SQL Server
                    WriteEvent("info", "Cancellation sent to SQL Server");
                }
                catch
                {
                    // Ignore errors during cancellation
                }
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            // Process all result sets to ensure InfoMessage events are fired in real-time
            do
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    // Read through all rows to trigger InfoMessage events
                }
            } while (await reader.NextResultAsync(cancellationToken));

            WriteEvent("completed", "SQL execution completed successfully");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WriteEvent("info", "SQL execution cancelled");
            throw;
        }
        catch (SqlException ex)
        {
            WriteEvent("error", $"SQL Error: {ex.Message}", ex.Class, ex.Number);
            throw;
        }
        catch (Exception ex)
        {
            WriteEvent("error", $"Execution failed: {ex.Message}");
            throw;
        }
    }

    private static void WriteEvent(string type, string message, byte? severity = null, int? errorNumber = null)
    {
        var eventData = new EventData
        {
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Type = type,
            Message = message,
            Severity = severity,
            ErrorNumber = errorNumber
        };

        var json = JsonSerializer.Serialize(eventData, EventDataContext.Default.EventData);

        Console.WriteLine(json);
        Console.Out.Flush();
    }

    private static Options ParseArguments(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION");
        var timeout = 0; // No timeout by default
        string? sqlInput = null;

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--connection":
                case "-c":
                    if (i + 1 < args.Length)
                        connectionString = args[++i];
                    break;
                case "--timeout":
                case "-t":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var timeoutValue))
                        timeout = timeoutValue;
                    break;
                case "--sql":
                case "-s":
                    if (i + 1 < args.Length)
                        sqlInput = args[++i];
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
            }

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException(
                "Connection string must be provided via --connection argument or SQL_CONNECTION environment variable");

        return new Options(connectionString, timeout, sqlInput);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: sql-info-streamer [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Executes SQL and streams InfoMessage events to stdout as JSON.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine(
            "  -c, --connection <string>    SQL Server connection string (or set SQL_CONNECTION env var)");
        Console.Error.WriteLine("  -s, --sql <sql|file>         SQL statement or path to SQL file");
        Console.Error.WriteLine("  -t, --timeout <seconds>      Command timeout in seconds (0 = no timeout, default)");
        Console.Error.WriteLine("  -h, --help                   Show this help message");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Examples:");
        Console.Error.WriteLine(
            "  sql-info-streamer -c \"Server=localhost;Database=MyDB;...\" --sql \"EXEC MyStoredProc\"");
        Console.Error.WriteLine("  sql-info-streamer --sql script.sql");
        Console.Error.WriteLine(
            "  echo \"EXEC MyStoredProc\" | sql-info-streamer -c \"Server=localhost;Database=MyDB;...\"");
        Console.Error.WriteLine("  export SQL_CONNECTION=\"Server=...\"");
        Console.Error.WriteLine("  echo \"EXEC LongRunningProc\" | sql-info-streamer");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Output format:");
        Console.Error.WriteLine(
            "  Each line is a JSON object with timestamp, type, message, and optional severity/errorNumber");
        Console.Error.WriteLine("  Types: started, info, completed, error");
    }

    private record Options(string ConnectionString, int Timeout, string? SqlInput);
}

public class EventData
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";

    [JsonPropertyName("type")] public string Type { get; set; } = "";

    [JsonPropertyName("message")] public string Message { get; set; } = "";

    [JsonPropertyName("severity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte? Severity { get; set; }

    [JsonPropertyName("errorNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ErrorNumber { get; set; }
}

[JsonSerializable(typeof(EventData))]
internal partial class EventDataContext : JsonSerializerContext
{
}