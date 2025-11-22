# PowerShell script to generate a test API key
# This creates a valid API key that you can use for testing

# Generate a random API key
$prefix = "sk_test_"
$randomBytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($randomBytes)
$randomString = [System.Convert]::ToBase64String($randomBytes).Replace("+", "").Replace("/", "").Replace("=", "").Substring(0, 40)
$apiKey = "$prefix$randomString"

Write-Host "========================================" -ForegroundColor Green
Write-Host "Generated API Key (SAVE THIS!):" -ForegroundColor Yellow
Write-Host $apiKey -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "⚠️  IMPORTANT: Copy this key now! It won't be shown again." -ForegroundColor Red
Write-Host ""
Write-Host "To insert this into your database, run:" -ForegroundColor Yellow
Write-Host ""
Write-Host "INSERT INTO quiz.api_keys (api_key_id, key_hash, key_prefix, name, description, scopes, is_active, created_at, updated_at)" -ForegroundColor White
Write-Host "VALUES (" -ForegroundColor White
Write-Host "    gen_random_uuid()," -ForegroundColor White
Write-Host "    crypt('$apiKey', gen_salt('bf'))," -ForegroundColor Cyan
Write-Host "    '$($apiKey.Substring(0, 10))'," -ForegroundColor Cyan
Write-Host "    'Test API Key'," -ForegroundColor White
Write-Host "    'Generated API key for testing'," -ForegroundColor White
Write-Host "    ARRAY['quiz:write', 'quiz:read', 'question:write', 'question:read', 'response:write', 'response:read', 'content:write', 'content:read']," -ForegroundColor White
Write-Host "    TRUE," -ForegroundColor White
Write-Host "    CURRENT_TIMESTAMP," -ForegroundColor White
Write-Host "    CURRENT_TIMESTAMP" -ForegroundColor White
Write-Host ");" -ForegroundColor White
Write-Host ""
Write-Host "API Key Details:" -ForegroundColor Yellow
Write-Host "  - Key: $apiKey" -ForegroundColor Cyan
Write-Host "  - Prefix: $($apiKey.Substring(0, 10))" -ForegroundColor Cyan
Write-Host "  - Length: $($apiKey.Length) characters" -ForegroundColor Cyan
Write-Host ""
Write-Host "Use this key in your API requests:" -ForegroundColor Yellow
Write-Host "  Header: X-API-Key: $apiKey" -ForegroundColor Cyan
