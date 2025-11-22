-- =============================================
-- Simple Users and User Levels Tables
-- =============================================

DO $$
DECLARE
    v_version_id INT;
BEGIN
    v_version_id := quiz.register_migration('015-simple-users', 'Create simple users and user_levels tables', '015_simple_users.sql');
END $$;

-- Create users table
CREATE TABLE IF NOT EXISTS quiz.users (
    user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username VARCHAR(100) UNIQUE NOT NULL,
    password VARCHAR(255) NOT NULL,
    full_name VARCHAR(255),
    role VARCHAR(20) NOT NULL CHECK (role IN ('student', 'tutor', 'admin')),
    is_active BOOLEAN DEFAULT true,
    last_login_at TIMESTAMP,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW(),
    deleted_at TIMESTAMP

);

-- Create indexes for better query performance
CREATE INDEX IF NOT EXISTS idx_users_username ON quiz.users(username);
CREATE INDEX IF NOT EXISTS idx_users_role ON quiz.users(role);
COMMENT ON TABLE quiz.users IS 'Stores user authentication information';
-- Insert a test admin user (password: "admin123" - you should hash this in production)
INSERT INTO quiz.users (user_id, username, password, full_name, role, is_active)
VALUES (gen_random_uuid(), 'admin', 'admin123', 'System Administrator', 'admin', true)
ON CONFLICT (username) DO NOTHING;



