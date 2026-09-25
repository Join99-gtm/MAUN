# GooseDeluxe — мод для Desktop Goose 0.31

Графика и анимации для гуся, не трогая сам `GooseDesktop.exe`. Мод гасит родной рендер
(окно гуся с цветовым ключом не умеет полупрозрачность) и рисует гуся в собственном
окне с честной per-pixel alpha (`UpdateLayeredWindow`).

Что добавляет:

- сглаженный гусь без «лесенки», мягкая тень, ноги;
- перевалка при ходьбе, плавные развороты, squash & stretch при разгоне/торможении;
- голова поворачивается за курсором, глаза следят, моргание;
- idle-анимации: зевок с потягиванием, встряхивание головы (перья летят), чистка перьев;
- крылья раскрываются при разбеге и хлопают при гудке, клюв открывается, всплывает «HONK!»;
- пыль из-под лап на бегу, «линии борьбы» у клюва, пока гусь держит курсор;
- шляпы: цилиндр, колпак, шапка Деда Мороза; масштаб гуся.

Всё выключается по отдельности в `GooseDeluxe.ini`.

## Установка

1. Скопировать `GooseDeluxe.dll` и `GooseDeluxe.ini` в `Assets/Mods/GooseDeluxe/` рядом с `GooseDesktop.exe`
   (папку создать; в ней не должно быть `GooseModdingAPI.dll`).
2. В `config.ini` поставить `EnableMods=True`.
3. Запустить гуся, в диалоге про моды нажать «Yes».

Если что-то пошло не так, мод сам отключается и возвращает родной рендер, а причина
пишется в `Assets/Mods/GooseDeluxe/GooseDeluxe.log`.

## Сборка

```
dotnet build GooseDeluxe -c Release
```

Результат: `GooseDeluxe/bin/Release/net452/GooseDeluxe.dll` (+ `GooseDeluxe.ini`).
`lib/GooseModdingAPI.dll` — официальный API из папки `FOR MOD-MAKERS` дистрибутива гуся.

## Превью без Windows

`tools/Preview` компилирует аниматор и рендерер мода с флагом `HEADLESS` и отрисовывает
набор состояний гуся в PNG через libgdiplus:

```
dotnet run --project tools/Preview -c Release -- preview-out
```

`preview-out/contact-sheet.png` — все сцены на одном листе.
