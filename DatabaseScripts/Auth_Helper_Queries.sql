-- ============================================================================
-- Helper Queries for Authentication & Level Management
-- Common operations for managing users, levels, and assignments
-- ============================================================================

-- ============================================================================
-- USER MANAGEMENT
-- ============================================================================

-- View all users with their roles
SELECT 
    user_id,
    username,
    email,
    full_name,
    role,
    is_active,
    last_login_at,
    created_at
FROM quiz.users
WHERE deleted_at IS NULL
ORDER BY created_at DESC;

-- View students with their enrolled levels
SELECT 
    u.username,
    u.full_name,
    STRING_AGG(l.level_code, ', ' ORDER BY l.display_order) as enrolled_levels,
    COUNT(DISTINCT ul.level_id) as level_count
FROM quiz.users u
INNER JOIN quiz.user_levels ul ON u.user_id = ul.user_id
INNER JOIN quiz.levels l ON ul.level_id = l.level_id
WHERE u.role = 'student' 
AND u.deleted_at IS NULL
AND ul.completed_at IS NULL
GROUP BY u.user_id, u.username, u.full_name
ORDER BY u.username;

-- View tutors with their assigned levels
SELECT 
    u.username,
    u.full_name,
    STRING_AGG(l.level_code, ', ' ORDER BY l.display_order) as assigned_levels,
    COUNT(DISTINCT tla.level_id) as level_count
FROM quiz.users u
INNER JOIN quiz.tutor_level_assignments tla ON u.user_id = tla.tutor_id
INNER JOIN quiz.levels l ON tla.level_id = l.level_id
WHERE u.role = 'tutor' 
AND u.deleted_at IS NULL
AND tla.is_active = true
GROUP BY u.user_id, u.username, u.full_name
ORDER BY u.username;

-- ============================================================================
-- CREATE USERS
-- ============================================================================

-- Create admin user
-- Password: admin123 (CHANGE IN PRODUCTION!)
INSERT INTO quiz.users (username, password_hash, email, full_name, role, is_active)
VALUES (
    'admin',
    '$2a$11$YourBCryptHashHere',
    'admin@quizapp.com',
    'System Administrator',
    'admin',
    true
) RETURNING user_id, username, role;

-- Create student user
-- Password: student123
INSERT INTO quiz.users (username, password_hash, email, full_name, role, is_active)
VALUES (
    'student1',
    '$2a$11$YourBCryptHashHere',
    'student1@email.com',
    'John Doe',
    'student',
    true
) RETURNING user_id, username, role;

-- Create tutor user
-- Password: tutor123
INSERT INTO quiz.users (username, password_hash, email, full_name, role, is_active)
VALUES (
    'tutor1',
    '$2a$11$YourBCryptHashHere',
    'tutor1@email.com',
    'Ms. Smith',
    'tutor',
    true
) RETURNING user_id, username, role;

-- ============================================================================
-- LEVEL MANAGEMENT
-- ============================================================================

-- View all levels with statistics
SELECT 
    l.level_code,
    l.level_name,
    l.description,
    l.display_order,
    l.is_active,
    COUNT(DISTINCT ul.user_id) as enrolled_students,
    COUNT(DISTINCT tla.tutor_id) as assigned_tutors,
    COUNT(DISTINCT q.quiz_id) as quiz_count
FROM quiz.levels l
LEFT JOIN quiz.user_levels ul ON l.level_id = ul.level_id AND ul.completed_at IS NULL
LEFT JOIN quiz.tutor_level_assignments tla ON l.level_id = tla.level_id AND tla.is_active = true
LEFT JOIN quiz.quizzes q ON l.level_id = q.level_id AND q.deleted_at IS NULL
GROUP BY l.level_id, l.level_code, l.level_name, l.description, l.display_order, l.is_active
ORDER BY l.display_order;

-- Add custom level
INSERT INTO quiz.levels (level_code, level_name, description, display_order, is_active)
VALUES (
    'level5',
    'Master',
    'Advanced mastery level',
    5,
    true
) RETURNING level_id, level_code, level_name;

-- ============================================================================
-- ENROLL STUDENTS IN LEVELS
-- ============================================================================

-- Enroll a student in a level
INSERT INTO quiz.user_levels (user_id, level_id)
SELECT 
    u.user_id,
    l.level_id
FROM quiz.users u, quiz.levels l
WHERE u.username = 'student1'
AND l.level_code = 'level1'
AND NOT EXISTS (
    SELECT 1 FROM quiz.user_levels ul
    WHERE ul.user_id = u.user_id AND ul.level_id = l.level_id
)
RETURNING user_level_id;

-- Enroll a student in multiple levels
INSERT INTO quiz.user_levels (user_id, level_id)
SELECT 
    u.user_id,
    l.level_id
FROM quiz.users u
CROSS JOIN quiz.levels l
WHERE u.username = 'student1'
AND l.level_code IN ('level1', 'level2', 'level3')
AND NOT EXISTS (
    SELECT 1 FROM quiz.user_levels ul
    WHERE ul.user_id = u.user_id AND ul.level_id = l.level_id
);

