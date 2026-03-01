# VoxAiGo Windows — O que falta ser feito

> Atualizado: 2026-03-01
> Repo: voxaigo-windows
> Status: MVP funcional (gravação, transcrição, auth, wake word)

---

## ✅ O que já está funcionando

- **Gravação por voz** — Ctrl+Space (segurar) com WASAPI/NAudio
- **Transcrição AI** — Supabase (Pro), Gemini BYOK, Whisper local (Free)
- **15 modos de transcrição** — Código, Texto, Email, Vibe Coder, etc.
- **30 idiomas** com bandeiras e aliases de voz
- **Autenticação** — Google OAuth, email/senha, magic link, reset de senha
- **Agente Vox** — Wake word "Vox" para trocar modo/idioma por voz
- **Setup Wizard** — 7 etapas em português (login, mic, gravação, modos, voz, idioma, pronto)
- **Gate de login** — App exige login antes de mostrar tela principal
- **Auto-paste** — Cola automaticamente no app ativo (VS Code, etc.)
- **Clipboard retry** — Retry com 10 tentativas para COMException do Windows
- **Managers** — Settings, Subscription, Trial, Analytics, Snippets, WritingStyle, Sound
- **Tray icon** — Ícone na bandeja com menu básico

---

## 🔴 Fase 1 — Bugs Críticos

### 1.1 Investigar roteamento de engine (IsPro)
- Usuário Pro pode estar sendo roteado para Whisper em vez de Supabase/Gemini
- Verificar lógica em `MainViewModel.cs` → `DetermineEngine()`
- Testar com conta Pro ativa

### 1.2 Corrigir paleta de cores (WINDOWS-UI-GUIDE.md)
| Elemento | Atual (errado) | Correto |
|----------|----------------|---------|
| Fundo | #1A1A2E (navy) | #0A0A0A (preto) |
| Superfície | #252540 | #141414 |
| Ouro | #D4A017 | #D4AF37 |
| Borda | #333366 | #1F1F1F |
| Texto | #E0E0E0 | #F5F5F5 |
| Perigo | #FF6666 | #FF4444 |
| Sucesso | #00CC88 | #4ADE80 |

---

## 🟡 Fase 2 — UX Principal

### 2.1 Redesign do HUD Overlay
- Formato atual: retângulo fixo → Precisa ser **cápsula** (border-radius 9999)
- Tamanho: idle 200×56, listening 440×56, processing 220×56
- Posição: 25% do fundo da tela (não topo)
- 3 estados: idle → listening → processing → success/error
- Ondas orgânicas sinusoidais (não barras discretas)
- Borda dourada para Pro
- Pill de idioma + modo ativo
- Animação spring (0.45s)
- Não roubar foco do app ativo

### 2.2 Atalhos de teclado
| Atalho | Ação | Status |
|--------|------|--------|
| Alt+M | Ciclar modo | ❌ |
| Alt+L | Ciclar idioma | ❌ |
| Alt+P | Colar última transcrição | ❌ |
| Ctrl+, | Abrir configurações | ❌ |
| Ctrl+Y | Abrir histórico | ❌ |
| Ctrl+Shift+V | Mostrar/ocultar janela | ❌ |

### 2.3 HUDs de notificação
- Pop-in cápsula ao trocar idioma/modo (auto-dismiss 2s)
- Notificação de "Colado!" após transcrição
- Notificação de comando de voz detectado
- Click-through (não interceptar cliques)

---

## 🟡 Fase 3 — Paridade de Features

### 3.1 Menu do tray expandido
- Submenu de 15 modos com checkmark
- Submenu de idiomas com favoritos
- Seleção de microfone
- Status do plano: "email — Pro" ou "email — Grátis: 23/75"
- Toggle offline (Pro only)
- Links para Histórico/Snippets/Stats

### 3.2 Modais e diálogos
- Modal de boas-vindas ao trial (400×560)
- Modal de trial expirado (upgrade + preço)
- Modal de limite mensal (75/mês atingido)
- Modal de upgrade contextual (modo/idioma bloqueado)

### 3.3 Melhorias no SoundManager
- Sons reais (.wav) para start/stop/error/success
- Atualmente usa MessageBeep (bip básico do sistema)

---

## 🔵 Fase 4 — Recursos Avançados

### 4.1 Conversation Reply HUD
- HUD 440×(64-200) com 4 estados
- Detecção de idioma
- Gravação de resposta por voz
- Pipeline de tradução
- Auto-dismiss (25s timeout) com barra de countdown

### 4.2 Vox Transform (texto-para-texto)
- Pro only
- Transformar texto selecionado usando IA
- Sem gravação — apenas processa texto da clipboard

### 4.3 Janela de Histórico standalone
- 550×600 com busca e exportação
- Atualmente existe como seção, mas não como janela separada

### 4.4 Analytics com gráficos
- Breakdown por modo e idioma
- Gráfico de uso diário/semanal

### 4.5 Gamificação
- 7 tiers de nível
- 20 achievements
- Streaks e milestones

### 4.6 Seleção de microfone
- Nas configurações + menu do tray
- Listar dispositivos WASAPI disponíveis

### 4.7 Sync de subscription (5min)
- Refresh em background a cada 5 minutos
- Atualmente só verifica no login

---

## 📁 Estrutura atual do código

```
src/
├── VoxAiGo.Core/           # Lógica de negócio
│   ├── Managers/            # Settings, Subscription, Trial, Analytics, Snippets, WritingStyle, Sound
│   ├── Services/            # Auth, Supabase, Gemini, Whisper, PromptBuilder, WakeWord, History
│   └── Models/              # TranscriptionMode, SpeechLanguage, TranscriptionServiceType
│
└── VoxAiGo.App/             # WPF UI
    ├── Views/               # MainWindow, OverlayWindow, SetupWizardWindow
    ├── ViewModels/           # MainViewModel, MainWindowViewModel, SettingsViewModel
    ├── Controls/             # SoundWaveControl
    └── Platform/             # AudioRecorder, GlobalHotkeyManager, NativeMethods
```

## 🔧 Build & Run

```bash
cd "C:\Users\PC\Downloads\voxaigo-windows-github"
dotnet publish src/VoxAiGo.App/VoxAiGo.App.csproj -c Release -r win-x64 --self-contained -o publish
./publish/VoxAiGo.App.exe
```

---

## ⚠️ Notas importantes

1. **Token do GitHub** — O token `ghp_XcFe...` foi exposto no terminal. **REVOGAR IMEDIATAMENTE** em GitHub > Settings > Developer settings > Personal access tokens
2. **Config sensível** — API keys estão em `%LOCALAPPDATA%\VoxAiGo\settings.json` (encriptado com DPAPI)
3. **Whisper models** — Modelos .bin não devem ser commitados (estão no .gitignore)
