# Studio-HoK → Android: feature-parity scope

This is the porting contract for the Android application. It is based on the upstream Studio-HoK desktop GUI source:
- AssetStudio.GUI/MainForm.cs
- AssetStudio.GUI/MainForm.Designer.cs
- AssetStudio.GUI/AssetBrowser.cs
- AssetStudio.GUI/Exporter.cs
- AssetStudio.GUI/ExportOptions.cs
- AssetStudio.GUI/Studio.cs

The Windows Forms UI is not copied mechanically. The Android UI must expose the same user workflows using Android-native navigation, file pickers, previews, settings, and background jobs. The portable AssetStudio/Studio-HoK parsing core remains the shared engine.

## Desktop workflows that must be represented on Android

| Desktop area / control | Android port requirement |
|---|---|
| Load file, Load folder, drag-and-drop, .txt file list | SAF multi-file picker, folder picker, import path-list text file |
| Extract file, Extract folder | Extract Bundle, WebFile, BLK, Block, MHY and supported container formats to a chosen folder |
| Reset, Abort | Clear current workspace and cancel long-running jobs |
| Display all assets, type filter, search, sorting | All-object mode, per-type filters, name/container/source/PathID search, sortable virtualized list |
| Asset Browser | Dedicated browser with name, container, type, PathID, size and filter controls |
| Scene Hierarchy | GameObject/Transform tree, tree search, selection, context actions, export selected nodes |
| Asset Classes | Version-grouped class/type-tree browser and class structure export |
| Texture preview | Full-resolution preview, channel toggles (R/G/B/A), image export |
| Audio preview | Play/pause/stop/loop/volume/seek using Android-supported codecs; raw export for unsupported codecs |
| Shader/TextAsset/MonoBehaviour/Font previews | Native text/code/JSON/font inspection with copy and export |
| Mesh/GameObject/Animator/AnimationClip preview | Android OpenGL ES model/scene preview with camera controls and wireframe/shading/normal modes |
| Export all / selected / filtered assets | Preserve the three separate scopes and the original duplicate-name policy |
| Raw / Dump / JSON / XML | Raw object bytes, dump, JSON conversion, asset-list XML export |
| Animator + AnimationClips | Export Animator with selected clips and dependencies |
| Model export split / merge / selected nodes | Keep split/merge/node workflows; provide a documented Android-compatible interchange format where FBX native code is unavailable |
| Export options | Persist type parse/export flags, texture/audio conversion, image format, restored extension names, duplicate handling, model scale/axes/UV/material/skin/animation options, encryption and asset-map options |
| Specify Game / Unity version / UnityCN key | Game profile picker, Unity version override, UnityCN key manager |
| Specify AI version / Load AI / CABMap | AI version selection and loadable AI/CAB map resources |
| Build Map / Build Both / Clear Map | Android workflow for map creation, combined build and clear |
| Build AssetMap / Load AssetMap / AssetMap Type | AssetMap creation, loading and type selection |
| Dependency resolution / duplicates / skip container | Persist and apply the same load/export behavior |
| Debug console / file logging / logged events | In-app log viewer, event filters, shareable log file |
| Export class structures | Export all class/type-tree definitions grouped by Unity version |

## Android replacements for desktop-only dependencies

- Windows Forms controls → Android-native views and navigation.
- OpenTK WinForms preview → Android GLSurfaceView / OpenGL ES renderer.
- FMOD Windows DLL → Android-compatible audio backend; do not claim every FMOD codec is playable until tested.
- FBX P/Invoke DLL → managed model export/import path (OBJ/glTF first); retain FBX only if an Android ARM64 implementation is available and verified.
- Windows file dialogs → Storage Access Framework with persistable URI permissions.
- Windows settings/configuration → versioned app-private settings with import/export.
- Windows console → in-app log console.

## Existing Android work that can be reused

The current Android branch already contains the Studio-HoK parser integration, HoK DB/QTS VFS readers, AssetBrowser, SAF copy helpers, asset export helpers, texture/material/shader/audio/mesh patchers, and a UnityFS build service. These are engine/service pieces, not proof of full UI parity.

## Porting order

1. Core parity: file/folder/list loading, multi-file dependencies, reset/cancel, extract workflows, game/version profile, logging.
2. Workspace UI: desktop-equivalent asset list fields, filters, sorting, multi-selection, Asset Browser, Scene Hierarchy and Asset Classes.
3. Preview parity: Texture channel controls, audio controls, shader/text/JSON/font inspection, OpenGL ES mesh/model preview.
4. Export parity: all/selected/filtered, raw/dump/JSON/XML, textures/audio/fonts/video/mesh/shader, Animator/AnimationClip, split/merge/node export.
5. Settings/tools parity: complete Export Options, AI/CABMap/AssetMap, UnityCN key and map workflows.
6. Edit/build parity: texture/material/shader/audio/mesh import and verified UnityFS repack; retain explicit refusal for unsupported encrypted or structurally incompatible containers.

## Acceptance rule

A desktop feature is not considered ported merely because a button or menu item exists. It must have an Android interaction, a connected implementation, an output or preview that can be checked, and a clear error path for unsupported inputs. Do not run another APK build until the porting work has reached an integrated checkpoint and the user explicitly asks to build.
