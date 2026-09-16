# Quest 3 performance audit tools

These tools collect evidence. They do not optimize or save game assets.

- `Quest3AuditProbe.cs`: source for a temporary Unity Editor probe. With Unity closed, copy it into `Assets/KINOVR/Editor/`, run Unity 6000.3.23f1 with `-batchmode -nographics -projectPath C:/KINO2_VR_GAME -executeMethod Quest3AuditProbe.Run -quit -logFile C:/KINO2_VR_GAME/Artifacts/Quest3Performance/unity-audit.log`, then remove that temporary `.cs` and `.meta`. Output: `Artifacts/Quest3Performance/scene-inventory.json`. Do not run project rebuild/setup menu commands. Unity/package import hooks may independently change files; inspect the diff and preserve pre-existing user edits.
- `collect_metrics.py --adb <full-path-to-adb.exe> --seconds 100 --output <directory>`: records only VrApi and Unity warning/error messages for the already running KINO process. Does not install, restart the app, change headset settings, clear logcat, or force CPU/GPU clocks. Metadata distinguishes timeout completion from an early logcat disconnect. Requires one authorized device or an explicitly selected ADB transport.
- `summarize.py`: reads the root capture and the earlier capture in `Artifacts/Quest3Performance/initial/`, plus `scene-inventory.json`, and creates CSV files and `summary.json`. Run from the project root after both captures are complete.

Use a fresh output directory or archive the previous results before collecting another sample. The audit used an existing release APK; an actual Unity CPU/GC/Frame Debugger investigation requires a separate development build, followed by a release validation.

The inventory is a saved-scene census, not frame statistics: it does not frustum-cull renderers, run gameplay, or include spawned balls, runtime hands, UGUI triangles, or particle quads. The editor texture residency numbers are not on-device memory measurements. VrApi output is approximately one aggregate per second; its p95 is not a per-frame p95. Never interpret `CPU&GPU` as an isolated GPU time.
