# AudioJoiner 🔊

**AudioJoiner** é um utilitário desktop moderno, ultra-leve (< 40MB de RAM) e de alta performance para **Windows 10 e Windows 11**, desenvolvido em C# / .NET 8 e WPF por **Rafael Lannes** ([rafael-lannes.github.io](https://rafael-lannes.github.io)).

Seu objetivo principal é **clonar, unificar e agrupar a saída de áudio do sistema em tempo real para múltiplos dispositivos de som físicos simultaneamente** (ex: unir 2 monitores HDMI como "Áudio dos Monitores", fone USB, caixas Bluetooth, etc.) sem a necessidade de instalar drivers virtuais externos pesados.

---

## ✨ Recursos Principais

- 👥 **Unificação e Grupos de Dispositivos:** Crie grupos personalizados (ex: *"Áudio dos Monitores"*, unindo Monitor HDMI 1 + Monitor HDMI 2) com controle de liga/desliga unificado e slider de volume mestre do grupo.
- 🎚️ **Balanço Individual em Grupos:** Painel retrátil que permite ajustar o ganho independente de cada monitor/alto-falante dentro do grupo para equilibrar volumes.
- 🌐 **Integração Nativa com o Windows:** Permite vincular grupos a dispositivos virtuais e defini-los como saída padrão do Windows com 1 clique via API CoreAudio (`IPolicyConfigVista`).
- ⚡ **Zero Drivers Virtuais Obrigatórios:** Utiliza **WASAPI direto (Loopback Capture & Shared Renderers)** com a biblioteca `NAudio`.
- 🎛️ **Latência Ultra-Baixa (~20ms a 30ms):** Sem delay perceptível ou eco entre os fones e alto-falantes.
- 🔄 **Resampling e Downmix em Tempo Real:** Converte automaticamente taxas de amostragem (44.1 kHz vs 48.0 kHz) e faz downmix estéreo de fluxos multicanais (5.1/7.1 Surround).
- 🔌 **Detecção a Quente (Hot-Plugging):** Monitoramento via `IMMNotificationClient` para reconectar USB/HDMI/Bluetooth sem quebrar a reprodução.
- 🪟 **Design Windows 11 Fluent Dark:** Interface escura moderna de alto contraste e legibilidade, com medidores de pico (VU Meters) em tempo real por dispositivo e por grupo.
- 📌 **Bandeja do Sistema (System Tray):** Minimiza para a bandeja e oferece menu de contexto rápido para alternar o espelhamento sem abrir a janela.
- ℹ️ **Painel Sobre:** Informações completas da aplicação, créditos do desenvolvedor **Rafael Lannes** e atalho direto para o portfólio no GitHub.

---

## 👨‍💻 Desenvolvedor

- **Desenvolvido por:** Rafael Lannes
- **Website / GitHub:** [rafael-lannes.github.io](https://rafael-lannes.github.io)

---

## 🛠️ Como Compilar e Executar

### Pré-requisitos
- **Windows 10 ou Windows 11** (64-bit)
- **.NET 8 SDK** (ou .NET 9 SDK) instalado ([Download .NET](https://dotnet.microsoft.com/download))

### Execução em Modo de Desenvolvimento
```bash
dotnet run
```

### Compilar para Produção (Executável Leve e Otimizado)
```bash
dotnet publish -c Release -r win-x64 --self-contained false
```
O executável compilado estará localizado na pasta:
`bin\Release\net8.0-windows\win-x64\publish\AudioJoiner.exe`
