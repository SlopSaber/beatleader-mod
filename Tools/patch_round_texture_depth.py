"""Make BeatLeader's rounded UI textures respect scene depth.

Requires UnityPy 1.25.3. Pass the known original asset bundle and a new output path.
"""

import argparse
import hashlib
from pathlib import Path

import UnityPy


ORIGINAL_SHA256 = "857bb9428d83f8160139d08c72d05f6a924724a7e549f592b48d7f786b645e56"
SHADER_NAME = "BeatLeader/UIRoundTexture"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    data = args.source.read_bytes()
    if hashlib.sha256(data).hexdigest() != ORIGINAL_SHA256:
        raise ValueError("Input is not the reviewed BeatLeader asset bundle")
    if args.output.exists():
        raise FileExistsError(args.output)

    environment = UnityPy.load(data)
    shaders = [
        obj for obj in environment.objects
        if obj.type.name == "Shader" and obj.read().m_ParsedForm.m_Name == SHADER_NAME
    ]
    if len(shaders) != 1:
        raise ValueError(f"Expected one {SHADER_NAME} shader, found {len(shaders)}")

    shader = shaders[0].read()
    subshaders = shader.m_ParsedForm.m_SubShaders
    if len(subshaders) != 1 or len(subshaders[0].m_Passes) != 1:
        raise ValueError("Rounded texture shader pass layout changed")
    depth_test = subshaders[0].m_Passes[0].m_State.zTest
    if depth_test.val != 8.0 or subshaders[0].m_Passes[0].m_State.zWrite.val != 0.0:
        raise ValueError("Rounded texture shader render state changed")

    depth_test.val = 4.0  # Unity CompareFunction.LessEqual; 8 was Always.
    shader.save()
    output = environment.file.save(packer="lz4")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(output)
    print(f"Built {args.output} ({len(output)} bytes)")
    print(f"SHA-256 {hashlib.sha256(output).hexdigest()}")


if __name__ == "__main__":
    main()
