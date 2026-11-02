# Patch notes

## v1.0.9 — 2 November 2026

- Keep Ollama visible in the backend dropdown and its selected-value label instead of passing it through the game's official-only formatter.
- Remove a stripped string-concatenation call that stopped the model-refresh async method before its error handler could run.
- Preserve readable HTTP errors with the same Unity runtime compatibility fix.
- Validate plugin method references against the supplied game runtime during builds.
- Make the easy Linux script fetch the latest stable installer on each run.
- Verify both Ollama dropdown labels, model-list population, and the Local AI test inside the game against a running qwen2.5:7b model.
- Reject the old broken plugin in the runtime validator and pass the corrected plugin.

## v1.0.8 — 2 November 2026

- Update the Local AI runtime for game 0.1.95-swyf, Steam build 26261495.
- Store provider selection in the game's current local save store; preserve Ollama instead of letting the native provider getter reset it.
- Map host-menu choices to the current Official Backend, Official Backend (DeepSeek), and Ollama provider IDs.
- Rebuild the plugin and offline patcher against the current game assemblies.
- Include Linux/Proton and Windows installers with verified original backups.
- Verify Linux installation, repeat installation, old-backup upgrades, unsupported-build refusal and exact uninstall restoration using the current game assemblies. Live gameplay remains unverified.

## v1.0.7 — 2 November 2026 (Linux/Proton preview)

- Add Linux/Proton setup through the shared Mono installer, with Steam and Flatpak library discovery, portable Managed paths, Linux process detection and a Linux release archive.
- Add installer compilation and publication checks in GitHub Actions.
- Installation, rollback and gameplay validation on Linux remain required before release.

## v1.0.6 — 22 October 2026

- Supports game 0.1.83-swyf, Steam build 26097532.
- Disables the piracy-warning presentation: the flashing overlay, simulated download bars and looping alarm.
- Restores the menu when that warning would hide it, while retaining the local Ollama Play route.
- Adds no Steam ownership checks, sign-in requirements or firewall changes.
- Keeps the game's agreement and voice-readiness flow, along with saved Ollama settings and verified uninstall backups.

Close the game and run the new setup.exe. Cloud authentication results remain separate from the local AI route.

## v1.0.5 — 18 October 2026

- Supports game 0.1.81-swyf, Steam build 26020526.
- Restores Play after the game update; optional AI-service Steam sign-in stays below Quit.
- Keeps the new game-agreement screen and voice-readiness prompts in the Play flow.
- Preserves saved Ollama settings and archives verified backups from an earlier game build when reinstalling after the update.
- Removes the older optional Rick audio hooks. Famous Callers handles its own audio effects independently; neither mod requires the other.

Close the game and run the new setup.exe. For game 0.1.74-swyf, use the older v1.0.4 release.
