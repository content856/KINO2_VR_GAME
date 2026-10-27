"""Non-generative sky replacement, preserving the supplied city at native coordinates."""
from pathlib import Path
import argparse
import hashlib
import json
import numpy as np
from PIL import Image, ImageChops, ImageEnhance

Image.MAX_IMAGE_PIXELS = None
ROOT = Path(__file__).resolve().parent
CITY = Path('C:/Users/Yannis/OneDrive/Υπολογιστής/athensNoSy.png')
TRACE = ROOT.parent / 'Athens120m-20261027/skyline.json'


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for chunk in iter(lambda: f.read(1048576), b''):
            h.update(chunk)
    return h.hexdigest()


def composite(args):
    width = args.width
    height = width // 2
    native = Image.open(CITY)
    city = native.convert('RGB')
    original_alpha = native.getchannel('A')
    if city.size != (width, height):
        city = city.resize((width, height), Image.Resampling.LANCZOS)
        original_alpha = original_alpha.resize((width, height), Image.Resampling.LANCZOS)

    sky_path = ROOT / (args.sky + '.jpg')
    sky = Image.open(sky_path).convert('RGB')
    # A cyclic yaw preserves the panorama wrap and all photographed cloud detail.
    sky = ImageChops.offset(sky, round((args.target_sun_x - args.source_sun_x) * sky.width), 0)
    # Put the captured sun at the original photograph's elevation, without tilting the city.
    vertical_scale = args.target_sun_y / args.source_sun_y
    sky = sky.resize((width, round(height * vertical_scale)), Image.Resampling.LANCZOS)
    sky = sky.crop((0, 0, width, height))

    if args.saturation != 1:
        sky = ImageEnhance.Color(sky).enhance(args.saturation)
    if args.contrast != 1:
        sky = ImageEnhance.Contrast(sky).enhance(args.contrast)

    trace = np.asarray(json.loads(TRACE.read_text()), dtype=np.float32)
    horizon = np.interp((np.arange(width) + .5) * len(trace) / width - .5,
                        np.arange(len(trace)), trace) * width / len(trace)
    # A four-native-pixel transition removes only the cut edge. The opaque city
    # uses its original RGB, since the supplied alpha also makes city haze translucent.
    feather = 4 * width / 14400
    mask = Image.new('L', (width, height))
    for top in range(0, height, 128):
        bottom = min(height, top + 128)
        coverage = np.clip((np.arange(top, bottom)[:, None] + .5 - horizon[None, :]) / feather + .5, 0, 1)
        distance = np.arange(top, bottom)[:, None] + .5 - horizon[None, :]
        original = np.asarray(original_alpha.crop((0, top, width, bottom))).astype(np.float32) / 255
        # Keep the supplied antialias and cutout holes at the edge. Its slightly
        # translucent city haze below this band should not admit replacement sky.
        restore_opaque = np.clip((distance - 50 * width / 14400) / (30 * width / 14400), 0, 1)
        coverage = np.minimum(coverage, original + (1 - original) * restore_opaque)
        chunk = np.uint8(np.rint(coverage * 255))
        mask.paste(Image.fromarray(chunk), (0, top))
    result = Image.composite(city, sky, mask)
    suffix = args.name or args.sky
    if args.final:
        out = ROOT / ('Athens-' + suffix + '-14400x7200.png')
        result.save(out, compress_level=6)
    else:
        out = ROOT / ('composite-' + suffix + '.jpg')
        result.save(out, quality=94, subsampling=0)
    preview = result.copy()
    preview.thumbnail((3840, 1920), Image.Resampling.LANCZOS)
    preview.save(ROOT / ('Athens-' + suffix + '-preview.jpg'), quality=94, subsampling=0)

    # Native image equality check for every pixel beyond the feathered skyline.
    preserved = changed = 0
    for top in range(0, height, 128):
        bottom = min(height, top + 128)
        opaque = np.asarray(mask.crop((0, top, width, bottom))) == 255
        a = np.asarray(city.crop((0, top, width, bottom)))
        b = np.asarray(result.crop((0, top, width, bottom)))
        preserved += int(opaque.sum())
        changed += int(np.any(a != b, axis=2)[opaque].sum())

    for label, box in [
        ('horizon-left', (0, int(height*.445), width//2, int(height*.54))),
        ('horizon-right', (width//2, int(height*.445), width, int(height*.54))),
    ]:
        detail = result.crop(box)
        detail.thumbnail((2560, 900), Image.Resampling.LANCZOS)
        detail.save(ROOT / (suffix + '-' + label + '.jpg'), quality=96, subsampling=0)

    metadata = dict(
        output=str(out), dimensions=[width, height], citySource=str(CITY),
        skySource=str(sky_path), skyPage='https://polyhaven.com/a/' + args.sky,
        skyLicense='CC0: https://polyhaven.com/license',
        method='Photographic sky asset, cyclic yaw, vertical sun alignment, sky-only color adjustment, geographic skyline matte; no image generation.',
        sourceSkyDimensions=Image.open(sky_path).size,
        cityOpaquePixelsChecked=preserved, changedCityOpaquePixels=changed,
        parameters=vars(args),
    )
    if args.final:
        metadata.update(citySha256=digest(CITY), skySha256=digest(sky_path), outputSha256=digest(out))
    (ROOT / ('verification-' + suffix + '.json')).write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(metadata, ensure_ascii=True))


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--sky', required=True)
    p.add_argument('--source-sun-x', type=float, required=True)
    p.add_argument('--source-sun-y', type=float, required=True)
    p.add_argument('--target-sun-x', type=float, default=.914)
    p.add_argument('--target-sun-y', type=float, default=.465)
    p.add_argument('--saturation', type=float, default=1.1)
    p.add_argument('--contrast', type=float, default=1.04)
    p.add_argument('--width', type=int, default=3840)
    p.add_argument('--name')
    p.add_argument('--final', action='store_true')
    composite(p.parse_args())
