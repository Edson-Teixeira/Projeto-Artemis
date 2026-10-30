# Projeto Artemis

## Objetivo
Criar um piloto automático / assistente de navegação para o jogo **KSP (Kerbal Space Program)** de modo a controlar foguetes e naves utilizando scripts de automação e navegação, sem intervenção humana.

## Diretrizes de Desenvolvimento
* **Linguagem:** C# nativo (KSP Plugin / MonoBehaviour).
* **Perfil do Desenvolvedor:** Background em Python, sem domínio prévio de C#. O código deve ser extremamente didático, limpo, conciso e com explicações simples do funcionamento de cada método.
* **Modularidade:** Isolar funções e responsabilidades em arquivos separados para facilitar manutenção e aprendizado.
* **Coleta de Telemetria:** Integrar com **MechJeb 2** (`vesselState` e bibliotecas de apoio), aproveitando cálculos de empuxo, TWR, tempos e queimas já mastigados pelo mod.

---

## Módulos e Funcionalidades

### 1. Módulo de Decolagem e Inserção Orbital
* **Altitude Alvo:** Campo na interface para digitar a altitude desejada do apoastro (ex: 80.000m / 80km).
* **Direção da Curva Gravitacional (Azimute):** Ângulo de 0° a 359° (padrão 90° = Leste).
* **Programa de Roll:** Manter ângulo de Roll travado em 0° de forma global.
* **Curva de Subida:** Subida vertical inicial (~500m) seguida de inclinação gradativa e contínua em direção ao horizonte.
* **Auto-Staging:** Monitorar os motores em queima. Detectar esgotamento de combustível (flameout), desacoplar o estágio vazio e acionar o próximo estágio automaticamente.
* **Transição Atmosférica:**
  * Ao subir acima de 75km: estender painéis solares, antenas, radiadores e ejetar coifas (fairings).
  * Ao descer abaixo de 70km: recolher automaticamente todos os instrumentos retráteis para proteção térmica/aerodinâmica.
* **Circularização Autônoma:** Ao atingir o apoastro no vácuo, calcular e executar automaticamente a queima de circularização em prograde, fechando uma órbita estável mantendo a altitude do apoastro definido.

---

### 2. Módulo de Reentrada e Pouso (Dividido em 3 Modos)

#### 2.1 Modo Lander / Cápsula (Pouso por Paraquedas) [STATUS: IMPLEMENTADO]
* **Orientação de Reentrada:** Travar a nave e o sistema SAS/piloto no vetor **Retrograde com Roll em 0°** para utilizar o escudo térmico contra o fluxo de ar e absorver o calor da reentrada sem conflitos de atuador.
* **Abertura Escalonada de Paraquedas:**
  * **Fase 1 (Drogue Chutes):** Acionar primeiro os paraquedas de frenagem (ex: modelo MK-12 Drogue Chute) assim que as condições de velocidade e pressão aerodinâmica estiverem seguras (corte da velocidade hipersônica).
  * **Fase 2 (Main Chutes):** Acionar os paraquedas principais em altitude mais baixa e segura (< 2.500m) para desaceleração final até o pouso suave.

#### 2.2 Modo Suicide Burn (Pouso Propulsivo / Foguete Vertical) [STATUS: NO RADAR]
* **Aplicação:** Foguetes reutilizáveis (estilo Falcon 9) e módulos lunares sem paraquedas.
* **Cálculo de Frenagem:** Monitorar continuamente a velocidade vertical, aceleração máxima dos motores (TWR) e altitude real em relação ao relevo (terreno/radar).
* **Queima no Último Segundo:** Manter queda livre controlada no vetor Retrograde e acionar potência máxima no ponto exato em que a distância de frenagem necessária iguala a altitude do solo, zerando a velocidade a ~0 m/s no instante do toque.
* **Extensão de Trem de Pouso:** Abertura automática das pernas de pouso (`gear`) antes do impacto.

#### 2.3 Modo Space Shuttle (Reentrada Alada e Pouso na Pista do KSC) [STATUS: NO RADAR]
* **Aplicação:** Ônibus espaciais, aviões espaciais (SSTO) e planadores orbitais.
* **Queima de Desorbitação:** Cálculo de retro-queima precisa com ponto de impacto aerodinâmico visando a aproximação da pista do KSC (Kennedy Space Center).
* **Voo Planado e Dissipação de Energia:** Sustentação com as asas durante a reentrada atmosférica, controlando ângulo de ataque (AoA) para frenagem térmica gradual e manobras em "S" se houver excesso de energia.
* **Aproximação e Pouso na Pista (Runway 09/27):** Navegação autônoma em direção à cabeceira da pista do KSC, rampa de descida suave (glide slope), ejeção do trem de pouso e frenagem horizontal na pista.

---

### 3. Módulo de Missões Autônomas

#### 3.1 Turismo Suborbital (Kerbin) [STATUS: IMPLEMENTADO]
* **Conceito:** Unificação total dos módulos de Lançamento e Lander em uma missão de 1 clique para contratos de turismo.
* **Fase de Subida:** Decola a 100% com curva gravitacional em arco balístico (inclinando até 45° a Leste) e Roll em 0°, cortando os motores assim que o Apoastro atingir 75km (garantindo rampa aerodinâmica necessária para frenagem segura na reentrada).
* **Fase Suborbital:** Sobe por inércia acima dos 70km, validando o cumprimento do objetivo de turismo no espaço.
* **Fase de Retorno:** Ao atingir o topo e iniciar a descida, ativa automaticamente o módulo Lander (alinhamento no vetor Retrograde com Roll 0° e disparo sequencial de paraquedas Drogue MK-12 -> Principais até o pouso completo).

---

### 4. Controle Global de Atitude
* **Roll 0° Global:** O ângulo de Roll em 0° é mantido de forma contínua em todos os estágios do voo (decolagem, planagem, reentrada e missões), com opção de travamento em voo livre na interface gráfica, eliminando oscilações e giros no próprio eixo.
