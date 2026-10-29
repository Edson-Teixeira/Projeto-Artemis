# 🚀 Oráculo 2.0 Guidance (KSP 1 Mod)

Mod de automação de voo espacial, guiagem nativa, acoplagem de ponta a ponta, missões de carreira e pouso de precisão para **Kerbal Space Program 1**, projetado em **C# (.NET Framework 4.7.2)** para rodar no **Linux (Steam)** e **Windows**.

---

## 🌟 O que há de novo no Oráculo 2.0?

O **Oráculo 2.0** foi reconstruído com um núcleo de matemática orbital nativa e física em malha fechada, eliminando latências e dependências externas de outros pilotos automáticos para o voo básico:

* **Núcleo Físico Nativo:** Controlador PID em tempo discreto com proteção anti-windup e derivada na medição (`PIDController.cs`).
* **Álgebra Vetorial e Empuxo Real:** Cálculo contínuo da capacidade real dos motores (`availableThrust`), corrigindo em definitivo falhas de colisão no pouso (`NavigationMath.cs`).
* **Interface Unificada:** Painel moderno acionado por tecla de atalho (**`F8`** ou **`Alt + O`**) dividido em 5 áreas de operação.

---

## 🛸 Funcionalidades Principais

### 1. Painel de Telemetria e Diagnóstico Geral
* **Dados Orbitais:** Apoastro ($Ap$), Periastro ($Pe$), contagem regressiva de nós ($T-Ap$ e $T-Pe$), Inclinação ($i$), Excentricidade ($e$), Período orbital e **Argumento do Periastro ($\omega$)**.
* **Dinâmica de Voo:** Velocidade orbital ($v_{obt}$), velocidade de superfície ($v_{srf}$), velocidade vertical e pressão dinâmica ($Q$ em kPa).
* **Propulsão & $\Delta v$:** TWR atual vs. TWR máximo, empuxo operacional real em kN, $\Delta v$ do estágio atual e $\Delta v$ total da nave.
* **Conexão CommNet:** Status de link (Verde/Vermelho), porcentagem de sinal em tempo real, nível de controle e contagem de saltos (*hops*).

---

### 2. Lançamento Orbital Nativo 2.0
* **Curva de Gravidade Matemática:** Perfis selecionáveis:
  * *Normal:* $\text{Ângulo} = \text{progresso} \times 85^\circ$
  * *Rasa:* $\text{Ângulo} = \sqrt{\text{progresso} \times 0.8} \times 90^\circ$
  * *Aguda:* $\text{Ângulo} = \text{progresso}^{1.2} \times 80^\circ$
* **Auto-Staging com Detecção de Flameout:** Monitora os motores ativos e desacopla automaticamente estágios vazios (SRBs ou tanques inferiores) sem necessidade de comando manual.
* **Limitador de Pressão Dinâmica (Max-Q):** Modula o acelerador para impedir que a pressão dinâmica ultrapasse $25\text{ kPa}$, prevenindo falhas estruturais na atmosfera densa.
* **Ejeção Automática de Coifas:** Desprende *fairings* de carga útil automaticamente ao cruzar $50\text{ km}$ de altitude.
* **Circularização Nativa no Apoastro:** Calcula a queima ideal via Equação Vis-Viva e Tsiolkovsky, criando e executando o nó de manobra com corte de precisão ($< 0.8\text{ m/s}$).

---

### 3. Pouso Propulsivo (Suicide Burn / Hoverslam) 2.0
* **Cálculo de Distância Quadrática de Parada:**
  $$d_{parada} = |v_{vert}| \cdot t_{queima} + \frac{1}{2} a_{net} t_{queima}^2$$
* **Alinhamento Retrógrado Contínuo:** Trava a atitude no vetor retrógrado aerodinâmico de superfície para anular deriva horizontal.
* **Acionamento Aerodinâmico e Pernas:** Freios de ar abertos na descida e trem de pouso baixado automaticamente a $120\text{ m}$ do solo.
* **Duplo PID de Aterrissagem:** Desaceleração agressiva até $50\text{ m}$ e transição suave para toque terminal macio a $-1.0\text{ m/s}$.

