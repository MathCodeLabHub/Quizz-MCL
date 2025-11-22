DO $$
DECLARE
    v_version_id INT;
BEGIN
    v_version_id := quiz.register_migration('016_user_levels', 'Create  user_levels tables', '016_user_levels.sql');
END $$;

-- Create user_levels table to store level information
CREATE TABLE IF NOT EXISTS quiz.user_levels (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES quiz.users(user_id) ON DELETE CASCADE,
    role VARCHAR(50) NOT NULL,
    level VARCHAR(50) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, level)
);
CREATE INDEX IF NOT EXISTS idx_user_levels_user_id ON quiz.user_levels(user_id);
CREATE INDEX IF NOT EXISTS idx_user_levels_level ON quiz.user_levels(level);
COMMENT ON TABLE quiz.user_levels IS 'Stores user level assignments (populated during signup)';