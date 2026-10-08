# 声望药房 ShengWangShop

宁州声望达到 **声名远扬** 后：

1. **大地图拜访门派时**，门派对话菜单（拜访长老/离开）中新增 **「购买药材」** 选项，点击直接打开该门派的药房购买面板；
2. 各大门派药房（场景里的"XX库房"）改用等量的 **灵石** 购买药材，不再需要对应门派贡献点。

## 背景

原版五大门派（竹山宗、离火门、金虹剑派、星河剑派、化尘教）的药房/库房使用**本门贡献货币**购买：

| 门派 | 场景 | 贡献货币 |
|---|---|---|
| 竹山宗 | 竹山宗库房 | 沧元丹 |
| 离火门 | 离火门库房 | 火晶 |
| 金虹剑派 | 金虹剑派库房 | 元灵石 |
| 星河剑派 | 星河剑派库房 | 寒玉 |
| 化尘教 | 化尘教库房 | 砂晶 |

本 mod 让**声名远扬**（宁州声望 500 起，等级 6）的修士凭名望在任何门派药房直接用灵石购买，价格与原版贡献价等价换算（游戏隐含定价 1 贡献 = 100 灵石，即按物品底价购买；例如一袋银月花 = 18 沧元丹 = 1800 灵石）。

在门派拜访对话菜单中提供 **「购买药材」** 入口：玩家站在门派节点上与门派弟子对话时，菜单会多出一个选项，点击即打开该门派的库房购买面板（灵石支付），无需加入该门派。

声望未达标时不介入：菜单不出现「购买药材」，药房行为与原版完全一致（本门弟子仍用贡献货币购买）。



## 配置项（BepInEx 配置文件 `BepInEx/config/chiqingsan.mcs.ShengWangShop.cfg`）

| 配置 | 默认值 | 说明 |
|---|---|---|
| `Plugin.Enabled` | `true` | 总开关 |
| `Plugin.MinShengWangLevel` | `6` | 所需宁州声望等级（6=声名远扬，7=誉满天下；等级区间见游戏内声望面板） |
| `Plugin.AddBuyHerbsOption` | `true` | 是否在门派拜访对话菜单中追加「购买药材」选项 |
| `Plugin.MaxBuyPerAction` | `999` | 单次购买的最大数量。**不建议调大**：游戏原生获得物品是逐袋处理（"一袋XX"会逐袋拆开），数量过大可能导致性能问题 |


## 安装

本地 mod 方式：

1. 打开游戏目录 `本地Mod测试\`
2. 新建文件夹 `ShengWangShop`，其下再建 `plugins`
3. 放入 `ShengWangShop.dll`：

```
本地Mod测试\
  ShengWangShop\
    plugins\
      ShengWangShop.dll
```

4. 启动游戏，BepInEx 日志出现 `声望药房已加载` 即生效

前置：工坊 BepInEx（ID 2824349934）。理论上不与其他 mod 冲突——只接管了 `UIMenPaiShop.RefreshUI` 一处，且声望未达标时完全走原版。

## 构建与代码结构

前置：.NET SDK（LangVersion 10）；本机装有游戏与工坊 BepInEx。路径在 `ShengWangShop.csproj` 中配置。

```
dotnet build -c Release
```

产物：`bin\Release\ShengWangShop.dll`

```
ShengWangShop.csproj        工程文件（引用路径按本机配置）
Plugin.cs                   BepInEx 入口 + 配置项 + 注册 patch
MenPaiShopPatch.cs          Harmony Prefix patch UIMenPaiShop.RefreshUI（灵石药房渲染）+ Close 清理
SectVisitOptionPatch.cs     Harmony Postfix patch Fungus.MenuDialog.AddOption（拜访菜单注入"购买药材"）
```
