using System;
using System.Collections.Generic;
using System.Text;
using GUIPackage;
using HarmonyLib;
using JSONClass;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShengWangShop;

/// <summary>
/// 宁州声望达到指定等级（默认 6 = 声名远扬）后，接管门派药房（UIMenPaiShop，
/// 五大门派场景的"XX库房"）的 RefreshUI：支付货币由本门贡献货币（沧元丹/元灵石/
/// 寒玉/火晶/砂晶）改为灵石，价格按原版隐含换算 1 贡献 = 100 灵石。
///
/// 原版行为：任何人都能打开药房查看，但货币是各门派贡献货币，非本门弟子没有
/// 货币，只能看不能买。声望未达标时本补丁不介入，行为与原版完全一致。
///
/// 渲染流程复刻原版 UIMenPaiShop.RefreshUI，仅支付路径不同：
///   原版  Player.getItemNum(贡献货币) / removeItem(贡献货币, 总价)
///   本版  (int)Player.money           / Player.AddMoney(-总价)
/// （灵石不是背包物品，走 money 字段，与游戏自带兑换商店 UIDuiHuanShop 的
///   EXGoodsID == 10035 分支一致。）
/// </summary>
[HarmonyPatch(typeof(UIMenPaiShop), "RefreshUI")]
public static class MenPaiShopPatch
{
	/// <summary>灵石物品 ID（配表 d_items.py.items.json，type 7 货币类）。</summary>
	private const int LingShiItemId = 10035;

	/// <summary>
	/// 1 点门派贡献货币折合的灵石数（药房 percent=100，物品底价的 1% 用贡献支付）。
	/// </summary>
	private const int LingShiPerContribution = 100;

	/// <summary>
	/// 玩家持有灵石数。money 是 ulong 字段，但游戏自身（AddMoney 等）按 int 域运算，
	/// 超过 int.MaxValue 时 (int) 截断为负数会破坏一切比较，这里统一封顶读取。
	/// </summary>
	private static int GetPlayerMoneyInt()
	{
		ulong money = PlayerEx.Player.money;
		return money > (ulong)int.MaxValue ? int.MaxValue : (int)money;
	}

	/// <summary>
	/// 打开药房时使用的门派子场景覆盖（如 "S125"）。正常在门派场景内打开药房时为 null，
	/// 由大地图"购买药材"选项设置，使 RefreshUI 能在 AllMaps 场景下渲染指定门派的库房。
	/// </summary>
	public static string OverrideShopScene;

	/// <summary>在大地图上打开指定门派的药房购买面板（灵石支付）。</summary>
	public static void OpenSectShop(string shopScene)
	{
		if ((UnityEngine.Object)UIMenPaiShop.Inst == (UnityEngine.Object)null)
		{
			Plugin.Log.LogError("[声望药房] UIMenPaiShop 实例不存在，无法打开药房面板");
			return;
		}
		OverrideShopScene = shopScene;
		UIMenPaiShop.Inst.Show();
		UIMenPaiShop.Inst.RefreshUI();
	}

	public static bool Prefix(UIMenPaiShop __instance)
	{
		if (!Plugin.Config_Enabled.Value)
		{
			_renderedShopKey = null;
			return true;
		}
		if (PlayerEx.GetNingZhouShengWangLevel() < Plugin.Config_MinShengWangLevel.Value)
		{
			// 走原版渲染（贡献货币）时清除缓存标识，避免下次达标后误复用原版面
			_renderedShopKey = null;
			return true;
		}
		try
		{
			RefreshUIWithLingShi(__instance);
		}
		catch (Exception e)
		{
			Plugin.Log.LogError("灵石药房渲染失败，回退原版逻辑：" + e);
			_renderedShopKey = null;
			return true;
		}
		return false;
	}

	/// <summary>
	/// 上次渲染的"场景 + 商品集合"标识。与当前一致且面板已有内容时跳过 UI 重建，
	/// 只刷新灵石数字（重建约 30 个商品对象（Instantiate + Destroy）是打开面板卡顿的主要来源；
	/// 药房商品固定、无库存概念，可安全复用）。
	/// </summary>
	private static string _renderedShopKey;

