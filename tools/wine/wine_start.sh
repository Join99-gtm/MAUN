#!/bin/bash
# Starts the real Desktop Goose under Wine with the freshly built GooseDeluxe, answers "Да" to the
# goose's Mod Enabler Warning and waits for GooseDeluxe.status. Leaves the goose running.
SP=${SP:-/tmp/goose-work}   # work dir: goose copy in $SP/goose, Wine prefix in $SP/wp
export LANG=ru_RU.UTF-8 LC_ALL=ru_RU.UTF-8 WINEPREFIX=$SP/wp WINEDEBUG=-all DISPLAY=:77
G="$SP/wp/drive_c/users/root/Desktop/Гусь"
M="$G/Assets/Mods/GooseDeluxe"

pgrep -x Xvfb >/dev/null || { (setsid Xvfb :77 -screen 0 1600x900x24 +extension Composite >/dev/null 2>&1 &); sleep 1; (setsid xcompmgr >/dev/null 2>&1 &); xsetroot -solid "#3a6ea5"; }
wineserver -k 2>/dev/null; sleep 1
cp /home/user/MAUN/GooseDeluxe/bin/Release/net452/GooseDeluxe.dll "$M/"
rm -f "$M/GooseDeluxe.status" "$M/GooseDeluxe.log"
[ "$1" = "fresh" ] && rm -f "$M/GooseDeluxe.version"
if [ -n "$NTFY_PORT" ]; then
  sed -i "s#^NtfyServer=.*#NtfyServer=http://127.0.0.1:$NTFY_PORT#" "$M/GooseDeluxe.ini"
fi
cd "$G"
(env -u HTTPS_PROXY -u HTTP_PROXY -u https_proxy -u http_proxy wine GooseDesktop.exe > $SP/goose-run.log 2>&1 &)

for i in $(seq 1 60); do
  w=$(xdotool search --onlyvisible --name "Mod Enabler Warning" 2>/dev/null | head -1)
  [ -n "$w" ] && break
  sleep 1
done
[ -z "$w" ] && { echo "NO MOD ENABLER WARNING"; exit 1; }
eval $(xdotool getwindowgeometry --shell $w)
# "Да" is the left button, 132,107 from the dialog's corner (330x128 dialog)
xdotool mousemove $((X + 132)) $((Y + 107)) click 1
for i in $(seq 1 60); do
  if [ -f "$M/GooseDeluxe.status" ] && grep -q "^hooked\|^failed" "$M/GooseDeluxe.status"; then break; fi
  sleep 1
done
echo "status: $(cat "$M/GooseDeluxe.status" 2>/dev/null)"
