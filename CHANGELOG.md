# Changelog - AudioJoiner

Todas as atualizações notáveis deste projeto serão documentadas neste arquivo.

O formato é baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/) e este projeto segue [Versionamento Semântico (SemVer)](https://semver.org/lang/pt-BR/).

---

## [1.3.0] - 2026-09-16

### ✨ Novidades & Recursos
- **Equalizador com Divulgação Progressiva (*Progressive Disclosure*)**:
  - Novo botão de chevron (`⌃` / `⌵`) para expandir ou recolher as 10 bandas do equalizador DSP sob demanda.
  - Persistência automática do estado de expansão (`IsExpanded`) nas preferências do usuário.
  - Efeito de esmaecimento (*dimming*) sutil nas bandas quando o equalizador estiver desativado.
- **Ações em Lote (*Batch Actions*)**:
  - Adicionados botões de acesso rápido *"Ativar Todos"* e *"Desativar Todos"* para gerenciar todas as saídas de áudio com um clique.
- **Atalhos de Teclado Globais**:
  - <kbd>Espaço</kbd>: Iniciar / Parar espelhamento em tempo real (com detecção inteligente para não conflitar com caixas de texto).
  - <kbd>F5</kbd> ou <kbd>Ctrl+R</kbd>: Atualizar lista de dispositivos de som.
  - <kbd>Ctrl+E</kbd>: Expandir / Recolher os sliders do equalizador.
  - <kbd>Esc</kbd>: Ocultar e minimizar para a bandeja do sistema.
  - <kbd>Enter</kbd> / <kbd>Esc</kbd>: Fechar a janela *Sobre o AudioJoiner*.
- **Restauração Rápida por Duplo Clique (*Double-Click Reset*)**:
  - Duplo clique no slider de Volume Master restaura instantaneamente para **100%** (1.0 Unity Gain).
  - Duplo clique no volume de qualquer saída individual restaura para **100%**.
  - Duplo clique em qualquer uma das 10 bandas do equalizador restaura para **0.0 dB (Flat)**.
- **Banner Inline de Notificações (`InlineAlertBar`)**:
  - Mensagens de validação e avisos do motor de áudio agora aparecem em uma barra integrada no topo, com botão para dispensar (`✕`), substituindo caixas de diálogo bloqueantes.
- **Card de Estado Vazio (*Empty State*)**:
  - Apresentação visual limpa com instruções e botão de atualização caso nenhum dispositivo de saída esteja conectado.
- **Acessibilidade e Foco Visual (WCAG AA)**:
  - Anéis de foco estilizados (`ModernFocusVisual`) em azul neon `#0EA5E9` em todos os controles interativos.
  - Otimização do percurso da tecla <kbd>Tab</kbd> no equalizador (`TabNavigation="Once"`).
  - Inclusão de rótulos `AutomationProperties.Name` para leitores de tela do Windows.

### 🐛 Correções & Melhorias
- Corrigida autorreferência de cor cíclica no recurso `TextSecondaryBrush` do XAML.
- Validação no botão de espelhamento impedindo loopback fantasma quando nenhuma saída estiver marcada.
- Preservação integral dos comportamentos e ícones originais de minimizar para barra, bandeja e fechar.

---

## [1.2.0] - 2026-09-16

### ✨ Novidades
- **Equalizador Gráfico DSP de 10 Bandas**:
  - Bandas de 31 Hz, 62 Hz, 125 Hz, 250 Hz, 500 Hz, 1 kHz, 2 kHz, 4 kHz, 8 kHz e 16 kHz.
  - Faixa de ganho de -12.0 dB a +12.0 dB com filtros Peaking BiQuad e proteção suave contra clipping.
- **9 Presets de Equalização Integrados**:
  - *Flat*, *Bass Boost*, *Treble Boost*, *Rock*, *Pop*, *Vocal/Podcast*, *Cinema*, *Gamer/FPS*, *Eletrônica* e *Personalizado*.
- **Diálogo Sobre**:
  - Modal estilizado com descrição, versão, tecnologias utilizadas e link para o portfólio de Rafael Lannes ([rafael-lannes.github.io](https://rafael-lannes.github.io)).

---

## [1.0.0] - 2026-09-13

### ✨ Versão Inicial
- Espelhamento de áudio WASAPI Loopback para múltiplos dispositivos simultaneamente.
- Resampling automático de taxas (44.1 kHz / 48 kHz) e downmix estéreo.
- Hot-plugging para detecção de conexão e desconexão de dispositivos USB, HDMI e Bluetooth.
- Ícone na bandeja do sistema com menu rápido.
- Medidores VU de pico em tempo real.
