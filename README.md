# SQL Info Streamer

A lightweight .NET 9 AOT console application that executes SQL Server stored procedures and streams real-time InfoMessage events as JSON.

## Features

- **Real-time progress**: Captures SQL Server InfoMessage events during execution
- **Lightweight**: Native AOT compiled to ~28MB single binary
- **Unix-style**: Reads SQL from stdin, writes JSON events to stdout
- **Docker buildable**: No need for local .NET SDK installation
- **Fast startup**: Native compilation means instant startup

## Usage

### Basic Usage

```bash
# Using --sql argument with SQL text
sql-info-streamer -c "Server=localhost;Database=MyDB;..." --sql "EXEC MyStoredProc @param1='value'"

# Using --sql argument with SQL file
sql-info-streamer --sql script.sql

# Using environment variable and stdin
export SQL_CONNECTION="Server=localhost;Database=MyDB;Integrated Security=true;"
echo "EXEC LongRunningProc" | sql-info-streamer
```

### With Timeout and Cancellation

```bash
# 30 minute timeout
sql-info-streamer -t 1800 --sql "EXEC VeryLongProc"

# Press Ctrl+C to cancel gracefully at any time
sql-info-streamer --sql long-running-script.sql
# ^C
# {"timestamp":"2024-01-15T10:30:52.123Z","type":"info","message":"Cancellation requested, shutting down gracefully..."}
# {"timestamp":"2024-01-15T10:30:52.124Z","type":"info","message":"Operation cancelled by user"}
```

### Building with Docker

```bash
# Build the image
docker build -t sql-info-streamer .

# Run with Docker
echo "EXEC MyProc" | docker run -i --rm \
  -e SQL_CONNECTION="Server=host.docker.internal;..." \
  sql-info-streamer
```

## Output Format

Each line is a JSON object:

```json
{"timestamp":"2024-01-15T10:30:45.123Z","type":"started","message":"SQL execution began"}
{"timestamp":"2024-01-15T10:30:47.456Z","type":"info","message":"Processing 1000 records...","severity":0}
{"timestamp":"2024-01-15T10:30:52.789Z","type":"info","message":"Halfway complete","severity":0}
{"timestamp":"2024-01-15T10:31:15.012Z","type":"completed","message":"SQL execution completed successfully"}
```

### Event Types

- `started`: Execution began
- `info`: InfoMessage from stored procedure (PRINT statements, etc.)
- `completed`: Successful completion
- `error`: SQL error or execution failure

### Properties

- `timestamp`: UTC timestamp in ISO 8601 format
- `type`: Event type (started/info/completed/error)
- `message`: The actual message text
- `severity`: SQL Server severity level (for info/error events)
- `errorNumber`: SQL Server error number (for error events)

## Integration Example

```php
// PHP integration
$process = proc_open('sql-info-streamer', [
    0 => ['pipe', 'r'], // stdin
    1 => ['pipe', 'r'], // stdout
    2 => ['pipe', 'r']  // stderr
], $pipes, null, ['SQL_CONNECTION' => $connectionString]);

fwrite($pipes[0], "EXEC MyLongStoredProc");
fclose($pipes[0]);

while (!feof($pipes[1])) {
    $line = fgets($pipes[1]);
    if ($line) {
        $event = json_decode(trim($line), true);
        if ($event['type'] === 'info') {
            updateTaskProgress($taskId, $event['message']);
        }
    }
}
```

## Testing

A test SQL file is provided to demonstrate real-time progress streaming:

```bash
# Test with the provided test SQL
cat test-progress.sql | sql-info-streamer -c "your-connection-string"

# Expected output: JSON events showing progress messages every few seconds
```

## Easy Multi-Platform Building with Docker

### build.sh

```bash
# Build for multiple platforms at once
./build.sh
```

This creates ready-to-deploy binaries in the project root:
- `sql-info-streamer-linux-x64` - Linux x64 (servers, containers)
- `sql-info-streamer-linux-arm64` - Linux ARM64 (Raspberry Pi, ARM servers)
- `sql-info-streamer-macos-arm64` - macOS ARM64 (if running on macOS with .NET SDK)

### What the Build Does

1. **Linux x64** - Built using Docker with `--platform linux/amd64`
2. **Linux ARM64** - Built using Docker with `--platform linux/arm64` 
3. **macOS ARM64** - Built locally using dotnet CLI (if available)
4. **Windows** - Build locally using dotnet CLI (see instructions below)

### Platform-Specific Instructions

**For Windows builds** (run on Windows):

It's not enough to install .NET 9 SDK, Microsoft VisualStudio 2022 is also required 
with the "Desktop development with C++" workload.

```cmd
dotnet publish -c Release -r win-x64 --self-contained -o .
```

**For macOS arm64 builds** (run on macOS):

Install the .NET 9 SDK.

```bash
dotnet publish -c Release -r osx-arm64 --self-contained -o .
```
