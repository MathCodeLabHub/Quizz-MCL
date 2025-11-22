-- ============================================================================
-- 008_api_keys.sql
-- Create `api_keys` and `api_key_audit` tables for secure API authentication
-- ============================================================================

DO $$
DECLARE
    v_version_id INT;
BEGIN
    v_version_id := quiz.register_migration('008-api-keys', 'Create api_keys and api_key_audit tables', '008_api_keys.sql');
END $$;

-- TABLE: api_keys
-- Stores API keys with bcrypt hashed values for secure authentication
CREATE TABLE IF NOT EXISTS quiz.api_keys (
    api_key_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    
    -- Key identification
    key_hash TEXT NOT NULL,                    -- bcrypt hash of the actual key
    key_prefix TEXT NOT NULL,                  -- first 8 chars for identification (e.g., "sk_live_")
    
    -- Metadata
    name TEXT NOT NULL,                        -- human-readable name (e.g., "Mobile App", "Admin Dashboard")
    description TEXT,                          -- optional description
    
    -- Permissions & scopes
    scopes TEXT[] NOT NULL DEFAULT '{}',       -- e.g., {"quiz:read", "quiz:write", "quiz:delete", "question:write"}
    is_admin BOOLEAN DEFAULT FALSE,            -- admin keys have all permissions
    
    -- Rate limiting
    rate_limit_per_hour INT DEFAULT 1000,      -- requests per hour
    rate_limit_per_day INT DEFAULT 10000,      -- requests per day
    
    -- Status & lifecycle
    is_active BOOLEAN DEFAULT TRUE,
    expires_at TIMESTAMP,                      -- optional expiration
    
    -- Tracking
    created_at TIMESTAMP DEFAULT NOW(),
    created_by UUID,                           -- user/admin who created the key
    last_used_at TIMESTAMP,
    last_used_ip INET,
    usage_count BIGINT DEFAULT 0,
    
    -- Metadata
    metadata JSONB DEFAULT '{}'::jsonb,        -- additional context (e.g., environment, tags)
    
    CONSTRAINT valid_rate_limits CHECK (rate_limit_per_hour > 0 AND rate_limit_per_day > 0),
    CONSTRAINT valid_scopes CHECK (array_length(scopes, 1) IS NULL OR array_length(scopes, 1) > 0 OR is_admin = TRUE)
);

-- Indexes for api_keys
CREATE INDEX IF NOT EXISTS idx_api_keys_key_hash ON quiz.api_keys(key_hash);
CREATE INDEX IF NOT EXISTS idx_api_keys_key_prefix ON quiz.api_keys(key_prefix);
CREATE INDEX IF NOT EXISTS idx_api_keys_is_active ON quiz.api_keys(is_active) WHERE is_active = TRUE;
CREATE INDEX IF NOT EXISTS idx_api_keys_expires_at ON quiz.api_keys(expires_at) WHERE expires_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_api_keys_created_by ON quiz.api_keys(created_by);
CREATE INDEX IF NOT EXISTS idx_api_keys_scopes ON quiz.api_keys USING GIN(scopes);
CREATE INDEX IF NOT EXISTS idx_api_keys_metadata ON quiz.api_keys USING GIN(metadata);

-- TABLE: api_key_audit
-- Logs every API key usage for security monitoring and rate limiting
CREATE TABLE IF NOT EXISTS quiz.api_key_audit (
    audit_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    api_key_id UUID NOT NULL REFERENCES quiz.api_keys(api_key_id) ON DELETE CASCADE,
    
    -- Request details
    timestamp TIMESTAMP DEFAULT NOW() NOT NULL,
    http_method TEXT NOT NULL,                 -- GET, POST, DELETE, etc.
    endpoint TEXT NOT NULL,                    -- /api/quizzes, /api/questions/{id}, etc.
    
    -- Request metadata
    ip_address INET,
    user_agent TEXT,
    request_id TEXT,                           -- correlation ID for tracing
    
    -- Response details
    status_code INT,                           -- 200, 401, 429, 500, etc.
    response_time_ms INT,                      -- response time in milliseconds
    
    -- Authorization
    required_scope TEXT,                       -- the scope that was required (e.g., "quiz:write")
    was_authorized BOOLEAN NOT NULL,           -- whether the key had permission
    
    -- Rate limiting
    rate_limit_exceeded BOOLEAN DEFAULT FALSE,
    
    -- Error tracking
    error_message TEXT,                        -- if request failed
    
    -- Additional context
    metadata JSONB DEFAULT '{}'::jsonb
);

