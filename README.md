# Smart Haul ![Version](https://img.shields.io/badge/version-1.1.0-blue) ![RimWorld](https://img.shields.io/badge/RimWorld-1.6-green)

[English](#english) | [Русский](#русский)

---

## English

### About
**Smart Haul** is a fork of [Pick Up and Haul](https://steamcommunity.com/sharedfiles/filedetails/?id=1279012058) by Mehni, rebuilt from the ground up. Pawns use their inventory to carry multiple items per trip instead of one-at-a-time vanilla hauling. But that's just the beginning — Smart Haul also auto-collects products from work: harvesting, deconstructing, mining, butchering, and floor removal.

### Compatibility
* **RimWorld:** 1.6+
* **Multiplayer:** Fully compatible with [RimWorld Multiplayer](https://github.com/rwmt/Multiplayer). All jobs, reservations and item tracking are deterministic and sync-safe.
* **Mods:** Works with Combat Extended, Allow Tool, Extended Storage / LWM's Deep Storage, and most other mods.
* **Save-safe:** Can be added or removed mid-save.

### ⚙️ Settings
Everything is configurable via **Options → Mod Settings → Smart Haul**:

* **Auto-haul triggers** — toggle and fine-tune auto-collection after deconstruct, mining, butchering, harvesting
* **Smart cleanup** — respect work priorities (drop at feet vs. haul to storage)
* **Route building** — search radius, max items per trip, rot urgency
* **Visual overlay** — see who's hauling what with route lines and hauler names
* **Cooperation** — nearby idle pawns help when your hauler's inventory is full

### 💡 Key Features

* **Inventory hauling** — pawns stuff multiple items into inventory, carry them all at once to storage
* **Smart routing** — nearest-neighbor algorithm builds efficient pickup routes
* **Auto-collect after work** — deconstructing a wall? Pawn grabs materials automatically. Harvesting? Crops go straight to inventory
* **Item protection** — other haulers won't steal items your worker is about to pick up
* **Safe inventory** — personal items (weapons, medicine "carry X"), inventory stock are never touched
* **Overflow handling** — storage full? Items are dropped at feet, pawn continues with the rest
* **Save/load safe** — all tracking survives save, load, and even mid-job saves

### 🛠️ Installation
1. Install **[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)**
2. Subscribe to Smart Haul or place the mod folder into `RimWorld/Mods/`
3. Enable in mod list, load **after Harmony**
4. That's it — works immediately, no restart required for existing saves

---

## Русский

### О моде
**Smart Haul** — это форк [Pick Up and Haul](https://steamcommunity.com/sharedfiles/filedetails/?id=1279012058) от Mehni, переписанный с нуля. Пешки используют инвентарь чтобы нести несколько предметов за раз вместо ванильной переноски по одному. Но это только начало — Smart Haul также автоматически собирает продукты работы: урожай, материалы от разбора, руду, мясо и ресурсы от снятия покрытий.

### Совместимость
* **RimWorld:** 1.6+
* **Мультиплеер:** Полная совместимость с [RimWorld Multiplayer](https://github.com/rwmt/Multiplayer). Все задачи, резервации и отслеживание предметов детерминированы и безопасны для синхронизации.
* **Моды:** Работает с Combat Extended, Allow Tool, Extended Storage / LWM's Deep Storage и большинством других модов.
* **Безопасен для сохранений:** Можно добавлять и убирать в любой момент.

### ⚙️ Настройки
Всё настраивается через **Настройки → Моды → Smart Haul**:

* **Авто-сбор** — включить/выключить и настроить сбор после разбора, добычи, разделки, урожая
* **Умная уборка** — учёт приоритетов работ (бросить на пол vs. нести на склад)
* **Построение маршрута** — радиус поиска, макс. предметов за рейс, срочность гниющего
* **Визуальное отображение** — линии маршрута и имена носильщиков под предметами
* **Кооперация** — ближайшие бездельники помогают когда инвентарь полон

### 💡 Ключевые фишки

* **Переноска через инвентарь** — пешки набивают карманы и несут всё разом на склад
* **Умный маршрут** — алгоритм ближайшего соседа строит эффективный путь сбора
* **Авто-сбор после работы** — разбираешь стену? Пешка сама подберёт материалы. Собираешь урожай? Картошка летит прямо в инвентарь
* **Защита предметов** — другие носильщики не украдут то, что ваш рабочий сейчас подберёт
* **Безопасный инвентарь** — личные вещи (оружие, лекарства "носить с собой") никогда не выгружаются
* **Обработка переполнения** — склад полон? Предметы падают на пол, пешка продолжает с остальными
* **Устойчивость** — всё отслеживание переживает сохранение, загрузку и даже сохранение посреди задачи

### 🛠️ Установка
1. Установите **[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)**
2. Подпишитесь на Smart Haul или поместите папку мода в `RimWorld/Mods/`
3. Включите в списке модов, загружайте **после Harmony**
4. Готово — работает сразу, перезапуск для существующих сохранений не нужен

---

### Credits
* **Mehni** — original Pick Up and Haul
* **paralax034** — Smart Haul fork, rewrite and new features

[English](#english) | [Русский](#русский)