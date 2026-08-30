# Repository Guidelines

## Project Context

This repository contains a Unity mobile game. The Unity project root is `ExpoTheExplorer/`; open that folder in Unity. Existing gameplay architecture should not be changed unless explicitly requested.

## Project Structure & Module Organization

Runtime code lives in `ExpoTheExplorer/Assets/Scripts`, with gameplay logic split across `Core`, `Systems`, `Session`, and `Bootstrap`. UI components live in `ExpoTheExplorer/Assets/Scripts/UI`. Editor tools are in `ExpoTheExplorer/Assets/Editor`, tests are in `ExpoTheExplorer/Assets/Tests/EditMode`, scenes are in `ExpoTheExplorer/Assets/Scenes`, prefabs are in `ExpoTheExplorer/Assets/Prefabs`, and data/config assets are in `ExpoTheExplorer/Assets/Data`.

## AI-Assisted Art Pipeline

New generated art must go under `ExpoTheExplorer/Assets/Art/Generated`. Approved production art must go under `ExpoTheExplorer/Assets/Art/Approved`. Existing sprites may be analyzed as style references, but must never be overwritten. Do not move existing assets unless explicitly requested. Never manually generate Unity `.meta` files; let Unity create and update them.

Sprite references should be assigned through Unity assets or prefabs whenever possible. Before creating a new prefab, inspect similar prefabs in `ExpoTheExplorer/Assets/Prefabs` and follow their hierarchy, components, anchors, fonts, materials, and naming. When adding UI, prefer creating or updating prefabs instead of duplicating scene objects. Reuse existing UI components, fonts, materials, sprites, and prefabs whenever possible.

## Coding Style & Naming Conventions

Use C# with four-space indentation. Public types, methods, and properties use PascalCase; private fields and locals use camelCase; serialized fields should remain `[SerializeField] private`. Before writing a new script, check whether an existing component already solves the problem. Keep content values in ScriptableObjects or day data, not hardcoded in gameplay scripts.

## Build, Test, and Development Commands

Unity version is `6000.3.16f1`, recorded in `ExpoTheExplorer/ProjectSettings/ProjectVersion.txt`.

```bash
git status --short
Unity -batchmode -projectPath ExpoTheExplorer -runTests -testPlatform EditMode -quit
```

Use Unity Test Runner for EditMode tests. Treat plain `dotnet build` cautiously because generated project files may not include every Unity source folder.

## Testing Guidelines

Tests live in `ExpoTheExplorer/Assets/Tests/EditMode` and are named `*Tests.cs`. Add focused tests for gameplay/system logic changes. For art, prefab, or UI layout changes, verify visually in Unity and include screenshots when reporting results.

## Commit & Pull Request Guidelines

Recent commits use short imperative summaries, such as `Give every ticket a customer's face`. Keep commits scoped. PRs should describe the gameplay, UI, or art-pipeline change, list verification performed, and include screenshots for visible changes.

## Agent Reporting Rules

After each task, report every file created or modified. Do not revert unrelated user changes. Read `ExpoTheExplorer/CLAUDE.md` and `.claude/index.md` before substantial implementation work.
