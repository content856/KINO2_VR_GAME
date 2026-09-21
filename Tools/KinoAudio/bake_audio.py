"""Offline first-pass KINO sound design. Never imported or executed by Unity.

Requires NumPy. Writes original PCM WAVs, a manifest and a listening reel.
Re-run explicitly; replace individual WAVs in Unity to art-direct the next pass.
"""
from pathlib import Path
import json
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/KINOVR/Audio/Baked"
PREVIEW = ROOT / "Artifacts/KinoGameplay/Audio"
SR = 48000
RNG = np.random.default_rng(20260921)
FILES = []
AUDIO = {}


def clock(seconds):
    return np.arange(round(seconds * SR)) / SR


def noise(seconds, low=150, high=4500):
    n = round(seconds * SR)
    f = np.fft.rfftfreq(n, 1 / SR)
    spectrum = np.fft.rfft(RNG.normal(size=n))
    spectrum *= (1 - np.exp(-(f / low) ** 4)) * np.exp(-(f / high) ** 4)
    x = np.fft.irfft(spectrum, n)
    return x / max(np.sqrt(np.mean(x * x)), 1e-9)


def sweep(seconds, start, end, decay=15):
    t = clock(seconds)
    f = end + (start - end) * np.exp(-decay * t)
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


def bell(seconds, frequency, decay=7):
    t = clock(seconds)
    return sum(gain * np.sin(2 * np.pi * frequency * partial * t) * np.exp(-decay * damping * t)
               for partial, gain, damping in [(1, 1, 1), (2, .25, 1.5), (3.01, .08, 2.5)]) * (1 - np.exp(-700 * t))


def add(dst, clip, seconds, gain=1, pan=0, wrap=False):
    start = round(seconds * SR)
    clip = clip * gain
    if dst.ndim == 2 and clip.ndim == 1:
        clip = np.column_stack((clip * np.sqrt((1 - pan) / 2), clip * np.sqrt((1 + pan) / 2)))
    if wrap:
        start %= len(dst)
        n = min(len(clip), len(dst) - start)
        dst[start:start+n] += clip[:n]
        if n < len(clip):
            dst[:len(clip)-n] += clip[n:]
    else:
        n = min(len(clip), len(dst) - start)
        if n > 0:
            dst[start:start+n] += clip[:n]


