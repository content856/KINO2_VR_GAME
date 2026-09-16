# Audit απόδοσης Quest 3 — 16/09/2026

Στόχος: **72 FPS, 13,89 ms ανά frame**, με προτεραιότητα στην ποιότητα των γραφικών. Το βασικό περιθώριο βελτίωσης βρίσκεται στις διακοσμητικές μπάλες, στον αριθμό ανεξάρτητων renderers/canvases και στην ενεργοποίηση foveation. Δεν υπάρχει ακόμη μέτρηση που να αποδίδει συγκεκριμένα milliseconds σε κάθε εύρημα.

Το audit δεν άλλαξε gameplay, σκηνές, shaders, textures ή build settings. Δημιουργήθηκαν η αναφορά, εργαλεία απογραφής και αρχεία μετρήσεων. Ο προσωρινός Editor probe αφαιρέθηκε και οι παρεπόμενες αλλαγές από το import επαναφέρθηκαν.

## Έκταση ελέγχου και πραγματικές μετρήσεις

- Unity **6000.3.23f1**, URP **17.3.0**, OpenXR **1.18.0**, Meta XR **205.0.0**.
- Enabled build scene: `Assets/KinoRotunda/Scenes/KinoRotunda.unity`. Η απογραφή έγινε με Unity στο Android target, με κανονική επίλυση prefabs και scene overrides.
- Μετρήθηκε το υπάρχον release APK στο συνδεδεμένο Quest 3. Το SHA-256 του εγκατεστημένου APK ταυτίστηκε με το `Builds/Quest3/KINO2_Quest3.apk`, μέγεθος **121.074.821 bytes**. Δεν έγινε νέο build του σημερινού checkout, επομένως η απογραφή της πηγής και οι μετρήσεις του APK παραμένουν διαφορετικά είδη τεκμηρίωσης.
- Η πρώτη καταγραφή έδωσε 56 δείγματα σε περίπου 55 δευτερόλεπτα πριν τη διακοπή της σύνδεσης ADB. Ακολούθησε επανεκκίνηση του παιχνιδιού και νέα καταγραφή με ζητούμενη διάρκεια 100 δευτερολέπτων, ώστε να περιλάβει έναν αυτόματο γύρο 75 δευτερολέπτων και τη φάση μετά τη λήξη του. Η κατάσταση του γύρου και η κατεύθυνση θέασης δεν έχουν instrumentation μέσα στο APK.
- Τα ακριβή αποτελέσματα είναι στο [summary.json](Artifacts/Quest3Performance/summary.json), τα πρωτογενή δείγματα στο [metrics.csv](Artifacts/Quest3Performance/metrics.csv) και στο [quest3-live.log](Artifacts/Quest3Performance/quest3-live.log).

Η δεύτερη καταγραφή ολοκλήρωσε κανονικά τα 100 s του host και έδωσε **98 δείγματα**, από 16:10:39 έως 16:12:16 ώρα συσκευής. Υπάρχει μικρή διαφορά ώρας μεταξύ host και συσκευής. Το logcat τερματίστηκε από το εργαλείο στο τέλος της προκαθορισμένης διάρκειας· ο κωδικός τερματισμού του δεν υποδεικνύει crash του παιχνιδιού.

| Μετρική δεύτερης καταγραφής | Όλα τα δείγματα | Μετά τα πρώτα 10 s δειγμάτων |
|---|---:|---:|
| Αριθμός δειγμάτων | 98 | 87 |
| Διάμεσος αναφερόμενων FPS | 72 | 72 |
| Ελάχιστα αναφερόμενα FPS | 56, στην εκκίνηση | 71 |
| Δείγματα με FPS κάτω από 72 | 8 | 3 |
| Άθροισμα πεδίου Stale | 123 | 54 |
| Άθροισμα πεδίου Tear | 0 | 0 |
| Διάμεσος / p95 πεδίου App | 11,13 / 12,19 ms | 11,12 / 12,18 ms |
| Διάμεσος GPU utilization | 90% | 90% |
| Διάμεσος CPU utilization | 37% | 36% |
| Fov | 0 σε όλα | 0 σε όλα |

