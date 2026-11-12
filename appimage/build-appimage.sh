#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C

die() { printf 'Error: %s\n' "$*" >&2; exit 1; }

[[ $# -ge 1 && $# -le 2 ]] || die "Usage: $0 <version-label> [output-directory]"
label=$1
[[ $label =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]] || die 'Invalid version label'
[[ $(uname -m) == x86_64 ]] || die 'This package requires an x86_64 Linux build host'

for tool in mono curl sha256sum readlink mktemp install cp tar find sed file objdump dd awk; do
    command -v "$tool" >/dev/null || die "Required tool not found: $tool"
done

script_dir=$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")
repo_dir=$(dirname "$script_dir")
[[ -f $repo_dir/Build/Builder.exe && -f $repo_dir/Build/libBuilderNative.so ]] ||
    die 'Run make linux before packaging'

mono_prefix=$(dirname "$(dirname "$(readlink -f "$(command -v mono)")")")
mono_runtime=$mono_prefix/bin/mono-sgen
mono_lib=$mono_prefix/lib/mono
[[ -x $mono_runtime && -d $mono_lib/4.5 && -d $mono_lib/gac ]] ||
    die "Incomplete Mono installation under $mono_prefix"
mono_config=$mono_prefix/etc/mono
if [[ ! -d $mono_config && $mono_prefix == /usr ]]; then
    mono_config=/etc/mono
fi
[[ -f $mono_config/config && -f $mono_config/4.5/machine.config ]] ||
    die "Missing Mono configuration under $mono_config"

tmp_base=$(readlink -f "${TMPDIR:-/tmp}")
[[ -d $tmp_base && -w $tmp_base ]] || die 'TMPDIR must be an existing writable directory'
case "$tmp_base/" in
    "$repo_dir/"*) die 'TMPDIR must be outside the source checkout' ;;
esac
work_dir=$(mktemp -d "$tmp_base/udb-appimage.XXXXXX")
trap 'printf "Packaging scratch retained at %s\n" "$work_dir" >&2' EXIT
export TMPDIR=$work_dir
app_dir=$work_dir/AppDir
output_dir=$(readlink -m "${2:-$repo_dir/Release}")
output=$output_dir/UltimateDoomBuilder-$label-x86_64.AppImage
[[ ! -e $output ]] || die "Output already exists: $output"

install -d "$app_dir/usr/bin" "$app_dir/usr/lib/mono" \
    "$app_dir/usr/lib/ultimatedoombuilder" "$app_dir/usr/etc/mono/4.5" \
    "$app_dir/usr/share/doc/ultimatedoombuilder/licenses" "$work_dir/mono"
tar -C "$repo_dir/Build" --exclude='*.pdb' --exclude='*.mdb' -cf - . |
    tar -C "$app_dir/usr/lib/ultimatedoombuilder" -xf -
# Preserve the profile's relative symlinks into the GAC while excluding SDK-only content.
cp -a "$mono_lib/4.5" "$mono_lib/gac" "$work_dir/mono/"
tar -C "$work_dir/mono" --exclude='*.pdb' --exclude='*.mdb' \
    --exclude='4.5/MSBuild' --exclude='4.5/xbuild*' -cf - 4.5 gac |
    tar -C "$app_dir/usr/lib/mono" -xf -
cp -a "$repo_dir/Fonts" "$app_dir/usr/lib/"
install -m 755 "$mono_runtime" "$app_dir/usr/bin/mono"
install -m 755 "$script_dir/AppRun" "$app_dir/AppRun"
install -m 644 "$mono_config/config" "$app_dir/usr/etc/mono/config"
install -m 644 "$mono_config/4.5/machine.config" "$app_dir/usr/etc/mono/4.5/machine.config"
install -m 644 "$repo_dir/LICENSE.txt" "$script_dir/README.md" \
    "$app_dir/usr/share/doc/ultimatedoombuilder/"
ln -s ../../AppRun "$app_dir/usr/bin/ultimatedoombuilder"

# Assembly-specific configs can override the global gdiplus mapping.
find "$app_dir/usr/lib/mono" "$app_dir/usr/etc/mono" -type f -name '*.config' \
    -exec sed -i -E '/<dllmap[^>]*dll="(gdiplus(\.dll)?|gdi32(\.dll)?)"/s@target="[^"]*"@target="libgdiplus.so.0"@g' {} +

linuxdeploy_args=(
    --appdir "$app_dir"
    --deploy-deps-only "$app_dir/usr/bin/mono"
    --deploy-deps-only "$app_dir/usr/lib/ultimatedoombuilder/libBuilderNative.so"
    --custom-apprun "$script_dir/AppRun"
    --desktop-file "$script_dir/io.github.ultimatedoombuilder.ultimatedoombuilder.desktop"
    --icon-file "$script_dir/io.github.ultimatedoombuilder.ultimatedoombuilder.png"
)
for library in libgdiplus.so.0 libMonoPosixHelper.so libmono-native.so libmono-btls-shared.so; do
    source_library=
    for candidate in "$mono_prefix/lib/$library" "$mono_prefix/lib64/$library" \
        "$mono_prefix"/lib/*-linux-gnu/"$library" \
        "/usr/lib/$library" "/usr/lib64/$library" /usr/lib/*-linux-gnu/"$library" \
        "/lib/$library" "/lib64/$library" /lib/*-linux-gnu/"$library"; do
        [[ -f $candidate ]] || continue
        case $(file -Lb "$candidate") in
            'ELF 64-bit LSB'*'x86-64'*) source_library=$candidate; break ;;
        esac
    done
    [[ -n $source_library ]] || die "Cannot locate an x86_64 $library"
    install -m 755 "$(readlink -f "$source_library")" "$app_dir/usr/lib/$library"
    linuxdeploy_args+=(--deploy-deps-only "$app_dir/usr/lib/$library")
done

# Preserve distro notices for Mono, which is copied rather than deployed as an executable.
for notice in "$mono_prefix"/share/doc/mono*/copyright \
    "$mono_prefix"/share/doc/libmono*/copyright "$mono_prefix/share/doc/libgdiplus/copyright"; do
    [[ -f $notice ]] || continue
    package=$(basename "$(dirname "$notice")")
    install -m 644 "$notice" "$app_dir/usr/share/doc/ultimatedoombuilder/licenses/$package-copyright"
done

linuxdeploy=$work_dir/linuxdeploy-x86_64.AppImage
curl --fail --location --retry 3 --proto '=https' --proto-redir '=https' \
    --output "$linuxdeploy" \
    https://github.com/linuxdeploy/linuxdeploy/releases/download/1-alpha-20251107-1/linuxdeploy-x86_64.AppImage
printf '%s  %s\n' c20cd71e3a4e3b80c3483cef793cda3f4e990aca14014d23c544ca3ce1270b4d \
    "$linuxdeploy" | sha256sum --check --status || die 'linuxdeploy checksum mismatch'
chmod +x "$linuxdeploy"

# Reuse the verified runtime instead of appimagetool's rolling runtime download.
runtime=$work_dir/runtime-x86_64
runtime_size=$("$linuxdeploy" --appimage-offset)
[[ $runtime_size =~ ^[0-9]+$ && $runtime_size -gt 0 ]] || die 'Invalid AppImage runtime offset'
dd if="$linuxdeploy" of="$runtime" bs="$runtime_size" count=1 iflag=fullblock status=none
sections=$(objdump -h "$runtime" | awk \
    '$2 == ".upd_info" || $2 == ".sha256_sig" || $2 == ".sig_key" || $2 == ".digest_md5" { print $3, $6 }')
[[ -n $sections ]] || die 'Cannot locate runtime metadata sections'
while read -r size offset; do
    dd if=/dev/zero of="$runtime" bs=1 count="$((16#$size))" \
        seek="$((16#$offset))" conv=notrunc status=none
done <<< "$sections"

install -d "$output_dir"
export APPIMAGE_EXTRACT_AND_RUN=1 ARCH=x86_64
export LDAI_RUNTIME_FILE=$runtime LDAI_NO_APPSTREAM=1 LDAI_VERSION=$label
export LDAI_OUTPUT=$work_dir/UltimateDoomBuilder-$label-x86_64.AppImage
export LD_LIBRARY_PATH="$app_dir/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
unset DISABLE_COPYRIGHT_FILES_DEPLOYMENT LINUXDEPLOY_PLUGIN_MODE
unset LDAI_UPDATE_INFORMATION UPDATE_INFORMATION LDAI_UPD_INFO UPD_INFO
unset LDAI_GUESS_UPDATE_INFORMATION LDAI_SIGN SIGN
(
    cd "$work_dir"
    "$linuxdeploy" "${linuxdeploy_args[@]}" --output appimage
)
[[ -s $LDAI_OUTPUT ]] || die 'linuxdeploy did not produce an AppImage'
install -m 755 "$LDAI_OUTPUT" "$output"
printf 'Created %s\n' "$output"
