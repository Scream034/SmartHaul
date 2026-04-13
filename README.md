# Smart Haul ![Version](https://img.shields.io/badge/version-1.3.0-blue) ![RimWorld](https://img.shields.io/badge/RimWorld-1.6-green)

[English](#english) | [Русский](#русский)

---

## English

### About
**Smart Haul** is a performance-focused fork of [Pick Up and Haul](https://steamcommunity.com/sharedfiles/filedetails/?id=1279012058), rebuilt for RimWorld 1.6 with multiplayer support. Pawns use their inventory to carry multiple items per trip, and automatically collect products from work (mining, harvesting, deconstructing, butchering).

### Compatibility
* **RimWorld:** 1.6+
* **Multiplayer:** Fully compatible with [RimWorld Multiplayer](https://github.com/rwmt/Multiplayer). All jobs are deterministic and sync-safe.
* **Mods:** Works with Combat Extended, Allow Tool, Deep Storage, and most other mods.
* **Save-safe:** Can be added or removed mid-save.

### ⚙️ Settings
**Options → Mod Settings → Smart Haul:**

* **Work Types** — toggle auto-collection for Mining, Deconstruct, Harvest, Crafting, Floor removal
* **Smart Hauling** — enable multi-item hauling with configurable pickup radius (default 10 cells)
* **Inventory Threshold** — when to haul collected items (80% default for work, 100% for hauling jobs)
* **Chunks** — option to ignore stone chunks

### 💡 Features

* **Multi-item hauling** — pawns pick up nearby haulables into inventory, deliver all at once
* **Auto-collection** — after mining ore, harvesting crops, or butchering, pawn automatically grabs products
* **Smart priority** — perishable food collected first, then items for same storage, then others
* **Work integration** — seamlessly integrates with vanilla job system via WorkGiver
* **Draft drop** — drops collected items when drafted (R key)
* **Safe inventory** — never touches personal gear, weapons, or inventory stock settings

### 🛠️ Installation
1. Install **[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)**
2. Subscribe or extract to `RimWorld/Mods/`
3. Enable after Harmony in mod list
4. Works immediately on existing saves

---

## Русский

### О моде
**Smart Haul** — форк [Pick Up and Haul](https://steamcommunity.com/sharedfiles/filedetails/?id=1279012058), переписанный для RimWorld 1.6 с поддержкой мультиплеера. Пешки несут несколько предметов в инвентаре за раз и автоматически собирают продукты работы (руда, урожай, разделка).

### Совместимость
* **RimWorld:** 1.6+
* **Мультиплеер:** Полная совместимость с [RimWorld Multiplayer](https://github.com/rwmt/Multiplayer)
* **Моды:** Работает с Combat Extended, Allow Tool, Deep Storage
* **Безопасен для сохранений:** Можно добавить/убрать в любой момент

### ⚙️ Настройки
**Настройки → Моды → Smart Haul:**

* **Типы работ** — включить авто-сбор для Добычи, Разбора, Урожая, Крафта, Снятия пола
* **Умная переноска** — перенос нескольких предметов с настраиваемым радиусом (по умолчанию 10 клеток)
* **Порог инвентаря** — когда нести собранное (80% по умолчанию)
* **Камни** — опция игнорировать каменные обломки

### 💡 Возможности

* **Мульти-переноска** — пешка подбирает ближайшие предметы в инвентарь и несёт всё разом
* **Авто-сбор** — после добычи руды, сбора урожая или разделки пешка автоматически забирает продукты
* **Умный приоритет** — сначала скоропорт, затем предметы на тот же склад, затем остальное
* **Интеграция** — работает через систему WorkGiver, совместимо с приоритетами работ
* **Сброс при призыве** — выгружает предметы при нажатии R (призыв)
* **Безопасность** — никогда не трогает личное снаряжение и оружие

### 🛠️ Установка
1. Установите **[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)**
2. Подпишитесь или распакуйте в `RimWorld/Mods/`
3. Включите после Harmony в списке модов
4. Работает сразу на существующих сохранениях

---

### Credits
* **Mehni** — original Pick Up and Haul
* **paralax034** — Smart Haul fork, rewrite for 1.6, MP compatibility