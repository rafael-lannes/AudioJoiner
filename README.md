# AudioJoiner 🔊

**AudioJoiner** é um utilitário desktop moderno, ultra-leve (< 35MB de RAM) e de alta performance para **Windows 10 e Windows 11**, desenvolvido em C# / .NET 8 e WPF por **Rafael Lannes** ([rafael-lannes.github.io](https://rafael-lannes.github.io)).

Seu objetivo principal é **clonar e espelhar o áudio do sistema em tempo real para múltiplos dispositivos de som físicos simultaneamente** (fones de ouvido, alto-falantes, monitores HDMI, caixas Bluetooth, etc.) com latência ultra-baixa e um **Equalizador Gráfico DSP de 10 Bandas** integrado.

---

## ✨ Recursos Principais

- 🎚️ **Equalizador DSP de 10 Bandas em Tempo Real:** Ajuste fino de frequências (31Hz a 16kHz) com alcance de -12 dB a +12 dB e filtros Peaking EQ de alta precisão com proteção contra clipping suave.
- 🎨 **Presets de Equalização Integrados:** Perfis rápidos para *Flat*, *Reforço de Graves (Bass Boost)*, *Reforço de Agudos (Treble Boost)*, *Rock*, *Pop*, *Vocal / Podcast*, *Cinema / Filmes*, *Gamer / FPS*, *Eletrônica / Dance* e *Personalizado*.
- ⚡ **Zero Drivers Virtuais Obrigatórios:** Utiliza **WASAPI direto (Loopback Capture & Shared Renderers)** com a biblioteca `NAudio`.
- 🎛️ **Latência Ultra-Baixa (~20ms a 25ms):** Sem delay perceptível ou eco entre os dispositivos de saída.
- 🔊 **Controle Individual e Mestre de Volume:** Ajuste o ganho master e o volume independente de cada saída conectada.
- 🔄 **Resampling e Downmix em Tempo Real:** Converte automaticamente taxas de amostragem (44.1 kHz vs 48.0 kHz) e faz downmix estéreo de fluxos multicanais (5.1/7.1 Surround).
- 🔌 **Detecção a Quente (Hot-Plugging):** Monitoramento via `IMMNotificationClient` para reconectar USB/HDMI/Bluetooth automaticamente sem travar o áudio.
- 🪟 **Design Windows 11 Fluent Dark:** Interface escura moderna de alto contraste e legibilidade, com medidores de pico (VU Meters) em tempo real.
- 📌 **Bandeja do Sistema (System Tray):** Minimiza para a bandeja com menu de contexto rápido para alternar o espelhamento sem abrir a janela.
- 💾 **Persistência Automática:** Configurações salvas em `%APPDATA%\AudioJoiner\settings.json`.
- ℹ️ **Painel Sobre:** Informações completas da aplicação, créditos do desenvolvedor **Rafael Lannes** e atalho direto para o portfólio no GitHub.

---

## 👨‍💻 Desenvolvedor

- **Desenvolvido por:** Rafael Lannes
- **Website / GitHub:** [rafael-lannes.github.io](https://rafael-lannes.github.io)

---

## 🛠️ Como Compilar e Executar

### Pré-requisitos
- **Windows 10 ou Windows 11** (64-bit)
- **.NET 8 SDK** ([Download .NET](https://dotnet.microsoft.com/download))

### Execução em Modo de Desenvolvimento
```bash
dotnet run
```

### Compilar para Produção (Executável Otimizado)
```bash
dotnet publish -c Release -r win-x64 --self-contained false
```
O executável compilado estará localizado na pasta:
`bin\Release\net8.0-windows\win-x64\publish\AudioJoiner.exe`

