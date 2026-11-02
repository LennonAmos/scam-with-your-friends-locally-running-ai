# Scam With Your Friends — Local AI

Runs the game's chat AI with **Ollama** on this PC or a trusted PC over Tailscale. Choose an installed model and adjust its settings in-game. Supports game **0.1.83-swyf**, Steam build **26097532**. Famous Callers is not required.

Disables the piracy-warning overlay, looping alarm and warning-related menu block. Adds no Steam ownership checks and leaves your firewall settings alone.

## Setup

### Linux / Proton (experimental)

The Linux installer targets the **Windows Steam build under Proton**. Native Linux game binaries are not supported. The game version and DLL hash checks are the same as on Windows.

Install your distribution's full Mono runtime, including `System.Web.Extensions`, and install [Ollama for Linux](https://ollama.com/download/linux). Run `ollama pull qwen2.5:7b`.

Download and extract [ScamOllama-v1.0.7-linux-proton.zip](https://github.com/LennonAmos/scam-with-your-friends-locally-running-ai/releases/tag/v1.0.7), then run:

```sh
sh Install-linux.sh --check
sh Install-linux.sh
```

Setup searches standard Steam and Flatpak Steam libraries. For a custom library or multiple copies, specify `--game-path "/path/to/Scam With Your Friends"`. Close the game before installation, then launch it normally through Steam with Proton and follow steps 3–5 below. Ollama runs on the Linux host at `http://127.0.0.1:11434`; do not install it inside Proton. No Proton prefix or launch-option changes are needed by this installer.

To remove the mod, close the game and run `sh Install-linux.sh --uninstall`. Original backups and settings are preserved. The installer compiles and its Linux entry points have been checked with Mono. Installation, rollback and gameplay with the actual game under Proton still need validation. The source checkout requires a release build to produce the embedded installer.

### Windows

1. Install [Ollama](https://ollama.com/download/windows), then download a model by running `ollama pull qwen2.5:7b` in a terminal.
2. Close the game and run [setup.exe](https://github.com/LennonAmos/scam-with-your-friends-locally-running-ai/releases/latest).
3. Start the game, click **Play**, and host a lobby. If AI-service sign-in is unavailable, Play selects Ollama. The optional **Sign in with Steam** button is below Quit.
4. Open **Settings → Host → AI Backend → Ollama (Local / Tailscale)**.
5. Click **Refresh models**, choose your model, then **Test local AI**.

Ollama must be running while you play. Start with **16,384 context tokens** and **2,048 maximum output tokens**. After a game update, rerun the latest installer to restore the mod.

For Tailscale, enter `http://<server-tailscale-ip>:11434` in **Ollama address**, then refresh models. Both PCs need Tailscale access to the server. On the GPU PC, Ollama can stay local while `tailscale serve --bg --tcp=11434 tcp://127.0.0.1:11434` shares it within the tailnet.

[Patch notes](CHANGELOG.md)
