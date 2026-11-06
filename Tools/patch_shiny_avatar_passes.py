"""Split BeatLeader's shiny avatar shader so its halo renders under URP.

Canvas rendering under URP draws only the first pass of the two-pass
BeatLeader/UIShinyAvatar shader, which drops the additive halo. This keeps the
avatar pass in the original shader and adds BeatLeader/UIShinyAvatarHalo with
only the halo pass, registered as assets/beatleader/3_shaders/ui/uishinyavatarhalo.shader
so the mod can load it and draw the halo on a second image.

Requires UnityPy 1.25.3. Pass the original asset bundle and a new output path.
"""

import argparse
import copy
import hashlib
from pathlib import Path

import UnityPy


SOURCE_SHA256 = "c12aecd5bdb863a645c4f2836f2c02be4aa4750b605c27196ebbcaec5ce39dd3"
SHADER_NAME = "BeatLeader/UIShinyAvatar"
HALO_NAME = "BeatLeader/UIShinyAvatarHalo"
HALO_CONTAINER = "assets/beatleader/3_shaders/ui/uishinyavatarhalo.shader"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    data = args.source.read_bytes()
    if hashlib.sha256(data).hexdigest() != SOURCE_SHA256:
        raise ValueError("Input is not the reviewed BeatLeader asset bundle")
    if args.output.exists():
        raise FileExistsError(args.output)

    environment = UnityPy.load(data)
    shaders = [
        obj for obj in environment.objects
        if obj.type.name == "Shader" and obj.read_typetree()["m_ParsedForm"]["m_Name"] == SHADER_NAME
    ]
    bundles = [obj for obj in environment.objects if obj.type.name == "AssetBundle"]
    if len(shaders) != 1 or len(bundles) != 1:
        raise ValueError("Unexpected shader or asset bundle count")

    source = shaders[0]
    tree = source.read_typetree()
    passes = tree["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"]
    if len(tree["m_ParsedForm"]["m_SubShaders"]) != 1 or len(passes) != 2:
        raise ValueError("Shiny avatar shader pass layout changed")

    halo = copy.deepcopy(tree)
    halo["m_ParsedForm"]["m_Name"] = HALO_NAME
    halo["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"] = [copy.deepcopy(passes[1])]
    tree["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"] = [passes[0]]
    source.save_typetree(tree)

    assets = source.assets_file
    halo_id = int.from_bytes(hashlib.sha256(HALO_NAME.encode()).digest()[:8], "little", signed=True)
    if halo_id in assets.objects:
        raise ValueError("Halo shader path id is already used")
    halo_object = copy.copy(source)
    halo_object.path_id = halo_id
    assets.objects[halo_id] = halo_object
    halo_object.save_typetree(halo)

    bundle = bundles[0]
    bundle_tree = bundle.read_typetree()
    pointer = {"m_FileID": 0, "m_PathID": halo_id}
    bundle_tree["m_Container"].append((HALO_CONTAINER, {
        "preloadIndex": len(bundle_tree["m_PreloadTable"]),
        "preloadSize": 1,
        "asset": pointer,
    }))
    bundle_tree["m_PreloadTable"].append(pointer)
    bundle.save_typetree(bundle_tree)

    output = environment.file.save(packer="lz4")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(output)
    print(f"Built {args.output} ({len(output)} bytes)")
    print(f"SHA-256 {hashlib.sha256(output).hexdigest()}")


if __name__ == "__main__":
    main()