def save(name, x, description, loop=False, peak_db=-6):
    x = x - np.mean(x, axis=0)
    if not loop:
        fade = np.ones(len(x))
        a, b = min(96, len(x)//4), min(960, len(x)//4)
        fade[:a] = np.linspace(0, 1, a)
        fade[-b:] = np.linspace(1, 0, b)
        x *= fade[:, None] if x.ndim == 2 else fade
    x *= 10 ** (peak_db / 20) / max(np.max(np.abs(x)), 1e-9)
    pcm = np.round(x * 32767).astype('<i2')
    with wave.open(str(OUT / (name + '.wav')), 'wb') as w:
        w.setnchannels(1 if x.ndim == 1 else x.shape[1])
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    AUDIO[name] = x
    FILES.append(dict(name=name, seconds=round(len(x) / SR, 4), channels=1 if x.ndim == 1 else 2,
                      loop=loop, peak_db=peak_db, description=description))


def air(seconds, weight=.6):
    t = clock(seconds)
    env = np.sin(np.pi * t / seconds) ** 1.5
    return noise(seconds, 220, 4200) * env * (.7 + .3 * np.sin(2 * np.pi * 3.2 * t)) * weight


def catch(seconds, base, rich=0):
    t = clock(seconds)
    x = .75 * sweep(seconds, 1050, 230) * np.exp(-32 * t)
    x += .1 * noise(seconds, 700, 5500) * np.exp(-70 * t)
    add(x, bell(max(.1, seconds - .025), base, 12 if rich == 0 else 7), .025, .38)
    if rich:
        add(x, bell(seconds - .07, base * 1.5, 8), .07, .23)
        add(x, bell(seconds - .115, base * 2, 9), .115, .14)
    if rich == 2:
        x += .7 * sweep(seconds, 165, 78, 18) * np.exp(-12 * t)
    return x


def chime(notes, seconds=1.3):
    x = np.zeros(round(seconds * SR))
    for i, freq in enumerate(notes):
        add(x, bell(seconds - i * .105, freq, 4.8), i * .105, .6 ** (i / 5))
    return x


def bake_effects():
    t = clock(1.65)
    x = air(1.65) * np.linspace(.15, 1, len(t))
    add(x, chime([523.25, 783.99], .55), 1.0, .18)
    save('RoundStart', x, 'Air pressure rises, followed by a restrained ready chime.')
    for i in range(3):
        t = clock(1.8)
        x = noise(1.8, 120, 2000 + 200*i) * np.sin(np.pi * t / 1.8) ** .8
        x *= .7 + .15 * np.sin(2*np.pi*(5+i*.3)*t)
        save(f'TubeRise_{i+1:02}', x, 'Soft pressure inside a tube during ascent.', peak_db=-10)
        t = clock(.27)
        x = noise(.27, 170, 3300) * (1-np.exp(-200*t)) * np.exp(-22*t)
        x += .7 * sweep(.27, 550+30*i, 140) * np.exp(-32*t)
        save(f'TubeExit_{i+1:02}', x, 'Compact pneumatic puff/pop at the outlet.')
        save(f'BallWhoosh_{i+1:02}', air(.52+i*.025), 'Short passing airflow; follows the approaching ball.', peak_db=-9)
        save(f'CatchNormal_{i+1:02}', catch(.34, [1046.5, 1174.66, 1318.51][i]), 'Soft golden pop and tiny coin sparkle.')
        save(f'CatchGreen_{i+1:02}', catch(.48, [1567.98, 1760, 2093][i], 1), 'Bright glassy green reward; lighter than the red bonus.')
        save(f'CatchBoost_{i+1:02}', catch(.32, [783.99, 880, 1046.5][i], 1), 'Short energetic reward for dense Boost catches.')
        t = clock(.22)
        x = .6*sweep(.22, 340, 110)*np.exp(-36*t) + .12*noise(.22, 500, 3800)*np.exp(-55*t)
        save(f'BallMiss_{i+1:02}', x, 'Gentle floor tap, no failure buzzer.', peak_db=-12)
    save('CatchBonus', catch(.92, 1046.5, 2), 'Full red bonus pop, rounded low impact and reward sparkle.', peak_db=-5)
    save('BonusReveal', chime([523.25, 659.25, 1046.5], 1.15), 'Short invitation announcing the red bonus.')
    save('SecondChanceOut', air(1.0) * np.linspace(.15, 1, SR), 'Air sweep into the one-second blackout.', peak_db=-9)
    save('SecondChanceReveal', chime([587.33, 880, 1174.66], 1.45), 'Fresh, ascending logo reveal.')
    x = air(1.35)
    add(x, chime([261.63, 523.25, 783.99, 1046.5], 1.05), .22, .5)
    save('BoostStart', x, 'Warm power-up under the existing Boost voice.', peak_db=-8)
    save('BoostEnd', chime([1046.5, 783.99, 523.25], .85), 'Gentle release from Boost energy.', peak_db=-10)
    save('RoundComplete', chime([523.25, 659.25, 783.99, 1046.5], 1.7), 'Small positive resolution, not a jackpot fanfare.')
    save('Restart', catch(.16, 1318.51) * .65, 'Short, clear button confirmation.', peak_db=-9)
    save('Countdown', bell(.13, 880, 28), 'Soft last-five-seconds marker.', peak_db=-14)
    for i in range(4):
        x = np.zeros(round((1.05 + i*.16)*SR))
        for k in range(3 + i % 2):
            dur = .085 + .025*RNG.random()
            t = clock(dur)
            f = 2300 + i*290 + 1000*np.sin(np.pi*t/dur) + 180*np.sin(2*np.pi*45*t)
            chirp = np.sin(2*np.pi*np.cumsum(f)/SR) * np.sin(np.pi*t/dur)**1.5
            add(x, chirp, .04 + k*(.19 + .018*i), .65**k)
        save(f'Bird_{i+1:02}', x, 'Stylized distant bird phrase, baked offline.', peak_db=-13)


def bake_loops():
    dur = 12
    t = clock(dur)
    wind = np.column_stack((noise(dur, 100, 2200), noise(dur, 100, 2200)))
    wind *= (.58 + .22*np.sin(2*np.pi*t/dur) + .1*np.cos(2*np.pi*3*t/dur))[:, None]
    save('OutdoorAmbienceLoop', wind, 'Soft stereo courtyard breeze; birds are separate clips.', True, -13)
    dur = 8
    t = clock(dur)
    x = noise(dur, 90, 1500) * (.65+.15*np.sin(2*np.pi*3*t/dur))
    x += .18*np.sin(2*np.pi*72*t) + .05*np.sin(2*np.pi*144*t)
    for i in range(21):
        tick = bell(.065, 600 + RNG.random()*1100, 75)
        add(x, tick, RNG.uniform(0, dur), .075, wrap=True)
    save('MachineLoop', x, 'Quiet air motor with soft chamber contacts.', True, -13)

    # Original 8-bar / 120 BPM instrumental. All tails wrap into the next cycle.
    dur = 16
    music = np.zeros((SR*dur, 2))
    chords = [(50, 53, 57, 60), (46, 50, 53, 57), (53, 57, 60, 64), (48, 52, 55, 60)]
    def hz(midi): return 440 * 2 ** ((midi-69)/12)
    for c, chord in enumerate(chords):
        t = clock(4.8)
        pad = np.zeros(len(t))
        for midi in chord:
            f = hz(midi+12)
            pad += np.sin(2*np.pi*f*t) + .23*np.sin(2*np.pi*f*2*t)
        pad *= (1-np.exp(-t*3)) * np.minimum(1, (4.8-t)/1.0)
        add(music, pad, c*4, .021, -.18 if c%2 else .18, True)
        for step in range(16):
            midi = chord[[0, 2, 1, 3, 2, 1, 3, 2][step%8]] + 24
            note = bell(.62, hz(midi), 10)
            at = c*4 + step*.25
            add(music, note, at, .056 if step%2 == 0 else .038, -.4 if step%2 else .4, True)
            add(music, note, at+.375, .013, .4 if step%2 else -.4, True)
        for step in range(8):
            t = clock(.43)
            f = hz(chord[0]-12)
            bass = (np.sin(2*np.pi*f*t)+.2*np.sin(2*np.pi*2*f*t))*(1-np.exp(-120*t))*np.exp(-7*t)
            add(music, bass, c*4+step*.5+.03, .20, 0, True)
    for beat in range(32):
        t = clock(.3)
        kick = sweep(.3, 135, 46, 32) * np.exp(-18*t) * (1-np.exp(-600*t))
        add(music, kick, beat*.5, .29, 0, True)
        if beat%2:
            t = clock(.15)
            clap = noise(.15, 800, 7000)*np.exp(-35*t)*(1-np.exp(-900*t))
            add(music, clap, beat*.5, .054, .06, True)
    for step in range(64):
        t = clock(.10)
        hat = noise(.10, 6000, 13000)*np.exp(-55*t)*(1-np.exp(-1000*t))
        add(music, hat, step*.25, .024 if step%2 else .016, -.25 if step%2 else .25, True)
    save('BoostMusicLoop', music, 'Original warm electronic instrumental, 120 BPM, D minor / Bb / F / C, 8 bars.', True, -5)


def preview():
    names = ['RoundStart', 'TubeRise_01', 'TubeExit_01', 'BallWhoosh_01', 'CatchNormal_01',
             'CatchNormal_02', 'CatchGreen_01', 'BonusReveal', 'CatchBonus', 'BallMiss_01',
             'SecondChanceOut', 'SecondChanceReveal', 'BoostStart', 'CatchBoost_01',
             'BoostEnd', 'RoundComplete', 'Restart', 'Countdown', 'Bird_01',
             'OutdoorAmbienceLoop', 'MachineLoop', 'BoostMusicLoop']
    parts, lines, at = [], ['# First-pass listening reel', '', '| Start | Clip |', '|---|---|'], 0.
    for name in names:
        x = AUDIO[name]
        if name.endswith('Loop'):
            x = x[:SR*(8 if name == 'BoostMusicLoop' else 4)].copy()
            fade = np.linspace(1, 0, SR//4)
            x[-len(fade):] *= fade[:, None] if x.ndim == 2 else fade
        if x.ndim == 1: x = np.column_stack((x, x))
        lines.append(f'| {int(at)//60:02}:{at%60:05.2f} | {name} |')
        parts.extend((x, np.zeros((round(.55*SR), 2))))
        at += len(x)/SR + .55
    x = np.concatenate(parts)
    with wave.open(str(PREVIEW / 'KinoAudio_FirstPass.wav'), 'wb') as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(np.round(x*32767).astype('<i2').tobytes())
    (PREVIEW / 'ListeningGuide.md').write_text('\n'.join(lines)+'\n', encoding='utf-8')


if __name__ == '__main__':
    OUT.mkdir(parents=True, exist_ok=True)
    PREVIEW.mkdir(parents=True, exist_ok=True)
    bake_effects()
    bake_loops()
    (OUT / 'manifest.json').write_text(json.dumps(FILES, indent=2)+'\n', encoding='utf-8')
    preview()
    print(f'Baked {len(FILES)} original WAVs. Listening reel: {PREVIEW / "KinoAudio_FirstPass.wav"}')
