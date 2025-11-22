-- ============================================================================
-- Debug Questions - Check what's in your questions table
-- ============================================================================

-- 1. Check if questions table exists and what columns it has
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema = 'quiz' 
  AND table_name = 'questions'
ORDER BY ordinal_position;

-- 2. Count ALL questions (including soft-deleted)
SELECT COUNT(*) as total_questions_count
FROM quiz.questions;

-- 3. Count questions with deleted_at IS NULL (active only)
SELECT COUNT(*) as active_questions_count
FROM quiz.questions
WHERE deleted_at IS NULL;

-- 4. Check if any questions have deleted_at set
SELECT COUNT(*) as deleted_questions_count
FROM quiz.questions
WHERE deleted_at IS NOT NULL;

-- 5. See first 5 questions with all their data
SELECT 
    question_id,
    question_type,
    question_text,
    subject,
    difficulty,
    deleted_at,
    created_at
FROM quiz.questions
LIMIT 5;

-- 6. Check the exact SQL the API is running
SELECT question_id, question_type, question_text, age_min, age_max, 
       difficulty, estimated_seconds, subject, locale, points, 
       allow_partial_credit, negative_marking, supports_read_aloud,
       content, version, created_at, updated_at
FROM quiz.questions
WHERE deleted_at IS NULL
ORDER BY created_at DESC 
LIMIT 50 OFFSET 0;
