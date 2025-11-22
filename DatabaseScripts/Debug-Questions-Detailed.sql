-- Detailed Question Diagnostics
-- Run this in pgAdmin to see what's in your questions table

-- 1. Check the structure of your questions
SELECT 
    question_id,
    question_type,
    question_text,
    locale,
    points,
    age_min,
    age_max,
    difficulty,
    subject,
    created_at,
    deleted_at,
    -- Check if content JSONB is valid
    CASE 
        WHEN content IS NULL THEN 'NULL'
        WHEN content::text = '{}' THEN 'EMPTY'
        ELSE 'HAS DATA'
    END as content_status,
    -- Show content structure
    jsonb_typeof(content) as content_type,
    -- Show first few chars of content
    LEFT(content::text, 100) as content_preview
FROM quiz.questions
ORDER BY created_at DESC;

-- 2. Compare with the API query that's returning 0 results
-- This is the EXACT query the API uses:
SELECT question_id, question_type, question_text, age_min, age_max, 
       difficulty, estimated_seconds, subject, locale, points, 
       allow_partial_credit, negative_marking, supports_read_aloud,
       content, version, created_at, updated_at
FROM quiz.questions
WHERE deleted_at IS NULL
ORDER BY created_at DESC LIMIT 50 OFFSET 0;

-- 3. Check if manually inserted questions have all required columns with values
SELECT 
    COUNT(*) as total_questions,
    COUNT(question_id) as has_id,
    COUNT(question_type) as has_type,
    COUNT(question_text) as has_text,
    COUNT(locale) as has_locale,
    COUNT(points) as has_points,
    COUNT(content) as has_content,
    COUNT(version) as has_version
FROM quiz.questions
WHERE deleted_at IS NULL;
