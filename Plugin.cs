using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ShengWangShop;

[BepInPlugin("chiqingsan.mcs.ShengWangShop", "声望药房", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
	internal static ManualLogSource Log;

	internal static ConfigEntry<bool> Config_Enabled;

	internal static ConfigEntry<int> Config_MinShengWangLevel;

	internal static ConfigEntry<bool> Config_AddBuyHerbsOption;

	internal static ConfigEntry<int> Config_MaxBuyPerAction;

	public void Start()
	{
		Log = Logger;
		Config_Enabled = Config.Bind("Plugin", "Enabled", true, "声望药房（启用：true/禁用：false）");
		Config_MinShengWangLevel = Config.Bind("Plugin", "MinShengWangLevel", 6, "购买门派药房所需的宁州声望等级（6=声名远扬，7=誉满天下；各等级声望区间见游戏内声望面板）");
		Config_AddBuyHerbsOption = Config.Bind("Plugin", "AddBuyHerbsOption", true, "大地图门派对话的\"拜访长老\"菜单中追加\"购买药材\"选项（启用：true/禁用：false）");
		Config_MaxBuyPerAction = Config.Bind("Plugin", "MaxBuyPerAction", 999, "单次购买的最大数量。游戏获得物品是逐袋处理（一袋XX会逐袋拆开），数量过大（数万）会导致游戏卡死，不建议超过几千");
		if (Config_MaxBuyPerAction.Value < 1)
		{
			Config_MaxBuyPerAction.Value = 1;
		}
		// 场景切换（读档/传送等）后清除药房场景覆盖，防止残留导致后续打开药房显示错误门派
		UnityEngine.SceneManagement.SceneManager.sceneLoaded += delegate
		{
			MenPaiShopPatch.OverrideShopScene = null;
		};
		try
		{
			Harmony.CreateAndPatchAll(typeof(MenPaiShopPatch), null);
			Harmony.CreateAndPatchAll(typeof(MenPaiShopClosePatch), null);
			Harmony.CreateAndPatchAll(typeof(SectVisitOptionPatch), null);
			Log.LogInfo($"声望药房已加载：宁州声望等级 ≥ {Config_MinShengWangLevel.Value} 时，门派药房（XX库房）改用灵石购买，并在拜访门派菜单中提供\"购买药材\"入口");
		}
		catch (Exception e)
		{
			Log.LogError("补丁应用失败（游戏代码可能已变动，本补丁未生效）：" + e);
		}
	}
}
