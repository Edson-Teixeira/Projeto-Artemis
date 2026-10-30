# 🚀 Projeto Artemis — Piloto Automático para Kerbal Space Program

[![KSP Version](https://img.shields.io/badge/KSP-1.12.x-orange.svg)](https://www.kerbalspaceprogram.com/)
[![Language](https://img.shields.io/badge/Language-C%23-blue.svg)](https://learn.microsoft.com/dotnet/csharp/)
[![Target](https://img.shields.io/badge/Framework-.NET%20Framework%204.8-green.svg)](https://dotnet.microsoft.com/)
[![Integration](https://img.shields.io/badge/Integration-MechJeb%202-red.svg)](https://github.com/MuMech/MechJeb2)

O **Projeto Artemis** é um mod de piloto automático e assistente de navegação autônoma para **Kerbal Space Program (KSP 1)**. Ele controla naves e foguetes desde a plataforma de lançamento até a órbita, reentrada e pouso seguro, sem necessidade de intervenção humana durante as fases críticas de voo.

O código foi arquitetado com base em princípios pedagógicos, com forte legibilidade, métodos concisos e separação clara de responsabilidades (ideal para desenvolvedores com background em **Python** que desejam entender ou estender código em C#).

---

## 📋 Índice

- [Principais Funcionalidades](#-principais-funcionalidades)
  - [1. Decolagem Orbital e Inserção](#1-decolagem-orbital-e-inserção)
  - [2. Reentrada e Pouso](#2-reentrada-e-pouso)
  - [3. Missões Autônomas: Turismo Suborbital](#3-missões-autônomas-turismo-suborbital)
  - [4. Controle Global de Atitude (Roll 0°)](#4-controle-global-de-atitude-roll-0)
- [Matemática da Curva Gravitacional (MechJeb 2)](#-matemática-da-curva-gravitacional-mechjeb-2)
- [Estrutura do Projeto](#-estrutura-do-projeto)
- [Interface de Usuário (GUI)](#-interface-de-usuário-gui)
- [Compilação e Instalação](#-compilação-e-instalação)
- [Futuro e Roadmap](#-futuro-e-roadmap)

---

## 🌟 Principais Funcionalidades

### 1. Decolagem Orbital e Inserção
* **Configuração de Voo:** Definição interativa de Apoastro alvo (ex: 80.000m) e Azimute/Direção de lançamento (padrão 90° = Leste).
* **Curva Gravitacional MechJeb 2:** Subida vertical inicial até 3,5 km ou 100 m/s, seguida de inclinação exponencial contínua até 60 km, alinhando a nave na horizontal ($0^\circ$ de pitch) ao cruzar as camadas superiores da atmosfera.
* **Auto-Staging Inteligente:** Detecta automaticamente o corte/esgotamento dos motores ativos (*flameout*) ou perda de empuxo, acionando o desacoplamento e ignição do estágio subsequente com temporizador de segurança (*debounce*).
* **Automação de Equipamentos (DeployableManager):**
  * **Acima de 75 km (Vácuo):** Estende automaticamente painéis solares, antenas de comunicação, radiadores térmicos e ejeta coifas protetoras (*fairings*).
  * **Abaixo de 70 km (Reentrada):** Recolhe imediatamente todos os instrumentos retráteis para evitar que sejam incinerados ou arrancados pelo arrasto aerodinâmico.
* **Circularização Orbital Autônoma:** Aguarda a aproximação do Apoastro no vácuo e realiza queima prograde precisa para circularizar e fechar uma órbita estável.

---

### 2. Reentrada e Pouso

#### 🟢 Modo Lander (Implementado)
* **Orientação Retrograde Estável:** Trava a nave rigidamente no vetor retrógrado (`Retrograde`) com `Roll 0°`, mantendo o escudo térmico frontal apontado contra o fluxo de plasma hipersônico.
* **Desativação Inteligente do SAS Nativo:** Desliga o atuador nativo do KSP para evitar conflitos de oscilação ou congelamento de comandos, operando em harmonia com o controlador de atitude do MechJeb.
* **Abertura Escalonada de Paraquedas:**
  * **Fase 1 (Drogue Chutes MK-12):** Dispara primeiro os paraquedas de desaceleração hipersônica em alta altitude assim que as condições de pressão e velocidade forem seguras.
  * **Fase 2 (Main Chutes):** Abre os paraquedas principais abaixo de 2.500 m de altitude real em relação ao solo/radar (com disparo emergencial garantido abaixo de 1.000 m).

#### 🟡 Modo Suicide Burn (No Radar)
* Pouso propulsivo vertical no estilo Falcon 9 e módulos de pouso sem paraquedas, calculando o ponto exato da queima final com empuxo máximo para zerar a velocidade no momento do toque com extensão do trem de pouso.

#### 🟡 Modo Space Shuttle (No Radar)
* Reentrada alada de ônibus espaciais e planadores orbitais (SSTO), controlando o ângulo de ataque (AoA) para dissipação térmica e rampa de descida autônoma até o pouso na pista do KSC (*Runway 09/27*).

---

### 3. Missões Autônomas: Turismo Suborbital
Um módulo de missão completa em **1 único clique**, ideal para contratos de turismo espacial:
1. **Lançamento Balístico:** Decola com curva gravitacional em arco mais íngreme (inclinando até $\sim 58^\circ$ de pitch).
2. **Corte a 75 km:** Desliga a aceleração exatamente ao atingir 75 km de Apoastro, subindo por inércia até o vácuo ($> 70\text{ km}$).
3. **Descarte Seguro do Booster no Espaço:** Ao cruzar os 70 km, aciona a separação do booster via `StageManager.ActivateNextStage()`. Isso garante a liberação limpa do propulsor **sem ejetar o escudo térmico**.
4. **Transição Automática para Lander:** Ao iniciar a descida, passa o controle imediatamente para o módulo Lander (Retrograde com Roll 0° e abertura dos paraquedas até o pouso suave).

---

### 4. Controle Global de Atitude (Roll 0°)
* Elimina a tendência dos foguetes do KSP de girar descontroladamente em torno do próprio eixo (*roll*).
* Mantém o ângulo de Roll fixado em $0^\circ$ durante todas as fases do voo e oferece um botão na interface para travamento de Roll em voo livre.

---

## 📐 Matemática da Curva Gravitacional (MechJeb 2)

O **Projeto Artemis** utiliza exatamente a mesma fórmula do editor de trajetória do MechJeb 2 (**Ascent Path Editor - Classic Profile**):

$$\text{pitch}(\text{alt}) = 90^\circ - \left( \left[ \frac{\text{alt} - \text{alt}_{\text{start}}}{\text{alt}_{\text{end}} - \text{alt}_{\text{start}}} \right]^{\text{shape}} \times (90^\circ - \theta_{\text{final}}) \right)$$

### Parâmetros Padrão Utilizados:
* **$\text{alt}_{\text{start}}$ (Início do Turn):** $3.500\text{ m}$ (ou velocidade de superfície $\ge 100\text{ m/s}$).
* **$\text{alt}_{\text{end}}$ (Fim da Curva):** $60.000\text{ m}$ ($60\text{ km}$).
* **$\text{shape}$ (Turn Shape Exponent):** $0.40$ ($40\%$).
* **$\theta_{\text{final}}$ (Ângulo Final):** $0^\circ$ (horizontal) para órbitas completas, ou $58^\circ$ para missões balísticas suborbitais.

Essa curva de potência garante que a nave comece subindo em pé para atravessar a atmosfera mais densa e gradualmente tombe suavemente em direção ao horizonte com mínimo arrasto e máxima eficiência de combustível.

---

## 📂 Estrutura do Projeto

```text
oraculo_2/
├── Artemis.csproj                 # Configuração do projeto .NET Framework 4.8
├── build.sh                       # Script de compilação e deploy automático no KSP
├── Raiz.md                        # Documento arquitetural e requisitos do projeto
├── README.md                      # Esta documentação completa
├── GameData/
│   └── Artemis/
│       └── Plugins/
│           └── Artemis.dll        # Binário compilado final do mod
└── Source/
    ├── ArtemisCore.cs             # Ponto de entrada MonoBehaviour do plugin KSP
    ├── Flight/
    │   ├── AscentAutopilot.cs     # Autopiloto de decolagem, curva gravitacional e circularização
    │   ├── AutoStaging.cs         # Monitoramento de flameout e avanço seguro de estágios
    │   ├── ReentryAutopilot.cs    # Controle de reentrada, orientação retrograde e pouso
    │   └── SuborbitalTourismController.cs # Orquestrador de missões de turismo 1-clique
    ├── Systems/
    │   ├── DeployableManager.cs   # Gestão de painéis solares, antenas, radiadores e coifas
    │   └── ParachuteManager.cs    # Disparo sequencial de paraquedas (Drogue -> Principais)
    ├── Telemetry/
    │   └── TelemetryReader.cs     # Coleta e conversão de dados de telemetria do KSP e MechJeb
    └── UI/
        └── MainWindow.cs          # Interface gráfica flutuante in-game (IMGUI)
```

---

## 🖥️ Interface de Usuário (GUI)

* **Atalho:** Pressione **`[F8]`** a qualquer momento durante o voo para abrir ou fechar a janela do Artemis.
* **Abas Disponíveis:**
  1. **`[Decolagem]`**: Definição de Apoastro, azimute e acionamento da subida orbital com acompanhamento de telemetria em tempo real.
  2. **`[Reentrada]`**: Acionamento manual do modo Lander para retorno de cápsulas com orientação e paraquedas automáticos.
  3. **`[Missões]`**: Disparo e monitoramento da missão integrada de Turismo Suborbital.
  4. **`[Roll 0° Global]`**: Botão de alternância rápida no rodapé para travar o eixo de Roll em voo livre.

---

## 🛠️ Compilação e Instalação

### Requisitos:
* **Linux ou Windows** com o **.NET SDK** instalado (suporte a `net48`).
* Instalação do **Kerbal Space Program 1 (1.12.x)** com o mod **MechJeb 2** instalado em `GameData/MechJeb2/`.

### Como compilar:
Execute o script de compilação no terminal:
```bash
./build.sh
```

O script realiza automaticamente:
1. A detecção da instalação do KSP no sistema;
2. A verificação das bibliotecas do jogo e do MechJeb 2;
3. A compilação da DLL `Artemis.dll` via `dotnet build`;
4. A cópia e instalação direta para a pasta `GameData/Artemis/Plugins/` do seu KSP.

---

## 📜 Licença

Projeto desenvolvido para fins educacionais e de automação no Kerbal Space Program. Todos os direitos reservados aos autores.