	private static void RefreshUIWithLingShi(UIMenPaiShop shop)
	{
		string sceneName = string.IsNullOrEmpty(OverrideShopScene) ? SceneEx.NowSceneName : OverrideShopScene;
		List<NomelShopJsonData> list = NomelShopJsonData.DataList.FindAll((NomelShopJsonData d) => $"S{d.threeScene}" == sceneName);
		if (list.Count == 0)
		{
			Debug.LogError((object)"声望药房：UIMenPaiShop刷新UI异常，此场景没有商品信息");
			return;
		}
		// 原版硬编码 3 页，这里防御数据不足 3 条的场景
		int pageCount = Mathf.Min(3, list.Count);
		List<List<jiaoHuanShopGoods>> pages = new List<List<jiaoHuanShopGoods>>(pageCount);
		StringBuilder keyBuilder = new StringBuilder(sceneName, 64);
		for (int i = 0; i < pageCount; i++)
		{
			List<jiaoHuanShopGoods> goods = UIDuiHuanShop.GetShopGoods(list[i].ExShopID);
			goods.Sort();
			pages.Add(goods);
			foreach (jiaoHuanShopGoods g in goods)
			{
				keyBuilder.Append('|').Append(g.GoodsID);
			}
		}
		string shopKey = keyBuilder.ToString();
		bool hasContent = false;
		for (int i = 0; i < pageCount; i++)
		{
			if (shop.ShopRT[i].childCount > 0)
			{
				hasContent = true;
				break;
			}
		}
		if (shopKey == _renderedShopKey && hasContent)
		{
			shop.MoneyText.text = GetPlayerMoneyInt().ToString();
			return;
		}
		shop.ShopTitle.text = list[0].Title;
		int levelType = PlayerEx.Player.getLevelType();
		ItemDatebase itemDb = jsonData.instance.GetComponent<ItemDatebase>();
		for (int i = 0; i < pageCount; i++)
		{
			NomelShopJsonData nomelShopJsonData = list[i];
			shop.ShopName[i].text = nomelShopJsonData.ChildTitle;
			List<jiaoHuanShopGoods> shopGoods = pages[i];
			((Transform)(object)shop.ShopRT[i]).DestoryAllChild();
			foreach (jiaoHuanShopGoods good in shopGoods)
			{
				_ItemJsonData item = _ItemJsonData.DataDict[good.GoodsID];
				if (nomelShopJsonData.SType == 1 && levelType < item.quality && (item.type == 3 || item.type == 4))
				{
					continue;
				}
				UIMenPaiShopItem component = UnityEngine.Object.Instantiate<GameObject>(shop.UIMenPaiShopItemPrefab, (Transform)(object)shop.ShopRT[i]).GetComponent<UIMenPaiShopItem>();
				int price = CalcLingShiPrice(item, good);
				component.PriceText.text = price.ToString();
				component.PriceIcon.sprite = itemDb.items[LingShiItemId].itemIconSprite;
				component.IconShow.SetItem(good.GoodsID);
				component.IconShow.Count = 1;
				int goodsId = good.GoodsID;
				component.IconShow.OnClick += (UnityAction<PointerEventData>)delegate
				{
					int maxBuy = Mathf.Min(GetPlayerMoneyInt() / price, item.maxNum);
					// 游戏获得物品是逐件（袋装药材还会逐袋拆开）处理，单次数量必须设上限，
					// 否则大量购买会卡死游戏
					maxBuy = Mathf.Min(maxBuy, Plugin.Config_MaxBuyPerAction.Value);
					switch (maxBuy)
					{
					case 0:
						UIPopTip.Inst.Pop("灵石不足");
						break;
					case 1:
						USelectBox.Show("是否购买" + item.name + " x1", (UnityAction)delegate
						{
							BuyItem(goodsId, price, 1);
						});
						break;
					default:
						// 注意：USelectNum 的标题用 Decs.Replace("{num}", 当前值) 更新，
						// 必须保留 {num} 占位符，否则拖动滑块时显示的数量不会变化
						USelectNum.Show("购买数量 x{num}", 1, maxBuy, (UnityAction<int>)delegate(int num)
						{
							BuyItem(goodsId, price, num);
						});
						break;
					}
				};
			}
		}
		shop.MoneyIcon.sprite = itemDb.items[LingShiItemId].itemIconSprite;
		shop.MoneyText.text = GetPlayerMoneyInt().ToString();
		_renderedShopKey = shopKey;
	}

	/// <summary>
	/// 灵石价 = 原版贡献价（ceil(item.price / percent)）× 100。
	/// percent &lt;= 0 时（配表异常的防御）直接按物品底价计。
	/// </summary>
	private static int CalcLingShiPrice(_ItemJsonData item, jiaoHuanShopGoods good)
	{
		if (good.percent <= 0)
		{
			return item.price;
		}
		int contributionPrice = item.price / good.percent;
		if (item.price % good.percent > 0)
		{
			contributionPrice++;
		}
		return contributionPrice * LingShiPerContribution;
	}

	private static void BuyItem(int goodsId, int price, int num)
	{
		// long 计算防用户调大 MaxBuyPerAction 后 price * num 溢出 int
		long need = (long)price * num;
		if (GetPlayerMoneyInt() < need)
		{
			UIPopTip.Inst.Pop("灵石不足");
			return;
		}
		PlayerEx.Player.AddMoney(-(int)need);
		PlayerEx.Player.addItem(goodsId, num, Tools.CreateItemSeid(goodsId));
		// 增量刷新：药房商品固定且无库存概念，购买后只需更新顶部灵石数字；
		// 不调用 UIMenPaiShop.Inst.RefreshUI()（会销毁重建整个面板，造成每次购买的卡顿感）
		if ((UnityEngine.Object)UIMenPaiShop.Inst != (UnityEngine.Object)null && (UnityEngine.Object)UIMenPaiShop.Inst.MoneyText != (UnityEngine.Object)null)
		{
			UIMenPaiShop.Inst.MoneyText.text = GetPlayerMoneyInt().ToString();
		}
		UIPopTip.Inst.Pop("购买了" + _ItemJsonData.DataDict[goodsId].name + "x" + num, PopTipIconType.包裹);
	}
}

/// <summary>药房面板关闭时清除场景覆盖，恢复正常按当前场景渲染。</summary>
[HarmonyPatch(typeof(UIMenPaiShop), "Close")]
public static class MenPaiShopClosePatch
{
	public static void Postfix()
	{
		MenPaiShopPatch.OverrideShopScene = null;
	}
}
