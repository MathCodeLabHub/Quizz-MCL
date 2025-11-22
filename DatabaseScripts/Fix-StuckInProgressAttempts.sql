-- Fix Stuck In-Progress Attempts
-- This script identifies and optionally fixes attempts that are stuck in 'in_progress' status

-- First, let's see what we have
SELECT 
    status,
    COUNT(*) as count,
    MIN(started_at) as earliest,
    MAX(started_at) as latest
FROM quiz.attempts
WHERE user_id = 'student_1763632299617_672'
GROUP BY status
ORDER BY status;

-- Show the in_progress attempts
SELECT 
    attempt_id,
    quiz_id,
    started_at,
    EXTRACT(EPOCH FROM (NOW() - started_at))/3600 as hours_ago
FROM quiz.attempts
WHERE user_id = 'student_1763632299617_672'
  AND status = 'in_progress'
ORDER BY started_at DESC
LIMIT 20;

-- Show recent completions to verify they're working
SELECT 
    attempt_id,
    quiz_id,
    started_at,
    completed_at,
    total_score,
    score_percentage,
    status
FROM quiz.attempts
WHERE user_id = 'student_1763632299617_672'
  AND status = 'completed'
ORDER BY completed_at DESC NULLS LAST
LIMIT 10;

-- OPTIONAL: Auto-complete old stuck attempts (older than 1 hour)
-- Uncomment the following if you want to automatically complete very old in_progress attempts
/*
UPDATE quiz.attempts
SET 
    status = 'completed',
    completed_at = started_at + INTERVAL '30 minutes',
    total_score = 0,
    score_percentage = 0
WHERE user_id = 'student_1763632299617_672'
  AND status = 'in_progress'
  AND started_at < NOW() - INTERVAL '1 hour'
RETURNING attempt_id, quiz_id, started_at;
*/

-- ALTERNATIVE: Mark old attempts as abandoned (if you add this status)
-- Or simply delete them if they're test data
/*
DELETE FROM quiz.responses
WHERE attempt_id IN (
    SELECT attempt_id
    FROM quiz.attempts
    WHERE user_id = 'student_1763632299617_672'
      AND status = 'in_progress'
      AND started_at < NOW() - INTERVAL '1 hour'
);

DELETE FROM quiz.attempts
WHERE user_id = 'student_1763632299617_672'
  AND status = 'in_progress'
  AND started_at < NOW() - INTERVAL '1 hour'
RETURNING attempt_id, quiz_id, started_at;
*/
