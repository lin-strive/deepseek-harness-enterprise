param(
    [Parameter(Mandatory)]
    [uri] $BaseUrl,

    [Parameter(Mandatory)]
    [string] $CertificatePath,

    [Parameter(Mandatory)]
    [string] $ActivationCode
)

$ErrorActionPreference = 'Stop'
$temporaryFiles = [Collections.Generic.List[string]]::new()
$jobs = [Collections.Generic.List[object]]::new()

function Invoke-JsonRequest(
    [string] $RelativePath,
    [object] $Body,
    [string] $BearerToken = '') {
    $bodyFile = [IO.Path]::GetTempFileName()
    $temporaryFiles.Add($bodyFile)
    [IO.File]::WriteAllText($bodyFile, ($Body | ConvertTo-Json -Compress))
    $arguments = @(
        '--silent',
        '--show-error',
        '--cacert', $CertificatePath,
        '--header', 'Content-Type: application/json',
        '--data-binary', "@$bodyFile"
    )
    if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
        $arguments += @('--header', "Authorization: Bearer $BearerToken")
    }

    $arguments += $RelativePath
    $response = & curl.exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "curl failed for $RelativePath."
    }

    return $response | ConvertFrom-Json
}

try {
    $root = $BaseUrl.AbsoluteUri.TrimEnd('/')
    $previewRequest = @{ activationCode = $ActivationCode; clientVersion = 'integration-test' }
    $preview1 = Invoke-JsonRequest "$root/api/v1/activation/preview" $previewRequest
    $preview2 = Invoke-JsonRequest "$root/api/v1/activation/preview" $previewRequest

    foreach ($sessionId in @($preview1.activationSessionId, $preview2.activationSessionId)) {
        if ([string]::IsNullOrWhiteSpace($sessionId)) {
            throw 'Preview did not return an activation session.'
        }

        $requestFile = [IO.Path]::GetTempFileName()
        $responseFile = [IO.Path]::GetTempFileName()
        $temporaryFiles.Add($requestFile)
        $temporaryFiles.Add($responseFile)
        [IO.File]::WriteAllText(
            $requestFile,
            (@{ activationSessionId = $sessionId } | ConvertTo-Json -Compress))

        $jobs.Add((Start-Job -ScriptBlock {
            param($CaPath, $RequestPath, $ResponsePath, $Url)

            $status = & curl.exe `
                --silent `
                --show-error `
                --cacert $CaPath `
                --header 'Content-Type: application/json' `
                --data-binary "@$RequestPath" `
                --output $ResponsePath `
                --write-out '%{http_code}' `
                $Url
            [pscustomobject] @{ Status = [int] $status; ResponsePath = $ResponsePath }
        } -ArgumentList $CertificatePath, $requestFile, $responseFile, "$root/api/v1/activation/confirm"))
    }

    $results = @($jobs | Wait-Job | Receive-Job)
    $jobs | Remove-Job -Force
    $statuses = @($results | ForEach-Object { $_.Status })
    if (@($statuses | Where-Object { $_ -eq 200 }).Count -ne 1 -or
        @($statuses | Where-Object { $_ -eq 409 }).Count -ne 1) {
        throw "Expected one HTTP 200 and one HTTP 409, received: $($statuses -join ', ')."
    }

    $success = $results | Where-Object { $_.Status -eq 200 } | Select-Object -First 1
    $confirmation = [IO.File]::ReadAllText($success.ResponsePath) | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($confirmation.virtualKey)) {
        throw 'Successful activation response did not contain a virtual key.'
    }

    $modelsStatus = & curl.exe `
        --silent `
        --show-error `
        --cacert $CertificatePath `
        --header "Authorization: Bearer $($confirmation.virtualKey)" `
        --output NUL `
        --write-out '%{http_code}' `
        "$root/v1/models"
    if ($modelsStatus -ne '200') {
        throw "Issued key model check failed with HTTP $modelsStatus."
    }

    [pscustomobject] @{
        EmployeeNumber = $confirmation.employee.employeeNumber
        ConcurrentStatuses = $statuses -join ','
        ModelsStatus = [int] $modelsStatus
    }
}
finally {
    $jobs | Where-Object { $_.State -eq 'Running' } | Stop-Job -ErrorAction SilentlyContinue
    $jobs | Remove-Job -Force -ErrorAction SilentlyContinue
    foreach ($path in $temporaryFiles) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
}
