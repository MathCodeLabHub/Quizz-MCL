-- =============================================
-- COMPLETE SETUP - Run this file in pgAdmin
-- =============================================

-- Step 1: Create users table
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

-- Step 2: Create user_levels table
CREATE TABLE IF NOT EXISTS quiz.user_levels (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES quiz.users(user_id) ON DELETE CASCADE,
    role VARCHAR(50) NOT NULL,
    level VARCHAR(50) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, level)
);

-- Step 3: Create indexes
CREATE INDEX IF NOT EXISTS idx_users_username ON quiz.users(username);
CREATE INDEX IF NOT EXISTS idx_users_role ON quiz.users(role);
CREATE INDEX IF NOT EXISTS idx_user_levels_user_id ON quiz.user_levels(user_id);
CREATE INDEX IF NOT EXISTS idx_user_levels_level ON quiz.user_levels(level);

-- Step 4: Insert admin user
INSERT INTO quiz.users (user_id, username, password, full_name, role, is_active)
VALUES (gen_random_uuid(), 'admin', 'admin123', 'System Administrator', 'admin', true)
ON CONFLICT (username) DO NOTHING;

-- Verify setup
SELECT 'Tables created successfully!' as status;
SELECT * FROM quiz.users WHERE username = 'admin';
