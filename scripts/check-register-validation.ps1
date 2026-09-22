param([string]$BaseUrl = "http://localhost:5000")

$body = @{
    fullName = ""
    email = "invalid"
    password = "ABCDEF1!"
    confirmPassword = "different"
} | ConvertTo-Json

try {
    Invoke-WebRequest -UseBasicParsing -Uri "$BaseUrl/api/v1/auth/register" `
        -Method Post -ContentType "application/json" -Body $body | Out-Null
    throw "Expected HTTP 400"
}
catch {
    $response = $_.Exception.Response
    if ($null -eq $response -or [int]$response.StatusCode -ne 400) { throw }

    $reader = [IO.StreamReader]::new($response.GetResponseStream())
    $result = $reader.ReadToEnd() | ConvertFrom-Json
    $fields = @($result.errors | ForEach-Object field)
    if ($fields -notcontains "fullName" -or $fields -notcontains "email" -or $fields -notcontains "password" -or $fields -notcontains "confirmPassword") {
        throw "Missing expected validation errors"
    }
}

Write-Output "Register validation check passed"
