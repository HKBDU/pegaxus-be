param([string]$BaseUrl = "http://localhost:5000")

function Assert-ValidationError([string]$Path, [hashtable]$Body, [string[]]$ExpectedFields) {
    try {
        Invoke-WebRequest -UseBasicParsing -Uri "$BaseUrl$Path" `
            -Method Post -ContentType "application/json" -Body ($Body | ConvertTo-Json) | Out-Null
        throw "Expected HTTP 400 from $Path"
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response -or [int]$response.StatusCode -ne 400) { throw }

        $reader = [IO.StreamReader]::new($response.GetResponseStream())
        $result = $reader.ReadToEnd() | ConvertFrom-Json
        $fields = @($result.errors | ForEach-Object field)
        foreach ($field in $ExpectedFields) {
            if ($fields -notcontains $field) { throw "Missing '$field' validation error from $Path" }
        }
    }
}

Assert-ValidationError "/api/v1/auth/register" @{
    fullName = ""
    email = "invalid"
    password = "ABCDEF1!"
    confirmPassword = "different"
} @("fullName", "email", "password", "confirmPassword")

Assert-ValidationError "/api/v1/auth/forgot-password" @{
    email = "invalid"
} @("email")

Write-Output "Auth validation checks passed"
