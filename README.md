# VoxAiGo (Windows Port)

This is the Windows version of VoxAiGo (formerly VibeFlow), built with **.NET 8 WPF**.
It is a pixel-perfect port of the macOS Swift app.

## Prerequisites

1.  **Windows 10/11**
2.  **Visual Studio 2022** (Community Edition is free)
    *   Workload: **.NET Desktop Development**
3.  **.NET 8 SDK** (Should come with VS 2022)

## Getting Started

1.  Clone this repository.
2.  Open `VoxAiGo.sln` in Visual Studio.
3.  Right-click `VoxAiGo.App` in Solution Explorer and select **Set as Startup Project**.
4.  Open `src/VoxAiGo.App/ViewModels/MainViewModel.cs` and replace `YOUR_GEMINI_API_KEY` with your actual Google Gemini API Key.
5.  Press **F5** to Run.

## How to Use

1.  The app runs in the background (check System Tray if implemented, or just running process).
2.  **Hold Alt + Win** key combination.
3.  Speak your command.
4.  Release keys.
5.  The text will be transcribed by Gemini and pasted into your active application.

## Project Structure

*   `src/VoxAiGo.Core`: Shared logic, models, services (Gemini, Audio processing).
*   `src/VoxAiGo.App`: WPF Application, UI, Windows Hooks (P/Invoke).

## Troubleshooting

*   **Audio Issues:** Ensure your default microphone is set correctly in Windows Settings.
*   **API Errors:** Check the Output window in Visual Studio for debug logs.
*   **Hotkeys:** If Alt+Win doesn't work, check if another app is using it or run Visual Studio as Administrator.
