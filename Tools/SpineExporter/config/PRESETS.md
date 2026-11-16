# 配置预设

- `SpineExporter.json` + `Rules.None.json`：配置组合“无”。使用默认导出配置，验证规则关闭。
- `SpineExporter.GearSoldier.json` + `Rules.GearSoldier.json`：配置组合“齿轮士兵”。要求导出 Atlas 使用 `pma:true`；验证要求 `attack` 动画包含 `attack` 事件，失败时 SpineTool 弹窗；缺少 `BulletPostion` 骨骼只记录 Warning，不阻断导出。

两套设置均随包提供官方 4.1 的 JSON、Binary 导出模板和纹理打包模板。齿轮士兵的预乘设置位于 `official/GearSoldier/pack.json`，Exporter 检查最终 Atlas 的 `pma:true`（兼容空格）。图片目录从导出 JSON 读取，相对源工程定位。
