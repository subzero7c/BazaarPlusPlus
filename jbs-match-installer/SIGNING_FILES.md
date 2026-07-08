# JBSMatch Signing Files

This installer reads signing secrets from `signing-secrets/` or from matching
environment variables. The `signing-secrets/` directory is git-ignored; keep its
contents private and backed up separately.

## Required for updater signing

| File | Environment variable | Save/backup? | Notes |
| --- | --- | --- | --- |
| `signing-secrets/tauri-updater.key` | `TAURI_SIGNING_PRIVATE_KEY` | Yes | Tauri updater private key. Losing it can prevent existing clients from accepting future updates. |
| `signing-secrets/tauri-updater.password` | `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` | Yes, if used | Password for the updater private key. Omit if the key has no password. |

The updater public key is not secret and lives in
`src-tauri/tauri.conf.json` under `plugins.updater.pubkey`.

## Required for macOS signing and notarization

| File | Environment variable | Save/backup? | Notes |
| --- | --- | --- | --- |
| `signing-secrets/apple-signing-identity` | `APPLE_SIGNING_IDENTITY` | Yes | Exact Developer ID Application identity name. The certificate/private key itself is stored in macOS Keychain and should also be backed up/exported securely. |
| `signing-secrets/apple-api-issuer` | `APPLE_API_ISSUER` | Yes | Apple App Store Connect issuer ID for notarization. |
| `signing-secrets/apple-api-key` | `APPLE_API_KEY` | Yes | Apple API key ID. |
| `signing-secrets/apple-api-key-path` | `APPLE_API_KEY_PATH` | Yes | Path to the Apple API private key file. |
| `signing-secrets/AuthKey_<APPLE_API_KEY>.p8` | Used via `APPLE_API_KEY_PATH` inference | Yes | Apple API private key. If this file exists and matches the key ID, `build.sh` can infer the path. |

## Not signing secrets

These do not need private backup as signing secrets:

- `src-tauri/tauri.conf.json` updater public key
- `src-tauri/resources/BepInExSource/*/BepInEx.zip`
- `src-tauri/resources/SourceForBuild/`
- `src-tauri/target/`
- `build/`
- `node_modules/`
