"""Let URP draw both passes of BeatLeader's shiny avatar shader.

URP draws only the first untagged pass of a shader, which drops the additive
halo pass. Tagging the passes with the two light modes URP draws in order
keeps the avatar pass first and the halo pass second.

Requires UnityPy 1.25.3. Pass the known asset bundle and a new output path.
"""

import argparse
import hashlib
from pathlib import Path

import UnityPy


SOURCE_SHA256 = "c12aecd5bdb863a645c4f2836f2c02be4aa4750b605c27196ebbcaec5ce39dd3"
SHADER_NAME = "BeatLeader/UIShinyAvatar"
LIGHT_MODES = ("SRPDefaultUnlit", "UniversalForward")


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
    if len(shaders) != 1:
        raise ValueError(f"Expected one {SHADER_NAME} shader, found {len(shaders)}")

    tree = shaders[0].read_typetree()
    subshaders = tree["m_ParsedForm"]["m_SubShaders"]
    if len(subshaders) != 1 or len(subshaders[0]["m_Passes"]) != 2:
        raise ValueError("Shiny avatar shader pass layout changed")

    for shader_pass, light_mode in zip(subshaders[0]["m_Passes"], LIGHT_MODES):
        tags = shader_pass["m_Tags"]["tags"]
        if tags:
            raise ValueError("Shiny avatar shader passes are already tagged")
        tags.append(("LightMode", light_mode))

    shaders[0].save_typetree(tree)
    output = environment.file.save(packer="lz4")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(output)
    print(f"Built {args.output} ({len(output)} bytes)")
    print(f"SHA-256 {hashlib.sha256(output).hexdigest()}")


if __name__ == "__main__":
    main()
