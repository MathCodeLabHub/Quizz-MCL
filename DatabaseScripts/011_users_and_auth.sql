-- ============================================================================
-- 011_users_and_auth.sql
-- Create authentication and user management system
-- Includes: users, levels, user_levels, and tutor_level_assignments tables
-- ============================================================================

DO $$
DECLARE
    v_version_id INT;
BEGIN
    v_version_id := quiz.register_migration('011-users-auth', 'Create users, levels, and authentication tables', '011_users_and_auth.sql');
END $$;

-- ============================================================================
-- TABLE: users
-- Core authentication and user information
-- ============================================================================
CREATE TABLE IF NOT EXISTS quiz.users (
    user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username VARCHAR(100) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,  -- bcrypt hashed password
    email VARCHAR(255) UNIQUE,
    full_name VARCHAR(255),
    role VARCHAR(20) NOT NULL CHECK (role IN ('student', 'tutor', 'admin')),
    is_active BOOLEAN DEFAULT true,
    last_login_at TIMESTAMP,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW(),
    deleted_at TIMESTAMP,
    metadata JSONB,  -- Additional user preferences, settings, etc.
    
    CONSTRAINT username_lowercase CHECK (username = LOWER(username)),
    CONSTRAINT username_length CHECK (LENGTH(username) >= 3),
    CONSTRAINT email_format CHECK (email ~* '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$' OR email IS NULL)
);

-- Trigger for updated_at
CREATE TRIGGER update_users_updated_at
    BEFORE UPDATE ON quiz.users
    FOR EACH ROW
    EXECUTE FUNCTION quiz.update_updated_at_column();

-- Indexes for users
CREATE INDEX IF NOT EXISTS idx_users_username ON quiz.users(username) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_users_email ON quiz.users(email) WHERE deleted_at IS NULL AND email IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_users_role ON quiz.users(role) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_users_is_active ON quiz.users(is_active, role) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_users_last_login ON quiz.users(last_login_at DESC) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_users_created_at ON quiz.users(created_at DESC) WHERE deleted_at IS NULL;

COMMENT ON TABLE quiz.users IS 'Core user authentication and profile information';
COMMENT ON COLUMN quiz.users.password_hash IS 'bcrypt hashed password (never store plain text)';
COMMENT ON COLUMN quiz.users.role IS 'User role: student (takes quizzes), tutor (creates quizzes, grades), admin (full access)';
COMMENT ON COLUMN quiz.users.metadata IS 'Additional user settings, preferences, profile data as JSONB';

-- ============================================================================
-- TABLE: levels
-- Define education levels (e.g., level0, level1, level2, level3, level4)
-- ============================================================================
CREATE TABLE IF NOT EXISTS quiz.levels (
    level_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    level_code VARCHAR(50) UNIQUE NOT NULL,  -- e.g., 'level0', 'level1', 'level2'
    level_name VARCHAR(255) NOT NULL,        -- e.g., 'Beginner', 'Intermediate', 'Advanced'
    description TEXT,
    display_order INT NOT NULL,              -- For sorting in UI
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW(),
    
    CONSTRAINT level_code_lowercase CHECK (level_code = LOWER(level_code)),
    CONSTRAINT display_order_positive CHECK (display_order >= 0)
);

-- Trigger for updated_at
CREATE TRIGGER update_levels_updated_at
    BEFORE UPDATE ON quiz.levels
    FOR EACH ROW
    EXECUTE FUNCTION quiz.update_updated_at_column();

-- Indexes for levels
CREATE INDEX IF NOT EXISTS idx_levels_level_code ON quiz.levels(level_code) WHERE is_active = true;
CREATE INDEX IF NOT EXISTS idx_levels_display_order ON quiz.levels(display_order) WHERE is_active = true;
CREATE INDEX IF NOT EXISTS idx_levels_is_active ON quiz.levels(is_active);

