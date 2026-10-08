{ pkgs, lib, ... }:

# Toolchain only (D-14). No services: Jaeger and Postgres come from docker compose in later phases.
#
# ruff and pyright are deliberately NOT taken from nixpkgs. They come from python/uv.lock through
# `uv sync`, so a developer in this shell and a reviewer without Nix run the same pinned versions
# (research Pitfall 12).
#
# If native NuGet libraries are added later (PDFium, SkiaSharp), enable `programs.nix-ld` on the
# NixOS host so their prebuilt binaries load (research A12).
{
  languages.dotnet.enable = true;
  languages.dotnet.package = pkgs.dotnet-sdk_10;

  packages = [
    pkgs.uv
    pkgs.just
    pkgs.python312
    pkgs.git
    pkgs.curl
    # pyright (PyPI) is a wrapper around the Node tool. Without a node on PATH it downloads one,
    # and a downloaded binary cannot run on NixOS (research Pitfall 13).
    pkgs.nodejs
    # The secretspec CLI only. Neither this file nor devenv.yaml enables devenv's own secretspec
    # integration, so `devenv shell` never reads the keyring or loads the provider key. Offline
    # checks therefore never see a live key, and an agent running `devenv shell -- just check`
    # needs no access reason. The key is resolved only by the live recipes (just skeleton,
    # just spike-live), through `secretspec run`.
    pkgs.secretspec
  ];

  env = {
    # uv must use the Nix interpreter and never download one (NixOS cannot run unpatched binaries).
    UV_PYTHON = "${pkgs.python312}/bin/python3.12";
    UV_PYTHON_DOWNLOADS = "never";
    DOTNET_CLI_TELEMETRY_OPTOUT = "1";
    # The Nix interpreter cannot find libstdc++ for prebuilt wheels (zxing-cpp, pypdfium2, lxml, ...).
    LD_LIBRARY_PATH = lib.makeLibraryPath [
      pkgs.stdenv.cc.cc.lib
      pkgs.zlib
    ];
  };

  enterShell = ''
    echo "carimbo toolchain: dotnet $(dotnet --version), $(uv --version), $(just --version)"
  '';
}
