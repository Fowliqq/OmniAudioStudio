# OmniAudio Studio 🎵

<p align="center">
  <img src="assets/icon.png" width="128" height="128" alt="OmniAudio Studio Logo"/>
</p>

<p align="center">
  <strong>Современный высокопроизводительный аудиоконвертер для Windows 11 / 10</strong><br>
  Построен на базе <b>C# .NET 8 (WPF + Wpf.Ui)</b> с аппаратным ускорением <b>FFmpeg</b>, сверхбыстрым чтением тегов <b>TagLibSharp</b> и дизайном в стиле <b>Fluent UI / Mica</b>.
</p>

<p align="center">
  <a href="https://github.com"><img src="https://img.shields.io/badge/.NET-8.0%20LTS-512BD4?logo=dotnet&logoColor=white" alt=".NET 8"/></a>
  <a href="https://github.com"><img src="https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-0078D6?logo=windows&logoColor=white" alt="Windows"/></a>
  <a href="https://github.com"><img src="https://img.shields.io/badge/UI-Fluent%20Design%20(Mica)-00A4EF" alt="Fluent Design"/></a>
  <a href="https://github.com"><img src="https://img.shields.io/badge/Engine-FFmpeg%209.0-007808?logo=ffmpeg&logoColor=white" alt="FFmpeg"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License"/></a>
</p>

---

## 🌟 Ключевые возможности

- 🚀 **Экстремальная скорость работы**: чистый C# .NET 8, многопоточная очередь с пулом `SemaphoreSlim` по количеству реальных ядер процессора.
- ⚡ **Мгновенный анализ треков**: `TagLibSharp` считывает точную длительность и аудиопараметры за миллисекунды без запуска внешних процессов.
- 🎨 **Современный Progressive UI/UX**:
  - Нативный Fluent Design с эффектом **Mica** на Windows 11 и мягким Acrylic на Windows 10.
  - Кастомный тонкий TitleBar с переключением темы (тёмная/светлая).
  - Сетка отступов 8px, мягкие pill-бейджи, проработанные состояния (Default, Hover, Active, Disabled).
- ⚖ **Динамический расчёт веса файла**:
  - До конвертации отображается расчётный размер файла на основе битрейта, каналов и длительности (например, `[FLAC · ~1.0 MB]`).
  - После конвертации автоматически выводится точный физический размер готового файла на диске.
- 📥 **Нативный Drag & Drop**:
  - Перетаскивание файлов и целых папок напрямую из Проводника Windows.
  - Рекурсивный поиск аудиофайлов в папках с защитой от дублирования.
- 🛡 **Безопасность перезаписи**:
  - При совпадении исходного и целевого файла имя безопасно дополняется суффиксом `_converted.ext`.

---

## 🎛 Поддерживаемые форматы

| Категория | Форматы | Особенности |
|---|---|---|
| **Lossless (Без потерь)** | **FLAC**, **WAV**, **ALAC**, **AIFF** | Студийное качество, сохранение всех нюансов звучания, уровень сжатия 5 для FLAC, 16/24-bit PCM. |
| **Lossy (Сжатие)** | **MP3**, **AAC / M4A**, **OGG**, **OPUS**, **WMA** | Регулируемый битрейт до 320 kbps (256 kbps для Opus), LAME ID3v2.3 теги, `+faststart` стриминг. |
| **Входные форматы** | 14+ форматов | `.mp3`, `.flac`, `.wav`, `.m4a`, `.aac`, `.ogg`, `.opus`, `.wma`, `.aiff`, `.aif`, `.ape`, `.alac`, `.wv`, `.mka` |

---

## 🚀 Быстрый старт

### Вариант 1. Запуск готового релиза (Standalone)
Скачайте готовый `OmniAudioStudio.exe` из раздела [Releases](https://github.com) и запустите. Никаких сторонних библиотек и установки .NET не требуется.

### Вариант 2. Сборка из исходного кода

#### 1. Предварительные требования
- Windows 10 (1903+) или Windows 11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

#### 2. Клонирование репозитория
```powershell
git clone https://github.com/your-username/OmniAudioStudio.git
cd OmniAudioStudio
```

#### 3. Скачивание кодека FFmpeg
Для локального автономного режима запустите входящий в комплект скрипт:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\download-ffmpeg.ps1
```
*(Или установите системно через `winget install Gyan.FFmpeg`)*

#### 4. Сборка и запуск
```powershell
# Сборка проекта
dotnet build OmniAudioStudio.sln -c Release

# Запуск тестов
dotnet run --project OmniAudioStudio.Tests/OmniAudioStudio.Tests.csproj -c Release

# Сборка единого автономного .exe
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -PublishSingleFile
```

---

## 🏗 Архитектура проекта

```text
OmniAudioStudio/
├── OmniAudioStudio.sln          # Главное решение Visual Studio
├── assets/                      # Векторные и растровые иконки высокого разрешения
├── bin/                         # Директория для ffmpeg.exe и ffprobe.exe
├── scripts/                     # Скрипты автоматизации (загрузка кодеков, сборка, тесты)
├── OmniAudioStudio.Desktop/     # Основное приложение WPF (.NET 8)
│   ├── Models/                  # AudioFormatProfile, ConversionTask, Enums
│   ├── Services/                # FFmpegLocator, AudioMetadataService, ConversionEngine, QueueManager
│   ├── ViewModels/              # MainViewModel (реактивный MVVM)
│   ├── Converters/              # XAML Value Converters
│   ├── MainWindow.xaml          # Fluent UI разметка
│   └── App.xaml                 # Глобальные стили и темы
└── OmniAudioStudio.Tests/       # Интеграционные тесты транскодирования и верификации
```

---

## 📖 Документация и решение проблем

Подробное руководство архитектора и **исчерпывающая матрица всех возможных ошибок и способов их решения** находятся в файле:
👉 **[INFO.md](INFO.md)**

---

## 📄 Лицензия

Проект распространяется под свободной лицензией **MIT**. Подробности в файле [LICENSE](LICENSE).
