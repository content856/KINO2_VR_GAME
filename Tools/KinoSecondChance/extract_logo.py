"""Render the supplied vector logo, preserving its white letter faces and alpha."""
import sys
from pathlib import Path
import pymupdf
from PIL import Image

source, output = map(Path, sys.argv[1:3])
document = pymupdf.open(source)
page = document[0]
pixmap = page.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=True)
image = Image.frombytes("RGBA", (pixmap.width, pixmap.height), pixmap.samples)
# PyMuPDF returns premultiplied alpha. Unmultiply antialiased boundary pixels.
import numpy as np
pixels = np.asarray(image).copy()
alpha = pixels[:, :, 3:4].astype(float)
pixels[:, :, :3] = np.minimum(255, pixels[:, :, :3].astype(float) * 255 / np.maximum(alpha, 1)).astype("uint8")
image = Image.fromarray(pixels)
bounds = image.getbbox()
image = image.crop((max(0, bounds[0] - 12), max(0, bounds[1] - 12), min(image.width, bounds[2] + 12), min(image.height, bounds[3] + 12)))
output.parent.mkdir(parents=True, exist_ok=True)
image.save(output)
print(f"{output}: {image.size}, alpha preserved")
