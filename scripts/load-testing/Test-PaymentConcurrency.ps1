
#Requires -Version 7.0

param(
    [string]$BaseUrl = "https://localhost:7052",

    [ValidateRange(1, 1000)]
    [int]$TotalRequests = 10,

    [ValidateRange(1, 100)]
    [int]$Concurrency = 10,

    [string]$CustomerId = "CUST-1001",

    [string]$SubMerchantId = "ZAP0004182",

    [long]$Amount = 1500,

    [string]$CurrencyCode = "ZAR",

    [ValidateSet("Unique", "Duplicate")]
    [string]$Mode = "Unique",

    [ValidateRange(10, 300)]
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"

# Restrict this utility to local assessment testing.
$baseUri = [Uri]$BaseUrl

if (-not $baseUri.IsLoopback) {
    throw "This load-testing script may only target localhost."
}

if ($baseUri.Scheme -notin @("http", "https")) {
    throw "Only HTTP and HTTPS endpoints are supported."
}

$Endpoint = "$($BaseUrl.TrimEnd('/'))/api/payments"

$RunId = "LOAD-" +
    (Get-Date -Format "yyyyMMdd-HHmmss") +
    "-" +
    ([Guid]::NewGuid().ToString("N").Substring(0, 8))

$ResultsDirectory = Join-Path $PSScriptRoot "results"

New-Item `
    -ItemType Directory `
    -Force `
    -Path $ResultsDirectory | Out-Null

Write-Host ""
Write-Host "============================================"
Write-Host " ZAPPER PAYMENTS - CONCURRENT LOAD TEST"
Write-Host "============================================"
Write-Host "Run ID         : $RunId"
Write-Host "Endpoint       : $Endpoint"
Write-Host "Total Requests : $TotalRequests"
Write-Host "Concurrency    : $Concurrency"
Write-Host "Mode           : $Mode"
Write-Host "Customer       : $CustomerId"
Write-Host "Amount         : $Amount minor units"
Write-Host "============================================"
Write-Host ""

$TestStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

$Results = 1..$TotalRequests | ForEach-Object -Parallel {

    $RequestNumber = $_

    $Endpoint = $using:Endpoint
    $RunId = $using:RunId
    $CustomerId = $using:CustomerId
    $SubMerchantId = $using:SubMerchantId
    $Amount = $using:Amount
    $CurrencyCode = $using:CurrencyCode
    $Mode = $using:Mode
    $TimeoutSeconds = $using:TimeoutSeconds

    if ($Mode -eq "Duplicate") {
        $AppPaymentId = "$RunId-DUPLICATE"
    }
    else {
        $AppPaymentId = "{0}-{1:D4}" -f `
            $RunId, $RequestNumber
    }

    $RequestBody = @{
        customerId    = $CustomerId
        subMerchantId = $SubMerchantId
        amount        = $Amount
        currencyCode  = $CurrencyCode
        appPaymentId  = $AppPaymentId
    } | ConvertTo-Json -Compress

    $StartedAtUtc = [DateTime]::UtcNow

    $Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    $HttpStatusCode = $null
    $PaymentId = $null
    $TransactionId = $null
    $OrderNumber = $null
    $PaymentStatus = "Unknown"
    $AcmeResponseCode = $null
    $ErrorMessage = $null

    $ResponseAppPaymentId = $null
    $ResponseAmount = $null
    $ResponseCurrencyCode = $null

    try {

        $Response = Invoke-WebRequest `
            -Uri $Endpoint `
            -Method Post `
            -ContentType "application/json" `
            -Body $RequestBody `
            -SkipCertificateCheck `
            -SkipHttpErrorCheck `
            -MaximumRedirection 0 `
            -TimeoutSec $TimeoutSeconds `
            -ErrorAction Stop

        $HttpStatusCode = [int]$Response.StatusCode

        if ($Response.Content) {

            try {

                $ResponseBody = $Response.Content |
                    ConvertFrom-Json -ErrorAction Stop

                $PaymentId = $ResponseBody.paymentId
                $TransactionId = $ResponseBody.transactionId
                $OrderNumber = $ResponseBody.orderNumber
                $AcmeResponseCode = $ResponseBody.acmeResponseCode

                $ResponseAppPaymentId = $ResponseBody.appPaymentId
                $ResponseAmount = $ResponseBody.amount
                $ResponseCurrencyCode = $ResponseBody.currencyCode

                if ($ResponseBody.status -is [string]) {
                    $PaymentStatus = $ResponseBody.status
                }
                else {
                    $PaymentStatus = "UnexpectedResponse"
                }

                if ($HttpStatusCode -ge 400) {
                    if ($ResponseBody.detail) {
                        $ErrorMessage = $ResponseBody.detail
                    }
                    elseif ($ResponseBody.title) {
                        $ErrorMessage = $ResponseBody.title
                    }
                }

            }
            catch {
                $PaymentStatus = "UnexpectedResponse"
                $ErrorMessage = "Response could not be parsed as JSON."
            }
        }
        else {
            $PaymentStatus = "UnexpectedResponse"
            $ErrorMessage = "API returned an empty response."
        }

    }
    catch {

        $PaymentStatus = "ClientError"
        $ErrorMessage = $_.Exception.Message

    }
    finally {
        $Stopwatch.Stop()
    }

    # --------------------------------------------
    # Mode-specific response classification
    # --------------------------------------------

    $ValidationErrors = @()

    $IsExpectedResponse = $false

    if ($Mode -eq "Duplicate") {

        # Duplicate submissions may return Pending
        # while the original payment is still processing.

        $IsExpectedResponse = (
            (
                $HttpStatusCode -eq 200 -and
                $PaymentStatus -eq "Approved"
            ) -or
            (
                $HttpStatusCode -eq 202 -and
                $PaymentStatus -eq "Pending"
            )
        )

    }
    else {

        # Preserve supported payment outcomes
        # for normal and resilience load testing.

        $IsExpectedResponse = (
            (
                $HttpStatusCode -eq 200 -and
                $PaymentStatus -in @(
                    "Approved",
                    "Declined"
                )
            ) -or
            (
                $HttpStatusCode -eq 202 -and
                $PaymentStatus -eq "PendingReconciliation"
            ) -or
            (
                $HttpStatusCode -eq 503 -and
                $PaymentStatus -eq "NotDispatched"
            )
        )
    }

    if (-not $IsExpectedResponse) {

        $ValidationErrors += (
            "Unexpected HTTP status/payment status combination: " +
            "HTTP $HttpStatusCode, Status $PaymentStatus"
        )
    }

    # --------------------------------------------
    # Payment identity and payload validation
    # --------------------------------------------

    if ($IsExpectedResponse) {

        if (
            $null -eq $PaymentId -or
            "$PaymentId" -notmatch '^[1-9][0-9]*$'
        ) {
            $ValidationErrors += "Missing or invalid PaymentId."
        }

        $ParsedTransactionId = [Guid]::Empty

        if (
            -not [Guid]::TryParse(
                [string]$TransactionId,
                [ref]$ParsedTransactionId
            ) -or
            $ParsedTransactionId -eq [Guid]::Empty
        ) {
            $ValidationErrors += "Missing or invalid TransactionId."
        }
        else {
            # Normalize GUID formatting for identity comparison.
            $TransactionId = $ParsedTransactionId.ToString()
        }

        if ([string]::IsNullOrWhiteSpace($OrderNumber)) {
            $ValidationErrors += "Missing OrderNumber."
        }

        if ($ResponseAppPaymentId -cne $AppPaymentId) {
            $ValidationErrors += "AppPaymentId mismatch."
        }

        if (
            $null -eq $ResponseAmount -or
            $ResponseAmount -ne $Amount
        ) {
            $ValidationErrors += "Payment amount mismatch."
        }

        if ($ResponseCurrencyCode -ne $CurrencyCode) {
            $ValidationErrors += "Currency code mismatch."
        }

        if (
            $PaymentStatus -eq "Approved" -and
            $AcmeResponseCode -cne "00"
        ) {
            $ValidationErrors += "Approved response has an invalid Acme response code."
        }
    }

    if ($ErrorMessage -and -not $IsExpectedResponse) {
        $ValidationErrors += $ErrorMessage
    }

    $IsValidResponse = ($ValidationErrors.Count -eq 0)

    [PSCustomObject]@{
        RunId                = $RunId
        RequestNumber        = $RequestNumber
        AppPaymentId         = $AppPaymentId
        ResponseAppPaymentId = $ResponseAppPaymentId
        PaymentId            = $PaymentId
        TransactionId        = $TransactionId
        OrderNumber          = $OrderNumber
        HttpStatusCode       = $HttpStatusCode
        PaymentStatus        = $PaymentStatus
        AcmeResponseCode     = $AcmeResponseCode
        Amount               = $Amount
        ResponseAmount       = $ResponseAmount
        CurrencyCode         = $CurrencyCode
        ResponseCurrencyCode = $ResponseCurrencyCode
        IsExpectedResponse   = $IsExpectedResponse
        IsValidResponse      = $IsValidResponse
        ValidationErrors     = ($ValidationErrors -join "; ")
        DurationMilliseconds = $Stopwatch.ElapsedMilliseconds
        StartedAtUtc         = $StartedAtUtc.ToString("o")
        CompletedAtUtc       = [DateTime]::UtcNow.ToString("o")
        ErrorMessage         = $ErrorMessage
    }

} -ThrottleLimit $Concurrency

$TestStopwatch.Stop()

$Results = @($Results | Sort-Object RequestNumber)

# --------------------------------------------
# Latency calculations
# --------------------------------------------

$SortedDurations = @(
    $Results |
        Select-Object -ExpandProperty DurationMilliseconds |
        Sort-Object
)

$DurationStats = $SortedDurations |
    Measure-Object -Average -Minimum -Maximum

$P95LatencyMs = $null
$AverageLatencyMs = $null

if ($SortedDurations.Count -gt 0) {

    $P95Index = [Math]::Max(
        0,
        [int][Math]::Ceiling(
            $SortedDurations.Count * 0.95
        ) - 1
    )

    $P95LatencyMs = $SortedDurations[$P95Index]

    $AverageLatencyMs = [Math]::Round(
        $DurationStats.Average, 2
    )
}

$TotalSeconds = [Math]::Max(
    0.001,
    $TestStopwatch.Elapsed.TotalSeconds
)

# --------------------------------------------
# Response counters
# --------------------------------------------

$Http200Count = @(
    $Results | Where-Object HttpStatusCode -eq 200
).Count

$Http202Count = @(
    $Results | Where-Object HttpStatusCode -eq 202
).Count

$Http503Count = @(
    $Results | Where-Object HttpStatusCode -eq 503
).Count

$ApprovedCount = @(
    $Results | Where-Object PaymentStatus -eq "Approved"
).Count

$DeclinedCount = @(
    $Results | Where-Object PaymentStatus -eq "Declined"
).Count

$PendingCount = @(
    $Results | Where-Object PaymentStatus -eq "Pending"
).Count

$PendingReconciliationCount = @(
    $Results |
        Where-Object PaymentStatus -eq "PendingReconciliation"
).Count

$NotDispatchedCount = @(
    $Results | Where-Object PaymentStatus -eq "NotDispatched"
).Count

$ClientErrorCount = @(
    $Results | Where-Object PaymentStatus -eq "ClientError"
).Count

$UnexpectedResultCount = @(
    $Results | Where-Object {
        -not $_.IsExpectedResponse -and
        $_.PaymentStatus -ne "ClientError"
    }
).Count

$InvalidResponseCount = @(
    $Results | Where-Object {
        -not $_.IsValidResponse
    }
).Count

$ValidResponseCount = @(
    $Results | Where-Object {
        $_.IsValidResponse
    }
).Count

# --------------------------------------------
# Identity consistency validation
# --------------------------------------------

$UniqueAppPaymentIds = @(
    $Results |
        Where-Object { $_.AppPaymentId } |
        Select-Object -ExpandProperty AppPaymentId -Unique
).Count

$UniquePaymentIds = @(
    $Results |
        Where-Object { $null -ne $_.PaymentId } |
        Select-Object -ExpandProperty PaymentId -Unique
).Count

$UniqueTransactionIds = @(
    $Results |
        Where-Object { $_.TransactionId } |
        Select-Object -ExpandProperty TransactionId -Unique
).Count

$UniqueOrderNumbers = @(
    $Results |
        Where-Object { $_.OrderNumber } |
        Select-Object -ExpandProperty OrderNumber -Unique
).Count

if ($Mode -eq "Duplicate") {
    $ExpectedUniqueIdentities = 1
}
else {
    $ExpectedUniqueIdentities = $TotalRequests
}

$IdentityValidationPassed = (
    $ValidResponseCount -eq $TotalRequests -and
    $UniqueAppPaymentIds -eq $ExpectedUniqueIdentities -and
    $UniquePaymentIds -eq $ExpectedUniqueIdentities -and
    $UniqueTransactionIds -eq $ExpectedUniqueIdentities -and
    $UniqueOrderNumbers -eq $ExpectedUniqueIdentities
)

# --------------------------------------------
# Overall test acceptance validation
# --------------------------------------------

$TestValidationErrors = @()

if ($Results.Count -ne $TotalRequests) {

    $TestValidationErrors += (
        "Expected $TotalRequests completed results, " +
        "but received $($Results.Count)."
    )
}

if ($ClientErrorCount -gt 0) {
    $TestValidationErrors += "$ClientErrorCount client errors detected."
}

if ($UnexpectedResultCount -gt 0) {
    $TestValidationErrors += "$UnexpectedResultCount unexpected responses detected."
}

if ($InvalidResponseCount -gt 0) {
    $TestValidationErrors += "$InvalidResponseCount invalid responses detected."
}

if (-not $IdentityValidationPassed) {
    $TestValidationErrors += "Payment identity consistency validation failed."
}

if ($Mode -eq "Duplicate") {

    if ($ApprovedCount -lt 1) {
        $TestValidationErrors += "No approved payment response was observed."
    }

    if (($ApprovedCount + $PendingCount) -ne $TotalRequests) {

        $TestValidationErrors += (
            "Duplicate mode requires all responses to be " +
            "either Approved or Pending."
        )
    }
}

$TestPassed = ($TestValidationErrors.Count -eq 0)

if ($TestPassed) {
    $ValidationResult = "PASSED"
}
else {
    $ValidationResult = "FAILED"
}

# --------------------------------------------
# Summary output
# --------------------------------------------

$Summary = [PSCustomObject]@{

    RunId = $RunId

    Mode = $Mode

    RequestedConcurrency = $Concurrency

    TotalRequests = $TotalRequests

    CompletedResults = $Results.Count

    Http200 = $Http200Count

    Http202 = $Http202Count

    Http503 = $Http503Count

    Approved = $ApprovedCount

    Declined = $DeclinedCount

    Pending = $PendingCount

    PendingReconciliation = $PendingReconciliationCount

    NotDispatched = $NotDispatchedCount

    ClientErrors = $ClientErrorCount

    UnexpectedResults = $UnexpectedResultCount

    ValidResponses = $ValidResponseCount

    InvalidResponses = $InvalidResponseCount

    ExpectedUniqueIdentities = $ExpectedUniqueIdentities

    UniqueAppPaymentIds = $UniqueAppPaymentIds

    UniquePaymentIds = $UniquePaymentIds

    UniqueTransactionIds = $UniqueTransactionIds

    UniqueOrderNumbers = $UniqueOrderNumbers

    IdentityValidationPassed = $IdentityValidationPassed

    MinimumLatencyMs = $DurationStats.Minimum

    AverageLatencyMs = $AverageLatencyMs

    P95LatencyMs = $P95LatencyMs

    MaximumLatencyMs = $DurationStats.Maximum

    TotalDurationSeconds = [Math]::Round(
        $TotalSeconds, 3
    )

    ApiRequestsPerSecond = [Math]::Round(
        $Results.Count / $TotalSeconds, 2
    )

    ValidationResult = $ValidationResult

    ValidationErrors = @($TestValidationErrors)
}

# --------------------------------------------
# Export test evidence
# --------------------------------------------

$CsvPath = Join-Path `
    $ResultsDirectory "$RunId-details.csv"

$SummaryPath = Join-Path `
    $ResultsDirectory "$RunId-summary.json"

$Results | Export-Csv `
    -Path $CsvPath `
    -Encoding UTF8

$Summary |
    ConvertTo-Json -Depth 5 |
    Set-Content -Path $SummaryPath -Encoding UTF8

# --------------------------------------------
# Console reporting
# --------------------------------------------

Write-Host ""
Write-Host "============================================"
Write-Host " LOAD TEST SUMMARY"
Write-Host "============================================"

$Summary | Format-List

Write-Host ""
Write-Host "HTTP Status Distribution"

$Results |
    Group-Object HttpStatusCode |
    Select-Object Name, Count |
    Format-Table -AutoSize

Write-Host "Payment Status Distribution"

$Results |
    Group-Object PaymentStatus |
    Select-Object Name, Count |
    Format-Table -AutoSize

if ($InvalidResponseCount -gt 0) {

    Write-Host ""
    Write-Host "INVALID RESPONSES" -ForegroundColor Red

    $Results |
        Where-Object { -not $_.IsValidResponse } |
        Select-Object `
            RequestNumber,
            HttpStatusCode,
            PaymentStatus,
            ValidationErrors |
        Format-Table -AutoSize -Wrap
}

Write-Host ""
Write-Host "============================================"

if ($TestPassed) {

    Write-Host `
        " FINAL VALIDATION RESULT: PASSED" `
        -ForegroundColor Green

}
else {

    Write-Host `
        " FINAL VALIDATION RESULT: FAILED" `
        -ForegroundColor Red

    foreach ($ValidationError in $TestValidationErrors) {

        Write-Host `
            " - $ValidationError" `
            -ForegroundColor Red
    }
}

Write-Host "============================================"
Write-Host ""
Write-Host "Detailed results : $CsvPath"
Write-Host "Summary results  : $SummaryPath"
Write-Host "Run ID           : $RunId"
Write-Host "============================================"
