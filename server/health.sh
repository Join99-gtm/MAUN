#!/bin/bash
# Проверка состояния сервера. Только чтение, ничего не меняет.
# Запуск: bash ~/health.sh
ok()   { printf '  \033[32mOK\033[0m   %s\n' "$*"; }
warn() { printf '  \033[33mВНИМ\033[0m %s\n' "$*"; }
bad()  { printf '  \033[31mПЛОХО\033[0m %s\n' "$*"; }
h()    { printf '\n\033[1m== %s ==\033[0m\n' "$*"; }

h "Нагрузка и память"
read l1 l5 l15 _ < /proc/loadavg
cores=$(nproc)
echo "  load average: $l1 / $l5 / $l15 (ядер: $cores), $(uptime -p)"
awk -v l="$l5" -v c="$cores" 'BEGIN{exit !(l > c*0.7)}' && bad "нагрузка высокая" || ok "нагрузка в норме"
free -h | awk 'NR==2{printf "  память: занято %s из %s, доступно %s\n",$3,$2,$7}'

h "Температура"
t=$(sensors k10temp-pci-00c3 2>/dev/null | awk '/Tctl/{gsub(/[+°C]/,"",$2); print int($2)}')
if [ -n "$t" ]; then
  if   [ "$t" -ge 90 ]; then bad "процессор ${t}°C (предел 95)"
  elif [ "$t" -ge 75 ]; then warn "процессор ${t}°C"
  else ok "процессор ${t}°C"; fi
fi
g=$(nvidia-smi --query-gpu=temperature.gpu,utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits 2>/dev/null)
[ -n "$g" ] && echo "$g" | awk -F', ' '{printf "  видеокарта: %s°C, загрузка %s%%, память %s/%s МБ\n",$1,$2,$3,$4}' || bad "видеокарта: драйвер не отвечает (nvidia-smi)"
sensors 2>/dev/null | awk '/^fan[0-9]/{printf "  %s %s об/мин\n",$1,$2}'

h "Диски"
df -h / /data 2>/dev/null | awk 'NR>1{p=$5; sub(/%/,"",p); s=(p>=95?"\033[31mПЛОХО\033[0m":(p>=90?"\033[33mВНИМ\033[0m":"\033[32mOK\033[0m")); printf "  %-5s %s занято %s из %s (%s%%)\n",s,$6,$3,$2,p}'

h "Контейнеры"
docker ps -a --format '{{.Names}}|{{.Status}}' | while IFS='|' read n s; do
  case "$s" in
    *unhealthy*|*Restarting*) bad "$n: $s";;
    Up*) :;;
    *) echo "  --   $n: $s";;
  esac
done
echo "  запущено: $(docker ps -q | wc -l), всего: $(docker ps -aq | wc -l)"
echo "  больше всех CPU:"
docker stats --no-stream --format '{{.CPUPerc}} {{.Name}} {{.MemUsage}}' | sort -rn | head -4 | sed 's/^/    /'

h "Сервисы"
curl -sk -m 10 https://10.10.106.144:8080/status.php | grep -q '"installed":true' && ok "Nextcloud отвечает" || bad "Nextcloud не отвечает"
[ "$(curl -sk -m 10 https://10.10.106.144:8443/healthcheck)" = "true" ] && ok "ONLYOFFICE отвечает" || bad "ONLYOFFICE не отвечает"
n=$(ps -eo pcpu,comm | awk '/php-fpm/ && $1>50' | wc -l)
[ "$n" -ge 3 ] && bad "php-fpm: $n процессов выше 50% CPU (риск зависания Nextcloud)" || ok "php-fpm спокойны"
[ "$(sudo -n iptables -S DOCKER-USER 2>/dev/null | grep -c 172.30.0.0/24)" -ge 4 ] && ok "ONLYOFFICE без интернета (правила на месте)" || warn "правила DOCKER-USER не проверены (нужен sudo) или отсутствуют"
systemctl is-active --quiet fts-index.service && echo "  --   индексация поиска (fts-index) сейчас работает"

h "Ядро и драйвер NVIDIA"
newest=$(ls /lib/modules | grep -E "^[0-9]" | sort -V | tail -1)
echo "  загружено ядро: $(uname -r), самое новое установленное: $newest"
v=$(modinfo -k "$newest" -F version nvidia 2>/dev/null)
[ -n "$v" ] && ok "модуль NVIDIA для $newest есть ($v)" || bad "для $newest НЕТ модуля NVIDIA: не перезагружать, пока не поставлен"
[ -f /var/run/reboot-required ] && warn "система просит перезагрузку" || ok "перезагрузка не требуется"
[ -s /var/lib/unattended-upgrades/kept-back ] && warn "автообновление не смогло поставить: $(cat /var/lib/unattended-upgrades/kept-back)"
echo