-- Remove student from level (mark as completed)
UPDATE quiz.user_levels
SET completed_at = NOW()
WHERE user_id = (SELECT user_id FROM quiz.users WHERE username = 'student1')
AND level_id = (SELECT level_id FROM quiz.levels WHERE level_code = 'level1');

-- ============================================================================
-- ASSIGN TUTORS TO LEVELS
-- ============================================================================

-- Assign tutor to a level
INSERT INTO quiz.tutor_level_assignments (tutor_id, level_id, is_active)
SELECT 
    u.user_id,
    l.level_id,
    true
FROM quiz.users u, quiz.levels l
WHERE u.username = 'tutor1'
AND l.level_code = 'level1'
AND u.role IN ('tutor', 'admin')
AND NOT EXISTS (
    SELECT 1 FROM quiz.tutor_level_assignments tla
    WHERE tla.tutor_id = u.user_id AND tla.level_id = l.level_id
)
RETURNING assignment_id;

-- Assign tutor to multiple levels
INSERT INTO quiz.tutor_level_assignments (tutor_id, level_id, is_active)
SELECT 
    u.user_id,
    l.level_id,
    true
FROM quiz.users u
CROSS JOIN quiz.levels l
WHERE u.username = 'tutor1'
AND l.level_code IN ('level1', 'level2')
AND u.role IN ('tutor', 'admin')
AND NOT EXISTS (
    SELECT 1 FROM quiz.tutor_level_assignments tla
    WHERE tla.tutor_id = u.user_id AND tla.level_id = l.level_id
);

-- Remove tutor from level
UPDATE quiz.tutor_level_assignments
SET is_active = false
WHERE tutor_id = (SELECT user_id FROM quiz.users WHERE username = 'tutor1')
AND level_id = (SELECT level_id FROM quiz.levels WHERE level_code = 'level1');

-- ============================================================================
-- ASSIGN QUIZZES TO LEVELS
-- ============================================================================

-- View quizzes and their assigned levels
SELECT 
    q.quiz_id,
    q.title,
    q.difficulty,
    q.subject,
    l.level_code,
    l.level_name,
    COUNT(DISTINCT qq.question_id) as question_count
FROM quiz.quizzes q
LEFT JOIN quiz.levels l ON q.level_id = l.level_id
LEFT JOIN quiz.quiz_questions qq ON q.quiz_id = qq.quiz_id
WHERE q.deleted_at IS NULL
GROUP BY q.quiz_id, q.title, q.difficulty, q.subject, l.level_code, l.level_name
ORDER BY l.display_order NULLS LAST, q.title;

-- Assign a quiz to a level
UPDATE quiz.quizzes
SET level_id = (SELECT level_id FROM quiz.levels WHERE level_code = 'level1')
WHERE title = 'Math Basics Quiz';

-- Assign quizzes by age range to levels
-- Ages 3-6 → level0
UPDATE quiz.quizzes
SET level_id = (SELECT level_id FROM quiz.levels WHERE level_code = 'level0')
WHERE age_min >= 3 AND age_max <= 6 AND deleted_at IS NULL;

-- Ages 7-9 → level1
UPDATE quiz.quizzes
SET level_id = (SELECT level_id FROM quiz.levels WHERE level_code = 'level1')
WHERE age_min >= 7 AND age_max <= 9 AND deleted_at IS NULL;

-- Ages 10-12 → level2
UPDATE quiz.quizzes
SET level_id = (SELECT level_id FROM quiz.levels WHERE level_code = 'level2')
WHERE age_min >= 10 AND age_max <= 12 AND deleted_at IS NULL;

-- Ages 13-15 → level3
UPDATE quiz.quizzes
SET level_id = (SELECT level_id FROM quiz.levels WHERE level_code = 'level3')
WHERE age_min >= 13 AND age_max <= 15 AND deleted_at IS NULL;

-- Ages 16-18 → level4
UPDATE quiz.quizzes
SET level_id = (SELECT level_id FROM quiz.levels WHERE level_code = 'level4')
WHERE age_min >= 16 AND age_max <= 18 AND deleted_at IS NULL;

-- ============================================================================
-- REPORTING QUERIES
-- ============================================================================

-- Student progress report
SELECT 
    u.username,
    u.full_name,
    l.level_code,
    l.level_name,
    ul.progress_percentage,
    ul.enrolled_at,
    COUNT(DISTINCT a.attempt_id) as total_attempts,
    COUNT(DISTINCT CASE WHEN a.status = 'completed' THEN a.attempt_id END) as completed_attempts,
    ROUND(AVG(CASE WHEN a.status = 'completed' THEN a.total_score END), 2) as avg_score,
    ROUND(AVG(CASE WHEN a.status = 'completed' THEN (a.total_score / NULLIF(a.max_possible_score, 0)) * 100 END), 2) as avg_percentage