COMMENT ON TABLE quiz.levels IS 'Education levels (e.g., level0 to level4) for organizing content';
COMMENT ON COLUMN quiz.levels.level_code IS 'Unique identifier code (e.g., level0, level1)';
COMMENT ON COLUMN quiz.levels.display_order IS 'Order for displaying levels in UI (lower numbers first)';

-- ============================================================================
-- TABLE: user_levels
-- Many-to-many relationship between users and levels (enrollment)
-- ============================================================================
CREATE TABLE IF NOT EXISTS quiz.user_levels (
    user_level_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES quiz.users(user_id) ON DELETE CASCADE,
    level_id UUID NOT NULL REFERENCES quiz.levels(level_id) ON DELETE CASCADE,
    enrolled_at TIMESTAMP DEFAULT NOW(),
    completed_at TIMESTAMP,  -- NULL = in progress, NOT NULL = completed
    progress_percentage DECIMAL(5,2) DEFAULT 0.0 CHECK (progress_percentage >= 0 AND progress_percentage <= 100),
    
    CONSTRAINT unique_user_level UNIQUE (user_id, level_id),
    CONSTRAINT completed_after_enrolled CHECK (completed_at IS NULL OR completed_at >= enrolled_at)
);

-- Indexes for user_levels
CREATE INDEX IF NOT EXISTS idx_user_levels_user_id ON quiz.user_levels(user_id, enrolled_at DESC);
CREATE INDEX IF NOT EXISTS idx_user_levels_level_id ON quiz.user_levels(level_id, enrolled_at DESC);
CREATE INDEX IF NOT EXISTS idx_user_levels_completed ON quiz.user_levels(completed_at DESC) WHERE completed_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_user_levels_in_progress ON quiz.user_levels(user_id, level_id) WHERE completed_at IS NULL;

COMMENT ON TABLE quiz.user_levels IS 'Student enrollment in education levels (many-to-many relationship)';
COMMENT ON COLUMN quiz.user_levels.progress_percentage IS 'Optional: Track completion progress (0-100%)';

-- ============================================================================
-- TABLE: tutor_level_assignments
-- Which tutors are assigned to teach which levels
-- ============================================================================
CREATE TABLE IF NOT EXISTS quiz.tutor_level_assignments (
    assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tutor_id UUID NOT NULL REFERENCES quiz.users(user_id) ON DELETE CASCADE,
    level_id UUID NOT NULL REFERENCES quiz.levels(level_id) ON DELETE CASCADE,
    assigned_at TIMESTAMP DEFAULT NOW(),
    is_active BOOLEAN DEFAULT true,
    
    CONSTRAINT unique_tutor_level UNIQUE (tutor_id, level_id),
    CONSTRAINT tutor_must_be_tutor CHECK (
        EXISTS (
            SELECT 1 FROM quiz.users 
            WHERE user_id = tutor_id 
            AND role IN ('tutor', 'admin')
        )
    )
);

-- Indexes for tutor_level_assignments
CREATE INDEX IF NOT EXISTS idx_tutor_assignments_tutor_id ON quiz.tutor_level_assignments(tutor_id) WHERE is_active = true;
CREATE INDEX IF NOT EXISTS idx_tutor_assignments_level_id ON quiz.tutor_level_assignments(level_id) WHERE is_active = true;
CREATE INDEX IF NOT EXISTS idx_tutor_assignments_active ON quiz.tutor_level_assignments(is_active, assigned_at DESC);

COMMENT ON TABLE quiz.tutor_level_assignments IS 'Assigns tutors to levels they can manage and grade';
COMMENT ON COLUMN quiz.tutor_level_assignments.is_active IS 'Allows temporarily disabling assignments without deletion';

