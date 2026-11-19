# HoK Tool Android

Android mobile inspector project in this repository. The GitHub Actions workflow builds a debug APK.

Implemented: Vietnamese Android UI, file picker, HoK DB/QTSF/UnityFS/ZIP header identification, ZIP entry listing, report export, and QuickBMS script export.

Limitations: this is not a complete Android port of Studio-HoK/AssetStudio. Unity object extraction, texture/model/audio export, AssetBundle editing/repacking, and Oodle decompression are not implemented. The original desktop GUI is Windows Forms and its Oodle path depends on a Windows native DLL.

Build: open Actions and run Build HoK Tool Android APK, then download the HoK-Tool-Android-debug artifact. A push to main touching app or Gradle project files also triggers the workflow.