Το refresh target είναι 72 Hz σε όλα τα δείγματα· περιστασιακές αναφορές 73–74 FPS είναι αποτέλεσμα των συγκεντρωτικών χρονικών παραθύρων. Η εφαρμογή πετυχαίνει συνήθως τον στόχο, αλλά τα Stale δείχνουν ότι χρειάζεται έλεγχος για μικρές ασυνέχειες. Η αξιοποίηση GPU είναι ένδειξη υπέρ της προτεραιότητας στο rendering, **όχι απόδειξη συγκεκριμένου GPU bottleneck**, ιδίως με αυτόματη μεταβολή clocks. Το στιγμιότυπο μνήμης της δεύτερης δοκιμής έδωσε PSS **606,4 MiB** / RSS **733,5 MiB**, χωρίς ένδειξη ότι η μνήμη είναι ο άμεσος περιορισμός.

Τα VrApi logs δίνουν περίπου ένα συγκεντρωτικό δείγμα ανά δευτερόλεπτο. Το p95 αυτών των δειγμάτων **δεν είναι per-frame p95**, και τα πεδία `App` / `CPU&GPU` δεν αντικαθιστούν χωριστά Unity CPU/GPU profiler timings. Οι ενδείξεις αξιοποίησης GPU/CPU περιλαμβάνουν επιρροές του συστήματος/compositor. Τα σύντομα captures δεν πιστοποιούν θερμική σταθερότητα σε μακρά συνεδρία.

## Απογραφή της σκηνής

| Μέγεθος | Αποθηκευμένη σκηνή |
|---|---:|
| Ενεργοί, enabled Renderers | 552 |
| Τρίγωνα των παραπάνω meshes | 613.058 |
| Διακοσμητικές αριθμημένες μπάλες | 77 |
| Τρίγωνα αυτών των μπαλών | 315.392 — 51,45% του συνόλου |
| Ορατά ανεξάρτητα αρχιτεκτονικά χρυσά στολίδια | 278 |
| World-space canvases | 78: πίνακας + 77 αριθμοί μπαλών |
| Graphics κάτω από το canvas του πίνακα, μαζί με ανενεργά | 170 |
| LODGroups | 0 |
| Φώτα | 118 Baked + 1 Mixed |
| Lightmaps | 2 |
| Ενεργά reflection probes | 2, Custom cubemaps 512 |
| Ενεργά colliders / αποθηκευμένα rigidbodies | 59 / 0 |

Αυτά είναι σύνολα πριν το Play Mode και πριν το frustum culling. **552 renderers δεν σημαίνει 552 draw calls ανά frame.** Τα τρίγωνα δεν περιλαμβάνουν UGUI, particle quads, runtime hand meshes ή τις μπάλες που δημιουργεί ο launcher. Η βιβλιοθήκη περιέχει παλιότερες αναφορές με διαφορετικούς αριθμούς· εδώ χρησιμοποιείται η νέα απογραφή της πραγματικής σκηνής.

## Προτεραιότητες βελτίωσης

### 1. Απλούστερο mesh για τις διακοσμητικές μπάλες

