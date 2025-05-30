-- Test SQL for real-time streaming with RAISERROR and NOWAIT
-- RAISERROR with NOWAIT forces immediate message sending

RAISERROR('Starting test procedure...', 0, 1) WITH NOWAIT;
RAISERROR('Initializing process', 0, 1) WITH NOWAIT;

-- Wait 2 seconds
WAITFOR DELAY '00:00:02';

RAISERROR('Step 1 complete - processed initial data', 0, 1) WITH NOWAIT;
RAISERROR('Progress: 25%% complete', 0, 1) WITH NOWAIT;

-- Wait 3 seconds  
WAITFOR DELAY '00:00:03';

RAISERROR('Step 2 complete - running calculations', 0, 1) WITH NOWAIT;
RAISERROR('Progress: 50%% complete', 0, 1) WITH NOWAIT;

-- Wait 2 seconds
WAITFOR DELAY '00:00:02';

RAISERROR('Step 3 complete - validating results', 0, 1) WITH NOWAIT;
RAISERROR('Progress: 75%% complete', 0, 1) WITH NOWAIT;

-- Wait 1 second
WAITFOR DELAY '00:00:01';

RAISERROR('Step 4 complete - finalizing output', 0, 1) WITH NOWAIT;
RAISERROR('Progress: 100%% complete', 0, 1) WITH NOWAIT;
RAISERROR('Test procedure finished successfully', 0, 1) WITH NOWAIT;