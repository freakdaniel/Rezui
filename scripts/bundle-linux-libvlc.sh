#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <dotnet-publish-directory>" >&2
  exit 2
fi

publish_dir=$(realpath "$1")
app_binary="$publish_dir/Rezui"

if [[ ! -x "$app_binary" ]]; then
  echo "Rezui executable was not found in $publish_dir" >&2
  exit 1
fi

case "$(uname -m)" in
  x86_64) runtime_id="linux-x64" ;;
  aarch64 | arm64) runtime_id="linux-arm64" ;;
  *)
    echo "Unsupported Linux architecture: $(uname -m)" >&2
    exit 1
    ;;
esac

find_library() {
  local pattern=$1
  find \
    /usr/lib/x86_64-linux-gnu \
    /usr/lib/aarch64-linux-gnu \
    /usr/lib64 \
    /usr/lib \
    /usr/local/lib \
    -maxdepth 1 \
    -name "$pattern" \
    \( -type f -o -type l \) \
    -print \
    -quit 2>/dev/null
}

libvlc_path=$(find_library "libvlc.so.5")
libvlccore_path=$(find_library "libvlccore.so.9")
plugin_dir=${VLC_PLUGIN_PATH:-}
if [[ -z "$plugin_dir" ]] ||
  ! find "$plugin_dir" -type f -name '*_plugin.so' -print -quit 2>/dev/null |
    grep -q .; then
  plugin_file=$(
    find \
      /usr/lib/x86_64-linux-gnu/vlc/plugins \
      /usr/lib/aarch64-linux-gnu/vlc/plugins \
      /usr/lib64/vlc/plugins \
      /usr/lib/vlc/plugins \
      /usr/local/lib/vlc/plugins \
      -type f \
      -name '*_plugin.so' \
      -print \
      -quit 2>/dev/null
  )
  plugin_dir=${plugin_file%/*}
fi

if [[ -z "$libvlc_path" || -z "$libvlccore_path" ]]; then
  echo "libvlc.so.5/libvlccore.so.9 are missing in the build environment." >&2
  exit 1
fi

if [[ -z "$plugin_dir" ]]; then
  echo "VLC plugins are missing. On Debian/Ubuntu install vlc-plugin-base." >&2
  exit 1
fi

runtime_dir="$publish_dir/libvlc/$runtime_id"
dependencies_dir="$runtime_dir/deps"
mkdir -p "$runtime_dir" "$dependencies_dir"

cp -L "$libvlc_path" "$runtime_dir/libvlc.so.5"
cp -L "$libvlccore_path" "$runtime_dir/libvlccore.so.9"
cp -a "$plugin_dir" "$runtime_dir/plugins"

mapfile -t dependencies < <(
  {
    ldd "$runtime_dir/libvlc.so.5"
    ldd "$runtime_dir/libvlccore.so.9"
    find "$runtime_dir/plugins" -type f -name '*.so' -exec ldd {} \;
  } |
    awk '
      /=> \// { print $3 }
      /^\// { print $1 }
    ' |
    sort -u
)

for dependency in "${dependencies[@]}"; do
  case "$(basename "$dependency")" in
    ld-linux*.so* | libc.so* | libdl.so* | libm.so* | libpthread.so* | librt.so*)
      continue
      ;;
  esac

  cp -L -n "$dependency" "$dependencies_dir/$(basename "$dependency")"
done

mv "$app_binary" "$publish_dir/Rezui.bin"

cat >"$app_binary" <<'LAUNCHER'
#!/usr/bin/env bash
set -euo pipefail
app_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
case "$(uname -m)" in
  x86_64) runtime_id="linux-x64" ;;
  aarch64 | arm64) runtime_id="linux-arm64" ;;
  *) echo "Unsupported Linux architecture: $(uname -m)" >&2; exit 1 ;;
esac
runtime_dir="$app_dir/libvlc/$runtime_id"
export LD_LIBRARY_PATH="$runtime_dir:$runtime_dir/deps${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export VLC_PLUGIN_PATH="$runtime_dir/plugins"
exec "$app_dir/Rezui.bin" "$@"
LAUNCHER

chmod +x "$app_binary" "$publish_dir/Rezui.bin"
echo "Bundled LibVLC runtime into $runtime_dir"
