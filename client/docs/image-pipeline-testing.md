# Image pipeline verification

The client and tools use StbImageSharp for decoding and StbImageWriteSharp for
PNG encoding. Screenshots retain top-to-bottom RGBA pixels, file naming, and
atomic temporary-file replacement. PNG compression and metadata can differ;
decoded pixel values are the compatibility contract.

## Short client check

1. Log in with existing prepared content. Walk outside and into a building;
   check terrain, walls, trees and transparent textures for missing or changed art.
2. Open inventory and MossTank. Check item icons, spell icons, plugin sidebar
   icons and transparent edges. Check the client icon in the taskbar/title bar.
3. Take a screenshot using your configured screenshot binding. Open the saved
   PNG and compare it with the client: correct colours, orientation, dimensions
   and UI. Resize the window and take another; check that it uses the new size
   and saves a new file.

## Automated coverage

- Image pixel fixtures cover bicubic up/downscaling, nearest-neighbor zoom,
  clipped source-over compositing, transparent RGB/alpha, cropping and PNG round trips.
- Screenshot lifecycle tests cover framebuffer orientation, resize, filenames
  and completion/failure handling. Window-icon tests decode the embedded images.
- A pre-change mesh fixture covers bounds and all seven stored texture formats;
  reading and writing it must produce identical bytes.
- DXT1, DXT3 and DXT5 extraction tests verify decoded channels and surface opacity.
- Existing CLI comparison tests cover tolerance and masks.

For manual tooling parity, run `render-vitals-mockup`, `dump-font-atlas`,
`dump-sprite-sheet`, `mock-selbar`, `export-ui-sprite` and `crop` with the same
inputs on both builds. Compare PNGs using `compare-screenshots old.png new.png
result.json 0 0`. Comparing file hashes is inappropriate because PNG encoders
can produce different compressed bytes for identical pixels.

Build with normal warnings-as-errors and NuGet auditing enabled. Restore the
neutral, win-x64, linux-x64, osx-arm64 and osx-x64 dependency graphs; none should
contain Chorizite.Core, SixLabors packages or BCnEncoder.Net.ImageSharp.
