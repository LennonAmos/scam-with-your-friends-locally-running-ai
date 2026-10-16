Publish only the product source and documentation explicitly allowed by .gitignore.

Keep development tests, fixtures, diagnostics, logs, screenshots and local build
data outside this repository. Never publish real user-profile paths, game-library
paths, credentials or machine-specific information. Use placeholders in examples.

Before every push or release, inspect the exact tracked files and nested release
archives for private data. Run the publish checks in Build-Release.ps1. Never add
ignored files with git add --force. Do not publish old development history.