---

### 4. Acoplagem Total (Do Lançamento ao Docking)
* **1-Clique da Plataforma ao Docking:**
  1. *Janela & Azimute:* Calcula quando o KSC cruza o plano da estação alvo e determina o rumo de lançamento.
  2. *Subida no Plano:* Lança direto na inclinação da estação e insere em órbita de perseguição ($85\text{ km}$).
  3. *Correção de Plano:* Zera inclinação relativa nos nós AN/DN ($\Delta i < 0.05^\circ$).
  4. *Transferência de Hohmann:* Queima prograde no ângulo de fase ideal para interceptação a menos de $150\text{ m}$.
  5. *Frenagem no Alvo:* Cancela a velocidade relativa ($\Delta v_{rel} < 0.2\text{ m/s}$) ao lado da estação.
  6. *Alinhamento de Portas:* Localiza e vira a porta de acoplagem frente a frente com a porta da estação.
  7. *Aproximação Translacional RCS (PID 3D):* Pilota jatos de RCS nos eixos X, Y e Z até a atracação magnética.

---

### 5. Central de Missões

#### A. Satélites de Contrato (Órbitas Específicas)
* **Varredura da Carreira:** Detecta automaticamente contratos ativos do `ContractSystem`.
* **Respeito ao Argumento do Periastro ($\omega$):** Sincroniza a queima de apoastro no ponto angular exato para garantir que a elipse coincida com a linha visual pontilhada da missão (Lado A e Lado B corretos do planeta).
* **3 Etapas Nativas:** Plano (AN/DN) $\longrightarrow$ Apoastro $\longrightarrow$ Periastro $\longrightarrow$ Estabilidade de 10s para validação.

#### B. Voos Turísticos Automatizados
* **Salto Suborbital:** Subida íngreme até $75\text{ km}$, validação do contrato no espaço ($>70\text{ km}$), reentrada travada no escudo térmico e aterrissagem suave (por paraquedas ou hoverslam).
* **Cruzeiro Orbital:** Decolagem orbital ($85\text{ km}$), tempo de apreciação da vista, queima retrógrada de desorbita ($Pe = 30\text{ km}$) e retorno seguro.

#### C. Missões Lunares (Mun e Minmus)
* Injeção translunar (TLI) automática a partir de órbita baixa de Kerbin, aceleração temporal até a SOI lunar e queima retrógrada de captura circular em órbita baixa ($25\text{ km}$ em Mun / $15\text{ km}$ em Minmus).

---

### 6. Salvaguardas Automáticas de Bordo

* **Automação Atmosférica de Dispositivos:**
  * Ao cruzar $70\text{ km}$ saindo da atmosfera: estende automaticamente todos os painéis solares, antenas e radiadores.
  * Ao reentrar na atmosfera ($< 70\text{ km}$ em descida): recolhe todas as peças retráteis para protegê-las do atrito e destruição aerodinâmica.
* **Salvaguarda de Bateria (Modo Hibernação):**
  * Se a carga elétrica cair abaixo de $15\%$, desliga giroscópios e orienta a nave para o Sol.
  * Ao recuperar $80\%$ de carga, restaura os sistemas de bordo automaticamente.

---

## 🛠️ Compilação e Instalação

### Pré-requisitos
* **Kerbal Space Program 1** (versão 1.8.0 até 1.12.5).
* **.NET SDK** (.NET 6, 7 ou 8 com target para `net472`).

### Compilando no Linux:
```bash
chmod +x build.sh
./build.sh
```

### Compilando no Windows (PowerShell):
```powershell
.\build.ps1
```

A `.dll` compilada será gerada em `GameData/OraculoGuidance/Plugins/OraculoGuidance.dll`.

---

## 🎮 Controles no Jogo

* **F8** ou **Alt + O**: Abre e fecha a janela do Oráculo 2.0.
