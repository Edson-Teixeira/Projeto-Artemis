#!/usr/bin/env bash
set -e

echo "============================================="
echo "   ORÁCULO GUIDANCE - Build Script (Linux)   "
echo "============================================="

# Detecta automaticamente o diretório do KSP na Steam do Linux
DEFAULT_KSP_PATH="$HOME/.steam/steam/steamapps/common/Kerbal Space Program"
ALT_KSP_PATH="$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"

if [ -z "$KSP_DIR" ]; then
    if [ -d "$DEFAULT_KSP_PATH" ]; then
        export KSP_DIR="$DEFAULT_KSP_PATH"
    elif [ -d "$ALT_KSP_PATH" ]; then
        export KSP_DIR="$ALT_KSP_PATH"
    fi
fi

if [ -z "$KSP_DIR" ] || [ ! -d "$KSP_DIR" ]; then
    echo "[-] AVISO: Diretório do KSP não encontrado automaticamente."
    echo "    Defina a variável KSP_DIR antes de rodar, exemplo:"
    echo "    export KSP_DIR=\"$HOME/.steam/steam/steamapps/common/Kerbal Space Program\""
    exit 1
fi

echo "[+] Diretório KSP detectado: $KSP_DIR"

# Verifica se o MechJeb2 está instalado pelo CKAN
MECHJEB_DLL="$KSP_DIR/GameData/MechJeb2/Plugins/MechJeb2.dll"
if [ ! -f "$MECHJEB_DLL" ]; then
    echo "[-] ERRO: MechJeb2.dll não encontrada em $MECHJEB_DLL."
    echo "    Por favor, instale o mod 'MechJeb 2' pelo CKAN primeiro!"
    exit 1
fi

echo "[+] MechJeb2 encontrado via CKAN."
echo "[+] Compilando OraculoGuidance..."

# Compilação usando o .NET SDK
dotnet build OraculoGuidance.csproj -c Release

# Diretório de destino no jogo
DEST_DIR="$KSP_DIR/GameData/OraculoGuidance"
mkdir -p "$DEST_DIR/Plugins"

echo "[+] Copiando arquivos para $DEST_DIR..."
cp -r GameData/OraculoGuidance/* "$DEST_DIR/"

echo "============================================="
echo " [OK] Mod instalado no KSP com sucesso!"
echo " Inicie o jogo pela Steam e aperte F8 em voo."
echo "============================================="
