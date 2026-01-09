# com.barkar.cg_tools

Короткая памятка по основным инструментам.

## Кастомные мипы (Custom MipMap Generator)
Для чего: генерация мип-цепочки на GPU с контролем фильтрации, Toksvig и форматами, с выводом в `.cmips` или отдельные варианты.

Как использовать:
- Открой окно `Tools/Custom MipMap Generator/Open Window`.
- Выбери текстуру и тип (`Color`, `Normal Map`, `Packed/Data`), настрой фильтрацию/alpha-режим и вывод.
- Нажми `Generate Custom Mip File (.cmips)` либо сгенерируй варианты `.mobile`/`.standalone`.

Автогенерация:
- Создай профиль-сет: `Tools/Custom MipMap Generator/Create Profile Set`.
- В профилях укажи суффиксы имен файлов и настройки.
- Авто-импорт будет создавать `.cmips` на импорт текстур; полная регенерация: `Tools/Custom MipMap Generator/Regenerate CMips From Profiles`.

Важно:
- Для Toksvig нормалей включи `Toksvig In Alpha` и используй шейдер с поддержкой (например, ARMLit: `Use Toksvig`).
- `.cmips` импортируется отдельным импортёром и пережимается под активный билд-таргет.

## Квантайз мешей (Mesh Quantization)
Для чего: упаковать нормали и тангенты в `vertex colors` (Color32), уменьшить вертексные данные и чтение.

Как использовать:
- Открой `Tools/Mesh Quantization/Settings` и задай суффикс (по умолчанию `_MQ`).
- Модели с этим суффиксом (`.fbx/.obj/.dae/.blend`) автоматически квантуются на импорт.
- Вариант `Bake Quantized Mesh Assets` создаёт `.asset`-копии рядом с моделью.

Важно:
- Если `vertex colors` уже заняты и `Overwrite Vertex Colors` выключен, квантизация будет пропущена.
- Требуются корректные нормали и тангенты; при `Read/Write` = Off квантизация пропускается (можно включить авто-разрешение).
- В шейдере нужно включить `_MQ_QUANTIZED` (в ARMLit это `Use Quantized Normals`).

## Шейдер ARMLit
Для чего: PBR-шейдер под URP с ARM картой, normal, specular/clearcoat/sheen (glTF-расширения), эмиссией и Toksvig.

Как использовать:
- Создай материал с шейдером `CGTools/ARMLit`.
- Назначь карты: `_BaseMap` (albedo/alpha), `_AdditionalMap` (R=AO, G=Roughness, B=Metallic), `_NormalMap`, при необходимости specular/clearcoat/sheen/emission.
- Включи опции `Use Specular`, `Use Clearcoat`, `Use Sheen`, `Use Alpha Clip` по необходимости.
- Для Toksvig нормалей включи `Use Toksvig` и задай `ToksvigStrength`.
- Для квантизированных мешей включи `Use Quantized Normals`.

Компонент для нужных текстур:
- `GltfBrdfLutGlobal` выставляет глобальные LUT-текстуры (`_GltfBrdfLut`, `_GltfSheenELut`, `_GltfCharlieLut`) для IBL.
- Добавь компонент на объект в сцене или назначь текстуры вручную (по умолчанию берутся из `Runtime/Resources`).

## GTAO Render Feature
Для чего: экранное AO на базе XeGTAO, опционально с bent normals для IBL.

Как использовать:
- В URP Renderer добавь `GTAORenderFeature`.
- Назначь `Shaders/GTAO/XeGTAO.compute` в поле `Compute Shader`.
- Настрой `Quality`, `Denoise`, `Resolution`, `Temporal`, `Intensity` и др.
- Если нужен bent normals, включи `Bent Normals` (ARMLit будет использовать `_GTAO_BENT_NORMALS` и `_GTAOBentNormalTexture` автоматически).

Важно:
- При `Temporal = On` нужны motion vectors.
- Фича выставляет `_SCREEN_SPACE_OCCLUSION` и глобальную SSAO-текстуру для шейдеров.