**Βεβαιότητα: υψηλή για τη σπατάλη γεωμετρίας, άγνωστο ακόμη το κέρδος σε ms.** Το `KinoOvalBall.asset` έχει 2.145 vertices / 4.096 τρίγωνα και χρησιμοποιείται 77 φορές. Ο generator ορίζει 64 στήλες × 32 σειρές στο [KinoGameplayVisuals.cs](Assets/KINOVR/Editor/KinoGameplayVisuals.cs#L87). Οι διακοσμητικές μπάλες στους σωλήνες και στη λοταρία δεν χρειάζονται κατ' ανάγκη την ίδια πυκνότητα με την μπάλα κοντά στα χέρια.

Πρώτη δοκιμή: ξεχωριστό διακοσμητικό mesh 32 × 16, δηλαδή 1.024 τρίγωνα. Το σύνολό τους πέφτει από 315.392 σε 78.848: **236.544 λιγότερα τρίγωνα**, περίπου **38,6%** λιγότερα στην απογραφή όλης της σκηνής. Αυτή είναι αριθμητική μείωση γεωμετρίας, όχι πρόβλεψη αύξησης FPS. Μετά από έλεγχο της σιλουέτας στο headset, δοκιμάζουμε δεύτερο LOD 512 τριγώνων. Οι κοντινές gameplay μπάλες διατηρούν υψηλότερη λεπτομέρεια.

Έλεγχος: silhouette/specular highlight στην κοντινότερη επιτρεπτή απόσταση, σωστό oval 1,68:1, θέση αριθμού και collider, και ίδιο capture πριν/μετά. Η αλλαγή πρέπει να ενσωματωθεί και στον generator ώστε να επιβιώνει από regeneration.

### 2. Λιγότερα ανεξάρτητα renderers και canvases

**Βεβαιότητα: υψηλή για τη δομή, χρειάζεται Frame Debugger για το πραγματικό κόστος υποβολής.** Τα 278 ορατά αρχιτεκτονικά στολίδια έχουν μόνο 20.824 τρίγωνα συνολικά, αλλά είναι ξεχωριστοί non-static renderers. Ο builder τα κρατά σκόπιμα μη στατικά για μελλοντικό animation: [KinoRotundaBuilder.cs](Assets/KinoRotunda/Editor/KinoRotundaBuilder.cs#L152). Το SRP Batcher και το material instancing checkbox δεν εγγυώνται ότι διαφορετικά meshes θα γίνουν ένα draw call.

Για όσα παραμένουν ακίνητα: Quest variant με grouping ανά αρχιτεκτονικό τμήμα και material ή ελεγχόμενο static batching. Διατηρούμε τα source transforms για editing/μελλοντικό animation. Αποφεύγουμε να ενωθεί ολόκληρη η αίθουσα σε ένα mesh, ώστε να παραμείνει χρήσιμο το culling και να μη χαλάσουν lightmap UVs/probe lighting.

Οι 77 διακοσμητικοί αριθμοί έχουν ο καθένας ξεχωριστό canvas και `KinoBallNumber.LateUpdate`. Προτείνεται atlas 1–80 με κοινό υλικό και quads/instanced rendering, διατηρώντας το facing προς τον παίκτη και το στήριγμα στην οβάλ επιφάνεια. Εναλλακτικά δοκιμάζεται TextMeshPro 3D με κοινό font material. Η αντικατάσταση canvas από TMP 3D μόνη της δεν εγγυάται batching.

### 3. Ενεργοποίηση και επιβεβαίωση foveated rendering

**Βεβαιότητα: υψηλή ότι παραμένει ανενεργό στα δείγματα.** Τα Android OpenXR features περιλαμβάνουν Foveated Rendering, Meta XR Foveation και Subsampled Layout, αλλά δεν βρέθηκε runtime ρύθμιση ισχύος στα gameplay scripts. Τα καταγεγραμμένα δείγματα αναφέρουν `Fov=0`.

Το εγκατεστημένο OpenXR 1.18 τεκμηριώνει ότι η ισχύς είναι off από προεπιλογή και απαιτεί runtime ενεργοποίηση. Με την υπάρχουσα επιλογή **SRP Foveation**, χρησιμοποιούμε ένα συνεπές API, π.χ. `XRDisplaySubsystem.foveatedRenderingLevel`, αφού ξεκινήσει το XR subsystem. Ξεκινάμε με ήπια ισχύ και συγκρίνουμε την αναγνωσιμότητα του πίνακα και τις μαρμάρινες λεπτομέρειες στην περιφέρεια. Επιβεβαιώνουμε το αποτέλεσμα σε telemetry/frame capture, αντί να βασιστούμε μόνο στο checkbox. Η foveation μειώνει pixel shading· δεν λύνει το πλήθος draw calls ή τη γεωμετρία. [Unity XR οδηγίες](https://docs.unity3d.com/6000.3/Documentation/Manual/xr-untethered-device-optimization.html), [Meta FFR](https://developers.meta.com/horizon/documentation/unity/unity-fixed-foveated-rendering/).

### 4. UI updates και allocations

Το [KinoNumberBoard.SetProgress](Assets/KINOVR/Scripts/KinoNumberBoard.cs#L47) καλείται κάθε frame από το round controller. Δημιουργεί strings για score/time ακόμη και όταν η ορατή τιμή δεν αλλάζει. Το TMP μπορεί να αποφεύγει περιττό rebuild όταν λαμβάνει το ίδιο κείμενο, αλλά τα strings έχουν ήδη δημιουργηθεί. Το `timeFill.anchorMax` αλλάζει κάθε frame μέσα στο ίδιο canvas με τα υπόλοιπα graphics.

Προτείνεται cache τελευταίου δευτερολέπτου/score/status, ενημέρωση μόνο όταν αλλάζουν, και ξεχωριστό μικρό canvas για timer/fill ώστε να διατηρηθεί ομαλή κίνηση. Τα caught markers χρειάζονται ενημέρωση scale μόνο όσο διαρκεί το pulse και μία επαναφορά στο τέλος. Δεν χρειάζεται συνεχής επανεγγραφή όλων των παλιών markers μετά τον γύρο.

Αναμενόμενο όφελος: λιγότερα allocations και UI rebuild work. Έλεγχος με GC Alloc και Canvas.BuildBatch markers σε development build, ιδίως μετά από πολλά catches.

### 5. Pooling για τις gameplay μπάλες

Ο [BallLauncher](Assets/KINOVR/Scripts/BallLauncher.cs#L69) κάνει Instantiate, TMP setup και Destroy ανά μπάλα, με spawn interval 0,5–1,2 s και lifetime 6 s. Αυτό είναι περισσότερο υποψήφιο για μικρά spikes παρά για το κύριο συνεχές bottleneck.

Προτείνεται prewarmed pool περίπου 16 μπαλών, προσαρμοσμένο αν αλλάξει ο ρυθμός εκτόξευσης. Απαραίτητα resets: `caught`, round/number/viewer, Rigidbody velocity/rotation και expiry deadline. Το σημερινό delayed `Destroy` πρέπει να αντικατασταθεί με ασφαλές lifetime ανά ενεργοποίηση, για να μην καταστρέψει επαναχρησιμοποιημένη μπάλα. Οι υπάρχοντες έλεγχοι catch, deadline, restart και διπλού hand trigger πρέπει να εξακολουθήσουν να περνούν.

### 6. Διαφάνειες και board shader — δεύτερος γύρος GPU ελέγχου

Η σκηνή έχει 14 transparent tube surfaces και 2 transparent lottery domes με URP Lit. Δεν θεωρούνται αυθαίρετα διπλότυπα προς διαγραφή: πιθανόν εξυπηρετούν εσωτερικό/εξωτερικό κέλυφος. Χρειάζεται σύγκριση από τις πραγματικές οπτικές γωνίες του παίκτη και έλεγχος overdraw. Η λοταρία έχει 6.048 τρίγωνα ανά κέλυφος.

Ο [KinoBoardGraphic.shader](Assets/KINOVR/Shaders/KinoBoardGraphic.shader#L48) υπολογίζει πολλές `exp`, `pow`, `sin` ανά fragment για φώτα και ribbon. Αν το board καταλαμβάνει μεγάλο μέρος της εικόνας και αποδειχθεί ακριβό, δοκιμάζουμε baked gradients/LUT και απλούστερη κινούμενη μάσκα διατηρώντας το ύφος. Προτεραιότητα χαμηλότερη από τις μπάλες· χωρίς GPU capture δεν αποδίδεται αυθαίρετα μεγάλο κόστος στον shader.

### 7. Δευτερεύοντα CPU / memory θέματα

- Το `KinoAirChamber` κάνει 8 περάσματα pairwise contacts στα 60 Hz. Για τις 14 lottery balls είναι 43.680 pair tests/s· σε idle, οι 7 σωλήνες των 9 μπαλών ανεβάζουν το σύνολο σε 164.640. Οι σωλήνες σταματούν στη διάρκεια του γύρου, άρα το idle πρέπει επίσης να μετρηθεί. Αν εμφανιστεί στον CPU profiler: 30 Hz simulation με interpolation ή επαφές γειτονικών μπαλών στους σωλήνες, με επανέλεγχο containment/overlap.
- Οι τέσσερις marble maps είναι 2048² ASTC 4×4, περίπου 21,3 MiB μαζί στην Android editor απογραφή. Δοκιμή ASTC 6×6 μπορεί να εξοικονομήσει περίπου 11,8 MiB, με έλεγχο banding/normal detail. Δεν είναι η πρώτη προτεραιότητα όσο η μνήμη δεν πιέζεται.
- Το 4K HDRI αντιστοιχεί περίπου σε 10,7 MiB στην τρέχουσα Android import απογραφή. Μόνο προαιρετική δοκιμή χαμηλότερης ανάλυσης, αφού προτεραιότητα είναι η εικόνα. Οι αριθμοί texture residency του Editor δεν είναι μέτρηση συνολικής μνήμης στη συσκευή· η σχετική καταγραφή υπάρχει στο `device-memory.txt`.
- Η collision matrix επιτρέπει όλα τα layer pairs. Μελλοντικός διαχωρισμός Ball/Hand/Environment βοηθά στον έλεγχο ανεπιθύμητων επαφών. Η παρούσα σκηνή έχει λίγα runtime physics objects· δεν προτείνεται τυφλή υποβάθμιση continuous collision ή αύξηση physics frequency.

## Τι είναι ήδη σωστά ρυθμισμένο

Vulkan, IL2CPP ARM64, OpenXR single-pass, Forward renderer, SRP Batcher, MSAA 4×, render scale 1, depth/opaque textures off, χωρίς SSAO renderer feature και χωρίς depth priming. Το ενεργό URP global asset έχει compatibility mode off. Υπάρχει κυρίως baked lighting και custom cubemap probes, χωρίς realtime probe captures. Η σκόνη έχει cap 128 particles και τα πουλιά ένα μικρό combined mesh· δεν είναι οι πρώτοι στόχοι.

**Διόρθωση της αρχικής υπόθεσης HDR/bloom:** το Quest pipeline επιτρέπει HDR, αλλά η `CenterEyeAnchor` έχει `m_HDR: 0` και `m_RenderPostProcessing: 0`. Το `KinoPlayerView.Awake` ενεργοποιεί το VR rig και απενεργοποιεί τις δύο preview cameras. Το URP υπολογίζει HDR ως `camera.allowHDR && pipeline.supportsHDR`. Επομένως **δεν τεκμηριώνεται ενεργό HDR/bloom στο headset** από την παρούσα σκηνή. Η αφαίρεσή τους από το pipeline δεν πρέπει να παρουσιαστεί ως μετρημένο κέρδος. Αρχικά διατηρούμε 4× MSAA και full render scale.

Το profile ονομάζεται `Quest 3 Development`, αλλά έχει `m_Development: 0`, `m_ConnectProfiler: 0` και Deep Profiling off. Αυτό ταιριάζει με το release APK που μετρήθηκε. Για διερεύνηση χρειάζεται ξεχωριστό profiling profile, όχι ενεργοποίηση Deep Profile στο baseline σύγκρισης.

## Προτεινόμενη σειρά υλοποίησης και επαλήθευσης

1. Decorative mesh 1.024 triangles + UI string caching / pulse updates. Καταγραφή πριν/μετά στην ίδια θέα και στα ίδια στάδια του γύρου.
2. Ήπιο SRP foveation, με επιβεβαίωση ενεργοποίησης και έλεγχο ποιότητας στα άκρα της εικόνας.
3. Grouping των σταθερών ornaments και atlas για τους αριθμούς, με πραγματική μέτρηση batches/SetPass/CPU render thread.
4. Pooling και, μόνο όπου δείξει κόστος ο profiler, glass/board shader ή air simulation.
5. Τελική release συνεδρία 15–20 λεπτών, με επαναλαμβανόμενους γύρους, πολλά catches, στροφές κεφαλής και idle. Στόχος 72 Hz χωρίς παρατεταμένα missed/stale frames. Χρήσιμος εσωτερικός στόχος είναι CPU και GPU p95 χωριστά κάτω από περίπου 11–12 ms, ώστε να υπάρχει περιθώριο κάτω από τα 13,89 ms· αυτό απαιτεί κατάλληλα per-frame metrics, δεν προκύπτει από το παρόν logcat.

Δεν υπάρχει τεκμηριωμένο ποσοστό αύξησης FPS πριν εφαρμοστούν και μετρηθούν οι αλλαγές. Η μεγαλύτερη ασφαλής αριθμητική ευκαιρία που αποδεικνύεται σήμερα είναι η μείωση της γεωμετρίας των διακοσμητικών μπαλών.
