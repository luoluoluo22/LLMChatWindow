# LLMChatWindow

A simple AI chat window application built with WPF and C#.

## Features

*   Chat interface for interacting with Large Language Models (LLMs).
*   Loads chat history.
*   Configurable API settings (API Key, Base URL, Model Name) via a settings window.
*   Runs in the system tray.
*   Global hotkey (`Alt + Space`) to show/hide the window.
*   Attempts to adapt tray icon color based on Windows theme.

## Configuration

Upon first launch, the application will create a `settings.json` file in your user's AppData directory (`%APPDATA%\LLMChatWindow\`). You need to edit this file or use the in-app settings (gear icon) to provide a valid API Key for the AI service you intend to use.

## Building

1.  Clone the repository.
2.  Open the solution (`LLMChatWindow.sln`) in Visual Studio.
3.  Ensure you have the required .NET Desktop Runtime installed (targetting .NET 9 in this project).
4.  Restore NuGet packages.
5.  Build the solution (Debug or Release).

## Running the Installer (if published via ClickOnce)

Run the `setup.exe` file located in the publish output directory. 