
-- Daily Financial Summary
-- Reporting date: South African Standard Time
-- All financial amounts are expressed in minor units.

DECLARE @ReportDate DATE =
    CAST(DATEADD(HOUR, 2, SYSUTCDATETIME()) AS DATE);

-- For historical reporting, use a specific date:
-- DECLARE @ReportDate DATE = '2026-11-20';

DECLARE @StartUtc DATETIME2 =
    DATEADD(HOUR, -2, CAST(@ReportDate AS DATETIME2));

DECLARE @EndUtc DATETIME2 =
    DATEADD(DAY, 1, @StartUtc);

SELECT
    @ReportDate AS ReportDate,
    sm.ZapperMerchantId,
    sm.TradingName,
    p.CurrencyCode,

    SUM(CASE
        WHEN pe.EventType = 2 THEN 1
        ELSE 0
    END) AS ApprovedPaymentCount,

    SUM(CASE
        WHEN pe.EventType = 2 THEN COALESCE(pe.Amount, 0)
        ELSE 0
    END) AS ApprovedPaymentTotal,

    SUM(CASE
        WHEN pe.EventType = 3 THEN 1
        ELSE 0
    END) AS DeclinedPaymentCount,

    SUM(CASE
        WHEN pe.EventType = 3 THEN COALESCE(pe.Amount, 0)
        ELSE 0
    END) AS DeclinedPaymentTotal,

    SUM(CASE
        WHEN pe.EventType = 5 THEN 1
        ELSE 0
    END) AS ReversalCount,

    SUM(CASE
        WHEN pe.EventType = 5 THEN COALESCE(pe.Amount, 0)
        ELSE 0
    END) AS ReversalTotal,

    SUM(CASE
        WHEN pe.EventType = 6 THEN 1
        ELSE 0
    END) AS RefundCount,

    SUM(CASE
        WHEN pe.EventType = 6 THEN COALESCE(pe.Amount, 0)
        ELSE 0
    END) AS RefundTotal,

    SUM(CASE
        WHEN pe.EventType = 2
            THEN COALESCE(pe.Amount, 0)
        WHEN pe.EventType IN (5, 6)
            THEN -COALESCE(pe.Amount, 0)
        ELSE 0
    END) AS NetPaymentTotal

FROM dbo.PaymentEvents pe

INNER JOIN dbo.Payments p
    ON p.Id = pe.PaymentId

INNER JOIN dbo.SubMerchants sm
    ON sm.Id = p.SubMerchantId

WHERE pe.OccurredAtUtc >= @StartUtc
    AND pe.OccurredAtUtc < @EndUtc
    AND pe.EventType IN (2, 3, 5, 6)

GROUP BY
    sm.ZapperMerchantId,
    sm.TradingName,
    p.CurrencyCode

ORDER BY
    sm.TradingName,
    p.CurrencyCode;
