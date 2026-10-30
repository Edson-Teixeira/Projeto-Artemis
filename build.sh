#!/usr/bin/env bash
set -e

echo "=================================================="
echo "          PROJETO ARTEMIS - BUILD SYSTEM          "
echo "=================================================="

# 1. Detecção automática do diretório do KSP
SECONDARY_DRIVE="/mnt/21c32c7a-ee6b-4913-8493-f14c9c20f55c/SteamLibrary/steamapps/common/Kerbal Space Program"
DEFAULT_KSP_PATH="$HOME/.steam/steam/steamapps/common/Kerbal Space Program"
ALT_KSP_PATH="$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"

if [ -z "$KSP_DIR" ]; then
    if [ -d "$SECONDARY_DRIVE/KSP_x64_Data" ]; then
        export KSP_DIR="$SECONDARY_DRIVE"
    elif [ -d "$DEFAULT_KSP_PATH/KSP_x64_Data" ]; then
        export KSP_DIR="$DEFAULT_KSP_PATH"
    elif [ -d "$ALT_KSP_PATH/KSP_x64_Data" ]; then
        export KSP_DIR="$ALT_KSP_PATH"
    fi
fi

if [ -z "$KSP_DIR" ] || [ ! -d "$KSP_DIR" ]; then
    echo "[-] ERRO: Instalação do KSP não encontrada automaticamente."
    echo "    Defina KSP_DIR manualmente, ex: export KSP_DIR=\"...\""
    exit 1
fi

echo "[+] Diretório do KSP detectado: $KSP_DIR"

# 2. Verificação do MechJeb2
MECHJEB_DLL="$KSP_DIR/GameData/MechJeb2/Plugins/MechJeb2.dll"
if [ ! -f "$MECHJEB_DLL" ]; then
    echo "[-] ERRO: MechJeb2.dll não encontrada em $MECHJEB_DLL."
    echo "    Certifique-se de que o MechJeb 2 está instalado no KSP."
    exit 1
fi
echo "[+] MechJeb2 detectado com sucesso."

# 3. Compilação do Artemis
echo "[+] Compilando Artemis via .NET SDK..."
dotnet build Artemis.csproj -c Release

# 4. Instalação no GameData do KSP
DEST_DIR="$KSP_DIR/GameData/Artemis"
mkdir -p "$DEST_DIR/Plugins"

echo "[+] Instalando no jogo em: $DEST_DIR..."
cp -r GameData/Artemis/* "$DEST_DIR/"

echo "=================================================="
echo " [OK] SUCESSO! Artemis compilado e instalado."
echo " Inicie o KSP e aperte [F8] durante o voo."
echo "=================================================="
