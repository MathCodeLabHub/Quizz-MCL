-- ============================================================================
-- Schema Verification Script
-- Run this to verify your manually inserted data matches what the API expects
-- ============================================================================

-- 1. Check what columns exist in YOUR questions table
SELECT 
    column_name,
    data_type,
    is_nullable,
    column_default
FROM information_schema.columns
WHERE table_schema = 'quiz' 
  AND table_name = 'questions'
ORDER BY ordinal_position;

-- 2. Check what data you actually have in your questions
SELECT 
    question_id,
    question_type,
    LEFT(question_text, 50) as question_text_preview,
    age_min,
    age_max,
    difficulty,
    estimated_seconds,
    subject,
    locale,
    points,
    allow_partial_credit,
    negative_marking,
    supports_read_aloud,
    CASE 
        WHEN content IS NULL THEN 'NULL ❌'
        WHEN content::text = '{}' THEN 'EMPTY ❌'
        ELSE 'HAS DATA ✓'
    END as content_status,
    version,
    created_at,
    updated_at,
    deleted_at
FROM quiz.questions
ORDER BY created_at DESC;

-- 3. Find NULL columns that might be causing issues
SELECT 
    'question_id' as column_name, COUNT(*) FILTER (WHERE question_id IS NULL) as null_count FROM quiz.questions
UNION ALL
SELECT 'question_type', COUNT(*) FILTER (WHERE question_type IS NULL) FROM quiz.questions
UNION ALL
SELECT 'question_text', COUNT(*) FILTER (WHERE question_text IS NULL) FROM quiz.questions
UNION ALL
SELECT 'locale', COUNT(*) FILTER (WHERE locale IS NULL) FROM quiz.questions
UNION ALL
SELECT 'points', COUNT(*) FILTER (WHERE points IS NULL) FROM quiz.questions
UNION ALL
SELECT 'allow_partial_credit', COUNT(*) FILTER (WHERE allow_partial_credit IS NULL) FROM quiz.questions
UNION ALL
SELECT 'negative_marking', COUNT(*) FILTER (WHERE negative_marking IS NULL) FROM quiz.questions
UNION ALL
SELECT 'supports_read_aloud', COUNT(*) FILTER (WHERE supports_read_aloud IS NULL) FROM quiz.questions
UNION ALL
SELECT 'content', COUNT(*) FILTER (WHERE content IS NULL) FROM quiz.questions
UNION ALL
SELECT 'version', COUNT(*) FILTER (WHERE version IS NULL) FROM quiz.questions
UNION ALL
SELECT 'deleted_at', COUNT(*) FILTER (WHERE deleted_at IS NOT NULL) FROM quiz.questions;

-- 4. Test the EXACT query the API uses
SELECT question_id, question_type, question_text, age_min, age_max, 
       difficulty, estimated_seconds, subject, locale, points, 
       allow_partial_credit, negative_marking, supports_read_aloud,
       content, version, created_at, updated_at
FROM quiz.questions
WHERE deleted_at IS NULL
ORDER BY created_at DESC 
LIMIT 50 OFFSET 0;

-- 5. Check if there are any PostgreSQL errors or type mismatches
DO $$
DECLARE
    rec RECORD;
    error_count INT := 0;
BEGIN
    FOR rec IN 
        SELECT question_id, question_type, locale, points, 
               allow_partial_credit, negative_marking, supports_read_aloud,
               content, version
        FROM quiz.questions 
        WHERE deleted_at IS NULL
        LIMIT 5
    LOOP
        -- Try to access each field as the expected type
        BEGIN
            IF rec.question_id IS NULL THEN
                RAISE NOTICE 'Question has NULL question_id';
                error_count := error_count + 1;
            END IF;
            IF rec.locale IS NULL THEN
                RAISE NOTICE 'Question % has NULL locale', rec.question_id;
                error_count := error_count + 1;
            END IF;
            IF rec.points IS NULL THEN
                RAISE NOTICE 'Question % has NULL points', rec.question_id;
                error_count := error_count + 1;
            END IF;
            IF rec.allow_partial_credit IS NULL THEN
                RAISE NOTICE 'Question % has NULL allow_partial_credit', rec.question_id;
                error_count := error_count + 1;
            END IF;
            IF rec.negative_marking IS NULL THEN
                RAISE NOTICE 'Question % has NULL negative_marking', rec.question_id;
                error_count := error_count + 1;
            END IF;
            IF rec.supports_read_aloud IS NULL THEN
                RAISE NOTICE 'Question % has NULL supports_read_aloud', rec.question_id;
                error_count := error_count + 1;
            END IF;
            IF rec.content IS NULL THEN
                RAISE NOTICE 'Question % has NULL content', rec.question_id;
                error_count := error_count + 1;
            END IF;
            IF rec.version IS NULL THEN
                RAISE NOTICE 'Question % has NULL version', rec.question_id;
                error_count := error_count + 1;
            END IF;
        EXCEPTION WHEN OTHERS THEN
            RAISE NOTICE 'Error checking question %: %', rec.question_id, SQLERRM;
            error_count := error_count + 1;
        END;
    END LOOP;
    
    IF error_count = 0 THEN
        RAISE NOTICE '✓ All questions have required non-NULL values!';
    ELSE
        RAISE NOTICE '❌ Found % issues with question data', error_count;
    END IF;
END $$;
