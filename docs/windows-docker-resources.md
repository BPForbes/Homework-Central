# Docker resources on this Windows 11 workstation

This workstation has 7.8 GiB of physical RAM and 8 logical processors. Docker
Desktop's WSL 2 backend is the appropriate lightweight Linux environment for the
project. It uses Microsoft's maintained Linux kernel in a utility VM; a custom
microkernel would add maintenance and compatibility risk without reducing the
application working sets inside the containers.

## Resource profiles

The two heavy services are opt-in because they cannot safely coexist on an 8 GiB
Windows host. The default `docker compose up` starts only the lightweight core.

| Service | Profile | CPU ceiling | RAM ceiling | Main control |
|---|---|---:|---:|---|
| PostgreSQL | default | 1.00 | 512 MiB | Smaller buffers, 50 connections, JIT disabled |
| FCaptcha | default | 0.25 | 96 MiB | Small Go service |
| Redis | default | 0.25 | 96 MiB | 48 MiB LRU cache, persistence disabled |
| ASP.NET API | default | 1.00 | 512 MiB | Container-aware .NET runtime limit |
| nginx frontend | default | 0.25 | 48 MiB | Static production build only |
| ClamAV | `antivirus` | 0.75 | 2,560 MiB | Non-concurrent signature reload, two scan workers |
| Ollama | `ai` | 1.50 | 1,536 MiB | Qwen3 0.6B, one loaded model/request |

The default core is capped at 1,264 MiB (1.23 GiB). Adding antivirus raises the
container ceilings to 3,824 MiB (3.73 GiB); adding local AI instead raises them
to 2,800 MiB (2.73 GiB). These are hard ceilings, not normal idle usage. CPU
limits are also ceilings rather than reservations.

The Windows development launchers also bound the two processes that run outside
Docker: Vite's V8 old-space heap defaults to 384 MiB, and the ASP.NET API's
managed GC heap defaults to 384 MiB. Set `NODE_OPTIONS` or
`DOTNET_GCHeapHardLimit` before launching to override either value.

Use one of these modes:

```powershell
# Core web application only
docker compose up -d

# Core plus attachment malware scanning
docker compose --profile antivirus up -d

# Core plus local AI
docker compose --profile ai up -d
```

Do not enable `antivirus` and `ai` together on this machine. The normal
`scripts/run-dev.ps1` workflow explicitly starts ClamAV plus PostgreSQL and
FCaptcha, while running the API and Vite directly on Windows; it does not start
Ollama. Compose permits explicitly starting a profiled service, so the launcher
continues to work without extra flags.

ClamAV dominates the antivirus profile because it keeps its signature engine in
RAM. The local image pauses new scans briefly during signature reload rather than
holding old and new engines at once. Ollama uses `qwen3:0.6b` (about 523 MB on
disk), disables thinking for classification, caps context at 2,048 tokens, and
unloads an idle model after two minutes. It swaps between the chat and embedding
models because only one may be loaded. These choices trade some latency for a
lower peak working set.

## Cap the WSL 2 utility VM

Compose limits do not include the Linux kernel, Docker daemon, filesystem cache,
or other WSL distributions. Copy the supplied configuration to the Windows user
profile to cap the entire WSL 2 VM at half of physical RAM:

```powershell
Copy-Item .\deploy\windows\.wslconfig.example $env:USERPROFILE\.wslconfig
wsl --shutdown
```

Restart Docker Desktop afterward. The cap is 4 GiB with four logical processors.
The 2 GiB disk-backed swap handles brief build or signature-update pressure, but
sustained swapping indicates that the active profile is too large. The settings
also apply to Ubuntu and Kali, so do not run those distributions concurrently
with a heavy Docker profile on this machine.

Microsoft documents `.wslconfig` and the `autoMemoryReclaim` behavior at
<https://learn.microsoft.com/windows/wsl/wsl-config>.

## Observe and override

Measure the warmed-up stack rather than assuming every service reaches its cap:

```powershell
docker stats --no-stream
```

Every limit can be overridden in the untracked `.env` file. The available keys
are listed in `.env.example`. For example:

```dotenv
POSTGRES_MEMORY_LIMIT=640m
POSTGRES_CPUS=1.25
```

Only raise a service limit if `docker stats` or an OOM-killed container shows it
is necessary. Keep the sum for an active profile below roughly 3.75 GiB so the
Docker daemon and WSL kernel fit under the 4 GiB VM cap.

Release resources while preserving data volumes with:

```powershell
docker compose down
```

If Windows still reports a large WSL process afterward, quit Docker Desktop and
run `wsl --shutdown`.
