# NoCableLauncher 2

[![build](https://github.com/evg00000/NoCableLauncher/actions/workflows/build.yml/badge.svg)](https://github.com/evg00000/NoCableLauncher/actions/workflows/build.yml)

Лончер для **Rocksmith 2014 / Remastered**, с которым можно играть без кабеля Real Tone: подойдёт любая
звуковая карта или аудиоинтерфейс. Это форк [Maxx53/NoCableLauncher](https://github.com/Maxx53/NoCableLauncher)
с переписанной работой со звуком и встроенным управлением [RS_ASIO](https://github.com/mdias/rs_asio).

*English summary below.*

## Возможности

- **Два режима звука:**
  - **NoCable (WASAPI)** — лончер подменяет в памяти игры ID кабеля Real Tone на ID вашего устройства;
  - **RS_ASIO** — звук идёт через ASIO-драйвер вашего интерфейса с минимальной задержкой. Лончер сам
    кладёт RS_ASIO в папку игры, настраивает `RS_ASIO.ini` и `Rocksmith.ini`, а при переключении
    обратно в NoCable убирает его.
- **Не нужно отключать лишние устройства вручную.** На время игры лончер скрывает остальные входы и
  выходы и делает выбранные устройствами по умолчанию.
- **Автоматическая частота и разрядность** (по умолчанию 48000 Гц / 24 бит) для входа и выхода.
- **После выхода из игры всё возвращается как было.** Каждое изменение сначала записывается в журнал,
  поэтому даже если лончер упадёт, устройства вернутся при следующем запуске (или через `NCL_RestoreAudio.bat`).
- **Исправлена ошибка «Offcets not found»:** поиск в памяти повторяется, пока игра не распакуется,
  записанный патч проверяется, перезапуск игры Steam'ом отслеживается.
- Мультиплеер на двоих (Ctrl+M), как в оригинале.
- Окно настроек на русском, настройки хранятся в обычном `NoCableLauncher.ini`, все шаги пишутся в `NoCableLauncher.log`.

## Установка

1. Скачайте `NoCableLauncher.zip` со страницы [Releases](https://github.com/evg00000/NoCableLauncher/releases)
   и распакуйте в любую папку.
2. Запустите `NoCableLauncher.exe`. При первом запуске откроются настройки: выберите вход, к которому
   подключена гитара, и выход для наушников.
3. Дальше просто запускайте `NoCableLauncher.exe` вместо игры.

Настройки можно открыть позже через `NCL_Settings.bat` или запуском лончера с зажатым **Shift**.

### Режим RS_ASIO

1. Скачайте `release-*.zip` со страницы [RS_ASIO releases](https://github.com/mdias/rs_asio/releases).
2. Положите `avrt.dll`, `RS_ASIO.dll` и `RS_ASIO.ini` в папку `RS_ASIO` рядом с лончером.
3. В настройках на вкладке «Игра» выберите **RS_ASIO**, на вкладке «RS_ASIO» — драйвер и номер входа
   (0 — первый вход интерфейса).

Частоту (48 кГц) и размер буфера в этом режиме задают в панели ASIO-драйвера. Ограничения RS_ASIO:
только Steam-версия игры и 32-битный ASIO-драйвер ([список проверенных интерфейсов](https://github.com/mdias/rs_asio)).

### Задержка

Для меньшей задержки подберите `LatencyBuffer` в `Rocksmith.ini` (4 → 3 → 2) и размер буфера
ASIO-драйвера: оставьте самые маленькие значения, при которых нет треска. Лончер `LatencyBuffer` не меняет.

## Командная строка

| Команда | Действие |
|---|---|
| `NoCableLauncher.exe` | запустить игру |
| `NoCableLauncher.exe -set` | открыть настройки |
| `NoCableLauncher.exe -restore` | вернуть скрытые устройства, устройства по умолчанию и форматы |

## Сборка из исходников

Visual Studio не нужна. Достаточно запустить `build.bat`: он собирает exe компилятором `csc.exe`, который
входит в .NET Framework 4.x (есть в Windows 10/11). Код написан на C# 5, проект собирается под x86, как и сама игра.

Каждый push собирается в GitHub Actions. При публикации тега `v*` релиз создаётся автоматически.

```
src/
  Program.cs       порядок запуска, режимы, мультиплеер
  AudioSetup.cs    скрытие устройств, устройства по умолчанию, формат, восстановление
  CoreAudio.cs     взаимодействие с Windows Core Audio и IPolicyConfig
  Patcher.cs       поиск и подмена VID/PID в памяти игры
  RsAsio.cs        установка и настройка RS_ASIO, поиск игры в Steam
  SettingsForm.cs  окно настроек
  Config.cs        INI-настройки и журнал
```

## Если что-то не работает

Создайте [issue](https://github.com/evg00000/NoCableLauncher/issues) и приложите `NoCableLauncher.log`
(и `NoCableLauncher.log.old` от предыдущего запуска). В режиме RS_ASIO пригодится и `RS_ASIO-log.txt` из папки игры.

---

## English

A launcher for **Rocksmith 2014 / Remastered** that lets you play without the Real Tone Cable, using any
audio interface. It is a fork of Maxx53/NoCableLauncher with a rewritten audio layer.

- **Two modes:** patching the cable VID/PID in game memory (WASAPI), or [RS_ASIO](https://github.com/mdias/rs_asio),
  installed and configured by the launcher.
- While the game runs, other audio endpoints are hidden automatically, the selected devices become the
  defaults, and the sample rate and bit depth are set (48 kHz / 24-bit by default). Everything is restored
  on exit and journaled, so the launcher repairs it even after a crash.
- The "Offcets not found" error is fixed: the memory is scanned region by region with retries, the patch
  is verified, and Steam relaunches are handled.
- The UI is in Russian. Settings are kept in `NoCableLauncher.ini`, and the log is written to `NoCableLauncher.log`.
- To build, run `build.bat`; it needs only .NET Framework 4.x, which comes with Windows.

## Благодарности / Credits

- [Maxx53](https://github.com/Maxx53/NoCableLauncher) — оригинальный NoCableLauncher (GPL v2).
- [mdias](https://github.com/mdias/rs_asio) — RS_ASIO (MIT); распространяется отдельно, в этот репозиторий не входит.
- Ray Molenkamp — исходная обёртка CoreAudioApi, на которой основан `CoreAudio.cs`.
- [Mywk](https://github.com/Mywk/NoCableLauncher) — форк, из которого взята идея отключать только конфликтующие устройства.

## Лицензия

[GNU GPL v2](LICENSE), как у оригинального проекта.

Rocksmith — торговая марка Ubisoft. Проект не связан с Ubisoft и не одобрен ею.
Используйте на свой страх и риск.
