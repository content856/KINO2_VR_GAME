"""Capture only this app's VrApi/Unity logs; does not change device settings."""
import argparse
import datetime
import json
import pathlib
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument('--adb', required=True)
parser.add_argument('--seconds', type=int, default=90)
parser.add_argument('--output', default='Artifacts/Quest3Performance')
args = parser.parse_args()
package = 'com.UnityTechnologies.com.unity.template.urpblank'
out = pathlib.Path(args.output)
out.mkdir(parents=True, exist_ok=True)
def adb(*commands):
    return subprocess.check_output([args.adb, *commands], text=True, errors='replace', timeout=20).strip()
pid = adb('shell', 'pidof', package).split()[0]
metadata = {'package': package, 'pid': pid, 'startUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'requestedSeconds': args.seconds}
started = time.monotonic()
timed_out = False
with (out / 'quest3-live.log').open('w', encoding='utf-8') as log, (out / 'adb-capture-errors.log').open('w', encoding='utf-8') as err:
    process = subprocess.Popen([args.adb, 'logcat', '-v', 'threadtime', '--pid=' + pid, '-T', '1', 'VrApi:I', 'Unity:W', '*:S'], stdout=log, stderr=err)
    try:
        process.wait(timeout=args.seconds)
    except subprocess.TimeoutExpired:
        timed_out = True
        process.terminate()
        process.wait(timeout=10)
metadata['endUtc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
metadata['elapsedSeconds'] = round(time.monotonic() - started, 3)
metadata['completedRequestedDuration'] = timed_out
metadata['logcatExitCode'] = process.returncode
(out / 'capture-metadata.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
print(json.dumps(metadata))
