-- ============================================================================
-- Check and Insert Missing Attempts
-- ============================================================================
-- This script checks if the seed data attempts exist and inserts them if missing

-- First, let's see what's currently in the attempts table
SELECT 
    'Current Attempts' as status,
    COUNT(*) as count
FROM quiz.attempts;

-- Check if the sample attempt exists
SELECT 
    CASE 
        WHEN EXISTS (SELECT 1 FROM quiz.attempts WHERE attempt_id = 'a1111111-1111-1111-1111-111111111111')
        THEN 'Sample attempt EXISTS'
        ELSE 'Sample attempt MISSING'
    END as attempt_status;

-- If missing, insert the sample attempt
INSERT INTO quiz.attempts (
    attempt_id,
    quiz_id,
    user_id,
    status,
    started_at,
    completed_at,
    total_score,
    max_possible_score,
    metadata
)
SELECT
    'a1111111-1111-1111-1111-111111111111'::uuid,
    '11111111-1111-1111-1111-111111111111'::uuid,
    'user_12345',
    'completed',
    NOW() - INTERVAL '1 hour',
    NOW() - INTERVAL '50 minutes',
    32.5,
    35.0,
    '{
        "device": "tablet",
        "browser": "Safari",
        "screen_size": "1024x768",
        "accessibility_mode": "read_aloud_enabled"
    }'::jsonb
WHERE NOT EXISTS (
    SELECT 1 FROM quiz.attempts WHERE attempt_id = 'a1111111-1111-1111-1111-111111111111'
);

-- Insert the responses if they don't exist
INSERT INTO quiz.responses (
    response_id,
    attempt_id,
    question_id,
    answer_payload,
    submitted_at,
    points_earned,
    points_possible,
    is_correct,
    grading_details,
    graded_at
)
SELECT * FROM (VALUES
    (
        'r1111111-1111-1111-1111-111111111111'::uuid,
        'a1111111-1111-1111-1111-111111111111'::uuid,
        'q1111111-1111-1111-1111-111111111111'::uuid,
        '{"selected_option": "a"}'::jsonb,
        NOW() - INTERVAL '59 minutes',
        10.0,
        10.0,
        true,
        '{
            "auto_graded": true,
            "feedback": "Great job! 🎉",
            "time_taken_seconds": 25
        }'::jsonb,
        NOW() - INTERVAL '59 minutes'
    ),
    (
        'r2222222-2222-2222-2222-222222222222'::uuid,
        'a1111111-1111-1111-1111-111111111111'::uuid,
        'q2222222-2222-2222-2222-222222222222'::uuid,
        '{"selected_options": ["a", "b"]}'::jsonb,
        NOW() - INTERVAL '57 minutes',
        10.0,
        15.0,
        false,
        '{
            "auto_graded": true,
            "feedback": "Good effort! You got 2 out of 3 correct. ⭐",
            "correct_selections": 2,
            "total_correct": 3,
            "incorrect_selections": 0,
            "partial_credit_applied": true,
            "time_taken_seconds": 42
        }'::jsonb,
        NOW() - INTERVAL '57 minutes'
    ),
    (
        'r3333333-3333-3333-3333-333333333333'::uuid,
        'a1111111-1111-1111-1111-111111111111'::uuid,
        'q3333333-3333-3333-3333-333333333333'::uuid,
        '{"blanks": [{"position": 1, "answer": "on"}, {"position": 2, "answer": "four"}, {"position": 3, "answer": "two"}]}'::jsonb,
        NOW() - INTERVAL '52 minutes',
        7.5,
        10.0,
        false,
        '{
            "auto_graded": true,
            "feedback": "Almost there! 2 out of 3 blanks correct. 💪",
            "blank_results": [
                {"position": 1, "correct": true, "submitted": "on", "accepted": ["on", "upon", "sitting on"]},
                {"position": 2, "correct": true, "submitted": "four", "accepted": ["four", "4"]},
                {"position": 3, "correct": false, "submitted": "two", "accepted": ["one", "1", "a"]}
            ],
            "partial_credit_applied": true,
            "time_taken_seconds": 68
        }'::jsonb,
        NOW() - INTERVAL '52 minutes'
    )
) AS new_responses(response_id, attempt_id, question_id, answer_payload, submitted_at, points_earned, points_possible, is_correct, grading_details, graded_at)
WHERE NOT EXISTS (
    SELECT 1 FROM quiz.responses WHERE response_id = new_responses.response_id
);

-- Verify the insert
SELECT 
    'After Insert' as status,
    COUNT(*) as attempt_count
FROM quiz.attempts;

SELECT 
    'After Insert' as status,
    COUNT(*) as response_count
FROM quiz.responses;

-- Show the inserted attempt
SELECT 
    attempt_id,
    quiz_id,
    user_id,
    status,
    total_score,
    max_possible_score,
    started_at,
    completed_at
FROM quiz.attempts
WHERE attempt_id = 'a1111111-1111-1111-1111-111111111111';
