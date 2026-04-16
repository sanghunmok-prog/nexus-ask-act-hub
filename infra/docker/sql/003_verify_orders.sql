SELECT COUNT_BIG(*) AS TotalOrders
FROM dbo.Orders;

SELECT
  Status,
  COUNT_BIG(*) AS OrderCount
FROM dbo.Orders
GROUP BY Status
ORDER BY Status;

SELECT TOP (10)
  OrderId,
  CreatedAtUtc,
  Status,
  ExpectedShipDateUtc,
  ActualShipDateUtc,
  Carrier,
  DelayReason
FROM dbo.Orders
ORDER BY ExpectedShipDateUtc, OrderId;
