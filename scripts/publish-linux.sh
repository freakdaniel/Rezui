#!/usr/bin/env bash
set -euo pipefail

repository_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
runtime_id=${1:-linux-x64}
output_dir="$repository_dir/artifacts/$runtime_id"
archive="$repository_dir/artifacts/Rezui-$runtime_id.tar.gz"

rm -rf "$output_dir"
mkdir -p "$output_dir"

dotnet publish "$repository_dir/src/Rezui/Rezui.csproj" \
  --configuration Release \
  --runtime "$runtime_id" \
  --self-contained true \
  --output "$output_dir"

"$repository_dir/scripts/bundle-linux-libvlc.sh" "$output_dir"

tar -C "$repository_dir/artifacts" \
  -czf "$archive" \
  "$runtime_id"

echo "Created $archive"