-- ============================================================================
-- Add level_id to existing quizzes table
-- ============================================================================
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_schema = 'quiz' 
        AND table_name = 'quizzes' 
        AND column_name = 'level_id'
    ) THEN
        ALTER TABLE quiz.quizzes 
        ADD COLUMN level_id UUID REFERENCES quiz.levels(level_id);
        
        -- Create index on level_id
        CREATE INDEX idx_quizzes_level_id ON quiz.quizzes(level_id) WHERE deleted_at IS NULL;
        
        COMMENT ON COLUMN quiz.quizzes.level_id IS 'Education level this quiz belongs to';
    END IF;
END $$;

-- ============================================================================
-- Update attempts table to use UUID user_id (foreign key to users table)
-- ============================================================================
DO $$
BEGIN
    -- Check if user_id is still VARCHAR
    IF EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_schema = 'quiz' 
        AND table_name = 'attempts' 
        AND column_name = 'user_id'
        AND data_type = 'character varying'
    ) THEN
        -- Add temporary new column
        ALTER TABLE quiz.attempts ADD COLUMN user_id_new UUID;
        
        -- You'll need to migrate existing data manually or set to NULL
        -- For now, just create the column structure
        
        -- Later, you can:
        -- 1. Migrate data: UPDATE quiz.attempts SET user_id_new = (SELECT user_id FROM quiz.users WHERE username = attempts.user_id);
        -- 2. Drop old column: ALTER TABLE quiz.attempts DROP COLUMN user_id;
        -- 3. Rename new column: ALTER TABLE quiz.attempts RENAME COLUMN user_id_new TO user_id;
        -- 4. Add foreign key: ALTER TABLE quiz.attempts ADD CONSTRAINT fk_attempts_user_id FOREIGN KEY (user_id) REFERENCES quiz.users(user_id);
        
        RAISE NOTICE 'Added user_id_new column to attempts table. Manual data migration required.';
    END IF;
END $$;

-- ============================================================================
-- Seed default levels (level0 to level4)
-- ============================================================================
INSERT INTO quiz.levels (level_code, level_name, description, display_order, is_active)
VALUES
    ('level0', 'Foundation', 'Foundational concepts and basic skills', 0, true),
    ('level1', 'Beginner', 'Introduction to core topics', 1, true),
    ('level2', 'Intermediate', 'Building on fundamental knowledge', 2, true),
    ('level3', 'Advanced', 'Complex topics and applications', 3, true),
    ('level4', 'Expert', 'Mastery level and advanced challenges', 4, true)
ON CONFLICT (level_code) DO NOTHING;

-- ============================================================================
-- Helper Functions
-- ============================================================================

