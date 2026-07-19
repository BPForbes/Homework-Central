# System efficiency and service choices

This stack is tuned first for an 8 GB Windows 11 development host. The WSL2 cap
protects Windows; each container also has its own CPU and memory ceiling.

## Application hot paths

- Effective authorization masks are memoized only for the current request or hub
  invocation. This removes duplicate database calls without retaining user data in
  an unbounded process-wide cache.
- Custom room details use the atomically refreshed `CustomChannelStore` snapshot.
  Its room lookup is dictionary-backed instead of scanning the snapshot or querying
  PostgreSQL for each room open.
- Message history uses EF split queries for votes, attachments, and link previews.
  This prevents a cartesian result when a message has items in several collections.
- Multiple attachment UUIDs are loaded in one query instead of one query per file.
- Npgsql pools are capped at 20 connections for the master database and 4 per
  tenant database. Idle connections are pruned and a small automatic prepared-plan
  cache handles repeated EF query shapes.
- The browser retains at most 250 live messages for a room, preventing a tab left
  open all day from growing indefinitely.

Do not run parallel operations on the same EF `DbContext`. Parallelism is useful
only for independent work with separate contexts; database work in one request is
batched, projected, or cached instead.

## Shared memory

There are two deliberately bounded shared-memory layers:

1. The backend's in-process assessment channel is limited to 256 jobs and applies
   backpressure.
2. PostgreSQL receives a 192 MB `/dev/shm` allocation and uses 128 MB of
   `shared_buffers` inside its 512 MB container limit.

This is process-local acceleration, not a source of truth. PostgreSQL remains the
durable system of record, and Redis remains disposable.

## RabbitMQ decision

RabbitMQ is not part of the default 8 GB profile. The current deployment has one
backend worker, so a broker would duplicate the existing bounded work channel while
adding an Erlang VM, another connection pool, health checks, and several hundred MB
of working memory.

RabbitMQ becomes worthwhile when assessment workers run on multiple machines or
jobs must survive backend restarts. At that point use a PostgreSQL transactional
outbox: write the chat message and outbox row in one transaction, relay it to a
durable RabbitMQ queue with publisher confirms, and acknowledge only after the
consumer commits its PostgreSQL result. Adding RabbitMQ without that outbox creates
a database/broker dual-write failure window.

## ClamAV and non-Docker alternatives

An npm ClamAV package is a client wrapper. It still needs `clamd`, `clamdscan`, or
`clamscan` plus the signature database; it does not replace the memory-heavy scan
engine. The current C# `nClam` client already streams directly to `clamd` without a
Node intermediary.

ClamAV can instead be installed as a native Windows service. Set the backend's
`ClamAv:Host` to the Windows host address and port 3310. This removes the container
but does not materially reduce ClamAV's signature RAM, and it moves that RAM outside
the WSL safety cap. The antivirus Docker profile is therefore the safer default.

PostgreSQL, Redis, and the frontend web server can also be installed natively, but
doing so mostly moves their memory rather than eliminating it. Docker provides the
hard resource ceilings that protect the host.
