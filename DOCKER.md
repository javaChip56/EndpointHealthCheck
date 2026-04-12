# Docker Compose

The default compose file builds and runs the ASP.NET Core dashboard in a Linux .NET 8 container. The Windows compose file uses Windows .NET 8 container images for Docker engines switched to Windows containers.

## Configure The Port

The host port is controlled by `APIHEALTHDASHBOARD_PORT`.

PowerShell:

```powershell
$env:APIHEALTHDASHBOARD_PORT="9090"
docker compose up -d --build
```

Bash:

```bash
APIHEALTHDASHBOARD_PORT=9090 docker compose up -d --build
```

You can also copy `.env.example` to `.env` and edit `APIHEALTHDASHBOARD_PORT`.

## Run On Linux Containers

From the repository root:

```powershell
docker compose up -d --build
```

Open `http://localhost:8080`, or the port you set through `APIHEALTHDASHBOARD_PORT`.

Stop the app:

```powershell
docker compose down
```

Remove the persisted runtime-state volume as well:

```powershell
docker compose down -v
```

## Run On Windows Containers

Switch Docker to Windows containers first, then run:

```powershell
docker compose -f .\docker-compose.windows.yml up -d --build
```

Use the same `APIHEALTHDASHBOARD_PORT` variable to choose the host port:

```powershell
$env:APIHEALTHDASHBOARD_PORT="9090"
docker compose -f .\docker-compose.windows.yml up -d --build
```

The default Windows images target LTSC 2022. Override `APIHEALTHDASHBOARD_WINDOWS_SDK_IMAGE` and `APIHEALTHDASHBOARD_WINDOWS_RUNTIME_IMAGE` if your Docker host requires a different Windows container base.

## Runtime State

Compose stores runtime state in a named Docker volume:

- Linux containers: `/app/runtime-state`
- Windows containers: `C:\app\runtime-state`

The app continues to use `dashboard.yaml` and the `endpoints` folder copied into the image during publish. Rebuild the image after changing those files, or override `APIHEALTHDASHBOARD_BOOTSTRAP__DASHBOARDCONFIGPATH` and mount your own config path in a deployment-specific compose file.