-- Function to get all quizzes for a student based on their enrolled levels
CREATE OR REPLACE FUNCTION quiz.get_student_quizzes(p_user_id UUID)
RETURNS TABLE (
    quiz_id UUID,
    title VARCHAR(255),
    description TEXT,
    level_code VARCHAR(50),
    level_name VARCHAR(255),
    difficulty VARCHAR(20),
    estimated_minutes INT
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        q.quiz_id,
        q.title,
        q.description,
        l.level_code,
        l.level_name,
        q.difficulty,
        q.estimated_minutes
    FROM quiz.quizzes q
    INNER JOIN quiz.levels l ON q.level_id = l.level_id
    INNER JOIN quiz.user_levels ul ON l.level_id = ul.level_id
    WHERE ul.user_id = p_user_id
        AND q.deleted_at IS NULL
        AND l.is_active = true
        AND ul.completed_at IS NULL  -- Only show current enrollments
    ORDER BY l.display_order, q.created_at DESC;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION quiz.get_student_quizzes(UUID) IS 'Returns all quizzes for levels the student is enrolled in';

-- Function to get all levels assigned to a tutor
CREATE OR REPLACE FUNCTION quiz.get_tutor_levels(p_tutor_id UUID)
RETURNS TABLE (
    level_id UUID,
    level_code VARCHAR(50),
    level_name VARCHAR(255),
    description TEXT,
    student_count BIGINT,
    quiz_count BIGINT
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        l.level_id,
        l.level_code,
        l.level_name,
        l.description,
        COUNT(DISTINCT ul.user_id) as student_count,
        COUNT(DISTINCT q.quiz_id) as quiz_count
    FROM quiz.levels l
    INNER JOIN quiz.tutor_level_assignments tla ON l.level_id = tla.level_id
    LEFT JOIN quiz.user_levels ul ON l.level_id = ul.level_id AND ul.completed_at IS NULL
    LEFT JOIN quiz.quizzes q ON l.level_id = q.level_id AND q.deleted_at IS NULL
    WHERE tla.tutor_id = p_tutor_id
        AND tla.is_active = true
        AND l.is_active = true
    GROUP BY l.level_id, l.level_code, l.level_name, l.description, l.display_order
    ORDER BY l.display_order;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION quiz.get_tutor_levels(UUID) IS 'Returns all levels assigned to a tutor with statistics';

-- Function to get student responses for a tutor's assigned levels
CREATE OR REPLACE FUNCTION quiz.get_tutor_student_responses(
    p_tutor_id UUID,
    p_level_id UUID DEFAULT NULL
)
RETURNS TABLE (
    response_id UUID,
    student_username VARCHAR(100),
    student_full_name VARCHAR(255),
    quiz_title VARCHAR(255),
    question_text TEXT,
    submitted_at TIMESTAMP,
    points_earned DECIMAL(10,2),
    points_possible DECIMAL(10,2),
    is_correct BOOLEAN,
    level_code VARCHAR(50)
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        r.response_id,
        u.username as student_username,
        u.full_name as student_full_name,
        qz.title as quiz_title,
        qs.question_text,
        r.submitted_at,
        r.points_earned,
        r.points_possible,
        r.is_correct,
        l.level_code
    FROM quiz.responses r
    INNER JOIN quiz.attempts a ON r.attempt_id = a.attempt_id
    INNER JOIN quiz.users u ON a.user_id = u.user_id
    INNER JOIN quiz.quizzes qz ON a.quiz_id = qz.quiz_id
    INNER JOIN quiz.questions qs ON r.question_id = qs.question_id
    INNER JOIN quiz.levels l ON qz.level_id = l.level_id
    INNER JOIN quiz.tutor_level_assignments tla ON l.level_id = tla.level_id
    WHERE tla.tutor_id = p_tutor_id
        AND tla.is_active = true
        AND (p_level_id IS NULL OR l.level_id = p_level_id)
        AND qz.deleted_at IS NULL
        AND qs.deleted_at IS NULL
    ORDER BY r.submitted_at DESC;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION quiz.get_tutor_student_responses(UUID, UUID) IS 'Returns all student responses for quizzes in tutor''s assigned levels';

-- ============================================================================
-- Security: Row Level Security (RLS) Policies
-- ============================================================================

-- Enable RLS on users table
ALTER TABLE quiz.users ENABLE ROW LEVEL SECURITY;

-- Policy: Users can view their own profile
CREATE POLICY users_view_own ON quiz.users
    FOR SELECT
    USING (user_id = current_setting('app.current_user_id')::UUID OR current_setting('app.current_user_role') IN ('admin', 'tutor'));

-- Policy: Only admins can insert users
CREATE POLICY users_insert_admin_only ON quiz.users
    FOR INSERT
    WITH CHECK (current_setting('app.current_user_role') = 'admin');

-- Policy: Users can update their own profile, admins can update all
CREATE POLICY users_update_own_or_admin ON quiz.users
    FOR UPDATE
    USING (user_id = current_setting('app.current_user_id')::UUID OR current_setting('app.current_user_role') = 'admin');

-- ============================================================================
-- Complete migration
-- ============================================================================
DO $$
DECLARE
    v_version_id INT;
BEGIN
    SELECT version_id INTO v_version_id FROM quiz.schema_versions WHERE version_number = '011-users-auth';
    PERFORM quiz.complete_migration(v_version_id, 0);
    PERFORM quiz.log_migration(v_version_id, 'INFO', 'Users, levels, and authentication system created');
END $$;
