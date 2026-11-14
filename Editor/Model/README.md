# YYB Miku Skeleton Reference Rig

`_model.pmx` uses the skeleton from `YYB Hatsune Miku_10th_v1.02.pmx` (model author: SANMUYYB). It preserves bone order, names, positions, hierarchy, append transforms, axes and IK definitions. Skeletal and group morphs are retained; mesh, material and physics morphs provide names only.

Character mesh, textures and physics are omitted. A tiny transparent compatibility triangle remains for PMX importers that require vertices.

Regenerate from a local source model, from the package root:

```text
node Tools~/generate-reference-pmx.mjs "path/to/YYB Hatsune Miku_10th_v1.02.pmx"
```

An optional second argument selects the output PMX path. The generator checks the resulting skeleton against the source before writing it. The source model is not included.

The generator is Apache-2.0. Extracted rig data remains subject to the source model's terms; see `NOTICE.txt`.
