# Scam With Your Friends — Local AI

Runs the game's chat AI with **Ollama** on this PC or a trusted PC over Tailscale. Choose an installed model and adjust its settings in-game. Supports game version **0.1.74-swyf**.

## Setup

1. Install [Ollama](https://ollama.com/download/windows), then download a model by running `ollama pull qwen2.5:7b` in a terminal.
2. Close the game and run [setup.exe](https://github.com/LennonAmos/scam-with-your-friends-locally-running-ai/releases/latest).
3. Start the game, click **Play**, and host a lobby. If AI-service sign-in is unavailable, Play selects Ollama. The optional **Sign in with Steam** button is below Quit.
4. Open **Settings → Host → AI Backend → Ollama (Local / Tailscale)**.
5. Click **Refresh models**, choose your model, then **Test local AI**.

Ollama must be running while you play. Start with **16,384 context tokens** and **2,048 maximum output tokens**. After a game update, rerun the latest installer to restore the mod.

For Tailscale, enter `http://<server-tailscale-ip>:11434` in **Ollama address**, then refresh models. Both PCs need Tailscale access to the server. On the GPU PC, Ollama can stay local while `tailscale serve --bg --tcp=11434 tcp://127.0.0.1:11434` shares it within the tailnet.