FROM quiz.users u
INNER JOIN quiz.user_levels ul ON u.user_id = ul.user_id
INNER JOIN quiz.levels l ON ul.level_id = l.level_id
LEFT JOIN quiz.quizzes q ON l.level_id = q.level_id AND q.deleted_at IS NULL
LEFT JOIN quiz.attempts a ON q.quiz_id = a.quiz_id AND a.user_id = u.user_id
WHERE u.role = 'student' AND ul.completed_at IS NULL
GROUP BY u.user_id, u.username, u.full_name, l.level_id, l.level_code, l.level_name, ul.progress_percentage, ul.enrolled_at
ORDER BY u.username, l.display_order;

-- Tutor workload report
SELECT 
    u.username as tutor_username,
    u.full_name as tutor_name,
    l.level_code,
    l.level_name,
    COUNT(DISTINCT ul.user_id) as student_count,
    COUNT(DISTINCT q.quiz_id) as quiz_count,
    COUNT(DISTINCT r.response_id) as total_responses,
    COUNT(DISTINCT CASE WHEN r.graded_at IS NULL THEN r.response_id END) as ungraded_responses
FROM quiz.users u
INNER JOIN quiz.tutor_level_assignments tla ON u.user_id = tla.tutor_id
INNER JOIN quiz.levels l ON tla.level_id = l.level_id
LEFT JOIN quiz.user_levels ul ON l.level_id = ul.level_id AND ul.completed_at IS NULL
LEFT JOIN quiz.quizzes q ON l.level_id = q.level_id AND q.deleted_at IS NULL
LEFT JOIN quiz.attempts a ON q.quiz_id = a.quiz_id
LEFT JOIN quiz.responses r ON a.attempt_id = r.attempt_id
WHERE u.role = 'tutor' AND tla.is_active = true
GROUP BY u.user_id, u.username, u.full_name, l.level_id, l.level_code, l.level_name, l.display_order
ORDER BY u.username, l.display_order;

-- Level completion statistics
SELECT 
    l.level_code,
    l.level_name,
    COUNT(DISTINCT CASE WHEN ul.completed_at IS NULL THEN ul.user_id END) as active_students,
    COUNT(DISTINCT CASE WHEN ul.completed_at IS NOT NULL THEN ul.user_id END) as completed_students,
    ROUND(AVG(CASE WHEN ul.completed_at IS NULL THEN ul.progress_percentage END), 2) as avg_progress,
    COUNT(DISTINCT q.quiz_id) as quiz_count,
    COUNT(DISTINCT a.attempt_id) as total_attempts,
    COUNT(DISTINCT CASE WHEN a.status = 'completed' THEN a.attempt_id END) as completed_attempts
FROM quiz.levels l
LEFT JOIN quiz.user_levels ul ON l.level_id = ul.level_id
LEFT JOIN quiz.quizzes q ON l.level_id = q.level_id AND q.deleted_at IS NULL
LEFT JOIN quiz.attempts a ON q.quiz_id = a.quiz_id
WHERE l.is_active = true
GROUP BY l.level_id, l.level_code, l.level_name, l.display_order
ORDER BY l.display_order;

-- ============================================================================
-- MAINTENANCE QUERIES
-- ============================================================================

-- Deactivate user
UPDATE quiz.users
SET is_active = false, deleted_at = NOW()
WHERE username = 'student1';

-- Reactivate user
UPDATE quiz.users
SET is_active = true, deleted_at = NULL
WHERE username = 'student1';

-- Update user password (must use BCrypt hash)
UPDATE quiz.users
SET password_hash = '$2a$11$NewBCryptHashHere',
    updated_at = NOW()
WHERE username = 'student1';

-- Update user role
UPDATE quiz.users
SET role = 'tutor', updated_at = NOW()
WHERE username = 'student1';

-- Clean up expired tokens (if you add a tokens table later)
-- DELETE FROM quiz.refresh_tokens WHERE expires_at < NOW();

-- ============================================================================
-- VERIFICATION QUERIES
-- ============================================================================

-- Verify schema is correctly set up
SELECT 
    'users' as table_name,
    COUNT(*) as row_count
FROM quiz.users
UNION ALL
SELECT 'levels', COUNT(*) FROM quiz.levels
UNION ALL
SELECT 'user_levels', COUNT(*) FROM quiz.user_levels
UNION ALL
SELECT 'tutor_level_assignments', COUNT(*) FROM quiz.tutor_level_assignments
UNION ALL
SELECT 'quizzes_with_levels', COUNT(*) FROM quiz.quizzes WHERE level_id IS NOT NULL;

-- Check for users without enrolled levels (students only)
SELECT 
    u.user_id,
    u.username,
    u.full_name,
    u.role
FROM quiz.users u
LEFT JOIN quiz.user_levels ul ON u.user_id = ul.user_id AND ul.completed_at IS NULL
WHERE u.role = 'student'
AND u.deleted_at IS NULL
AND ul.user_level_id IS NULL;

-- Check for quizzes without assigned levels
SELECT 
    quiz_id,
    title,
    difficulty,
    subject
FROM quiz.quizzes
WHERE level_id IS NULL
AND deleted_at IS NULL
ORDER BY created_at DESC;
