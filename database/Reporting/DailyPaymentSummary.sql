DECLARE @ReportDate DATE = CAST(GETDATE() AS DATE);

SELECT
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
    END) AS RefundTotal

FROM dbo.PaymentEvents pe
INNER JOIN dbo.Payments p
    ON p.Id = pe.PaymentId
INNER JOIN dbo.SubMerchants sm
    ON sm.Id = p.SubMerchantId

WHERE CAST(pe.OccurredAtUtc AS DATE) = @ReportDate

GROUP BY
    sm.ZapperMerchantId,
    sm.TradingName,
    p.CurrencyCode

ORDER BY
    sm.TradingName,
    p.CurrencyCode;