# BetterPresets

BetterPresets 是一个给 `Sephiria` 使用的本地 AddOn Mod。它在游戏原版“预设设置”面板上增加一个“外部预设”入口，用独立的 `presets.json` 保存更多预设，再按需写回原版 1-5 号槽位，或直接加载到当前配置。

这个 Mod 只管理本机预设数据，不改联网协议，也不要求联机的其他玩家同时安装。

## 功能

- 外部预设列表，不受原版 5 个槽位限制。
- 从当前配置创建外部预设。
- 从当前原版槽位创建外部预设。
- 将选中的外部预设写入原版目标槽位 1-5。
- 将选中的外部预设直接加载到当前配置。
- 支持预设改名、删除、刷新列表。
- 显示武器、角色、皮肤、才能、初始神器、许愿泉、偏好神器、水果串等信息。
- 神器图标悬停时复用原版详情面板。
- `presets.json` 缺失时会自动创建。
- 保存时会保留有限数量备份，降低预设文件损坏风险。

## 下载与安装

普通玩家建议从 GitHub Releases 下载已构建好的压缩包：

```text
BetterPresets-v0.1.4.zip
```

解压后会得到 `BetterPresets` 文件夹，把这个文件夹放到游戏安装目录下：

```text
<Sephiria 安装目录>/AddOns/
```

最终目录结构应类似：

```text
<Sephiria 安装目录>/AddOns/BetterPresets/metadata.json
<Sephiria 安装目录>/AddOns/BetterPresets/BetterPresets.dll
<Sephiria 安装目录>/AddOns/BetterPresets/BetterPresets.Core.dll
```

启动游戏后，打开原版“预设设置”面板，点击上方的“外部预设”按钮即可使用。

## 手动安装

在游戏安装目录下创建或打开：

```text
<Sephiria 安装目录>/AddOns/BetterPresets/
```

放入以下文件：

```text
metadata.json
BetterPresets.dll
BetterPresets.Core.dll
```

不需要复制 `.pdb` 或 `.deps.json` 文件。

## 使用方式

1. 进入游戏并打开原版“预设设置”面板。
2. 点击“外部预设”。
3. 使用左侧按钮创建、刷新或删除外部预设。
4. 在右上角选择原版目标槽位 1-5。
5. 点击“写入原版目标槽位”可把外部预设写回原版槽位。
6. 点击“加载到当前配置”可直接把外部预设应用到当前配置。

## 数据文件

外部预设保存在：

```text
<Sephiria 安装目录>/AddOns/BetterPresets/presets.json
```

如果文件不存在，Mod 会在启动时自动创建一个空列表。

保存时会保留备份文件：

```text
presets.json.bak
presets.json.bak.1
presets.json.bak.2
```

如果主文件损坏，Mod 会尝试从备份读取。

## 兼容性说明

- 不需要 BepInEx。
- 使用游戏自带 AddOn 加载机制。
- `BetterPresets.dll` 是轻量入口。
- `BetterPresets.Core.dll` 会在原版预设面板打开后延迟加载，尽量减少进入存档时的卡顿。
- Mod 只在本地读写外部预设文件，不会修改游戏联网逻辑。

## 从源码构建

需要安装 .NET SDK，并能访问游戏目录中的托管程序集：

```text
<Sephiria 安装目录>/Sephiria_Data/Managed/
```

如果你的游戏安装路径不同，请按本机路径调整 `.csproj` 中的程序集引用路径。
也可以在构建时传入 `SephiriaManagedDir`，或设置环境变量 `SEPHIRIA_MANAGED_DIR`。

构建命令：

```powershell
$managed = "<Sephiria 安装目录>/Sephiria_Data/Managed"
dotnet build SephiriaBetterPresets.csproj -c Release -p:SephiriaManagedDir="$managed"
dotnet build SephiriaBetterPresets.Core.csproj -c Release -p:SephiriaManagedDir="$managed"
```

构建产物会输出到 `dist/`。发布给普通玩家时，只需要打包以下文件：

```text
BetterPresets/metadata.json
BetterPresets/BetterPresets.dll
BetterPresets/BetterPresets.Core.dll
BetterPresets/README.md
```

## 仓库内容

```text
metadata.json                 AddOn 元数据
src/                           源码
SephiriaBetterPresets.csproj   入口 DLL 工程
SephiriaBetterPresets.Core.csproj
README.md
```

`dist/`、`bin/`、`obj/`、本地预设文件和备份文件不建议提交到仓库。
