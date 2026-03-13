# CitySkylinesBridge Mod

Mod C# que expoe uma API HTTP na porta 8080 para controle externo do Cities: Skylines.

## Instalacao no Windows

### 1. Ajustar path do jogo

Abra `CitySkylinesBridge/CitySkylinesBridge.csproj` e ajuste o `CitiesManaged` pro seu path:

```xml
<CitiesManaged>C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed</CitiesManaged>
```

Se instalou em outro disco, ajuste (ex: `D:\SteamLibrary\steamapps\common\...`).

### 2. Compilar

```powershell
cd mod\CitySkylinesBridge
dotnet build -c Release
```

### 3. Instalar o mod

Copie a DLL pro Cities Skylines:

```powershell
$modDir = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\TwitchCityBridge"
mkdir $modDir -Force
copy bin\Release\CitySkylinesBridge.dll $modDir\
```

### 4. Ativar no jogo

1. Abra Cities: Skylines
2. Va em Content Manager > Mods
3. Ative "TwitchCity Bridge"
4. Carregue ou crie uma cidade

### 5. Liberar porta no firewall (para acesso remoto)

```powershell
netsh advfirewall firewall add rule name="TwitchCity Bridge" dir=in action=allow protocol=TCP localport=8080
```

### 6. Testar

```powershell
curl http://localhost:8080/api/ping
# Deve retornar: {"ok":true}
```

Do Mac (substituir pelo IP do Windows):
```bash
curl http://192.168.x.x:8080/api/ping
```
