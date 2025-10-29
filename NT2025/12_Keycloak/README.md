## Infra Steps

### .NET
```
dotnet new globaljson --sdk-version 9.0.304

dotnet new aspire-apphost -n AppHost -o infra\aspire\AppHost -f net9.0
dotnet new aspire-servicedefaults -n ServiceDefaults -o infra\aspire\ServiceDefaults -f net9.0
```

### HSM
```
docker build -f infra/hsm/Dockerfile.utimaco-sim-debian -t registry.local/utimaco/sim:6.0-debian infra/hsm
```

### Keycloak SPIs
```
// run docker
cd .\infra\keycloak\providers\keycloak-hsm\
wsl
bash ./build.sh

```

## Backend Steps
```
dotnet new webapi -n Platform -o .\services\backend\Platform -f net9.0 --use-controllers
dotnet new webapi -n Portal -o .\services\backend\Portal -f net9.0 --use-controllers

dotnet add .\services\backend\Platform\Platform.csproj reference .\infra\aspire\ServiceDefaults\ServiceDefaults.csproj
dotnet add .\services\backend\Portal\Portal.csproj reference .\infra\aspire\ServiceDefaults\ServiceDefaults.csproj

dotnet add .\infra\aspire\AppHost\AppHost.csproj reference .\services\backend\Platform\Platform.csproj
dotnet add .\infra\aspire\AppHost\AppHost.csproj reference .\services\backend\Portal\Portal.csproj

mkdir -p services/backend/_shared/Common.Auth
dotnet new classlib -n Common.Auth -f net9.0 -o services/backend/_shared/Common.Auth

dotnet add services/backend/Platform/Platform.csproj reference services/backend/_shared/Common.Auth/Common.Auth.csproj
dotnet add services/backend/Portal/Portal.csproj   reference services/backend/_shared/Common.Auth/Common.Auth.csproj
```

## Aspire

### RUN_MODE
- `RUN_MODE=infra-only`
- `RUN_MODE=platform:be`
- `RUN_MODE=platform:be+fe`
- `RUN_MODE=platform:be,portal:be+fe`
- `RUN_MODE=platform:be+fe,portal:be+fe`
- ...