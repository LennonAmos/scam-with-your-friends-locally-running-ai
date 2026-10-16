# Scam With Your Friends — Local AI

Runs the game's chat AI on your PC with **Ollama**. Choose an installed model and adjust its settings in-game. Supports game version **0.1.71-swyf**.

## Setup

1. Install [Ollama](https://ollama.com/download/windows), then download a model by running `ollama pull qwen2.5:7b` in a terminal.
2. Close the game and run [setup.exe](https://github.com/LennonAmos/scam-with-your-friends-locally-running-ai/releases/latest).
3. Start the game and host a lobby.
4. Open **Settings → Host → AI Backend → Ollama (Runs on your own pc)**.
5. Click **Refresh models**, choose your model, then **Test local AI**.

Ollama must be running while you play. Start with **16,384 context tokens** and **2,048 maximum output tokens**. After a game update, rerun the latest installer to restore the mod.
