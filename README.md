# WAP Chess — Unity Android App

## Overview
Unity 2022.3 LTS Android app migrated from the WAP Chess Vite/React web app.
- **W**atch — Live Lichess broadcasts
- **A**nalyze — Full 3D chess board + Stockfish engine
- **P**ractice — Puzzles, Free Play vs Computer, Stats

Landscape-only. Built entirely via GitHub Actions (no local Unity required).

---

## 🚀 First-Time Setup

### Step 1: Activate Unity Personal License (ONE TIME ONLY)

1. Push this repo to GitHub
2. Go to **Actions → "Activate Unity License" → Run workflow**
3. Wait for it to complete, then **download the artifact** (`unity-activation-file`)
4. Visit **https://license.unity3d.com/manual**
5. Upload the `.alf` file → click **Activate**
6. Download the resulting `.ulf` license file

### Step 2: Add GitHub Secrets

Go to your repo → **Settings → Secrets and variables → Actions → New repository secret**

| Secret Name | Value |
|---|---|
| `UNITY_LICENSE` | Full contents of the `.ulf` file you downloaded |
| `UNITY_EMAIL` | Your Unity account email |
| `UNITY_PASSWORD` | Your Unity account password |
| `ANDROID_KEYSTORE_BASE64` | Base64-encoded `.jks` keystore (see below) |
| `ANDROID_KEY_ALIAS` | Your key alias |
| `ANDROID_KEY_PASS` | Key password |
| `ANDROID_STORE_PASS` | Keystore password |

### Step 3: Generate an Android Keystore

```bash
# Generate keystore (run this on your PC or in any environment with Java)
keytool -genkey -v -keystore chessgod.jks \
  -alias ChessGod \
  -keyalg RSA -keysize 2048 \
  -validity 10000

# Encode it to base64 (on Linux/Mac)
base64 -w 0 chessgod.jks

# On Windows PowerShell:
[Convert]::ToBase64String([IO.File]::ReadAllBytes("chessgod.jks"))
```

Paste the base64 string as the `ANDROID_KEYSTORE_BASE64` secret.

---

## 🔨 Building

Once secrets are configured, every push to `main` triggers a build automatically.

To trigger manually:
1. Go to **Actions → "Build Android APK" → Run workflow**
2. Wait ~30-60 minutes for the build
3. Download the APK from the **Artifacts** section of the completed run

---

## 📁 Project Structure

```
ChessGodUnity/
├── .github/workflows/
│   ├── activate.yml        ← One-time license activation
│   └── build.yml           ← Main CI/CD build pipeline
├── Assets/
│   ├── Audio/SFX/          ← Chess sound effects (6 files)
│   ├── Materials/          ← Board + piece materials
│   ├── Models/             ← chess_set.glb, wood_plank.glb
│   ├── Plugins/Android/    ← Stockfish ARM64 binary (CI-downloaded)
│   ├── Prefabs/Pieces/     ← 12 chess piece prefabs
│   ├── Scenes/Main.unity   ← Single persistent scene
│   ├── Scripts/
│   │   ├── App/            ← AppController, NavigationState
│   │   ├── Audio/          ← ChessAudioManager
│   │   ├── Board2D/        ← 2D fallback board
│   │   ├── Board3D/        ← 3D board, pieces, animator, input
│   │   ├── Chess/          ← Core chess logic (ChessBoard, FEN, PGN, MoveTree)
│   │   ├── Engine/         ← StockfishBridge, AnalysisManager
│   │   ├── Input/          ← TouchPieceInput, InputRouter
│   │   ├── Network/        ← LichessClient
│   │   ├── Puzzles/        ← PuzzleDatabase, PuzzleLoader
│   │   ├── Screens/        ← AnalyzeScreen, PracticeScreen, WatchScreen, etc.
│   │   └── UI/             ← ScreenManager, BottomTabBar, all UI components
│   └── Settings/           ← URP pipeline asset
├── Packages/manifest.json
└── ProjectSettings/
```

---

## 🎮 Features

| Feature | Web Source | Unity Implementation |
|---|---|---|
| Watch Broadcasts | Watch.tsx + WatchDetails.tsx | WatchScreen.cs + LichessClient.cs |
| 3D Analysis Board | Playground3D.tsx (Three.js) | AnalyzeScreen.cs + BoardScene3D.cs |
| Stockfish Engine | fish.js (web worker) | StockfishBridge.cs (native process) |
| Puzzles | Puzzles.tsx + usePuzzles.ts | PuzzleScreen.cs + PuzzleDatabase.cs |
| Stats Dashboard | Stats.tsx | StatsScreen.cs |
| PGN Import | Import.tsx | ImportScreen.cs |
| Move Tree | moveTree.js | MoveTree.cs |
| Clock Sync | ClockManager.ts | ClockManager.cs |
| Audio SFX | audio.js | ChessAudioManager.cs |

---

## 📱 Device Requirements
- Android 7.0+ (API 24)
- ARM64 processor
- ~200MB storage (app) + optional puzzle download
- Internet connection for Watch screen
