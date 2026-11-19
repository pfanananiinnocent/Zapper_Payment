
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
        customerId   = $CustomerId
        subMerchantId = $SubMerchantId
        amount       = $Amount
        currencyCode = $CurrencyCode
        appPaymentId = $AppPaymentId
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

    try {

        $Response = Invoke-WebRequest `
            -Uri $Endpoint `
            -Method Post `
            -ContentType "application/json" `
            -Body $RequestBody `
            -SkipCertificateCheck `
            -SkipHttpErrorCheck `
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

                if ($ResponseBody.status) {
                    $PaymentStatus = $ResponseBody.status
                }
                else {
                    $PaymentStatus = "UnexpectedResponse"
                }

            }
            catch {
                $ErrorMessage = "Response could not be parsed as JSON."
            }
        }

    }
    catch {

        $PaymentStatus = "ClientError"
        $ErrorMessage = $_.Exception.Message

    }
    finally {
        $Stopwatch.Stop()
    }

    [PSCustomObject]@{
        RunId                = $RunId
        RequestNumber        = $RequestNumber
        AppPaymentId         = $AppPaymentId
        PaymentId            = $PaymentId
        TransactionId        = $TransactionId
        OrderNumber          = $OrderNumber
        HttpStatusCode       = $HttpStatusCode
        PaymentStatus        = $PaymentStatus
        AcmeResponseCode     = $AcmeResponseCode
        Amount               = $Amount
        CurrencyCode         = $CurrencyCode
        DurationMilliseconds = $Stopwatch.ElapsedMilliseconds
        StartedAtUtc         = $StartedAtUtc.ToString("o")
        CompletedAtUtc       = [DateTime]::UtcNow.ToString("o")
        ErrorMessage         = $ErrorMessage
    }

} -ThrottleLimit $Concurrency

$TestStopwatch.Stop()

$Results = @($Results | Sort-Object RequestNumber)

$SortedDurations = @(
    $Results.DurationMilliseconds | Sort-Object
)

$DurationStats = $SortedDurations |
    Measure-Object -Average -Minimum -Maximum

$P95Index = [Math]::Max(
    0,
    [int][Math]::Ceiling(
        $SortedDurations.Count * 0.95
    ) - 1
)

$TotalSeconds = [Math]::Max(
    0.001,
    $TestStopwatch.Elapsed.TotalSeconds
)

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

$Summary = [PSCustomObject]@{
    RunId = $RunId

    Mode = $Mode

    RequestedConcurrency = $Concurrency

    TotalRequests = $TotalRequests

    CompletedResults = $Results.Count

    Http200 = @(
        $Results | Where-Object HttpStatusCode -eq 200
    ).Count

    Http202 = @(
        $Results | Where-Object HttpStatusCode -eq 202
    ).Count

    Http503 = @(
        $Results | Where-Object HttpStatusCode -eq 503
    ).Count

    Approved = @(
        $Results | Where-Object PaymentStatus -eq "Approved"
    ).Count

    Declined = @(
        $Results | Where-Object PaymentStatus -eq "Declined"
    ).Count

    PendingReconciliation = @(
        $Results |
            Where-Object PaymentStatus -eq "PendingReconciliation"
    ).Count

    NotDispatched = @(
        $Results | Where-Object PaymentStatus -eq "NotDispatched"
    ).Count

    ClientErrors = @(
        $Results | Where-Object PaymentStatus -eq "ClientError"
    ).Count

    UnexpectedResults = @(
        $Results | Where-Object {
            $_.PaymentStatus -notin @(
                "Approved",
                "Declined",
                "PendingReconciliation",
                "NotDispatched",
                "ClientError"
            )
        }
    ).Count

    UniquePaymentIds = $UniquePaymentIds

    UniqueTransactionIds = $UniqueTransactionIds

    MinimumLatencyMs = $DurationStats.Minimum

    AverageLatencyMs = [Math]::Round(
        $DurationStats.Average, 2
    )

    P95LatencyMs = $SortedDurations[$P95Index]

    MaximumLatencyMs = $DurationStats.Maximum

    TotalDurationSeconds = [Math]::Round(
        $TotalSeconds, 3
    )

    ApiRequestsPerSecond = [Math]::Round(
        $Results.Count / $TotalSeconds, 2
    )
}

$CsvPath = Join-Path `
    $ResultsDirectory "$RunId-details.csv"

$SummaryPath = Join-Path `
    $ResultsDirectory "$RunId-summary.json"

$Results | Export-Csv `
    -Path $CsvPath `
    -NoTypeInformation `
    -Encoding UTF8

$Summary |
    ConvertTo-Json -Depth 5 |
    Set-Content -Path $SummaryPath -Encoding UTF8

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

Write-Host ""
Write-Host "Detailed results : $CsvPath"
Write-Host "Summary results  : $SummaryPath"
Write-Host "Run ID           : $RunId"
Write-Host "============================================"
