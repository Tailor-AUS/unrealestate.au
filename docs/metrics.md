# Growth metrics

North star: **rolling 30-day monthly active users (MAU)** toward 100,000.

An active user is a unique authenticated user who completed at least one
meaningful product action in the last 30 days. Anonymous traffic is an
acquisition metric, not MAU.

## Event vocabulary

`ProductEvents` stores only:

- opaque `UserId`
- stable event `Name`
- optional opaque `ListingId`
- UTC `OccurredAt`

It never stores names, email addresses, search text, enquiry messages, property
addresses, or other PII.

Meaningful events:

- `search.performed`
- `listing.viewed`
- `listing.created`
- `listing.updated`
- `ai_chat.started`
- `enquiry.submitted`
- `inspection.booked`
- `offer.submitted`
- `agent_proposal.submitted`

## Canonical PostgreSQL query

```sql
SELECT
  COUNT(DISTINCT "UserId") FILTER (
    WHERE "OccurredAt" >= NOW() - INTERVAL '30 days'
  ) AS mau_30d,
  COUNT(DISTINCT "UserId") FILTER (
    WHERE "OccurredAt" >= NOW() - INTERVAL '7 days'
  ) AS wau_7d
FROM "ProductEvents";
```

Event mix and data freshness:

```sql
SELECT
  "Name",
  COUNT(*) AS event_count,
  COUNT(DISTINCT "UserId") AS active_users,
  MAX("OccurredAt") AS latest_event_at
FROM "ProductEvents"
WHERE "OccurredAt" >= NOW() - INTERVAL '30 days'
GROUP BY "Name"
ORDER BY event_count DESC;
```

Application code exposes the same calculation through
`IProductEventRecorder.GetGrowthMetricsAsync()`. Wire that aggregate to an
operator-only dashboard when the AloomU observability surface is available; do
not expose user-level event rows publicly.

## Listing activity

Public listing pages show only real data:

- distinct authenticated viewers in the last 24 hours (`listing.viewed`)
- persisted buyer questions and inspection requests in the last 24 hours
- persisted offers total
- relative time of the latest enquiry

The previous fabricated activity counters were removed.
