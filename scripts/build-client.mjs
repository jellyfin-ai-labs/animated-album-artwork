import { readFile } from 'node:fs/promises';
import { build } from 'esbuild';

const license = await readFile(new URL('../node_modules/mp4box/LICENSE', import.meta.url), 'utf8');
await build({
    entryPoints: ['Jellyfin.Plugin.AnimatedAlbumArt/Web/animatedAlbumArt.js'],
    outfile: 'Jellyfin.Plugin.AnimatedAlbumArt/Web/client.bundle.js',
    bundle: true,
    minify: true,
    format: 'iife',
    target: 'es2020',
    legalComments: 'inline',
    banner: { js: '/*! Bundled MP4Box.js 2.4.1 (BSD-3-Clause)\n' + license + '*/' }
});
