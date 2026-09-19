FROM clamav/clamav:1.4

# clamd normally keeps both the old and new signature engines resident while it
# reloads. On a developer workstation it is preferable to pause new scans during
# the reload and avoid the temporary ~2x memory spike. Two scan workers are ample
# for local uploads and reduce per-request CPU/RAM concurrency.
RUN sed -i -E 's/^#?ConcurrentDatabaseReload[[:space:]].*/ConcurrentDatabaseReload no/' /etc/clamav/clamd.conf && \
    sed -i -E 's/^#?MaxThreads[[:space:]].*/MaxThreads 2/' /etc/clamav/clamd.conf && \
    grep -q '^ConcurrentDatabaseReload no$' /etc/clamav/clamd.conf && \
    grep -q '^MaxThreads 2$' /etc/clamav/clamd.conf
