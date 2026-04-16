INSERT INTO dbo.Orders (
  CreatedAtUtc,
  Status,
  ExpectedShipDateUtc,
  ActualShipDateUtc,
  Carrier,
  DelayReason
)
VALUES
  ('2026-01-03T14:12:00', 'Delivered', '2026-01-05T17:00:00', '2026-01-05T15:44:00', 'UPS', NULL),
  ('2026-01-04T09:30:00', 'Delayed', '2026-01-06T17:00:00', '2026-01-08T11:20:00', 'FedEx', 'Weather hold at regional hub'),
  ('2026-01-05T18:05:00', 'Shipped', '2026-01-08T17:00:00', '2026-01-08T09:10:00', 'DHL', NULL),
  ('2026-01-07T11:42:00', 'Delayed', '2026-01-09T17:00:00', NULL, 'USPS', 'Address verification required'),
  ('2026-01-08T08:18:00', 'Processing', '2026-01-12T17:00:00', NULL, 'UPS', NULL),
  ('2026-01-09T16:27:00', 'Delayed', '2026-01-13T17:00:00', '2026-01-15T13:35:00', 'OnTrac', 'Inventory allocation delay'),
  ('2026-01-10T10:00:00', 'Delivered', '2026-01-14T17:00:00', '2026-01-14T16:02:00', 'FedEx', NULL),
  ('2026-01-11T13:55:00', 'Delayed', '2026-01-15T17:00:00', NULL, 'DHL', 'Customs documentation review'),
  ('2026-01-12T07:25:00', 'Shipped', '2026-01-16T17:00:00', '2026-01-16T12:48:00', 'UPS', NULL),
  ('2026-01-13T15:33:00', 'Processing', '2026-01-19T17:00:00', NULL, 'FedEx', NULL),
  ('2026-01-14T12:09:00', 'Delayed', '2026-01-20T17:00:00', NULL, 'USPS', 'Carrier pickup missed'),
  ('2026-01-15T17:46:00', 'Delivered', '2026-01-21T17:00:00', '2026-01-21T10:22:00', 'OnTrac', NULL);
