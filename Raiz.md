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
* **Programa de Roll:** Manter ângulo de Roll travado em 0°.
* **Curva de Subida:** Subida vertical inicial seguida de pitch gradativo na direção do azimute escolhido.
* **Auto-Staging:** Monitorar os motores em queima. Detectar esgotamento de combustível (flameout), desacoplar o estágio vazio e acionar o próximo estágio automaticamente.
* **Transição Atmosférica (75 km):** Ao sair da atmosfera (Kerbin > 75km), acionar e liberar automaticamente:
  * Painéis solares
  * Antenas
  * Radiadores
  * Coifas (fairings)
* **Circularização Autônoma:** Ao atingir o apoastro no vácuo, calcular e executar automaticamente a queima de circularização em prograde, fechando uma órbita estável mantendo a altitude do apoastro definido.

---

### 2. Módulo de Reentrada e Pouso (Dividido em 3 Modos)

#### 2.1 Modo Lander / Cápsula (Pouso por Paraquedas) [STATUS: IMPLEMENTADO]
* **Orientação de Reentrada:** Travar a nave e o sistema SAS/piloto no vetor **Retrograde** para utilizar o escudo térmico contra o fluxo de ar e absorver o calor da reentrada.
* **Abertura Escalonada de Paraquedas:**
  * **Fase 1 (Drogue Chutes):** Acionar primeiro os paraquedas de frenagem (ex: modelo MK-12 Drogue Chute) assim que as condições de velocidade e pressão aerodinâmica estiverem seguras (corte da velocidade hipersônica).
  * **Fase 2 (Main Chutes):** Acionar os paraquedas principais em altitude mais baixa e segura para desaceleração final até o pouso suave.

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
