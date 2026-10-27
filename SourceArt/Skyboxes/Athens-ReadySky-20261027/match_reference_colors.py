"""Color-only grade of the ready-made sky composite, plus an aligned comparison."""
from pathlib import Path
import argparse
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageChops

Image.MAX_IMAGE_PIXELS = None
ROOT = Path(__file__).resolve().parent
REFERENCE = ROOT.parent / 'Athens120m-20261027/reference-preview.png'


def smooth(a, b, x):
    t = np.clip((x-a)/(b-a), 0, 1)
    return t*t*(3-2*t)


def rgb_to_hsv(rgb):
    maximum = rgb.max(axis=-1)
    minimum = rgb.min(axis=-1)
    delta = maximum-minimum
    safe = np.maximum(delta, 1e-6)
    r, g, b = np.moveaxis(rgb, -1, 0)
    hue = np.where(maximum == r, (g-b)/safe % 6,
                   np.where(maximum == g, (b-r)/safe+2, (r-g)/safe+4))/6
    hue = np.where(delta > 1e-6, hue, 0)
    return hue, delta/np.maximum(maximum, 1e-6), maximum


def hsv_to_rgb(h, s, v):
    k = (h[..., None]*6 + np.array([5, 3, 1], dtype=np.float32)) % 6
    return v[..., None]*(1-s[..., None]*np.clip(np.minimum(k, 4-k), 0, 1))


def grade(rgb, sky_weight):
    h, s, v = rgb_to_hsv(rgb)
    blue = smooth(.27, .46, s)*smooth(.48, .55, h)*(1-smooth(.64, .72, h))
    # Match the reference's cyan-blue sky while keeping neutral cloud highlights.
    sh = (h-blue*14/360) % 1
    ss = s + blue*(.985-s)*.90
    sv = v*(1-.18*blue)
    sky = hsv_to_rgb(sh, ss, sv)
    lum = rgb @ np.array([.2126,.7152,.0722], dtype=np.float32)
    cloud = (1-blue)*(1-smooth(.25,.65,s))
    contrast = np.interp(lum, [0,.2,.4,.6,.8,1], [0,.16,.34,.57,.82,1]).astype(np.float32)
    sky *= (1 + cloud[...,None]*(contrast/np.maximum(lum,.001)-1)[...,None])
    warmth = cloud*smooth(.32,.80,lum)
    sky *= 1 + warmth[...,None]*np.array([.045,-.035,-.075],dtype=np.float32)

    # City: stronger tonal separation, cool shadows and sunlit golden highlights.
    city = hsv_to_rgb(h, np.clip(s*1.18,0,1),v)
    target_lum = np.interp(lum,[0,.12,.30,.50,.70,.88,1],
                               [0,.095,.265,.49,.715,.89,1]).astype(np.float32)
    city *= (target_lum/np.maximum(lum,.001))[...,None]
    shadows = (1-smooth(.32,.74,lum))*smooth(.035,.20,lum)
    highlights = smooth(.48,.83,lum)*(1-smooth(.93,1,lum))
    city += shadows[...,None]*np.array([-.045,.009,.087],dtype=np.float32)
    city += highlights[...,None]*np.array([.065,-.005,-.050],dtype=np.float32)
    # Residual white balance measured from low-chroma architecture in both images,
    # conditioned on luminance; leaves saturated foliage and sunlit accents intact.
    neutral = 1-smooth(.35,.65,s)
    anchors = [0,.15,.25,.35,.45,.55,.65,.75,.90,1]
    red_delta = np.interp(lum,anchors,[0,3,4,-2,-8,-14,-14,-8,0,0])/255
    blue_delta = np.interp(lum,anchors,[0,0,2,6,14,23,23,10,0,0])/255
    city[...,0] += neutral*red_delta
    city[...,2] += neutral*blue_delta
    out = sky*sky_weight[...,None]+city*(1-sky_weight[...,None])
    return np.uint8(np.rint(np.clip(out,0,1)*255))


def comparison(new, name):
    reference = Image.open(REFERENCE).convert('RGB')
    new = new.copy()
    new.thumbnail((3840,1920),Image.Resampling.LANCZOS)
    # Align sunlight in yaw and match the old cropped panorama's 93.647-degree
    # vertical coverage. The actual output retains every original pixel position.
    new = ImageChops.offset(new, round((.5560302734-.914)*new.width),0)
    crop_top = (90-63.188905)/180*new.height
    crop_bottom = (90+30.458555)/180*new.height
    aligned = new.crop((0,round(crop_top),new.width,round(crop_bottom)))
    aligned = aligned.resize(reference.size,Image.Resampling.LANCZOS)
    board = Image.new('RGB',(3840,1660),(18,23,29))
    draw = ImageDraw.Draw(board)
    font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',42)
    small = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',26)
    draw.text((42,24),'ΠΡΟΗΓΟΥΜΕΝΟ SKYBOX',font=font,fill=(244,245,247))
    draw.text((1962,24),'ΝΕΟ — ΤΑΙΡΙΑΣΜΕΝΕΣ ΑΠΟΧΡΩΣΕΙΣ',font=font,fill=(244,245,247))
    for col, im in enumerate([reference, aligned]):
        x = col*1920
        strip = im.resize((1880,489),Image.Resampling.LANCZOS)
        board.paste(strip,(x+20,100))
        # The same 169-degree view around the sun, enlarged for color comparison.
        detail = im.crop((1200,0,3000,999)).resize((1880,1043),Image.Resampling.LANCZOS)
        board.paste(detail,(x+20,610))
    board.save(ROOT/(name+'-side-by-side.jpg'),quality=95,subsampling=0)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--final',action='store_true')
    args=parser.parse_args()
    name='Athens-ColorMatched'
    base=Image.open(ROOT/'Athens-ReadySky-14400x7200.png').convert('RGB')
    if not args.final:
        base=base.resize((3840,1920),Image.Resampling.LANCZOS)
    width,height=base.size
    trace=np.array(json.loads((ROOT.parent/'Athens120m-20261027/skyline.json').read_text()),dtype=np.float32)
    horizon=np.interp((np.arange(width)+.5)*3840/width-.5,np.arange(3840),trace)*width/3840
    result=Image.new('RGB',base.size)
    clipped=0
    for top in range(0,height,96):
        bottom=min(top+96,height)
        rgb=np.asarray(base.crop((0,top,width,bottom))).astype(np.float32)/255
        distance=np.arange(top,bottom)[:,None]+.5-horizon[None,:]
        sky_weight=1-smooth(-6*width/14400,12*width/14400,distance)
        output=grade(rgb,sky_weight)
        clipped+=int(np.any((output==0)|(output==255),axis=-1).sum())
        result.paste(Image.fromarray(output),(0,top))
    if args.final:
        output=ROOT/(name+'-14400x7200.png')
        result.save(output,compress_level=6)
    preview=result.copy()
    preview.thumbnail((3840,1920),Image.Resampling.LANCZOS)
    preview.save(ROOT/(name+'-preview.jpg'),quality=95,subsampling=0)
    comparison(preview,name)
    stats=dict(dimensions=[width,height],anyChannelAtLimitFraction=clipped/(width*height),
               reference=str(REFERENCE),source='Athens-ReadySky-14400x7200.png',
               operation='Color transformations only; no geometric changes or generated pixels.',
               output=name+'-14400x7200.png' if args.final else name+'-preview.jpg')
    (ROOT/(name+'-color-metadata.json')).write_text(json.dumps(stats,indent=2),encoding='utf-8')
    print(json.dumps(stats))


if __name__=='__main__':
    main()
