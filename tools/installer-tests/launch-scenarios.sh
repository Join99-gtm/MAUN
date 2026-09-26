#!/bin/bash
# Installer scenarios that start the goose: a fake GooseDesktop.exe (a shell script) plays the goose and
# writes GooseDeluxe.status the way the mod does, or misbehaves.
set -u
export PATH="$PATH:/root/.dotnet/tools"
# work dir: a copy of the goose in $SP/goose/"Desktop Goose v0.31"/"DesktopGoose v0.31", the friend's .rar in $GOOSE_RAR
SP=${SP:-/tmp/goose-work}; T=$SP/insttest2; rm -rf $T; mkdir -p $T
G="$SP/goose/Desktop Goose v0.31/DesktopGoose v0.31"
mkpayload() { mkdir -p "$1/Assets/Mods/GooseDeluxe" && cp /home/user/MAUN/GooseDeluxe/bin/Release/net452/GooseDeluxe.dll /home/user/MAUN/GooseDeluxe/GooseDeluxe.ini "$1/Assets/Mods/GooseDeluxe/" && cp /home/user/MAUN/installer/install.ps1 "$1/"; }
# fakegoose <goose dir> <behaviour>: replaces GooseDesktop.exe with a script
fakegoose() {
  cat > "$1/GooseDesktop.exe" <<EOF
#!/bin/bash
D="\$(cd "\$(dirname "\$0")" && pwd)/Assets/Mods/GooseDeluxe"
echo "\$(date +%T) fake goose started in \$(pwd)" >> "$T/fake.log"
case "$2" in
  hooked) sleep 1; printf 'started|2026-09-25T10:00:00Z|0.5.0|pid 1' > "\$D/GooseDeluxe.status"; sleep 1
          printf 'hooked|2026-09-25T10:00:01Z|0.5.0|engine=True settings=True' > "\$D/GooseDeluxe.status"; sleep 20 ;;
  failed) sleep 1; printf 'failed|2026-09-25T10:00:01Z|0.5.0|InvalidOperationException: boom' > "\$D/GooseDeluxe.status"; sleep 20 ;;
  silent) sleep 20 ;;
  av)     sleep 1; rm -f "\$D/GooseDeluxe.dll"; sleep 20 ;;
  exits)  exit 3 ;;
esac
EOF
  chmod +x "$1/GooseDesktop.exe"
}
run() { H=$1; I=$2; shift 2; USERPROFILE=$H pwsh -NoProfile -Command "& '$I/install.ps1' -DesktopPath '$H/Desktop' -SearchRoots @('$H/Desktop/Гусь','$H/Desktop','$H/Downloads','$I') -NoGui -AutoYes -WaitSeconds 8 $*; exit \$LASTEXITCODE" 2>&1 | grep -v "  searching " | sed 's/^/    /'; echo "    exit=${PIPESTATUS[0]}"; }
cleanup() { pkill -f "fake goose" 2>/dev/null; pkill -f "$T/.*/GooseDesktop.exe" 2>/dev/null; true; }

for mode in hooked failed silent av exits; do
  echo "################ LAUNCH: fake goose '$mode'"
  H=$T/$mode; mkdir -p "$H/Desktop/Гусь" "$H/Downloads" && cp -r "$G/." "$H/Desktop/Гусь/" && fakegoose "$H/Desktop/Гусь" $mode && mkpayload $H/Downloads/GooseDeluxe-v0.5
  run $H $H/Downloads/GooseDeluxe-v0.5 | grep -E "exit=|\[INFO\]|\[ERROR\]|wait result|report: (Итог|Файл мода|Проверка)" | cut -c1-260
  [ -f "$H/Downloads/GooseDeluxe-v0.5/install-report.txt" ] && echo "    report file: $(wc -l < "$H/Downloads/GooseDeluxe-v0.5/install-report.txt") lines"
  cleanup
done

echo "################ TWO COPIES: Desktop\\Гусь and an old one in Downloads; both get the mod"
H=$T/two; mkdir -p "$H/Desktop/Гусь" "$H/Downloads/old/DesktopGoose v0.31" && cp -r "$G/." "$H/Desktop/Гусь/" && cp -r "$G/." "$H/Downloads/old/DesktopGoose v0.31/" && fakegoose "$H/Desktop/Гусь" hooked && mkpayload $H/Downloads/GooseDeluxe-v0.5
run $H $H/Downloads/GooseDeluxe-v0.5 | grep -E "exit=|Мод поставлен|main goose|\[INFO\]" | cut -c1-200
ls "$H/Downloads/old/DesktopGoose v0.31/Assets/Mods/GooseDeluxe/" "$H/Desktop/Гусь/Assets/Mods/GooseDeluxe/"; grep EnableMods "$H/Downloads/old/DesktopGoose v0.31/config.ini"
cleanup

echo "################ ZIP UNPACKED INTO THE GOOSE FOLDER ITSELF (payload = mod folder)"
H=$T/inplace; mkdir -p "$H/Desktop/Гусь" && cp -r "$G/." "$H/Desktop/Гусь/" && fakegoose "$H/Desktop/Гусь" hooked && mkpayload "$H/Desktop/Гусь"
run $H "$H/Desktop/Гусь" | grep -E "exit=|\[INFO\]|\[ERROR\]" | cut -c1-200
cleanup
