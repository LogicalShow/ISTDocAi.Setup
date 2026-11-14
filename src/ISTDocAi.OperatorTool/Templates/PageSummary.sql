SELECT c.Code,c.Name,c.QuotaMode,c.QuotaWarningPercent,
 COALESCE((SELECT SUM(Pages) FROM dbo.PageAllowanceGrants WHERE ConsumerId=@id),0) Granted,
 COALESCE((SELECT SUM(Pages) FROM dbo.CustomerPageCharges WHERE ConsumerId=@id),0) Used,
 COALESCE((SELECT SUM(Pages) FROM dbo.PageReservations WHERE ConsumerId=@id AND State='Reserved'),0) Reserved,
 b.AvailablePages Remaining,t.BalanceAfter CycleBase,w.WarningAtPages,
 CASE WHEN c.QuotaMode='Unlimited' THEN 'Unlimited' WHEN b.AvailablePages<=0 THEN 'Exhausted'
 WHEN b.AvailablePages<=w.WarningAtPages THEN 'Low' ELSE 'Ok' END Status
FROM dbo.Consumers c JOIN dbo.ConsumerPageBalance b ON b.ConsumerId=c.ConsumerId
OUTER APPLY(SELECT TOP(1) g.BalanceAfter FROM dbo.PageAllowanceGrants g WHERE g.ConsumerId=c.ConsumerId AND g.Kind='TopUp'
 AND NOT EXISTS (SELECT 1 FROM dbo.PageAllowanceGrants r WHERE r.Kind='Reversal' AND r.OriginalGrantId=g.GrantId)
 ORDER BY g.RecordedAtUtc DESC,g.GrantId DESC)t
OUTER APPLY(SELECT CASE WHEN t.BalanceAfter>0 AND FLOOR(CONVERT(decimal(38,2),t.BalanceAfter)*c.QuotaWarningPercent/100)<1 THEN CONVERT(bigint,1)
 ELSE CONVERT(bigint,FLOOR(CONVERT(decimal(38,2),t.BalanceAfter)*c.QuotaWarningPercent/100)) END WarningAtPages)w
WHERE c.ConsumerId=@id;