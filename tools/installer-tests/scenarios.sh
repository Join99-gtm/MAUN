set -u
# work dir: a copy of the goose in $SP/goose/"Desktop Goose v0.31"/"DesktopGoose v0.31", the friend's .rar in $GOOSE_RAR
SP=${SP:-/tmp/goose-work}; T=$SP/insttest; rm -rf $T; mkdir -p $T/bin && ln -s /usr/bin/bsdtar $T/bin/tar
G="$SP/goose/Desktop Goose v0.31/DesktopGoose v0.31"
mkpayload() { mkdir -p "$1/Assets/Mods/GooseDeluxe" && cp /home/user/MAUN/GooseDeluxe/bin/Release/net452/GooseDeluxe.dll /home/user/MAUN/GooseDeluxe/GooseDeluxe.ini "$1/Assets/Mods/GooseDeluxe/" && cp /home/user/MAUN/installer/install.ps1 "$1/"; }
run() { H=$1; I=$2; shift 2; USERPROFILE=$H pwsh -NoProfile -Command "& '$I/install.ps1' -DesktopPath '$H/Desktop' -SearchRoots @('$H/Desktop/Гусь','$H/Desktop','$H/Downloads','$I') -NoGui -AutoYes -NoLaunch $*; exit \$LASTEXITCODE" 2>&1 | grep -v "  searching " | sed 's/^/    /'; echo "    exit=${PIPESTATUS[0]}"; }
tree() { find "$1" -maxdepth 4 -not -path "*/FOR MOD-MAKERS/*" | sed "s|$1||" | sort | grep -v "Assets/\(Images\|Sound\|Text\)" | head -30; }

echo "################ TEST A: goose in Downloads (not on Desktop)"
H=$T/A; mkdir -p "$H/Desktop" "$H/Downloads/Desktop Goose v0.31" && cp -r "$G" "$H/Downloads/Desktop Goose v0.31/" && mkpayload $H/Downloads/GooseDeluxe-v0.1
run $H $H/Downloads/GooseDeluxe-v0.1
echo "  -- tree:"; tree $H; echo "  -- config:"; grep EnableMods "$H/Desktop/Гусь/config.ini"; echo "  -- Downloads now:"; ls "$H/Downloads"

echo; echo "################ TEST B1: only the RAR in Downloads, no rar tool (GNU tar)"
H=$T/B1; mkdir -p "$H/Desktop" "$H/Downloads" && cp ${GOOSE_RAR:-$SP/Desktop_Goose_v0.31.rar} "$H/Downloads/Desktop_Goose_v0.31.rar" && mkpayload $H/Downloads/GooseDeluxe-v0.1
run $H $H/Downloads/GooseDeluxe-v0.1
echo "  -- tree:"; find $H/Desktop | sed "s|$H||"

echo; echo "################ TEST B2: only the RAR, bsdtar available as tar"
H=$T/B2; mkdir -p "$H/Desktop" "$H/Downloads" && cp ${GOOSE_RAR:-$SP/Desktop_Goose_v0.31.rar} "$H/Downloads/Desktop_Goose_v0.31.rar" && mkpayload $H/Downloads/GooseDeluxe-v0.1
PATH="$T/bin:$PATH" run $H $H/Downloads/GooseDeluxe-v0.1
echo "  -- tree:"; tree "$H/Desktop"; echo "  -- config:"; grep EnableMods "$H/Desktop/Гусь/config.ini"

echo; echo "################ TEST C: re-run on B2 result (idempotent, keeps user ini, removes stray API dll)"
echo "Hat=Santa" >> "$H/Desktop/Гусь/Assets/Mods/GooseDeluxe/GooseDeluxe.ini"; touch "$H/Desktop/Гусь/Assets/Mods/GooseDeluxe/GooseModdingAPI.dll"
run $H $H/Downloads/GooseDeluxe-v0.1
ls "$H/Desktop/Гусь/Assets/Mods/GooseDeluxe/"; tail -1 "$H/Desktop/Гусь/Assets/Mods/GooseDeluxe/GooseDeluxe.ini"; grep EnableMods "$H/Desktop/Гусь/config.ini"; ls "$H/Desktop"

echo; echo "################ TEST D: payload missing (bat run from inside the zip)"
H=$T/D; mkdir -p "$H/Desktop" "$H/tmp" && cp /home/user/MAUN/installer/install.ps1 "$H/tmp/"
run $H $H/tmp