-- Indexes for api_key_audit
CREATE INDEX IF NOT EXISTS idx_api_key_audit_api_key_id ON quiz.api_key_audit(api_key_id, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_api_key_audit_timestamp ON quiz.api_key_audit(timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_api_key_audit_ip_address ON quiz.api_key_audit(ip_address, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_api_key_audit_endpoint ON quiz.api_key_audit(endpoint, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_api_key_audit_status_code ON quiz.api_key_audit(status_code) WHERE status_code >= 400;
CREATE INDEX IF NOT EXISTS idx_api_key_audit_unauthorized ON quiz.api_key_audit(was_authorized, timestamp DESC) WHERE was_authorized = FALSE;
CREATE INDEX IF NOT EXISTS idx_api_key_audit_rate_limited ON quiz.api_key_audit(rate_limit_exceeded, timestamp DESC) WHERE rate_limit_exceeded = TRUE;

-- Function: Update last_used_at and usage_count when key is used
CREATE OR REPLACE FUNCTION quiz.update_api_key_usage()
RETURNS TRIGGER AS $$
BEGIN
    -- Only update if request was authorized
    IF NEW.was_authorized = TRUE THEN
        UPDATE quiz.api_keys
        SET 
            last_used_at = NEW.timestamp,
            last_used_ip = NEW.ip_address,
            usage_count = usage_count + 1
        WHERE api_key_id = NEW.api_key_id;
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Drop trigger if exists before creating
DROP TRIGGER IF EXISTS trg_update_api_key_usage ON quiz.api_key_audit;

CREATE TRIGGER trg_update_api_key_usage
AFTER INSERT ON quiz.api_key_audit
FOR EACH ROW
EXECUTE FUNCTION quiz.update_api_key_usage();

-- Function: Check if API key is rate limited (hourly)
CREATE OR REPLACE FUNCTION quiz.is_api_key_rate_limited_hourly(p_api_key_id UUID)
RETURNS BOOLEAN AS $$
DECLARE
    v_rate_limit INT;
    v_usage_count BIGINT;
BEGIN
    -- Get the rate limit for this key
    SELECT rate_limit_per_hour INTO v_rate_limit
    FROM quiz.api_keys
    WHERE api_key_id = p_api_key_id;
    
    -- Count requests in the last hour
    SELECT COUNT(*) INTO v_usage_count
    FROM quiz.api_key_audit
    WHERE api_key_id = p_api_key_id
      AND timestamp >= NOW() - INTERVAL '1 hour'
      AND was_authorized = TRUE;
    
    RETURN v_usage_count >= v_rate_limit;
END;
$$ LANGUAGE plpgsql;

-- Function: Check if API key is rate limited (daily)
CREATE OR REPLACE FUNCTION quiz.is_api_key_rate_limited_daily(p_api_key_id UUID)
RETURNS BOOLEAN AS $$
DECLARE
    v_rate_limit INT;
    v_usage_count BIGINT;
BEGIN
    -- Get the rate limit for this key
    SELECT rate_limit_per_day INTO v_rate_limit
    FROM quiz.api_keys
    WHERE api_key_id = p_api_key_id;
    
    -- Count requests in the last 24 hours
    SELECT COUNT(*) INTO v_usage_count
    FROM quiz.api_key_audit
    WHERE api_key_id = p_api_key_id
      AND timestamp >= NOW() - INTERVAL '24 hours'
      AND was_authorized = TRUE;
    
    RETURN v_usage_count >= v_rate_limit;
END;
$$ LANGUAGE plpgsql;

-- Function: Get API key usage stats
CREATE OR REPLACE FUNCTION quiz.get_api_key_stats(p_api_key_id UUID, p_days INT DEFAULT 7)
RETURNS TABLE (
    total_requests BIGINT,
    successful_requests BIGINT,
    failed_requests BIGINT,
    unauthorized_requests BIGINT,
    rate_limited_requests BIGINT,
    avg_response_time_ms NUMERIC,
    unique_ips BIGINT
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        COUNT(*)::BIGINT as total_requests,
        COUNT(*) FILTER (WHERE status_code < 400)::BIGINT as successful_requests,
        COUNT(*) FILTER (WHERE status_code >= 400)::BIGINT as failed_requests,
        COUNT(*) FILTER (WHERE was_authorized = FALSE)::BIGINT as unauthorized_requests,
        COUNT(*) FILTER (WHERE rate_limit_exceeded = TRUE)::BIGINT as rate_limited_requests,
        AVG(response_time_ms)::NUMERIC as avg_response_time_ms,
        COUNT(DISTINCT ip_address)::BIGINT as unique_ips
    FROM quiz.api_key_audit
    WHERE api_key_id = p_api_key_id
      AND timestamp >= NOW() - (p_days || ' days')::INTERVAL;
END;
$$ LANGUAGE plpgsql;

-- Seed data: Create a default admin API key for testing
-- Key: sk_test_admin_1234567890abcdef1234567890abcdef
-- (This is just an example - replace with real bcrypt hash in production)
INSERT INTO quiz.api_keys (
    key_hash,
    key_prefix,
    name,
    description,
    scopes,
    is_admin,
    rate_limit_per_hour,
    rate_limit_per_day,
    metadata
) VALUES (
    -- This is a placeholder hash - you'll generate real ones via your API
    '$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy', -- bcrypt hash of "test_key_12345"
    'sk_test_',
    'Test Admin Key',
    'Default admin key for development and testing',
    ARRAY['quiz:read', 'quiz:write', 'quiz:delete', 'question:read', 'question:write', 'question:delete'],
    TRUE,
    5000,
    50000,
    '{"environment": "development", "created_by": "system"}'::jsonb
) ON CONFLICT DO NOTHING;

-- Complete migration
DO $$
DECLARE
    v_version_id INT;
BEGIN
    SELECT version_id INTO v_version_id FROM quiz.schema_versions WHERE version_number = '008-api-keys';
    PERFORM quiz.complete_migration(v_version_id, 0);
    PERFORM quiz.log_migration(v_version_id, 'INFO', 'api_keys and api_key_audit tables created with triggers and helper functions');
END $$;
