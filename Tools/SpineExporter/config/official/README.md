# 官方 Spine 配置

这里放由 Spine 编辑器保存的通用 Export Settings 文件。配置组合预设：

- `SpineExporter.json`：组合“无”的默认导出配置。
- `SpineExporter.GearSoldier.json`：组合“齿轮士兵”，要求最终 Atlas 含 `pma:true`。

`Generic` 和 `GearSoldier` 目录包含官方 4.1 示例的 JSON、Binary 和纹理打包设置。`GearSoldier/pack.json` 启用 `premultiplyAlpha: true`，Exporter 会检查输出 Atlas 的 PMA 标记。

图片目录使用 `{imagesDirectory}`：读取导出 JSON 的 skeleton.images，按源工程目录解析；缺省为源工程旁的 images 目录。默认 Atlas 名称使用工程名。模板来源：https://github.com/EsotericSoftware/spine-runtimes/tree/4.1/examples/export
