-- ======================================================================
-- SCRIPT: Verify Attempt Data
-- PURPOSE: Check if the specific attempt exists and verify search path
-- ======================================================================

-- Show current search path
SHOW search_path;

-- Check if the attempt exists in quiz schema
SELECT 'Checking quiz.attempts for a1111111-1111-1111-1111-111111111111:' AS check_type;
SELECT attempt_id, quiz_id, user_id, status, started_at, completed_at
FROM quiz.attempts
WHERE attempt_id = 'a1111111-1111-1111-1111-111111111111'::uuid;

-- Check all attempts in quiz schema
SELECT 'All attempts in quiz.attempts (limit 10):' AS check_type;
SELECT attempt_id, quiz_id, user_id, status, started_at
FROM quiz.attempts
ORDER BY started_at DESC
LIMIT 10;

-- Count total attempts
SELECT 'Total attempts in quiz.attempts:' AS check_type;
SELECT COUNT(*) as total_attempts FROM quiz.attempts;

-- Check if there's a public.attempts table
SELECT 'Checking if public.attempts exists:' AS check_type;
SELECT EXISTS (
    SELECT FROM information_schema.tables 
    WHERE table_schema = 'public' 
    AND table_name = 'attempts'
) AS public_attempts_exists;

-- If public.attempts exists, check for the attempt there
DO $$
BEGIN
    IF EXISTS (
        SELECT FROM information_schema.tables 
        WHERE table_schema = 'public' 
        AND table_name = 'attempts'
    ) THEN
        RAISE NOTICE 'Checking public.attempts for the attempt:';
        PERFORM attempt_id FROM public.attempts 
        WHERE attempt_id = 'a1111111-1111-1111-1111-111111111111'::uuid;
    END IF;
END $$;

-- Show all schemas
SELECT 'All schemas in database:' AS check_type;
SELECT schema_name 
FROM information_schema.schemata
WHERE schema_name NOT LIKE 'pg_%'
  AND schema_name != 'information_schema'
ORDER BY schema_name;

-- Check which schema contains attempts table
SELECT 'Tables named "attempts" in any schema:' AS check_type;
SELECT table_schema, table_name 
FROM information_schema.tables 
WHERE table_name = 'attempts'
ORDER BY table_schema;
