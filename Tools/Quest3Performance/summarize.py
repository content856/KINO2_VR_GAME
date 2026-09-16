"""Summarize saved scene inventory and per-app VrApi samples, without edits."""
import collections
import csv
import datetime
import json
import math
from pathlib import Path
import re
import statistics

out = Path('Artifacts/Quest3Performance')
inventory = json.loads((out / 'scene-inventory.json').read_text(encoding='utf-8'))
active = [r for r in inventory['renderers'] if r['active'] and r['enabled']]
decorative = [r for r in active if r['meshAsset'].endswith(('KinoOvalBall.asset', 'KinoDecorativeOvalBall.asset'))]
ornaments = [r for r in active if '/Animation_Balls/' in r['path']]
total_triangles = sum(r['triangles'] for r in active)
scene = {
    'scope': 'Saved scene before Play Mode: no frustum culling, no runtime hand meshes or spawned balls, excludes UGUI geometry and particle quads. Renderer counts are not draw calls.',
    'enabledActiveRenderers': len(active),
    'meshTriangles': total_triangles,
    'decorativeBalls': len(decorative),
    'decorativeBallTriangles': sum(r['triangles'] for r in decorative),
    'decorativeBallTriangleSharePercent': round(sum(r['triangles'] for r in decorative) / total_triangles * 100, 2),
    'visibleSeparateOrnaments': len(ornaments),
    'ornamentTriangles': sum(r['triangles'] for r in ornaments),
    'canvases': len(inventory['canvases']),
    'lodGroups': len(inventory['lodGroups']),
    'lightTypes': dict(collections.Counter(l['bakeType'] for l in inventory['lights'] if l['enabled'] and l['active'])),
    'lightmaps': inventory['lightmaps'],
    'reflectionProbes': len(inventory['probes']),
    'colliders': inventory['enabledColliders'],
    'savedSceneRigidbodies': inventory['activeRigidbodies'],
}
patterns = {
    'fps': r'FPS=(\d+)/', 'refreshHz': r'FPS=\d+/(\d+)',
    'stale': r',Stale=(\d+)', 'tear': r',Tear=(\d+)',
    'foveation': r',Fov=(\d+)', 'appReportedMs': r',App=([\d.]+)ms',
    'combinedReportedMs': r',CPU&GPU=([\d.]+)ms',
    'gpuUtilization': r',GPU%=([\d.]+)', 'cpuUtilization': r',CPU%=([\d.]+)',
    'temperatureC': r',Temp=([\d.]+)',
}
def stats(values):
    values = sorted(values)
    if not values:
        return None
    return {'min': min(values), 'median': statistics.median(values), 'mean': round(statistics.mean(values), 4), 'p95': values[math.ceil(len(values) * .95) - 1], 'max': max(values)}

def summarize_log(folder):
    rows = []
    for line in (folder / 'quest3-live.log').read_text(encoding='utf-8').splitlines():
        if 'VrApi' not in line or 'FPS=' not in line:
            continue
        row = {'deviceTimestamp': line[:18].strip()}
        for name, pattern in patterns.items():
            match = re.search(pattern, line)
            if match:
                row[name] = float(match.group(1))
        rows.append(row)
    if rows:
        start = datetime.datetime.strptime(rows[0]['deviceTimestamp'], '%m-%d %H:%M:%S.%f')
        for row in rows:
            timestamp = datetime.datetime.strptime(row['deviceTimestamp'], '%m-%d %H:%M:%S.%f')
            row['secondsSinceFirstSample'] = (timestamp - start).total_seconds()
        with (folder / 'metrics.csv').open('w', encoding='utf-8', newline='') as csvfile:
            writer = csv.DictWriter(csvfile, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
    steady = [r for r in rows if r['secondsSinceFirstSample'] >= 10]
    def group_summary(samples):
        return {'samples': len(samples), 'stats': {name: stats([r[name] for r in samples if name in r]) for name in patterns}, 'staleTotal': sum(r.get('stale', 0) for r in samples), 'tearTotal': sum(r.get('tear', 0) for r in samples), 'samplesBelow72Fps': sum(r.get('fps', 72) < 72 for r in samples)}
    return {'source': str(folder / 'quest3-live.log'), 'firstTimestamp': rows[0]['deviceTimestamp'] if rows else '', 'lastTimestamp': rows[-1]['deviceTimestamp'] if rows else '', 'sampleSpanSeconds': rows[-1]['secondsSinceFirstSample'] if rows else 0, 'all': group_summary(rows), 'afterFirst10Seconds': group_summary(steady), 'limitation': 'About one aggregate per second; percentile is of sampled aggregate values, not individual-frame timings. App and CPU&GPU are raw VrApi fields, not isolated Unity main-thread or GPU timings. Gameplay state and view direction are not instrumented.'}
summary = {'scene': scene, 'initialCapture': summarize_log(out / 'initial'), 'freshLaunchCapture': summarize_log(out)}
(out / 'summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(summary, ensure_ascii=False, indent=2))
