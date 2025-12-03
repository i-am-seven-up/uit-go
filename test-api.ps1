# Test API with JWT token
$body = @{
    pickupLat = 10.762622
    pickupLng = 106.660172
    dropoffLat = 10.773996
    dropoffLng = 106.697214
} | ConvertTo-Json

# Generate JWT token using helper
Add-Type -Path "E2E.PerformanceTests\bin\Release\net8.0\E2E.PerformanceTests.dll"
$token = [E2E.PerformanceTests.Infrastructure.JwtTokenHelper]::GeneratePassengerToken()

Write-Host "Token: $token"
Write-Host "Body: $body"

# Test the API
$response = Invoke-WebRequest -Uri "http://127.0.0.1:8080/api/trips" `
    -Method POST `
    -Headers @{
        "Authorization" = "Bearer $token"
        "Content-Type" = "application/json"
    } `
    -Body $body `
    -UseBasicParsing

Write-Host "Status: $($response.StatusCode)"
Write-Host "Response: $($response.Content)"
