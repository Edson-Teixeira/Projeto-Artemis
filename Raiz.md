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
* **Programa de Roll:** Manter ângulo de Roll travado em 45° (posiciona as janelas da cabine viradas para cima por estética e orientação).
* **Curva de Subida:** Subida vertical inicial seguida de pitch gradativo na direção do azimute escolhido.
* **Auto-Staging:** Monitorar os motores em queima. Detectar esgotamento de combustível (flameout), desacoplar o estágio vazio e acionar o próximo estágio automaticamente.
* **Transição Atmosférica (75 km):** Ao sair da atmosfera (Kerbin > 75km), acionar e liberar automaticamente:
  * Painéis solares
  * Antenas
  * Radiadores
  * Coifas (fairings)
* **Circularização Autônoma:** Ao atingir o apoastro no vácuo, calcular e executar automaticamente a queima de circularização em prograde, fechando uma órbita estável mantendo a altitude do apoastro definido.

---

### 2. Módulo de Reentrada Atmosférica e Pouso
* **Orientação de Reentrada:** Travar a nave e o sistema SAS/piloto no vetor **Retrograde** para utilizar o escudo térmico contra o fluxo de ar e absorver o calor da reentrada.
* **Abertura Escalonada de Paraquedas:**
  * **Fase 1 (Drogue Chutes):** Acionar primeiro os paraquedas de frenagem (ex: modelo MK-12 Drogue Chute) assim que as condições de velocidade e pressão aerodinâmica estiverem seguras (corte da velocidade hipersônica).
  * **Fase 2 (Main Chutes):** Acionar os paraquedas principais em altitude mais baixa e segura para desaceleração final até o pouso suave.
