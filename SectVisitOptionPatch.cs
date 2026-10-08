using System;
using System.Collections.Generic;
using Fungus;
using HarmonyLib;
using KBEngine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShengWangShop;

/// <summary>
/// 在大地图门派对话的"拜访长老"菜单中追加"购买药材"选项。
///
/// 原版行为：玩家在大地图上门派节点处触发门派对话（Fungus Flowchart + MenuDialog），
/// 菜单选项为"拜访长老/离开"。非本门弟子没有渠道进入该门派的药房场景（S115~S155
/// 的"XX库房"），无法购买其门派药材。
///
/// 本补丁：当 MenuDialog 添加"拜访长老"选项时，若宁州声望等级达标且能识别出玩家
/// 所在的门派节点，就追加一个"购买药材"选项；点击后关闭对话菜单，直接打开该门派
/// 的药房购买面板（由 MenPaiShopPatch 接管为灵石支付）。
///
/// 门派识别：对话触发时玩家位于门派节点上（NowMapIndex），读取该节点对象下的名牌
/// Text（显示"竹山宗/离火门/金虹剑派/星河剑派/化尘教"）映射到药房子场景
/// （S115/S125/S135/S145/S155，NomelShopJsonData.threeScene）。
/// </summary>
[HarmonyPatch(typeof(MenuDialog), "AddOption", new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(Block) })]
public static class SectVisitOptionPatch
{
	/// <summary>原版门派拜访菜单中的选项文本。</summary>
	private const string VisitElderOptionText = "拜访长老";

	/// <summary>本 mod 注入的选项文本。</summary>
	private const string BuyHerbsOptionText = "购买药材";

	/// <summary>大地图场景名。</summary>
	private const string AllMapsSceneName = "AllMaps";

	/// <summary>门派名牌文本 → 该门派药房（库房）所在子场景（NomelShopJsonData.threeScene）。</summary>
	private static readonly Dictionary<string, string> SectNameToShopScene = new Dictionary<string, string>
	{
		{ "竹山宗", "S115" },
		{ "离火门", "S125" },
		{ "金虹剑派", "S135" },
		{ "星河剑派", "S145" },
		{ "化尘教", "S155" }
	};

	/// <summary>MenuDialog 私有重载 AddOption(string, bool, bool, UnityAction)，用于追加选项。</summary>
	private static System.Reflection.MethodInfo _addOptionWithAction;

	public static void Postfix(MenuDialog __instance, string text, bool interactable, bool hideOption)
	{
		try
		{
			if (!Plugin.Config_Enabled.Value || !Plugin.Config_AddBuyHerbsOption.Value)
			{
				return;
			}
			if (text != VisitElderOptionText || hideOption || !interactable)
			{
				return;
			}
			if (PlayerEx.GetNingZhouShengWangLevel() < Plugin.Config_MinShengWangLevel.Value)
			{
				return;
			}
			if (Tools.getScreenName() != AllMapsSceneName)
			{
				return;
			}
			string shopScene = DetectSectShopScene();
			if (shopScene == null)
			{
				Plugin.Log.LogInfo("[声望药房] 检测到" + VisitElderOptionText + "菜单，但未识别到门派节点，不注入" + BuyHerbsOptionText);
				return;
			}
			if (_addOptionWithAction == null)
			{
				_addOptionWithAction = AccessTools.Method(typeof(MenuDialog), "AddOption",
					new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(UnityAction) });
			}
			if (_addOptionWithAction == null)
			{
				Plugin.Log.LogError("[声望药房] 未找到 MenuDialog.AddOption(UnityAction) 重载，无法注入选项");
				return;
			}
			MenuDialog dialog = __instance;
			string scene = shopScene;
			UnityAction onBuyHerbs = delegate
			{
				CloseMenuDialog(dialog);
				MenPaiShopPatch.OpenSectShop(scene);
			};
			object added = _addOptionWithAction.Invoke(__instance, new object[] { BuyHerbsOptionText, true, false, onBuyHerbs });
			if (added is bool success && !success)
			{
				Plugin.Log.LogWarning("[声望药房] 菜单按钮位已用满，本次未能追加\"" + BuyHerbsOptionText + "\"选项");
			}
		}
		catch (Exception e)
		{
			Plugin.Log.LogError("[声望药房] 注入" + BuyHerbsOptionText + "选项失败：" + e);
		}
	}

	/// <summary>模仿 Fungus Menu 选项默认回调：停止协程、清空并关闭菜单对话框。</summary>
	private static void CloseMenuDialog(MenuDialog dialog)
	{
		if (EventSystem.current != null)
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
		((MonoBehaviour)dialog).StopAllCoroutines();
		dialog.Clear();
		dialog.HideSayDialog();
		dialog.gameObject.SetActive(false);
		try
		{
			Tools.instance.getPlayer()?.StreamData?.FungusSaveMgr?.ClearMenu();
		}
		catch (Exception)
		{
		}
	}

	/// <summary>
	/// 识别玩家当前所在门派节点对应的药房子场景。
	/// 优先读取玩家当前大地图节点对象子树中的名牌 Text，匹配门派名；
	/// 失败时回退到当前对话文本（SayDialog）中匹配门派名。
	/// </summary>
	private static string DetectSectShopScene()
	{
		string scene = DetectFromNodeNameplate();
		if (scene == null)
		{
			scene = DetectFromSayDialogText();
		}
		return scene;
	}

	private static string DetectFromNodeNameplate()
	{
		Avatar player = Tools.instance.getPlayer();
		if (player == null)
		{
			return null;
		}
		if ((UnityEngine.Object)AllMapManage.instance == (UnityEngine.Object)null || AllMapManage.instance.mapIndex == null)
		{
			return null;
		}
		if (!AllMapManage.instance.mapIndex.TryGetValue(player.NowMapIndex, out BaseMapCompont node) || node == null)
		{
			return null;
		}
		Text[] texts = ((Component)node).GetComponentsInChildren<Text>(true);
		foreach (Text t in texts)
		{
			string scene = MatchSectScene(t.text);
			if (scene != null)
			{
				return scene;
			}
		}
		return null;
	}

	private static string DetectFromSayDialogText()
	{
		SayDialog sayDialog = SayDialog.ActiveSayDialog;
		if ((UnityEngine.Object)sayDialog == (UnityEngine.Object)null)
		{
			return null;
		}
		try
		{
			return MatchSectScene(sayDialog.StoryText);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static string MatchSectScene(string raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return null;
		}
		string label = ToolsEx.ToCN(raw);
		foreach (KeyValuePair<string, string> kv in SectNameToShopScene)
		{
			if (label.Contains(kv.Key))
			{
				return kv.Value;
			}
		}
		return null;
	}
}
